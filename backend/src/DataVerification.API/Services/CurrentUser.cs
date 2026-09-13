using System.Globalization;
using System.Security.Claims;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;
using DataVerification.Infrastructure.Identity;

namespace DataVerification.API.Services;

/// <summary>
/// Resolves the caller from the JWT on the current request. This is the single place applicant
/// scoping originates: every applicant query filters on <see cref="OrderId"/>, so a token can
/// never be used to read another order's data.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private static readonly string[] SupportedLanguages =
        ["ar", "en", "ru", "tr", "uz", "de", "hi", "zh", "ja", "pl"];

    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? OrderId => ReadGuid(DataVerificationClaims.OrderId);

    public Guid? AdminUserId => IsInRole(DataVerificationRoles.Admin)
        ? ReadGuid(ClaimTypes.NameIdentifier) ?? ReadGuid("sub")
        : null;

    public string? DisplayName => Principal?.FindFirstValue(ClaimTypes.Name)
        ?? Principal?.FindFirstValue(DataVerificationClaims.OrderNumber);

    public IReadOnlySet<string> Permissions => Principal?.Claims
        .Where(c => c.Type == DataVerificationClaims.Permission)
        .Select(c => c.Value)
        .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Negotiated from <c>Accept-Language</c> first so a signed-in applicant can read the UI in a
    /// different language than the one they registered with, falling back to the token's locale.
    /// </summary>
    public string LanguageCode
    {
        get
        {
            var header = _accessor.HttpContext?.Request.Headers.AcceptLanguage.ToString();
            var fromHeader = ParseAcceptLanguage(header);
            if (fromHeader is not null)
            {
                return fromHeader;
            }

            var fromToken = Principal?.FindFirstValue(DataVerificationClaims.LanguageCode);
            return Normalize(fromToken) ?? "en";
        }
    }

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => Header("User-Agent");

    public string? AcceptLanguage => Header("Accept-Language");

    /// <summary>Header values are attacker-controlled, so they are truncated before being stored.</summary>
    private string? Header(string name)
    {
        var value = _accessor.HttpContext?.Request.Headers[name].ToString();
        if (string.IsNullOrWhiteSpace(value)) return null;

        return value.Length <= 512 ? value : value[..512];
    }

    public Actor ToActor()
    {
        if (AdminUserId is { } adminId)
        {
            return Actor.Admin(adminId, DisplayName);
        }

        if (OrderId is { } orderId)
        {
            return Actor.Applicant(orderId, DisplayName);
        }

        return Actor.System();
    }

    private bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(Principal?.FindFirstValue(claimType), out var value) ? value : null;

    /// <summary>Picks the highest-weighted supported language from an Accept-Language header.</summary>
    private static string? ParseAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var ranked = header
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                var segments = part.Split(';', StringSplitOptions.TrimEntries);
                var quality = 1.0d;

                if (segments.Length > 1 && segments[1].StartsWith("q=", StringComparison.OrdinalIgnoreCase))
                {
                    _ = double.TryParse(
                        segments[1][2..],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out quality);
                }

                return (Language: Normalize(segments[0]), Quality: quality);
            })
            .Where(entry => entry.Language is not null)
            .OrderByDescending(entry => entry.Quality);

        return ranked.FirstOrDefault().Language;
    }

    /// <summary>Reduces a tag such as <c>ar-EG</c> to a supported two-letter code.</summary>
    private static string? Normalize(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return null;
        }

        var primary = languageTag.Split('-')[0].ToLowerInvariant();
        return SupportedLanguages.Contains(primary) ? primary : null;
    }
}
