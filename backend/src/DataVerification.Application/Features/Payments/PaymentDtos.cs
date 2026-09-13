using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;

namespace DataVerification.Application.Features.Payments;

/// <summary>
/// A payment type as the admin panel sees it. The four <c>Requires…</c> flags are the whole
/// contract between configuration and both realms' forms: the method editor renders the fields
/// they demand, and the applicant's deposit form validates against the same four.
/// </summary>
public sealed record PaymentMethodTypeDto(
    Guid Id,
    string Name,
    string NameAr,
    string NameEn,
    bool IsActive,
    PaymentMethodKind Kind,
    string KindName,
    bool RequiresAccountNumber,
    bool RequiresBarcode,
    bool RequiresBank,
    bool RequiresExternalUrl,
    /// <summary>The type talks to a payment provider, so its methods carry credentials.</summary>
    bool UsesProviderCredentials,
    bool RequiresProofDocument,
    bool RequiresReferenceNumber,
    bool NeedsApproval,
    int SortOrder,
    int MethodCount)
{
    public static PaymentMethodTypeDto From(
        PaymentMethodType type,
        string? languageCode,
        int methodCount = 0)
    {
        ArgumentNullException.ThrowIfNull(type);

        return new(
            type.Id,
            type.ResolveName(languageCode),
            type.NameAr,
            type.NameEn,
            type.IsActive,
            type.Kind,
            type.Kind.ToString(),
            type.RequiresAccountNumber,
            type.RequiresBarcode,
            type.RequiresBank,
            type.RequiresExternalUrl,
            type.UsesProviderCredentials,
            type.RequiresProofDocument,
            type.RequiresReferenceNumber,
            type.NeedsApproval,
            type.SortOrder,
            methodCount);
    }
}

/// <summary>A bank in the catalogue, with the country it operates in.</summary>
public sealed record BankDto(
    Guid Id,
    Guid CountryId,
    string CountryName,
    string Name,
    string NameAr,
    string NameEn,
    string? SwiftCode,
    int SortOrder,
    bool IsActive)
{
    public static BankDto From(Bank bank, string? languageCode)
    {
        ArgumentNullException.ThrowIfNull(bank);

        return new(
            bank.Id,
            bank.CountryId,
            bank.Country?.ResolveName(languageCode) ?? string.Empty,
            bank.ResolveName(languageCode),
            bank.NameAr,
            bank.NameEn,
            bank.SwiftCode,
            bank.SortOrder,
            bank.IsActive);
    }
}

/// <summary>One mailbox told about activity on a payment method. Admin-only.</summary>
public sealed record PaymentNotificationEmailDto(
    Guid Id,
    string Email,
    string? DisplayName,
    bool NotifyOnSubmitted,
    bool NotifyOnApproved,
    bool NotifyOnRejected)
{
    public static PaymentNotificationEmailDto From(PaymentMethodNotificationEmail recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);

        return new(
            recipient.Id,
            recipient.Email,
            recipient.DisplayName,
            recipient.NotifyOnSubmitted,
            recipient.NotifyOnApproved,
            recipient.NotifyOnRejected);
    }
}

/// <summary>One receiving account, as the admin panel edits it.</summary>
public sealed record PaymentMethodAccountDto(
    Guid Id,
    string Label,
    string LabelAr,
    string LabelEn,
    string AccountNumber,
    string? AccountHolder,
    Guid? BankId,
    string? BankName,
    bool HasBarcode,
    string? BarcodeFileName,
    bool IsActive,
    int SortOrder)
{
    public static PaymentMethodAccountDto From(PaymentMethodAccount account, string? languageCode)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new(
            account.Id,
            account.ResolveLabel(languageCode),
            account.LabelAr,
            account.LabelEn,
            account.AccountNumber,
            account.AccountHolder,
            account.BankId,
            account.Bank?.ResolveName(languageCode),
            account.HasBarcode,
            account.BarcodeFileName,
            account.IsActive,
            account.SortOrder);
    }
}

/// <summary>
/// The full configuration of one method. Admin-only: it is the single shape that carries the
/// private notes, which no applicant-facing response ever includes.
/// </summary>
public sealed record AdminPaymentMethodDto(
    Guid Id,
    string Name,
    string NameAr,
    string NameEn,
    bool IsActive,
    Guid PaymentMethodTypeId,
    string TypeName,
    PaymentMethodKind Kind,
    string KindName,
    string? DescriptionAr,
    string? DescriptionEn,
    string? PublicNoteAr,
    string? PublicNoteEn,
    string? PrivateNoteAr,
    string? PrivateNoteEn,
    string? ExternalUrl,
    /// <summary>
    /// What this method's type asks for, carried on the method so a list can say what paying it
    /// looks like without loading the type catalogue alongside it.
    /// </summary>
    bool RequiresAccountNumber,
    bool RequiresBarcode,
    bool RequiresBank,
    bool RequiresExternalUrl,
    /// <summary>The method talks to a provider, so its editor offers the credentials panel.</summary>
    bool UsesProviderCredentials,
    int SortOrder,
    IReadOnlyList<Guid> CountryIds,
    IReadOnlyList<Guid> CurrencyIds,
    IReadOnlyList<PaymentMethodAccountDto> Accounts,
    IReadOnlyList<PaymentNotificationEmailDto> NotificationEmails,
    /// <summary>Null when the method is paid by hand rather than through a provider.</summary>
    PaymentIntegrationDto? Integration,
    bool IsUsable)
{
    public static AdminPaymentMethodDto From(PaymentMethod method, string? languageCode)
    {
        ArgumentNullException.ThrowIfNull(method);

        var type = method.Type;

        return new(
            method.Id,
            method.ResolveName(languageCode),
            method.NameAr,
            method.NameEn,
            method.IsActive,
            method.PaymentMethodTypeId,
            type?.ResolveName(languageCode) ?? string.Empty,
            // The type is always loaded in practice; the fallback only keeps the mapping total.
            type?.Kind ?? PaymentMethodKind.Transfer,
            (type?.Kind ?? PaymentMethodKind.Transfer).ToString(),
            method.DescriptionAr,
            method.DescriptionEn,
            method.PublicNoteAr,
            method.PublicNoteEn,
            method.PrivateNoteAr,
            method.PrivateNoteEn,
            method.ExternalUrl,
            type?.RequiresAccountNumber ?? false,
            type?.RequiresBarcode ?? false,
            type?.RequiresBank ?? false,
            type?.RequiresExternalUrl ?? false,
            type?.UsesProviderCredentials ?? false,
            method.SortOrder,
            method.CountryLinks.Select(link => link.CountryId).Distinct().ToList(),
            method.CurrencyLinks.Select(link => link.CurrencyId).Distinct().ToList(),
            method.Accounts
                .OrderBy(account => account.SortOrder)
                .ThenBy(account => account.CreatedAtUtc)
                .Select(account => PaymentMethodAccountDto.From(account, languageCode))
                .ToList(),
            method.NotificationEmails
                .OrderBy(recipient => recipient.Email)
                .Select(PaymentNotificationEmailDto.From)
                .ToList(),
            method.Integration is null ? null : PaymentIntegrationDto.From(method.Integration),
            type is not null && method.IsUsable(type));
    }
}

/// <summary>
/// One receiving account as the applicant sees it — no sort order, no activity flag, and only the
/// accounts they may actually pay.
/// </summary>
public sealed record PaymentAccountOptionDto(
    Guid Id,
    string Label,
    string AccountNumber,
    string? AccountHolder,
    string? BankName,
    bool HasBarcode);

/// <summary>
/// A method offered to an applicant, already filtered to their country and wallet currency. The
/// <c>Requires…</c> flags travel with it so the deposit form is generated rather than hard-coded,
/// and the private notes are absent from the shape entirely.
/// </summary>
public sealed record PaymentMethodOptionDto(
    Guid Id,
    string Name,
    string? Description,
    string? PublicNote,
    Guid PaymentMethodTypeId,
    string TypeName,
    PaymentMethodKind Kind,
    string KindName,
    string? ExternalUrl,
    bool RequiresAccountNumber,
    bool RequiresBarcode,
    bool RequiresBank,
    bool RequiresExternalUrl,
    bool RequiresProofDocument,
    bool RequiresReferenceNumber,
    IReadOnlyList<PaymentAccountOptionDto> Accounts)
{
    public static PaymentMethodOptionDto From(PaymentMethod method, string? languageCode)
    {
        ArgumentNullException.ThrowIfNull(method);

        var type = method.Type
            ?? throw new InvalidOperationException("The payment method's type must be loaded.");

        return new(
            method.Id,
            method.ResolveName(languageCode),
            method.ResolveDescription(languageCode),
            method.ResolvePublicNote(languageCode),
            method.PaymentMethodTypeId,
            type.ResolveName(languageCode),
            type.Kind,
            type.Kind.ToString(),
            method.ExternalUrl,
            type.RequiresAccountNumber,
            type.RequiresBarcode,
            type.RequiresBank,
            type.RequiresExternalUrl,
            type.RequiresProofDocument,
            type.RequiresReferenceNumber,
            method.Accounts
                .Where(account => account.IsActive)
                .OrderBy(account => account.SortOrder)
                .ThenBy(account => account.CreatedAtUtc)
                .Select(account => new PaymentAccountOptionDto(
                    account.Id,
                    account.ResolveLabel(languageCode),
                    account.AccountNumber,
                    account.AccountHolder,
                    account.Bank?.ResolveName(languageCode),
                    account.HasBarcode))
                .ToList());
    }
}

/// <summary>An attached receipt, as listed beside a wallet request in both realms.</summary>
public sealed record WalletRequestFileDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc)
{
    public static WalletRequestFileDto From(WalletRequestFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new(file.Id, file.FileName, file.ContentType, file.SizeBytes, file.CreatedAtUtc);
    }
}

/// <summary>Bytes plus the metadata needed to replay them, for barcode and receipt downloads.</summary>
public sealed record FileDownload(Stream Content, string ContentType, string FileName);

/// <summary>
/// A method's provider integration, as the admin page is allowed to see it.
/// </summary>
/// <remarks>
/// Carries no secret, ever — only whether each one is set. A secret that can be read back out of
/// the API is a secret that leaks through every browser cache, log and screenshot between here and
/// the person looking at it, and nothing on this page needs the value to do its job.
/// </remarks>
public sealed record PaymentIntegrationDto(
    string? Provider,
    PaymentIntegrationMode Mode,
    string ModeName,
    string? MerchantId,
    string? IntegrationId,
    bool HasApiKey,
    bool HasPassword,
    bool HasWebhookSecret,
    string? BaseUrl,
    string? RedirectUrl,
    string? CancelUrl,
    string? CallbackUrl,
    int? SessionTimeoutMinutes,
    /// <summary>True when a callback is configured that nothing can be verified against.</summary>
    bool CallbackIsUnverified)
{
    public static PaymentIntegrationDto From(PaymentMethodIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);

        return new(
            integration.Provider,
            integration.Mode,
            integration.Mode.ToString(),
            integration.MerchantId,
            integration.IntegrationId,
            integration.HasApiKey,
            integration.HasPassword,
            integration.HasWebhookSecret,
            integration.BaseUrl,
            integration.RedirectUrl,
            integration.CancelUrl,
            integration.CallbackUrl,
            integration.SessionTimeoutMinutes,
            integration.CallbackIsUnverified);
    }
}

/// <summary>
/// The integration as it arrives from the editor.
/// </summary>
/// <remarks>
/// The three secrets follow a deliberate three-state convention, because the editor can never show
/// what is already stored:
/// <list type="bullet">
///   <item><c>null</c> — leave whatever is stored alone. This is what an untouched field sends.</item>
///   <item>empty string — clear it.</item>
///   <item>anything else — replace it.</item>
/// </list>
/// Without this, saving a name change on a form whose secret fields render empty would silently
/// wipe live credentials.
/// </remarks>
public sealed record PaymentIntegrationInput(
    string? Provider,
    PaymentIntegrationMode Mode,
    string? MerchantId,
    string? IntegrationId,
    string? ApiKey,
    string? Password,
    string? WebhookSecret,
    string? BaseUrl,
    string? RedirectUrl,
    string? CancelUrl,
    string? CallbackUrl,
    int? SessionTimeoutMinutes);
