using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// The tenant an order belongs to. v1 seeds exactly one record — NEN — and every Order is created
/// against it; the table exists so additional clients can be onboarded without a schema change.
/// </summary>
public class Client : Entity
{
    public const string DefaultCode = "NEN";

    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Marks the one client whose orders are local.
    ///
    /// Exactly one client carries this at any time: setting it on another moves it, and it cannot
    /// simply be switched off — a platform with none would have nowhere for a local order to
    /// belong. A filtered unique index in the database is what makes "at most one" true no matter
    /// which code path writes it; the handler is what makes it "exactly one".
    /// </summary>
    public bool IsLocalOrder { get; set; }

    public ICollection<Order> Orders { get; set; } = [];
}
