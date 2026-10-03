namespace VocabularyApp.WebApi.DTOs;

public sealed class SuccessResponse<T>
{
    public bool Success => true;
    public required T Data { get; init; }
}
