using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Content;
using DataVerification.API.Infrastructure;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// Admin management of editable site content. The landing "features" section heading and its cards
/// are surfaced here; each action is gated by its own permission. Writes are upserts, so the
/// required permission (create vs update) is resolved from the id at request time.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/content")]
[Produces("application/json")]
public sealed class AdminContentController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminContentController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    private void RequireUpsert(Guid? id) =>
        Require(id is null || id == Guid.Empty
            ? Permissions.LandingContentCreate
            : Permissions.LandingContentUpdate);

    private void RequireToolUpsert(Guid? id) =>
        Require(id is null || id == Guid.Empty ? Permissions.ToolsCreate : Permissions.ToolsUpdate);

    private void Require(string permission)
    {
        if (!_currentUser.Permissions.Contains(permission))
        {
            throw new ForbiddenAccessException($"This action requires the '{permission}' permission.");
        }
    }

    /// <summary>Every landing card (including unpublished) with all translations, plus the heading.</summary>
    [HttpGet("landing")]
    [RequirePermission(Permissions.LandingContentView)]
    [ProducesResponseType(typeof(AdminLandingContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminLandingContentDto>> GetLanding(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAdminLandingContentQuery(), cancellationToken));

    /// <summary>Edit the section heading (eyebrow + title).</summary>
    [HttpPut("landing/heading")]
    [RequirePermission(Permissions.LandingContentUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateHeading(
        [FromBody] UpdateLandingHeadingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _sender.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>Create or update a single feature card.</summary>
    [HttpPost("landing/features")]
    [ProducesResponseType(typeof(AdminLandingFeatureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminLandingFeatureDto>> UpsertFeature(
        [FromBody] UpsertLandingFeatureCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Delete a feature card.</summary>
    [HttpDelete("landing/features/{id:guid}")]
    [RequirePermission(Permissions.LandingContentDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteFeature(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteLandingFeatureCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Create or update a single statistic in the landing strip.</summary>
    [HttpPost("landing/stats")]
    [ProducesResponseType(typeof(AdminLandingStatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminLandingStatDto>> UpsertStat(
        [FromBody] UpsertLandingStatCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Delete a statistic.</summary>
    [HttpDelete("landing/stats/{id:guid}")]
    [RequirePermission(Permissions.LandingContentDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteStat(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteLandingStatCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Create or update one step in the "how it works" section.</summary>
    [HttpPost("landing/steps")]
    [ProducesResponseType(typeof(AdminLandingStepDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminLandingStepDto>> UpsertStep(
        [FromBody] UpsertLandingStepCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>
    /// How the "how it works" cards are arranged: a grid of N per row, or a slider.
    /// </summary>
    [HttpPut("landing/steps/layout")]
    [RequirePermission(Permissions.LandingContentUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStepsLayout(
        [FromBody] UpdateLandingStepsLayoutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _sender.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>Delete a step.</summary>
    [HttpDelete("landing/steps/{id:guid}")]
    [RequirePermission(Permissions.LandingContentDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteStep(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteLandingStepCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Create or update one body in the "trusted for verification with" strip.</summary>
    [HttpPost("landing/trusted-by")]
    [ProducesResponseType(typeof(AdminLandingTrustEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminLandingTrustEntryDto>> UpsertTrustEntry(
        [FromBody] UpsertLandingTrustEntryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Delete a trust-strip entry.</summary>
    [HttpDelete("landing/trusted-by/{id:guid}")]
    [RequirePermission(Permissions.LandingContentDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteTrustEntry(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteLandingTrustEntryCommand(id), cancellationToken);
        return NoContent();
    }

    // ------------------------------------------------------------------ Tools

    /// <summary>Every tool entry, including unpublished ones, with all translations.</summary>
    [HttpGet("tools")]
    [RequirePermission(Permissions.ToolsView)]
    [ProducesResponseType(typeof(IReadOnlyList<AdminToolResourceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminToolResourceDto>>> GetTools(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAdminToolResourcesQuery(), cancellationToken));

    /// <summary>
    /// Create or update one entry. An image entry is saved first and its picture uploaded second,
    /// so a new image entry stays off the public page until the file arrives.
    /// </summary>
    [HttpPost("tools")]
    [ProducesResponseType(typeof(AdminToolResourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminToolResourceDto>> UpsertTool(
        [FromBody] UpsertToolResourceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireToolUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Uploads or replaces the picture on an image entry. JPEG or PNG, up to 5 MB.</summary>
    [HttpPost("tools/{id:guid}/image")]
    [RequirePermission(Permissions.ToolsUpdate)]
    [RequestSizeLimit(ToolResourceHandlers.MaxImageBytes + 8192)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(AdminToolResourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminToolResourceDto>> UploadToolImage(
        Guid id,
        [FromForm] UploadToolImageRequest request,
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

        // Buffered to a seekable stream so the signature can be read and the same bytes then
        // written to storage from the start.
        await using var buffer = new MemoryStream();
        await request.File.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return Ok(await _sender.Send(
            new UploadToolResourceImageCommand(id, request.File.FileName, request.File.Length, buffer),
            cancellationToken));
    }

    /// <summary>Delete an entry, and its uploaded picture if it had one.</summary>
    [HttpDelete("tools/{id:guid}")]
    [RequirePermission(Permissions.ToolsDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteTool(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteToolResourceCommand(id), cancellationToken);
        return NoContent();
    }

    // ----------------------------------------------------------------- Footer

    /// <summary>Every footer link and mark, including the inactive ones, with all translations.</summary>
    [HttpGet("footer")]
    [RequirePermission(Permissions.LandingContentView)]
    [ProducesResponseType(typeof(AdminFooterContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminFooterContentDto>> GetFooter(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAdminFooterContentQuery(), cancellationToken));

    /// <summary>The footer's subtitle and column headings.</summary>
    [HttpPut("footer/headings")]
    [RequirePermission(Permissions.LandingContentUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateFooterHeadings(
        [FromBody] UpdateFooterHeadingsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _sender.Send(command, cancellationToken);
        return NoContent();
    }

    // --------------------------------------------------------------- Coverage

    /// <summary>Every country on the coverage map, including the unpublished ones.</summary>
    [HttpGet("coverage")]
    [RequirePermission(Permissions.LandingContentView)]
    [ProducesResponseType(typeof(AdminCoverageContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminCoverageContentDto>> GetCoverage(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAdminCoverageContentQuery(), cancellationToken));

    /// <summary>The coverage section's title and subtitle.</summary>
    [HttpPut("coverage/heading")]
    [RequirePermission(Permissions.LandingContentUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateCoverageHeading(
        [FromBody] UpdateCoverageHeadingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _sender.Send(command, cancellationToken);
        return NoContent();
    }

    // ------------------------------------------------------------------ Site header

    /// <summary>Every header entry, including the hidden ones, with any label overrides.</summary>
    [HttpGet("header")]
    [RequirePermission(Permissions.LandingContentView)]
    [ProducesResponseType(typeof(AdminHeaderContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminHeaderContentDto>> GetHeader(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAdminHeaderContentQuery(), cancellationToken));

    /// <summary>Add a header entry, or change where it sits, where it goes and who sees it.</summary>
    [HttpPost("header/links")]
    [ProducesResponseType(typeof(AdminHeaderLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminHeaderLinkDto>> UpsertHeaderLink(
        [FromBody] UpsertHeaderLinkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Removes a header entry. Hiding one instead keeps it available to put back.</summary>
    [HttpDelete("header/links/{id:guid}")]
    [RequirePermission(Permissions.LandingContentDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteHeaderLink(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteHeaderLinkCommand(id), cancellationToken);
        return NoContent();
    }

    // ----------------------------------------------------------- New application wizard

    /// <summary>Every wizard step's heading, in every language it has been written in.</summary>
    [HttpGet("wizard")]
    [RequirePermission(Permissions.LandingContentView)]
    [ProducesResponseType(typeof(AdminWizardContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminWizardContentDto>> GetWizard(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAdminWizardContentQuery(), cancellationToken));

    /// <summary>One step's title and subtitle. Blank puts the step back to the built-in wording.</summary>
    [HttpPut("wizard/heading")]
    [RequirePermission(Permissions.LandingContentUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateWizardHeading(
        [FromBody] UpdateWizardStepHeadingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _sender.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>Put a country on the map, or change where it sits and whether it shows.</summary>
    [HttpPost("coverage/countries")]
    [ProducesResponseType(typeof(AdminCoverageEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminCoverageEntryDto>> UpsertCoverageCountry(
        [FromBody] UpsertCoverageEntryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Take a country off the map.</summary>
    [HttpDelete("coverage/countries/{id:guid}")]
    [RequirePermission(Permissions.LandingContentDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCoverageCountry(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteCoverageEntryCommand(id), cancellationToken);
        return NoContent();
    }

    // ----------------------------------------------------------------- Footer

    /// <summary>Create or update one row in a footer column.</summary>
    [HttpPost("footer/links")]
    [ProducesResponseType(typeof(AdminFooterLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminFooterLinkDto>> UpsertFooterLink(
        [FromBody] UpsertFooterLinkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Delete a footer link.</summary>
    [HttpDelete("footer/links/{id:guid}")]
    [RequirePermission(Permissions.LandingContentDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteFooterLink(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteFooterLinkCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Create or update one mark beside the brand block. Its image is uploaded separately.</summary>
    [HttpPost("footer/logos")]
    [ProducesResponseType(typeof(AdminFooterLogoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminFooterLogoDto>> UpsertFooterLogo(
        [FromBody] UpsertFooterLogoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Attaches or replaces one mark's image.</summary>
    [HttpPost("footer/logos/{id:guid}/image")]
    [RequirePermission(Permissions.LandingContentUpdate)]
    [RequestSizeLimit(FooterContentDefaults.MaxLogoBytes + 8192)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(AdminFooterLogoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminFooterLogoDto>> UploadFooterLogoImage(
        Guid id,
        [FromForm] UploadFooterLogoRequest request,
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

        // Buffered to a seekable stream so the signature can be read and the same bytes then
        // written to storage from the start.
        await using var buffer = new MemoryStream();
        await request.File.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return Ok(await _sender.Send(
            new UploadFooterLogoImageCommand(id, buffer, request.File.FileName, request.File.Length),
            cancellationToken));
    }

    /// <summary>Delete a mark, and its uploaded image.</summary>
    [HttpDelete("footer/logos/{id:guid}")]
    [RequirePermission(Permissions.LandingContentDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteFooterLogo(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteFooterLogoCommand(id), cancellationToken);
        return NoContent();
    }

    // --------------------------------------------------------------- Branding

    /// <summary>Whether a logo has been uploaded, and the version token the sites cache against.</summary>
    [HttpGet("branding")]
    [RequirePermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(BrandingDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BrandingDto>> GetBranding(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetBrandingQuery(), cancellationToken));

    /// <summary>
    /// Replaces the platform logo. Applies to both sites on their next load, with no redeploy.
    /// </summary>
    [HttpPost("branding/logo")]
    [RequirePermission(Permissions.SettingsUpdate)]
    [RequestSizeLimit(BrandingDefaults.MaxLogoBytes + 8192)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(BrandingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BrandingDto>> UploadLogo(
        [FromForm] UploadLogoRequest request,
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

        // Buffered to a seekable stream so the signature can be read and the same bytes then
        // written to storage from the start.
        await using var buffer = new MemoryStream();
        await request.File.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return Ok(await _sender.Send(
            new UploadLogoCommand(buffer, request.File.FileName, request.File.Length),
            cancellationToken));
    }

    /// <summary>Removes the uploaded logo; the mark bundled with the build applies again.</summary>
    [HttpDelete("branding/logo")]
    [RequirePermission(Permissions.SettingsUpdate)]
    [ProducesResponseType(typeof(BrandingDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BrandingDto>> DeleteLogo(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteLogoCommand(), cancellationToken));
}

/// <param name="File">The JPEG or PNG to use as the platform logo.</param>
public sealed record UploadLogoRequest(IFormFile? File);

/// <param name="File">The JPEG or PNG to show as this footer mark.</param>
public sealed record UploadFooterLogoRequest(IFormFile? File);

/// <param name="File">The JPEG or PNG to attach to the entry.</param>
public sealed record UploadToolImageRequest(IFormFile? File);
