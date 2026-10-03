namespace VocabularyApp.WebApi.DTOs;

public sealed class ApiErrorResponse
{
    public bool Success => false;
    public object? Data => null;
    public required string Error { get; init; }
    public string Message => Error;
    public required string Code { get; init; }
    public required string TraceId { get; init; }
}
