namespace VocabularyApp.WebApi.Models
{
  public class AddWordRequest
  {
    public string? Word { get; set; }
    public string? Definition { get; set; }
    public string? Example { get; set; }
    public string? PartOfSpeech { get; set; }
    public string? Pronunciation { get; set; }
    public int? PreferredWordDefinitionId { get; set; }
  }
}
