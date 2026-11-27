using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Models.Autorization.Configuration;
using Models.Autorization.Data;
using Models.Autorization.Discovery;
using Models.Autorization.Filters;
using Models.Autorization.Security;
using Models.Autorization.Services;

namespace Models.Autorization.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAuthorizationFramework(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Authorization")
    {
        var opt = new AuthorizationOptions();
        configuration.GetSection(sectionName).Bind(opt);
        return services.AddAuthorizationFramework(opt);
    }

    public static IServiceCollection AddAuthorizationFramework(
        this IServiceCollection services,
        AuthorizationOptions opt)
    {
        if (string.IsNullOrWhiteSpace(opt.Jwt.PrivateKeyPath) && opt.Jwt.SecretKey.Length < 32)
            throw new InvalidOperationException("Настройте Authorization:Jwt:PrivateKeyPath или надёжный Jwt:SecretKey");

        services.AddSingleton(opt);
        services.AddSingleton(opt.Jwt);

        services.AddDbContext<AuthDbContext>(o =>
        {
            if (opt.ConnectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)) o.UseNpgsql(opt.ConnectionString);
            else o.UseSqlite(opt.ConnectionString);
        });
        services.AddMemoryCache();

        services.AddSingleton<IPasswordHasher, VersionedPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // ── AccessResolver ─────────────────────────────
        // ВАЖНО: если AccessResolver использует AuthDbContext через ctor,
        // он должен быть Scoped — иначе captive dependency.
        // Если внутри IServiceScopeFactory — можно Singleton.
        services.AddScoped<IAccessResolver, AccessResolver>();

        // ── Discovery ──────────────────────────────────
        // Сканер сам создаёт scope через IServiceScopeFactory — Singleton ок.
        services.AddSingleton<IToolDiscoveryService, ToolDiscoveryService>();

        services.AddSingleton<DesktopPairing>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IToolService, ToolService>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opt.Jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = opt.Jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [string.IsNullOrWhiteSpace(opt.Jwt.PublicKeyPath) ? SecurityAlgorithms.HmacSha256 : SecurityAlgorithms.RsaSha256],
                    IssuerSigningKey = JwtSigningKeys.VerificationKey(opt.Jwt),
                    ClockSkew = TimeSpan.FromSeconds(opt.Jwt.ClockSkewSeconds),
                };
            });

        services.AddAuthorization();
        services.AddScoped<ToolAuthorizationFilter>();

        return services;
    }

    /// <summary>
    /// Подключает глобальный фильтр авторизации.
    /// Использование: AddControllers(o => o.AddToolAuthorizationFilter()).
    /// </summary>
    public static MvcOptions AddToolAuthorizationFilter(this MvcOptions options)
    {
        options.Filters.AddService<ToolAuthorizationFilter>();
        return options;
    }

    /// <summary>
    /// Инициализация БД + seed + sync discovery. Вызывать после builder.Build().
    /// </summary>
    public static async Task UseAuthorizationAsync(
        this WebApplication app,
        CancellationToken ct = default)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var db     = sp.GetRequiredService<AuthDbContext>();
        var opt    = sp.GetRequiredService<AuthorizationOptions>();
        var logger = sp.GetRequiredService<ILogger<AuthDbContext>>();

        if (!db.Database.IsNpgsql()) EnsureSqliteDirectory(opt.ConnectionString);

        // ⬇⬇⬇  БЫЛО: await db.Database.MigrateAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);
        // Совместимость с существующими схемами SQLite/PostgreSQL. Для эксплуатации нужны миграции EF.
        var tableDdl=db.Database.IsNpgsql()
            ? "CREATE TABLE IF NOT EXISTS managed_identities (\"Id\" uuid PRIMARY KEY, \"Name\" character varying(160) NOT NULL, \"Kind\" character varying(16) NOT NULL, \"SecretHash\" character varying(200) NOT NULL, \"Enabled\" boolean NOT NULL, \"CreatedUtc\" timestamp with time zone NOT NULL, \"LastAuthenticatedUtc\" timestamp with time zone NULL)"
            : "CREATE TABLE IF NOT EXISTS managed_identities (\"Id\" TEXT NOT NULL PRIMARY KEY, \"Name\" TEXT NOT NULL, \"Kind\" TEXT NOT NULL, \"SecretHash\" TEXT NOT NULL, \"Enabled\" INTEGER NOT NULL, \"CreatedUtc\" TEXT NOT NULL, \"LastAuthenticatedUtc\" TEXT NULL)";
        await db.Database.ExecuteSqlRawAsync(tableDdl,ct);

        // Existing databases predate the registration acceptance table.
        var acceptanceDdl = db.Database.IsNpgsql()
            ? """
              CREATE TABLE IF NOT EXISTS audit_registration_acceptances (
                  "UserId" uuid PRIMARY KEY REFERENCES users("Id") ON DELETE CASCADE,
                  "TermsVersion" varchar(40) NOT NULL,
                  "PrivacyVersion" varchar(40) NOT NULL,
                  "AcceptedUtc" timestamp with time zone NOT NULL
              )
              """
            : """
              CREATE TABLE IF NOT EXISTS audit_registration_acceptances (
                  "UserId" TEXT NOT NULL PRIMARY KEY REFERENCES users("Id") ON DELETE CASCADE,
                  "TermsVersion" TEXT NOT NULL,
                  "PrivacyVersion" TEXT NOT NULL,
                  "AcceptedUtc" TEXT NOT NULL
              )
              """;
        await db.Database.ExecuteSqlRawAsync(acceptanceDdl, ct);

        await AuthSeeder.SeedAsync(db, opt, logger, ct);

        var discovery = sp.GetRequiredService<IToolDiscoveryService>();
        await discovery.SyncAsync(ct);
    }
    
    // ⬇⬇⬇  И ЭТОТ ХЕЛПЕР
    private static void EnsureSqliteDirectory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;

        if (string.IsNullOrWhiteSpace(dataSource)) return;
        if (dataSource == ":memory:") return;
        if (dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return; // URI-режим, разбирать вручную не надо

        var fullPath = Path.GetFullPath(dataSource);
        var dir = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}