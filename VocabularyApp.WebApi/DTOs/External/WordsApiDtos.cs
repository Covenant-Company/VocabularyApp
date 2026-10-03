namespace VocabularyApp.WebApi.DTOs.External;

public sealed class WordsApiResponse
{
    public string? Word { get; set; }
    public List<WordsApiResult?>? Results { get; set; }
    // Omission is allowed; explicit null is rejected by the provider boundary.
    public Dictionary<string, string?>? Pronunciation { get; set; } = new();
}

public sealed class WordsApiResult
{
    public string? Definition { get; set; }
    public string? PartOfSpeech { get; set; }
    public List<string?>? Examples { get; set; } = new();
}
