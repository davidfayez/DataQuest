using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// An immutable ledger line. A single Payment row can reference several applications, because the
/// applicant may settle multiple unpaid applications in one action.
/// </summary>
public class WalletTransaction : Entity
{
    public Guid WalletId { get; set; }

    public Wallet? Wallet { get; set; }

    public WalletTransactionType Type { get; set; }

    /// <summary>Always positive; <see cref="Type"/> carries the direction.</summary>
    public decimal Amount { get; set; }

    /// <summary>Wallet balance immediately after this entry, so statements need no running sum.</summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>Applications settled or refunded by this entry. Persisted as a JSON array.</summary>
    public List<Guid> ReferenceApplicationIds { get; set; } = [];

    public ActorType PerformedByType { get; set; }

    public Guid? PerformedById { get; set; }

    public string? PerformedByName { get; set; }

    public string? Note { get; set; }
}
