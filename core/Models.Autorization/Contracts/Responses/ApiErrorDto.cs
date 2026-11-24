namespace Models.Autorization.Contracts.Responses;

public sealed class ApiErrorDto
{
    public string Code { get; set; } = "error";
    public string Message { get; set; } = "";
    public string? Detail { get; set; }
    public string? TraceId { get; set; }
}