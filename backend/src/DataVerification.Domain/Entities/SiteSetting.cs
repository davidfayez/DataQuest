using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A single editable piece of site content addressed by a stable string key (for example the
/// landing "features" section heading). Kept generic so new editable copy needs a key, not a table.
/// </summary>
public sealed class SiteSetting : Entity
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}
