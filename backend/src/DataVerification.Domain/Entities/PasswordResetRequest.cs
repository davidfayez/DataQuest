using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One press of "forgot password", and what came of it.
///
/// Recorded whether or not the order number was real: a run of requests against numbers that do not
/// exist is exactly the pattern worth seeing, and dropping those would hide it.
///
/// What can be recorded is what a browser and the connection reveal — the address the request came
/// from, the browser and operating system it announced, the language it asked for, and where that
/// address geolocates to. A web server cannot see the machine's name; nothing in HTTP carries it,
/// and no browser will tell a site what a computer is called.
/// </summary>
public sealed class PasswordResetRequest : Entity
{
    /// <summary>The order number as typed, kept even when it matched nothing.</summary>
    public required string OrderNumber { get; set; }

    /// <summary>Null when the number matched no order.</summary>
    public Guid? OrderId { get; set; }

    public Order? Order { get; set; }

    /// <summary>The mailbox the password went to, masked. Null when nothing was sent.</summary>
    public string? MaskedEmail { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>The browser's own description of itself, kept whole for anything parsing misses.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Read out of the user agent, so a reader does not have to.</summary>
    public string? Browser { get; set; }

    public string? OperatingSystem { get; set; }

    /// <summary><c>Desktop</c>, <c>Mobile</c> or <c>Tablet</c>, as far as the user agent says.</summary>
    public string? DeviceType { get; set; }

    /// <summary>Geolocated from the address. Null when lookup is off, failed, or the address is local.</summary>
    public string? Country { get; set; }

    public string? CountryCode { get; set; }

    public string? City { get; set; }

    public string? Continent { get; set; }

    public string? ContinentCode { get; set; }

    /// <summary>The region's code, such as <c>TAS</c>.</summary>
    public string? Region { get; set; }

    /// <summary>The region's name, such as <c>Tashkent</c>.</summary>
    public string? RegionName { get; set; }

    public string? District { get; set; }

    public string? PostalCode { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? TimeZone { get; set; }

    /// <summary>The zone's offset from UTC in seconds.</summary>
    public int? UtcOffsetSeconds { get; set; }

    public string? Currency { get; set; }

    /// <summary>Who sells the connection.</summary>
    public string? Isp { get; set; }

    /// <summary>Who the address is registered to, which is often not the ISP.</summary>
    public string? Organisation { get; set; }

    /// <summary>The autonomous system, such as <c>AS15169 Google LLC</c>.</summary>
    public string? AutonomousSystem { get; set; }

    /// <summary>
    /// The reverse-DNS name of the address. This is as close to a machine name as a web server can
    /// get, and it usually names the ISP's line rather than the visitor's own computer.
    /// </summary>
    public string? ReverseDns { get; set; }

    /// <summary>A mobile carrier's network.</summary>
    public bool? IsMobileNetwork { get; set; }

    /// <summary>A known proxy, VPN or Tor exit — the flag worth reading first on a suspect request.</summary>
    public bool? IsProxy { get; set; }

    /// <summary>A data centre rather than a home or office connection.</summary>
    public bool? IsHosting { get; set; }

    /// <summary>What the browser asked for, which is not always the order's own language.</summary>
    public string? AcceptLanguage { get; set; }

    /// <summary>The order's language, which is the one the email was written in.</summary>
    public string? LanguageCode { get; set; }

    /// <summary>How long the password offered by this request was good for.</summary>
    public int ValidityMinutes { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public PasswordResetOutcome Outcome { get; set; } = PasswordResetOutcome.Pending;

    /// <summary>When the applicant signed in with the emailed password, making it their real one.</summary>
    public DateTime? UsedAtUtc { get; set; }
}
