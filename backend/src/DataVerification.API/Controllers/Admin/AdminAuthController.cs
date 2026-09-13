using Asp.Versioning;
using DataVerification.API.Infrastructure;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Admin.Account;
using DataVerification.Application.Features.Admin.Auth;
using DataVerification.Application.Features.Orders.Commands;
using DataVerification.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers.Admin;

/// <summary>Back-office authentication.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/auth")]
[Produces("application/json")]
public sealed class AdminAuthController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IJwtTokenService _tokenService;

    public AdminAuthController(ISender sender, IJwtTokenService tokenService)
    {
        _sender = sender;
        _tokenService = tokenService;
    }

    /// <summary>Signs an admin in and returns a token carrying their resolved permissions.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(typeof(LoginAdminResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginAdminResult>> Login(
        [FromBody] LoginAdminRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _sender.Send(
            new LoginAdminCommand(request.Identifier, request.Password),
            cancellationToken);

        IssueRefreshCookie(result.AdminUserId);
        return Ok(result);
    }

    /// <summary>
    /// Silently re-issues an access token from the httpOnly refresh cookie, so a page reload keeps
    /// the admin signed in. Returns 401 (and clears the cookie) when the refresh token is missing,
    /// expired or belongs to a deactivated account.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginAdminResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginAdminResult>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie.AdminName, out var token)
            || _tokenService.ValidateRefreshToken(token)
                is not { Realm: DataVerificationRoles.Admin } info)
        {
            RefreshTokenCookie.Clear(Response, RefreshTokenCookie.AdminName);
            return Unauthorized();
        }

        LoginAdminResult result;
        try
        {
            result = await _sender.Send(new RefreshAdminSessionCommand(info.SubjectId), cancellationToken);
        }
        catch (InvalidCredentialsException)
        {
            RefreshTokenCookie.Clear(Response, RefreshTokenCookie.AdminName);
            return Unauthorized();
        }

        // Rotate the refresh token on every use so a captured one has a limited lifetime.
        IssueRefreshCookie(result.AdminUserId);
        return Ok(result);
    }

    /// <summary>Ends the session by clearing the refresh cookie. The in-memory access token is dropped client-side.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        RefreshTokenCookie.Clear(Response, RefreshTokenCookie.AdminName);
        return NoContent();
    }

    private void IssueRefreshCookie(Guid adminUserId)
    {
        var refresh = _tokenService.CreateRefreshToken(adminUserId, DataVerificationRoles.Admin);
        RefreshTokenCookie.Set(Response, RefreshTokenCookie.AdminName, refresh.Token, refresh.ExpiresAtUtc);
    }

    /// <summary>
    /// Starts a password reset. Always returns 202 whether or not the email matched an account, so
    /// it cannot be used to discover which addresses are registered.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _sender.Send(new RequestAdminPasswordResetCommand(request.Email), cancellationToken);
        return Accepted();
    }

    /// <summary>Completes a reset with the token from the email and a new password.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _sender.Send(
            new ResetAdminPasswordCommand(request.Token, request.NewPassword),
            cancellationToken);

        return NoContent();
    }
}

/// <summary>
/// Admin sign-in credentials. Send the handle as <c>usernameOrEmail</c>; the legacy
/// <c>email</c> field is still accepted so existing callers keep working.
/// </summary>
public sealed record LoginAdminRequest(string? UsernameOrEmail, string? Email, string Password)
{
    /// <summary>Whichever handle the caller supplied, preferring the current field name.</summary>
    public string Identifier =>
        string.IsNullOrWhiteSpace(UsernameOrEmail) ? Email ?? string.Empty : UsernameOrEmail;
}

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);
