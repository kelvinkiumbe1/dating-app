using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DatingApp.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace DatingApp.Api.Auth;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "dating-app";
    public string Audience { get; set; } = "dating-app";
    public int LifetimeHours { get; set; } = 24;
}

public class TokenService(JwtOptions options)
{
    public SymmetricSecurityKey SigningKey { get; } =
        new(Encoding.UTF8.GetBytes(options.Key));

    public string Issue(User user)
    {
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            expires: DateTime.UtcNow.AddHours(options.LifetimeHours),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// JwtRegisteredClaimNames spelled out so the claim strings stay obvious at the call site.
internal static class JwtRegisteredClaimNames
{
    public const string Sub = "sub";
    public const string Email = "email";
    public const string Jti = "jti";
}
