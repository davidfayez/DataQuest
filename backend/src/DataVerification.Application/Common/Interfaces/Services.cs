using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;

namespace DataVerification.Application.Common.Interfaces;

/// <summary>Abstracts the clock so time-dependent rules can be tested deterministically.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

/// <summary>
/// Generates the applicant's order number: the owning client's code followed by nine random
/// digits, e.g. <c>NEN408125993</c>.
/// </summary>
public interface IOrderNumberGenerator
{
    /// <summary>Returns a number that is not already taken, retrying on the (rare) collision.</summary>
    Task<string> GenerateUniqueAsync(string clientCode, CancellationToken cancellationToken = default);
}

/// <summary>Generates the applicant's one-time 8-character password.</summary>
public interface IPasswordGenerator
{
    /// <summary>Cryptographically random, with at least one upper, one lower and one digit.</summary>
    string Generate(int length = 8);
}

/// <summary>Hashes and verifies credentials for both identity realms.</summary>
public interface IPasswordHashingService
{
    string Hash(string password);

    /// <summary>Constant-time verification; returns false rather than throwing on a bad hash.</summary>
    bool Verify(string hash, string password);
}

/// <summary>
/// Reversible protection for secrets the platform must be able to read back: the applicant's
/// generated password (revealed to an operator holding <c>Orders.ViewPassword</c>) and the SendGrid
/// API key managed from the admin panel. Deliberately separate from
/// <see cref="IPasswordHashingService"/>: the hash stays the only thing sign-in trusts, and this
/// ciphertext is never consulted during authentication.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// False when no encryption key is configured. Nothing is then stored and nothing can be read
    /// back, which keeps the platform usable without the key present.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>Encrypts the plaintext, or returns <c>null</c> when protection is not configured.</summary>
    string? Protect(string plaintext);

    /// <summary>
    /// Decrypts a value produced by <see cref="Protect"/>. Returns <c>null</c> rather than throwing
    /// when the value is absent, malformed, or was written under a different key.
    /// </summary>
    string? Unprotect(string? protectedValue);
}

/// <summary>
/// The SendGrid API key as managed from the admin panel. Stored encrypted in <c>SiteSettings</c>
/// so it can be rotated without a redeploy; the value in configuration remains the fallback.
/// </summary>
public interface IEmailSettingsStore
{
    /// <summary>
    /// The key the sender should use, and where it came from. Returns a null key when neither the
    /// database nor configuration has one.
    /// </summary>
    Task<SendGridKey> GetSendGridApiKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the key encrypted, or clears the stored value when <paramref name="apiKey"/> is null
    /// or blank — after which the configured fallback (if any) applies again.
    /// </summary>
    Task SetSendGridApiKeyAsync(string? apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The sender an email falls back to when nothing overrides it, and which transport would
    /// carry it. Read by the admin page so "why did no email arrive" has an answer on screen
    /// rather than only in a server log.
    /// </summary>
    /// <remarks>
    /// Configuration only. Use <see cref="GetDefaultSenderAsync"/> for the address that actually
    /// applies — an operator can override this one from the admin panel.
    /// </remarks>
    EmailTransportDefaults GetTransportDefaults();

    /// <summary>
    /// The sender every email falls back to when its own kind sets no override, and where that
    /// address came from.
    /// </summary>
    Task<EmailDefaultSender> GetDefaultSenderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the platform-wide default sender, or clears it when the address is null or blank —
    /// after which the configured transport default applies again.
    /// </summary>
    Task SetDefaultSenderAsync(
        string? fromAddress,
        string? fromName,
        CancellationToken cancellationToken = default);
}

/// <param name="FromAddress">The address in force — stored where one is set, configured otherwise.</param>
/// <param name="Source">Which of the two it came from, so the page can say so.</param>
public readonly record struct EmailDefaultSender(
    string FromAddress,
    string? FromName,
    EmailDefaultSenderSource Source);

public enum EmailDefaultSenderSource
{
    /// <summary>Nothing is stored; the transport's configured sender applies.</summary>
    Configuration = 0,

    /// <summary>Set from the admin panel. Takes precedence.</summary>
    Database = 1,
}

/// <param name="Value">The usable key, or <c>null</c> when none is available.</param>
/// <param name="Source">Where it came from, for the settings page to show.</param>
public readonly record struct SendGridKey(string? Value, SendGridKeySource Source);

public enum SendGridKeySource
{
    /// <summary>Neither the database nor configuration holds a key; SendGrid cannot send.</summary>
    None = 0,

    /// <summary>Set from the admin panel. Takes precedence.</summary>
    Database = 1,

    /// <summary>Falling back to <c>SendGrid:ApiKey</c> from configuration.</summary>
    Configuration = 2,

    /// <summary>A key is stored but cannot be decrypted with the configured encryption key.</summary>
    Unreadable = 3,
}

/// <summary>Issues the JWTs for the applicant and admin realms.</summary>
public interface IJwtTokenService
{
    AccessToken CreateApplicantToken(Order order);

    AccessToken CreateAdminToken(AdminUser adminUser, IReadOnlySet<string> permissions);

    /// <summary>
    /// Mints a long-lived refresh token for a session. It is delivered only as an httpOnly cookie
    /// and carries a distinct audience, so it can never be accepted as a bearer access token.
    /// </summary>
    IssuedRefreshToken CreateRefreshToken(Guid subjectId, string realm);

    /// <summary>
    /// Validates a refresh token, returning its subject and realm, or <c>null</c> when it is
    /// invalid, expired or not a refresh token.
    /// </summary>
    RefreshTokenInfo? ValidateRefreshToken(string token);
}

/// <param name="Token">The signed JWT.</param>
/// <param name="ExpiresAtUtc">Absolute expiry, so clients can refresh proactively.</param>
public readonly record struct AccessToken(string Token, DateTime ExpiresAtUtc);

/// <param name="Token">The signed refresh JWT.</param>
/// <param name="ExpiresAtUtc">Absolute expiry, used as the cookie's max-age.</param>
public readonly record struct IssuedRefreshToken(string Token, DateTime ExpiresAtUtc);

/// <param name="SubjectId">The admin user id or order id the refresh token was issued for.</param>
/// <param name="Realm">The realm (<c>Admin</c> or <c>Applicant</c>) the token belongs to.</param>
public readonly record struct RefreshTokenInfo(Guid SubjectId, string Realm);

/// <summary>The caller behind the current request, resolved from the JWT.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Set when an applicant is calling; scopes every applicant query to their order.</summary>
    Guid? OrderId { get; }

    /// <summary>Set when an admin is calling.</summary>
    Guid? AdminUserId { get; }

    string? DisplayName { get; }

    IReadOnlySet<string> Permissions { get; }

    /// <summary>Locale negotiated from Accept-Language; drives localized lookup names.</summary>
    string LanguageCode { get; }

    string? IpAddress { get; }

    /// <summary>The browser's own description of itself, for the security log. Null outside a request.</summary>
    string? UserAgent { get; }

    /// <summary>The raw Accept-Language header, which is not always the negotiated language.</summary>
    string? AcceptLanguage { get; }

    /// <summary>Projects the caller into the domain's <see cref="Actor"/> for history and audit.</summary>
    Actor ToActor();
}

/// <summary>
/// Applies the administrator's per-kind sender and blind-copy list to a rendered email.
/// </summary>
/// <remarks>
/// Separate from the renderers, which decide what an email says, and from the senders, which
/// decide how it travels: this decides who it comes from and who else sees it, and that is
/// configuration rather than either.
/// </remarks>
public interface IEmailRouting
{
    /// <summary>
    /// Returns the message with the configured sender and blind copies applied. An unconfigured
    /// kind comes back unchanged, so the platform default still sends it.
    /// </summary>
    Task<EmailMessage> ApplyAsync(
        EmailType type,
        EmailMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>Sends transactional email. Implementations must not throw on delivery failure.</summary>
public interface IEmailSender
{
    /// <summary>
    /// Hands the message to the transport.
    /// </summary>
    /// <remarks>
    /// Never throws on a delivery failure — the thing that produced the email has usually already
    /// committed, and losing an order to a bounced notification would be far worse than losing the
    /// notification. The result says what actually happened, so a caller that <em>is</em> asking on
    /// someone's behalf, like the settings page's test send, can report it instead of guessing.
    /// </remarks>
    Task<EmailDeliveryResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>What became of one message.</summary>
/// <param name="Status">
/// A stable code the UI can translate: <c>Delivered</c>, <c>Logged</c> (no transport configured,
/// so it was written to the log instead), <c>NoApiKey</c>, <c>Rejected</c> (the provider refused
/// it) or <c>Failed</c>.
/// </param>
/// <param name="Detail">
/// The provider's own words where there are any. Shown to an administrator diagnosing a send, so
/// it must never carry the message body or a credential.
/// </param>
/// <param name="Transport">
/// <c>SendGrid</c>, <c>Smtp</c>, or <c>Log</c> when neither is enabled — in which case mail is
/// written to the server log and never leaves the machine.
/// </param>
public sealed record EmailTransportDefaults(string Transport, string FromAddress, string? FromName);

public sealed record EmailDeliveryResult(bool Delivered, string Status, string? Detail = null)
{
    public static EmailDeliveryResult Sent() => new(true, "Delivered");

    public static EmailDeliveryResult Logged() => new(true, "Logged");

    public static EmailDeliveryResult NoApiKey(string source) =>
        new(false, "NoApiKey", $"No usable API key ({source}).");

    public static EmailDeliveryResult Rejected(string detail) => new(false, "Rejected", detail);

    public static EmailDeliveryResult Failed(string detail) => new(false, "Failed", detail);
}

/// <param name="FromAddress">
/// Overrides the platform's configured sender. Null keeps the default, so a renderer that does not
/// care about the sender says nothing about it.
/// </param>
/// <param name="Bcc">Mailboxes copied invisibly to the recipient. Null or empty copies nobody.</param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string PlainTextBody,
    string? FromAddress = null,
    string? FromName = null,
    IReadOnlyList<string>? Bcc = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>
/// A file travelling with an email.
/// </summary>
/// <remarks>
/// Held in memory as bytes rather than as a stream: a message may be handed to a provider, retried,
/// or logged, and a stream that has already been read once cannot survive any of that.
/// </remarks>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content)
{
    /// <summary>
    /// The ceiling on everything attached to one email. Providers reject oversized messages
    /// outright, and a refusal the sender never sees is worse than asking support to split a reply.
    /// </summary>
    public const long MaxTotalBytes = 10 * 1024 * 1024;
}

/// <summary>Builds the localized bodies for the platform's transactional emails.</summary>
public interface IEmailTemplateRenderer
{
    EmailMessage RenderOrderCredentials(
        string to,
        string orderNumber,
        string password,
        string languageCode);

    /// <summary>
    /// The new password issued to an applicant who asked for one, sent to the address on their
    /// order. Distinct from the administrator's reset, which sends a link rather than a password.
    /// </summary>
    /// <param name="validityMinutes">
    /// How long the password is good for. Stated in the email because the password only becomes the
    /// account's once it is used — the reader has to know both halves of that.
    /// </param>
    EmailMessage RenderOrderPasswordReset(
        string to,
        string orderNumber,
        string password,
        string languageCode,
        int validityMinutes);

    /// <summary>The password-reset email for a back-office administrator.</summary>
    EmailMessage RenderAdminPasswordReset(
        string to,
        string fullName,
        string resetUrl,
        string languageCode);

    /// <summary>
    /// Notifies the applicant that a reviewer moved their application to a new status. When
    /// <paramref name="messageToApplicant"/> is provided it is shown in the body; the reviewer's
    /// internal note is never passed here.
    /// </summary>
    EmailMessage RenderApplicationStatusChanged(
        string to,
        string applicationNumber,
        ApplicationStatus status,
        string? messageToApplicant,
        string languageCode);

    /// <summary>
    /// Tells an operator that money moved through one of their payment methods: a deposit claimed
    /// against it, or an administrator's decision either way.
    ///
    /// Operator-facing, not applicant-facing — it names the order and the reviewer, which is
    /// exactly what the applicant's own mail must never contain.
    /// </summary>
    EmailMessage RenderPaymentNotification(
        string to,
        WalletRequestStatus status,
        string orderNumber,
        string methodName,
        decimal amount,
        string currencyCode,
        string? referenceNumber,
        string? decidedBy,
        string? reviewerNote,
        string languageCode);

    /// <summary>
    /// Confirms to whoever wrote in through the contact form that their enquiry landed, and gives
    /// them the reference to quote. The reference is in the subject line: it is the only handle
    /// they have on the conversation, and a mailbox is searched by subject.
    /// </summary>
    EmailMessage RenderTicketCreated(
        string to,
        string ticketNumber,
        string name,
        string categoryName,
        string subject,
        string languageCode);

    /// <summary>
    /// Sends an administrator's answer to the person who raised a ticket.
    /// </summary>
    /// <remarks>
    /// Only ever called with a public action. Nothing in this signature can carry an internal note:
    /// the caller passes the body it means to publish, and the domain refuses to record a send for
    /// an action that may not leave the platform.
    /// </remarks>
    EmailMessage RenderTicketReply(
        string to,
        string ticketNumber,
        string name,
        string subject,
        string? body,
        IReadOnlyList<TicketReplyAttachment> attachments,
        string languageCode);
}

/// <summary>
/// A document named in a reply email. Carries the title and description support wrote, plus how
/// many files it holds — never the files themselves, which stay behind the download endpoint.
/// </summary>
public sealed record TicketReplyAttachment(string Title, string? Description, int FileCount);

/// <summary>Writes the audit trail. Called by the pipeline, not by individual handlers.</summary>
public interface IAuditLogger
{
    Task LogAsync(
        string action,
        string entityType,
        Guid? entityId,
        object? data = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Answers a visitor's question from the platform's own guides.
///
/// The provider is an implementation detail and the key never leaves the server: the site posts a
/// question to this API and gets prose back, so a key configured in the admin panel is not exposed
/// to every browser that loads the page.
/// </summary>
public interface IAiAssistant
{
    /// <summary>False until an API key has been set, which the site reports rather than failing.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers <paramref name="question"/> in <paramref name="language"/>, using
    /// <paramref name="context"/> — the platform's own guides — as the material to answer from,
    /// and the open web for anything the guides do not cover.
    /// </summary>
    Task<AiReply> AskAsync(
        string question,
        string? language,
        string context,
        CancellationToken cancellationToken = default);
}

/// <param name="Text">The prose, or empty when the provider had nothing to say.</param>
/// <param name="WebSources">
/// The pages the answer drew on, when it went to the web. Empty when it answered from the guides
/// alone — which is the common case and not a failure.
/// </param>
public sealed record AiReply(string Text, IReadOnlyList<AiWebSource> WebSources);

public sealed record AiWebSource(string Title, string Url);

/// <summary>Reads and writes the assistant's provider settings, stored with the key encrypted.</summary>
public interface IAiSettingsStore
{
    Task<AiSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>The stored key, decrypted, or null when none is stored or it cannot be read.</summary>
    Task<string?> GetApiKeyAsync(CancellationToken cancellationToken = default);

    /// <param name="clearApiKey">Removes the stored key; a blank <paramref name="apiKey"/> keeps it.</param>
    Task SaveAsync(
        AiSettings settings,
        string? apiKey,
        bool clearApiKey = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What the admin panel shows and stores. The key itself is never returned — only whether one is
/// present — so opening the settings page cannot leak it.
/// </summary>
public sealed record AiSettings(bool IsEnabled, string Model, bool HasApiKey);

/// <summary>
/// Where an IP address geolocates to, for the security log.
///
/// Best-effort by nature: the lookup is a third party, addresses on a private network resolve to
/// nothing, and a VPN resolves to the wrong place confidently. A caller records what comes back and
/// carries on when nothing does — a password reset must never fail because a geolocation service
/// was slow.
/// </summary>
public interface IIpGeolocator
{
    Task<IpLocation?> LocateAsync(string? ipAddress, CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything a geolocation provider will say about an address.
///
/// All of it is optional. Providers differ in what they return, a free tier returns less than a
/// paid one, and any field may simply be absent for a given address — so a reader treats a null as
/// "not known" rather than "not true".
/// </summary>
public sealed record IpLocation
{
    public string? Country { get; init; }

    public string? CountryCode { get; init; }

    public string? Continent { get; init; }

    public string? ContinentCode { get; init; }

    /// <summary>The region's code, such as <c>TAS</c>.</summary>
    public string? Region { get; init; }

    /// <summary>The region's name, such as <c>Tashkent</c>.</summary>
    public string? RegionName { get; init; }

    public string? City { get; init; }

    public string? District { get; init; }

    public string? PostalCode { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? TimeZone { get; init; }

    /// <summary>The zone's offset from UTC in seconds, which is how ip-api reports it.</summary>
    public int? UtcOffsetSeconds { get; init; }

    public string? Currency { get; init; }

    /// <summary>Who sells the connection.</summary>
    public string? Isp { get; init; }

    /// <summary>Who the address is registered to, which is often not the ISP.</summary>
    public string? Organisation { get; init; }

    /// <summary>The autonomous system, such as <c>AS15169 Google LLC</c>.</summary>
    public string? AutonomousSystem { get; init; }

    /// <summary>
    /// The reverse-DNS name of the address — the closest thing to a machine name that exists at
    /// this distance, and usually the ISP's name for the line rather than the visitor's own.
    /// </summary>
    public string? ReverseDns { get; init; }

    /// <summary>A mobile carrier's network.</summary>
    public bool? IsMobileNetwork { get; init; }

    /// <summary>A known proxy, VPN or Tor exit — the flag worth reading first on a suspect request.</summary>
    public bool? IsProxy { get; init; }

    /// <summary>A data centre rather than a home or office connection.</summary>
    public bool? IsHosting { get; init; }
}

/// <summary>Reads a user agent string into the three things a reader of the log wants.</summary>
public interface IUserAgentReader
{
    UserAgentDetails Read(string? userAgent);
}

/// <param name="DeviceType"><c>Desktop</c>, <c>Mobile</c> or <c>Tablet</c>.</param>
public sealed record UserAgentDetails(string? Browser, string? OperatingSystem, string? DeviceType);

/// <summary>
/// How long a password emailed by "forgot password" stays good for, managed from the admin panel.
/// </summary>
public interface IPasswordResetSettingsStore
{
    Task<int> GetValidityMinutesAsync(CancellationToken cancellationToken = default);

    Task SetValidityMinutesAsync(int minutes, CancellationToken cancellationToken = default);
}
