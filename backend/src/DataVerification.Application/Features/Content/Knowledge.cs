using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content;

// -------------------------------------------------------------------- Public DTOs

/// <summary>One tool or guide that matched a search.</summary>
/// <param name="Snippet">
/// The words around the match, so the reader can see why this came back without opening it.
/// </param>
public sealed record KnowledgeHitDto(
    Guid Id,
    string Kind,
    string Name,
    string Snippet,
    string? VideoUrl,
    string? ImageUrl);

/// <summary>What the assistant made of a question, and whether it was in a position to answer.</summary>
/// <param name="IsConfigured">
/// False when no API key has been set. The site says so plainly rather than showing an empty answer
/// or a provider error a visitor cannot act on.
/// </param>
/// <param name="Sources">The platform's own guides the answer drew on.</param>
/// <param name="WebSources">
/// Pages on the open web it used, when the guides did not cover the question. Empty when it
/// answered from the guides alone.
/// </param>
public sealed record AiAnswerDto(
    string Answer,
    bool IsConfigured,
    IReadOnlyList<KnowledgeHitDto> Sources,
    IReadOnlyList<AiWebSourceDto> WebSources);

public sealed record AiWebSourceDto(string Title, string Url);

// ------------------------------------------------------------------------ Queries

public sealed record SearchKnowledgeQuery(string Query) : IRequest<IReadOnlyList<KnowledgeHitDto>>;

public sealed record AskKnowledgeQuery(string Question) : IRequest<AiAnswerDto>;

// --------------------------------------------------------------------- Validation

public sealed class SearchKnowledgeQueryValidator : AbstractValidator<SearchKnowledgeQuery>
{
    public SearchKnowledgeQueryValidator()
    {
        RuleFor(q => q.Query).NotEmpty().WithMessage("Type something to search for.").MaximumLength(200);
    }
}

public sealed class AskKnowledgeQueryValidator : AbstractValidator<AskKnowledgeQuery>
{
    public AskKnowledgeQueryValidator()
    {
        RuleFor(q => q.Question)
            .NotEmpty().WithMessage("Type a question.")
            // Long enough for a real question, short enough that the endpoint cannot be used to
            // push a document through somebody else's AI quota.
            .MaximumLength(500);
    }
}

// ----------------------------------------------------------------------- Handlers

public sealed class KnowledgeHandlers :
    IRequestHandler<SearchKnowledgeQuery, IReadOnlyList<KnowledgeHitDto>>,
    IRequestHandler<AskKnowledgeQuery, AiAnswerDto>
{
    /// <summary>Characters of context either side of a match.</summary>
    private const int SnippetPadding = 90;

    /// <summary>Enough for the assistant to answer from, short enough to keep the prompt cheap.</summary>
    private const int MaxSources = 6;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAiAssistant _assistant;

    public KnowledgeHandlers(IApplicationDbContext db, ICurrentUser currentUser, IAiAssistant assistant)
    {
        _db = db;
        _currentUser = currentUser;
        _assistant = assistant;
    }

    public async Task<IReadOnlyList<KnowledgeHitDto>> Handle(
        SearchKnowledgeQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entries = await ShowableAsync(cancellationToken);
        return Match(entries, request.Query, _currentUser.LanguageCode).ToList();
    }

    public async Task<AiAnswerDto> Handle(AskKnowledgeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await _assistant.IsConfiguredAsync(cancellationToken))
        {
            return new AiAnswerDto(string.Empty, false, [], []);
        }

        var language = _currentUser.LanguageCode;
        var entries = await ShowableAsync(cancellationToken);

        // The assistant answers from this platform's own guides rather than from whatever it knows
        // about document verification in general: a confident answer about somebody else's process
        // is worse than no answer at all.
        var sources = Match(entries, request.Question, language).ToList();
        if (sources.Count == 0)
        {
            sources = entries
                .Take(MaxSources)
                .Select(e => ToHit(e, language, e.ResolveDescription(language)))
                .ToList();
        }

        var context = string.Join(
            "\n\n",
            sources.Take(MaxSources).Select(s => $"## {s.Name}\n{s.Snippet}"));

        var reply = await _assistant.AskAsync(request.Question, language, context, cancellationToken);

        return new AiAnswerDto(
            reply.Text,
            true,
            sources.Take(MaxSources).ToList(),
            reply.WebSources.Select(w => new AiWebSourceDto(w.Title, w.Url)).ToList());
    }

    // --------------------------------------------------------------------- Helpers

    private async Task<List<Domain.Entities.ToolResource>> ShowableAsync(CancellationToken cancellationToken)
    {
        var entries = await _db.ToolResources
            .AsNoTracking()
            .Where(t => t.IsPublished)
            .Include(t => t.Translations)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return entries.Where(t => t.IsShowable).ToList();
    }

    /// <summary>
    /// Matches in memory rather than in SQL.
    ///
    /// The guides are an admin-curated list of a few dozen rows at most, and the text lives in a
    /// per-language side table. Filtering here keeps the match independent of the database's
    /// collation — which decides, among other things, whether "Ali" finds "Alī" — and lets the same
    /// comparison build the snippet. It would be the wrong choice over a table that grows.
    /// </summary>
    private static IEnumerable<KnowledgeHitDto> Match(
        IEnumerable<Domain.Entities.ToolResource> entries,
        string query,
        string? language)
    {
        var needle = query.Trim();
        if (needle.Length == 0) yield break;

        foreach (var entry in entries)
        {
            var name = entry.ResolveName(language);
            var description = entry.ResolveDescription(language);

            var inName = name.Contains(needle, StringComparison.OrdinalIgnoreCase);
            var at = description.IndexOf(needle, StringComparison.OrdinalIgnoreCase);

            if (!inName && at < 0) continue;

            yield return ToHit(entry, language, at >= 0 ? Snippet(description, at, needle.Length) : description);
        }
    }

    private static KnowledgeHitDto ToHit(
        Domain.Entities.ToolResource entry,
        string? language,
        string snippet) => new(
        entry.Id,
        entry.Kind.ToString(),
        entry.ResolveName(language),
        Trim(snippet),
        entry.Kind == ToolResourceKind.Video ? entry.VideoUrl : null,
        entry.HasImage ? $"content/tools/{entry.Id}/image" : null);

    /// <summary>The words around a match, with an ellipsis where text was cut away.</summary>
    private static string Snippet(string text, int at, int length)
    {
        var start = Math.Max(0, at - SnippetPadding);
        var end = Math.Min(text.Length, at + length + SnippetPadding);

        var body = text[start..end].Trim();

        if (start > 0) body = $"…{body}";
        if (end < text.Length) body = $"{body}…";

        return body;
    }

    private static string Trim(string text) =>
        text.Length <= 400 ? text.Trim() : $"{text[..400].Trim()}…";
}
