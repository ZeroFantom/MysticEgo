using Microsoft.EntityFrameworkCore;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Data
{
    public sealed class AuthDbContext : DbContext
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

        public DbSet<AuditRegistrationAcceptance> AuditRegistrationAcceptances => Set<AuditRegistrationAcceptance>();
        public DbSet<AccountLicenseAcceptance> AccountLicenseAcceptances => Set<AccountLicenseAcceptance>();
        public DbSet<AccountFeatureConsent> AccountFeatureConsents => Set<AccountFeatureConsent>();
        public DbSet<AccountDeviceGrant> AccountDeviceGrants => Set<AccountDeviceGrant>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<Tool> Tools => Set<Tool>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<ManagedIdentity> ManagedIdentities => Set<ManagedIdentity>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<ManagedIdentity>(e => { e.ToTable("managed_identities"); e.HasKey(x=>x.Id); e.Property(x=>x.Name).HasMaxLength(160).IsRequired(); e.Property(x=>x.Kind).HasMaxLength(16).IsRequired(); e.Property(x=>x.SecretHash).HasMaxLength(200).IsRequired(); e.HasIndex(x=>x.Kind); });

            mb.Entity<User>(e =>
            {
                e.ToTable("users");
                e.HasKey(x => x.Id);
                e.Property(x => x.Login).HasMaxLength(100).IsRequired();
                e.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
                e.Property(x => x.Nickname).HasMaxLength(100);
                e.Property(x => x.AvatarUrl).HasMaxLength(500);
                e.Property(x => x.StatusText).HasMaxLength(200);

                e.HasIndex(x => x.Login).IsUnique();
                e.HasIndex(x => x.IsActive);
                e.HasIndex(x => x.CreatedByUserId);
            });

            mb.Entity<AuditRegistrationAcceptance>(e =>
            {
                e.ToTable("audit_registration_acceptances");
                e.HasKey(x => x.UserId);
                e.Property(x => x.TermsVersion).HasMaxLength(40).IsRequired();
                e.Property(x => x.PrivacyVersion).HasMaxLength(40).IsRequired();
                e.HasOne<User>().WithOne()
                    .HasForeignKey<AuditRegistrationAcceptance>(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<AccountLicenseAcceptance>(e =>
            {
                e.ToTable("account_license_acceptances"); e.HasKey(x=>new{x.UserId,x.Version});
                e.Property(x=>x.Version).HasMaxLength(80).IsRequired();
                e.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);
            });
            mb.Entity<AccountFeatureConsent>(e =>
            {
                e.ToTable("account_feature_consents"); e.HasKey(x=>new{x.UserId,x.Scope});
                e.Property(x=>x.Scope).HasMaxLength(40).IsRequired();
                e.Property(x=>x.PolicyVersion).HasMaxLength(40).IsRequired();
                e.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);
            });
            mb.Entity<AccountDeviceGrant>(e =>
            {
                e.ToTable("account_device_grants"); e.HasKey(x=>new{x.UserId,x.DeviceId,x.Scope});
                e.Property(x=>x.Scope).HasMaxLength(40).IsRequired();
                e.Property(x=>x.PolicyVersion).HasMaxLength(40).IsRequired();
                e.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);
            });
            mb.Entity<Role>(e =>
            {
                e.ToTable("roles");
                e.HasKey(x => x.Id);
                e.Property(x => x.Code).HasMaxLength(100).IsRequired();
                e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
                e.Property(x => x.Description).HasMaxLength(1000);

                e.HasIndex(x => x.Code).IsUnique();
                e.HasIndex(x => x.Priority);
                e.HasIndex(x => x.IsHidden);
            });

            mb.Entity<Permission>(e =>
            {
                e.ToTable("permissions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Code).HasMaxLength(200).IsRequired();
                e.Property(x => x.DisplayName).HasMaxLength(300).IsRequired();
                e.Property(x => x.Description).HasMaxLength(2000);
                e.Property(x => x.Category).HasMaxLength(100);
                e.Property(x => x.ControllerName).HasMaxLength(200);

                e.HasIndex(x => x.Code).IsUnique();
                e.HasIndex(x => x.Category);
                e.HasIndex(x => x.IsSystem);
            });

            mb.Entity<Tool>(e =>
            {
                e.ToTable("tools");
                e.HasKey(x => x.Id);
                e.Property(x => x.Code).HasMaxLength(300).IsRequired();
                e.Property(x => x.DisplayName).HasMaxLength(300);
                e.Property(x => x.Description).HasMaxLength(2000);
                e.Property(x => x.HttpMethod).HasMaxLength(10);
                e.Property(x => x.PathTemplate).HasMaxLength(500);

                e.HasOne(x => x.Permission)
                    .WithMany(p => p.Tools)
                    .HasForeignKey(x => x.PermissionId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => x.Code).IsUnique();
                e.HasIndex(x => x.PermissionId);
                e.HasIndex(x => x.IsEnabled);
            });

            mb.Entity<RolePermission>(e =>
            {
                e.ToTable("role_permissions");
                e.HasKey(x => new { x.RoleId, x.PermissionId });

                e.HasOne(x => x.Role).WithMany(r => r.RolePermissions)
                    .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Permission).WithMany(p => p.RolePermissions)
                    .HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<UserRole>(e =>
            {
                e.ToTable("user_roles");
                e.HasKey(x => new { x.UserId, x.RoleId });

                e.HasOne(x => x.User).WithMany(u => u.UserRoles)
                    .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Role).WithMany(r => r.UserRoles)
                    .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => x.UserId);
                e.HasIndex(x => x.RoleId);
            });

            mb.Entity<RefreshToken>(e =>
            {
                e.ToTable("refresh_tokens");
                e.HasKey(x => x.Id);
                e.Property(x => x.Token).HasMaxLength(500).IsRequired().IsConcurrencyToken();
                e.HasIndex(x => x.SessionId).IsUnique();
                e.Property(x => x.CreatedByIp).HasMaxLength(64);
                e.Property(x => x.UserAgent).HasMaxLength(500);

                e.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
                    .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => x.Token).IsUnique();
                e.HasIndex(x => x.UserId);
                e.HasIndex(x => x.ExpiresAt);
            });
        }
    }
}