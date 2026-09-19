using Microsoft.AspNetCore.Mvc.Filters;

namespace DataVerification.API.Infrastructure;

/// <summary>
/// Lets public artwork be drawn by the sites, which live on origins of their own.
///
/// Every response carries <c>Cross-Origin-Resource-Policy: same-origin</c> (see
/// <see cref="SecurityHeadersMiddleware"/>), which is right for uploads streamed back to their owner.
/// But the applicant site and the admin panel are served from different origins than the API, and a
/// browser refuses to paint an <c>&lt;img&gt;</c> whose response says same-origin — so a logo uploaded
/// in Settings drew as a broken image on both sites. Only anonymous, public images opt out; anything
/// behind a session is fetched with a token and keeps the stricter policy.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EmbeddableResourceAttribute : ResultFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Runs after the middleware has written its default, and before the body starts.
        context.HttpContext.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        base.OnResultExecuting(context);
    }
}
