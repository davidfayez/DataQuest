using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// The applicant's name in one script. Every application carries exactly two rows — Arabic and
/// English — because verification authorities need both forms.
/// </summary>
public class ApplicationName : Entity
{
    public Guid ApplicationId { get; set; }

    public VerificationApplication? Application { get; set; }

    public NameLanguageType LanguageType { get; set; }

    public required string FirstName { get; set; }

    public string? MiddleName { get; set; }

    public required string LastName { get; set; }

    public string FullName => string.Join(' ', new[] { FirstName, MiddleName, LastName }
        .Where(part => !string.IsNullOrWhiteSpace(part)));
}
