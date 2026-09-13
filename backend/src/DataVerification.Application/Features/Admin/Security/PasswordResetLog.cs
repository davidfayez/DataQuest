using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Security;

/// <summary>
/// One press of "forgot password" as the admin panel shows it.
/// </summary>
/// <param name="Outcome">
/// <c>Pending</c>, <c>Used</c>, <c>Expired</c>, <c>Superseded</c> or <c>UnknownOrder</c> — sent by
/// name so the panel reads it rather than decoding a number.
/// </param>
public sealed record PasswordResetLogEntryDto(
    Guid Id,
    string OrderNumber,
    Guid? OrderId,
    string? MaskedEmail,
    string? IpAddress,
    string? Country,
    string? CountryCode,
    string? City,
    string? Continent,
    string? ContinentCode,
    string? Region,
    string? RegionName,
    string? District,
    string? PostalCode,
    double? Latitude,
    double? Longitude,
    string? TimeZone,
    int? UtcOffsetSeconds,
    string? Currency,
    string? Isp,
    string? Organisation,
    string? AutonomousSystem,
    string? ReverseDns,
    bool? IsMobileNetwork,
    bool? IsProxy,
    bool? IsHosting,
    string? Browser,
    string? OperatingSystem,
    string? DeviceType,
    string? UserAgent,
    string? AcceptLanguage,
    string? LanguageCode,
    int ValidityMinutes,
    DateTime? ExpiresAtUtc,
    DateTime? UsedAtUtc,
    string Outcome,
    DateTime RequestedAtUtc);

/// <param name="OnlyUnknownOrders">
/// Narrows to requests against order numbers that do not exist — the shape a guessing run leaves.
/// </param>
public sealed record ListPasswordResetsQuery : PagedQuery, IRequest<PagedResult<PasswordResetLogEntryDto>>
{
    public bool? OnlyUnknownOrders { get; init; }
}

/// <summary>How long an emailed password stays good for, and the bounds an operator may set.</summary>
public sealed record PasswordResetSettingsDto(int ValidityMinutes, int MinMinutes, int MaxMinutes);

public sealed record GetPasswordResetSettingsQuery : IRequest<PasswordResetSettingsDto>;

public sealed record UpdatePasswordResetSettingsCommand(int ValidityMinutes)
    : IRequest<PasswordResetSettingsDto>;

public sealed class UpdatePasswordResetSettingsCommandValidator
    : AbstractValidator<UpdatePasswordResetSettingsCommand>
{
    public UpdatePasswordResetSettingsCommandValidator()
    {
        // Under a minute the email cannot arrive in time; a week is long enough that an unused
        // password stops being a reset and becomes a second standing credential.
        RuleFor(c => c.ValidityMinutes)
            .InclusiveBetween(1, 10_080)
            .WithMessage("Choose between 1 minute and 7 days (10080 minutes).");
    }
}

public sealed class PasswordResetLogHandlers :
    IRequestHandler<ListPasswordResetsQuery, PagedResult<PasswordResetLogEntryDto>>,
    IRequestHandler<GetPasswordResetSettingsQuery, PasswordResetSettingsDto>,
    IRequestHandler<UpdatePasswordResetSettingsCommand, PasswordResetSettingsDto>
{
    private const int MinMinutes = 1;
    private const int MaxMinutes = 10_080;

    private readonly IApplicationDbContext _db;
    private readonly IPasswordResetSettingsStore _settings;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;

    public PasswordResetLogHandlers(
        IApplicationDbContext db,
        IPasswordResetSettingsStore settings,
        IAuditLogger auditLogger,
        IDateTimeProvider clock)
    {
        _db = db;
        _settings = settings;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public Task<PagedResult<PasswordResetLogEntryDto>> Handle(
        ListPasswordResetsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var query = _db.PasswordResetRequests.AsNoTracking();

        if (request.OnlyUnknownOrders is true)
        {
            query = query.Where(r => r.Outcome == PasswordResetOutcome.UnknownOrder);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            // What an investigation starts from: which order, which address, where it sits, and
            // whose network it is on.
            query = query.Where(r =>
                EF.Functions.Like(r.OrderNumber, $"%{term}%")
                || (r.IpAddress != null && EF.Functions.Like(r.IpAddress, $"%{term}%"))
                || (r.City != null && EF.Functions.Like(r.City, $"%{term}%"))
                || (r.Country != null && EF.Functions.Like(r.Country, $"%{term}%"))
                || (r.RegionName != null && EF.Functions.Like(r.RegionName, $"%{term}%"))
                || (r.Isp != null && EF.Functions.Like(r.Isp, $"%{term}%"))
                || (r.Organisation != null && EF.Functions.Like(r.Organisation, $"%{term}%"))
                || (r.AutonomousSystem != null && EF.Functions.Like(r.AutonomousSystem, $"%{term}%"))
                || (r.ReverseDns != null && EF.Functions.Like(r.ReverseDns, $"%{term}%")));
        }

        // Ordered and paged as rows, shaped into the DTO afterwards — ordering an already
        // projected queryable would make the projection itself the sort key.
        return query
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToPagedResultInSqlAsync(
                request,
                r => new PasswordResetLogEntryDto(
                r.Id,
                r.OrderNumber,
                r.OrderId,
                r.MaskedEmail,
                r.IpAddress,
                r.Country,
                r.CountryCode,
                r.City,
                r.Continent,
                r.ContinentCode,
                r.Region,
                r.RegionName,
                r.District,
                r.PostalCode,
                r.Latitude,
                r.Longitude,
                r.TimeZone,
                r.UtcOffsetSeconds,
                r.Currency,
                r.Isp,
                r.Organisation,
                r.AutonomousSystem,
                r.ReverseDns,
                r.IsMobileNetwork,
                r.IsProxy,
                r.IsHosting,
                r.Browser,
                r.OperatingSystem,
                r.DeviceType,
                r.UserAgent,
                r.AcceptLanguage,
                r.LanguageCode,
                r.ValidityMinutes,
                r.ExpiresAtUtc,
                r.UsedAtUtc,
                // An offer whose window has closed is reported as expired even though nothing has
                // rewritten the row: the passage of time is what expires it, not a background job.
                r.Outcome == PasswordResetOutcome.Pending
                    && r.ExpiresAtUtc != null
                    && r.ExpiresAtUtc <= now
                        ? "Expired"
                        : r.Outcome.ToString(),
                    r.CreatedAtUtc),
                cancellationToken,
                // The table's column names are not the row's: "when" is the row's creation time,
                // and "valid for" is the window it was issued with.
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["requestedAtUtc"] = nameof(PasswordResetRequest.CreatedAtUtc),
                    ["validity"] = nameof(PasswordResetRequest.ValidityMinutes),
                });
    }

    public async Task<PasswordResetSettingsDto> Handle(
        GetPasswordResetSettingsQuery request,
        CancellationToken cancellationToken) =>
        new(await _settings.GetValidityMinutesAsync(cancellationToken), MinMinutes, MaxMinutes);

    public async Task<PasswordResetSettingsDto> Handle(
        UpdatePasswordResetSettingsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _settings.SetValidityMinutesAsync(request.ValidityMinutes, cancellationToken);
        await _auditLogger.LogAsync(
            "PasswordResetSettings.Updated",
            "SiteSetting",
            null,
            new { request.ValidityMinutes },
            cancellationToken);

        return new PasswordResetSettingsDto(
            await _settings.GetValidityMinutesAsync(cancellationToken), MinMinutes, MaxMinutes);
    }
}
