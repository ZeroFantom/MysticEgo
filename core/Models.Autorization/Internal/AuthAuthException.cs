namespace Models.Autorization.Internal;

public sealed class AuthAuthException : AuthException
{
    public AuthAuthException(string message)
        : base("invalid_credentials", message, 401) { }
}