using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Orders.Commands;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataVerification.Application.Features.Admin.Auth;

/// <summary>
/// Exchanges admin credentials for a JWT carrying the caller's resolved permissions.
/// <paramref name="Identifier"/> is matched against either the username or the email address.
/// </summary>
public sealed record LoginAdminCommand(string Identifier, string Password) : IRequest<LoginAdminResult>;

public sealed record LoginAdminResult(
    string AccessToken,
    DateTime ExpiresAtUtc,
    Guid AdminUserId,
    string FullName,
    string Email,
    string Username,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<string> Roles,
    bool HasAvatar,
    DateTime LoginAtUtc);

public sealed class LoginAdminCommandValidator : AbstractValidator<LoginAdminCommand>
{
    public LoginAdminCommandValidator()
    {
        // Deliberately not an email rule: the identifier is a username or an email address, and
        // the handler decides which by looking for '@' — a shape usernames are forbidden to take.
        RuleFor(c => c.Identifier).NotEmpty().MaximumLength(320);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class LoginAdminCommandHandler : IRequestHandler<LoginAdminCommand, LoginAdminResult>
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly IJwtTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<LoginAdminCommandHandler> _logger;

    public LoginAdminCommandHandler(
        IApplicationDbContext db,
        IPasswordHashingService passwordHasher,
        IJwtTokenService tokenService,
        IDateTimeProvider clock,
        ILogger<LoginAdminCommandHandler> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<LoginAdminResult> Handle(
        LoginAdminCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identifier = request.Identifier.Trim();

        // Usernames are stored lower-cased and can never contain '@', so a single normalised
        // comparison covers both forms without the two ever colliding.
        var normalized = identifier.ToLowerInvariant();

        // Direct grants are loaded up front so they can be baked into the token.
        var admin = await _db.AdminUsers
            .Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(
                u => u.Email == identifier || u.Username == normalized,
                cancellationToken);

        if (admin is null || !admin.IsActive)
        {
            throw new InvalidCredentialsException();
        }

        if (admin.IsLockedOut(_clock.UtcNow))
        {
            _logger.LogWarning("Login attempt against locked-out admin {Identifier}.", identifier);
            throw new InvalidCredentialsException();
        }

        if (!_passwordHasher.Verify(admin.PasswordHash, request.Password))
        {
            admin.RegisterFailedLogin(_clock.UtcNow, MaxFailedAttempts, LockoutDuration);
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidCredentialsException();
        }

        var loginAtUtc = _clock.UtcNow;
        admin.RegisterSuccessfulLogin(loginAtUtc);
        await _db.SaveChangesAsync(cancellationToken);

        var permissions = admin.ResolvePermissions();
        var token = _tokenService.CreateAdminToken(admin, permissions);

        return new LoginAdminResult(
            token.Token,
            token.ExpiresAtUtc,
            admin.Id,
            admin.FullName,
            admin.Email,
            admin.Username,
            permissions.ToArray(),
            Array.Empty<string>(),
            admin.AvatarStoragePath is not null,
            loginAtUtc);
    }
}

/// <summary>
/// Rebuilds an admin session from a valid refresh token, minting a fresh access token with the
/// admin's <em>current</em> permissions — so a permission change takes effect on the next refresh.
/// No password is checked and the last-login time is preserved, because this is not a new sign-in.
/// </summary>
public sealed record RefreshAdminSessionCommand(Guid AdminUserId) : IRequest<LoginAdminResult>;

public sealed class RefreshAdminSessionCommandHandler
    : IRequestHandler<RefreshAdminSessionCommand, LoginAdminResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IJwtTokenService _tokenService;
    private readonly IDateTimeProvider _clock;

    public RefreshAdminSessionCommandHandler(
        IApplicationDbContext db,
        IJwtTokenService tokenService,
        IDateTimeProvider clock)
    {
        _db = db;
        _tokenService = tokenService;
        _clock = clock;
    }

    public async Task<LoginAdminResult> Handle(
        RefreshAdminSessionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var admin = await _db.AdminUsers
            .Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Id == request.AdminUserId, cancellationToken);

        // A deactivated (or deleted) account cannot refresh its way back in.
        if (admin is null || !admin.IsActive)
        {
            throw new InvalidCredentialsException();
        }

        var permissions = admin.ResolvePermissions();
        var token = _tokenService.CreateAdminToken(admin, permissions);

        return new LoginAdminResult(
            token.Token,
            token.ExpiresAtUtc,
            admin.Id,
            admin.FullName,
            admin.Email,
            admin.Username,
            permissions.ToArray(),
            Array.Empty<string>(),
            admin.AvatarStoragePath is not null,
            admin.LastLoginAtUtc ?? _clock.UtcNow);
    }
}
