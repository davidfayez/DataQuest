using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using FluentValidation;
using MediatR;

namespace DataVerification.Application.Features.Admin.Settings;

/// <summary>
/// What the panel shows for the AI assistant. The key is never returned — only whether one is
/// stored — so opening the page cannot leak it and a screenshot of it is harmless.
/// </summary>
/// <param name="CanStore">
/// False when the platform has no encryption key configured, so a provider key could not be stored
/// safely. The panel says so rather than letting an operator paste a key into a form that will
/// refuse it.
/// </param>
public sealed record AiAssistantSettingsDto(bool IsEnabled, string Model, bool HasApiKey, bool CanStore);

public sealed record GetAiAssistantSettingsQuery : IRequest<AiAssistantSettingsDto>;

/// <summary>
/// Saves the assistant's settings.
/// </summary>
/// <param name="ApiKey">
/// Blank leaves the stored key alone, which is what lets an operator flip the switch or change the
/// model without pasting the key again — and is the only way to save the page at all, since the
/// key is never sent back to it.
/// </param>
/// <param name="ClearApiKey">
/// Removes the stored key. Blank means "keep", so without this there would be no way to take a key
/// back out once it had been pasted in.
/// </param>
public sealed record UpdateAiAssistantSettingsCommand(
    bool IsEnabled,
    string Model,
    string? ApiKey,
    bool ClearApiKey = false) : IRequest<AiAssistantSettingsDto>;

public sealed class UpdateAiAssistantSettingsCommandValidator
    : AbstractValidator<UpdateAiAssistantSettingsCommand>
{
    public UpdateAiAssistantSettingsCommandValidator()
    {
        RuleFor(c => c.Model)
            .NotEmpty().WithMessage("Choose a model.")
            .MaximumLength(100)
            // Model names are a vendor identifier, not free text; anything else is a typo that
            // would only surface as a provider error on a visitor's first question.
            .Matches("^[A-Za-z0-9.:_-]+$")
            .WithMessage("A model name is letters, digits, dots and dashes.");

        RuleFor(c => c.ApiKey!)
            .MaximumLength(200)
            .When(c => !string.IsNullOrWhiteSpace(c.ApiKey));

        // Switching it on without a key would leave the site offering a button that cannot answer.
        RuleFor(c => c.IsEnabled)
            .Must((command, enabled) => !enabled || !string.IsNullOrWhiteSpace(command.ApiKey))
            .WithMessage("Add an API key before turning the assistant on.")
            .When(c => c.IsEnabled && c.ApiKey is not null);
    }
}

public sealed class AiAssistantSettingsHandlers :
    IRequestHandler<GetAiAssistantSettingsQuery, AiAssistantSettingsDto>,
    IRequestHandler<UpdateAiAssistantSettingsCommand, AiAssistantSettingsDto>
{
    private readonly IAiSettingsStore _store;
    private readonly ISecretProtector _protector;
    private readonly IAuditLogger _auditLogger;

    public AiAssistantSettingsHandlers(
        IAiSettingsStore store,
        ISecretProtector protector,
        IAuditLogger auditLogger)
    {
        _store = store;
        _protector = protector;
        _auditLogger = auditLogger;
    }

    public async Task<AiAssistantSettingsDto> Handle(
        GetAiAssistantSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetAsync(cancellationToken);
        return new AiAssistantSettingsDto(
            settings.IsEnabled, settings.Model, settings.HasApiKey, _protector.IsEnabled);
    }

    public async Task<AiAssistantSettingsDto> Handle(
        UpdateAiAssistantSettingsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = await _store.GetAsync(cancellationToken);

        // A key with nowhere safe to go is refused here, where the panel can explain it, rather
        // than deeper down where it would surface as an unexplained failure.
        if (!string.IsNullOrWhiteSpace(request.ApiKey) && !_protector.IsEnabled)
        {
            throw new Common.Exceptions.ValidationException(
                "The platform has no encryption key configured, so an API key cannot be stored.");
        }

        // Clearing the key and switching on in the same save would leave a button that cannot answer.
        var willHaveKey = request.ClearApiKey
            ? !string.IsNullOrWhiteSpace(request.ApiKey)
            : current.HasApiKey || !string.IsNullOrWhiteSpace(request.ApiKey);

        // Turning it on needs a key from somewhere — this request, or one saved earlier.
        if (request.IsEnabled && !willHaveKey)
        {
            throw new Common.Exceptions.ValidationException("Add an API key before turning the assistant on.");
        }

        await _store.SaveAsync(
            new AiSettings(request.IsEnabled, request.Model, current.HasApiKey),
            request.ApiKey,
            request.ClearApiKey,
            cancellationToken);

        // The key itself is never logged, only that it changed.
        await _auditLogger.LogAsync(
            "AiAssistantSettings.Updated",
            "SiteSetting",
            null,
            new { request.IsEnabled, request.Model, KeyChanged = !string.IsNullOrWhiteSpace(request.ApiKey) },
            cancellationToken);

        var saved = await _store.GetAsync(cancellationToken);
        return new AiAssistantSettingsDto(
            saved.IsEnabled, saved.Model, saved.HasApiKey, _protector.IsEnabled);
    }
}
