namespace VocabularyApp.Data.Models;

public class UserWordReviewState
{
    public int UserWordId { get; set; }
    public int UserId { get; set; }
    public int? ScheduledWordDefinitionId { get; set; }
    public long? ScheduledDefinitionRevision { get; set; }
    public DateTime EnrollmentAt { get; set; }
    public DateTime NextReviewAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public int ConsecutiveCorrectCount { get; set; }
    public int ReviewIntervalDays { get; set; }
    public int LapseCount { get; set; }
    // Stable persistence values: New=0, Learning=1, Reviewing=2 (Phase 1 domain contract).
    public int ReviewStatus { get; set; }
    public string AlgorithmVersion { get; set; } = "interval-v1";
    public long ScheduleVersion { get; set; } = 1;
    public UserWord UserWord { get; set; } = null!;
    public WordDefinition? ScheduledWordDefinition { get; set; }
}
