using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataVerification.Application.Features.Orders.Commands;

/// <summary>
/// Creates an order from an email address alone and mails the generated credentials.
/// The password is returned to the caller only in <see cref="RegisterOrderResult.Password"/> when
/// the platform is running with credential echo enabled for local development.
/// </summary>
public sealed record RegisterOrderCommand(string Email, string LanguageCode) : IRequest<RegisterOrderResult>;

/// <param name="OrderNumber">Echoed only in development; production clients read it from the email.</param>
public sealed record RegisterOrderResult(string Email, string? OrderNumber, string? Password);

public sealed class RegisterOrderCommandValidator : AbstractValidator<RegisterOrderCommand>
{
    private static readonly string[] SupportedLanguages = ["ar", "en", "ru", "tr", "uz", "de", "hi", "zh", "ja", "pl"];

    public RegisterOrderCommandValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress();

        RuleFor(c => c.LanguageCode)
            .NotEmpty()
            .Must(code => SupportedLanguages.Contains(code, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Language must be one of: {string.Join(", ", SupportedLanguages)}.");
    }
}

public sealed class RegisterOrderCommandHandler
    : IRequestHandler<RegisterOrderCommand, RegisterOrderResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IOrderNumberGenerator _orderNumberGenerator;
    private readonly IEmailRouting _emailRouting;
    private readonly IPasswordGenerator _passwordGenerator;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly ISecretProtector _passwordProtector;
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<RegisterOrderCommandHandler> _logger;
    private readonly bool _echoCredentials;

    public RegisterOrderCommandHandler(
        IApplicationDbContext db,
        IOrderNumberGenerator orderNumberGenerator,
        IEmailRouting emailRouting,
        IPasswordGenerator passwordGenerator,
        IPasswordHashingService passwordHasher,
        ISecretProtector passwordProtector,
        IEmailSender emailSender,
        IEmailTemplateRenderer templateRenderer,
        IAuditLogger auditLogger,
        ILogger<RegisterOrderCommandHandler> logger,
        IConfiguration configuration)
    {
        _db = db;
        _orderNumberGenerator = orderNumberGenerator;
        _emailRouting = emailRouting;
        _passwordGenerator = passwordGenerator;
        _passwordHasher = passwordHasher;
        _passwordProtector = passwordProtector;
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _auditLogger = auditLogger;
        _logger = logger;
        _echoCredentials = configuration.GetValue<bool>("App:EchoCredentialsInResponse");
    }

    public async Task<RegisterOrderResult> Handle(
        RegisterOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Every order belongs to the single seeded client. Resolving it by code rather than by a
        // hardcoded id keeps the seeder free to assign its own identifier.
        var client = await _db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == Client.DefaultCode, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The default client '{Client.DefaultCode}' has not been seeded.");

        var orderNumber = await _orderNumberGenerator.GenerateUniqueAsync(client.Code, cancellationToken);
        var password = _passwordGenerator.Generate();

        var order = new Order
        {
            ClientId = client.Id,
            Email = request.Email.Trim(),
            OrderNumber = orderNumber,
            PasswordHash = _passwordHasher.Hash(password),
            // Recoverable copy for the back office. Null when no encryption key is configured,
            // which costs the reveal but never the registration.
            PasswordSecret = _passwordProtector.Protect(password),
            LanguageCode = request.LanguageCode.ToLowerInvariant(),
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var message = _templateRenderer.RenderOrderCredentials(
            order.Email,
            orderNumber,
            password,
            order.LanguageCode);

        // Who it comes from and who is copied is an administrator's setting, applied after the
        // renderer has decided what it says.
        message = await _emailRouting.ApplyAsync(EmailType.OrderCreated, message, cancellationToken);

        // Delivery failures must not lose the order that was just created — the applicant can
        // always ask for the credentials again.
        await _emailSender.SendAsync(message, cancellationToken);

        await _auditLogger.LogAsync(
            "Order.Registered",
            nameof(Order),
            order.Id,
            new { order.Email, order.LanguageCode },
            cancellationToken);

        _logger.LogInformation("Registered order {OrderNumber}.", orderNumber);

        return new RegisterOrderResult(
            order.Email,
            _echoCredentials ? orderNumber : null,
            _echoCredentials ? password : null);
    }
}
