using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ReviewPersistenceModelTests : IClassFixture<IndependentConnectionDatabaseFixture>
{
    private readonly IndependentConnectionDatabaseFixture _fixture;
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    public ReviewPersistenceModelTests(IndependentConnectionDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task EnrollmentRoundTripsThePureSchedulerStateAndPreservesPersonalData()
    {
        var saved = await SeedAsync();
        var schedule = new IntervalV1ReviewScheduler().Enroll(Now);
        await using (var context = _fixture.CreateContext())
        {
            context.UserWordReviewStates.Add(new UserWordReviewState
            {
                UserWordId = saved.Id, UserId = saved.UserId,
                ScheduledWordDefinitionId = saved.PreferredWordDefinitionId, ScheduledDefinitionRevision = 1,
                EnrollmentAt = schedule.EnrollmentAt, NextReviewAt = schedule.NextReviewAt,
                LastReviewedAt = schedule.LastReviewedAt, ConsecutiveCorrectCount = schedule.ConsecutiveCorrectCount,
                ReviewIntervalDays = schedule.ReviewIntervalDays, ReviewStatus = (int)schedule.ReviewStatus,
                LapseCount = schedule.LapseCount, ScheduleVersion = schedule.ScheduleVersion, AlgorithmVersion = schedule.AlgorithmVersion
            });
            await context.SaveChangesAsync();
        }
        await using var read = _fixture.CreateContext();
        var actual = await read.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.Id);
        Assert.Equal(schedule, new ReviewScheduleState(actual.EnrollmentAt, actual.NextReviewAt, actual.LastReviewedAt,
            actual.ReviewIntervalDays, actual.ConsecutiveCorrectCount, actual.LapseCount, (ReviewStatus)actual.ReviewStatus,
            actual.ScheduleVersion, actual.AlgorithmVersion));
        Assert.Equal(DateTimeKind.Utc, actual.EnrollmentAt.Kind);
        Assert.True(new IntervalV1ReviewScheduler().IsDue(schedule, actual.NextReviewAt));
        var word = await read.UserWords.Include(w => w.ReviewState).SingleAsync(w => w.Id == saved.Id);
        Assert.NotNull(word.ReviewState);
        Assert.Equal("preserve notes", word.PersonalNotes);
        Assert.True(word.IsFavorite);
        Assert.Equal(5, word.TotalAttempts);
        Assert.Equal(3, word.CorrectAnswers);
        Assert.Equal(saved.PreferredWordDefinitionId, word.PreferredWordDefinitionId);
        Assert.Equal(saved.LastReviewedAt, word.LastReviewedAt);
        Assert.False(await read.ReviewHistories.AnyAsync(h => h.UserWordId == saved.Id));
    }

    [Fact]
    public async Task OneStatePerSavedWordIsEnforcedByTheDatabase()
    {
        var saved = await SeedAsync();
        await using (var context = _fixture.CreateContext())
        {
            context.UserWordReviewStates.Add(State(saved));
            await context.SaveChangesAsync();
        }
        await using var duplicate = _fixture.CreateContext();
        duplicate.UserWordReviewStates.Add(State(saved));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact]
    public async Task StateCannotUseAnotherUsersSavedWord()
    {
        var first = await SeedAsync();
        var other = await SeedAsync();
        var state = State(first);
        state.UserId = other.UserId;
        await AssertRejectedAsync(state);
    }

    [Fact]
    public async Task StateRequiresSavedWordAndAnySelectedDefinitionToExist()
    {
        var saved = await SeedAsync();
        var state = State(saved);
        state.UserWordId = int.MaxValue;
        await AssertRejectedAsync(state);
        state = State(saved);
        state.ScheduledWordDefinitionId = int.MaxValue;
        state.ScheduledDefinitionRevision = 1;
        await AssertRejectedAsync(state);
    }

    [Fact]
    public async Task BlockedMeaningMayHavePairedNullDefinitionFields()
    {
        var saved = await SeedAsync();
        await using var context = _fixture.CreateContext();
        var state = State(saved);
        context.UserWordReviewStates.Add(state);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var actual = await context.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.Id);
        Assert.Null(actual.ScheduledWordDefinitionId);
        Assert.Null(actual.ScheduledDefinitionRevision);
    }

    [Theory]
    [InlineData("meaning-pair")]
    [InlineData("meaning-revision")]
    [InlineData("version")]
    [InlineData("lapse")]
    [InlineData("algorithm")]
    [InlineData("status")]
    [InlineData("progression")]
    [InlineData("last-reviewed")]
    [InlineData("due-before-enrollment")]
    public async Task InvalidStateConstraintsAreEnforced(string violation)
    {
        var saved = await SeedAsync();
        var state = State(saved);
        switch (violation)
        {
            case "meaning-pair": state.ScheduledDefinitionRevision = 1; break;
            case "meaning-revision": state.ScheduledWordDefinitionId = saved.PreferredWordDefinitionId; state.ScheduledDefinitionRevision = 0; break;
            case "version": state.ScheduleVersion = 0; break;
            case "lapse": state.LapseCount = -1; break;
            case "algorithm": state.AlgorithmVersion = "fsrs"; break;
            case "status": state.ReviewStatus = 3; break;
            case "progression": state.ConsecutiveCorrectCount = 1; break;
            case "last-reviewed": state.LastReviewedAt = Now; break;
            case "due-before-enrollment": state.NextReviewAt = Now.AddTicks(-1); break;
        }
        await AssertRejectedAsync(state);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 7, 2)]
    [InlineData(4, 14, 2)]
    [InlineData(5, 30, 2)]
    [InlineData(6, 60, 2)]
    [InlineData(7, 90, 2)]
    [InlineData(0, 1, 1)]
    public async Task ValidReviewedProgressionAndUtcNullableTimeRoundTrip(int count, int interval, int status)
    {
        var saved = await SeedAsync();
        var state = State(saved);
        state.ConsecutiveCorrectCount = count; state.ReviewIntervalDays = interval; state.ReviewStatus = status;
        state.LapseCount = 1; state.LastReviewedAt = Now; state.NextReviewAt = Now.AddDays(interval); state.ScheduleVersion = 2;
        await using var context = _fixture.CreateContext();
        context.Add(state);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(DateTimeKind.Utc, (await context.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.Id)).LastReviewedAt!.Value.Kind);
    }

    [Fact]
    public async Task SessionAndStateTokensRejectStaleUpdatesOnSeparateConnections()
    {
        var saved = await SeedAsync();
        var session = Session(saved.UserId);
        await using (var context = _fixture.CreateContext())
        {
            context.AddRange(session, State(saved)); await context.SaveChangesAsync();
        }
        await using var first = _fixture.CreateContext();
        await using var stale = _fixture.CreateContext();
        Assert.NotSame(first.Database.GetDbConnection(), stale.Database.GetDbConnection());
        var firstState = await first.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.Id);
        var staleState = await stale.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.Id);
        firstState.ScheduleVersion++; await first.SaveChangesAsync();
        staleState.ScheduleVersion++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        stale.ChangeTracker.Clear();
        var firstSession = await first.ReviewSessions.SingleAsync(s => s.Id == session.Id);
        var staleSession = await stale.ReviewSessions.SingleAsync(s => s.Id == session.Id);
        firstSession.Version++; await first.SaveChangesAsync();
        staleSession.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    [Fact]
    public async Task ContentRevisionHasClrAndDatabaseDefaultsAndPortableConcurrency()
    {
        var saved = await SeedAsync();
        await using var first = _fixture.CreateContext();
        var defaultRevision = new WordDefinition();
        Assert.Equal(1, defaultRevision.ContentRevision);
        await first.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO WordDefinitions (WordId, PartOfSpeechId, Definition, DisplayOrder, CreatedAt) VALUES ({saved.WordId}, {saved.PartOfSpeechId}, {"raw definition"}, {99}, {Now})");
        var inserted = await first.WordDefinitions.SingleAsync(d => d.WordId == saved.WordId && d.DisplayOrder == 99);
        Assert.Equal(1, inserted.ContentRevision);
        await using var stale = _fixture.CreateContext();
        var staleDefinition = await stale.WordDefinitions.SingleAsync(d => d.Id == inserted.Id);
        inserted.ContentRevision = 2; await first.SaveChangesAsync();
        staleDefinition.ContentRevision = 2;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await Assert.ThrowsAsync<SqliteException>(() => first.Database.ExecuteSqlInterpolatedAsync($"UPDATE WordDefinitions SET ContentRevision = 0 WHERE Id = {inserted.Id}"));
    }

    [Theory]
    [InlineData("unknown-owner")]
    [InlineData("count-zero")]
    [InlineData("count-excess")]
    [InlineData("item-count")]
    [InlineData("expiry")]
    [InlineData("version")]
    public async Task SessionOwnerTimingCountsAndVersionAreConstrained(string violation)
    {
        var saved = await SeedAsync();
        var session = Session(saved.UserId);
        switch (violation)
        {
            case "unknown-owner": session.UserId = int.MaxValue; break;
            case "count-zero": session.RequestedQuestionCount = 0; break;
            case "count-excess": session.RequestedQuestionCount = 21; break;
            case "item-count": session.ItemCount = 3; break;
            case "expiry": session.ExpiresAt = session.CreatedAt; break;
            case "version": session.Version = 0; break;
        }
        await AssertRejectedAsync(session);
    }

    [Fact]
    public async Task ItemsEnforceBothSessionAndSavedWordOwnershipAndExistence()
    {
        var saved = await SeedAsync(); var other = await SeedAsync();
        var session = await PersistSessionAsync(saved.UserId);
        var item = Item(session, other);
        await AssertRejectedAsync(item); // UserId from session cannot own the other word.
        item = Item(session, saved); item.UserId = other.UserId;
        await AssertRejectedAsync(item); // Other owner cannot own the session.
        item = Item(session, saved); item.SessionId = Guid.NewGuid();
        await AssertRejectedAsync(item);
    }

    [Fact]
    public async Task DuplicateSessionOrdinalAndTargetAreRejectedButPendingKeysMayBeNull()
    {
        var saved = await SeedAsync(); var second = await SeedAsync(saved.UserId);
        var session = await PersistSessionAsync(saved.UserId);
        await PersistAsync(Item(session, saved));
        var duplicateOrdinal = Item(session, second);
        await AssertRejectedAsync(duplicateOrdinal);
        var duplicateTarget = Item(session, saved); duplicateTarget.Ordinal = 1;
        await AssertRejectedAsync(duplicateTarget);
        var anotherPending = Item(session, second); anotherPending.Ordinal = 1;
        await PersistAsync(anotherPending); // Both pending submission IDs are NULL.
    }

    [Theory]
    [InlineData("options-null")]
    [InlineData("prompt-null")]
    [InlineData("correct-option")]
    [InlineData("ordinal")]
    [InlineData("captured-version")]
    [InlineData("half-accepted")]
    [InlineData("receipt-version")]
    public async Task ItemRequiredSnapshotsAndAcceptanceCoherenceAreEnforced(string violation)
    {
        var saved = await SeedAsync(); var session = await PersistSessionAsync(saved.UserId);
        var item = Item(session, saved);
        switch (violation)
        {
            case "options-null": item.OptionsJson = null!; break;
            case "prompt-null": item.Prompt = null!; break;
            case "correct-option": item.CorrectOptionId = 4; break;
            case "ordinal": item.Ordinal = 20; break;
            case "captured-version": item.CapturedScheduleVersion = 0; break;
            case "half-accepted": item.AcceptedAt = Now; break;
            case "receipt-version": Accept(item, Guid.NewGuid()); item.ReceiptContractVersion = 2; break;
        }
        await AssertRejectedAsync(item);
    }

    [Fact]
    public async Task AcceptedKeyUniquenessIsOwnerScopedAcrossSessions()
    {
        var saved = await SeedAsync(); var second = await SeedAsync(saved.UserId); var other = await SeedAsync();
        var submission = Guid.NewGuid();
        var firstSession = await PersistSessionAsync(saved.UserId);
        var accepted = Item(firstSession, saved); Accept(accepted, submission); await PersistAsync(accepted);
        var sameOwnerSession = await PersistSessionAsync(saved.UserId);
        var duplicate = Item(sameOwnerSession, second); Accept(duplicate, submission); await AssertRejectedAsync(duplicate);
        var otherSession = await PersistSessionAsync(other.UserId);
        var separateOwner = Item(otherSession, other); Accept(separateOwner, submission); await PersistAsync(separateOwner);
    }

    [Fact]
    public async Task HistoryAndExactReceiptSurviveAFreshContextWithNoCanonicalHistoryForeignKey()
    {
        var saved = await SeedAsync(); var session = await PersistSessionAsync(saved.UserId);
        var item = Item(session, saved); Accept(item, Guid.NewGuid()); await PersistAsync(item);
        var history = History(item); history.DefinitionText = new string('x', 1000);
        await PersistAsync(history);
        await using var context = _fixture.CreateContext();
        var actual = await context.ReviewHistories.Include(h => h.SessionItem).ThenInclude(i => i.Session).SingleAsync(h => h.Id == history.Id);
        Assert.Equal(item.ReceiptJson, actual.ReceiptJson);
        Assert.Equal(item.ReceiptJson, actual.SessionItem.ReceiptJson);
        Assert.Equal(1000, actual.DefinitionText.Length);
        Assert.Equal(session.Id, actual.SessionItem.Session.Id);
        Assert.Equal(DateTimeKind.Utc, actual.ReviewedAt.Kind);
        Assert.DoesNotContain(context.Model.FindEntityType(typeof(ReviewHistory))!.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(WordDefinition));
    }

    [Fact]
    public async Task HistoryEnforcesTargetOwnershipItemUniquenessAndSubmissionUniqueness()
    {
        var saved = await SeedAsync(); var second = await SeedAsync(saved.UserId); var other = await SeedAsync();
        var session = await PersistSessionAsync(saved.UserId);
        var firstItem = Item(session, saved); Accept(firstItem, Guid.NewGuid()); await PersistAsync(firstItem);
        var firstHistory = History(firstItem); await PersistAsync(firstHistory);
        await AssertRejectedAsync(History(firstItem)); // Same item, new history ID.
        var secondItem = Item(session, second); secondItem.Ordinal = 1; Accept(secondItem, Guid.NewGuid()); await PersistAsync(secondItem);
        var duplicateSubmission = History(secondItem); duplicateSubmission.SubmissionId = firstHistory.SubmissionId;
        await AssertRejectedAsync(duplicateSubmission);
        var wrongTarget = History(secondItem); wrongTarget.UserWordId = saved.Id;
        await AssertRejectedAsync(wrongTarget);
        var wrongOwner = History(secondItem); wrongOwner.UserId = other.UserId; wrongOwner.UserWordId = other.Id;
        await AssertRejectedAsync(wrongOwner);
    }

    [Theory]
    [InlineData("receipt-null")]
    [InlineData("version-jump")]
    [InlineData("status")]
    [InlineData("outcome-source")]
    [InlineData("review-before-due")]
    public async Task HistoryRequiredFieldsAndTransitionBoundsAreEnforced(string violation)
    {
        var saved = await SeedAsync(); var session = await PersistSessionAsync(saved.UserId);
        var item = Item(session, saved); Accept(item, Guid.NewGuid()); await PersistAsync(item);
        var history = History(item);
        switch (violation)
        {
            case "receipt-null": history.ReceiptJson = null!; break;
            case "version-jump": history.NextScheduleVersion = 3; break;
            case "status": history.NextStatus = 0; break;
            case "outcome-source": history.OutcomeSource = "inferred-quiz"; break;
            case "review-before-due": history.ReviewedAt = Now.AddTicks(-1); break;
        }
        await AssertRejectedAsync(history);
    }

    [Fact]
    public async Task StateOnlySavedWordDeletionCascadesStateButAcceptedAuditRequiresExplicitDeletionOrder()
    {
        var saved = await SeedAsync();
        await PersistAsync(State(saved));
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM UserWords WHERE Id = {saved.Id}");
            Assert.False(await context.UserWordReviewStates.AnyAsync(s => s.UserWordId == saved.Id));
        }
        var retained = await SeedAsync(); var session = await PersistSessionAsync(retained.UserId);
        var item = Item(session, retained); Accept(item, Guid.NewGuid()); await PersistAsync(item); await PersistAsync(History(item));
        await using var database = _fixture.CreateContext();
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM UserWords WHERE Id = {retained.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ReviewSessions WHERE Id = {session.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ReviewSessionItems WHERE Id = {item.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Id = {retained.UserId}"));
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ReviewHistories WHERE SessionItemId = {item.Id}");
        await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ReviewSessionItems WHERE Id = {item.Id}");
        await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ReviewSessions WHERE Id = {session.Id}");
        await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM UserWords WHERE Id = {retained.Id}");
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task CurrentDefinitionForeignKeyBlocksRemovalWhileHistoricalIdentifierDoesNot()
    {
        var saved = await SeedAsync(); var state = State(saved);
        state.ScheduledWordDefinitionId = saved.PreferredWordDefinitionId; state.ScheduledDefinitionRevision = 1; await PersistAsync(state);
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserWords SET PreferredWordDefinitionId = NULL WHERE Id = {saved.Id}");
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM WordDefinitions WHERE Id = {saved.PreferredWordDefinitionId}"));
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserWordReviewStates SET ScheduledWordDefinitionId = NULL, ScheduledDefinitionRevision = NULL WHERE UserWordId = {saved.Id}");
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM WordDefinitions WHERE Id = {saved.PreferredWordDefinitionId}");
    }

    [Fact]
    public async Task NonUtcReviewTimesCannotBeSilentlyPersisted()
    {
        var saved = await SeedAsync(); var state = State(saved); state.EnrollmentAt = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified);
        await using var context = _fixture.CreateContext(); context.Add(state);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        await using var verification = _fixture.CreateContext();
        Assert.False(await verification.UserWordReviewStates.AnyAsync(s => s.UserWordId == saved.Id));
    }

    [Fact]
    public async Task ExistingSavedWordIdentityIsStillUserAndCanonicalWord()
    {
        var saved = await SeedAsync();
        await using var context = _fixture.CreateContext();
        context.Add(new UserWord { UserId = saved.UserId, WordId = saved.WordId, PartOfSpeechId = saved.PartOfSpeechId, AddedAt = Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private async Task<UserWord> SeedAsync(int? owner = null)
    {
        await using var context = _fixture.CreateContext();
        var user = owner.HasValue ? await context.Users.FindAsync(owner.Value) : new User
        {
            Username = $"f6-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test", PasswordHash = "test-only", CreatedAt = Now
        };
        var word = new Word { Text = $"word-{Guid.NewGuid():N}", CreatedAt = Now };
        var definition = new WordDefinition { Word = word, PartOfSpeechId = 1, Definition = "A distinct meaning", CreatedAt = Now };
        var saved = new UserWord
        {
            User = user!, Word = word, PartOfSpeechId = 1, PreferredWordDefinition = definition,
            AddedAt = Now, PersonalNotes = "preserve notes", IsFavorite = true, TotalAttempts = 5, CorrectAnswers = 3, LastReviewedAt = Now.AddDays(-1)
        };
        context.Add(saved); await context.SaveChangesAsync(); return saved;
    }

    private static UserWordReviewState State(UserWord saved) => new()
    {
        UserWordId = saved.Id, UserId = saved.UserId, EnrollmentAt = Now, NextReviewAt = Now, ScheduleVersion = 1, AlgorithmVersion = "interval-v1"
    };

    private static ReviewSession Session(int userId) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, CreatedAt = Now, ExpiresAt = Now.AddMinutes(30), RequestedQuestionCount = 2, ItemCount = 2, Version = 1
    };

    private async Task<ReviewSession> PersistSessionAsync(int userId)
    {
        var session = Session(userId); await PersistAsync(session); return session;
    }

    private static ReviewSessionItem Item(ReviewSession session, UserWord saved) => new()
    {
        Id = Guid.NewGuid(), SessionId = session.Id, UserId = session.UserId, UserWordId = saved.Id,
        CapturedScheduleVersion = 1, WordDefinitionId = saved.PreferredWordDefinitionId!.Value, DefinitionRevision = 1,
        WordText = saved.Word.Text, DefinitionText = "A distinct meaning", PartOfSpeech = "Noun", Prompt = "Choose a meaning", OptionsJson = "[]", CorrectOptionId = 0
    };

    private static void Accept(ReviewSessionItem item, Guid submission)
    {
        item.AcceptedSubmissionId = submission; item.AcceptedAt = Now; item.SelectedOptionId = 0;
        item.IsCorrect = true; item.ReceiptJson = "{\"success\":true,\"data\":{\"example\":\"immutable snapshot\"}}"; item.ReceiptContractVersion = 1;
    }

    private static ReviewHistory History(ReviewSessionItem item) => new()
    {
        Id = Guid.NewGuid(), UserId = item.UserId, UserWordId = item.UserWordId, SessionItemId = item.Id,
        SubmissionId = item.AcceptedSubmissionId!.Value, ReviewedAt = Now, WordDefinitionId = item.WordDefinitionId,
        DefinitionRevision = 1, WordText = item.WordText, DefinitionText = item.DefinitionText,
        IsCorrect = true, SelectedOptionId = 0, SelectedAnswer = "A distinct meaning", CorrectAnswer = "A distinct meaning",
        PreviousScheduleVersion = 1, NextScheduleVersion = 2, PreviousNextReviewAt = Now, NextReviewAt = Now.AddDays(1),
        PreviousStatus = 0, NextStatus = 1, PreviousCorrectCount = 0, NextCorrectCount = 1, NextIntervalDays = 1,
        ReceiptJson = item.ReceiptJson!, ReceiptContractVersion = 1
    };

    private async Task PersistAsync(object entity)
    {
        await using var context = _fixture.CreateContext(); context.Add(entity); await context.SaveChangesAsync();
    }

    private async Task AssertRejectedAsync(object entity)
    {
        await using var context = _fixture.CreateContext(); context.Add(entity);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
