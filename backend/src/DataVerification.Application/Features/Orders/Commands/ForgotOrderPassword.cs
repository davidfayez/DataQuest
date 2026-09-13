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
/// Issues a fresh password for an order and emails it to the address on file.
/// </summary>
/// <remarks>
/// A new password rather than a reset link, matching how an order is created in the first place:
/// the applicant never chooses a password, so there is nothing for a link to let them set. The
/// mailbox is the factor either way.
/// </remarks>
public sealed record ForgotOrderPasswordCommand(string OrderNumber)
    : IRequest<ForgotOrderPasswordResult>;

/// <param name="MaskedEmail">
/// The mailbox the password went to, masked — enough for the applicant to recognise which of their
/// addresses to open, and useless to anyone else. Null when no such order exists.
/// </param>
/// <param name="ValidityMinutes">
/// How long the emailed password is good for, so the page can say the same thing the email does.
/// </param>
/// <param name="Password">
/// Development only, and null everywhere else: the same escape hatch registration has, so the flow
/// can be exercised without reading a mailbox. Gated on <c>App:EchoCredentialsInResponse</c>.
/// </param>
public sealed record ForgotOrderPasswordResult(
    string? MaskedEmail,
    int ValidityMinutes,
    string? Password = null);

public sealed class ForgotOrderPasswordCommandValidator
    : AbstractValidator<ForgotOrderPasswordCommand>
{
    public ForgotOrderPasswordCommandValidator()
    {
        RuleFor(c => c.OrderNumber)
            .NotEmpty().WithMessage("Enter your order number.")
            .MaximumLength(29);
    }
}

public sealed class ForgotOrderPasswordCommandHandler
    : IRequestHandler<ForgotOrderPasswordCommand, ForgotOrderPasswordResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordGenerator _passwordGenerator;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly ISecretProtector _passwordProtector;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IEmailSender _emailSender;
    private readonly IEmailRouting _emailRouting;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUser _currentUser;
    private readonly IIpGeolocator _geolocator;
    private readonly IUserAgentReader _userAgentReader;
    private readonly IPasswordResetSettingsStore _settings;
    private readonly IDateTimeProvider _clock;
    private readonly bool _echoCredentials;
    private readonly ILogger<ForgotOrderPasswordCommandHandler> _logger;

    public ForgotOrderPasswordCommandHandler(
        IApplicationDbContext db,
        IPasswordGenerator passwordGenerator,
        IPasswordHashingService passwordHasher,
        ISecretProtector passwordProtector,
        IEmailTemplateRenderer templateRenderer,
        IEmailSender emailSender,
        IEmailRouting emailRouting,
        IAuditLogger auditLogger,
        ICurrentUser currentUser,
        IIpGeolocator geolocator,
        IUserAgentReader userAgentReader,
        IPasswordResetSettingsStore settings,
        IDateTimeProvider clock,
        IConfiguration configuration,
        ILogger<ForgotOrderPasswordCommandHandler> logger)
    {
        _db = db;
        _passwordGenerator = passwordGenerator;
        _passwordHasher = passwordHasher;
        _passwordProtector = passwordProtector;
        _templateRenderer = templateRenderer;
        _emailSender = emailSender;
        _emailRouting = emailRouting;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _geolocator = geolocator;
        _userAgentReader = userAgentReader;
        _settings = settings;
        _clock = clock;
        _echoCredentials = configuration.GetValue<bool>("App:EchoCredentialsInResponse");
        _logger = logger;
    }

    public async Task<ForgotOrderPasswordResult> Handle(
        ForgotOrderPasswordCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderNumber = request.OrderNumber.Trim();
        var now = _clock.UtcNow;
        var validityMinutes = await _settings.GetValidityMinutesAsync(cancellationToken);

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, cancellationToken);

        // Recorded whether or not the number was real. A run of requests against numbers that do
        // not exist is the pattern most worth seeing, and it is invisible if only hits are logged.
        var log = await BuildLogAsync(orderNumber, order, validityMinutes, now, cancellationToken);
        _db.PasswordResetRequests.Add(log);

        // No such order: answered the same way as a real one, minus the mask. Telling a stranger
        // which order numbers exist is not worth the marginally friendlier error.
        if (order is null)
        {
            log.Outcome = PasswordResetOutcome.UnknownOrder;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Password reset requested for unknown order {OrderNumber}.", orderNumber);
            return new ForgotOrderPasswordResult(null, validityMinutes);
        }

        // An earlier offer still outstanding is retired, so the log shows one live offer per order
        // and an operator can see that the applicant asked twice.
        await SupersedePreviousAsync(order.Id, log.Id, cancellationToken);

        var password = _passwordGenerator.Generate();

        // Offered, not applied. The password the applicant already has keeps working until this one
        // is used, so a reset somebody else asked for cannot lock them out — and the lockout is
        // deliberately left alone here, or requesting a reset would be a way to clear one.
        order.OfferPassword(
            _passwordHasher.Hash(password),
            _passwordProtector.Protect(password),
            now.AddMinutes(validityMinutes),
            now);

        log.MaskedEmail = EmailMask.Apply(order.Email);
        log.ExpiresAtUtc = order.PendingPasswordExpiresAtUtc;

        await _db.SaveChangesAsync(cancellationToken);

        var message = _templateRenderer.RenderOrderPasswordReset(
            order.Email,
            order.OrderNumber,
            password,
            order.LanguageCode,
            validityMinutes);

        message = await _emailRouting.ApplyAsync(
            EmailType.ForgotPassword, message, cancellationToken);

        // The password is already committed, so a delivery failure must not roll it back — the
        // sender swallows those, and the applicant can ask again.
        await _emailSender.SendAsync(message, cancellationToken);

        await _auditLogger.LogAsync(
            "Order.PasswordReset",
            nameof(Order),
            order.Id,
            new { order.OrderNumber, ValidityMinutes = validityMinutes },
            cancellationToken);

        _logger.LogInformation(
            "Offered a new password for order {OrderNumber}, good for {Minutes} minutes.",
            order.OrderNumber,
            validityMinutes);

        return new ForgotOrderPasswordResult(
            EmailMask.Apply(order.Email),
            validityMinutes,
            _echoCredentials ? password : null);
    }

    /// <summary>
    /// Everything the request itself reveals: the address it came from, what the browser said about
    /// itself, the language it asked for, and where that address geolocates to.
    ///
    /// Not the machine's name — nothing in HTTP carries it and no browser will tell a site what a
    /// computer is called. The closest available answers are the browser, operating system and
    /// device type read out of the user agent, and the address's reverse-DNS name, which usually
    /// names the ISP's line rather than the visitor's own computer.
    /// </summary>
    private async Task<PasswordResetRequest> BuildLogAsync(
        string orderNumber,
        Order? order,
        int validityMinutes,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var ip = _currentUser.IpAddress;
        var userAgent = _currentUser.UserAgent;
        var agent = _userAgentReader.Read(userAgent);

        // Best-effort, and never allowed to fail the reset: a geolocation service that is slow or
        // down costs this record a city, not the applicant their password.
        var location = await _geolocator.LocateAsync(ip, cancellationToken);

        return new PasswordResetRequest
        {
            OrderNumber = orderNumber,
            OrderId = order?.Id,
            IpAddress = ip,
            UserAgent = userAgent,
            Browser = agent.Browser,
            OperatingSystem = agent.OperatingSystem,
            DeviceType = agent.DeviceType,
            Country = location?.Country,
            CountryCode = location?.CountryCode,
            City = location?.City,
            Continent = location?.Continent,
            ContinentCode = location?.ContinentCode,
            Region = location?.Region,
            RegionName = location?.RegionName,
            District = location?.District,
            PostalCode = location?.PostalCode,
            Latitude = location?.Latitude,
            Longitude = location?.Longitude,
            TimeZone = location?.TimeZone,
            UtcOffsetSeconds = location?.UtcOffsetSeconds,
            Currency = location?.Currency,
            Isp = location?.Isp,
            Organisation = location?.Organisation,
            AutonomousSystem = location?.AutonomousSystem,
            ReverseDns = location?.ReverseDns,
            IsMobileNetwork = location?.IsMobileNetwork,
            IsProxy = location?.IsProxy,
            IsHosting = location?.IsHosting,
            AcceptLanguage = _currentUser.AcceptLanguage,
            LanguageCode = order?.LanguageCode,
            ValidityMinutes = validityMinutes,
            CreatedAtUtc = now,
        };
    }

    /// <summary>Retires any offer this one replaces, so only the newest is ever outstanding.</summary>
    private async Task SupersedePreviousAsync(
        Guid orderId,
        Guid currentLogId,
        CancellationToken cancellationToken)
    {
        var previous = await _db.PasswordResetRequests
            .Where(r => r.OrderId == orderId
                        && r.Id != currentLogId
                        && r.Outcome == PasswordResetOutcome.Pending)
            .ToListAsync(cancellationToken);

        foreach (var row in previous) row.Outcome = PasswordResetOutcome.Superseded;
    }
}

/// <summary>
/// Masks an email down to a recognisable shape: <c>da*****93@**mail.com</c>.
///
/// Each run of stars is a fixed length rather than the real one, so the mask says nothing about how
/// long the hidden part is. The top-level domain is kept whole, since it is the part that tells
/// someone which of their mailboxes this is.
/// </summary>
public static class EmailMask
{
    private const string LocalStars = "*****";

    /// <summary>Shorter than the local part's run, so the two halves stay easy to tell apart.</summary>
    private const string DomainStars = "**";

    /// <summary>How much of the domain's tail survives — enough to recognise, not enough to name.</summary>
    private const int DomainTail = 4;

    public static string Apply(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1)
        {
            // Not an address shape we recognise; mask the lot rather than guess at its parts.
            return LocalStars;
        }

        var local = email[..at];
        var domain = email[(at + 1)..];

        var dot = domain.LastIndexOf('.');
        if (dot <= 0)
        {
            return $"{MaskLocal(local)}@{MaskDomain(domain)}";
        }

        return $"{MaskLocal(local)}@{MaskDomain(domain[..dot])}{domain[dot..]}";
    }

    /// <summary>
    /// Keeps the first and last two characters. Anything too short to show four without showing
    /// all of it keeps less, so a short mailbox is not handed over whole — and a single-character
    /// one keeps nothing at all, since its first character is the whole of it.
    /// </summary>
    private static string MaskLocal(string part) => part.Length switch
    {
        <= 1 => LocalStars,
        2 => $"{part[0]}{LocalStars}",
        <= 4 => $"{part[0]}{LocalStars}{part[^1]}",
        _ => $"{part[..2]}{LocalStars}{part[^2..]}",
    };

    /// <summary>
    /// Hides the front of the domain and keeps its tail: <c>gmail</c> becomes <c>**mail</c>.
    ///
    /// At least one character is always hidden, so a domain shorter than the tail is shortened
    /// rather than shown whole — <c>aol.com</c> masks to <c>**ol.com</c>, not <c>**aol.com</c>.
    /// </summary>
    private static string MaskDomain(string label)
    {
        if (label.Length == 0)
        {
            return DomainStars;
        }

        var visible = Math.Min(DomainTail, label.Length - 1);
        return $"{DomainStars}{label[^visible..]}";
    }
}
