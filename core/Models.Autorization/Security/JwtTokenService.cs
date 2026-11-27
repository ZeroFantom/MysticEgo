using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Models.Autorization.Configuration;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Security;

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _opt;

    public JwtTokenService(JwtOptions opt) => _opt = opt;

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(
        User user, IEnumerable<string> roleCodes, IEnumerable<string>? permissions = null, Guid? sessionId = null)
    {

        var expires = DateTime.UtcNow.AddMinutes(_opt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Login),
            new("account_created", DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc).ToString("O")),
        };

        if (sessionId is Guid sid) claims.Add(new Claim("mystic_session", sid.ToString("D")));
        if (!string.IsNullOrWhiteSpace(user.Nickname))
            claims.Add(new Claim("nickname", user.Nickname));

        foreach (var code in roleCodes)
            if (!string.IsNullOrWhiteSpace(code))
                {
                claims.Add(new Claim("role_code", code));
                claims.Add(new Claim(ClaimTypes.Role, code));
            }

        if(permissions is not null)
            foreach(var permission in permissions.Distinct(StringComparer.Ordinal))
                if(!string.IsNullOrWhiteSpace(permission))claims.Add(new Claim("permission",permission));
        var creds = JwtSigningKeys.SigningKey(_opt);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public ClaimsPrincipal? ValidateToken(string token, bool validateLifetime = true)
    {
        try
        {
            var key = JwtSigningKeys.VerificationKey(_opt);

            return new JwtSecurityTokenHandler().ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = _opt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _opt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = validateLifetime,
                    ClockSkew = TimeSpan.FromSeconds(_opt.ClockSkewSeconds),
                }, out _);
        }
        catch { return null; }
    }
}