using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// The credentials and endpoints a payment method uses to talk to an online payment provider.
///
/// Held apart from <see cref="PaymentMethod"/> rather than as more columns on it: this row holds
/// secrets, and keeping them in their own table means the queries that build pickers, lists and the
/// applicant's payment page never load them at all.
/// </summary>
/// <remarks>
/// The three secret properties store <em>protected</em> values, never clear text. Encrypting and
/// decrypting is the application layer's job — the domain only knows that these are the fields that
/// must never be handed back to a client.
/// </remarks>
public class PaymentMethodIntegration : Entity
{
    public Guid PaymentMethodId { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>The provider's name, as a label — "Paymob", "Fawry", "Stripe".</summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Which of the provider's environments this talks to.
    ///
    /// Defaults to <see cref="PaymentIntegrationMode.Sandbox"/> so a half-configured method cannot
    /// start moving real money before anyone has said it should.
    /// </summary>
    public PaymentIntegrationMode Mode { get; set; } = PaymentIntegrationMode.Sandbox;

    /// <summary>Who the platform is to the provider. Most gateways need this beside the key.</summary>
    public string? MerchantId { get; set; }

    /// <summary>
    /// The specific integration/iframe the provider should use, where one applies. Several gateways
    /// issue a merchant one of these per currency or per channel.
    /// </summary>
    public string? IntegrationId { get; set; }

    /// <summary>Protected. Authenticates the platform's outbound calls to the provider.</summary>
    public string? ApiKeySecret { get; set; }

    /// <summary>Protected. The companion secret some providers issue alongside the key.</summary>
    public string? PasswordSecret { get; set; }

    /// <summary>
    /// Protected. The shared secret a callback is signed with.
    /// </summary>
    /// <remarks>
    /// Not the same thing as the API key, and the more important of the two: the API key proves who
    /// we are when calling out, this proves who <em>they</em> are when calling in. Without it the
    /// callback URL is a public endpoint that accepts "this order is paid" from anyone who finds it.
    /// </remarks>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// The provider's API host. Kept as data because it differs between sandbox and live, and
    /// between regions — a provider moving host should be an edit, not a deployment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Where the provider sends the applicant after a successful payment.</summary>
    public string? RedirectUrl { get; set; }

    /// <summary>
    /// Where the provider sends the applicant when they cancel or the payment fails.
    ///
    /// Separate from <see cref="RedirectUrl"/> because the two mean different things to the person
    /// arriving: one has paid and one has not, and sending both to the same page produces an
    /// applicant who believes they have paid when they have not.
    /// </summary>
    public string? CancelUrl { get; set; }

    /// <summary>
    /// Where the provider posts the result server to server. This is the message that actually
    /// decides a payment landed — the applicant's browser being redirected proves nothing.
    /// </summary>
    public string? CallbackUrl { get; set; }

    /// <summary>How long a generated payment session stays valid. Null leaves it to the provider.</summary>
    public int? SessionTimeoutMinutes { get; set; }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKeySecret);

    public bool HasPassword => !string.IsNullOrWhiteSpace(PasswordSecret);

    public bool HasWebhookSecret => !string.IsNullOrWhiteSpace(WebhookSecret);

    /// <summary>
    /// True when this is configured well enough to attempt a payment: something to authenticate
    /// with, and somewhere to send the applicant back to.
    /// </summary>
    public bool IsUsable => HasApiKey && !string.IsNullOrWhiteSpace(RedirectUrl);

    /// <summary>
    /// True when the configuration would accept an unverifiable callback.
    ///
    /// Surfaced to the admin UI as a warning rather than blocked: a provider that signs nothing
    /// exists, and refusing to save would make this platform unusable with it. Saying so out loud
    /// is the honest middle ground.
    /// </summary>
    public bool CallbackIsUnverified =>
        !string.IsNullOrWhiteSpace(CallbackUrl) && !HasWebhookSecret;
}
