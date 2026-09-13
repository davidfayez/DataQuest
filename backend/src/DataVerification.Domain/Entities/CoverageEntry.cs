using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One country marked on the landing page's coverage map.
///
/// This points at a real <see cref="Entities.Country"/> rather than carrying a name of its own, for
/// two reasons. The map is drawn from ISO alpha-2 codes, so a free-typed name could never be placed
/// on it; and the country's name is already translated into every language the site speaks, so
/// copying it here would mean a second set of names to keep in step with the first.
///
/// It stays a separate table rather than a flag on the country: the lookup holds every country in
/// the world so an order can name any of them, while this is the much shorter marketing claim about
/// where the platform actually verifies.
/// </summary>
public sealed class CoverageEntry : Entity
{
    public Guid CountryId { get; set; }

    public Country? Country { get; set; }

    /// <summary>Whether the map draws a pin or the country's flag here.</summary>
    public CoverageMarker Marker { get; set; } = CoverageMarker.Spot;

    /// <summary>Ascending order in the list beside the map.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only published entries reach the public site.</summary>
    public bool IsPublished { get; set; } = true;
}
