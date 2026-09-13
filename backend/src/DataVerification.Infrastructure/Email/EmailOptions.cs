namespace DataVerification.Infrastructure.Email;

/// <summary>Bound from the <c>Smtp</c> configuration section.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>When false the platform logs the message instead of dialling an SMTP server.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool UseStartTls { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = "no-reply@dataverification.local";

    public string FromName { get; set; } = "Data Verification";
}

/// <summary>
/// Bound from the <c>SendGrid</c> configuration section. When enabled (and an API key is present)
/// SendGrid becomes the transport for every transactional email, taking precedence over SMTP.
/// The API key is a secret and must be supplied out-of-band (environment variable
/// <c>SendGrid__ApiKey</c> or user-secrets), never committed to source control.
/// </summary>
public sealed class SendGridOptions
{
    public const string SectionName = "SendGrid";

    /// <summary>When true and an API key is configured, SendGrid delivers all outbound mail.</summary>
    public bool Enabled { get; set; }

    /// <summary>SendGrid API key (starts with <c>SG.</c>). Supplied via environment/user-secrets.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The verified "from" / no-reply sender address registered in SendGrid.</summary>
    public string FromAddress { get; set; } = "NoReply@followmetric.com";

    public string FromName { get; set; } = "NEN Verification";
}

/// <summary>Bound from the <c>App</c> configuration section.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Base URL of the applicant web app, used to build the login deep link.</summary>
    public string WebUrl { get; set; } = "http://localhost:5173";

    public string AdminUrl { get; set; } = "http://localhost:5174";

    public string DefaultClientCode { get; set; } = "NEN";

    public string[] SupportedLanguages { get; set; } =
        ["ar", "en", "ru", "tr", "uz", "de", "hi", "zh", "ja", "pl"];

    /// <summary>
    /// Development-only escape hatch that returns the generated credentials in the registration
    /// response so the flow can be exercised without reading a mailbox.
    /// </summary>
    public bool EchoCredentialsInResponse { get; set; }
}
