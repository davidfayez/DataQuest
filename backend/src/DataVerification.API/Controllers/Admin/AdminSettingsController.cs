using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.API.Infrastructure;
using DataVerification.Application.Features.Admin.Settings;
using DataVerification.Domain.Authorization;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Admin.Security;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers.Admin;

/// <summary>Operator-managed platform settings that would otherwise need a redeploy to change.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/settings")]
[Produces("application/json")]
public sealed class AdminSettingsController : ControllerBase
{
    private readonly ISender _sender;

    public AdminSettingsController(ISender sender) => _sender = sender;

    /// <summary>
    /// The state of the email configuration: whether a SendGrid key is available, a masked preview
    /// of it, and which source it came from. The key itself is never returned.
    /// </summary>
    [HttpGet("email")]
    [RequirePermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(EmailSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EmailSettingsDto>> GetEmail(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetEmailSettingsQuery(), cancellationToken));

    /// <summary>
    /// Who each kind of outgoing email comes from, and who is blind-copied on it. Every kind is
    /// returned, configured or not, so the page renders the full set.
    /// </summary>
    [HttpGet("email-routing")]
    [RequirePermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(IReadOnlyList<EmailRoutingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<EmailRoutingDto>>> GetEmailRouting(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetEmailRoutingQuery(), cancellationToken));

    /// <summary>
    /// Sets the sender and blind-copy list for one kind of email. A blank sender clears the
    /// override, after which the address in configuration applies again.
    /// </summary>
    [HttpPut("email-routing")]
    [RequirePermission(Permissions.SettingsUpdate)]
    [ProducesResponseType(typeof(EmailRoutingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EmailRoutingDto>> SaveEmailRouting(
        [FromBody] SaveEmailRoutingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>
    /// Whether the platform can send email at all, and which sender it falls back to. Answers
    /// "why did nothing arrive" without reading a server log.
    /// </summary>
    [HttpGet("email-delivery")]
    [RequirePermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(EmailDeliveryStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailDeliveryStatusDto>> GetEmailDelivery(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetEmailDeliveryStatusQuery(), cancellationToken));

    /// <summary>
    /// The sender every kind of email falls back to when it sets no override of its own.
    /// </summary>
    [HttpGet("email-default-sender")]
    [RequirePermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(EmailDefaultSenderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailDefaultSenderDto>> GetEmailDefaultSender(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetEmailDefaultSenderQuery(), cancellationToken));

    /// <summary>
    /// Sets the platform-wide default sender, or clears it when the address is empty — after which
    /// the address in configuration applies again. Takes effect on the next email, with no restart.
    /// </summary>
    [HttpPut("email-default-sender")]
    [RequirePermission(Permissions.SettingsUpdate)]
    [ProducesResponseType(typeof(EmailDefaultSenderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EmailDefaultSenderDto>> SaveEmailDefaultSender(
        [FromBody] SaveEmailDefaultSenderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>
    /// Sends a real test email for one kind, down the same path a live one takes, and reports what
    /// the transport actually said.
    /// </summary>
    [HttpPost("email-routing/test")]
    [RequirePermission(Permissions.SettingsUpdate)]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(typeof(TestEmailResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TestEmailResultDto>> SendTestEmail(
        [FromBody] SendTestEmailCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>
    /// Sets the SendGrid API key, or clears it when the value is empty — after which the key in
    /// configuration (if any) applies again. Takes effect on the next email, with no restart.
    /// </summary>
    [HttpPut("email")]
    [RequirePermission(Permissions.SettingsUpdate)]
    [ProducesResponseType(typeof(EmailSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmailSettingsDto>> UpdateEmail(
        [FromBody] UpdateEmailSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new UpdateEmailSettingsCommand(request.ApiKey),
            cancellationToken));
    }

    /// <summary>Every press of "forgot password", newest first, with where it came from.</summary>
    [HttpGet("password-resets")]
    [RequirePermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(PagedResult<PasswordResetLogEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PasswordResetLogEntryDto>>> GetPasswordResets(
        [FromQuery] ListPasswordResetsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Ok(await _sender.Send(query, cancellationToken));
    }

    /// <summary>How long an emailed password stays good for.</summary>
    [HttpGet("password-reset-validity")]
    [RequirePermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(PasswordResetSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PasswordResetSettingsDto>> GetPasswordResetValidity(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetPasswordResetSettingsQuery(), cancellationToken));

    /// <summary>Changes the window. Takes effect on the next reset, with no restart.</summary>
    [HttpPut("password-reset-validity")]
    [RequirePermission(Permissions.SettingsUpdate)]
    [ProducesResponseType(typeof(PasswordResetSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PasswordResetSettingsDto>> UpdatePasswordResetValidity(
        [FromBody] UpdatePasswordResetSettingsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>
    /// The AI assistant's settings: whether it is on, which model it uses, and whether a key has
    /// been stored. The key itself is never returned.
    /// </summary>
    [HttpGet("ai")]
    [RequirePermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(AiAssistantSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiAssistantSettingsDto>> GetAi(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAiAssistantSettingsQuery(), cancellationToken));

    /// <summary>
    /// Saves the assistant's settings. A blank key leaves the stored one alone, which is the only
    /// way to change the model or the switch without pasting the key again.
    /// </summary>
    [HttpPut("ai")]
    [RequirePermission(Permissions.SettingsUpdate)]
    [ProducesResponseType(typeof(AiAssistantSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AiAssistantSettingsDto>> UpdateAi(
        [FromBody] UpdateAiAssistantSettingsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Ok(await _sender.Send(command, cancellationToken));
    }
}

/// <param name="ApiKey">The new key, or null/empty to clear the stored one.</param>
public sealed record UpdateEmailSettingsRequest(string? ApiKey);
