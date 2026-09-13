using System.Security.Cryptography;
using DataVerification.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Infrastructure.Services;

/// <summary>
/// Produces references of the form <c>TKT-YYMM-XXXXXX</c>, mirroring how applications are numbered.
///
/// The random tail matters more here than elsewhere: the contact form is open to anyone, so a
/// sequential reference would let a stranger walk the list and learn how much support the platform
/// is receiving — and, once a reference reaches an email subject line, guess at others.
/// </summary>
public sealed class TicketNumberGenerator : ITicketNumberGenerator
{
    // No I, O, 0 or 1: these get read aloud and retyped by people quoting them over the phone.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int RandomLength = 6;
    private const int MaxAttempts = 10;

    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public TicketNumberGenerator(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string> GenerateUniqueAsync(CancellationToken cancellationToken = default)
    {
        var prefix = $"TKT-{_clock.UtcNow:yyMM}-";

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = prefix + RandomTail();

            var taken = await _db.Tickets
                .AsNoTracking()
                .AnyAsync(t => t.TicketNumber == candidate, cancellationToken);

            if (!taken)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not generate a unique ticket number after {MaxAttempts} attempts.");
    }

    private static string RandomTail()
    {
        var characters = new char[RandomLength];
        for (var i = 0; i < RandomLength; i++)
        {
            characters[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(characters);
    }
}
