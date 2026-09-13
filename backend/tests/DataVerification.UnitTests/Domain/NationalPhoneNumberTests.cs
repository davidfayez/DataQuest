using DataVerification.Domain.Common;
using FluentAssertions;

namespace DataVerification.UnitTests.Domain;

/// <summary>
/// The national part of a phone number, as it is stored beside a country's calling code.
///
/// The leading zero is a trunk prefix — dialled from inside the country and dropped the moment a
/// calling code goes in front. Keeping it would store <c>+20 01208691253</c>, which cannot be
/// dialled from anywhere.
/// </summary>
public sealed class NationalPhoneNumberTests
{
    [Theory]
    [InlineData("01208691253", "1208691253")]
    [InlineData("07700900123", "7700900123")]
    [InlineData("  01208691253  ", "1208691253")]
    public void DropsTheTrunkZero(string input, string expected) =>
        NationalPhoneNumber.Normalise(input).Should().Be(expected);

    [Fact]
    public void LeavesANumberThatDoesNotStartWithZeroAlone()
    {
        NationalPhoneNumber.Normalise("1208691253").Should().Be("1208691253");
        NationalPhoneNumber.Normalise("9876543210").Should().Be("9876543210");
    }

    [Fact]
    public void DropsEveryLeadingZero()
    {
        // Nobody's national number begins with a zero once the trunk prefix is gone, so a run of
        // them is someone typing an international prefix into a national field. One rule for both
        // rather than a guess about which zero meant what.
        NationalPhoneNumber.Normalise("001208691253").Should().Be("1208691253");
    }

    [Fact]
    public void KeepsZerosThatAreNotAtTheFront()
    {
        NationalPhoneNumber.Normalise("01020304050").Should().Be("1020304050");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("0000")]
    public void GivesNothingBackForWhatIsNotANumber(string? input) =>
        // All zeros trims away to nothing; storing an empty string would be inventing a number.
        NationalPhoneNumber.Normalise(input).Should().BeNull();
}
