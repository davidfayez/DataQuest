using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One channel in the public footer: a profile under "Follow us", or a chat shortcut under
/// "Message us".
///
/// Both rows are the same shape — a platform and an address to reach it at — so they share a table
/// and are told apart by <see cref="Placement"/>. That also lets the same platform appear in both,
/// which Telegram does: a channel to follow, and a direct message.
///
/// Only the platform and the address are stored. The icon and the brand colour belong to the site
/// that draws them, so adding a channel is a matter of picking from a list rather than of pasting a
/// hex code an operator would have to look up.
/// </summary>
public class SocialLink : Entity
{
    public SocialLinkPlacement Placement { get; set; }

    public SocialPlatform Platform { get; set; }

    /// <summary>
    /// Where the link goes. Held exactly as typed: a WhatsApp shortcut and a Facebook page have
    /// nothing in common but being an address, and composing one from a phone number is the kind
    /// of cleverness that breaks the first time a provider changes its URL shape.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>Ascending display order within its row of the footer.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only active links reach the public site.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True when the link is complete enough to publish. An entry with no address would render as
    /// an icon that goes nowhere, which is worse than not offering the channel at all.
    /// </summary>
    public bool IsShowable => IsActive && !string.IsNullOrWhiteSpace(Url);
}
