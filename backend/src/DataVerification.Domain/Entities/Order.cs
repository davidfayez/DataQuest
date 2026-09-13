using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// An applicant's account, created from an email address alone. The order owns the generated
/// credentials, the verification country/currency chosen at setup, one wallet, and many
/// applications.
/// </summary>
public class Order : Entity
{
    public Guid ClientId { get; set; }

    public Client? Client { get; set; }

    public required string Email { get; set; }

    /// <summary>12 characters from an unambiguous charset. Unique, and the applicant's login name.</summary>
    public required string OrderNumber { get; set; }

    /// <summary>Hash of the generated 8-character password. This is the only credential sign-in trusts.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>
    /// The same password under reversible encryption, so an operator holding
    /// <c>Orders.ViewPassword</c> can be shown it again. Null for orders registered before this
    /// existed, and null when no encryption key is configured — recovery is best-effort, the hash
    /// is not.
    /// </summary>
    public string? PasswordSecret { get; set; }

    /// <summary>
    /// Hash of a password emailed by "forgot password" that has not been used yet.
    ///
    /// A reset does not take the old password away. Until someone signs in with the new one it is
    /// only an offer, and the password already in the applicant's hands keeps working — so a reset
    /// requested by somebody else, or by the applicant and then thought better of, locks nobody
    /// out. Signing in with it is what commits it; the offer lapses on its own otherwise.
    /// </summary>
    public string? PendingPasswordHash { get; set; }

    /// <summary>The pending password under reversible encryption, kept in step with its hash.</summary>
    public string? PendingPasswordSecret { get; set; }

    /// <summary>When the offer lapses. Null when none is outstanding.</summary>
    public DateTime? PendingPasswordExpiresAtUtc { get; set; }

    /// <summary>Locale the applicant registered in; drives the language of outgoing email.</summary>
    public string LanguageCode { get; set; } = "en";

    public Guid? VerificationCountryId { get; set; }

    public Country? VerificationCountry { get; set; }

    public Guid? CurrencyId { get; set; }

    public Currency? Currency { get; set; }

    /// <summary>
    /// Who to contact about this order. Captured at setup alongside the country and currency;
    /// null on orders created before contact details were collected.
    /// </summary>
    public string? ContactPersonName { get; set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 code of the country the contact phone belongs to, e.g. <c>EG</c>. Kept
    /// beside the dial code because a dial code alone is ambiguous — <c>+1</c> is both US and CA —
    /// and the flag shown next to the number is drawn from this.
    /// </summary>
    public string? ContactPersonPhoneCountry { get; set; }

    /// <summary>International dial prefix of the contact phone, e.g. <c>+20</c>.</summary>
    public string? ContactPersonPhoneCode { get; set; }

    /// <summary>The contact phone's national part, digits only, without the dial prefix.</summary>
    public string? ContactPersonPhoneNumber { get; set; }

    /// <summary>Dial code and national number joined, or null when no phone was captured.</summary>
    public string? ContactPersonPhone =>
        string.IsNullOrWhiteSpace(ContactPersonPhoneNumber)
            ? null
            : $"{ContactPersonPhoneCode}{ContactPersonPhoneNumber}";

    public Wallet? Wallet { get; set; }

    public ICollection<VerificationApplication> Applications { get; set; } = [];

    public int FailedLoginAttempts { get; set; }

    /// <summary>Set when repeated failed logins trigger a temporary lockout.</summary>
    public DateTime? LockoutEndsAtUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>True once the verification country and currency have been chosen.</summary>
    public bool IsSetupComplete => VerificationCountryId.HasValue && CurrencyId.HasValue;

    public bool IsLockedOut(DateTime utcNow) => LockoutEndsAtUtc.HasValue && LockoutEndsAtUtc > utcNow;

    /// <summary>
    /// Locks the order's country and currency, records who to contact about it, and opens its
    /// wallet. Setup runs once: the wallet currency must stay fixed, otherwise an existing ledger
    /// would mix denominations.
    /// </summary>
    public Wallet CompleteSetup(
        Country country,
        Currency currency,
        string contactPersonName,
        string contactPhoneCountry,
        string contactPhoneCode,
        string contactPhoneNumber)
    {
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(currency);

        if (IsSetupComplete)
        {
            throw new DomainException(
                "order.setup_already_completed",
                "The verification country and currency have already been set for this order.");
        }

        if (!country.SupportsCurrency(currency.Id))
        {
            throw new DomainException(
                "order.currency_not_available_in_country",
                $"Currency '{currency.Code}' is not available for country '{country.Code}'.");
        }

        VerificationCountryId = country.Id;
        VerificationCountry = country;
        CurrencyId = currency.Id;
        Currency = currency;
        ContactPersonName = contactPersonName.Trim();
        ContactPersonPhoneCountry = contactPhoneCountry.Trim().ToUpperInvariant();
        ContactPersonPhoneCode = contactPhoneCode.Trim();
        // The trunk zero is dropped: it is only dialled from inside the country, and this number is
        // stored beside a calling code.
        ContactPersonPhoneNumber = NationalPhoneNumber.Normalise(contactPhoneNumber);
        UpdatedAtUtc = DateTime.UtcNow;

        Wallet = new Wallet { OrderId = Id, CurrencyId = currency.Id };
        return Wallet;
    }

    /// <summary>True while a password issued by "forgot password" is still waiting to be used.</summary>
    public bool HasPendingPassword(DateTime utcNow) =>
        PendingPasswordHash is not null
        && PendingPasswordExpiresAtUtc.HasValue
        && PendingPasswordExpiresAtUtc > utcNow;

    /// <summary>
    /// Offers a new password without taking the current one away.
    ///
    /// Any offer already outstanding is replaced: asking again should send one password that works,
    /// not leave a trail of them valid at once.
    /// </summary>
    public void OfferPassword(string hash, string? secret, DateTime expiresAtUtc, DateTime utcNow)
    {
        PendingPasswordHash = hash;
        PendingPasswordSecret = secret;
        PendingPasswordExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>
    /// Makes the pending password the real one, which is what signing in with it means.
    ///
    /// The lockout goes with it: reaching the mailbox is proof enough, and leaving it would lock
    /// the applicant out of the credentials they were just sent. It is cleared here rather than
    /// when the reset was requested, so asking for a password cannot be used to clear somebody
    /// else's lockout.
    /// </summary>
    public void CommitPendingPassword(DateTime utcNow)
    {
        PasswordHash = PendingPasswordHash ?? PasswordHash;
        PasswordSecret = PendingPasswordSecret;

        ClearPendingPassword();
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Drops any outstanding offer, leaving the current password in place.</summary>
    public void ClearPendingPassword()
    {
        PendingPasswordHash = null;
        PendingPasswordSecret = null;
        PendingPasswordExpiresAtUtc = null;
    }

    public void RegisterSuccessfulLogin(DateTime utcNow)
    {
        FailedLoginAttempts = 0;
        LockoutEndsAtUtc = null;
        LastLoginAtUtc = utcNow;
    }

    /// <summary>
    /// Records a failed sign-in and locks the order once the threshold is reached, so credential
    /// stuffing against a guessable order number is not viable.
    /// </summary>
    public void RegisterFailedLogin(DateTime utcNow, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
        {
            LockoutEndsAtUtc = utcNow.Add(lockoutDuration);
            FailedLoginAttempts = 0;
        }
    }
}
