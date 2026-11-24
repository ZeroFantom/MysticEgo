namespace Models.Autorization.Contracts.Responses;

public sealed class AuthResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public UserDto User { get; set; } = new();
}