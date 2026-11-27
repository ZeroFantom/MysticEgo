using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;

namespace Api.Auth;

/// <summary>Публичная регистрация постоянного домашнего аккаунта AuditOrchestra.
/// Нельзя указать роль, приоритет или привилегии в запросе.</summary>
public static class AuditRegistration
{
    public sealed record Request(
        string Login, string Password, string Nickname,
        string? TermsVersion = null, string? PrivacyVersion = null, string? LicenseVersion = null,
        string? PermissionPolicyVersion = null, string[]? EnabledScopes = null);

    internal sealed record Document(string Version, string Url);
    internal sealed record RegistrationDocuments(Document Terms, Document Privacy, Document License);

    private static Document? ReadDocument(IConfiguration config, string name)
    {
        var version = config[$"AuditRegistration:{name}:Version"];
        var url = config[$"AuditRegistration:{name}:Url"];
        return version is { Length: > 0 and <= 40 } &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.UserInfo.Length == 0 ? new Document(version, uri.AbsoluteUri) : null;
    }

    internal static RegistrationDocuments? ReadDocuments(IConfiguration config) =>
        ReadDocument(config, "Terms") is { } terms && ReadDocument(config, "Privacy") is { } privacy
            ? new RegistrationDocuments(terms, privacy, new Document("polyform-noncommercial-1.0.0", "https://orchestra.isdarkline.ru/license"))
            : null;

    private static IResult DocumentsUnavailable() => Results.Json(
        new { error = "Документы регистрации ещё не опубликованы." }, statusCode: 503);
    public static void MapAuditRegistration(this WebApplication app)
    {
        app.MapGet("/api/auth/audit/registration-documents", (HttpContext context, IConfiguration config) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return ReadDocuments(config) is { } documents
                ? Results.Ok(new { required = true, documents.Terms, documents.Privacy, documents.License, permissionPolicyVersion = AccountConsentPolicy.Version, scopes = AccountConsentPolicy.Scopes })
                : DocumentsUnavailable();
        }).AllowAnonymous();

        app.MapPost("/api/auth/audit/register", async (
            Request input, AuthDbContext db, IPasswordHasher hasher, IConfiguration config, CancellationToken ct) =>
        {
            var documents = ReadDocuments(config);
            if (documents is null) return DocumentsUnavailable();
            if (input.TermsVersion != documents.Terms.Version || input.PrivacyVersion != documents.Privacy.Version ||
                input.LicenseVersion != documents.License.Version || input.PermissionPolicyVersion != AccountConsentPolicy.Version ||
                !AccountConsentPolicy.Valid(input.EnabledScopes))
                return Results.BadRequest(new { error = "Accept the current terms, privacy notice, license and review feature permissions." });

            var login=(input.Login??"").Trim();var nickname=(input.Nickname??"").Trim();
            if(!System.Text.RegularExpressions.Regex.IsMatch(login,"^[a-zA-Z0-9_.-]{4,40}$") ||
               (input.Password??"").Length is <12 or >128 || nickname.Length is <2 or >60)
                return Results.BadRequest(new {error="Логин 4–40 символов; пароль минимум 12; никнейм 2–60."});
            if(await db.Users.AnyAsync(x=>x.Login==login,ct))return Results.Conflict(new {error="Логин уже занят."});
            // Пользователь без административных ролей. Привилегии Auth/Global не выдаются автоматически.
            var user = new User{Id=Guid.NewGuid(),Login=login,Nickname=nickname,
                PasswordHash=hasher.Hash(input.Password!),IsActive=true,CreatedAt=DateTime.UtcNow};
            db.Users.Add(user);
            if (documents is not null)
            {
                // Commit the account and document acceptance together.
                db.AuditRegistrationAcceptances.Add(new AuditRegistrationAcceptance
                {
                    UserId = user.Id,
                    TermsVersion = documents.Terms.Version,
                    PrivacyVersion = documents.Privacy.Version,
                    AcceptedUtc = DateTime.UtcNow
                });
            }
            db.AccountLicenseAcceptances.Add(new AccountLicenseAcceptance {UserId=user.Id,Version=documents.License.Version,AcceptedUtc=DateTime.UtcNow});
            foreach(var scope in AccountConsentPolicy.Scopes)
                db.AccountFeatureConsents.Add(new AccountFeatureConsent {UserId=user.Id,Scope=scope,PolicyVersion=AccountConsentPolicy.Version,Enabled=input.EnabledScopes!.Contains(scope),UpdatedUtc=DateTime.UtcNow});
            try{await db.SaveChangesAsync(ct);}
            catch(DbUpdateException){return Results.Conflict(new {error="Не удалось создать учётную запись. Проверьте логин."});}
            return Results.Created("/api/auth/login",new {registered=true,accountType="audit",login,
                gameAccount=GameAccount(user)});
        }).RequireRateLimiting("audit-signup");

        app.MapGet("/api/auth/game/me", async (ClaimsPrincipal principal, AuthDbContext db, CancellationToken ct) =>
        {
            if (principal.HasClaim("token_use", "device") ||
                !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
                return Results.Forbid();
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
            return user is null ? Results.NotFound() : Results.Ok(GameAccount(user));
        }).RequireAuthorization();
    }

    // Registration and creation of the game identity share the same durable User row.
    private static object GameAccount(User user) => new
    {
        id = user.Id,
        nickname = string.IsNullOrWhiteSpace(user.Nickname) ? user.Login : user.Nickname,
        createdAt = DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc),
        level = 1,
        rank = "F",
        state = "EgoShard"
    };
}
