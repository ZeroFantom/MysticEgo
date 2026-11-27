using Models.Autorization.Contracts.Responses;

namespace Models.Autorization.Services;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(
        string login, string password, string? ip, string? userAgent,
        CancellationToken ct = default);
    Task<AuthResponse?> RefreshAsync(
        string refreshToken, string? ip, string? userAgent,
        CancellationToken ct = default);
    Task<bool> LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task<string?> ApproveDesktopAsync(string refreshToken,string challenge,CancellationToken ct);
    Task<AuthResponse?> RedeemDesktopAsync(string code,string verifier,string? ip,string? userAgent,CancellationToken ct);
    Task<int> RevokeAllAsync(Guid userId, CancellationToken ct = default);
}