using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VocabularyApp.Data.Models;

namespace VocabularyApp.Data.Configurations;

public sealed class UserWordReviewStateConfiguration : IEntityTypeConfiguration<UserWordReviewState>
{
    public void Configure(EntityTypeBuilder<UserWordReviewState> entity)
    {
        entity.ToTable("UserWordReviewStates", table =>
        {
            table.HasCheckConstraint("CK_UserWordReviewStates_Progression", ReviewMapping.Progression("ReviewStatus", "ConsecutiveCorrectCount", "ReviewIntervalDays"));
            table.HasCheckConstraint("CK_UserWordReviewStates_Versions", "[ScheduleVersion] >= 1 AND [LapseCount] >= 0 AND [AlgorithmVersion] = 'interval-v1'");
            table.HasCheckConstraint("CK_UserWordReviewStates_Meaning", "([ScheduledWordDefinitionId] IS NULL AND [ScheduledDefinitionRevision] IS NULL) OR ([ScheduledWordDefinitionId] IS NOT NULL AND [ScheduledDefinitionRevision] IS NOT NULL AND [ScheduledDefinitionRevision] >= 1)");
            table.HasCheckConstraint("CK_UserWordReviewStates_Timestamps", "[NextReviewAt] >= [EnrollmentAt] AND (([ReviewStatus] = 0 AND [LastReviewedAt] IS NULL) OR ([ReviewStatus] <> 0 AND [LastReviewedAt] IS NOT NULL AND [LastReviewedAt] >= [EnrollmentAt] AND [NextReviewAt] > [LastReviewedAt]))");
        });
        entity.HasKey(e => e.UserWordId);
        entity.Property(e => e.UserWordId).ValueGeneratedNever();
        entity.HasOne(e => e.UserWord).WithOne(e => e.ReviewState)
            .HasForeignKey<UserWordReviewState>(e => new { e.UserId, e.UserWordId })
            .HasPrincipalKey<UserWord>(e => new { e.UserId, e.Id }).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(e => e.ScheduledWordDefinition).WithMany()
            .HasForeignKey(e => e.ScheduledWordDefinitionId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(e => new { e.UserId, e.NextReviewAt, e.UserWordId });
        entity.Property(e => e.ScheduleVersion).IsConcurrencyToken();
        ReviewMapping.Text(entity.Property(e => e.AlgorithmVersion), 32);
        ReviewMapping.Utc(entity.Property(e => e.EnrollmentAt));
        ReviewMapping.Utc(entity.Property(e => e.NextReviewAt));
        ReviewMapping.Utc(entity.Property(e => e.LastReviewedAt));
    }
}
