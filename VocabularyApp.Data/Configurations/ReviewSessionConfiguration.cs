using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VocabularyApp.Data.Models;

namespace VocabularyApp.Data.Configurations;

public sealed class ReviewSessionConfiguration : IEntityTypeConfiguration<ReviewSession>
{
    public void Configure(EntityTypeBuilder<ReviewSession> entity)
    {
        entity.ToTable("ReviewSessions", table =>
        {
            table.HasCheckConstraint("CK_ReviewSessions_Counts", "[RequestedQuestionCount] BETWEEN 1 AND 20 AND [ItemCount] BETWEEN 1 AND [RequestedQuestionCount]");
            table.HasCheckConstraint("CK_ReviewSessions_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_ReviewSessions_Timestamps", "[ExpiresAt] > [CreatedAt] AND ([CompletedAt] IS NULL OR [CompletedAt] >= [CreatedAt])");
        });
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();
        entity.HasAlternateKey(e => new { e.UserId, e.Id });
        entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(e => new { e.UserId, e.ExpiresAt, e.Id });
        entity.Property(e => e.Version).IsConcurrencyToken();
        ReviewMapping.Utc(entity.Property(e => e.CreatedAt));
        ReviewMapping.Utc(entity.Property(e => e.ExpiresAt));
        ReviewMapping.Utc(entity.Property(e => e.CompletedAt));
    }
}
