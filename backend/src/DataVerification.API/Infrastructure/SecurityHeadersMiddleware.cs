namespace DataVerification.API.Infrastructure;

/// <summary>
/// Adds the response headers a JSON API should always send.
///
/// This is an API consumed by two SPAs, so there is no HTML to protect with a content policy —
/// the value here is in stopping content-type sniffing, keeping responses out of frames, and not
/// leaking the full request URL (which carries order and application ids) in the Referer header
/// of any outbound navigation.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        // Nothing here needs a camera, microphone or geolocation.
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        // Uploaded files are streamed back to their owner; forbid the browser from ever treating
        // one as an active document in this origin.
        headers["Cross-Origin-Resource-Policy"] = "same-origin";

        return _next(context);
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
