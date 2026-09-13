using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;
using DataVerification.Domain.Events;

namespace DataVerification.Domain.Entities;

/// <summary>
/// The aggregate root of a single verification request. It owns the applicant's names, purchased
/// services, files, comments and status history, and it is the only place the status lifecycle may
/// be changed — see <see cref="TransitionTo"/>.
/// </summary>
/// <remarks>
/// Named <c>VerificationApplication</c> rather than <c>Application</c> so the type does not collide
/// with the <c>DataVerification.Application</c> namespace. It still maps to the
/// <c>Applications</c> table.
/// </remarks>
public class VerificationApplication : Entity, ISoftDeletable
{
    /// <summary>
    /// The status lifecycle, as an explicit adjacency map. Anything not listed is illegal, which
    /// is what stops a paid application from being edited back into Draft or a finished one from
    /// being refunded.
    /// </summary>
    private static readonly Dictionary<ApplicationStatus, ApplicationStatus[]> AllowedTransitions = new()
    {
        [ApplicationStatus.Draft] = [ApplicationStatus.PendingPayment],
        [ApplicationStatus.PendingPayment] = [ApplicationStatus.Draft, ApplicationStatus.Pending],
        [ApplicationStatus.Pending] = [ApplicationStatus.InProgress, ApplicationStatus.Refunded],
        [ApplicationStatus.InProgress] =
        [
            ApplicationStatus.MissedInfo,
            ApplicationStatus.Success,
            ApplicationStatus.Failed,
        ],
        [ApplicationStatus.MissedInfo] = [ApplicationStatus.InProgress],
        [ApplicationStatus.Success] = [],
        [ApplicationStatus.Failed] = [],
        [ApplicationStatus.Refunded] = [],
    };

    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    /// <summary>Human-facing reference shown to the applicant and used in admin search.</summary>
    public required string ApplicationNumber { get; set; }

    /// <summary>الطلب موجه إلي — the body the request is addressed to.</summary>
    public required string AddressedTo { get; set; }

    /// <summary>
    /// Null while the applicant is still filling the wizard in. Every field a Draft may leave
    /// blank is nullable for exactly that reason — <see cref="EnsureReadyToSubmit"/> is where
    /// completeness is insisted on, not the column definition.
    /// </summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>The applicant's own email address, as entered on the personal step.</summary>
    public string? ApplicantEmail { get; set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 code of the country the applicant's phone belongs to, e.g. <c>EG</c>.
    /// Kept beside the dial code because a dial code alone is ambiguous — <c>+1</c> is both US and
    /// CA — and the flag shown next to the number is drawn from this.
    /// </summary>
    public string? ApplicantPhoneCountry { get; set; }

    /// <summary>International dial prefix of the applicant's phone, e.g. <c>+20</c>.</summary>
    public string? ApplicantPhoneCode { get; set; }

    /// <summary>The applicant's phone, national part only, digits without the dial prefix.</summary>
    public string? ApplicantPhoneNumber { get; set; }

    /// <summary>Dial code and national number joined, or null while the phone is still blank.</summary>
    public string? ApplicantPhone =>
        string.IsNullOrWhiteSpace(ApplicantPhoneNumber)
            ? null
            : $"{ApplicantPhoneCode}{ApplicantPhoneNumber}";

    public ApplicationStatus Status { get; private set; } = ApplicationStatus.Draft;

    public Guid? TransactionTypeId { get; set; }

    public TransactionType? TransactionType { get; set; }

    public Guid? SubTransactionTypeId { get; set; }

    public SubTransactionType? SubTransactionType { get; set; }

    public Guid? VerificationAuthorityId { get; set; }

    public VerificationAuthority? VerificationAuthority { get; set; }

    /// <summary>Sum of the service line totals, always recomputed server-side from stored prices.</summary>
    public decimal TotalCost { get; private set; }

    public DateTime? PaidAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>Guards the paid/unpaid check against two concurrent payment attempts.</summary>
    public byte[]? RowVersion { get; set; }

    public ICollection<ApplicationName> Names { get; set; } = [];

    public ICollection<ApplicationService> Services { get; set; } = [];

    /// <summary>Answers to the custom fields configured on the required documents.</summary>
    public ICollection<ApplicationDocumentValue> DocumentValues { get; set; } = [];

    public ICollection<ApplicationFile> Files { get; set; } = [];

    /// <summary>Documents an administrator attached while reviewing, with their recorded details.</summary>
    public ICollection<ApplicationDocument> Documents { get; set; } = [];

    public ICollection<ApplicationComment> Comments { get; set; } = [];

    public ICollection<ApplicationStatusHistory> StatusHistory { get; set; } = [];

    /// <summary>Paid applications are locked: no edit, no delete.</summary>
    public bool IsPaid => PaidAtUtc.HasValue;

    /// <summary>Editing is allowed only before the applicant has paid.</summary>
    public bool CanEdit() => !IsPaid && Status is ApplicationStatus.Draft or ApplicationStatus.PendingPayment;

    /// <summary>Deleting follows the same rule as editing.</summary>
    public bool CanDelete() => CanEdit();

    /// <summary>Refunds are only possible while the work has not started.</summary>
    public bool CanRefund() => Status == ApplicationStatus.Pending && IsPaid;

    /// <summary>
    /// The single entry point for status changes. Rejects illegal transitions with a
    /// <see cref="DomainException"/> and appends a history row that drives the activity timeline.
    /// </summary>
    public ApplicationStatusHistory TransitionTo(ApplicationStatus newStatus, Actor actor, string? note = null)
    {
        if (newStatus == Status)
        {
            throw new DomainException(
                "application.status_unchanged",
                $"The application is already in status '{Status}'.");
        }

        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new DomainException(
                "application.illegal_status_transition",
                $"Cannot move an application from '{Status}' to '{newStatus}'.");
        }

        var previous = Status;
        Status = newStatus;
        UpdatedAtUtc = DateTime.UtcNow;

        var history = new ApplicationStatusHistory
        {
            ApplicationId = Id,
            Application = this,
            FromStatus = previous,
            ToStatus = newStatus,
            ChangedByType = actor.Type,
            ChangedById = actor.Id,
            ChangedByName = actor.DisplayName,
            Note = note,
        };

        StatusHistory.Add(history);
        Raise(new ApplicationStatusChangedEvent(Id, previous, newStatus, actor, DateTime.UtcNow));
        return history;
    }

    /// <summary>
    /// Everything a Draft is allowed to omit while it is being filled in, but must carry before it
    /// can be submitted. Returns the missing field names, empty when the application is complete.
    /// </summary>
    public IReadOnlyList<string> FindMissingRequiredFields()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(AddressedTo)) missing.Add(nameof(AddressedTo));
        if (BirthDate is null) missing.Add(nameof(BirthDate));
        if (string.IsNullOrWhiteSpace(ApplicantEmail)) missing.Add(nameof(ApplicantEmail));
        if (string.IsNullOrWhiteSpace(ApplicantPhoneNumber)) missing.Add(nameof(ApplicantPhone));
        if (Names.Count < 2) missing.Add(nameof(Names));
        if (TransactionTypeId is null) missing.Add(nameof(TransactionTypeId));
        if (SubTransactionTypeId is null) missing.Add(nameof(SubTransactionTypeId));
        if (VerificationAuthorityId is null) missing.Add(nameof(VerificationAuthorityId));
        if (Services.Count == 0) missing.Add(nameof(Services));

        return missing;
    }

    /// <summary>Refuses to submit a draft that is still missing any of its required parts.</summary>
    public void EnsureReadyToSubmit()
    {
        var missing = FindMissingRequiredFields();

        if (missing.Count > 0)
        {
            throw new DomainException(
                "application.incomplete",
                $"Complete every step before submitting. Missing: {string.Join(", ", missing)}.");
        }
    }

    /// <summary>Draft → PendingPayment. Callers must have verified that mandatory files are present.</summary>
    public ApplicationStatusHistory Submit(Actor actor)
    {
        EnsureReadyToSubmit();
        return TransitionTo(ApplicationStatus.PendingPayment, actor);
    }

    /// <summary>
    /// Marks the application paid and moves it into the review queue. Called inside the payment
    /// transaction after the wallet has been debited.
    /// </summary>
    public ApplicationStatusHistory MarkPaid(Actor actor, DateTime utcNow)
    {
        if (Status != ApplicationStatus.PendingPayment)
        {
            throw new DomainException(
                "application.not_awaiting_payment",
                $"Only applications awaiting payment can be paid; this one is '{Status}'.");
        }

        PaidAtUtc = utcNow;
        return TransitionTo(ApplicationStatus.Pending, actor);
    }

    /// <summary>Reverses a payment while the work has not started, returning money to the wallet.</summary>
    public ApplicationStatusHistory Refund(Actor actor, string? note = null)
    {
        if (!CanRefund())
        {
            throw new DomainException(
                "application.not_refundable",
                $"Only paid applications still awaiting review can be refunded; this one is '{Status}'.");
        }

        return TransitionTo(ApplicationStatus.Refunded, actor, note);
    }

    /// <summary>Guard used by the update/delete commands so the rule lives in one place.</summary>
    public void EnsureEditable()
    {
        if (!CanEdit())
        {
            throw new DomainException(
                "application.not_editable",
                IsPaid
                    ? "A paid application can no longer be edited or deleted."
                    : $"An application in status '{Status}' can no longer be edited or deleted.");
        }
    }

    /// <summary>Recomputes the total from the current service lines. Never trusts a client total.</summary>
    public decimal RecalculateTotal()
    {
        TotalCost = Services.Sum(s => s.LineTotal);
        return TotalCost;
    }

    public void SoftDelete(DateTime utcNow)
    {
        EnsureEditable();
        IsDeleted = true;
        DeletedAtUtc = utcNow;
    }
}
