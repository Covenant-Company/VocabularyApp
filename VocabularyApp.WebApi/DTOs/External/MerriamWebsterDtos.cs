using System.Text.Json.Serialization;

namespace VocabularyApp.WebApi.DTOs.External;

public sealed class MerriamWebsterEntry
{
    [JsonPropertyName("meta")]
    public MerriamWebsterMetadata? Metadata { get; set; }

    [JsonPropertyName("hwi")]
    public MerriamWebsterHeadwordInformation? HeadwordInformation { get; set; }
}

public sealed class MerriamWebsterMetadata
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("stems")]
    public List<string>? Stems { get; set; } = new();
}

public sealed class MerriamWebsterHeadwordInformation
{
    [JsonPropertyName("hw")]
    public string? Headword { get; set; }

    [JsonPropertyName("prs")]
    public List<MerriamWebsterPronunciation>? Pronunciations { get; set; } = new();
}

public sealed class MerriamWebsterPronunciation
{
    [JsonPropertyName("sound")]
    public MerriamWebsterSound? Sound { get; set; }
}

public sealed class MerriamWebsterSound
{
    [JsonPropertyName("audio")]
    public string? Audio { get; set; }
}
