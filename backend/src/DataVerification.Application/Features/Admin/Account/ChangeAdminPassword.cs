using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Account;

/// <summary>Lets a signed-in administrator change their own password.</summary>
public sealed record ChangeAdminPasswordCommand(string CurrentPassword, string NewPassword)
    : IRequest;

public sealed class ChangeAdminPasswordCommandValidator
    : AbstractValidator<ChangeAdminPasswordCommand>
{
    public ChangeAdminPasswordCommandValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty().MaximumLength(128);

        // The same strength rule the reset flow enforces, kept in one place below.
        RuleFor(c => c.NewPassword).ApplyAdminPasswordRules();

        RuleFor(c => c.NewPassword)
            .NotEqual(c => c.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

public sealed class ChangeAdminPasswordCommandHandler : IRequestHandler<ChangeAdminPasswordCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly IDateTimeProvider _clock;

    public ChangeAdminPasswordCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IPasswordHashingService passwordHasher,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    public async Task Handle(ChangeAdminPasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var adminId = _currentUser.AdminUserId
            ?? throw new ForbiddenAccessException("Only a signed-in administrator can change a password.");

        var admin = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == adminId, cancellationToken)
            ?? throw new NotFoundException("AdminUser", adminId);

        // A wrong current password is treated as a field validation error (400), not an
        // authentication failure (401). The caller's session is perfectly valid — returning 401
        // here would trip the client's "token rejected" handling and sign them out mid-form.
        if (!_passwordHasher.Verify(admin.PasswordHash, request.CurrentPassword))
        {
            var failure = new Common.Exceptions.ValidationException();
            failure.Errors["CurrentPassword"] = ["Your current password is not correct."];
            throw failure;
        }

        admin.SetPassword(_passwordHasher.Hash(request.NewPassword), _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
