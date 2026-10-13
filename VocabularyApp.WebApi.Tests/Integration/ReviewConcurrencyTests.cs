using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VocabularyApp.Data;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ReviewConcurrencyTests
{
    [Fact]
    public async Task ConcurrentVocabularyAddsCreateExactlyOneStateAndReturnOriginalAcknowledgements()
    {
        await using var database = new IndependentConnectionDatabaseFixture(); await database.InitializeAsync();
        var synchronization = new ReviewSynchronizationInterceptor("UserWords");
        using var host = new ReviewTestHost(configureDatabase: services =>
        {
            services.RemoveAll<ApplicationDbContext>(); services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(database.ConnectionString).AddInterceptors(synchronization));
        });
        await host.SeedAsync(0); var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(host.Factory, "concurrent-add", "one meaning");
        synchronization.Armed = true;
        var payload = new { word = "concurrent-add", preferredWordDefinitionId = word.WordDefinitionId };
        var responses = await Task.WhenAll(Task.Run(() => host.Client.PostAsJsonAsync("/api/words/vocabulary/add", payload)), Task.Run(() => host.Client.PostAsJsonAsync("/api/words/vocabulary/add", payload)));
        synchronization.Armed = false;
        var results = new List<AddToVocabularyResultDto>();
        foreach (var response in responses)
        {
            using (response) { var result = await ApiTestClientHelper.ReadApiResultAsync<AddToVocabularyResultDto>(response); Assert.True(result.Success, result.RawContent); results.Add(result.Data!); }
        }
        Assert.Equal(results[0].UserWordId, results[1].UserWordId); Assert.Single(results, r => r.AlreadyExisted);
        await using var read = database.CreateContext(); Assert.Equal(1, await read.UserWordReviewStates.CountAsync()); Assert.Equal(1, await read.UserWords.CountAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentPreferenceOrContentWriterStalesUnacceptedRequest(bool content)
    {
        await using var database = new IndependentConnectionDatabaseFixture(); await database.InitializeAsync();
        var pause = new ReviewRequestPauseInterceptor();
        using var host = new ReviewTestHost(configureDatabase: services =>
        {
            services.RemoveAll<ApplicationDbContext>(); services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(database.ConnectionString).AddInterceptors(pause));
        });
        await host.SeedAsync(); var session = await host.StartAsync(); var saved = host.Words[0]; var request = host.Answer(session);
        var alternative = await IntegrationTestSeeder.SeedDefinitionAsync(host.Factory, saved.Word.WordId, "changed selection");
        pause.Armed = true; var submission = Task.Run(() => host.Client.PostAsJsonAsync("/api/reviews/submit", request));
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            if (content)
            {
                using var scope = host.Factory.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReviewContentChangeService>().UpdateDefinitionAsync(saved.Word.WordDefinitionId, "Changed content", saved.Word.PartOfSpeechId, saved.Word.WordId, null, 1);
            }
            else
            {
                using var response = await host.Client.PutAsJsonAsync($"/api/words/vocabulary/{saved.SavedId}/preferred-definition", new { preferredWordDefinitionId = alternative }); response.EnsureSuccessStatusCode();
            }
        }
        finally { pause.Release(); }
        await ReviewTestHost.AssertErrorAsync(await submission, 409, "review_state_changed");
        await using var read = database.CreateContext(); Assert.False(await read.ReviewHistories.AnyAsync());
        Assert.Equal(2, (await read.UserWordReviewStates.SingleAsync(s => s.UserWordId == saved.SavedId)).ScheduleVersion);
    }

    [Theory]
    [InlineData("identical")] [InlineData("different-key")] [InlineData("different-payload")]
    [InlineData("different-session")] [InlineData("independent-items")]
    public async Task SimultaneousSubmissionsUseIndependentConnections(string scenario)
    {
        await using var database = new IndependentConnectionDatabaseFixture(); await database.InitializeAsync();
        var synchronization = new ReviewSynchronizationInterceptor();
        using var host = new ReviewTestHost(configureDatabase: services =>
        {
            services.RemoveAll<ApplicationDbContext>(); services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(database.ConnectionString).AddInterceptors(synchronization));
        });
        await host.SeedAsync(); var session = await host.StartAsync(2); var first = host.Answer(session);
        var second = host.Answer(session, scenario == "independent-items" ? 1 : 0);
        if (scenario == "identical" || scenario == "different-payload") second.SubmissionId = first.SubmissionId;
        if (scenario == "different-payload") second.SelectedOptionId = (first.SelectedOptionId + 1) % 4;
        if (scenario == "different-session") second = host.Answer(await host.StartAsync());
        synchronization.Armed = true;
        var calls = await Task.WhenAll(Task.Run(() => host.Client.PostAsJsonAsync("/api/reviews/submit", first)), Task.Run(() => host.Client.PostAsJsonAsync("/api/reviews/submit", second)));
        synchronization.Armed = false;
        var statuses = calls.Select(r => (int)r.StatusCode).Order().ToArray();
        Assert.Equal(scenario is "identical" or "independent-items" ? new[] { 200, 200 } : new[] { 200, 409 }, statuses);
        foreach (var response in calls) response.Dispose();
        await using var read = database.CreateContext();
        Assert.Equal(scenario == "independent-items" ? 2 : 1, await read.ReviewHistories.CountAsync());
        var current = await read.ReviewSessions.SingleAsync(s => s.Id == session.SessionId);
        if (scenario != "different-session") Assert.Equal(scenario == "independent-items" ? 3 : 2, current.Version);
        Assert.Equal(scenario == "independent-items" ? 2 : 1, await read.UserWordReviewStates.CountAsync(s => s.ScheduleVersion == 2));
    }

    [Theory]
    [InlineData(2, 200)] [InlineData(3, 500)]
    public async Task BusyRetriesAreBoundedAndPreserveLogicalSubmission(int failures, int status)
    {
        await using var database = new IndependentConnectionDatabaseFixture(); await database.InitializeAsync();
        var busy = new ReviewBusyInterceptor();
        using var host = new ReviewTestHost(configureDatabase: services =>
        {
            services.RemoveAll<ApplicationDbContext>(); services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(database.ConnectionString).AddInterceptors(busy));
        });
        await host.SeedAsync(); var start = await host.StartAsync(); var request = host.Answer(start); busy.FailuresRemaining = failures;
        using var response = await host.Client.PostAsJsonAsync("/api/reviews/submit", request); Assert.Equal(status, (int)response.StatusCode); Assert.Equal(failures, busy.Attempts);
        await using var read = database.CreateContext(); Assert.Equal(status == 200 ? 1 : 0, await read.ReviewHistories.CountAsync());
        await host.SubmitAsync(request);
        Assert.Equal(1, await read.ReviewHistories.CountAsync());
    }
}
