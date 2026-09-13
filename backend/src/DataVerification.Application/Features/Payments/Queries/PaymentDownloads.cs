using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Authorization;
using DataVerification.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Payments.Queries;

/// <summary>
/// The barcode for one receiving account, fetched by an applicant. Scoped rather than reusing the
/// admin query: an applicant may only see the codes belonging to methods their own order can
/// actually pay through.
/// </summary>
public sealed record GetOrderAccountBarcodeQuery(Guid AccountId) : IRequest<FileDownload>;

/// <summary>
/// One receipt attached to a wallet request. Serves both realms — an applicant may read their own
/// order's attachments, and a reviewer with <c>Orders.View</c> may read any — because the file is
/// the evidence the decision is made on.
/// </summary>
public sealed record GetWalletRequestFileQuery(Guid RequestId, Guid FileId) : IRequest<FileDownload>;

public sealed class PaymentDownloadHandlers :
    IRequestHandler<GetOrderAccountBarcodeQuery, FileDownload>,
    IRequestHandler<GetWalletRequestFileQuery, FileDownload>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;

    public PaymentDownloadHandlers(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
    }

    public async Task<FileDownload> Handle(
        GetOrderAccountBarcodeQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.VerificationCountryId, o.CurrencyId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        var account = await _db.PaymentMethodAccounts
            .AsNoTracking()
            .Include(a => a.PaymentMethod)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken)
            ?? throw new NotFoundException(nameof(PaymentMethodAccount), request.AccountId);

        var reachable = account.IsActive
            && account.PaymentMethod?.IsActive == true
            && order.VerificationCountryId is { } countryId
            && order.CurrencyId is { } currencyId
            && await _db.PaymentMethodCountries.AnyAsync(
                link => link.PaymentMethodId == account.PaymentMethodId && link.CountryId == countryId,
                cancellationToken)
            && await _db.PaymentMethodCurrencies.AnyAsync(
                link => link.PaymentMethodId == account.PaymentMethodId && link.CurrencyId == currencyId,
                cancellationToken);

        // 404 rather than 403: an applicant has no business learning that an account they cannot
        // reach exists at all.
        if (!reachable || !account.HasBarcode)
        {
            throw new NotFoundException("Barcode for payment account", request.AccountId);
        }

        var content = await _storage.OpenReadAsync(account.BarcodeStoragePath!, cancellationToken);

        return new FileDownload(
            content,
            account.BarcodeContentType ?? "application/octet-stream",
            account.BarcodeFileName ?? "barcode");
    }

    public async Task<FileDownload> Handle(
        GetWalletRequestFileQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var file = await _db.WalletRequestFiles
            .AsNoTracking()
            .Include(f => f.WalletRequest)
            .FirstOrDefaultAsync(
                f => f.Id == request.FileId && f.WalletRequestId == request.RequestId,
                cancellationToken)
            ?? throw new NotFoundException(nameof(WalletRequestFile), request.FileId);

        EnsureReadable(file);

        var content = await _storage.OpenReadAsync(file.StoragePath, cancellationToken);

        return new FileDownload(content, file.ContentType, file.FileName);
    }

    private void EnsureReadable(WalletRequestFile file)
    {
        if (_currentUser.OrderId is { } orderId)
        {
            if (file.WalletRequest?.OrderId != orderId)
            {
                throw new NotFoundException(nameof(WalletRequestFile), file.Id);
            }

            return;
        }

        if (!_currentUser.Permissions.Contains(Permissions.OrdersView))
        {
            throw new ForbiddenAccessException(
                $"This action requires the '{Permissions.OrdersView}' permission.");
        }
    }
}
