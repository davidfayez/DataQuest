using System.Security.Cryptography;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Infrastructure.Services;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Wraps ASP.NET Identity's PBKDF2 hasher so both identity realms share one implementation and
/// the Application layer never references an Identity type.
/// </summary>
public sealed class PasswordHashingService : IPasswordHashingService
{
    // The hasher is generic over a user type it never inspects, so any reference type will do.
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object HashSubject = new();

    public string Hash(string password) => _hasher.HashPassword(HashSubject, password);

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            var result = _hasher.VerifyHashedPassword(HashSubject, hash, password);
            return result is PasswordVerificationResult.Success
                or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            // A malformed stored hash is a failed verification, not a server error.
            return false;
        }
    }
}

/// <summary>
/// Generates the applicant's one-time password: cryptographically random, and guaranteed to
/// contain at least one upper-case letter, one lower-case letter and one digit.
/// </summary>
public sealed class PasswordGenerator : IPasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string All = Upper + Lower + Digits;

    public string Generate(int length = 8)
    {
        if (length < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "A password needs at least three characters to satisfy the complexity rule.");
        }

        // Seed one character from each required class, fill the rest at random, then shuffle so
        // the guaranteed characters are not always in the same positions.
        var characters = new char[length];
        characters[0] = PickRandom(Upper);
        characters[1] = PickRandom(Lower);
        characters[2] = PickRandom(Digits);

        for (var i = 3; i < length; i++)
        {
            characters[i] = PickRandom(All);
        }

        RandomNumberGenerator.Shuffle(characters.AsSpan());
        return new string(characters);
    }

    private static char PickRandom(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}

/// <summary>
/// Generates the order number as the owning client's code followed by nine random digits, so the
/// number identifies its client on sight — <c>NEN</c> + <c>408125993</c> = <c>NEN408125993</c>.
/// </summary>
public sealed class OrderNumberGenerator : IOrderNumberGenerator
{
    /// <summary>Digits appended after the client code.</summary>
    private const int DigitCount = 9;
    private const int MaxAttempts = 10;

    private readonly IApplicationDbContext _db;

    public OrderNumberGenerator(IApplicationDbContext db) => _db = db;

    public async Task<string> GenerateUniqueAsync(
        string clientCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientCode);

        var prefix = clientCode.Trim().ToUpperInvariant();

        // One billion numbers per client makes a collision unlikely but not impossible, and the
        // unique index turns one into a hard failure — so the check is worth the round trip.
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = Generate(prefix);

            var taken = await _db.Orders
                .AsNoTracking()
                .AnyAsync(o => o.OrderNumber == candidate, cancellationToken);

            if (!taken)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not generate a unique order number for client '{prefix}' after {MaxAttempts} attempts.");
    }

    private static string Generate(string prefix)
    {
        var digits = new char[DigitCount];
        for (var i = 0; i < DigitCount; i++)
        {
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        }

        return prefix + new string(digits);
    }
}

/// <summary>Writes audit rows for the current caller.</summary>
public sealed class AuditLogger : IAuditLogger
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AuditLogger(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        Guid? entityId,
        object? data = null,
        CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.ToActor();

        _db.AuditLog.Add(new AuditLogEntry
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ActorType = actor.Type,
            ActorId = actor.Id,
            ActorName = actor.DisplayName,
            Data = data is null ? null : System.Text.Json.JsonSerializer.Serialize(data, JsonOptions),
            IpAddress = _currentUser.IpAddress,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
