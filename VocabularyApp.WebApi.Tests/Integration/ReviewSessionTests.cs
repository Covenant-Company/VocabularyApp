using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ReviewSessionTests
{
    [Fact]
    public async Task ResumeReplayCompletionAndExpiredReplayPreserveExactReceipts()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var start = await host.StartAsync(2);
        var first = host.Answer(start); var receipt = await host.SubmitAsync(first);
        Assert.Equal(1, receipt.ReviewIntervalDays); Assert.Equal(host.Clock.Now.UtcDateTime.AddDays(1), receipt.NextReviewAt);
        var resumed = await host.StartAsync(20, start.SessionId);
        Assert.Equal(start.ExpiresAtUtc, resumed.ExpiresAtUtc); Assert.Equal(start.Items[1].ItemId, resumed.Items[1].ItemId);
        Assert.Equal(receipt.SubmissionId, resumed.Items[0].AcceptedReceipt!.SubmissionId);
        Assert.Null(resumed.Items[1].AcceptedReceipt);
        var second = await host.SubmitAsync(host.Answer(start, 1, false)); Assert.True(second.SessionSummary.IsComplete);
        Assert.Equal(1, second.SessionSummary.CorrectCount); Assert.Equal(1, second.SessionSummary.IncorrectCount);
        var replay = await host.SubmitAsync(first); Assert.Equal(1, replay.SessionSummary.AcceptedCount);
        host.Clock.Now = host.Clock.Now.AddMinutes(30);
        var expiredReplay = await host.SubmitAsync(first);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(receipt), System.Text.Json.JsonSerializer.Serialize(expiredReplay));
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = start.SessionId }), 409, "review_session_expired");
        await host.WithDbAsync(async db => { Assert.Equal(2, await db.ReviewHistories.CountAsync()); Assert.Equal(0, await db.QuizResults.CountAsync()); });
    }

    [Fact]
    public async Task ZeroOptionIsAcceptedAndConflictingKeysOrPayloadsAreRejected()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var start = await host.StartAsync(2);
        var request = host.Answer(start); request.SelectedOptionId = 0;
        var receipt = await host.SubmitAsync(request); Assert.Equal(0, receipt.SelectedOptionId);
        var secondKey = host.Answer(start);
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", secondKey), 409, "review_submission_conflict");
        request.SelectedOptionId = 1;
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", request), 409, "review_submission_conflict");
        var reuse = host.Answer(start, 1); reuse.SubmissionId = request.SubmissionId;
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", reuse), 409, "review_submission_conflict");
    }

    [Fact]
    public async Task AbandonmentExpiryAtEqualityAndCleanupDoNotCreateOutcomes()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var abandoned = await host.StartAsync(2); var request = host.Answer(abandoned);
        host.Clock.Now = host.Clock.Now.AddMinutes(30);
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", request), 409, "review_session_expired");
        Assert.Equal(4, (await host.GetAsync<ReviewSummaryResponseDto>("/api/reviews/summary")).DueCount);
        await host.WithDbAsync(async db => { Assert.Equal(1, await db.ReviewSessions.CountAsync()); Assert.False(await db.ReviewHistories.AnyAsync()); });
        await host.StartAsync();
        await host.WithDbAsync(async db => { Assert.Equal(1, await db.ReviewSessions.CountAsync()); Assert.False(await db.ReviewHistories.AnyAsync()); Assert.All(await db.UserWordReviewStates.ToListAsync(), s => Assert.Equal(1, s.ScheduleVersion)); });
    }

    [Fact]
    public async Task AnotherSessionAcceptanceStalesPendingTarget()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var first = await host.StartAsync(); var other = await host.StartAsync();
        await host.SubmitAsync(host.Answer(first));
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", host.Answer(other)), 409, "review_state_changed");
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = other.SessionId }), 409, "review_state_changed");
    }

    [Theory]
    [InlineData("UserWordReviewStates")] [InlineData("ReviewHistories")] [InlineData("ReviewSessionItems")]
    public async Task FailureAfterActualWriteRollsBackAllFourRecords(string table)
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var start = await host.StartAsync(); var request = host.Answer(start);
        host.Failure.FailAfterTable = table;
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", request), 500, "internal_error");
        await host.WithDbAsync(async db =>
        {
            Assert.False(await db.ReviewHistories.AnyAsync()); Assert.All(await db.UserWordReviewStates.ToListAsync(), s => Assert.Equal(1, s.ScheduleVersion));
            Assert.Null((await db.ReviewSessionItems.SingleAsync()).ReceiptJson); Assert.Equal(1, (await db.ReviewSessions.SingleAsync()).Version);
        });
        Assert.True((await host.SubmitAsync(request)).IsCorrect);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BeforeCommitRollbackAndUnknownCommittedOutcomeRecovery(bool afterCommit)
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var start = await host.StartAsync(); var request = host.Answer(start);
        host.CommitFailure.BeforeCommit = !afterCommit; host.CommitFailure.AfterCommit = afterCommit;
        if (afterCommit) Assert.True((await host.SubmitAsync(request)).IsCorrect);
        else
        {
            await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", request), 500, "internal_error");
            await host.WithDbAsync(async db => Assert.False(await db.ReviewHistories.AnyAsync()));
            await host.SubmitAsync(request);
        }
        await host.WithDbAsync(async db => { Assert.Equal(1, await db.ReviewHistories.CountAsync()); Assert.Equal(2, (await db.UserWordReviewStates.SingleAsync(s => s.UserWordId == start.Items[0].UserWordId)).ScheduleVersion); });
    }

    [Fact]
    public async Task SevenCorrectAnswersCapAtNinetyAndFailurePreservesLifetimeLapses()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync();
        await host.WithDbAsync(async db =>
        {
            foreach (var state in await db.UserWordReviewStates.Where(s => s.UserWordId != host.Words[0].SavedId).ToListAsync()) state.NextReviewAt = host.Clock.Now.UtcDateTime.AddYears(2);
            await db.SaveChangesAsync();
        });
        for (var i = 0; i < 8; i++)
        {
            var start = await host.StartAsync(); var result = await host.SubmitAsync(host.Answer(start));
            Assert.Equal(new[] { 1, 3, 7, 14, 30, 60, 90, 90 }[i], result.ReviewIntervalDays);
            Assert.Equal(i < 2 ? "learning" : "reviewing", result.ReviewStatus);
            host.Clock.Now = new DateTimeOffset(result.NextReviewAt);
        }
        var failure = await host.StartAsync(); var failed = await host.SubmitAsync(host.Answer(failure, correct: false));
        Assert.Equal(1, failed.ReviewIntervalDays); Assert.Equal("learning", failed.ReviewStatus);
        await host.WithDbAsync(async db => { var state = await db.UserWordReviewStates.SingleAsync(s => s.UserWordId == host.Words[0].SavedId); Assert.Equal(1, state.LapseCount); Assert.Equal(0, state.ConsecutiveCorrectCount); });
    }
}
