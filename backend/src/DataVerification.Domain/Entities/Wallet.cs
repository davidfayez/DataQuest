using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One wallet per order, denominated in the currency chosen at setup. <see cref="Balance"/> is a
/// cached running total; <see cref="Transactions"/> is the source of truth and the two must always
/// reconcile (see <see cref="LedgerBalance"/>).
/// </summary>
public class Wallet : Entity
{
    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    public Guid CurrencyId { get; set; }

    public Currency? Currency { get; set; }

    public decimal Balance { get; private set; }

    /// <summary>Guards against two concurrent payments spending the same balance.</summary>
    public byte[]? RowVersion { get; set; }

    public ICollection<WalletTransaction> Transactions { get; set; } = [];

    /// <summary>Balance recomputed from the ledger. Used by tests and reconciliation checks.</summary>
    public decimal LedgerBalance => Transactions.Sum(t => t.Type.IsDebit() ? -t.Amount : t.Amount);

    /// <summary>
    /// Adds funds — a top-up, a refund returning money to the applicant, or a held payout coming
    /// back after the withdrawal was refused.
    /// </summary>
    public WalletTransaction Credit(
        decimal amount,
        WalletTransactionType type,
        Actor actor,
        IReadOnlyCollection<Guid>? referenceApplicationIds = null,
        string? note = null)
    {
        if (amount <= 0)
        {
            throw new DomainException("wallet.invalid_amount", "Credit amount must be greater than zero.");
        }

        if (type.IsDebit())
        {
            throw new DomainException(
                "wallet.invalid_transaction_type",
                $"A {type} entry cannot credit the wallet.");
        }

        Balance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
        return AppendTransaction(amount, type, actor, referenceApplicationIds, note);
    }

    /// <summary>
    /// Spends or holds funds. Refuses to overdraw — the balance can never go negative, which is what
    /// makes paying several applications in one call safe to attempt optimistically, and what stops
    /// a payout being promised twice over the same money.
    /// </summary>
    public WalletTransaction Debit(
        decimal amount,
        Actor actor,
        IReadOnlyCollection<Guid>? referenceApplicationIds = null,
        string? note = null,
        WalletTransactionType type = WalletTransactionType.Payment)
    {
        if (amount <= 0)
        {
            throw new DomainException("wallet.invalid_amount", "Debit amount must be greater than zero.");
        }

        if (!type.IsDebit())
        {
            throw new DomainException(
                "wallet.invalid_transaction_type",
                $"A {type} entry cannot debit the wallet.");
        }

        if (amount > Balance)
        {
            throw new InsufficientFundsException(amount, Balance);
        }

        Balance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;
        return AppendTransaction(amount, type, actor, referenceApplicationIds, note);
    }

    private WalletTransaction AppendTransaction(
        decimal amount,
        WalletTransactionType type,
        Actor actor,
        IReadOnlyCollection<Guid>? referenceApplicationIds,
        string? note)
    {
        var transaction = new WalletTransaction
        {
            WalletId = Id,
            Wallet = this,
            Type = type,
            Amount = amount,
            BalanceAfter = Balance,
            ReferenceApplicationIds = referenceApplicationIds?.ToList() ?? [],
            PerformedByType = actor.Type,
            PerformedById = actor.Id,
            PerformedByName = actor.DisplayName,
            Note = note,
        };

        Transactions.Add(transaction);
        return transaction;
    }
}

/// <summary>Raised when a debit would overdraw the wallet. Surfaced to the API as 422.</summary>
public sealed class InsufficientFundsException : DomainException
{
    public InsufficientFundsException(decimal required, decimal available)
        : base(
            "wallet.insufficient_funds",
            $"Insufficient wallet balance. Required {required:0.00}, available {available:0.00}.")
    {
        Required = required;
        Available = available;
    }

    public InsufficientFundsException() : base("wallet.insufficient_funds", "Insufficient wallet balance.") { }

    public InsufficientFundsException(string message) : base("wallet.insufficient_funds", message) { }

    public InsufficientFundsException(string message, Exception innerException)
        : base(message, innerException) { }

    public decimal Required { get; }

    public decimal Available { get; }
}
