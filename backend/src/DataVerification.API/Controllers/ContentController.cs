using Asp.Versioning;
using DataVerification.Application.Features.Content;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using DataVerification.API.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers;

/// <summary>
/// Public, unauthenticated site content consumed by the marketing pages of the applicant web app.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/content")]
[Produces("application/json")]
public sealed class ContentController : ControllerBase
{
    private readonly ISender _sender;

    public ContentController(ISender sender) => _sender = sender;

    /// <summary>The landing "features" section: heading plus the published cards, in display order.</summary>
    [HttpGet("landing")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LandingContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<LandingContentDto>> GetLanding(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetLandingContentQuery(), cancellationToken));

    /// <summary>The landing "tracks" section: active service types flagged to show on the landing.</summary>
    [HttpGet("services")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<LandingServiceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LandingServiceDto>>> GetServices(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetLandingServicesQuery(), cancellationToken));

    /// <summary>
    /// The contact page's directory: the authorised agents by country, and the administration's
    /// own details. Active, reachable entries only, resolved to the caller's language.
    /// </summary>
    [HttpGet("contact-directory")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ContactDirectoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ContactDirectoryDto>> GetContactDirectory(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetContactDirectoryQuery(), cancellationToken));

    /// <summary>
    /// Everything the site footer draws beyond its channels: the subtitle, the three column
    /// headings and their links, and the marks beside the brand block.
    /// </summary>
    [HttpGet("footer")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FooterContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FooterContentDto>> GetFooter(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetFooterContentQuery(), cancellationToken));

    /// <summary>
    /// Searches the published tools and guides for a phrase, in the caller's language. Anonymous:
    /// this is the same content the tools page already serves, reached a different way.
    /// </summary>
    [HttpGet("knowledge/search")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<KnowledgeHitDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<KnowledgeHitDto>>> SearchKnowledge(
        [FromQuery] string q,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new SearchKnowledgeQuery(q ?? string.Empty), cancellationToken));

    /// <summary>
    /// Answers a question from the published guides, using the configured AI provider.
    ///
    /// Rate limited harder than the rest of the API: every call spends the operator's provider
    /// quota, so this is the one public endpoint where a loop costs real money. Returns
    /// <c>isConfigured: false</c> rather than an error when no key has been set, which is a state
    /// the site can explain to a visitor.
    /// </summary>
    [HttpPost("knowledge/ask")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.KnowledgeAi)]
    [ProducesResponseType(typeof(AiAnswerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AiAnswerDto>> AskKnowledge(
        [FromBody] AskKnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Ok(await _sender.Send(query, cancellationToken));
    }

    /// <summary>
    /// The landing page's coverage section: its heading, and the countries to mark on the map.
    /// Each country carries its ISO code, which is what places its spot.
    /// </summary>
    [HttpGet("coverage")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CoverageContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CoverageContentDto>> GetCoverage(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetCoverageContentQuery(), cancellationToken));

    /// <summary>
    /// The site header's entries, in the order an operator arranged them. Visibility travels with
    /// each row and is applied by the site, so this response is the same for every visitor.
    /// </summary>
    [HttpGet("header")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HeaderContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<HeaderContentDto>> GetHeader(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetHeaderContentQuery(), cancellationToken));

    /// <summary>
    /// The headings above the New application wizard's steps, in the language asked for. A step
    /// with nothing written for that language comes back null, and the wizard draws its own copy.
    /// </summary>
    [HttpGet("wizard")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(WizardContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WizardContentDto>> GetWizard(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetWizardContentQuery(), cancellationToken));

    /// <summary>Streams one footer mark. Public, because the footer is on every public page.</summary>
    [HttpGet("footer/logos/{id:guid}/image")]
    [AllowAnonymous]
    [EmbeddableResource]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFooterLogoImage(Guid id, CancellationToken cancellationToken)
    {
        var image = await _sender.Send(new GetFooterLogoImageQuery(id), cancellationToken);

        // Content-hashed names are not used here, so a short cache keeps a replaced mark from
        // lingering while still sparing the server a fetch per page view.
        Response.Headers.CacheControl = "public, max-age=300";
        return File(image.Content, image.ContentType);
    }

    /// <summary>
    /// Whether a logo has been uploaded, and a version token that changes when it is replaced.
    /// Public, because the brand mark is on every page including the signed-out ones.
    /// </summary>
    [HttpGet("branding")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(BrandingDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BrandingDto>> GetBranding(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetBrandingQuery(), cancellationToken));

    /// <summary>
    /// Streams the uploaded logo. 404 when none has been uploaded, which the sites read as
    /// "use the mark bundled with the build".
    /// </summary>
    [HttpGet("logo")]
    [AllowAnonymous]
    [EmbeddableResource]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLogo(CancellationToken cancellationToken)
    {
        var logo = await _sender.Send(new GetLogoQuery(), cancellationToken);

        // The callers append a version token that changes on every upload, so this can be cached
        // hard without a replaced logo lingering.
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(logo.Content, logo.ContentType);
    }

    /// <summary>
    /// The footer's "Follow us" and "Message us" rows: active channels with an address, in display
    /// order. Public, because the footer is on every public page.
    /// </summary>
    [HttpGet("social-links")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FooterChannelsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FooterChannelsDto>> GetSocialLinks(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetSocialLinksQuery(), cancellationToken));

    /// <summary>
    /// The tools and guides shown to applicants: published video and image entries in display
    /// order, resolved to the caller's language.
    /// </summary>
    [HttpGet("tools")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<ToolResourceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ToolResourceDto>>> GetTools(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetToolResourcesQuery(), cancellationToken));

    /// <summary>
    /// Streams a tool entry's image. Public, because the page it appears on is public — the id is
    /// the only key, and it reveals nothing beyond the picture itself.
    /// </summary>
    [HttpGet("tools/{id:guid}/image")]
    [AllowAnonymous]
    [EmbeddableResource]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetToolImage(Guid id, CancellationToken cancellationToken)
    {
        var image = await _sender.Send(new GetToolResourceImageQuery(id), cancellationToken);

        // Content-hashed names are not used here, so a short cache keeps a replaced picture from
        // lingering while still sparing the server a fetch per page view.
        Response.Headers.CacheControl = "public, max-age=300";
        return File(image.Content, image.ContentType);
    }
}
