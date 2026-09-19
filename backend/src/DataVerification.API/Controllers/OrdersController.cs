using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.API.Infrastructure;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Orders.Commands;
using DataVerification.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers;

/// <summary>Applicant onboarding: register with an email, sign in, and complete order setup.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Produces("application/json")]
public sealed class OrdersController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IJwtTokenService _tokenService;

    public OrdersController(ISender sender, IJwtTokenService tokenService)
    {
        _sender = sender;
        _tokenService = tokenService;
    }

    /// <summary>Creates an order and emails the generated order number and password.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Registration)]
    [ProducesResponseType(typeof(RegisterOrderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RegisterOrderResult>> Register(
        [FromBody] RegisterOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _sender.Send(
            new RegisterOrderCommand(request.Email, request.LanguageCode),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Issues a new password for an order and emails it to the address on file.
    /// </summary>
    /// <remarks>
    /// Always 200, whether or not the order exists — the masked address comes back only when it
    /// does, so a caller learns nothing about order numbers they do not already hold. Rate-limited
    /// on the authentication policy, since it is a credential-issuing endpoint.
    /// </remarks>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(typeof(ForgotOrderPasswordResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ForgotOrderPasswordResult>> ForgotPassword(
        [FromBody] ForgotOrderPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new ForgotOrderPasswordCommand(request.OrderNumber),
            cancellationToken));
    }

    /// <summary>Exchanges the order number and password for an order-scoped JWT.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(typeof(LoginOrderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginOrderResult>> Login(
        [FromBody] LoginOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _sender.Send(
            new LoginOrderCommand(request.OrderNumber, request.Password),
            cancellationToken);

        IssueRefreshCookie(result.OrderId);
        return Ok(result);
    }

    /// <summary>
    /// Silently re-issues an access token from the httpOnly refresh cookie, so a page reload keeps
    /// the applicant signed in. Returns 401 (and clears the cookie) when the refresh token is
    /// missing, expired or its order no longer exists.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginOrderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginOrderResult>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie.ApplicantName, out var token)
            || _tokenService.ValidateRefreshToken(token)
                is not { Realm: DataVerificationRoles.Applicant } info)
        {
            RefreshTokenCookie.Clear(Response, RefreshTokenCookie.ApplicantName);
            return Unauthorized();
        }

        LoginOrderResult result;
        try
        {
            result = await _sender.Send(new RefreshOrderSessionCommand(info.SubjectId), cancellationToken);
        }
        catch (InvalidCredentialsException)
        {
            RefreshTokenCookie.Clear(Response, RefreshTokenCookie.ApplicantName);
            return Unauthorized();
        }

        // Rotate the refresh token on every use so a captured one has a limited lifetime.
        IssueRefreshCookie(result.OrderId);
        return Ok(result);
    }

    /// <summary>Ends the session by clearing the refresh cookie.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        RefreshTokenCookie.Clear(Response, RefreshTokenCookie.ApplicantName);
        return NoContent();
    }

    private void IssueRefreshCookie(Guid orderId)
    {
        var refresh = _tokenService.CreateRefreshToken(orderId, DataVerificationRoles.Applicant);
        RefreshTokenCookie.Set(Response, RefreshTokenCookie.ApplicantName, refresh.Token, refresh.ExpiresAtUtc);
    }

    /// <summary>
    /// Locks the order's verification country and wallet currency, records the contact person,
    /// and opens the wallet.
    /// </summary>
    [HttpPut("setup")]
    [Authorize(Policy = PolicyNames.Applicant)]
    [ProducesResponseType(typeof(SetupOrderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SetupOrderResult>> Setup(
        [FromBody] SetupOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _sender.Send(
            new SetupOrderCommand(
                request.VerificationCountryId,
                request.CurrencyId,
                request.ContactPersonName,
                request.ContactPersonPhoneCountry,
                request.ContactPersonPhoneCode,
                request.ContactPersonPhoneNumber),
            cancellationToken);

        return Ok(result);
    }
}

/// <param name="LanguageCode">One of ar, en, ru, ur, de, hi, zh — decides the email's language.</param>
public sealed record ForgotOrderPasswordRequest(string OrderNumber);

public sealed record RegisterOrderRequest(string Email, string LanguageCode);

public sealed record LoginOrderRequest(string OrderNumber, string Password);

/// <param name="ContactPersonPhoneCountry">ISO 3166-1 alpha-2 code of the dial code chosen, e.g. <c>EG</c>.</param>
/// <param name="ContactPersonPhoneCode">The dial prefix itself, e.g. <c>+20</c>.</param>
/// <param name="ContactPersonPhoneNumber">The national number, digits only, without the prefix.</param>
public sealed record SetupOrderRequest(
    Guid VerificationCountryId,
    Guid? CurrencyId,
    string ContactPersonName,
    string ContactPersonPhoneCountry,
    string ContactPersonPhoneCode,
    string ContactPersonPhoneNumber);
