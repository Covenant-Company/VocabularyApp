namespace VocabularyApp.Data.Models;

/// <summary>Accepted binary review event. Normal application writes must remain append-only.</summary>
public class ReviewHistory
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public int UserWordId { get; set; }
    public Guid SessionItemId { get; set; }
    public Guid SubmissionId { get; set; }
    public DateTime ReviewedAt { get; set; }
    public int WordDefinitionId { get; set; }
    public long DefinitionRevision { get; set; }
    public string WordText { get; set; } = string.Empty;
    public string DefinitionText { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "word-to-definition";
    public string OutcomeSource { get; set; } = "server-multiple-choice";
    public bool IsCorrect { get; set; }
    public int SelectedOptionId { get; set; }
    public string SelectedAnswer { get; set; } = string.Empty;
    public string CorrectAnswer { get; set; } = string.Empty;
    public string AlgorithmVersion { get; set; } = "interval-v1";
    public long PreviousScheduleVersion { get; set; }
    public long NextScheduleVersion { get; set; }
    public DateTime PreviousNextReviewAt { get; set; }
    public DateTime NextReviewAt { get; set; }
    public DateTime? PreviousLastReviewedAt { get; set; }
    public int PreviousIntervalDays { get; set; }
    public int NextIntervalDays { get; set; }
    public int PreviousCorrectCount { get; set; }
    public int NextCorrectCount { get; set; }
    public int PreviousLapseCount { get; set; }
    public int NextLapseCount { get; set; }
    public int PreviousStatus { get; set; }
    public int NextStatus { get; set; }
    public long? ElapsedSincePreviousReviewTicks { get; set; }
    public string ReceiptJson { get; set; } = string.Empty;
    public int ReceiptContractVersion { get; set; } = 1;
    public ReviewSessionItem SessionItem { get; set; } = null!;
    public UserWord UserWord { get; set; } = null!;
}
