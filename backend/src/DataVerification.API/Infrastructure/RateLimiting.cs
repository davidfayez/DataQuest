using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API;

public static class RateLimitPolicies
{
    /// <summary>Throttles order registration so the endpoint cannot be used to spam mailboxes.</summary>
    public const string Registration = "registration";

    /// <summary>Throttles sign-in attempts, complementing the per-account lockout.</summary>
    public const string Authentication = "authentication";

    /// <summary>Caps how fast files can be pushed at the platform, so uploads cannot be used to flood storage.</summary>
    public const string Uploads = "uploads";

    /// <summary>
    /// Throttles the AI answer endpoint. This is the one public endpoint whose every call spends
    /// somebody else's quota, so it is limited harder than the rest.
    /// </summary>
    public const string KnowledgeAi = "knowledge-ai";
}

/// <summary>
/// Bound from the <c>RateLimiting</c> configuration section. Limits are configurable so that
/// automated test runs — which legitimately register many orders from one address — can relax
/// them without the production defaults being weakened.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitWindow Registration { get; set; } = new() { PermitLimit = 5, WindowMinutes = 15 };

    public RateLimitWindow Authentication { get; set; } = new() { PermitLimit = 10, WindowMinutes = 5 };

    /// <summary>Generous enough for a full wizard, tight enough to stop a flood.</summary>
    public RateLimitWindow Uploads { get; set; } = new() { PermitLimit = 60, WindowMinutes = 5 };

    /// <summary>Enough to ask a few follow-up questions; nowhere near enough to drain a free tier.</summary>
    public RateLimitWindow KnowledgeAi { get; set; } = new() { PermitLimit = 10, WindowMinutes = 5 };
}

public sealed class RateLimitWindow
{
    public int PermitLimit { get; set; }

    public int WindowMinutes { get; set; }
}

public static class RateLimitingRegistration
{
    public static IServiceCollection AddDataVerificationRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(RateLimitingOptions.SectionName)
            .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Per-IP fixed windows. The account lockout in the login handler defends a single
            // order; these limits defend the endpoints themselves.
            limiter.AddPolicy(RateLimitPolicies.Registration, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(context),
                    _ => Build(options.Registration)));

            limiter.AddPolicy(RateLimitPolicies.Authentication, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(context),
                    _ => Build(options.Authentication)));

            limiter.AddPolicy(RateLimitPolicies.Uploads, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(context),
                    _ => Build(options.Uploads)));

            limiter.AddPolicy(RateLimitPolicies.KnowledgeAi, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(context),
                    _ => Build(options.KnowledgeAi)));
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions Build(RateLimitWindow window) => new()
    {
        PermitLimit = window.PermitLimit,
        Window = TimeSpan.FromMinutes(window.WindowMinutes),
        QueueLimit = 0,
    };

    private static string ResolvePartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
