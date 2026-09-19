using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = DataVerification.Application.Common.Exceptions.ValidationException;

namespace DataVerification.Application.Features.Payments.Admin;

/// <summary>
/// The payment-type catalogue: the lookup that decides what configuring a method involves and
/// what the applicant is asked for. Editing a flag here reshapes every method of that type in
/// both realms at once, which is what keeps providers out of the code.
/// </summary>
public sealed record ListPaymentMethodTypesQuery : PagedQuery, IRequest<PagedResult<PaymentMethodTypeDto>>
{
    /// <summary>Optional kind filter; the method editor asks for one kind at a time.</summary>
    public PaymentMethodKind? Kind { get; init; }
}

/// <remarks>
/// Every requirement is settable. They used to follow from the kind, which meant a new provider —
/// numbers plus a link, say, or numbers plus a QR code and a bank name — needed a new enum value
/// and a deployment. What a provider asks for is configuration.
///
/// The kind itself is not part of the command: a new type is a transfer, and an existing type
/// keeps the kind it has. A <c>kind</c> sent by an older client is ignored.
/// </remarks>
public sealed record UpsertPaymentMethodTypeCommand(
    Guid? Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    bool RequiresAccountNumber,
    bool RequiresBarcode,
    bool RequiresBank,
    bool RequiresExternalUrl,
    bool RequiresProofDocument,
    bool RequiresReferenceNumber,
    int SortOrder,
    bool IsActive) : IRequest<PaymentMethodTypeDto>;

public sealed record DeletePaymentMethodTypeCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertPaymentMethodTypeCommandValidator
    : AbstractValidator<UpsertPaymentMethodTypeCommand>
{
    public UpsertPaymentMethodTypeCommandValidator()
    {
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);

        // Required on every save, as on the other described lookups: a type that predates
        // descriptions gains them the next time someone edits it.
        RuleFor(c => c.DescriptionAr).NotEmpty()
            .WithMessage("Enter the Arabic description.").MaximumLength(2000);
        RuleFor(c => c.DescriptionEn).NotEmpty()
            .WithMessage("Enter the English description.").MaximumLength(2000);

        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        // A QR code or a bank name hangs off a receiving row, so asking for either without asking
        // for the rows themselves describes a method nobody could finish configuring.
        RuleFor(c => c.RequiresAccountNumber)
            .Equal(true)
            .When(c => c.RequiresBarcode || c.RequiresBank)
            .WithMessage("Receiving numbers are needed before a QR code or a bank can be required.");
    }
}

public sealed class PaymentMethodTypeHandlers :
    IRequestHandler<ListPaymentMethodTypesQuery, PagedResult<PaymentMethodTypeDto>>,
    IRequestHandler<UpsertPaymentMethodTypeCommand, PaymentMethodTypeDto>,
    IRequestHandler<DeletePaymentMethodTypeCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public PaymentMethodTypeHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public async Task<PagedResult<PaymentMethodTypeDto>> Handle(
        ListPaymentMethodTypesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Kind is { } kind
            ? _db.PaymentMethodTypes.Where(t => t.Kind == kind)
            : _db.PaymentMethodTypes;

        var page = await _lookups.ListAsync(
            source,
            request,
            type => type,
            cancellationToken);

        // How many methods hang off a type decides whether the admin may delete it, so the count
        // travels with the row rather than costing a request per row on the client.
        var ids = page.Items.Select(type => type.Id).ToList();
        var counts = await _db.PaymentMethods
            .AsNoTracking()
            .Where(method => ids.Contains(method.PaymentMethodTypeId))
            .GroupBy(method => method.PaymentMethodTypeId)
            .Select(group => new { TypeId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.TypeId, row => row.Count, cancellationToken);

        return new PagedResult<PaymentMethodTypeDto>(
            page.Items
                .Select(type => PaymentMethodTypeDto.From(
                    type,
                    _lookups.Language,
                    counts.GetValueOrDefault(type.Id)))
                .ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<PaymentMethodTypeDto> Handle(
        UpsertPaymentMethodTypeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        PaymentMethodType? type = null;

        if (request.Id is { } id && id != Guid.Empty)
        {
            type = await _lookups.RequireAsync(_db.PaymentMethodTypes, id, cancellationToken);
        }

        // A new type is a transfer; an existing one keeps its kind, so the seeded PayPal type can
        // still be renamed or described.
        var kind = type?.Kind ?? PaymentMethodKind.Transfer;

        // Everything a transfer offers is a way to hand money over. A type offering none of them
        // leaves the applicant a payment method they cannot pay. Checked here rather than in the
        // validator, because it depends on the stored kind, not on anything in the request.
        if (kind == PaymentMethodKind.Transfer
            && !request.RequiresAccountNumber
            && !request.RequiresExternalUrl)
        {
            throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(request.RequiresExternalUrl),
                    "A transfer type needs receiving numbers, a payment link, or both."),
            ]);
        }

        if (type is null)
        {
            type = new PaymentMethodType
            {
                NameAr = request.NameAr,
                NameEn = request.NameEn,
                Kind = PaymentMethodKind.Transfer,
            };
            _db.PaymentMethodTypes.Add(type);
        }

        var typeId = type.Id;
        await _lookups.EnsureUniqueAsync(
            _db.PaymentMethodTypes,
            other => other.Id != typeId
                && (other.NameEn == request.NameEn || other.NameAr == request.NameAr),
            "payment_method_type.duplicate_name",
            "A payment type with this name already exists.",
            cancellationToken);

        type.NameAr = request.NameAr;
        type.NameEn = request.NameEn;
        type.DescriptionAr = request.DescriptionAr!.Trim();
        type.DescriptionEn = request.DescriptionEn!.Trim();
        type.RequiresAccountNumber = request.RequiresAccountNumber;
        type.RequiresBarcode = request.RequiresBarcode;
        type.RequiresBank = request.RequiresBank;
        type.RequiresExternalUrl = request.RequiresExternalUrl;
        type.RequiresProofDocument = request.RequiresProofDocument;
        type.RequiresReferenceNumber = request.RequiresReferenceNumber;
        type.SortOrder = request.SortOrder;
        type.IsActive = request.IsActive;
        type.UpdatedAtUtc = DateTime.UtcNow;

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            "PaymentMethodType.Saved",
            nameof(PaymentMethodType),
            type.Id,
            new { type.NameEn, type.Kind, type.IsActive },
            cancellationToken);

        var methodCount = await _db.PaymentMethods
            .CountAsync(method => method.PaymentMethodTypeId == type.Id, cancellationToken);

        return PaymentMethodTypeDto.From(type, _lookups.Language, methodCount);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeletePaymentMethodTypeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Deactivated rather than removed while any method still points at it — deleting would
        // strip the flags that decide how that method's past requests are read.
        return _lookups.DeleteOrDeactivateAsync(
            _db.PaymentMethodTypes,
            request.Id,
            ct => _db.PaymentMethods.AnyAsync(m => m.PaymentMethodTypeId == request.Id, ct),
            "PaymentMethodType",
            cancellationToken);
    }
}
