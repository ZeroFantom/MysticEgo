namespace Models.Autorization.Configuration;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "MysticEgo";
    public string Audience { get; set; } = "MysticEgo.Clients";
    public string SecretKey { get; set; } = ""; // Устаревший HMAC; только для разработки.
    public string? PrivateKeyPath { get; set; }
    public string? PublicKeyPath { get; set; }

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public int ClockSkewSeconds { get; set; } = 30;
}