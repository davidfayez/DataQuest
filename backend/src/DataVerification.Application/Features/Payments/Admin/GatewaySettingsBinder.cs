using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using DataVerification.Domain.Payments;
using FluentValidation.Results;
using ValidationException = DataVerification.Application.Common.Exceptions.ValidationException;

namespace DataVerification.Application.Features.Payments.Admin;

/// <summary>The two JSON columns a gateway's values live in, read and written as text dictionaries.</summary>
internal static class GatewaySettingsJson
{
    public static Dictionary<string, string> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // Unreadable rather than fatal: an operator can retype the settings, and a save
            // rewrites the column.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public static string Write(IReadOnlyDictionary<string, string> values) =>
        JsonSerializer.Serialize(values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value));
}

/// <summary>
/// Checks what was submitted for a gateway against that gateway's own rules, and works out what
/// will be stored.
///
/// Settings are replaced outright. Secrets are applied key by key: a key left out keeps what is
/// stored, an empty value removes it, anything else replaces it — so an edit that never touched a
/// secret cannot wipe it. Every problem is reported at once, each against its own field.
/// </summary>
internal static class GatewaySettingsBinder
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    public static (Dictionary<string, string> Settings, Dictionary<string, string> Secrets) Resolve(
        PaymentGatewayDefinition gateway,
        IReadOnlyDictionary<string, string?>? submittedSettings,
        IReadOnlyDictionary<string, string?>? submittedSecrets,
        IReadOnlyDictionary<string, string> storedSecrets,
        PaymentIntegrationMode mode,
        ISecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(protector);

        var failures = new List<ValidationFailure>();
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        var postedSettings = submittedSettings ?? new Dictionary<string, string?>();
        var postedSecrets = submittedSecrets ?? new Dictionary<string, string?>();

        foreach (var field in gateway.Fields)
        {
            if (field.Type == GatewayFieldType.Secret)
            {
                ResolveSecret(field, postedSecrets, storedSecrets, protector, secrets, failures);
                continue;
            }

            postedSettings.TryGetValue(field.Key, out var raw);
            var value = raw?.Trim() ?? string.Empty;

            if (field.Type == GatewayFieldType.Boolean)
            {
                if (value.Length > 0 && value is not ("true" or "false"))
                {
                    failures.Add(Fail(field, $"{field.LabelEn} must be true or false."));
                }
                else if (value.Length > 0)
                {
                    settings[field.Key] = value;
                }

                continue;
            }

            if (value.Length == 0)
            {
                if (field.IsRequired)
                {
                    failures.Add(Fail(field, $"{field.LabelEn} is required.", "payment_integration.field_required"));
                }

                continue;
            }

            var problem = Check(field, value, mode);
            if (problem is not null)
            {
                failures.Add(Fail(field, problem));
                continue;
            }

            settings[field.Key] = value;
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return (settings, secrets);
    }

    private static void ResolveSecret(
        GatewayField field,
        IReadOnlyDictionary<string, string?> posted,
        IReadOnlyDictionary<string, string> stored,
        ISecretProtector protector,
        Dictionary<string, string> secrets,
        List<ValidationFailure> failures)
    {
        if (posted.TryGetValue(field.Key, out var submitted))
        {
            if (!string.IsNullOrWhiteSpace(submitted))
            {
                var trimmed = submitted.Trim();

                if (trimmed.Length > field.MaxLength)
                {
                    failures.Add(Fail(field, $"{field.LabelEn} must be {field.MaxLength} characters or fewer."));
                    return;
                }

                // Storing it unencrypted is never an option: refused rather than saved in the clear.
                if (!protector.IsEnabled)
                {
                    failures.Add(Fail(
                        field,
                        "Secrets cannot be saved: the server has no encryption key configured.",
                        "payment_integration.encryption_unavailable"));
                    return;
                }

                secrets[field.Key] = protector.Protect(trimmed)!;
            }

            // An empty value removes it: nothing is carried across.
        }
        else if (stored.TryGetValue(field.Key, out var kept))
        {
            secrets[field.Key] = kept;
        }

        if (field.IsRequired && !secrets.ContainsKey(field.Key))
        {
            failures.Add(Fail(field, $"{field.LabelEn} is required.", "payment_integration.field_required"));
        }
    }

    private static string? Check(GatewayField field, string value, PaymentIntegrationMode mode)
    {
        if (value.Length > field.MaxLength)
        {
            return $"{field.LabelEn} must be {field.MaxLength} characters or fewer.";
        }

        switch (field.Type)
        {
            case GatewayFieldType.Url:
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return $"{field.LabelEn} must be a full http(s) address.";
                }

                // A live gateway talking over plain http would expose every payment on the way.
                if (mode == PaymentIntegrationMode.Live && uri.Scheme != Uri.UriSchemeHttps)
                {
                    return $"{field.LabelEn} must use https in live mode.";
                }

                break;

            case GatewayFieldType.Email:
                if (!Regex.IsMatch(value, "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.None, PatternTimeout))
                {
                    return $"{field.LabelEn} must be an email address.";
                }

                break;

            case GatewayFieldType.Number:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    return $"{field.LabelEn} must be a number.";
                }

                break;

            case GatewayFieldType.Select:
                if (!field.Options.Any(option => option.Value == value))
                {
                    return $"{field.LabelEn} must be one of the listed options.";
                }

                break;
        }

        if (field.Pattern is not null)
        {
            try
            {
                if (!Regex.IsMatch(value, field.Pattern, RegexOptions.IgnoreCase, PatternTimeout))
                {
                    return $"{field.LabelEn} is not in the expected format.";
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return $"{field.LabelEn} is not in the expected format.";
            }
        }

        return null;
    }

    private static ValidationFailure Fail(
        GatewayField field,
        string message,
        string code = "payment_integration.field_invalid") =>
        new($"Fields.{field.Key}", message) { ErrorCode = code };
}
