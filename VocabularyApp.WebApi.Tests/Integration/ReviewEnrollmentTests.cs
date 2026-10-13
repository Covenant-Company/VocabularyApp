using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ReviewEnrollmentTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task IndependentFlagsAndDuplicateVocabularyAdd(bool enabled, bool enrollment)
    {
        using var host = new ReviewTestHost(enabled, enrollment); await host.SeedAsync(1);
        using var response = await host.Client.PostAsJsonAsync("/api/words/vocabulary/add", new { word = "review-word-0", preferredWordDefinitionId = int.MaxValue });
        var result = await ApiTestClientHelper.ReadApiResultAsync<AddToVocabularyResultDto>(response);
        Assert.True(result.Success); Assert.True(result.Data!.AlreadyExisted); Assert.Equal(host.Words[0].SavedId, result.Data.UserWordId);
        await host.WithDbAsync(async db =>
        {
            Assert.Equal(enrollment ? 1 : 0, await db.UserWordReviewStates.CountAsync()); Assert.False(await db.ReviewHistories.AnyAsync());
            Assert.Equal(0, (await db.UserWords.SingleAsync()).TotalAttempts);
        });
    }

    [Fact]
    public async Task EnrollmentWriteFailureRollsVocabularyBack()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(0);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(host.Factory, "failed-add", "failed meaning");
        host.Failure.FailAfterTable = "UserWordReviewStates";
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/words/vocabulary/add", new { word = "failed-add", preferredWordDefinitionId = word.WordDefinitionId }), 500, "internal_error");
        await host.WithDbAsync(async db => { Assert.False(await db.UserWords.AnyAsync()); Assert.False(await db.UserWordReviewStates.AnyAsync()); });
    }

    [Fact]
    public async Task SameEffectiveFallbackIsNoOpAndRealPreferenceResetPreservesHistoryLapsesAndQuiz()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var start = await host.StartAsync();
        await host.SubmitAsync(host.Answer(start, correct: false));
        var saved = host.Words[0]; var alternative = await IntegrationTestSeeder.SeedDefinitionAsync(host.Factory, saved.Word.WordId, "Different selected meaning");
        await host.WithDbAsync(async db =>
        {
            var word = await db.UserWords.SingleAsync(w => w.Id == saved.SavedId); word.PreferredWordDefinitionId = null;
            word.PersonalNotes = "preserve"; word.TotalAttempts = 7; word.CorrectAnswers = 5; word.IsFavorite = true;
            await db.SaveChangesAsync();
        });
        using var same = await host.Client.PutAsJsonAsync($"/api/words/vocabulary/{saved.SavedId}/preferred-definition", new { preferredWordDefinitionId = saved.Word.WordDefinitionId }); same.EnsureSuccessStatusCode();
        await host.WithDbAsync(async db => Assert.Equal(2, (await db.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.SavedId)).ScheduleVersion));
        using var changed = await host.Client.PutAsJsonAsync($"/api/words/vocabulary/{saved.SavedId}/preferred-definition", new { preferredWordDefinitionId = alternative }); changed.EnsureSuccessStatusCode();
        await host.WithDbAsync(async db =>
        {
            var state = await db.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.SavedId);
            Assert.Equal(3, state.ScheduleVersion); Assert.Equal(0, state.ReviewStatus); Assert.Equal(1, state.LapseCount); Assert.Null(state.LastReviewedAt);
            Assert.Equal(host.Clock.Now.UtcDateTime, state.NextReviewAt); Assert.Equal(alternative, state.ScheduledWordDefinitionId);
            Assert.Equal(1, await db.ReviewHistories.CountAsync()); var word = await db.UserWords.SingleAsync(w => w.Id == saved.SavedId);
            Assert.Equal(7, word.TotalAttempts); Assert.Equal(5, word.CorrectAnswers); Assert.True(word.IsFavorite); Assert.Equal("preserve", word.PersonalNotes);
        });
    }

    [Fact]
    public async Task PreferenceResetFailureKeepsOriginalMeaningAndSchedule()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(1); var saved = host.Words[0];
        var definition = await IntegrationTestSeeder.SeedDefinitionAsync(host.Factory, saved.Word.WordId, "Other meaning");
        host.Failure.FailAfterTable = "UserWordReviewStates";
        await ReviewTestHost.AssertErrorAsync(await host.Client.PutAsJsonAsync($"/api/words/vocabulary/{saved.SavedId}/preferred-definition", new { preferredWordDefinitionId = definition }), 500, "internal_error");
        await host.WithDbAsync(async db =>
        {
            Assert.Equal(saved.Word.WordDefinitionId, (await db.UserWords.SingleAsync()).PreferredWordDefinitionId);
            Assert.Equal(1, (await db.UserWordReviewStates.SingleAsync()).ScheduleVersion);
        });
    }
}
