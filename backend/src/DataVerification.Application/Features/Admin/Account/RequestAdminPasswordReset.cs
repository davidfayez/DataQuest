using System.Security.Cryptography;
using System.Text;
using DataVerification.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataVerification.Application.Features.Admin.Account;

/// <summary>
/// Starts the self-service reset: issues a one-time token, stores only its hash, and emails the
/// administrator a link. The response is always the same whether or not the email matched an
/// account, so the endpoint cannot be used to discover which addresses are registered.
/// </summary>
public sealed record RequestAdminPasswordResetCommand(string Email) : IRequest;

public sealed class RequestAdminPasswordResetCommandValidator
    : AbstractValidator<RequestAdminPasswordResetCommand>
{
    public RequestAdminPasswordResetCommandValidator() =>
        RuleFor(c => c.Email).NotEmpty().MaximumLength(320).EmailAddress();
}

public sealed class RequestAdminPasswordResetCommandHandler
    : IRequestHandler<RequestAdminPasswordResetCommand>
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    private readonly IApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _templates;
    private readonly IDateTimeProvider _clock;
    private readonly string _adminUrl;
    private readonly ILogger<RequestAdminPasswordResetCommandHandler> _logger;

    public RequestAdminPasswordResetCommandHandler(
        IApplicationDbContext db,
        IEmailSender emailSender,
        IEmailTemplateRenderer templates,
        IDateTimeProvider clock,
        IConfiguration configuration,
        ILogger<RequestAdminPasswordResetCommandHandler> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _templates = templates;
        _clock = clock;
        _adminUrl = configuration["App:AdminUrl"] ?? "http://localhost:5174";
        _logger = logger;
    }

    public async Task Handle(
        RequestAdminPasswordResetCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim();
        var admin = await _db.AdminUsers
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);

        // Silent no-op for an unknown or inactive address: the caller gets the same response as a
        // hit, so a stranger cannot enumerate accounts by watching for a different reply.
        if (admin is null)
        {
            _logger.LogInformation("Password reset requested for unknown admin email {Email}.", email);
            return;
        }

        // The raw token goes only into the email; the database keeps its hash. A database read
        // therefore cannot be turned into a working link.
        var rawToken = GenerateToken();
        var tokenHash = Hash(rawToken);

        admin.BeginPasswordReset(tokenHash, _clock.UtcNow.Add(TokenLifetime));
        await _db.SaveChangesAsync(cancellationToken);

        var resetUrl = BuildResetUrl(rawToken);
        var message = _templates.RenderAdminPasswordReset(
            admin.Email,
            admin.FullName,
            resetUrl,
            admin.LanguageCode);

        await _emailSender.SendAsync(message, cancellationToken);
    }

    private string BuildResetUrl(string rawToken) =>
        $"{_adminUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";

    /// <summary>A URL-safe, high-entropy token — 256 bits, so guessing is not a threat.</summary>
    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    internal static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
