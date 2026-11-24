namespace Models.Autorization.Contracts.Requests;

public sealed class AdminChangePasswordRequest
{
    public string NewPassword { get; set; } = "";
}