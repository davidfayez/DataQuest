using DataVerification.Application.Features.Payments;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;

namespace DataVerification.Application.Features.Wallets;

public sealed record WalletSummaryDto(
    Guid Id,
    decimal Balance,
    Guid CurrencyId,
    string CurrencyCode,
    string CurrencySymbol);

/// <summary>
/// What the wallet page is allowed to offer, decided by the server so the UI never has to guess.
/// The limits are the same ones the request validator enforces.
/// </summary>
/// <param name="SimulatedDepositsEnabled">
/// True only where <c>Wallet:AllowSimulatedDeposits</c> is switched on — development and demo
/// environments. Production leaves it off and the "add test funds" control never renders.
/// </param>
public sealed record WalletFeaturesDto(
    bool DepositRequestsEnabled,
    bool WithdrawalRequestsEnabled,
    bool SimulatedDepositsEnabled,
    decimal MinimumRequestAmount,
    decimal MaximumRequestAmount);

/// <summary>
/// One ledger line as shown on the wallet statement. <c>SignedAmount</c> is negative for payments
/// and positive for credits, so the statement renders without re-deriving direction per row.
/// </summary>
public sealed record WalletTransactionDto(
    Guid Id,
    WalletTransactionType Type,
    string TypeName,
    decimal Amount,
    decimal SignedAmount,
    decimal BalanceAfter,
    IReadOnlyList<Guid> ReferenceApplicationIds,
    IReadOnlyList<string> ReferenceApplicationNumbers,
    ActorType PerformedByType,
    string? PerformedByName,
    string? Note,
    DateTime CreatedAtUtc)
{
    public static WalletTransactionDto From(
        WalletTransaction transaction,
        IReadOnlyDictionary<Guid, string> applicationNumbers) => new(
        transaction.Id,
        transaction.Type,
        transaction.Type.ToString(),
        transaction.Amount,
        transaction.Type.IsDebit() ? -transaction.Amount : transaction.Amount,
        transaction.BalanceAfter,
        transaction.ReferenceApplicationIds,
        transaction.ReferenceApplicationIds
            .Select(id => applicationNumbers.TryGetValue(id, out var number) ? number : id.ToString())
            .ToList(),
        transaction.PerformedByType,
        transaction.PerformedByName,
        transaction.Note,
        transaction.CreatedAtUtc);
}

/// <summary>Outcome of settling one or more applications in a single wallet transaction.</summary>
public sealed record PaymentResultDto(
    Guid TransactionId,
    decimal AmountPaid,
    decimal BalanceAfter,
    string CurrencyCode,
    IReadOnlyList<PaidApplicationDto> Applications);

public sealed record PaidApplicationDto(
    Guid ApplicationId,
    string ApplicationNumber,
    decimal Amount,
    ApplicationStatus Status,
    DateTime PaidAtUtc);

public sealed record RefundResultDto(
    Guid TransactionId,
    Guid ApplicationId,
    string ApplicationNumber,
    decimal AmountRefunded,
    decimal BalanceAfter,
    ApplicationStatus Status);

/// <summary>
/// A deposit or withdrawal request as shown to both realms. <c>CanCancel</c> is server-decided so
/// the applicant's UI renders actions from it rather than re-deriving the state machine.
///
/// The payment fields are populated only for a deposit raised through a payment method, and carry
/// nothing an applicant may not see — a method's private notes never travel on this shape.
/// </summary>
public sealed record WalletRequestDto(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    WalletRequestType Type,
    string TypeName,
    WalletRequestStatus Status,
    string StatusName,
    decimal Amount,
    string CurrencyCode,
    string? ApplicantNote,
    string? ReviewerNote,
    string? RequestedByName,
    string? ReviewedByName,
    DateTime? ReviewedAtUtc,
    DateTime CreatedAtUtc,
    bool CanCancel,
    Guid? PaymentMethodId,
    string? PaymentMethodName,
    string? PaymentMethodTypeName,
    PaymentMethodKind? PaymentMethodKind,
    Guid? PaymentMethodAccountId,
    string? PaymentAccountLabel,
    string? PaymentAccountNumber,
    string? ReferenceNumber,
    decimal? ConfirmedAmount,
    string? ConfirmedReference,
    IReadOnlyList<WalletRequestFileDto> Files)
{
    /// <param name="languageCode">
    /// Resolves the method and account names. Optional: the queues that only need the money and the
    /// decision pass nothing and get the stored English text.
    /// </param>
    public static WalletRequestDto From(
        WalletRequest request,
        string orderNumber,
        string currencyCode,
        string? languageCode = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var method = request.PaymentMethod;
        var account = request.PaymentMethodAccount;

        return new(
            request.Id,
            request.OrderId,
            orderNumber,
            request.Type,
            request.Type.ToString(),
            request.Status,
            request.Status.ToString(),
            request.Amount,
            currencyCode,
            request.ApplicantNote,
            request.ReviewerNote,
            request.RequestedByName,
            request.ReviewedByName,
            request.ReviewedAtUtc,
            request.CreatedAtUtc,
            request.CanCancel,
            request.PaymentMethodId,
            method?.ResolveName(languageCode),
            method?.Type?.ResolveName(languageCode),
            method?.Type?.Kind,
            request.PaymentMethodAccountId,
            account?.ResolveLabel(languageCode),
            account?.AccountNumber,
            request.ReferenceNumber,
            request.ConfirmedAmount,
            request.ConfirmedReference,
            request.Files
                .OrderBy(file => file.CreatedAtUtc)
                .Select(WalletRequestFileDto.From)
                .ToList());
    }
}

/// <summary>
/// A wallet request flattened to exactly the columns <see cref="WalletRequestDto"/> needs.
/// </summary>
/// <remarks>
/// The list queries project into this instead of loading the request with its payment method,
/// method type, account and files attached.
///
/// Why it matters: the loaded shape dragged every column of those tables through a join that had
/// to be sorted — including seven <c>nvarchar(2000)</c> note and description columns on
/// PaymentMethods that nothing here reads, and the order's password material. SQL Server sizes a
/// sort's memory grant from the declared width of its columns, not from the rows actually found,
/// so a page of three requests asked for a grant it then waited about twenty-five seconds to be
/// given (<c>RESOURCE_SEMAPHORE</c>). Narrow rows make the grant small and the wait disappear.
///
/// The receipts are not on this row for the same reason: as a projected sub-collection they became
/// another join, and the ORDER BY that stitches one is exactly the sort whose memory grant this
/// instance could not satisfy. They are fetched by key and handed to <see cref="ToDto"/>.
///
/// The Arabic and English pairs travel unresolved because the language is negotiated per request;
/// <see cref="ToDto"/> applies the same rule the entities do.
/// </remarks>
public sealed record WalletRequestListRow(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    WalletRequestType Type,
    WalletRequestStatus Status,
    decimal Amount,
    string CurrencyCode,
    string? ApplicantNote,
    string? ReviewerNote,
    string? RequestedByName,
    string? ReviewedByName,
    DateTime? ReviewedAtUtc,
    DateTime CreatedAtUtc,
    Guid? PaymentMethodId,
    string? MethodNameAr,
    string? MethodNameEn,
    string? MethodTypeNameAr,
    string? MethodTypeNameEn,
    PaymentMethodKind? MethodKind,
    Guid? PaymentMethodAccountId,
    string? AccountLabelAr,
    string? AccountLabelEn,
    string? AccountNumber,
    string? ReferenceNumber,
    decimal? ConfirmedAmount,
    string? ConfirmedReference)
{
    /// <param name="files">
    /// Fetched separately and handed in. Projecting them as a sub-collection put the receipts back
    /// into the join, and with them the ORDER BY that costs the memory grant.
    /// </param>
    public WalletRequestDto ToDto(string? languageCode, IReadOnlyList<WalletRequestFileDto> files) => new(
        Id,
        OrderId,
        OrderNumber,
        Type,
        Type.ToString(),
        Status,
        Status.ToString(),
        Amount,
        CurrencyCode,
        ApplicantNote,
        ReviewerNote,
        RequestedByName,
        ReviewedByName,
        ReviewedAtUtc,
        CreatedAtUtc,
        // The same rule as WalletRequest.CanCancel: only a request nobody has decided yet.
        Status == WalletRequestStatus.Pending,
        PaymentMethodId,
        PaymentMethodId is null ? null : LocalizedText.Resolve(MethodNameAr, MethodNameEn, languageCode),
        MethodTypeNameAr is null && MethodTypeNameEn is null
            ? null
            : LocalizedText.Resolve(MethodTypeNameAr, MethodTypeNameEn, languageCode),
        MethodKind,
        PaymentMethodAccountId,
        PaymentMethodAccountId is null
            ? null
            : PaymentMethodAccount.ResolveLabel(AccountLabelAr, AccountLabelEn, AccountNumber ?? string.Empty, languageCode),
        AccountNumber,
        ReferenceNumber,
        ConfirmedAmount,
        ConfirmedReference,
        files);
}
