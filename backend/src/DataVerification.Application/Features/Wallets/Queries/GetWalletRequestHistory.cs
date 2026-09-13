using System.Text.Json;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Wallets.Queries;

/// <summary>
/// The trail of one wallet request: raised, decided, and by whom.
///
/// Read from the audit log rather than a second history table — the events are already written
/// there by the pipeline, and a parallel table would be one more thing to keep in step. Scoped to
/// a single request so a reviewer can read it with <c>Orders.View</c> alone, without being handed
/// the platform-wide audit trail.
/// </summary>
public sealed record GetWalletRequestHistoryQuery(Guid RequestId)
    : IRequest<IReadOnlyList<WalletRequestHistoryEntryDto>>;

/// <param name="Action">The audit action, e.g. <c>WalletRequest.Approved</c>.</param>
/// <param name="Status">
/// The status this entry moved the request to, where the action implies one. Null for entries that
/// changed no status, so the UI can render those differently rather than inventing a state.
/// </param>
/// <param name="Details">
/// Flattened key/value pairs from the audit snapshot, ready to render. Ordered as written.
/// </param>
public sealed record WalletRequestHistoryEntryDto(
    Guid Id,
    string Action,
    WalletRequestStatus? Status,
    string? StatusName,
    ActorType ActorType,
    string? ActorName,
    IReadOnlyList<WalletRequestHistoryDetailDto> Details,
    DateTime CreatedAtUtc);

public sealed record WalletRequestHistoryDetailDto(string Key, string Value);

public sealed class GetWalletRequestHistoryQueryHandler
    : IRequestHandler<GetWalletRequestHistoryQuery, IReadOnlyList<WalletRequestHistoryEntryDto>>
{
    /// <summary>
    /// Which status each audit action landed the request in. Actions that move no status are
    /// absent rather than mapped to Pending, so "nothing changed" stays distinguishable.
    /// </summary>
    private static readonly Dictionary<string, WalletRequestStatus> StatusByAction = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["WalletRequest.Created"] = WalletRequestStatus.Pending,
        ["WalletRequest.Approved"] = WalletRequestStatus.Approved,
        ["WalletRequest.Rejected"] = WalletRequestStatus.Rejected,
        ["WalletRequest.Cancelled"] = WalletRequestStatus.Cancelled,
    };

    /// <summary>
    /// Audit snapshots carry ids and internals nobody reading a history needs. Only these keys are
    /// surfaced, which also stops a future field leaking into the UI unreviewed.
    /// </summary>
    private static readonly string[] ShownKeys =
    [
        "Type", "Amount", "ClaimedAmount", "ClaimedReference", "Reference",
        "ConfirmedAmount", "ConfirmedReference", "Method", "Files", "Balance", "ReviewerNote",
    ];

    private readonly IApplicationDbContext _db;

    public GetWalletRequestHistoryQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<WalletRequestHistoryEntryDto>> Handle(
        GetWalletRequestHistoryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var exists = await _db.WalletRequests
            .AnyAsync(r => r.Id == request.RequestId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException(nameof(WalletRequest), request.RequestId);
        }

        var entries = await _db.AuditLog
            .AsNoTracking()
            .Where(entry => entry.EntityType == "WalletRequest" && entry.EntityId == request.RequestId)
            .OrderBy(entry => entry.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return entries.Select(ToDto).ToList();
    }

    private static WalletRequestHistoryEntryDto ToDto(AuditLogEntry entry)
    {
        var status = StatusByAction.TryGetValue(entry.Action, out var mapped)
            ? mapped
            : (WalletRequestStatus?)null;

        return new WalletRequestHistoryEntryDto(
            entry.Id,
            entry.Action,
            status,
            status?.ToString(),
            entry.ActorType,
            entry.ActorName,
            ReadDetails(entry.Data),
            entry.CreatedAtUtc);
    }

    /// <summary>
    /// Pulls the readable values out of the audit snapshot. Malformed JSON yields nothing rather
    /// than throwing: a history that cannot render one entry must still show the rest.
    /// </summary>
    private static List<WalletRequestHistoryDetailDto> ReadDetails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            // Audit snapshots are written with web defaults, so their keys are camelCase while the
            // allow-list reads as the property names it came from. Matched case-insensitively
            // rather than duplicating every key in both spellings.
            var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                properties[property.Name] = property.Value;
            }

            var details = new List<WalletRequestHistoryDetailDto>();

            foreach (var key in ShownKeys)
            {
                if (!properties.TryGetValue(key, out var value))
                {
                    continue;
                }

                var text = value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    JsonValueKind.String => value.GetString(),
                    _ => value.ToString(),
                };

                // Direction is serialised as the enum's number. Nobody reading a history knows
                // what "0" means, so it is named here rather than left for each client to decode.
                if (key == "Type"
                    && int.TryParse(text, out var direction)
                    && Enum.IsDefined(typeof(WalletRequestType), direction))
                {
                    text = ((WalletRequestType)direction).ToString();
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    details.Add(new WalletRequestHistoryDetailDto(key, text));
                }
            }

            return details;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
