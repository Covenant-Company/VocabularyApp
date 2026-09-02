namespace VocabularyApp.WebApi.Configuration;

public sealed class MerriamWebsterOptions
{
    public const string SectionName = "MerriamWebster";
    public const string DefaultBaseUrl = "https://www.dictionaryapi.com/";
    public const int DefaultTimeoutSeconds = 5;

    public string BaseUrl { get; init; } = DefaultBaseUrl;
    public string? ApiKey { get; init; }
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;
}
