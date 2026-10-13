using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VocabularyApp.Data.Models;

namespace VocabularyApp.Data.Configurations;

public sealed class ReviewSessionItemConfiguration : IEntityTypeConfiguration<ReviewSessionItem>
{
    public void Configure(EntityTypeBuilder<ReviewSessionItem> entity)
    {
        entity.ToTable("ReviewSessionItems", table =>
        {
            table.HasCheckConstraint("CK_ReviewSessionItems_Question", "[Ordinal] BETWEEN 0 AND 19 AND [CapturedScheduleVersion] >= 1 AND [DefinitionRevision] >= 1 AND [CorrectOptionId] BETWEEN 0 AND 3 AND [QuestionType] = 'word-to-definition'");
            table.HasCheckConstraint("CK_ReviewSessionItems_Acceptance", "([AcceptedSubmissionId] IS NULL AND [AcceptedAt] IS NULL AND [SelectedOptionId] IS NULL AND [IsCorrect] IS NULL AND [ReceiptJson] IS NULL AND [ReceiptContractVersion] IS NULL) OR ([AcceptedSubmissionId] IS NOT NULL AND [AcceptedAt] IS NOT NULL AND [SelectedOptionId] IS NOT NULL AND [SelectedOptionId] BETWEEN 0 AND 3 AND [IsCorrect] IS NOT NULL AND [ReceiptJson] IS NOT NULL AND [ReceiptContractVersion] IS NOT NULL AND [ReceiptContractVersion] = 1)");
        });
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();
        entity.HasAlternateKey(e => new { e.UserId, e.Id, e.UserWordId });
        entity.HasOne(e => e.Session).WithMany(e => e.Items)
            .HasForeignKey(e => new { e.UserId, e.SessionId }).HasPrincipalKey(e => new { e.UserId, e.Id }).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(e => e.UserWord).WithMany()
            .HasForeignKey(e => new { e.UserId, e.UserWordId }).HasPrincipalKey(e => new { e.UserId, e.Id }).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(e => new { e.SessionId, e.Ordinal }).IsUnique();
        entity.HasIndex(e => new { e.SessionId, e.UserWordId }).IsUnique();
        entity.HasIndex(e => new { e.UserId, e.AcceptedSubmissionId }).IsUnique()
            .HasFilter("[AcceptedSubmissionId] IS NOT NULL");
        entity.HasIndex(e => new { e.UserId, e.UserWordId, e.AcceptedAt });
        ReviewMapping.Text(entity.Property(e => e.WordText), 100);
        ReviewMapping.Text(entity.Property(e => e.DefinitionText), 1000);
        ReviewMapping.Text(entity.Property(e => e.PartOfSpeech), 100);
        ReviewMapping.Text(entity.Property(e => e.Prompt), 1500);
        ReviewMapping.Text(entity.Property(e => e.QuestionType), 32);
        ReviewMapping.Text(entity.Property(e => e.OptionsJson));
        entity.Property(e => e.ReceiptJson).IsUnicode();
        ReviewMapping.Utc(entity.Property(e => e.AcceptedAt));
    }
}
