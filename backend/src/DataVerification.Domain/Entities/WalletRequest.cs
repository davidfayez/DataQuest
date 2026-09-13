using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// An applicant's request to move money into or out of their order's wallet, awaiting an
/// administrator's decision. v1 has no payment gateway, so both directions are settled by an
/// operator out of band; this entity is the paper trail for that conversation.
/// </summary>
/// <remarks>
/// Deposits change nothing until they are approved. Withdrawals are different: the amount is
/// <em>held</em> — debited from the wallet the moment the request is made — so the same money
/// cannot also be spent on applications while an operator is deciding. Rejecting or cancelling a
/// withdrawal releases the hold back into the balance.
/// </remarks>
public class WalletRequest : Entity
{
    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    public Guid WalletId { get; set; }

    public Wallet? Wallet { get; set; }

    public WalletRequestType Type { get; set; }

    public WalletRequestStatus Status { get; private set; } = WalletRequestStatus.Pending;

    /// <summary>Always positive, in the wallet's currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>What the applicant told us — a transfer reference, a payout account, and so on.</summary>
    public string? ApplicantNote { get; set; }

    /// <summary>
    /// The channel the money came in through. Null on a withdrawal, and on deposits raised before
    /// payment methods existed.
    /// </summary>
    public Guid? PaymentMethodId { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>Which of the method's receiving accounts the applicant says they paid.</summary>
    public Guid? PaymentMethodAccountId { get; set; }

    public PaymentMethodAccount? PaymentMethodAccount { get; set; }

    /// <summary>
    /// The transfer reference quoted by the applicant, unique across the platform: the same receipt
    /// must not be claimable twice, whether by accident or by an applicant trying it on. Stored as
    /// typed for display; matched case-insensitively by the unique index.
    /// </summary>
    public string? ReferenceNumber { get; set; }

    /// <summary>Receipts and screenshots proving the transfer landed.</summary>
    public ICollection<WalletRequestFile> Files { get; set; } = [];

    /// <summary>
    /// What the reviewer confirmed actually arrived, which is what the wallet is credited with.
    /// Kept apart from <see cref="Amount"/> — the applicant's claim — because the two disagreeing
    /// is the normal case a reviewer exists to settle, and both halves have to stay readable
    /// afterwards.
    /// </summary>
    public decimal? ConfirmedAmount { get; private set; }

    /// <summary>
    /// The reference the reviewer matched against the statement. Unique among approved requests:
    /// the same receipt must never be credited twice. A rejection leaves it free, since nothing
    /// was taken.
    /// </summary>
    public string? ConfirmedReference { get; private set; }

    /// <summary>The amount that moved, or is proposed to move: the reviewer's word over the claim.</summary>
    public decimal EffectiveAmount => ConfirmedAmount ?? Amount;

    /// <summary>The reviewer's reason, shown to the applicant beside the decision.</summary>
    public string? ReviewerNote { get; private set; }

    public string? RequestedByName { get; set; }

    public Guid? ReviewedByAdminUserId { get; private set; }

    public string? ReviewedByName { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    /// <summary>
    /// The ledger line this request produced. For a withdrawal it is the hold, written at request
    /// time; for a deposit it is the credit, written on approval. Null while a deposit is pending.
    /// </summary>
    public Guid? WalletTransactionId { get; set; }

    /// <summary>Stops two administrators deciding the same request concurrently.</summary>
    public byte[]? RowVersion { get; set; }

    public bool IsPending => Status == WalletRequestStatus.Pending;

    /// <summary>The applicant may only call off a request nobody has decided yet.</summary>
    public bool CanCancel => IsPending;

    /// <summary>
    /// Accepts the request. The caller is responsible for the money itself: crediting the wallet
    /// for a deposit, or releasing the held funds to the payee for a withdrawal.
    /// </summary>
    public void Approve(
        Actor actor,
        decimal confirmedAmount,
        string confirmedReference,
        string? reviewerNote = null)
    {
        if (confirmedAmount <= 0)
        {
            throw new DomainException(
                "wallet_request.invalid_confirmed_amount",
                "The confirmed amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(confirmedReference))
        {
            throw new DomainException(
                "wallet_request.confirmed_reference_required",
                "The confirmed reference number is required.");
        }

        ConfirmedAmount = confirmedAmount;
        ConfirmedReference = confirmedReference.Trim();
        Decide(WalletRequestStatus.Approved, actor, reviewerNote);
    }

    /// <summary>
    /// Refuses the request. A held withdrawal must be released back by the caller.
    /// </summary>
    /// <remarks>
    /// The reviewer may record what they looked at, but nothing is required: rejecting usually
    /// means the payment was never found, so there may be no amount or reference to type. The
    /// reference is deliberately not kept — a refused claim must not burn a real receipt number
    /// the applicant will quote again once they get it right.
    /// </remarks>
    public void Reject(Actor actor, decimal? confirmedAmount = null, string? reviewerNote = null)
    {
        ConfirmedAmount = confirmedAmount;
        Decide(WalletRequestStatus.Rejected, actor, reviewerNote);
    }

    /// <summary>Withdrawn by the applicant before anyone acted on it.</summary>
    public void Cancel(Actor actor) => Decide(WalletRequestStatus.Cancelled, actor, null);

    private void Decide(WalletRequestStatus status, Actor actor, string? reviewerNote)
    {
        if (!IsPending)
        {
            throw new DomainException(
                "wallet_request.not_pending",
                $"This request was already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = status;
        ReviewerNote = reviewerNote;
        ReviewedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = ReviewedAtUtc;

        // An applicant cancelling their own request is not a review, so the reviewer stays blank.
        if (actor.Type == ActorType.Admin)
        {
            ReviewedByAdminUserId = actor.Id;
            ReviewedByName = actor.DisplayName;
        }
    }
}
