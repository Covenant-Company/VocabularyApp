using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

[Collection(QuizApiCollection.Name)]
public sealed class ReviewPhase5AcceptanceTests
{
    [Fact]
    public async Task AuthenticatedLifecycleEnrollsGradesReplaysResumesAndRestartsAfterLapseWithoutQuizContamination()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync();
        var initial = await host.GetAsync<ReviewSummaryResponseDto>("/api/reviews/summary");
        Assert.Equal(4, initial.TotalEnrolledCount); Assert.Equal(4, initial.NewCount); Assert.Equal(4, initial.DueCount); Assert.Equal(0, initial.OverdueCount);
        var due = await host.GetAsync<ReviewDueResponseDto>("/api/reviews/due"); Assert.Equal(host.Words.Select(w => w.SavedId), due.Items.Select(i => i.UserWordId));
        var start = await host.StartAsync(2);
        Assert.All(start.Items, item => { Assert.Equal(4, item.Options.Select(o => o.Text).Distinct().Count()); Assert.Null(item.AcceptedReceipt); });
        var first = host.Answer(start); var correct = await host.SubmitAsync(first); Assert.True(correct.IsCorrect);
        Assert.Equal(host.Clock.Now.UtcDateTime.AddDays(1), correct.NextReviewAt); Assert.Equal(1, correct.SessionSummary.AcceptedCount);
        host.Clock.Now = host.Clock.Now.AddSeconds(1);
        var replay = await host.SubmitAsync(first); Assert.Equal(JsonSerializer.Serialize(correct), JsonSerializer.Serialize(replay));
        var resumed = await host.StartAsync(resume: start.SessionId);
        Assert.Equal(start.Items.Select(i => JsonSerializer.Serialize(i.Options)), resumed.Items.Select(i => JsonSerializer.Serialize(i.Options)));
        Assert.Equal(correct.SubmissionId, resumed.Items[0].AcceptedReceipt!.SubmissionId);
        var incorrect = await host.SubmitAsync(host.Answer(start, 1, false));
        Assert.False(incorrect.IsCorrect); Assert.Equal(1, incorrect.ReviewIntervalDays); Assert.Equal("learning", incorrect.ReviewStatus);
        Assert.True(incorrect.SessionSummary.IsComplete); Assert.Equal(1, incorrect.SessionSummary.CorrectCount); Assert.Equal(1, incorrect.SessionSummary.IncorrectCount);
        await host.WithDbAsync(async db =>
        {
            Assert.Equal(2, await db.ReviewHistories.CountAsync());
            var state = await db.UserWordReviewStates.SingleAsync(s => s.UserWordId == start.Items[1].UserWordId);
            Assert.Equal(1, state.LapseCount); Assert.Equal(0, state.ConsecutiveCorrectCount); Assert.Equal(2, state.ScheduleVersion);
            foreach (var history in await db.ReviewHistories.Include(h => h.SessionItem).ToListAsync()) Assert.Equal(history.ReceiptJson, history.SessionItem.ReceiptJson);
            Assert.All(await db.UserWords.ToListAsync(), w => Assert.Equal(0, w.TotalAttempts)); Assert.False(await db.QuizResults.AnyAsync());
        });
        Assert.Equal(2, (await host.GetAsync<ReviewSummaryResponseDto>("/api/reviews/summary")).DueCount);
        host.Clock.Now = new DateTimeOffset(correct.NextReviewAt.AddTicks(-1));
        Assert.Equal(2, (await host.GetAsync<ReviewSummaryResponseDto>("/api/reviews/summary")).DueCount);
        host.Clock.Now = host.Clock.Now.AddTicks(1);
        Assert.Equal(3, (await host.GetAsync<ReviewSummaryResponseDto>("/api/reviews/summary")).DueCount);
        host.Clock.Now = new DateTimeOffset(incorrect.NextReviewAt);
        var nextSession = await host.StartAsync(20); Assert.Equal(4, nextSession.Items.Count);
        var lapsedIndex = nextSession.Items.FindIndex(i => i.UserWordId == start.Items[1].UserWordId);
        var afterLapse = await host.SubmitAsync(host.Answer(nextSession, lapsedIndex));
        Assert.Equal(1, afterLapse.ReviewIntervalDays); Assert.True(afterLapse.IsCorrect);
        foreach (var index in Enumerable.Range(0, nextSession.Items.Count).Where(i => i != lapsedIndex)) await host.SubmitAsync(host.Answer(nextSession, index));
        var completed = await host.StartAsync(resume: nextSession.SessionId); Assert.True(completed.SessionSummary!.IsComplete);
        Assert.All(completed.Items, i => Assert.NotNull(i.AcceptedReceipt));
        string? schedules = null;
        await host.WithDbAsync(async db =>
        {
            Assert.Equal(6, await db.ReviewHistories.CountAsync());
            Assert.Equal(1, (await db.UserWordReviewStates.SingleAsync(s => s.UserWordId == start.Items[1].UserWordId)).LapseCount);
            schedules = JsonSerializer.Serialize(await db.UserWordReviewStates.AsNoTracking().OrderBy(s => s.UserWordId).ToListAsync());
        });
        using var quizStartResponse = await host.Client.PostAsJsonAsync("/api/quiz/start", new { questionCount = 1, mode = "word-to-definition" });
        var quiz = await ApiTestClientHelper.ReadApiResultAsync<QuizStartResponseDto>(quizStartResponse); Assert.True(quiz.Success, quiz.RawContent);
        using var quizSubmit = await host.Client.PostAsJsonAsync("/api/quiz/submit", new { sessionId = quiz.Data!.SessionId,
            answers = new[] { new { questionId = quiz.Data.Questions[0].QuestionId, selectedOptionId = 0 } } }); quizSubmit.EnsureSuccessStatusCode();
        await host.WithDbAsync(async db =>
        {
            Assert.Equal(schedules, JsonSerializer.Serialize(await db.UserWordReviewStates.AsNoTracking().OrderBy(s => s.UserWordId).ToListAsync()));
            Assert.Equal(6, await db.ReviewHistories.CountAsync()); Assert.Equal(1, await db.QuizResults.CountAsync()); Assert.Equal(1, await db.UserWords.SumAsync(w => w.TotalAttempts));
        });
        Assert.Empty(host.Factory.DictionaryHandler.Requests);
    }

    [Theory]
    [InlineData("due", false, "invalid")] [InlineData("summary", false, "invalid")] [InlineData("start", true, "invalid")] [InlineData("submit", true, "invalid")]
    [InlineData("due", false, "expired")] [InlineData("summary", false, "expired")] [InlineData("start", true, "expired")] [InlineData("submit", true, "expired")]
    public async Task InvalidOrExpiredJwtIsRejectedByAllReviewRoutes(string route, bool post, string kind)
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(0);
        using var client = host.Factory.CreateClient();
        var settings = TestJwtSettingsFactory.Create();
        var expired = new JwtSecurityToken(settings.Issuer, settings.Audience, [new Claim(ClaimTypes.NameIdentifier, host.Owner.User.User.Id.ToString())],
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kind == "invalid" ? "invalid.jwt.value" : new JwtSecurityTokenHandler().WriteToken(expired));
        var request = route == "submit" ? (object)new ReviewSubmitRequestDto { SessionId = Guid.NewGuid(), ItemId = Guid.NewGuid(), SubmissionId = Guid.NewGuid(), SelectedOptionId = 0 } : new ReviewStartRequestDto();
        using var response = post ? await client.PostAsJsonAsync("/api/reviews/" + route, request) : await client.GetAsync("/api/reviews/" + route);
        Assert.Equal(401, (int)response.StatusCode); Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SubmissionKeysAreOwnerScopedAndNeverLeakAnotherOwnersReceipt()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var first = await host.StartAsync(); var request = host.Answer(first); await host.SubmitAsync(request);
        using var other = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(host.Factory);
        foreach (var word in host.Words)
        {
            using var add = await other.Client.PostAsJsonAsync("/api/words/vocabulary/add", new { word = "review-word-" + host.Words.IndexOf(word), preferredWordDefinitionId = word.Word.WordDefinitionId }); add.EnsureSuccessStatusCode();
        }
        using var startResponse = await other.Client.PostAsJsonAsync("/api/reviews/start", new { questionCount = 1 });
        var start = await ApiTestClientHelper.ReadApiResultAsync<ReviewStartResponseDto>(startResponse); Assert.True(start.Success);
        var independent = new ReviewSubmitRequestDto { SessionId = start.Data!.SessionId!.Value, ItemId = start.Data.Items[0].ItemId, SubmissionId = request.SubmissionId, SelectedOptionId = 0 };
        using var submitted = await other.Client.PostAsJsonAsync("/api/reviews/submit", independent); var receipt = await ApiTestClientHelper.ReadApiResultAsync<ReviewSubmitResponseDto>(submitted);
        Assert.True(receipt.Success, receipt.RawContent); Assert.Equal(independent.SessionId, receipt.Data!.SessionId);
        await ReviewTestHost.AssertErrorAsync(await other.Client.PostAsJsonAsync("/api/reviews/submit", request), 409, "review_submission_conflict");
        var foreignKey = host.Answer(first); // New key + nonowned session conceals existence with 404.
        await ReviewTestHost.AssertErrorAsync(await other.Client.PostAsJsonAsync("/api/reviews/submit", foreignKey), 404, "review_session_unavailable");
        await host.WithDbAsync(async db => Assert.Equal(2, await db.ReviewHistories.CountAsync(h => h.SubmissionId == request.SubmissionId)));
    }

    [Fact]
    public async Task RuntimeDisablePreservesReviewDataAndLeavesExistingApisOperational()
    {
        var monitor = new MutableReviewOptionsMonitor();
        using var host = new ReviewTestHost(configureDatabase: services => { services.RemoveAll<IOptionsMonitor<ReviewOptions>>(); services.AddSingleton<IOptionsMonitor<ReviewOptions>>(monitor); });
        await host.SeedAsync(); var start = await host.StartAsync(2); var answer = host.Answer(start); var receipt = await host.SubmitAsync(answer);
        monitor.CurrentValue.Enabled = false; monitor.CurrentValue.EnrollmentEnabled = false;
        await ReviewTestHost.AssertErrorAsync(await host.Client.GetAsync("/api/reviews/summary"), 503, "reviews_disabled");
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/submit", answer), 503, "reviews_disabled");
        foreach (var path in new[] { "/api/users/profile", "/api/words/vocabulary", "/api/words/lookup/review-word-0", "/api/quiz/history" })
        { using var response = await host.Client.GetAsync(path); response.EnsureSuccessStatusCode(); }
        await host.AddWordAsync("disabled-enrollment", "Not automatically enrolled");
        await host.WithDbAsync(async db => { Assert.Equal(4, await db.UserWordReviewStates.CountAsync()); Assert.Equal(1, await db.ReviewHistories.CountAsync()); });
        monitor.CurrentValue.Enabled = true; monitor.CurrentValue.EnrollmentEnabled = true;
        Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(await host.SubmitAsync(answer)));
        var resume = await host.StartAsync(resume: start.SessionId); Assert.Equal(receipt.SubmissionId, resume.Items[0].AcceptedReceipt!.SubmissionId);
        Assert.Equal(4, (await host.GetAsync<ReviewSummaryResponseDto>("/api/reviews/summary")).TotalEnrolledCount);
        Assert.Empty(host.Factory.DictionaryHandler.Requests);
    }

    [Fact]
    public async Task CompleteStaticBacklogUsesBoundedPagesInOrderAndHonorsDefaultAndMaximumTake()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(0);
        for (var i = 0; i < 103; i++) { host.Clock.Now = host.Clock.Now.AddTicks(1); await host.AddWordAsync($"page-{i:D3}", $"Unique page meaning {i}"); }
        var summary = await host.GetAsync<ReviewSummaryResponseDto>("/api/reviews/summary"); Assert.Equal(103, summary.DueCount); Assert.Equal(102, summary.OverdueCount);
        var first = await host.GetAsync<ReviewDueResponseDto>("/api/reviews/due"); Assert.Equal(20, first.Items.Count);
        var maximum = await host.GetAsync<ReviewDueResponseDto>("/api/reviews/due?take=100"); Assert.Equal(100, maximum.Items.Count); Assert.NotNull(maximum.NextCursor);
        var all = first.Items.ToList(); var page = first;
        while (page.NextCursor != null)
        {
            page = await host.GetAsync<ReviewDueResponseDto>("/api/reviews/due?take=20&cursor=" + Uri.EscapeDataString(page.NextCursor));
            Assert.Equal(first.AsOfUtc, page.AsOfUtc); all.AddRange(page.Items);
        }
        Assert.Equal(host.Words.Select(w => w.SavedId), all.Select(i => i.UserWordId)); Assert.Equal(103, all.Select(i => i.UserWordId).Distinct().Count());
        await ReviewTestHost.AssertErrorAsync(await host.Client.GetAsync("/api/reviews/due?take=101"), 400, "validation_failed");
    }

    [Fact]
    public async Task RestartWithIsolatedPersistedKeysPreservesCursorOptionsReceiptsAndPostExpiryReplay()
    {
        await using var database = new IndependentConnectionDatabaseFixture(); await database.InitializeAsync();
        var keyDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "f6-phase5-keys-" + Guid.NewGuid().ToString("N")));
        void Configure(IServiceCollection services)
        {
            services.RemoveAll<ApplicationDbContext>(); services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(database.ConnectionString));
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton(DataProtectionProvider.Create(keyDirectory, builder => builder.SetApplicationName("F6.Phase5.IsolatedKeys")));
        }
        string token; string cursor; ReviewStartResponseDto session; ReviewSubmitRequestDto answer; ReviewSubmitResponseDto receipt; List<int> saved;
        try
        {
            using (var first = new ReviewTestHost(configureDatabase: Configure))
            {
                await first.SeedAsync(5); token = first.Owner.User.Token; saved = first.Words.Select(w => w.SavedId).ToList();
                cursor = (await first.GetAsync<ReviewDueResponseDto>("/api/reviews/due?take=1")).NextCursor!;
                session = await first.StartAsync(2); answer = first.Answer(session); receipt = await first.SubmitAsync(answer);
            }
            Assert.NotEmpty(keyDirectory.GetFiles("*.xml"));
            using var second = new ReviewTestHost(configureDatabase: Configure); using var client = ApiTestClientHelper.CreateClientWithBearerToken(second.Factory, token);
            using var pageResponse = await client.GetAsync("/api/reviews/due?take=100&cursor=" + Uri.EscapeDataString(cursor));
            var page = await ApiTestClientHelper.ReadApiResultAsync<ReviewDueResponseDto>(pageResponse); Assert.True(page.Success, page.RawContent);
            Assert.Equal(saved.Skip(1), page.Data!.Items.Select(i => i.UserWordId));
            using var resumedResponse = await client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = session.SessionId });
            var resumed = await ApiTestClientHelper.ReadApiResultAsync<ReviewStartResponseDto>(resumedResponse); Assert.True(resumed.Success, resumed.RawContent);
            Assert.Equal(session.Items.Select(i => JsonSerializer.Serialize(i.Options)), resumed.Data!.Items.Select(i => JsonSerializer.Serialize(i.Options)));
            Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(resumed.Data.Items[0].AcceptedReceipt));
            second.Clock.Now = second.Clock.Now.AddMinutes(30);
            await ReviewTestHost.AssertErrorAsync(await client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = session.SessionId }), 409, "review_session_expired");
            using var replayResponse = await client.PostAsJsonAsync("/api/reviews/submit", answer);
            var replay = await ApiTestClientHelper.ReadApiResultAsync<ReviewSubmitResponseDto>(replayResponse); Assert.True(replay.Success, replay.RawContent);
            Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(replay.Data));
        }
        finally
        {
            // Delete only this verified isolated key directory's files, without recursive deletion.
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), keyDirectory.Parent!.FullName.TrimEnd(Path.DirectorySeparatorChar));
            Assert.StartsWith("f6-phase5-keys-", keyDirectory.Name);
            foreach (var file in keyDirectory.GetFiles()) file.Delete(); keyDirectory.Delete();
        }
    }
}
