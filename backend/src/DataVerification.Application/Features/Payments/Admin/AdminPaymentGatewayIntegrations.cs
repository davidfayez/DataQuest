using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using DataVerification.Domain.Payments;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Payments.Admin;

// ---------------------------------------------------------------------------------- contracts

/// <summary>The gateways an integration can be made for, with the settings each one needs.</summary>
public sealed record ListPaymentGatewaysQuery : IRequest<IReadOnlyList<PaymentGatewayDto>>;

public sealed record ListPaymentGatewayIntegrationsQuery
    : PagedQuery, IRequest<PagedResult<PaymentGatewayIntegrationDto>>
{
    public string? GatewayCode { get; init; }
}

public sealed record GetPaymentGatewayIntegrationQuery(Guid Id) : IRequest<PaymentGatewayIntegrationDto>;

/// <remarks>
/// No settings or credentials here: those belong to the payment method that uses the integration,
/// because two methods on the same gateway are usually two different merchant accounts.
/// </remarks>
public sealed record UpsertPaymentGatewayIntegrationCommand(
    Guid? Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string GatewayCode,
    PaymentIntegrationMode Mode,
    int SortOrder,
    bool IsActive) : IRequest<PaymentGatewayIntegrationDto>;

public sealed record DeletePaymentGatewayIntegrationCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed record PaymentGatewayFieldOptionDto(string Value, string Label);

public sealed record PaymentGatewayFieldDto(
    string Key,
    string Label,
    string? Hint,
    GatewayFieldType Type,
    string TypeName,
    bool IsRequired,
    string? Pattern,
    string? Placeholder,
    bool Multiline,
    int MaxLength,
    IReadOnlyList<PaymentGatewayFieldOptionDto> Options);

public sealed record PaymentGatewayDto(
    string Code,
    string Name,
    PaymentGatewayCategory Category,
    string CategoryName,
    string Region,
    string? Website,
    IReadOnlyList<PaymentGatewayFieldDto> Fields)
{
    public static PaymentGatewayDto From(PaymentGatewayDefinition gateway, string? languageCode)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        var arabic = languageCode?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;

        return new(
            gateway.Code,
            gateway.Name,
            gateway.Category,
            gateway.Category.ToString(),
            arabic ? gateway.RegionAr : gateway.RegionEn,
            gateway.Website,
            gateway.Fields
                .Select(field => new PaymentGatewayFieldDto(
                    field.Key,
                    arabic ? field.LabelAr : field.LabelEn,
                    arabic ? field.HintAr ?? field.HintEn : field.HintEn,
                    field.Type,
                    field.Type.ToString(),
                    field.IsRequired,
                    field.Pattern,
                    field.Placeholder,
                    field.Multiline,
                    field.MaxLength,
                    field.Options
                        .Select(option => new PaymentGatewayFieldOptionDto(
                            option.Value,
                            arabic ? option.LabelAr : option.LabelEn))
                        .ToList()))
                .ToList());
    }
}

/// <summary>An integration as the admin page reads it.</summary>
public sealed record PaymentGatewayIntegrationDto(
    Guid Id,
    string Name,
    string NameAr,
    string NameEn,
    string? Description,
    string? DescriptionAr,
    string? DescriptionEn,
    string GatewayCode,
    string GatewayName,
    PaymentGatewayCategory? GatewayCategory,
    PaymentIntegrationMode Mode,
    string ModeName,
    int SortOrder,
    bool IsActive,
    int MethodCount)
{
    internal static PaymentGatewayIntegrationDto From(
        PaymentGatewayIntegration integration,
        string? languageCode,
        int methodCount)
    {
        var gateway = PaymentGatewayCatalog.Find(integration.GatewayCode);

        return new(
            integration.Id,
            integration.ResolveName(languageCode),
            integration.NameAr,
            integration.NameEn,
            integration.ResolveDescription(languageCode),
            integration.DescriptionAr,
            integration.DescriptionEn,
            integration.GatewayCode,
            gateway?.Name ?? integration.GatewayCode,
            gateway?.Category,
            integration.Mode,
            integration.Mode.ToString(),
            integration.SortOrder,
            integration.IsActive,
            methodCount);
    }
}

public sealed class UpsertPaymentGatewayIntegrationCommandValidator
    : AbstractValidator<UpsertPaymentGatewayIntegrationCommand>
{
    public UpsertPaymentGatewayIntegrationCommandValidator()
    {
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DescriptionAr).NotEmpty()
            .WithMessage("Enter the Arabic description.").MaximumLength(2000);
        RuleFor(c => c.DescriptionEn).NotEmpty()
            .WithMessage("Enter the English description.").MaximumLength(2000);
        RuleFor(c => c.Mode).IsInEnum();
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);
        RuleFor(c => c.GatewayCode)
            .NotEmpty().WithMessage("Choose a payment gateway.")
            .Must(code => PaymentGatewayCatalog.Find(code) is not null)
            .WithMessage("That payment gateway is not in the catalogue.");
    }
}

// ----------------------------------------------------------------------------------- handlers

public sealed class PaymentGatewayIntegrationHandlers :
    IRequestHandler<ListPaymentGatewaysQuery, IReadOnlyList<PaymentGatewayDto>>,
    IRequestHandler<ListPaymentGatewayIntegrationsQuery, PagedResult<PaymentGatewayIntegrationDto>>,
    IRequestHandler<GetPaymentGatewayIntegrationQuery, PaymentGatewayIntegrationDto>,
    IRequestHandler<UpsertPaymentGatewayIntegrationCommand, PaymentGatewayIntegrationDto>,
    IRequestHandler<DeletePaymentGatewayIntegrationCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public PaymentGatewayIntegrationHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public Task<IReadOnlyList<PaymentGatewayDto>> Handle(
        ListPaymentGatewaysQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PaymentGatewayDto> gateways = PaymentGatewayCatalog.All
            .Select(gateway => PaymentGatewayDto.From(gateway, _lookups.Language))
            .ToList();
        return Task.FromResult(gateways);
    }

    public async Task<PagedResult<PaymentGatewayIntegrationDto>> Handle(
        ListPaymentGatewayIntegrationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = string.IsNullOrWhiteSpace(request.GatewayCode)
            ? _db.PaymentGatewayIntegrations
            : _db.PaymentGatewayIntegrations.Where(i => i.GatewayCode == request.GatewayCode);

        var page = await _lookups.ListAsync(source, request, integration => integration, cancellationToken);
        var counts = await CountMethodsAsync(page.Items.Select(i => i.Id).ToList(), cancellationToken);

        return new PagedResult<PaymentGatewayIntegrationDto>(
            page.Items
                .Select(i => PaymentGatewayIntegrationDto.From(
                    i,
                    _lookups.Language,
                    counts.GetValueOrDefault(i.Id)))
                .ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<PaymentGatewayIntegrationDto> Handle(
        GetPaymentGatewayIntegrationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var integration = await _db.PaymentGatewayIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(PaymentGatewayIntegration), request.Id);

        var counts = await CountMethodsAsync([integration.Id], cancellationToken);
        return PaymentGatewayIntegrationDto.From(
            integration,
            _lookups.Language,
            counts.GetValueOrDefault(integration.Id));
    }

    public async Task<PaymentGatewayIntegrationDto> Handle(
        UpsertPaymentGatewayIntegrationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gateway = PaymentGatewayCatalog.Find(request.GatewayCode)!;

        PaymentGatewayIntegration? integration = null;
        if (request.Id is { } id && id != Guid.Empty)
        {
            integration = await _lookups.RequireAsync(_db.PaymentGatewayIntegrations, id, cancellationToken);
        }

        var integrationId = integration?.Id ?? Guid.Empty;
        await _lookups.EnsureUniqueAsync(
            _db.PaymentGatewayIntegrations,
            other => other.Id != integrationId
                && (other.NameEn == request.NameEn || other.NameAr == request.NameAr),
            "payment_integration.duplicate_name",
            "An integration with this name already exists.",
            cancellationToken);

        // Changing gateway on an integration methods already use would leave those methods holding
        // settings for a gateway nobody asked for.
        if (integration is not null
            && integration.GatewayCode != gateway.Code
            && await _db.PaymentMethods.AnyAsync(m => m.GatewayIntegrationId == integration.Id, cancellationToken))
        {
            throw new ConflictException(
                "payment_integration.gateway_in_use",
                "Payment methods are configured against this integration's gateway. Detach them before changing it.");
        }

        if (integration is null)
        {
            integration = new PaymentGatewayIntegration
            {
                NameAr = request.NameAr,
                NameEn = request.NameEn,
                GatewayCode = gateway.Code,
            };
            _db.PaymentGatewayIntegrations.Add(integration);
        }

        integration.NameAr = request.NameAr.Trim();
        integration.NameEn = request.NameEn.Trim();
        integration.DescriptionAr = request.DescriptionAr!.Trim();
        integration.DescriptionEn = request.DescriptionEn!.Trim();
        integration.GatewayCode = gateway.Code;
        integration.Mode = request.Mode;
        integration.SortOrder = request.SortOrder;
        integration.IsActive = request.IsActive;
        integration.UpdatedAtUtc = DateTime.UtcNow;

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            "PaymentGatewayIntegration.Saved",
            nameof(PaymentGatewayIntegration),
            integration.Id,
            new
            {
                integration.NameEn,
                Gateway = gateway.Code,
                Mode = integration.Mode.ToString(),
                integration.IsActive,
            },
            cancellationToken);

        var counts = await CountMethodsAsync([integration.Id], cancellationToken);
        return PaymentGatewayIntegrationDto.From(
            integration,
            _lookups.Language,
            counts.GetValueOrDefault(integration.Id));
    }

    public Task<LookupDeleteOutcome> Handle(
        DeletePaymentGatewayIntegrationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _lookups.DeleteOrDeactivateAsync(
            _db.PaymentGatewayIntegrations,
            request.Id,
            ct => _db.PaymentMethods.AnyAsync(m => m.GatewayIntegrationId == request.Id, ct),
            "PaymentGatewayIntegration",
            cancellationToken);
    }

    private async Task<Dictionary<Guid, int>> CountMethodsAsync(
        List<Guid> ids,
        CancellationToken cancellationToken) =>
        await _db.PaymentMethods
            .AsNoTracking()
            .Where(m => m.GatewayIntegrationId != null && ids.Contains(m.GatewayIntegrationId.Value))
            .GroupBy(m => m.GatewayIntegrationId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Id, row => row.Count, cancellationToken);
}
