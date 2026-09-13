using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content.Admin;

/// <summary>A footer channel as the editor sees it — the inactive ones too.</summary>
public sealed record AdminSocialLinkDto(
    Guid Id,
    SocialLinkPlacement Placement,
    string PlacementName,
    SocialPlatform Platform,
    string PlatformName,
    string Url,
    bool IsActive,
    int SortOrder,
    /// <summary>False when the link would not reach the public footer, so the list can say so.</summary>
    bool IsShowable)
{
    public static AdminSocialLinkDto From(SocialLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        return new(
            link.Id,
            link.Placement,
            link.Placement.ToString(),
            link.Platform,
            link.Platform.ToString(),
            link.Url,
            link.IsActive,
            link.SortOrder,
            link.IsShowable);
    }
}

public sealed record ListSocialLinksQuery : PagedQuery, IRequest<PagedResult<AdminSocialLinkDto>>
{
    public SocialLinkPlacement? Placement { get; init; }
}

public sealed record UpsertSocialLinkCommand(
    Guid? Id,
    SocialLinkPlacement Placement,
    SocialPlatform Platform,
    string Url,
    bool IsActive,
    int SortOrder) : IRequest<AdminSocialLinkDto>;

public sealed record DeleteSocialLinkCommand(Guid Id) : IRequest<Unit>;

public sealed class UpsertSocialLinkCommandValidator : AbstractValidator<UpsertSocialLinkCommand>
{
    public UpsertSocialLinkCommandValidator()
    {
        RuleFor(c => c.Placement).IsInEnum();
        RuleFor(c => c.Platform).IsInEnum();
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        RuleFor(c => c.Url)
            .NotEmpty().WithMessage("Enter the address this link opens.")
            .MaximumLength(500);

        // http and https only. A footer icon is a link somebody clicks without reading it, and the
        // schemes that are dangerous there — javascript:, data: — are exactly the ones a plain
        // "is it a URL" check lets through.
        RuleFor(c => c.Url)
            .Must(BeHttpUrl)
            .When(c => !string.IsNullOrWhiteSpace(c.Url))
            .WithMessage("The address must start with http:// or https://");
    }

    private static bool BeHttpUrl(string url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}

public sealed class SocialLinkHandlers :
    IRequestHandler<ListSocialLinksQuery, PagedResult<AdminSocialLinkDto>>,
    IRequestHandler<UpsertSocialLinkCommand, AdminSocialLinkDto>,
    IRequestHandler<DeleteSocialLinkCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public SocialLinkHandlers(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<AdminSocialLinkDto>> Handle(
        ListSocialLinksQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.SocialLinks.AsNoTracking().AsQueryable();

        if (request.Placement is { } placement)
        {
            query = query.Where(link => link.Placement == placement);
        }

        if (request.IsActive is { } isActive)
        {
            query = query.Where(link => link.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(link => EF.Functions.Like(link.Url, $"%{term}%"));
        }

        // Grouped as the footer reads them, then by the operator's own arrangement.
        query = query
            .OrderBy(link => link.Placement)
            .ThenBy(link => link.SortOrder)
            .ThenBy(link => link.Platform);

        return await query.ToPagedResultAsync(request, AdminSocialLinkDto.From, cancellationToken);
    }

    public async Task<AdminSocialLinkDto> Handle(
        UpsertSocialLinkCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var editingId = request.Id ?? Guid.Empty;

        // Caught here rather than left to the unique index, so the editor gets a sentence instead
        // of a constraint violation.
        var duplicate = await _db.SocialLinks.AnyAsync(
            link => link.Placement == request.Placement
                && link.Platform == request.Platform
                && link.Id != editingId,
            cancellationToken);

        if (duplicate)
        {
            throw new ConflictException(
                $"{request.Platform} is already listed under {request.Placement}. Edit that entry instead.");
        }

        SocialLink link;

        if (request.Id is { } id && id != Guid.Empty)
        {
            link = await _db.SocialLinks
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(SocialLink), id);
        }
        else
        {
            link = new SocialLink { Url = string.Empty };
            _db.SocialLinks.Add(link);
        }

        link.Placement = request.Placement;
        link.Platform = request.Platform;
        link.Url = request.Url.Trim();
        link.IsActive = request.IsActive;
        link.SortOrder = request.SortOrder;

        await _db.SaveChangesAsync(cancellationToken);

        return AdminSocialLinkDto.From(link);
    }

    public async Task<Unit> Handle(DeleteSocialLinkCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var link = await _db.SocialLinks
            .FirstOrDefaultAsync(candidate => candidate.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(SocialLink), request.Id);

        _db.SocialLinks.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
