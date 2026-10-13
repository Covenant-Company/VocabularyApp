using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VocabularyApp.Data.Models;

namespace VocabularyApp.Data.Configurations;

public sealed class ReviewHistoryConfiguration : IEntityTypeConfiguration<ReviewHistory>
{
    public void Configure(EntityTypeBuilder<ReviewHistory> entity)
    {
        entity.ToTable("ReviewHistories", table =>
        {
            table.HasCheckConstraint("CK_ReviewHistories_Versions", "[PreviousScheduleVersion] >= 1 AND [PreviousScheduleVersion] < 9223372036854775807 AND [NextScheduleVersion] = [PreviousScheduleVersion] + 1 AND [DefinitionRevision] >= 1 AND [ReceiptContractVersion] = 1 AND [AlgorithmVersion] = 'interval-v1'");
            table.HasCheckConstraint("CK_ReviewHistories_PreviousProgression", ReviewMapping.Progression("PreviousStatus", "PreviousCorrectCount", "PreviousIntervalDays"));
            table.HasCheckConstraint("CK_ReviewHistories_NextProgression", "[NextStatus] IN (1, 2) AND (" + ReviewMapping.Progression("NextStatus", "NextCorrectCount", "NextIntervalDays") + ")");
            table.HasCheckConstraint("CK_ReviewHistories_Outcome", "[SelectedOptionId] BETWEEN 0 AND 3 AND [PreviousLapseCount] >= 0 AND [NextLapseCount] >= 0 AND [QuestionType] = 'word-to-definition' AND [OutcomeSource] = 'server-multiple-choice'");
            table.HasCheckConstraint("CK_ReviewHistories_Timestamps", "[ReviewedAt] >= [PreviousNextReviewAt] AND [NextReviewAt] > [ReviewedAt] AND (([PreviousStatus] = 0 AND [PreviousLastReviewedAt] IS NULL AND [ElapsedSincePreviousReviewTicks] IS NULL) OR ([PreviousStatus] <> 0 AND [PreviousLastReviewedAt] IS NOT NULL AND [PreviousLastReviewedAt] <= [ReviewedAt] AND [ElapsedSincePreviousReviewTicks] IS NOT NULL AND [ElapsedSincePreviousReviewTicks] >= 0))");
        });
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();
        entity.HasOne(e => e.SessionItem).WithOne(e => e.History)
            .HasForeignKey<ReviewHistory>(e => new { e.UserId, e.SessionItemId, e.UserWordId })
            .HasPrincipalKey<ReviewSessionItem>(e => new { e.UserId, e.Id, e.UserWordId }).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(e => e.UserWord).WithMany()
            .HasForeignKey(e => new { e.UserId, e.UserWordId }).HasPrincipalKey(e => new { e.UserId, e.Id }).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(e => e.SessionItemId).IsUnique();
        entity.HasIndex(e => new { e.UserId, e.SubmissionId }).IsUnique();
        entity.HasIndex(e => new { e.UserId, e.UserWordId, e.ReviewedAt, e.Id });
        ReviewMapping.Text(entity.Property(e => e.WordText), 100);
        ReviewMapping.Text(entity.Property(e => e.DefinitionText), 1000);
        ReviewMapping.Text(entity.Property(e => e.QuestionType), 32);
        ReviewMapping.Text(entity.Property(e => e.OutcomeSource), 32);
        ReviewMapping.Text(entity.Property(e => e.SelectedAnswer), 1000);
        ReviewMapping.Text(entity.Property(e => e.CorrectAnswer), 1000);
        ReviewMapping.Text(entity.Property(e => e.AlgorithmVersion), 32);
        ReviewMapping.Text(entity.Property(e => e.ReceiptJson));
        ReviewMapping.Utc(entity.Property(e => e.ReviewedAt));
        ReviewMapping.Utc(entity.Property(e => e.PreviousNextReviewAt));
        ReviewMapping.Utc(entity.Property(e => e.NextReviewAt));
        ReviewMapping.Utc(entity.Property(e => e.PreviousLastReviewedAt));
    }
}
