using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Services;

// Shared selection rules for enrollment, preference changes and review validation.
public sealed class ReviewStateCoordinator(ApplicationDbContext db, IReviewScheduler scheduler,
    TimeProvider clock, IOptionsMonitor<ReviewOptions> options)
{
    public bool EnrollmentEnabled => options.CurrentValue.EnrollmentEnabled;
    public static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim();
    public static WordDefinition? Select(UserWord word)
    {
        var definitions = word.Word.WordDefinitions;
        var preferred = definitions.FirstOrDefault(d => d.Id == word.PreferredWordDefinitionId && d.PartOfSpeechId == word.PartOfSpeechId);
        // An explicit blank preferred meaning is blocked, rather than silently replaced.
        return preferred ?? definitions.Where(d => d.PartOfSpeechId == word.PartOfSpeechId && !string.IsNullOrWhiteSpace(d.Definition))
            .OrderBy(d => d.DisplayOrder).ThenBy(d => d.Id).FirstOrDefault();
    }
    public static bool Usable(UserWord word) => !string.IsNullOrWhiteSpace(word.Word.Text) &&
        Select(word) is { } definition && !string.IsNullOrWhiteSpace(definition.Definition);
    public IQueryable<UserWord> Words(int userId) => db.UserWords.Where(w => w.UserId == userId)
        .Include(w => w.Word).ThenInclude(w => w.WordDefinitions).ThenInclude(d => d.PartOfSpeech);
    public async Task EnrollAsync(UserWord word)
    {
        if (!EnrollmentEnabled) return;
        // Called only for genuinely new tracked vocabulary, before its single atomic SaveChanges.
        var canonical = await db.Words.Include(w => w.WordDefinitions).SingleAsync(w => w.Id == word.WordId);
        word.Word = canonical;
        var state = new UserWordReviewState { UserWord = word, UserId = word.UserId };
        Apply(state, scheduler.Enroll(clock.GetUtcNow().UtcDateTime));
        Meaning(state, word);
        db.UserWordReviewStates.Add(state);
    }
    public void Reset(UserWordReviewState state, UserWord word)
    {
        Apply(state, scheduler.Reset(Read(state), clock.GetUtcNow().UtcDateTime));
        Meaning(state, word);
    }
    public static void Meaning(UserWordReviewState state, UserWord word)
    {
        var definition = Usable(word) ? Select(word) : null;
        state.ScheduledWordDefinitionId = definition?.Id;
        state.ScheduledDefinitionRevision = definition?.ContentRevision;
    }
    public static ReviewScheduleState Read(UserWordReviewState s) => new(s.EnrollmentAt, s.NextReviewAt,
        s.LastReviewedAt, s.ReviewIntervalDays, s.ConsecutiveCorrectCount, s.LapseCount,
        (ReviewStatus)s.ReviewStatus, s.ScheduleVersion, s.AlgorithmVersion);
    public static void Apply(UserWordReviewState s, ReviewScheduleState next)
    {
        s.EnrollmentAt = next.EnrollmentAt; s.NextReviewAt = next.NextReviewAt; s.LastReviewedAt = next.LastReviewedAt;
        s.ReviewIntervalDays = next.ReviewIntervalDays; s.ConsecutiveCorrectCount = next.ConsecutiveCorrectCount;
        s.LapseCount = next.LapseCount; s.ReviewStatus = (int)next.ReviewStatus;
        s.ScheduleVersion = next.ScheduleVersion; s.AlgorithmVersion = next.AlgorithmVersion;
    }
}
