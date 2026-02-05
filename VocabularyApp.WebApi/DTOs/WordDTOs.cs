namespace VocabularyApp.WebApi.DTOs;

public class WordDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Pronunciation { get; set; }
    public string? AudioUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<WordDefinitionDto> Definitions { get; set; } = new();
}

public class WordDefinitionDto
{
    public int Id { get; set; }
    public string Definition { get; set; } = string.Empty;
    public string? Example { get; set; }
    public string PartOfSpeech { get; set; } = string.Empty;
    public string PartOfSpeechAbbreviation { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public class WordLookupRequest
{
    public string Word { get; set; } = string.Empty;
}

public class WordLookupResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public WordDto? Word { get; set; }
    public bool WasFoundInCache { get; set; } // Indicates if word was found in our local database vs external API
    public bool IsInUserVocabulary { get; set; } // Indicates if word is in the user's personal vocabulary
}