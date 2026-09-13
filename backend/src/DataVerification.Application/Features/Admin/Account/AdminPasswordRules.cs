using FluentValidation;

namespace DataVerification.Application.Features.Admin.Account;

/// <summary>
/// The one place the administrator password policy is expressed, so the change-password and
/// reset-password flows can never drift apart on what counts as an acceptable password.
/// </summary>
public static class AdminPasswordRules
{
    public static IRuleBuilderOptions<T, string> ApplyAdminPasswordRules<T>(
        this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty()
            .MinimumLength(8).WithMessage("Use at least 8 characters.")
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Include at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Include at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Include at least one number.");
}
