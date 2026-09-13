using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataVerification.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataVerification.Infrastructure.Services;

/// <summary>Bound from the <c>Geolocation</c> configuration section.</summary>
public sealed class GeolocationOptions
{
    public const string SectionName = "Geolocation";

    /// <summary>
    /// Turn this off to stop sending visitor addresses to a third party. The security log then
    /// records everything else and leaves country and city empty.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where to ask. <c>{ip}</c> is replaced with the address.
    ///
    /// ip-api.com's free endpoint needs no key, which is why it is the default — but its free tier
    /// is licensed for non-commercial use only, so a commercial deployment needs either their paid
    /// endpoint (with the key in the URL) or a different provider here.
    ///
    /// Every field the free tier offers is asked for, <c>reverse</c> included. That one costs a
    /// reverse-DNS lookup and so is the slowest part of the call, but it is the only thing at this
    /// distance that resembles a machine name, which is worth the wait in a security log.
    /// </summary>
    public string Endpoint { get; set; } =
        "http://ip-api.com/json/{ip}?fields=status,message,continent,continentCode,country,"
        + "countryCode,region,regionName,city,district,zip,lat,lon,timezone,offset,currency,"
        + "isp,org,as,reverse,mobile,proxy,hosting,query";

    /// <summary>A slow provider must not hold up a password reset.</summary>
    public int TimeoutSeconds { get; set; } = 4;
}

/// <summary>
/// Looks an address up with an HTTP geolocation service, and remembers the answer for a while.
///
/// Every failure is swallowed: a private or malformed address, a provider that is down, over quota
/// or slow, all end the same way — no location, and the caller carries on. The log is worth less
/// without a city; it is worth nothing if the reset it belongs to failed.
/// </summary>
public sealed class HttpIpGeolocator : IIpGeolocator
{
    /// <summary>Addresses repeat within a session; the same lookup twice is a wasted round trip.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly GeolocationOptions _options;
    private readonly ILogger<HttpIpGeolocator> _logger;

    public HttpIpGeolocator(
        HttpClient http,
        IMemoryCache cache,
        IOptions<GeolocationOptions> options,
        ILogger<HttpIpGeolocator> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _cache = cache;
        _logger = logger;
    }

    public async Task<IpLocation?> LocateAsync(
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !IsLocatable(ipAddress)) return null;

        if (_cache.TryGetValue($"geo:{ipAddress}", out IpLocation? cached)) return cached;

        try
        {
            var url = _options.Endpoint.Replace("{ip}", Uri.EscapeDataString(ipAddress!), StringComparison.Ordinal);
            var payload = await _http.GetFromJsonAsync<JsonElement>(url, cancellationToken);

            var location = Read(payload);

            // Cached either way: a provider that says "no" about an address will keep saying it.
            _cache.Set($"geo:{ipAddress}", location, CacheFor);
            return location;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "Could not geolocate {Ip}.", ipAddress);
            return null;
        }
    }

    /// <summary>
    /// Only addresses that could belong to somewhere. A loopback or private address is the server's
    /// own network — asking about it wastes a call and returns nothing useful.
    /// </summary>
    private static bool IsLocatable(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return false;
        if (!IPAddress.TryParse(ipAddress, out var parsed)) return false;

        if (IPAddress.IsLoopback(parsed)) return false;

        var bytes = parsed.GetAddressBytes();

        return parsed.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => !(
                bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)),
            // Unique-local (fc00::/7) and link-local (fe80::/10).
            System.Net.Sockets.AddressFamily.InterNetworkV6 => !(
                (bytes[0] & 0xFE) == 0xFC
                || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)),
            _ => false,
        };
    }

    private static IpLocation? Read(JsonElement payload)
    {
        // ip-api reports its own failures in the body with a 200, so the status field decides.
        if (payload.TryGetProperty("status", out var status)
            && !string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var location = new IpLocation
        {
            Country = Text(payload, "country"),
            CountryCode = Text(payload, "countryCode"),
            Continent = Text(payload, "continent"),
            ContinentCode = Text(payload, "continentCode"),
            Region = Text(payload, "region"),
            RegionName = Text(payload, "regionName"),
            City = Text(payload, "city"),
            District = Text(payload, "district"),
            PostalCode = Text(payload, "zip"),
            Latitude = Number(payload, "lat"),
            Longitude = Number(payload, "lon"),
            TimeZone = Text(payload, "timezone"),
            UtcOffsetSeconds = Integer(payload, "offset"),
            Currency = Text(payload, "currency"),
            Isp = Text(payload, "isp"),
            Organisation = Text(payload, "org"),
            AutonomousSystem = Text(payload, "as"),
            ReverseDns = Text(payload, "reverse"),
            IsMobileNetwork = Flag(payload, "mobile"),
            IsProxy = Flag(payload, "proxy"),
            IsHosting = Flag(payload, "hosting"),
        };

        // A payload that said "success" but carried nothing usable is no better than no answer.
        return location == new IpLocation() ? null : location;
    }

    /// <summary>
    /// Empty strings are treated as absent. ip-api returns <c>""</c> rather than omitting a field
    /// it has no value for, and an empty city in the log should read as unknown, not as blank.
    /// </summary>
    private static string? Text(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static double? Number(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var parsed)
            ? parsed
            : null;

    private static int? Integer(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static bool? Flag(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}

/// <summary>
/// Reads the three things a reader of the security log actually wants out of a user agent.
///
/// Deliberately a handful of patterns rather than a parsing library. A user agent is a string a
/// client can put anything in, this is for a human reading a log rather than for analytics, and
/// "Unknown" against an odd one is a perfectly good answer — the raw string is stored beside it for
/// anything this misses.
/// </summary>
public sealed partial class UserAgentReader : IUserAgentReader
{
    public UserAgentDetails Read(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return new UserAgentDetails(null, null, null);

        return new UserAgentDetails(
            ReadBrowser(userAgent),
            ReadOperatingSystem(userAgent),
            ReadDeviceType(userAgent));
    }

    /// <summary>
    /// Order matters. Every Chromium browser says "Chrome", Edge and Opera say it too, and Safari
    /// appears in all of their strings — so the most specific claim is checked first.
    /// </summary>
    private static string ReadBrowser(string ua)
    {
        if (Contains(ua, "Edg/") || Contains(ua, "Edge/")) return $"Edge {Version(ua, "Edg[e]?/")}";
        if (Contains(ua, "OPR/") || Contains(ua, "Opera")) return $"Opera {Version(ua, "OPR/")}";
        if (Contains(ua, "SamsungBrowser")) return $"Samsung Internet {Version(ua, "SamsungBrowser/")}";
        if (Contains(ua, "Firefox/")) return $"Firefox {Version(ua, "Firefox/")}";
        if (Contains(ua, "Chrome/")) return $"Chrome {Version(ua, "Chrome/")}";
        if (Contains(ua, "Safari/")) return $"Safari {Version(ua, "Version/")}";

        return "Unknown";
    }

    private static string ReadOperatingSystem(string ua)
    {
        if (Contains(ua, "Windows NT 10.0")) return "Windows 10 or 11";
        if (Contains(ua, "Windows NT")) return "Windows";
        if (Contains(ua, "iPhone") || Contains(ua, "iPad")) return "iOS";
        if (Contains(ua, "Android")) return $"Android {Version(ua, "Android ")}";
        if (Contains(ua, "Mac OS X")) return "macOS";
        if (Contains(ua, "CrOS")) return "ChromeOS";
        if (Contains(ua, "Linux")) return "Linux";

        return "Unknown";
    }

    private static string ReadDeviceType(string ua)
    {
        if (Contains(ua, "iPad") || (Contains(ua, "Android") && !Contains(ua, "Mobile"))) return "Tablet";
        if (Contains(ua, "Mobi") || Contains(ua, "iPhone")) return "Mobile";

        return "Desktop";
    }

    private static bool Contains(string ua, string token) =>
        ua.Contains(token, StringComparison.OrdinalIgnoreCase);

    /// <summary>The major version after a token, or empty when it is not there in a shape we read.</summary>
    private static string Version(string ua, string token)
    {
        var match = Regex.Match(ua, $"{token}([0-9]+)", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
