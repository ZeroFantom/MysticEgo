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
    public sealed record Request(string Login,string Password,string Nickname);
    public static void MapAuditRegistration(this WebApplication app)
    {
        app.MapPost("/api/auth/audit/register",async(Request input,AuthDbContext db,IPasswordHasher hasher,CancellationToken ct)=>
        {
            var login=(input.Login??"").Trim();var nickname=(input.Nickname??"").Trim();
            if(!System.Text.RegularExpressions.Regex.IsMatch(login,"^[a-zA-Z0-9_.-]{4,40}$") ||
               (input.Password??"").Length is <12 or >128 || nickname.Length is <2 or >60)
                return Results.BadRequest(new {error="Логин 4–40 символов; пароль минимум 12; никнейм 2–60."});
            if(await db.Users.AnyAsync(x=>x.Login==login,ct))return Results.Conflict(new {error="Логин уже занят."});
            // Пользователь без административных ролей. Привилегии Auth/Global не выдаются автоматически.
            var user = new User{Id=Guid.NewGuid(),Login=login,Nickname=nickname,
                PasswordHash=hasher.Hash(input.Password!),IsActive=true,CreatedAt=DateTime.UtcNow};
            db.Users.Add(user);
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
