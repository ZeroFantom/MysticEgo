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
        string? TermsVersion = null, string? PrivacyVersion = null);

    private sealed record Document(string Version, string Url);
    private sealed record RegistrationDocuments(Document Terms, Document Privacy);

    private static Document? ReadDocument(IConfiguration config, string name)
    {
        var version = config[$"AuditRegistration:{name}:Version"];
        var url = config[$"AuditRegistration:{name}:Url"];
        return version is { Length: > 0 and <= 40 } &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.UserInfo.Length == 0 ? new Document(version, uri.AbsoluteUri) : null;
    }

    private static RegistrationDocuments? ReadDocuments(IConfiguration config) =>
        ReadDocument(config, "Terms") is { } terms && ReadDocument(config, "Privacy") is { } privacy
            ? new RegistrationDocuments(terms, privacy)
            : null;

    private static IResult DocumentsUnavailable() => Results.Json(
        new { error = "Документы регистрации ещё не опубликованы." }, statusCode: 503);
    public static void MapAuditRegistration(this WebApplication app)
    {
        app.MapGet("/api/auth/audit/registration-documents", (HttpContext context, IConfiguration config) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!config.GetValue<bool>("AuditRegistration:RequireDocuments"))
                return Results.Ok(new { required = false });

            return ReadDocuments(config) is { } documents
                ? Results.Ok(new { required = true, documents.Terms, documents.Privacy })
                : DocumentsUnavailable();
        }).AllowAnonymous();

        app.MapPost("/api/auth/audit/register", async (
            Request input, AuthDbContext db, IPasswordHasher hasher, IConfiguration config, CancellationToken ct) =>
        {
            RegistrationDocuments? documents = null;
            if (config.GetValue<bool>("AuditRegistration:RequireDocuments"))
            {
                documents = ReadDocuments(config);
                if (documents is null)
                    return DocumentsUnavailable();
                if (input.TermsVersion != documents.Terms.Version || input.PrivacyVersion != documents.Privacy.Version)
                    return Results.BadRequest(new { error = "Откройте актуальные условия и подтвердите ознакомление с документами." });
            }

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
