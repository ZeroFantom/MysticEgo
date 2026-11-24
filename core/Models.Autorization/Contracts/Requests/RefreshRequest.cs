namespace Models.Autorization.Contracts.Requests;

public sealed class RefreshRequest
{
    public string RefreshToken { get; set; } = "";
}