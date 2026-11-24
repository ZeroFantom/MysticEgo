namespace Models.Autorization.Internal;

public sealed class ForbiddenException : AuthException
{
    public ForbiddenException(string message)
        : base("forbidden", message, 403) { }
}