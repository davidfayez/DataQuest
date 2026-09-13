namespace DataVerification.Domain.Common;

/// <summary>
/// Tidies the national part of a phone number — the part stored beside a country's calling code.
/// </summary>
/// <remarks>
/// The leading zero is a trunk prefix: it belongs to dialling a number from inside its own country
/// and is dropped as soon as a calling code is put in front. Someone in Egypt writes their mobile
/// as <c>01208691253</c>, but reached from abroad it is <c>+20 1208691253</c> — so storing the zero
/// beside <c>+20</c> produces a number that cannot be dialled.
///
/// Applied where the number is stored rather than in the browser, so a number arriving from any
/// client is held the same way.
/// </remarks>
public static class NationalPhoneNumber
{
    /// <summary>
    /// Trims the number and removes leading zeros. Returns null for anything blank, and for a
    /// number that is nothing but zeros — which is not a phone number.
    /// </summary>
    public static string? Normalise(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var trimmed = number.Trim();
        var digits = trimmed.TrimStart('0');

        // All zeros: keep nothing rather than invent a number by trimming it away to empty.
        return digits.Length == 0 ? null : digits;
    }
}
