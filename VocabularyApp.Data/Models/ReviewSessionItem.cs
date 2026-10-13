namespace VocabularyApp.Data.Models;

public class ReviewSessionItem
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int UserId { get; set; }
    public int UserWordId { get; set; }
    public int Ordinal { get; set; }
    public long CapturedScheduleVersion { get; set; }
    public int WordDefinitionId { get; set; }
    public long DefinitionRevision { get; set; }
    public string WordText { get; set; } = string.Empty;
    public string DefinitionText { get; set; } = string.Empty;
    public string PartOfSpeech { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "word-to-definition";
    public string OptionsJson { get; set; } = string.Empty;
    public int CorrectOptionId { get; set; }
    public Guid? AcceptedSubmissionId { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public int? SelectedOptionId { get; set; }
    public bool? IsCorrect { get; set; }
    public string? ReceiptJson { get; set; }
    public int? ReceiptContractVersion { get; set; }
    public ReviewSession Session { get; set; } = null!;
    public UserWord UserWord { get; set; } = null!;
    public ReviewHistory? History { get; set; }
}
