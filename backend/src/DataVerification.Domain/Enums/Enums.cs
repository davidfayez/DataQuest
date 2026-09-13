namespace DataVerification.Domain.Enums;

/// <summary>
/// Lifecycle of an application: Draft → PendingPayment → Pending → InProgress → (MissedInfo ⇄
/// InProgress) → Success | Failed. A paid-but-unstarted application may instead be Refunded.
/// </summary>
public enum ApplicationStatus
{
    Draft = 0,
    PendingPayment = 1,

    /// <summary>Paid, waiting to be picked up by a reviewer.</summary>
    Pending = 2,
    InProgress = 3,

    /// <summary>A reviewer asked the applicant for more information.</summary>
    MissedInfo = 4,
    Success = 5,
    Failed = 6,
    Refunded = 7,
}

public enum ActorType
{
    System = 0,
    Applicant = 1,
    Admin = 2,
}

/// <summary>Who may read a comment. Internal comments never leave the admin realm.</summary>
public enum CommentVisibility
{
    ForUser = 0,
    Internal = 1,
}

/// <summary>Distinguishes applicant evidence from the deliverables an admin attaches on Success.</summary>
public enum ApplicationFileKind
{
    UserUpload = 0,
    AdminResult = 1,

    /// <summary>
    /// Part of an <see cref="Entities.ApplicationDocument"/> an administrator attached during
    /// review. Separate from <see cref="AdminResult"/> because a document may be internal, so this
    /// kind alone never means "the applicant can see it" — the parent document decides.
    /// </summary>
    AdminDocument = 2,
}

/// <summary>
/// Kinds of ledger line. <see cref="Payment"/> and <see cref="Withdrawal"/> take money out of the
/// wallet; everything else puts money in. Use <see cref="WalletTransactionTypes.IsDebit"/> rather
/// than testing for a single type, so adding a kind cannot silently flip a balance's sign.
/// </summary>
public enum WalletTransactionType
{
    TopUp = 0,
    Payment = 1,
    Refund = 2,

    /// <summary>Funds held the moment a payout is requested, so they cannot also be spent.</summary>
    Withdrawal = 3,

    /// <summary>Returns a held payout when the request is rejected or withdrawn.</summary>
    WithdrawalReversal = 4,
}

public static class WalletTransactionTypes
{
    /// <summary>True when the entry reduces the balance.</summary>
    public static bool IsDebit(this WalletTransactionType type) =>
        type is WalletTransactionType.Payment or WalletTransactionType.Withdrawal;
}

/// <summary>Which way money moves in a <see cref="Entities.WalletRequest"/>.</summary>
public enum WalletRequestType
{
    /// <summary>The applicant is adding funds; approving credits the wallet.</summary>
    Deposit = 0,

    /// <summary>The applicant is taking funds out; the amount is held until a decision is made.</summary>
    Withdrawal = 1,
}

/// <summary>
/// Lifecycle of a wallet request: Pending → Approved | Rejected | Cancelled. Only the applicant
/// cancels; only an administrator approves or rejects. Every transition is terminal.
/// </summary>
public enum WalletRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}

/// <summary>An application carries its holder's name in both Arabic and English.</summary>
public enum NameLanguageType
{
    Arabic = 0,
    English = 1,
}

/// <summary>
/// What a "how to use the platform" entry carries. An entry is one or the other.
///
/// Serialised by name rather than by number: the admin editor reads and writes "Video"/"Image",
/// and the responses already carry the name, so the contract is symmetric. The platform's older
/// enums stay numeric — this converter is deliberately scoped to this type alone.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ToolResourceKind
{
    /// <summary>A link to a video hosted elsewhere (YouTube, Vimeo, or any URL).</summary>
    Video = 0,

    /// <summary>An image uploaded to the platform's own storage.</summary>
    Image = 1,
}

/// <summary>The input an administrator asks the applicant to fill in beside a required document.</summary>
public enum RequiredFieldType
{
    Text = 0,
    Number = 1,
    Date = 2,
    Dropdown = 3,
}

/// <summary>Constrains a date field to the past or the future, on top of any explicit bounds.</summary>
public enum RequiredFieldDateRule
{
    Any = 0,
    PastOnly = 1,
    FutureOnly = 2,
}

/// <summary>
/// How a payment method moves money, and therefore what configuring one requires. The kind is the
/// single switch: it decides whether the method carries a link or receiving numbers, whether those
/// numbers also carry a scannable code, and whether an approval cycle is involved at all.
/// </summary>
/// <summary>
/// How money reaches the platform. Deliberately only two: the broad question of who moves it.
/// </summary>
/// <remarks>
/// What a channel <em>needs configuring</em> — receiving numbers, QR codes, a bank against each
/// row, a link to send the applicant to — is no longer inferred from this. It is switched on per
/// <see cref="PaymentMethodType"/>, because those needs vary by provider and combine freely: one
/// transfer provider wants numbers and QR codes, another wants numbers and a bank name, a third
/// wants a link as well. Deriving them from the kind meant a new provider needed a new enum value
/// and a deployment.
/// </remarks>
public enum PaymentMethodKind
{
    /// <summary>
    /// The applicant sends money to us — a wallet number, an InstaPay handle, a bank account — and
    /// tells us they did. An administrator confirms it before the wallet is credited.
    ///
    /// Value 1 is deliberate: it is what the old WalletTransfer kind stored, and the other transfer
    /// kinds were folded into it, so rows written before the change keep their meaning.
    /// </summary>
    Transfer = 1,

    /// <summary>
    /// The applicant pays through PayPal, which settles with us directly. Configured with the
    /// provider credentials on the method rather than with numbers to pay into.
    ///
    /// Value 4 rather than reusing a retired one: nothing has ever stored it, so it cannot be
    /// confused with a row written under the old scheme.
    /// </summary>
    PayPal = 4,
}

/// <summary>
/// The kinds of outgoing email whose sender and blind-copy list an administrator configures.
///
/// A closed set rather than a free-text key: each value is wired to a place in the code that sends
/// that email, so an entry nothing sends would be configuration that quietly does nothing.
/// </summary>
public enum EmailType
{
    /// <summary>The credentials email sent when an order is created.</summary>
    OrderCreated = 0,

    /// <summary>The new password sent when an applicant asks for one.</summary>
    ForgotPassword = 1,

    /// <summary>Enquiries sent from the public "contact us" form.</summary>
    ContactUs = 2,
}

/// <summary>
/// Where a support ticket stands. Every ticket opens at <see cref="Pending"/>: the platform has it,
/// nobody has picked it up yet.
/// </summary>
public enum TicketStatus
{
    /// <summary>Received and waiting for someone to pick it up. The status every ticket opens at.</summary>
    Pending = 0,

    /// <summary>Someone is working on it.</summary>
    InProgress = 1,

    /// <summary>An answer has gone back to the sender; their reply may still be to come.</summary>
    Answered = 2,

    /// <summary>Dealt with. Kept for the record rather than deleted.</summary>
    Closed = 3,
}

/// <summary>
/// Which of a payment provider's environments a method talks to.
///
/// Worth storing rather than inferring from the URLs: it is the one setting an operator must be
/// able to see at a glance, because getting it wrong means either testing against real money or
/// taking real payments into a sandbox that will never settle.
/// </summary>
public enum PaymentIntegrationMode
{
    /// <summary>The provider's test environment. The default for anything newly configured.</summary>
    Sandbox = 0,

    /// <summary>The provider's production environment. Real money.</summary>
    Live = 1,
}

/// <summary>
/// The two groups the public contact page shows: the people who represent the organisation in a
/// country, and the organisation itself.
/// </summary>
public enum ContactEntryKind
{
    /// <summary>A representative in a country. Listed by country, with how to reach them.</summary>
    AuthorizedAgent = 0,

    /// <summary>The organisation's own office — its address, mailbox and number.</summary>
    Administration = 1,
}

/// <summary>Which row of the public footer a <c>SocialLink</c> belongs to.</summary>
public enum SocialLinkPlacement
{
    /// <summary>"Follow us" — profiles and channels to subscribe to.</summary>
    FollowUs = 0,

    /// <summary>"Message us" — shortcuts that open a conversation with the organisation.</summary>
    MessageUs = 1,
}

/// <summary>
/// The platform a footer link points at.
///
/// A closed list rather than free text: the site draws each platform's own icon and brand colour
/// from this, so a value it does not recognise would render as a bare label. Adding a platform is
/// a code change in both realms — deliberately, because the artwork is half of what is being added.
/// </summary>
public enum SocialPlatform
{
    Facebook = 0,
    Instagram = 1,
    LinkedIn = 2,
    YouTube = 3,
    Telegram = 4,
    Vk = 5,
    X = 6,
    TikTok = 7,
    WhatsApp = 8,
    Messenger = 9,
}

/// <summary>How a landing section arranges its cards.</summary>
public enum LandingSectionLayout
{
    /// <summary>All cards at once, wrapping onto as many rows as they need.</summary>
    Grid = 0,

    /// <summary>One row that slides, with controls — for a set too long to show at once.</summary>
    Carousel = 1,
}

/// <summary>Which column of the site footer a link belongs to.</summary>
public enum FooterColumn
{
    Explore = 0,
    Account = 1,
    Organisation = 2,
}

/// <summary>
/// Who sees a footer link.
///
/// The Account column changes with the visitor's session — "Register" and "Sign in" for a stranger,
/// "My applications" and "Wallet" for someone signed in. Making the column editable without this
/// would have flattened it into one list and shown everyone both halves.
/// </summary>
public enum FooterLinkVisibility
{
    Everyone = 0,
    SignedIn = 1,
    SignedOut = 2,
}

/// <summary>
/// Who sees a header entry.
///
/// The same three cases as the footer, kept separate so the header can gain a case of its own
/// without moving the footer's stored numbers underneath it.
/// </summary>
public enum HeaderLinkVisibility
{
    Everyone = 0,
    SignedIn = 1,
    SignedOut = 2,
}

/// <summary>
/// How a country is marked on the landing page's coverage map.
///
/// A spot is the map pin most people expect; a flag says which country it is without the reader
/// having to know the shape. Which reads better depends on the country: a flag is worth the space
/// for a country somebody is looking for by name, a spot is enough where the shape already says it.
/// </summary>
public enum CoverageMarker
{
    Spot = 0,
    Flag = 1,
}

/// <summary>What became of a password offered by "forgot password".</summary>
public enum PasswordResetOutcome
{
    /// <summary>Emailed and still good — nobody has signed in with it yet.</summary>
    Pending = 0,

    /// <summary>Signed in with, and now the order's real password.</summary>
    Used = 1,

    /// <summary>Its window closed without being used; the old password still stands.</summary>
    Expired = 2,

    /// <summary>The order number matched nothing, so no password was issued and none was sent.</summary>
    UnknownOrder = 3,

    /// <summary>Replaced by a later request before it was used.</summary>
    Superseded = 4,
}
