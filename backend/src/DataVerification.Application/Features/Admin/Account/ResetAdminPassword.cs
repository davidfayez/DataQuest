using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Account;

/// <summary>Completes a reset: the token from the email plus the chosen new password.</summary>
public sealed record ResetAdminPasswordCommand(string Token, string NewPassword) : IRequest;

public sealed class ResetAdminPasswordCommandValidator : AbstractValidator<ResetAdminPasswordCommand>
{
    public ResetAdminPasswordCommandValidator()
    {
        RuleFor(c => c.Token).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NewPassword).ApplyAdminPasswordRules();
    }
}

public sealed class ResetAdminPasswordCommandHandler : IRequestHandler<ResetAdminPasswordCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly IDateTimeProvider _clock;

    public ResetAdminPasswordCommandHandler(
        IApplicationDbContext db,
        IPasswordHashingService passwordHasher,
        IDateTimeProvider clock)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    public async Task Handle(ResetAdminPasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The email carried the raw token; the database holds only its hash. Matching happens on
        // the hash, so the stored value is useless to anyone who reads the table.
        var tokenHash = RequestAdminPasswordResetCommandHandler.Hash(request.Token);

        var admin = await _db.AdminUsers
            .FirstOrDefaultAsync(u => u.PasswordResetTokenHash == tokenHash, cancellationToken);

        // One message for a token that is unknown, already used, or expired: a caller learns only
        // that the link no longer works, never which of those it was.
        if (admin is null || !admin.HasValidPasswordResetToken(_clock.UtcNow))
        {
            throw new ConflictException(
                "admin.reset_token_invalid",
                "This reset link is invalid or has expired. Request a new one.");
        }

        admin.SetPassword(_passwordHasher.Hash(request.NewPassword), _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
