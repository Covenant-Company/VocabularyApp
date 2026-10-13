namespace VocabularyApp.Data.Models;

public class ReviewSession
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int RequestedQuestionCount { get; set; }
    public int ItemCount { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long Version { get; set; } = 1;
    public User User { get; set; } = null!;
    public ICollection<ReviewSessionItem> Items { get; set; } = new List<ReviewSessionItem>();
}
