namespace DataVerification.API.Infrastructure;

/// <summary>
/// Reads and writes the refresh-token cookie. The token is stored httpOnly so JavaScript (and any
/// XSS payload) cannot read it, while the short-lived access token stays in the SPA's memory. The
/// cookie is scoped to the API path. The two realms use distinct names so the admin and applicant
/// apps never clobber each other's session when opened in the same browser.
/// </summary>
public static class RefreshTokenCookie
{
    public const string AdminName = "dv_admin_refresh";
    public const string ApplicantName = "dv_app_refresh";

    private const string Path = "/api/v1";

    public static void Set(HttpResponse response, string name, string token, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        var options = BuildOptions(response);
        options.Expires = expiresAt;
        response.Cookies.Append(name, token, options);
    }

    public static void Clear(HttpResponse response, string name)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(name, BuildOptions(response));
    }

    private static CookieOptions BuildOptions(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Production SPAs live on a different origin (Vercel) than the API (Railway). Browsers only
        // attach cookies on those cross-site credentialed fetches when SameSite=None and Secure.
        // The public edge is always HTTPS even when the container sees plain HTTP behind the proxy,
        // so in non-Development we force Secure + None. Localhost uses the Vite proxy (same-site)
        // over plain HTTP, where None is rejected, so Lax stays there.
        var env = response.HttpContext.RequestServices
            .GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        var forceCrossSite = env is not null && !env.IsDevelopment();
        var secure = forceCrossSite || response.HttpContext.Request.IsHttps;

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = secure ? SameSiteMode.None : SameSiteMode.Lax,
            Path = Path,
            IsEssential = true,
        };
    }
}
