using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.Autorization.Configuration;
using Models.Autorization.Contracts.Requests;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Internal;
using Models.Autorization.Security;

namespace Models.Autorization.Services;

public sealed class UserService : IUserService
{
    private readonly AuthDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAccessResolver _access;
    private readonly AuthorizationOptions _opt;
    private readonly ILogger<UserService> _log;

    public UserService(
        AuthDbContext db,
        IPasswordHasher hasher,
        IAccessResolver access,
        AuthorizationOptions opt,
        ILogger<UserService> log)
    {
        _db = db;
        _hasher = hasher;
        _access = access;
        _opt = opt;
        _log = log;
    }

    public async Task<List<UserDto>> ListAsync(Guid callerId, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var q = _db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!caller.HasBypass)
            q = q.Where(u => u.Id == callerId || u.CreatedByUserId == callerId);

        var list = await q.OrderBy(u => u.CreatedAt).ToListAsync(ct);
        return list.Select(AuthService.ToUserDto).ToList();
    }

    public async Task<UserDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking()
            .Include(x => x.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return u is null ? null : AuthService.ToUserDto(u);
    }

    public async Task<UserDto> CreateAsync(
        Guid callerId, CreateUserRequest req, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var login = (req.Login ?? "").Trim();
        if (!CredentialPolicy.ValidLogin(login))
            throw new ValidationException("login_format");

        ValidatePassword(req.Password,login);

        if (await _db.Users.AnyAsync(u => u.Login == login, ct))
            throw new ValidationException($"Пользователь '{login}' уже существует.");

        List<Role> roles = new();
        if (req.RoleIds is { Count: > 0 })
        {
            roles = await _db.Roles
                .Where(r => req.RoleIds.Contains(r.Id))
                .ToListAsync(ct);

            if (roles.Count != req.RoleIds.Count)
                throw new ValidationException("Некоторые роли не найдены.");

            foreach (var r in roles)
                EnsureCanManageRole(caller, r);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            PasswordHash = _hasher.Hash(req.Password),
            Nickname = req.Nickname?.Trim(),
            AvatarUrl = req.AvatarUrl?.Trim(),
            StatusText = req.StatusText?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = callerId,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        for (var i = 0; i < roles.Count; i++)
        {
            _db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = roles[i].Id,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = callerId,
                IsPrimary = i == 0,
            });
        }

        await _db.SaveChangesAsync(ct);
        _access.InvalidateUser(user.Id);

        _log.LogInformation(
            "User created: {Login} by {CallerId} with {RoleCount} role(s).",
            user.Login, callerId, roles.Count);

        return (await GetAsync(user.Id, ct))!;
    }

    public async Task<UserDto?> UpdateAsync(
        Guid callerId, Guid id, UpdateUserRequest req, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return null;

        EnsureCanManageUser(caller, user);

        if (req.Nickname is not null) user.Nickname = req.Nickname.Trim();
        if (req.AvatarUrl is not null) user.AvatarUrl = req.AvatarUrl.Trim();
        if (req.StatusText is not null) user.StatusText = req.StatusText.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> SetActiveAsync(
        Guid callerId, Guid id, bool active, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return false;

        EnsureCanManageUser(caller, user);

        user.IsActive = active;
        user.UpdatedAt = DateTime.UtcNow;

        if (!active && _opt.Session.RevokeTokensOnDeactivate)
        {
            var tokens = await _db.RefreshTokens
                .Where(t => t.UserId == id).ToListAsync(ct);
            _db.RefreshTokens.RemoveRange(tokens);
        }

        await _db.SaveChangesAsync(ct);
        _access.InvalidateUser(id);
        return true;
    }

    public async Task<bool> ChangePasswordAsync(
        Guid callerId, Guid id, string newPassword, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return false;

        EnsureCanManageUser(caller, user);
        ValidatePassword(newPassword,user.Login);

        user.PasswordHash = _hasher.Hash(newPassword);
        user.UpdatedAt = DateTime.UtcNow;

        if (_opt.Session.RevokeTokensOnPasswordChange)
        {
            var tokens = await _db.RefreshTokens
                .Where(t => t.UserId == id).ToListAsync(ct);
            _db.RefreshTokens.RemoveRange(tokens);
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ChangePasswordSelfAsync(
        Guid id, string currentPwd, string newPassword, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return false;

        if (!_hasher.Verify(currentPwd, user.PasswordHash))
            throw new AuthAuthException("Неверный текущий пароль.");

        ValidatePassword(newPassword,user.Login);

        user.PasswordHash = _hasher.Hash(newPassword);
        user.UpdatedAt = DateTime.UtcNow;

        if (_opt.Session.RevokeTokensOnPasswordChange)
        {
            var tokens = await _db.RefreshTokens
                .Where(t => t.UserId == id).ToListAsync(ct);
            _db.RefreshTokens.RemoveRange(tokens);
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static void EnsureCanManageUser(UserAccessSnapshot caller, User target)
    {
        if (caller.HasBypass) return;
        if (caller.UserId == target.Id) return;
        if (target.CreatedByUserId == caller.UserId) return;

        throw new ForbiddenException(
            "Недостаточно прав для управления этим пользователем.");
    }

    private static void EnsureCanManageRole(UserAccessSnapshot caller, Role role)
    {
        if (caller.HasBypass) return;

        if (role.IsSystem)
            throw new ForbiddenException("Системную роль нельзя назначать.");

        if (role.Priority >= caller.MaxPriority)
            throw new ForbiddenException(
                "Нельзя назначать роль с приоритетом ≥ своего.");
    }

    /// <summary>Apply the same policy as public signup and bootstrap provisioning.</summary>
    private void ValidatePassword(string password,string login)
    {
        var error=CredentialPolicy.PasswordError(password,login,_opt.PasswordPolicy);
        if(error is not null)throw new ValidationException(error);
    }
}
