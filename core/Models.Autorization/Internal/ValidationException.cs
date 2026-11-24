namespace Models.Autorization.Internal;

public sealed class ValidationException : AuthException
{
    public ValidationException(string message)
        : base("validation_failed", message, 400) { }
}