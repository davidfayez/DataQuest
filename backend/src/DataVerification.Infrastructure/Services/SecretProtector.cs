using System.Security.Cryptography;
using System.Text;
using DataVerification.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataVerification.Infrastructure.Services;

/// <summary>Bound from the <c>Security</c> configuration section.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Key for every reversibly-encrypted secret the platform stores — applicant passwords and the
    /// operator-managed SendGrid API key. Either 32 bytes base64-encoded (preferred —
    /// <c>openssl rand -base64 32</c>) or a passphrase of at least 16 characters, which is
    /// stretched to 32 bytes. Left empty, those features are simply switched off.
    /// This is a secret: supply it as <c>Security__EncryptionKey</c> or user-secrets, and never
    /// commit it. Changing it makes everything stored under the old key unreadable.
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// The name this setting shipped under when it only protected passwords. Still honoured so an
    /// environment that already sets it keeps working; <see cref="EncryptionKey"/> wins when both
    /// are present.
    /// </summary>
    public string PasswordEncryptionKey { get; set; } = string.Empty;

    /// <summary>Whichever of the two names was supplied.</summary>
    public string ResolvedKey =>
        string.IsNullOrWhiteSpace(EncryptionKey) ? PasswordEncryptionKey : EncryptionKey;
}

/// <summary>
/// AES-256-GCM protection for secrets the platform must be able to read back. A configured key
/// rather than a DataProtection key ring, because the API is deployed to container hosts with no
/// persistent volume — a key ring regenerated on redeploy would leave every stored secret
/// unreadable.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    /// <summary>Envelope layout: version | nonce | tag | ciphertext, base64-encoded.</summary>
    private const byte Version = 1;

    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int HeaderSizeBytes = 1 + NonceSizeBytes + TagSizeBytes;
    private const int MinimumPassphraseLength = 16;

    private readonly byte[]? _key;
    private readonly ILogger<SecretProtector> _logger;

    public SecretProtector(IOptions<SecurityOptions> options, ILogger<SecretProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _key = DeriveKey(options.Value.ResolvedKey);

        if (_key is null)
        {
            logger.LogWarning(
                "Security:EncryptionKey is not configured. Applicant passwords are stored as a hash "
                + "only and cannot be revealed, and the SendGrid API key cannot be managed from the "
                + "admin panel.");
        }
    }

    public bool IsEnabled => _key is not null;

    public string? Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        if (_key is null)
        {
            return null;
        }

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var envelope = new byte[HeaderSizeBytes + plainBytes.Length];
        envelope[0] = Version;

        // A fresh nonce per secret: GCM is only secure while a nonce is never reused under a key.
        var nonce = envelope.AsSpan(1, NonceSizeBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Encrypt(
            nonce,
            plainBytes,
            envelope.AsSpan(HeaderSizeBytes),
            envelope.AsSpan(1 + NonceSizeBytes, TagSizeBytes));

        return Convert.ToBase64String(envelope);
    }

    public string? Unprotect(string? protectedValue)
    {
        if (_key is null || string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        byte[] envelope;
        try
        {
            envelope = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException)
        {
            return null;
        }

        if (envelope.Length <= HeaderSizeBytes || envelope[0] != Version)
        {
            return null;
        }

        var cipherLength = envelope.Length - HeaderSizeBytes;
        var plainBytes = new byte[cipherLength];

        try
        {
            using var aes = new AesGcm(_key, TagSizeBytes);
            aes.Decrypt(
                envelope.AsSpan(1, NonceSizeBytes),
                envelope.AsSpan(HeaderSizeBytes, cipherLength),
                envelope.AsSpan(1 + NonceSizeBytes, TagSizeBytes),
                plainBytes);
        }
        catch (CryptographicException)
        {
            // Written under a different key, or tampered with. Either way it is unreadable, and an
            // unreadable secret is a "cannot show you this", not a server fault.
            _logger.LogWarning(
                "A stored secret could not be decrypted with the configured key. It was most likely "
                + "written before Security:EncryptionKey was changed.");
            return null;
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Accepts the key either as base64 of exactly 32 bytes, or as a passphrase that is hashed to
    /// 32 bytes. A too-short passphrase throws rather than quietly protecting nothing.
    /// </summary>
    private static byte[]? DeriveKey(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var trimmed = configured.Trim();

        Span<byte> decoded = stackalloc byte[KeySizeBytes];
        if (Convert.TryFromBase64String(trimmed, decoded, out var written) && written == KeySizeBytes)
        {
            return decoded.ToArray();
        }

        if (trimmed.Length < MinimumPassphraseLength)
        {
            throw new InvalidOperationException(
                "Security:EncryptionKey must be 32 bytes base64-encoded, or a passphrase of at "
                + $"least {MinimumPassphraseLength} characters.");
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
    }
}
