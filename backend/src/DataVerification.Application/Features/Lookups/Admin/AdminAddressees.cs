using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Lookups.Admin;

/// <summary>
/// The list of bodies offered on a new application's "Addressed to" field.
/// </summary>
/// <remarks>
/// Unlike the rest of the lookups this one is a convenience rather than a constraint: an
/// application stores whatever it was addressed to as text, so removing an entry here changes what
/// is offered from now on and never touches an application that already used it. That is why
/// deleting is a plain delete — there is nothing to orphan.
/// </remarks>
public sealed record ListAddresseesQuery : PagedQuery, IRequest<PagedResult<AddresseeDto>>;

public sealed record UpsertAddresseeCommand(
    Guid? Id,
    string NameAr,
    string NameEn,
    int SortOrder,
    bool IsActive) : IRequest<AddresseeDto>;

public sealed record DeleteAddresseeCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertAddresseeCommandValidator : AbstractValidator<UpsertAddresseeCommand>
{
    public UpsertAddresseeCommandValidator()
    {
        // 500 is the bound on the application's own AddressedTo column, so nothing can be put on
        // the list that an application could not then store.
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(500);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(500);

        RuleFor(c => c.SortOrder).InclusiveBetween(0, 10_000);
    }
}

public sealed class AdminAddresseeHandlers :
    IRequestHandler<ListAddresseesQuery, PagedResult<AddresseeDto>>,
    IRequestHandler<UpsertAddresseeCommand, AddresseeDto>,
    IRequestHandler<DeleteAddresseeCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public AdminAddresseeHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public Task<PagedResult<AddresseeDto>> Handle(
        ListAddresseesQuery request,
        CancellationToken cancellationToken) =>
        _lookups.ListAsync(
            _db.Addressees,
            request,
            a => AddresseeDto.From(a, _lookups.Language),
            cancellationToken);

    public async Task<AddresseeDto> Handle(
        UpsertAddresseeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nameEn = request.NameEn.Trim();

        // Two entries reading the same in the applicant's list would be indistinguishable to them.
        await _lookups.EnsureUniqueAsync(
            _db.Addressees,
            a => a.NameEn == nameEn && (request.Id == null || a.Id != request.Id),
            "addressee.duplicate_name",
            $"An addressee named '{nameEn}' already exists.",
            cancellationToken);

        Addressee addressee;
        if (request.Id is { } id)
        {
            addressee = await _lookups.RequireAsync(_db.Addressees, id, cancellationToken);
        }
        else
        {
            addressee = new Addressee { NameAr = request.NameAr, NameEn = nameEn };
            _db.Addressees.Add(addressee);
        }

        addressee.NameAr = request.NameAr.Trim();
        addressee.NameEn = nameEn;
        addressee.SortOrder = request.SortOrder;
        addressee.IsActive = request.IsActive;

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            request.Id is null ? "Addressee.Created" : "Addressee.Updated",
            nameof(Addressee),
            addressee.Id,
            new { addressee.NameEn, addressee.IsActive },
            cancellationToken);

        return AddresseeDto.From(addressee, _lookups.Language);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeleteAddresseeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Nothing references an addressee — applications keep the text, not the row — so it always
        // deletes outright rather than being retired the way the priced lookups are.
        return _lookups.DeleteOrDeactivateAsync(
            _db.Addressees,
            request.Id,
            _ => Task.FromResult(false),
            "Addressee",
            cancellationToken);
    }
}
