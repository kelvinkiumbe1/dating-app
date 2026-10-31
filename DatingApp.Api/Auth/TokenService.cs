using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DatingApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace DatingApp.Api.Auth;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "dating-app";
    public string Audience { get; set; } = "dating-app";

    /// Short, because an access token cannot be recalled once issued except by the
    /// per-request TokenVersion check. Refresh tokens carry the long-lived session.
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    /// A refresh token rotated this recently is answered with a plain 401 rather than treated
    /// as theft: two browser tabs refreshing at the same moment is not an attack.
    public int RefreshReuseGraceSeconds { get; set; } = 30;
}

public class TokenService(JwtOptions options)
{
    public TimeSpan RotationGrace { get; } = TimeSpan.FromSeconds(options.RefreshReuseGraceSeconds);

    public SymmetricSecurityKey SigningKey { get; } =
        new(Encoding.UTF8.GetBytes(options.Key));

    public (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(CustomClaims.TokenVersion, user.TokenVersion.ToString()),
            },
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// Creates a refresh token, stages it on the context, and returns the raw value — the
    /// only time it exists outside the client. Pass a familyId when rotating.
    public string IssueRefreshToken(AppDbContext db, User user, Guid? familyId = null)
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(raw),
            FamilyId = familyId ?? Guid.NewGuid(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(options.RefreshTokenDays),
        });
        return raw;
    }

    /// Issues a fresh access + refresh pair and saves.
    public async Task<AuthResponse> IssueSessionAsync(AppDbContext db, User user, Guid? familyId = null)
    {
        var refresh = IssueRefreshToken(db, user, familyId);
        await db.SaveChangesAsync();
        var (access, expiresAt) = IssueAccessToken(user);
        return new AuthResponse(access, user.Id, user.DisplayName, refresh, expiresAt);
    }

    public static async Task RevokeAllAsync(AppDbContext db, Guid userId)
    {
        // Filtered in memory: SQLite cannot compare DateTimeOffset columns in SQL, and one
        // user's token list is small.
        var now = DateTimeOffset.UtcNow;
        foreach (var t in await db.RefreshTokens.Where(t => t.UserId == userId).ToListAsync())
            t.RevokedAt ??= now;
    }

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}

/// JwtRegisteredClaimNames spelled out so the claim strings stay obvious at the call site.
internal static class JwtRegisteredClaimNames
{
    public const string Sub = "sub";
    public const string Email = "email";
    public const string Jti = "jti";
}

public static class CustomClaims
{
    public const string TokenVersion = "ver";

    /// Added at validation time from the database, never baked into the token, so revoking
    /// admin takes effect on the next request.
    public const string Role = "role";
    public const string AdminRole = "admin";
}
