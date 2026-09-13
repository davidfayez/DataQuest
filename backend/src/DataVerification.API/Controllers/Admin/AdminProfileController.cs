using Asp.Versioning;
using DataVerification.Application.Features.Admin.Account;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// The signed-in administrator's own account: profile, password and avatar. Every action here
/// operates on the caller resolved from the token, so no id is taken from the route.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/profile")]
[Produces("application/json")]
public sealed class AdminProfileController : ControllerBase
{
    // A profile photo is capped well below the document limit; a header thumbnail never needs more.
    private const long MaxAvatarBytes = 2 * 1024 * 1024;

    private readonly ISender _sender;

    public AdminProfileController(ISender sender) => _sender = sender;

    /// <summary>The caller's name, email, roles, avatar flag and last sign-in.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminProfileDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminProfileDto>> Get(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAdminProfileQuery(), cancellationToken));

    /// <summary>Changes the caller's own password.</summary>
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _sender.Send(
            new ChangeAdminPasswordCommand(request.CurrentPassword, request.NewPassword),
            cancellationToken);

        return NoContent();
    }

    /// <summary>Uploads or replaces the caller's profile photo. JPEG or PNG, up to 2 MB.</summary>
    [HttpPost("avatar")]
    [RequestSizeLimit(MaxAvatarBytes + 8192)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(AdminAvatarResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminAvatarResult>> UploadAvatar(
        [FromForm] UploadAvatarRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "No file supplied",
                Detail = "Attach an image to the 'file' form field.",
            });
        }

        // Buffered to a seekable stream so the type validator can read the signature and storage
        // can then write the same bytes from the start.
        await using var buffer = new MemoryStream();
        await request.File.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var result = await _sender.Send(
            new UploadAdminAvatarCommand(request.File.FileName, request.File.Length, buffer),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Streams the caller's avatar. Fetched by the SPA and shown as a blob in the header.</summary>
    [HttpGet("avatar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvatar(CancellationToken cancellationToken)
    {
        var download = await _sender.Send(new GetAdminAvatarQuery(), cancellationToken);

        // A profile photo rarely changes; let the browser cache it privately for the session.
        Response.Headers.CacheControl = "private, max-age=300";
        return File(download.Content, download.ContentType);
    }
}

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed class UploadAvatarRequest
{
    public IFormFile? File { get; set; }
}
