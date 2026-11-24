namespace Models.Autorization.Configuration;

public sealed class SessionPolicy
{
    public bool RevokeTokensOnPasswordChange { get; set; } = true;
    public bool RevokeTokensOnDeactivate { get; set; } = true;
}