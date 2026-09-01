using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ExpertToJob.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ExpertToJob.Web.Auth;

/// <summary>
/// Mints HS256 session tokens. The "sub" claim carries the user id so both the Web API and the
/// Agents service can attribute requests to a user (token-usage caps depend on this).
/// </summary>
public sealed class JwtTokenIssuer(IOptions<AuthOptions> options, TimeProvider clock) : IJwtTokenIssuer
{
    private readonly JwtOptions _jwt = options.Value.Jwt;

    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(_jwt.AccessTokenMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
