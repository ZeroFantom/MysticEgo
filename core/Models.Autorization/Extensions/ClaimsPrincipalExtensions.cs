using System.Security.Claims;

namespace Models.Autorization.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static Guid? GetUserId(this ClaimsPrincipal user)
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }

        public static string? GetLogin(this ClaimsPrincipal user)
            => user.FindFirstValue(ClaimTypes.Name);

        public static IReadOnlyList<string> GetRoleCodes(this ClaimsPrincipal user)
            => user.FindAll("role_code").Select(c => c.Value).ToList();
    }
}