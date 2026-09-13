using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content;

/// <summary>
/// One footer channel as the public site sees it. The platform is sent by name rather than by
/// number so the site can key its icon and brand colour off something readable, and so inserting a
/// value into the enum cannot silently repaint every link.
/// </summary>
public sealed record SocialLinkDto(Guid Id, string Platform, string Url, int SortOrder)
{
    public static SocialLinkDto From(SocialLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        return new(link.Id, link.Platform.ToString(), link.Url, link.SortOrder);
    }
}

/// <summary>The footer's two rows, each already filtered and ordered for rendering.</summary>
public sealed record FooterChannelsDto(
    IReadOnlyList<SocialLinkDto> FollowUs,
    IReadOnlyList<SocialLinkDto> MessageUs);

public sealed record GetSocialLinksQuery : IRequest<FooterChannelsDto>;

public sealed class GetSocialLinksQueryHandler : IRequestHandler<GetSocialLinksQuery, FooterChannelsDto>
{
    private readonly IApplicationDbContext _db;

    public GetSocialLinksQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<FooterChannelsDto> Handle(
        GetSocialLinksQuery request,
        CancellationToken cancellationToken)
    {
        // One trip for both rows: the footer renders them together, and there are only ever a
        // handful of links in total.
        var links = await _db.SocialLinks
            .AsNoTracking()
            .Where(link => link.IsActive && link.Url != "")
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.Platform)
            .ToListAsync(cancellationToken);

        return new FooterChannelsDto(
            Select(links, SocialLinkPlacement.FollowUs),
            Select(links, SocialLinkPlacement.MessageUs));
    }

    private static List<SocialLinkDto> Select(List<SocialLink> links, SocialLinkPlacement placement) =>
        links.Where(link => link.Placement == placement).Select(SocialLinkDto.From).ToList();
}
