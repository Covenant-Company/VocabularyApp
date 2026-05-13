namespace VocabularyApp.WebApi.DTOs;

public class UserVocabularyItemDto
{
  public int Id { get; set; }
  public string Word { get; set; } = string.Empty;
  public string Definition { get; set; } = string.Empty;
  public string? Example { get; set; }
  public string PartOfSpeech { get; set; } = string.Empty;
  public string? Pronunciation { get; set; }
  public string? AudioUrl { get; set; }
  public DateTime AddedAt { get; set; }
  public bool IsFavorite { get; set; }
  public string? PersonalNotes { get; set; }
  public int CorrectAnswers { get; set; }
  public int TotalAttempts { get; set; }
  public double? AccuracyRate => TotalAttempts > 0 ? (double)CorrectAnswers / TotalAttempts * 100 : null;
}

public class UpdateFavoriteRequestDto
{
  public bool IsFavorite { get; set; }
}

public class UserVocabularyResponseDto
{
  public List<UserVocabularyItemDto> Words { get; set; } = new();
  public int TotalCount { get; set; }
  public int Page { get; set; }
  public int PageSize { get; set; }
  public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}