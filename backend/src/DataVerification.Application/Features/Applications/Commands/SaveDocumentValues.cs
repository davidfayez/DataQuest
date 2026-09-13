using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Applications.Commands;

/// <summary>
/// Saves what the applicant entered for the custom fields configured on a service's required
/// documents. Sent as a whole set from the wizard's upload step, replacing anything held before.
/// </summary>
public sealed record SaveDocumentValuesCommand(
    Guid ApplicationId,
    IReadOnlyList<DocumentValueInput> Values) : IRequest<ApplicationDetailsDto>;

/// <param name="ApplicationServiceId">The purchased service line the document belongs to.</param>
public sealed record DocumentValueInput(
    Guid ApplicationServiceId,
    Guid RequiredFileFieldId,
    string? Value);

public sealed class SaveDocumentValuesCommandValidator : AbstractValidator<SaveDocumentValuesCommand>
{
    public SaveDocumentValuesCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        RuleFor(c => c.Values).NotNull();

        RuleForEach(c => c.Values).ChildRules(value =>
        {
            value.RuleFor(v => v.ApplicationServiceId).NotEmpty();
            value.RuleFor(v => v.RequiredFileFieldId).NotEmpty();
            value.RuleFor(v => v.Value).MaximumLength(1000);
        });
    }
}

public sealed class SaveDocumentValuesCommandHandler
    : IRequestHandler<SaveDocumentValuesCommand, ApplicationDetailsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ApplicationWriteService _writeService;
    private readonly ICurrentUser _currentUser;

    public SaveDocumentValuesCommandHandler(
        IApplicationDbContext db,
        ApplicationWriteService writeService,
        ICurrentUser currentUser)
    {
        _db = db;
        _writeService = writeService;
        _currentUser = currentUser;
    }

    public async Task<ApplicationDetailsDto> Handle(
        SaveDocumentValuesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var application = await _writeService.RequireOwnedAsync(
            request.ApplicationId,
            orderId,
            cancellationToken,
            includeChildren: true);

        // Values describe the evidence, so they follow the same lock as the files themselves.
        application.EnsureEditable();

        var lineIds = application.Services.Select(s => s.Id).ToHashSet();
        var serviceTypeIds = application.Services.Select(s => s.ServiceTypeId).Distinct().ToList();

        // Only fields that actually belong to a document of a purchased service may be written —
        // otherwise a caller could attach arbitrary values to someone else's field definitions.
        var allowedFields = await _db.RequiredFileFields
            .AsNoTracking()
            .Include(f => f.Options)
            .Where(f => f.IsActive
                        && f.RequiredFile!.IsActive
                        && serviceTypeIds.Contains(f.RequiredFile.ServiceTypeId))
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var input in request.Values)
        {
            if (!lineIds.Contains(input.ApplicationServiceId)
                || !allowedFields.TryGetValue(input.RequiredFileFieldId, out var field))
            {
                throw new NotFoundException(
                    "One or more of the fields supplied do not belong to this application.");
            }

            // Blank is allowed here even for a required field: the wizard saves as the applicant
            // types, and the submit gate is what insists every required field is filled.
            if (!string.IsNullOrWhiteSpace(input.Value)
                && field.Validate(input.Value, today) is { } error)
            {
                throw new ConflictException(error, $"'{field.NameEn}' is not valid.");
            }
        }

        var existing = await _db.ApplicationDocumentValues
            .Where(v => v.ApplicationId == application.Id)
            .ToListAsync(cancellationToken);

        foreach (var input in request.Values)
        {
            var current = existing.FirstOrDefault(v =>
                v.ApplicationServiceId == input.ApplicationServiceId
                && v.RequiredFileFieldId == input.RequiredFileFieldId);

            var trimmed = input.Value?.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                if (current is not null)
                {
                    _db.ApplicationDocumentValues.Remove(current);
                }

                continue;
            }

            if (current is null)
            {
                _db.ApplicationDocumentValues.Add(new ApplicationDocumentValue
                {
                    ApplicationId = application.Id,
                    ApplicationServiceId = input.ApplicationServiceId,
                    RequiredFileFieldId = input.RequiredFileFieldId,
                    Value = trimmed,
                });
            }
            else
            {
                current.Value = trimmed;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await ApplicationDetailsProjection.LoadAsync(
            _db,
            _writeService,
            application.Id,
            orderId,
            _currentUser.LanguageCode,
            cancellationToken);
    }
}
