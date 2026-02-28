using System.ComponentModel.DataAnnotations;

namespace VocabularyApp.Data.Models;

public class QuizResult
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public int UserWordId { get; set; }
    public Guid QuizSessionId { get; set; }

    public QuizType QuizType { get; set; } // Definition, PartOfSpeech, Usage, etc.

    public bool IsCorrect { get; set; }

    [StringLength(500)]
    public string? UserAnswer { get; set; }

    [StringLength(500)]
    public string? CorrectAnswer { get; set; }

    public int ResponseTimeSeconds { get; set; }

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual UserWord UserWord { get; set; } = null!;
}

public enum QuizType
{
    Definition = 1,
    PartOfSpeech = 2,
    Usage = 3,
    Synonym = 4,
    Antonym = 5
}