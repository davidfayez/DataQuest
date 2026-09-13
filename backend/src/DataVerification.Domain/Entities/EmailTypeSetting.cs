using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// Who one kind of outgoing email comes from, and who else silently receives a copy.
///
/// Per kind rather than one platform-wide setting: the mailbox that should answer a contact
/// enquiry is rarely the one that should appear on a credentials email, and the people who need
/// to watch registrations are rarely the people who need to watch password resets.
/// </summary>
public class EmailTypeSetting : Entity
{
    public EmailType Type { get; set; }

    /// <summary>
    /// The address the email appears to come from. Null falls back to the platform default in
    /// configuration, so an unconfigured type keeps working rather than failing to send.
    /// </summary>
    public string? FromAddress { get; set; }

    /// <summary>The display name beside the address. Null falls back with the address.</summary>
    public string? FromName { get; set; }

    /// <summary>Mailboxes copied on every email of this kind, invisibly to the recipient.</summary>
    public ICollection<EmailBccRecipient> BccRecipients { get; set; } = [];

    /// <summary>
    /// The addresses to copy, de-duplicated and trimmed, ready to hand to the mailer. Blank rows
    /// are dropped rather than sent as empty recipients, which some providers reject outright.
    /// </summary>
    public IReadOnlyList<string> BccAddresses() =>
        BccRecipients
            .Select(recipient => recipient.Email.Trim())
            .Where(email => email.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>One mailbox blind-copied on a kind of outgoing email.</summary>
public class EmailBccRecipient : Entity
{
    public Guid EmailTypeSettingId { get; set; }

    public EmailTypeSetting? EmailTypeSetting { get; set; }

    public required string Email { get; set; }

    /// <summary>Optional label for whose mailbox this is, shown only in the admin panel.</summary>
    public string? DisplayName { get; set; }
}
