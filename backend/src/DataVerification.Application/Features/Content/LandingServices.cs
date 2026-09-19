using DataVerification.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content;

/// <summary>A single verification "track" advertised on the public landing page, from a service type.</summary>
public sealed record LandingServiceDto(
    Guid Id,
    string Title,
    string? Description,
    decimal Cost,
    decimal ExpressCost,
    bool EnableExpress,
    int ExecutionTimeDays,
    string? CurrencySymbol);

/// <summary>Public read: the active service types flagged to show on the landing page. Anonymous.</summary>
public sealed record GetLandingServicesQuery : IRequest<IReadOnlyList<LandingServiceDto>>;

public sealed class GetLandingServicesHandler
    : IRequestHandler<GetLandingServicesQuery, IReadOnlyList<LandingServiceDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetLandingServicesHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<LandingServiceDto>> Handle(
        GetLandingServicesQuery request,
        CancellationToken cancellationToken)
    {
        var isArabic = _currentUser.LanguageCode
            .StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        var services = await _db.ServiceTypes
            .AsNoTracking()
            .Where(s => s.IsActive && s.ShowOnLanding)
            .OrderBy(s => s.Cost)
            .ThenBy(s => s.NameEn)
            .Select(s => new LandingServiceDto(
                s.Id,
                isArabic ? s.NameAr : s.NameEn,
                // An administrator may keep a description for the panel only.
                s.HideDescription ? null : (isArabic ? s.DescriptionAr : s.DescriptionEn),
                s.Cost,
                s.EnableExpress ? s.ExpressCost : 0m,
                s.EnableExpress,
                s.ExecutionTimeDays,
                // Best-effort price currency: the first currency mapped to the authority's country.
                s.VerificationAuthority!.Country!.CountryCurrencies
                    .Select(cc => cc.Currency!.Symbol)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return services;
    }
}
