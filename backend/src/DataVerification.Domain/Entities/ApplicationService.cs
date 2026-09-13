using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One purchased service line. Unit and express costs are snapshotted at creation time so a later
/// price change in the lookup never rewrites the history of an order that has already been quoted.
/// </summary>
public class ApplicationService : Entity
{
    public Guid ApplicationId { get; set; }

    public VerificationApplication? Application { get; set; }

    public Guid ServiceTypeId { get; set; }

    public ServiceType? ServiceType { get; set; }

    public int Quantity { get; set; } = 1;

    /// <summary>Language the verified output should be issued in.</summary>
    public required string LanguageCode { get; set; }

    public bool IsExpress { get; set; }

    public decimal UnitCost { get; set; }

    public decimal ExpressCost { get; set; }

    public decimal LineTotal { get; set; }

    public ICollection<ApplicationFile> Files { get; set; } = [];

    /// <summary>
    /// Prices this line from the service type's currency-specific configuration. Rejects express
    /// on a service that does not offer it, which is the rule the wizard's express toggle mirrors.
    /// </summary>
    public void PriceFrom(ServiceType serviceType, ServiceTypeCost price)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(price);

        LineTotal = serviceType.CalculateLineTotal(Quantity, IsExpress, price.Cost, price.ExpressCost);
        UnitCost = price.Cost;
        ExpressCost = IsExpress ? price.ExpressCost : 0m;
    }
}
