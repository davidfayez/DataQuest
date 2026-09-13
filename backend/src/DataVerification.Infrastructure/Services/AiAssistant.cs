using System.Net.Http.Json;
using System.Text.Json;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataVerification.Infrastructure.Services;

/// <summary>
/// Keeps the assistant's provider settings in <c>SiteSettings</c> with the key encrypted, so an
/// operator can set and rotate it from the admin panel without a redeploy.
///
/// Mirrors how the SendGrid key is stored — see <see cref="EmailSettingsStore"/> — including the
/// rule that the key is never read back out to the panel. The panel is told whether one is present
/// and nothing more.
/// </summary>
public sealed class AiSettingsStore : IAiSettingsStore
{
    public const string ApiKeySettingKey = "ai.gemini.apiKey";
    public const string EnabledSettingKey = "ai.enabled";
    public const string ModelSettingKey = "ai.model";

    /// <summary>Gemini's free tier. Fast, and enough for answering from a page of guides.</summary>
    public const string DefaultModel = "gemini-3.6-flash";

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;

    public AiSettingsStore(IApplicationDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<AiSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await ReadAsync(cancellationToken);

        return new AiSettings(
            IsEnabled: string.Equals(rows.GetValueOrDefault(EnabledSettingKey), "true", StringComparison.OrdinalIgnoreCase),
            Model: Blank(rows.GetValueOrDefault(ModelSettingKey)) ? DefaultModel : rows[ModelSettingKey],
            HasApiKey: !Blank(rows.GetValueOrDefault(ApiKeySettingKey)));
    }

    /// <summary>The decrypted key, or null when none is stored or protection is unavailable.</summary>
    public async Task<string?> GetApiKeyAsync(CancellationToken cancellationToken = default)
    {
        var rows = await ReadAsync(cancellationToken);
        var stored = rows.GetValueOrDefault(ApiKeySettingKey);

        return Blank(stored) ? null : _protector.Unprotect(stored!);
    }

    public async Task SaveAsync(
        AiSettings settings,
        string? apiKey,
        bool clearApiKey = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await SetAsync(EnabledSettingKey, settings.IsEnabled ? "true" : "false", cancellationToken);
        await SetAsync(
            ModelSettingKey,
            string.IsNullOrWhiteSpace(settings.Model) ? DefaultModel : settings.Model.Trim(),
            cancellationToken);

        if (clearApiKey && string.IsNullOrWhiteSpace(apiKey))
        {
            var row = await _db.SiteSettings
                .FirstOrDefaultAsync(s => s.Key == ApiKeySettingKey, cancellationToken);

            if (row is not null) _db.SiteSettings.Remove(row);
        }

        // A blank key means "leave the stored one alone", which is what lets the panel save the
        // model or the on/off switch without the operator pasting the key again every time.
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var protectedKey = _protector.Protect(apiKey.Trim())
                ?? throw new InvalidOperationException(
                    "No encryption key is configured, so the API key cannot be stored safely.");

            await SetAsync(ApiKeySettingKey, protectedKey, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken cancellationToken)
    {
        string[] keys = [ApiKeySettingKey, EnabledSettingKey, ModelSettingKey];

        return await _db.SiteSettings
            .AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
    }

    private async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        var row = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (row is null) _db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
        else row.Value = value;
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}

/// <summary>
/// Answers questions with Google's Gemini API.
///
/// Gemini rather than OpenAI because its API has a free tier that needs no billing account, which
/// is the whole of the practical difference for this feature. The call is made from the server so
/// the key stays here; nothing about the provider reaches the browser.
///
/// A provider that is slow, over quota or misconfigured must not take the page down, so every
/// failure is logged and answered with a plain sentence the visitor can act on.
/// </summary>
public sealed class GeminiAssistant : IAiAssistant
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models";

    /// <summary>Long enough for a considered answer, short enough that a hung provider is not a hung page.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    private readonly HttpClient _http;
    private readonly AiSettingsStore _settings;
    private readonly ILogger<GeminiAssistant> _logger;

    public GeminiAssistant(HttpClient http, AiSettingsStore settings, ILogger<GeminiAssistant> logger)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
        _http.Timeout = Timeout;
        _settings = settings;
        _logger = logger;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        return settings.IsEnabled && settings.HasApiKey;
    }

    public async Task<AiReply> AskAsync(
        string question,
        string? language,
        string context,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        var key = await _settings.GetApiKeyAsync(cancellationToken);

        if (!settings.IsEnabled || string.IsNullOrWhiteSpace(key))
        {
            return Nothing;
        }

        var url = $"{Endpoint}/{settings.Model}:generateContent?key={Uri.EscapeDataString(key)}";
        var prompt = BuildPrompt(question, language, context, searchTheWeb: true);

        try
        {
            var grounded = await SendAsync(url, prompt, searchTheWeb: true, cancellationToken);

            if (grounded is not null)
            {
                return new AiReply(ExtractText(grounded.Value), ExtractWebSources(grounded.Value));
            }

            // Searching the web is billed separately from the model, and a key on the free tier is
            // refused for it while ordinary questions still work. Falling back keeps the assistant
            // answering from the platform's own guides instead of going silent over a feature the
            // account does not have — and it starts citing the web by itself the day billing is on.
            var fallback = await SendAsync(
                url,
                BuildPrompt(question, language, context, searchTheWeb: false),
                searchTheWeb: false,
                cancellationToken);

            return fallback is null ? Nothing : new AiReply(ExtractText(fallback.Value), []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Gemini could not be reached.");
            return Nothing;
        }
    }

    /// <summary>
    /// One call to the provider. Returns null when it refused, having logged why — the caller
    /// decides whether that is worth another attempt.
    /// </summary>
    private async Task<JsonElement?> SendAsync(
        string url,
        string prompt,
        bool searchTheWeb,
        CancellationToken cancellationToken)
    {
        object body = searchTheWeb
            ? new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                // Google runs the search and reports which pages were used: no second provider and
                // no second key, at the cost of a billed account.
                tools = new[] { new { google_search = new { } } },
                // Generous, because the current models spend part of this budget reasoning before
                // they write: a cap sized for the reply alone gets a truncated one.
                generationConfig = new { temperature = 0.2, maxOutputTokens = 2048 },
            }
            : new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.2, maxOutputTokens = 2048 },
            };

        var response = await _http.PostAsJsonAsync(url, body, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }

        // The body carries the provider's reason — over quota, bad key — which belongs in the log
        // for an operator, never in the answer shown to a visitor.
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Gemini refused the request (web search: {Search}): {Status} {Body}",
            searchTheWeb,
            (int)response.StatusCode,
            detail);

        return null;
    }

    /// <summary>Nothing to say, from any of the several reasons there might be nothing to say.</summary>
    private static AiReply Nothing => new(string.Empty, []);

    /// <summary>
    /// Puts the platform's own guides first and the open web second.
    ///
    /// The order matters. Anything about *this* platform — its process, prices, turnaround — must
    /// come from its own guides, because the web's answer about document verification in general
    /// would be confidently wrong about this company in particular. The web is for the rest: what
    /// an apostille is, what a ministry requires, questions the guides were never going to cover.
    /// Saying which of the two an answer came from is what lets a reader judge it.
    /// </summary>
    private static string BuildPrompt(
        string question,
        string? language,
        string context,
        bool searchTheWeb) =>
        $"""
        You are the assistant on a document verification platform. Answer the visitor's question.

        Use the platform's own guides below for anything about this platform — how it works, what
        it charges, how long it takes, what it verifies. Never contradict them and never invent a
        process, a price or a turnaround time for this platform; if the guides do not cover such a
        question, say so and suggest contacting support.

        {(searchTheWeb
            ? """
              For general questions the guides were not written to answer — what a document or a
              procedure is, what an authority in some country requires — search the web and answer
              from what you find. Say plainly when an answer came from the web rather than from
              this platform's guides.
              """
            : """
              You cannot search the web. For a general question the guides do not answer, say what
              you can from general knowledge, make clear it is not from this platform's guides, and
              suggest contacting support to confirm anything they intend to act on.
              """)}

        Reply in the language with code "{language ?? "en"}", in at most three short paragraphs,
        as plain prose without Markdown and without inline citation markers.

        # The platform's guides
        {(string.IsNullOrWhiteSpace(context) ? "(No guides have been published yet.)" : context)}

        # The visitor's question
        {question}
        """;

    /// <summary>
    /// The pages a grounded answer drew on, from Gemini's grounding metadata.
    ///
    /// Absent metadata means the model answered without searching, which is the common case and not
    /// a failure. Duplicates are collapsed: one page cited for three sentences is still one link.
    /// </summary>
    private static IReadOnlyList<AiWebSource> ExtractWebSources(JsonElement payload)
    {
        if (!payload.TryGetProperty("candidates", out var candidates)) return [];

        var sources = new List<AiWebSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("groundingMetadata", out var metadata)) continue;
            if (!metadata.TryGetProperty("groundingChunks", out var chunks)) continue;

            foreach (var chunk in chunks.EnumerateArray())
            {
                if (!chunk.TryGetProperty("web", out var web)) continue;

                var url = web.TryGetProperty("uri", out var uri) ? uri.GetString() : null;
                if (string.IsNullOrWhiteSpace(url) || !seen.Add(url)) continue;

                var title = web.TryGetProperty("title", out var name) ? name.GetString() : null;
                sources.Add(new AiWebSource(
                    string.IsNullOrWhiteSpace(title) ? url : title!,
                    url));
            }
        }

        return sources;
    }

    /// <summary>
    /// Pulls the prose out of Gemini's envelope, tolerating a response that carries no text at all
    /// — a blocked or empty candidate is a normal outcome, not an exception.
    /// </summary>
    private static string ExtractText(JsonElement payload)
    {
        if (!payload.TryGetProperty("candidates", out var candidates)) return string.Empty;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)) continue;
            if (!content.TryGetProperty("parts", out var parts)) continue;

            var text = string.Concat(
                parts.EnumerateArray()
                    .Select(part => part.TryGetProperty("text", out var value) ? value.GetString() : null));

            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }

        return string.Empty;
    }
}
