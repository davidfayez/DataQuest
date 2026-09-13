using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DataVerification.Infrastructure.Identity;

/// <summary>Bound from the <c>Jwt</c> configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "DataVerification";

    public string Audience { get; set; } = "DataVerification.Clients";

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;

    public int RefreshTokenDays { get; set; } = 7;
}

/// <summary>Claim types shared by the token issuer and the API's authorization handlers.</summary>
public static class DataVerificationClaims
{
    /// <summary>Order id an applicant's token is scoped to.</summary>
    public const string OrderId = "order_id";

    public const string OrderNumber = "order_number";

    /// <summary>One claim per granted permission on an admin token.</summary>
    public const string Permission = "permission";

    public const string LanguageCode = "lang";

    /// <summary>The realm (<c>Admin</c>/<c>Applicant</c>) a refresh token belongs to.</summary>
    public const string Realm = "realm";

    /// <summary>Marks a token as a refresh token so it is never mistaken for an access token.</summary>
    public const string TokenUse = "token_use";
}

/// <summary>Roles used to separate the two identity realms.</summary>
public static class DataVerificationRoles
{
    public const string Applicant = "Applicant";
    public const string Admin = "Admin";
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _clock;

    public JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _clock = clock;

        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured with at least 32 characters. " +
                "Set it via user secrets or an environment variable.");
        }
    }

    public AccessToken CreateApplicantToken(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        // The order id is the only scope an applicant token carries; every applicant query filters
        // on it, so a token can never reach another order's data.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, order.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, order.Email),
            new(ClaimTypes.Role, DataVerificationRoles.Applicant),
            new(DataVerificationClaims.OrderId, order.Id.ToString()),
            new(DataVerificationClaims.OrderNumber, order.OrderNumber),
            new(DataVerificationClaims.LanguageCode, order.LanguageCode),
        };

        return CreateToken(claims);
    }

    public AccessToken CreateAdminToken(AdminUser adminUser, IReadOnlySet<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(adminUser);
        ArgumentNullException.ThrowIfNull(permissions);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, adminUser.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, adminUser.Email),
            new(ClaimTypes.Name, adminUser.FullName),
            new(ClaimTypes.Role, DataVerificationRoles.Admin),
            new(DataVerificationClaims.LanguageCode, adminUser.LanguageCode),
        };

        // Permissions ride on the token so authorization needs no database round trip. Revoking a
        // permission therefore takes effect at the next login or token refresh.
        claims.AddRange(permissions.Select(p => new Claim(DataVerificationClaims.Permission, p)));

        return CreateToken(claims);
    }

    private const string RefreshTokenUse = "refresh";

    /// <summary>
    /// A separate audience for refresh tokens. The bearer middleware validates the access-token
    /// audience, so a refresh token presented in an Authorization header is rejected outright.
    /// </summary>
    private string RefreshAudience => $"{_options.Audience}.Refresh";

    public IssuedRefreshToken CreateRefreshToken(Guid subjectId, string realm)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = _clock.UtcNow.AddDays(_options.RefreshTokenDays);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subjectId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(DataVerificationClaims.Realm, realm),
            new Claim(DataVerificationClaims.TokenUse, RefreshTokenUse),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: RefreshAudience,
            claims: claims,
            notBefore: _clock.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return new IssuedRefreshToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public RefreshTokenInfo? ValidateRefreshToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _options.Issuer,
            ValidAudience = RefreshAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            ClockSkew = TimeSpan.Zero,
        };

        try
        {
            // Keep the JWT claim names as-is; the default mapping renames "sub" to nameidentifier.
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, parameters, out _);

            if (principal.FindFirstValue(DataVerificationClaims.TokenUse) != RefreshTokenUse)
            {
                return null;
            }

            var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var realm = principal.FindFirstValue(DataVerificationClaims.Realm);

            if (!Guid.TryParse(subject, out var subjectId) || string.IsNullOrEmpty(realm))
            {
                return null;
            }

            return new RefreshTokenInfo(subjectId, realm);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    private AccessToken CreateToken(IEnumerable<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = _clock.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: _clock.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
