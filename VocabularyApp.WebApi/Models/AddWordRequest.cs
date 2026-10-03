namespace VocabularyApp.WebApi.Models
{
  public class AddWordRequest
  {
    public string? Word { get; set; }
    // Legacy accepted fields; these do not author or overwrite canonical data.
    public string? Definition { get; set; }
    public string? Example { get; set; }
    // Used only to resolve the existing POS fallback when no preferred ID is supplied.
    public string? PartOfSpeech { get; set; }
    // Legacy accepted field; ignored by canonical persistence.
    public string? Pronunciation { get; set; }
    public int? PreferredWordDefinitionId { get; set; }
  }
}
