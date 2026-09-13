using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A payment channel an applicant may use to put money into their wallet — "Vodafone Cash",
/// "InstaPay", "Pay by card". What a method needs configuring, and what it asks the applicant for,
/// is decided entirely by its <see cref="PaymentMethodType"/>, so a new channel is a row in the
/// admin panel rather than a code change.
/// </summary>
public class PaymentMethod : LocalizedLookup
{
    public Guid PaymentMethodTypeId { get; set; }

    public PaymentMethodType? Type { get; set; }

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    /// <summary>Shown to the applicant beside the method. Safe to publish.</summary>
    public string? PublicNoteAr { get; set; }

    public string? PublicNoteEn { get; set; }

    /// <summary>
    /// Never leaves the admin realm — reconciliation hints, who owns the account, which reviewer to
    /// escalate to. Deliberately on a different DTO from <see cref="PublicNoteEn"/> so an applicant
    /// response can never carry it by accident.
    /// </summary>
    public string? PrivateNoteAr { get; set; }

    public string? PrivateNoteEn { get; set; }

    /// <summary>
    /// Where the applicant is sent, for a method whose type is
    /// <see cref="PaymentMethodKind.ExternalLink"/>. Null for a transfer.
    /// </summary>
    public string? ExternalUrl { get; set; }

    /// <summary>Ascending display order in the applicant's method picker.</summary>
    public int SortOrder { get; set; }

    /// <summary>Countries this method is offered in.</summary>
    public ICollection<PaymentMethodCountry> CountryLinks { get; set; } = [];

    /// <summary>
    /// Currencies this method accepts. Always a subset of the currencies its countries support —
    /// a method cannot take a currency none of the countries it serves actually uses.
    /// </summary>
    public ICollection<PaymentMethodCurrency> CurrencyLinks { get; set; } = [];

    /// <summary>The receiving numbers/handles the applicant may transfer to.</summary>
    public ICollection<PaymentMethodAccount> Accounts { get; set; } = [];

    /// <summary>
    /// The provider credentials and endpoints behind an online payment method, or null when the
    /// method is paid by hand. Loaded only where it is needed — it holds secrets.
    /// </summary>
    public PaymentMethodIntegration? Integration { get; set; }

    /// <summary>
    /// Who to tell when money moves through this method — the finance mailbox, the operator on
    /// duty. Per method rather than platform-wide, because whoever reconciles InstaPay is rarely
    /// whoever reconciles the bank account.
    /// </summary>
    public ICollection<PaymentMethodNotificationEmail> NotificationEmails { get; set; } = [];

    /// <summary>
    /// The addresses that asked to hear about this event. Duplicates are collapsed and blanks
    /// dropped, so a caller can hand the result straight to the mailer.
    /// </summary>
    public IReadOnlyList<string> RecipientsFor(WalletRequestStatus status) =>
        NotificationEmails
            .Where(recipient => recipient.WantsNotifiedOf(status))
            .Select(recipient => recipient.Email.Trim())
            .Where(email => email.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Resolves the description for a language, falling back the way names do.</summary>
    public string? ResolveDescription(string? languageCode) =>
        PickLocalized(languageCode, DescriptionAr, DescriptionEn);

    public string? ResolvePublicNote(string? languageCode) =>
        PickLocalized(languageCode, PublicNoteAr, PublicNoteEn);

    public string? ResolvePrivateNote(string? languageCode) =>
        PickLocalized(languageCode, PrivateNoteAr, PrivateNoteEn);

    /// <summary>
    /// True when the method has everything its type demands and may be offered to an applicant.
    /// An external-link method with no link, or a transfer method with no usable account, would
    /// otherwise render as a dead end.
    /// </summary>
    public bool IsUsable(PaymentMethodType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!IsActive || !type.IsActive)
        {
            return false;
        }

        var offered = Accounts.Where(account => account.IsActive).ToList();

        // Everything the type asks for has to be there. Read as a list of requirements rather than
        // a switch on the kind, because a provider may want any combination of them.
        if (type.RequiresExternalUrl && string.IsNullOrWhiteSpace(ExternalUrl))
        {
            return false;
        }

        if (type.RequiresAccountNumber && offered.Count == 0)
        {
            return false;
        }

        // A QR that is not there is worse than none offered: the applicant is told to scan
        // something and finds a blank. Every number on offer must carry its code.
        if (type.RequiresBarcode && !offered.TrueForAll(account => account.HasBarcode))
        {
            return false;
        }

        // An IBAN with no bank against it cannot be paid: the applicant would not know where to
        // send the money.
        if (type.RequiresBank && !offered.TrueForAll(account => account.BankId.HasValue))
        {
            return false;
        }

        // A type that asks for nothing would otherwise be usable while offering the applicant no
        // way to pay at all.
        return type.RequiresExternalUrl
            || type.RequiresAccountNumber
            || type.Kind == PaymentMethodKind.PayPal;
    }

    private static string? PickLocalized(string? languageCode, string? arabic, string? english)
    {
        var isArabic = languageCode?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
        var preferred = isArabic ? arabic : english;
        var fallback = isArabic ? english : arabic;
        return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
    }
}

/// <summary>
/// The kind of payment channel, plus what configuring one requires. This is the lookup the admin
/// edits to add a new provider: switching <see cref="RequiresBarcode"/> on is what makes the
/// barcode uploader appear on every method of that type, in both realms.
/// </summary>
/// <remarks>
/// Two levels, matching how an operator thinks about it: <see cref="Kind"/> is the broad question
/// (they transfer to us, or they pay through PayPal), and the row itself is the provider — Vodafone
/// Cash, InstaPay, a bank. What that provider needs is switched on here, so the applicant's form is
/// generated from these flags and nothing about a provider is hard-coded anywhere.
/// </remarks>
public class PaymentMethodType : LocalizedLookup
{
    public PaymentMethodKind Kind { get; set; }

    /// <summary>
    /// The method configures receiving numbers, and the applicant picks the one they paid.
    /// </summary>
    /// <remarks>
    /// Stored, not derived. These four switches are what an operator sets when adding a provider,
    /// and they combine: a transfer type may want numbers with QR codes, or numbers each naming a
    /// bank, or numbers alongside a link — all of which the old kind-derived version made
    /// impossible to express without a new enum value.
    /// </remarks>
    public bool RequiresAccountNumber { get; set; }

    /// <summary>Every receiving number carries a scannable QR image, as InstaPay does.</summary>
    public bool RequiresBarcode { get; set; }

    /// <summary>
    /// Every receiving row names the bank it belongs to, from the country's bank list, alongside
    /// the IBAN or account number.
    /// </summary>
    public bool RequiresBank { get; set; }

    /// <summary>The method carries an address applicants are sent to in order to pay.</summary>
    public bool RequiresExternalUrl { get; set; }

    /// <summary>
    /// The method talks to a payment provider, so it carries provider credentials.
    /// </summary>
    /// <remarks>
    /// One predicate, read by the editor that offers the credentials and by the command that
    /// stores them. They were separate judgements once, and the two drifted: the form stopped
    /// offering the panel on a link type while the command went on deleting whatever was already
    /// there — which loses a live secret on the next save.
    ///
    /// A link is included because that is what an external payment link is: a redirect into a
    /// gateway, which needs the keys to settle back.
    /// </remarks>
    public bool UsesProviderCredentials =>
        RequiresExternalUrl || Kind == PaymentMethodKind.PayPal;

    /// <summary>
    /// The applicant must attach proof of the transfer before the request is accepted. Stored
    /// rather than derived: it is about what the applicant is asked for, not about what the
    /// channel mechanically needs, so an operator may relax it per type.
    /// </summary>
    public bool RequiresProofDocument { get; set; }

    /// <summary>The applicant must supply the transfer reference, which is unique platform-wide.</summary>
    public bool RequiresReferenceNumber { get; set; }

    /// <summary>Ascending display order wherever types are listed.</summary>
    public int SortOrder { get; set; }

    public ICollection<PaymentMethod> PaymentMethods { get; set; } = [];

    /// <summary>
    /// True when money arrives out of band and a human has to confirm it landed. Everything that
    /// distinguishes the review cycle from a direct payment hangs off this one question.
    /// </summary>
    public bool NeedsApproval => Kind == PaymentMethodKind.Transfer;
}

/// <summary>
/// One receiving account on a transfer method — a Vodafone Cash line, an InstaPay handle, an IBAN.
/// The applicant picks the one they actually paid, so a reviewer reconciles against the right
/// account instead of guessing.
/// </summary>
public class PaymentMethodAccount : Entity
{
    public Guid PaymentMethodId { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>How the account is introduced — "Main line", "Cairo branch".</summary>
    public string LabelAr { get; set; } = string.Empty;

    public string LabelEn { get; set; } = string.Empty;

    /// <summary>The number, handle or IBAN the applicant transfers to.</summary>
    public required string AccountNumber { get; set; }

    /// <summary>Optional name the funds arrive under, shown so the applicant can confirm the payee.</summary>
    public string? AccountHolder { get; set; }

    /// <summary>
    /// Which bank holds this account. Set only on a bank-transfer method — a wallet number
    /// identifies its provider by itself, so there is nothing to name.
    /// </summary>
    public Guid? BankId { get; set; }

    public Bank? Bank { get; set; }

    /// <summary>
    /// Storage-relative path of the scannable barcode/QR image. Never exposed; both realms fetch
    /// the bytes through an endpoint keyed on the account id.
    /// </summary>
    public string? BarcodeStoragePath { get; set; }

    public string? BarcodeContentType { get; set; }

    public string? BarcodeFileName { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public bool HasBarcode => !string.IsNullOrWhiteSpace(BarcodeStoragePath);

    public string ResolveLabel(string? languageCode) =>
        ResolveLabel(LabelAr, LabelEn, AccountNumber, languageCode);

    /// <summary>
    /// The same rule, from the columns alone, for a query that projects them instead of loading
    /// the account.
    /// </summary>
    public static string ResolveLabel(
        string? labelAr,
        string? labelEn,
        string accountNumber,
        string? languageCode)
    {
        var label = LocalizedText.Resolve(labelAr, labelEn, languageCode);

        // An unlabelled account still has to be tellable apart in a radio list.
        return string.IsNullOrWhiteSpace(label) ? accountNumber : label;
    }
}

/// <summary>Join table: countries in which a payment method is offered.</summary>
public class PaymentMethodCountry : Entity
{
    public Guid PaymentMethodId { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }

    public Guid CountryId { get; set; }

    public Country? Country { get; set; }
}

/// <summary>Join table: currencies a payment method accepts.</summary>
public class PaymentMethodCurrency : Entity
{
    public Guid PaymentMethodId { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }

    public Guid CurrencyId { get; set; }

    public Currency? Currency { get; set; }
}

/// <summary>
/// Proof of a transfer, attached by the applicant when raising a deposit request. Kept on its own
/// table rather than reusing <see cref="ApplicationFile"/>, which is scoped to an application and
/// carries review semantics a payment receipt has no use for.
/// </summary>
public class WalletRequestFile : Entity
{
    public Guid WalletRequestId { get; set; }

    public WalletRequest? WalletRequest { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    /// <summary>Storage-relative path. Never exposed; downloads go through an order-scoped endpoint.</summary>
    public required string StoragePath { get; set; }

    public long SizeBytes { get; set; }

    public string? UploadedByName { get; set; }
}

/// <summary>
/// One mailbox told about activity on a payment method: a deposit claimed against it, and an
/// administrator's decision either way.
///
/// The three switches are separate because the audiences differ — a finance mailbox usually wants
/// every event, while a manager may only want to hear about rejections.
/// </summary>
public class PaymentMethodNotificationEmail : Entity
{
    public Guid PaymentMethodId { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }

    public required string Email { get; set; }

    /// <summary>Optional label for whose mailbox this is, shown only in the admin panel.</summary>
    public string? DisplayName { get; set; }

    /// <summary>An applicant claimed a deposit through this method and it is awaiting a decision.</summary>
    public bool NotifyOnSubmitted { get; set; } = true;

    public bool NotifyOnApproved { get; set; } = true;

    public bool NotifyOnRejected { get; set; } = true;

    /// <summary>
    /// Maps a request's new status onto this recipient's switches. A cancelled request is nobody's
    /// news — the applicant called it off before anyone acted — so it notifies no one.
    /// </summary>
    public bool WantsNotifiedOf(WalletRequestStatus status) => status switch
    {
        WalletRequestStatus.Pending => NotifyOnSubmitted,
        WalletRequestStatus.Approved => NotifyOnApproved,
        WalletRequestStatus.Rejected => NotifyOnRejected,
        _ => false,
    };
}
