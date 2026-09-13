namespace DataVerification.Application.Features.Wallets;

/// <summary>
/// Bound from the <c>Wallet</c> configuration section. v1 has no payment gateway, so money moves
/// through operator-reviewed requests; these switches decide which of those an applicant may raise
/// and how large they may be.
/// </summary>
public sealed class WalletOptions
{
    public const string SectionName = "Wallet";

    /// <summary>When false, applicants cannot raise a top-up request at all.</summary>
    public bool AllowDepositRequests { get; set; } = true;

    /// <summary>When false, applicants cannot ask for a payout; funds only leave via payments.</summary>
    public bool AllowWithdrawalRequests { get; set; } = true;

    /// <summary>
    /// Credits the caller's own wallet on demand, with no operator in the loop. Strictly a
    /// development and demo convenience — it mints money, so it defaults to off and must never be
    /// enabled in production.
    /// </summary>
    public bool AllowSimulatedDeposits { get; set; }

    /// <summary>Rejects requests for trivial sums that cost more to process than they carry.</summary>
    public decimal MinimumRequestAmount { get; set; } = 1m;

    /// <summary>Upper bound on a single request, in the wallet's currency.</summary>
    public decimal MaximumRequestAmount { get; set; } = 1_000_000m;

    /// <summary>Caps a simulated deposit so a stray keystroke cannot distort demo data.</summary>
    public decimal MaximumSimulatedDeposit { get; set; } = 10_000m;
}
