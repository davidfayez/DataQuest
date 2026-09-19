using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A named connection to a payment gateway — "PayPal (live)", "Fawry Egypt", "SEPA account" — which
/// payment methods point at.
///
/// It names the gateway and whether this is a test or a live account; the credentials and settings
/// themselves belong to the payment method, because two methods on the same gateway are usually two
/// different merchant accounts. What each gateway asks for is listed in <c>PaymentGatewayCatalog</c>.
/// </summary>
public class PaymentGatewayIntegration : DescribedLookup
{
    /// <summary>A code from the gateway catalogue.</summary>
    public required string GatewayCode { get; set; }

    public PaymentIntegrationMode Mode { get; set; } = PaymentIntegrationMode.Sandbox;

    public int SortOrder { get; set; }

    public ICollection<PaymentMethod> PaymentMethods { get; set; } = [];
}
