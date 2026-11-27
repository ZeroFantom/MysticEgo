using System.Security.Claims;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Security;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(
        User user, IEnumerable<string> roleCodes, IEnumerable<string>? permissions = null, Guid? sessionId = null);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token, bool validateLifetime = true);
}