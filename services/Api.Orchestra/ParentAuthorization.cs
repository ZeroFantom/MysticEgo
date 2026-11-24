using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace MysticEgo.Api.Orchestra;

public sealed class ParentAuthorization(IOptionsMonitor<JwtBearerOptions> options)
{
    public const string Header = "X-Mystic-Parent";
    public ClaimsPrincipal? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) return null;
        try
        {
            var parsed = new JwtSecurityTokenHandler().ValidateToken(token,
                options.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters, out _);
            return parsed.HasClaim("token_use", "device") &&
                (parsed.IsInRole("global") || parsed.IsInRole("local")) ? parsed : null;
        }
        catch (Exception error) when (error is SecurityTokenException or ArgumentException) { return null; }
    }
}
