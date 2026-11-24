using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.Autorization.Configuration;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;

namespace Models.Autorization.Data
{
    public static class AuthSeeder
    {
        public static async Task SeedAsync(
            AuthDbContext db,
            AuthorizationOptions opt,
            ILogger logger,
            CancellationToken ct = default)
        {
            var bypassPerm = await db.Permissions
                .FirstOrDefaultAsync(p => p.Code == opt.Bootstrap.SystemBypassCode, ct);

            if (bypassPerm is null)
            {
                bypassPerm = new Permission
                {
                    Code = opt.Bootstrap.SystemBypassCode,
                    DisplayName = "Полный доступ (bypass)",
                    Description = "Абсолютный доступ. Только для роли разработчика.",
                    Category = "System",
                    IsSystem = true,
                    ControllerName = "System",
                    DiscoveredAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                db.Permissions.Add(bypassPerm);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Seeded permission {Code}", bypassPerm.Code);
            }

            var devRole = await db.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Code == opt.Bootstrap.DeveloperRoleCode, ct);

            if (devRole is null)
            {
                devRole = new Role
                {
                    Code = opt.Bootstrap.DeveloperRoleCode,
                    DisplayName = "Разработчик",
                    Description = "Абсолютная роль. Полный доступ ко всему.",
                    IsSystem = true,
                    IsHidden = true,
                    Priority = int.MaxValue,
                    CreatedAt = DateTime.UtcNow,
                };
                db.Roles.Add(devRole);
                await db.SaveChangesAsync(ct);

                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = devRole.Id,
                    PermissionId = bypassPerm.Id,
                    AssignedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);

                logger.LogInformation("Seeded role {Code}", devRole.Code);
            }

            if (!await db.Users.AnyAsync(ct))
            {
                if (string.IsNullOrWhiteSpace(opt.Bootstrap.Password))
                {
                    logger.LogWarning(
                        "Bootstrap: пароль не задан, пользователь-разработчик не создан.");
                    return;
                }

                var hasher = new BCryptPasswordHasher();

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    Login = opt.Bootstrap.Login,
                    PasswordHash = hasher.Hash(opt.Bootstrap.Password),
                    Nickname = opt.Bootstrap.Nickname,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);

                db.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = devRole.Id,
                    IsPrimary = true,
                    AssignedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Bootstrap developer user created: {Login}", user.Login);
            }
        }
    }
}