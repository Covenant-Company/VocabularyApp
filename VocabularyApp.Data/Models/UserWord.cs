using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VocabularyApp.Data.Models;

public class UserWord
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public int WordId { get; set; }
    [NotMapped]
    public DateTime CreatedAt { get; set; }

    [StringLength(500)]
    public string? PersonalNotes { get; set; } // User's personal notes about the word

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastReviewedAt { get; set; }
    public DateTime? LastCorrectAt { get; set; } // For quiz performance tracking

    public int CorrectAnswers { get; set; } = 0;
    public int TotalAttempts { get; set; } = 0;

    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual Word Word { get; set; } = null!;
    // Removed PartOfSpeech navigation

    public int PartOfSpeechId { get; set; }
    public int? PreferredWordDefinitionId { get; set; }

    [NotMapped]
    [StringLength(1000)]
    public string? CustomDefinition { get; set; } // User's personalized definition (optional)

    public bool IsFavorite { get; set; } = false;
    [NotMapped]
    public int DifficultyLevel { get; set; } = 1; // 1-5 scale for spaced repetition

    public virtual PartOfSpeech PartOfSpeech { get; set; } = null!;
    public virtual WordDefinition? PreferredWordDefinition { get; set; }
    public virtual ICollection<SampleSentence> SampleSentences { get; set; } = new List<SampleSentence>();
}