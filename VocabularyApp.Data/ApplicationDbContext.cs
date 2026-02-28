using Microsoft.EntityFrameworkCore;
using VocabularyApp.Data.Models;

namespace VocabularyApp.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    // DbSets for all entities
    public DbSet<User> Users { get; set; }
    public DbSet<Word> Words { get; set; }
    public DbSet<PartOfSpeech> PartsOfSpeech { get; set; }
    public DbSet<WordDefinition> WordDefinitions { get; set; }
    public DbSet<UserWord> UserWords { get; set; } = null!;
    public DbSet<SampleSentence> SampleSentences { get; set; }
    public DbSet<QuizResult> QuizResults { get; set; }
    public DbSet<ChatHistory> ChatHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Configure Word entity
        modelBuilder.Entity<Word>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Text).IsUnique(); // Ensure unique words in canonical dictionary
        });

        // Configure PartOfSpeech entity
        modelBuilder.Entity<PartOfSpeech>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Configure WordDefinition entity
        modelBuilder.Entity<WordDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);


            // Foreign key relationships
            entity.HasOne(e => e.Word)
                .WithMany(w => w.WordDefinitions)
                .HasForeignKey(e => e.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PartOfSpeech)
                .WithMany(p => p.WordDefinitions)
                .HasForeignKey(e => e.PartOfSpeechId)
                .OnDelete(DeleteBehavior.Restrict);

            // Ensure unique combination of Word + PartOfSpeech + DisplayOrder
            entity.HasIndex(e => new { e.WordId, e.PartOfSpeechId, e.DisplayOrder }).IsUnique();
        });

        // Configure UserWord entity (the key relationship table)
        modelBuilder.Entity<UserWord>(entity =>
        {
            entity.HasKey(e => e.Id);


            // Foreign key relationships
            entity.HasOne(e => e.User)
                .WithMany(u => u.UserWords)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Word)
                .WithMany(w => w.UserWords)
                .HasForeignKey(e => e.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            // Removed PartOfSpeech foreign key and composite index (no longer storing PartOfSpeech per user word)
            entity.HasOne(e => e.PartOfSpeech)
                .WithMany(p => p.UserWords)
                .HasForeignKey(e => e.PartOfSpeechId)
                .OnDelete(DeleteBehavior.Restrict);

            // Ensure unique combination - each user can only have one entry per word+part-of-speech
            entity.HasIndex(e => new { e.UserId, e.WordId, e.PartOfSpeechId }).IsUnique();
        });

        // Configure SampleSentence entity
        modelBuilder.Entity<SampleSentence>(entity =>
        {
            entity.HasKey(e => e.Id);


            entity.HasOne(e => e.User)
                .WithMany(u => u.SampleSentences)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.NoAction); // Changed to NoAction to avoid cascade cycle

            entity.HasOne(e => e.UserWord)
                .WithMany(uw => uw.SampleSentences)
                .HasForeignKey(e => e.UserWordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure QuizResult entity
        modelBuilder.Entity<QuizResult>(entity =>
        {
            entity.HasKey(e => e.Id);


            entity.HasOne(e => e.User)
                .WithMany(u => u.QuizResults)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.NoAction); // Changed to NoAction to avoid cascade cycle

            entity.HasOne(e => e.UserWord)
                .WithMany()
                .HasForeignKey(e => e.UserWordId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for performance on quiz analytics
            entity.HasIndex(e => new { e.UserId, e.QuizSessionId, e.AttemptedAt });
        });

        // Configure ChatHistory entity
        modelBuilder.Entity<ChatHistory>(entity =>
        {
            entity.HasKey(e => e.Id);


            entity.HasOne(e => e.User)
                .WithMany(u => u.ChatHistories)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for performance on chat retrieval
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
        });

        // Seed initial data for PartsOfSpeech
        SeedPartsOfSpeech(modelBuilder);
    }

    private static void SeedPartsOfSpeech(ModelBuilder modelBuilder)
    {
        var seedDate = new DateTime(2025, 10, 4, 20, 0, 0, DateTimeKind.Utc);


        modelBuilder.Entity<PartOfSpeech>().HasData(
            new PartOfSpeech { Id = 1, Name = "Noun", Abbreviation = "n.", CreatedAt = seedDate },
            new PartOfSpeech { Id = 2, Name = "Verb", Abbreviation = "v.", CreatedAt = seedDate },
            new PartOfSpeech { Id = 3, Name = "Adjective", Abbreviation = "adj.", CreatedAt = seedDate },
            new PartOfSpeech { Id = 4, Name = "Adverb", Abbreviation = "adv.", CreatedAt = seedDate },
            new PartOfSpeech { Id = 5, Name = "Pronoun", Abbreviation = "pron.", CreatedAt = seedDate },
            new PartOfSpeech { Id = 6, Name = "Preposition", Abbreviation = "prep.", CreatedAt = seedDate },
            new PartOfSpeech { Id = 7, Name = "Conjunction", Abbreviation = "conj.", CreatedAt = seedDate },
            new PartOfSpeech { Id = 8, Name = "Interjection", Abbreviation = "interj.", CreatedAt = seedDate }
        );
    }
}