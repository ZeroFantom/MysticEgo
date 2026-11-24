namespace Models.Autorization.Internal;

public class AuthException : Exception
{
    public string Code { get; }
    public int Status { get; }

    public AuthException(string code, string message, int status = 400)
        : base(message)
    {
        Code = code;
        Status = status;
    }
}