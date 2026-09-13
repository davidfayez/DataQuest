using DataVerification.Domain.Authorization;
using DataVerification.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace DataVerification.API.Authorization;

/// <summary>Requires a single named permission on the caller's token.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission) => Permission = permission;

    public string Permission { get; }
}

/// <summary>
/// Grants access when the admin's token carries the required permission claim. Permissions are
/// embedded at login, so this check costs no database round trip.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var hasPermission = context.User.Claims.Any(claim =>
            claim.Type == DataVerificationClaims.Permission
            && string.Equals(claim.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase));

        if (hasPermission)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Shorthand for <c>[Authorize(Policy = "...")]</c> on admin endpoints.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission) => Policy = permission;
}

public static class AuthorizationRegistration
{
    /// <summary>
    /// Registers one policy per permission constant. Doing this from the catalogue rather than by
    /// hand means a new permission cannot be referenced without also existing.
    /// </summary>
    public static IServiceCollection AddPermissionPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            // Fail closed. Without a fallback policy, an endpoint added without [Authorize] is
            // silently public; with one, a new endpoint is private until someone deliberately
            // marks it [AllowAnonymous].
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(PolicyNames.Applicant, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(DataVerificationRoles.Applicant));

            options.AddPolicy(PolicyNames.Admin, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(DataVerificationRoles.Admin));

            foreach (var permission in Permissions.Names)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(DataVerificationRoles.Admin)
                    .AddRequirements(new PermissionRequirement(permission)));
            }
        });

        return services;
    }
}

public static class PolicyNames
{
    public const string Applicant = "ApplicantOnly";
    public const string Admin = "AdminOnly";
}
