using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Models.Autorization.Configuration;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;

namespace Models.Autorization.Services;

public sealed class AuthService : IAuthService
{
    private readonly AuthDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly AuthorizationOptions _opt;
    private readonly ILogger<AuthService> _log;

    public AuthService(
        AuthDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        AuthorizationOptions opt,
        ILogger<AuthService> log)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _opt = opt;
        _log = log;
    }

    public async Task<AuthResponse?> LoginAsync(
        string login, string password, string? ip, string? userAgent, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Login == login, ct);

        if (user is null || !user.IsActive) return null;
        if (!_hasher.Verify(password, user.PasswordHash)) return null;

        user.LastLoginAt = DateTime.UtcNow;
        var response = await BuildAuthResponseAsync(user, ip, userAgent, ct);

        _log.LogInformation("Login OK: {Login} ({UserId})", user.Login, user.Id);
        return response;
    }

    public async Task<AuthResponse?> RefreshAsync(
        string refreshToken, string? ip, string? userAgent, CancellationToken ct)
    {
        var hash = HashRefreshToken(refreshToken);
        var stored = await _db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u!.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(t => t.Token == hash, ct);

        if (stored is null
            || stored.ExpiresAt <= DateTime.UtcNow
            || stored.User is null
            || !stored.User.IsActive)
            return null;

        AuthResponse response;
        try { response = await BuildAuthResponseAsync(stored.User, ip, userAgent, ct, stored); }
        catch (DbUpdateConcurrencyException) { return null; }

        _log.LogInformation("Refresh OK: {Login}", stored.User.Login);
        return response;
    }

    public async Task<bool> LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = HashRefreshToken(refreshToken);
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == hash, ct);
        if (stored is null) return false;

        _db.RefreshTokens.Remove(stored);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId).ToListAsync(ct);
        if (tokens.Count == 0) return 0;

        _db.RefreshTokens.RemoveRange(tokens);
        await _db.SaveChangesAsync(ct);
        return tokens.Count;
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(
        User user, string? ip, string? userAgent, CancellationToken ct, RefreshToken? existing = null)
    {
        var roleCodes = user.UserRoles.Select(ur => ur.Role.Code).ToList();

        var roleIds = user.UserRoles.Select(x=>x.RoleId).ToArray();
        var permissions = await _db.RolePermissions.AsNoTracking().Where(rp=>roleIds.Contains(rp.RoleId))
            .Select(rp=>rp.Permission.Code).Distinct().ToListAsync(ct);
        var refresh = _jwt.GenerateRefreshToken();

        var now = DateTime.UtcNow;
        var session = existing ?? new RefreshToken { UserId = user.Id, CreatedAt = now };
        session.SessionId ??= Guid.NewGuid();
        session.Token = HashRefreshToken(refresh);
        session.ExpiresAt = now.AddDays(_opt.Jwt.RefreshTokenDays);
        session.LastSeenUtc = now;
        session.CreatedByIp = ip is { Length: >64 } ? ip[..64] : ip;
        session.UserAgent = userAgent is { Length: >500 } ? userAgent[..500] : userAgent;
        if (existing is null) _db.RefreshTokens.Add(session);

        await _db.SaveChangesAsync(ct);

        var (access, expires) = _jwt.GenerateAccessToken(user, roleCodes, permissions, session.SessionId);
        return new AuthResponse
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresAt = expires,
            User = ToUserDto(user),
        };
    }

    private static string HashRefreshToken(string raw)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    internal static UserDto ToUserDto(User u) => new()
    {
        Id = u.Id,
        Login = u.Login,
        Nickname = u.Nickname,
        AvatarUrl = u.AvatarUrl,
        StatusText = u.StatusText,
        IsActive = u.IsActive,
        CreatedAt = u.CreatedAt,
        LastLoginAt = u.LastLoginAt,
        CreatedByUserId = u.CreatedByUserId,
        Roles = u.UserRoles.Select(ur => new UserRoleDto
        {
            RoleId = ur.RoleId,
            Code = ur.Role.Code,
            DisplayName = ur.Role.DisplayName,
            IsPrimary = ur.IsPrimary,
            AssignedAt = ur.AssignedAt,
        }).ToList(),
    };
}