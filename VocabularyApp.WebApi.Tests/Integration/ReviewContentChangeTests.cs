using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ReviewContentChangeTests
{
    [Fact]
    public async Task ControlledMaintenanceFailsClosedWhenCoordinationIsDisabled()
    {
        using var host = new ReviewTestHost(enabled: false, enrollment: false); await host.SeedAsync(1); var saved = host.Words[0];
        using var scope = host.Factory.Services.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ReviewContentChangeService>()
            .UpdateDefinitionAsync(saved.Word.WordDefinitionId, "Blocked edit", saved.Word.PartOfSpeechId, saved.Word.WordId, null, 1));
        await host.WithDbAsync(async db => Assert.Equal("Meaning number 0", (await db.WordDefinitions.SingleAsync()).Definition));
    }
    [Fact]
    public async Task CosmeticAndExampleEditsPreserveScheduleGenuineEditResetsAndOldReceiptReplays()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var session = await host.StartAsync();
        var request = host.Answer(session); var receipt = await host.SubmitAsync(request); var saved = host.Words[0];
        async Task Edit(string text, string? example)
        {
            using var scope = host.Factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ReviewContentChangeService>().UpdateDefinitionAsync(saved.Word.WordDefinitionId, text, saved.Word.PartOfSpeechId, saved.Word.WordId, example, 1);
        }
        await Edit("  Meaning   number 0\t", "example change");
        await host.WithDbAsync(async db => { Assert.Equal(1, (await db.WordDefinitions.SingleAsync(d => d.Id == saved.Word.WordDefinitionId)).ContentRevision); Assert.Equal(2, (await db.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.SavedId)).ScheduleVersion); });
        await Edit("Meaning number 0!", null);
        await host.WithDbAsync(async db =>
        {
            var state = await db.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.SavedId);
            Assert.Equal(3, state.ScheduleVersion); Assert.Equal(2, state.ScheduledDefinitionRevision); Assert.Equal(0, state.ReviewStatus); Assert.Equal(1, await db.ReviewHistories.CountAsync());
        });
        Assert.Equal(receipt.ReviewedAtUtc, (await host.SubmitAsync(request)).ReviewedAtUtc);
    }

    [Fact]
    public async Task DistractorContentChangeStalesQuestionWithoutResettingTarget()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var session = await host.StartAsync(); var saved = host.Words[1];
        using (var scope = host.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReviewContentChangeService>().UpdateDefinitionAsync(saved.Word.WordDefinitionId, "changed distractor", saved.Word.PartOfSpeechId, saved.Word.WordId, null, 1);
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", host.Answer(session)), 409, "review_state_changed");
        await host.WithDbAsync(async db => Assert.Equal(1, (await db.UserWordReviewStates.SingleAsync(s => s.UserWordId == host.Words[0].SavedId)).ScheduleVersion));
    }

    [Fact]
    public async Task OutOfBandSameRevisionTextEditIsDetected()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var session = await host.StartAsync();
        await host.WithDbAsync(async db => { var d = await db.WordDefinitions.SingleAsync(d => d.Id == host.Words[0].Word.WordDefinitionId); d.Definition = "uncoordinated edit"; await db.SaveChangesAsync(); });
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = session.SessionId }), 409, "review_state_changed");
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", host.Answer(session)), 409, "review_state_changed");
    }

    [Fact]
    public async Task FallbackReorderAdditionAndRemovalCoordinateSelectedMeaning()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(1); var saved = host.Words[0];
        await host.WithDbAsync(async db => { (await db.UserWords.SingleAsync()).PreferredWordDefinitionId = null; await db.SaveChangesAsync(); });
        using (var scope = host.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReviewContentChangeService>().AddDefinitionAsync(saved.Word.WordId, saved.Word.PartOfSpeechId, "Earlier fallback", 0);
        int selected = 0;
        await host.WithDbAsync(async db => { var state = await db.UserWordReviewStates.SingleAsync(); selected = state.ScheduledWordDefinitionId!.Value; Assert.NotEqual(saved.Word.WordDefinitionId, selected); Assert.Equal(2, state.ScheduleVersion); });
        using (var scope = host.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReviewContentChangeService>().UpdateDefinitionAsync(selected, "Earlier fallback", saved.Word.PartOfSpeechId, saved.Word.WordId, null, 3);
        await host.WithDbAsync(async db => { var state = await db.UserWordReviewStates.SingleAsync(); Assert.Equal(saved.Word.WordDefinitionId, state.ScheduledWordDefinitionId); Assert.Equal(3, state.ScheduleVersion); });
        using (var scope = host.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReviewContentChangeService>().RemoveDefinitionAsync(saved.Word.WordDefinitionId);
        await host.WithDbAsync(async db => { var state = await db.UserWordReviewStates.SingleAsync(); Assert.Equal(selected, state.ScheduledWordDefinitionId); Assert.Equal(4, state.ScheduleVersion); });
    }
}
