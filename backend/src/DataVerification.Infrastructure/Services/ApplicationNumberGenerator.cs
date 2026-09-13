using System.Security.Cryptography;
using DataVerification.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Infrastructure.Services;

/// <summary>
/// Produces references of the form <c>APP-YYMM-XXXXXX</c>. The date segment makes a reference
/// legible to support staff at a glance; the random tail keeps them unguessable so one applicant
/// cannot enumerate another's application numbers.
/// </summary>
public sealed class ApplicationNumberGenerator : IApplicationNumberGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int RandomLength = 6;
    private const int MaxAttempts = 10;

    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public ApplicationNumberGenerator(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string> GenerateUniqueAsync(CancellationToken cancellationToken = default)
    {
        var prefix = $"APP-{_clock.UtcNow:yyMM}-";

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = prefix + RandomTail();

            var taken = await _db.Applications
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(a => a.ApplicationNumber == candidate, cancellationToken);

            if (!taken)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not generate a unique application number after {MaxAttempts} attempts.");
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
