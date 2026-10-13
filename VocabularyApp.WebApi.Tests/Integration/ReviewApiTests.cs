using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ReviewApiTests
{
    [Theory]
    [InlineData("due", false)] [InlineData("summary", false)] [InlineData("start", true)] [InlineData("submit", true)]
    public async Task AuthenticationAndDisabledGates(string route, bool post)
    {
        using var host = new ReviewTestHost(enabled: false);
        await host.SeedAsync(0);
        using var anonymous = host.Factory.CreateClient();
        var request = route == "submit" ? (object)new ReviewSubmitRequestDto { SessionId = Guid.NewGuid(), ItemId = Guid.NewGuid(), SubmissionId = Guid.NewGuid() } : new ReviewStartRequestDto();
        using var unauthenticated = post ? await anonymous.PostAsJsonAsync($"/api/reviews/{route}", request) : await anonymous.GetAsync($"/api/reviews/{route}");
        Assert.Equal(401, (int)unauthenticated.StatusCode);
        await ReviewTestHost.AssertErrorAsync(post ? await host.Client.PostAsJsonAsync($"/api/reviews/{route}", request) : await host.Client.GetAsync($"/api/reviews/{route}"), 503, "reviews_disabled");
    }

    [Fact]
    public async Task FullBacklogSummaryUtcBoundaryAndProtectedCursor()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(5);
        await host.AddWordAsync("blocked", "\u2003\t");
        var summary = await host.GetAsync<ReviewSummaryResponseDto>("/api/reviews/summary");
        Assert.Equal(6, summary.DueCount); Assert.Equal(6, summary.NewCount); Assert.Equal(0, summary.OverdueCount);
        Assert.Equal(1, summary.BlockedCount); Assert.Equal(5, summary.UsableDueCount); Assert.Null(summary.NextReviewAt);
        var first = await host.GetAsync<ReviewDueResponseDto>("/api/reviews/due?take=2");
        Assert.Equal(2, first.Items.Count); Assert.NotNull(first.NextCursor); Assert.Equal(DateTimeKind.Utc, first.AsOfUtc.Kind);
        host.Clock.Now = host.Clock.Now.AddTicks(1);
        var all = first.Items.ToList(); var page = first;
        while (page.NextCursor != null)
        {
            page = await host.GetAsync<ReviewDueResponseDto>("/api/reviews/due?take=2&cursor=" + Uri.EscapeDataString(page.NextCursor));
            Assert.Equal(first.AsOfUtc, page.AsOfUtc); all.AddRange(page.Items);
        }
        Assert.Equal(6, all.Select(i => i.UserWordId).Distinct().Count()); Assert.False(all[^1].IsUsable);
        await ReviewTestHost.AssertErrorAsync(await host.Client.GetAsync("/api/reviews/due?cursor=" + Uri.EscapeDataString(first.NextCursor! + "tamper")), 400, "validation_failed");
        using var other = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(host.Factory);
        await ReviewTestHost.AssertErrorAsync(await other.Client.GetAsync("/api/reviews/due?cursor=" + Uri.EscapeDataString(first.NextCursor!)), 400, "validation_failed");
        host.Clock.Now = host.Clock.Now.AddMinutes(30);
        await ReviewTestHost.AssertErrorAsync(await host.Client.GetAsync("/api/reviews/due?cursor=" + Uri.EscapeDataString(first.NextCursor!)), 400, "validation_failed");
        Assert.Equal(6, (await host.GetAsync<ReviewSummaryResponseDto>("/api/reviews/summary")).OverdueCount);
    }

    [Theory]
    [InlineData(0, "empty", null)] [InlineData(1, "unavailable", "insufficient_usable_vocabulary")]
    public async Task AvailabilityCreatesNoSessions(int words, string availability, string? reason)
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(words);
        var start = await host.StartAsync(); Assert.Equal(availability, start.Availability); Assert.Equal(reason, start.Reason);
        Assert.Null(start.SessionId); Assert.Empty(start.Items);
        await host.WithDbAsync(async db => Assert.False(await db.ReviewSessions.AnyAsync()));
    }

    [Fact]
    public async Task BlockedAndAmbiguousAvailability()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(0);
        await host.AddWordAsync("blank", " "); Assert.Equal("no_usable_due_words", (await host.StartAsync()).Reason);
        for (var i = 0; i < 4; i++) await host.AddWordAsync($"ambiguous-{i}", "same meaning");
        Assert.Equal("ambiguous_options", (await host.StartAsync()).Reason);
    }

    [Fact]
    public async Task NonDueDistractorsAndNoPendingAnswerKey()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync();
        await host.WithDbAsync(async db =>
        {
            foreach (var state in await db.UserWordReviewStates.Where(s => s.UserWordId != host.Words[0].SavedId).ToListAsync())
                state.NextReviewAt = host.Clock.Now.UtcDateTime.AddDays(1);
            await db.SaveChangesAsync();
        });
        using var response = await host.Client.PostAsJsonAsync("/api/reviews/start", new { questionCount = 20 });
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctOptionId", raw); Assert.DoesNotContain("sourceDefinition", raw);
        var result = System.Text.Json.JsonSerializer.Deserialize<SuccessResponse<ReviewStartResponseDto>>(raw, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var item = Assert.Single(result.Data!.Items); Assert.Null(item.AcceptedReceipt);
        Assert.Equal(4, item.Options.Select(o => o.Text).Distinct().Count());
        Assert.Equal(host.Words[0].SavedId, item.UserWordId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sessionId\":\"00000000-0000-0000-0000-000000000000\",\"itemId\":\"00000000-0000-0000-0000-000000000000\",\"submissionId\":\"00000000-0000-0000-0000-000000000000\",\"selectedOptionId\":0}")]
    [InlineData("{\"sessionId\":null,\"itemId\":null,\"submissionId\":null,\"selectedOptionId\":null}")]
    public async Task InvalidSubmitUsesValidationProblem(string body)
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(0);
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsync("/api/reviews/submit", new StringContent(body, System.Text.Encoding.UTF8, "application/json")), 400, "validation_failed");
    }

    [Fact]
    public async Task InvalidQueryAndStartRequests()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(0);
        foreach (var query in new[] { "take=0", "take=101", "take=no", "cursor=bad" })
            await ReviewTestHost.AssertErrorAsync(await host.Client.GetAsync("/api/reviews/due?" + query), 400, "validation_failed");
        await ReviewTestHost.AssertErrorAsync(await host.Client.PostAsJsonAsync("/api/reviews/start", new { questionCount = 21 }), 400, "validation_failed");
    }

    [Fact]
    public async Task OwnerIsolationConcealsSessionsAndItems()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(); var session = await host.StartAsync();
        using var other = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(host.Factory);
        await ReviewTestHost.AssertErrorAsync(await other.Client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = session.SessionId }), 404, "review_session_unavailable");
        await ReviewTestHost.AssertErrorAsync(await other.Client.PostAsJsonAsync("/api/reviews/submit", host.Answer(session)), 404, "review_session_unavailable");
        var due = await ApiTestClientHelper.ReadApiResultAsync<ReviewDueResponseDto>(await other.Client.GetAsync("/api/reviews/due")); Assert.Empty(due.Data!.Items);
        var summary = await ApiTestClientHelper.ReadApiResultAsync<ReviewSummaryResponseDto>(await other.Client.GetAsync("/api/reviews/summary")); Assert.Equal(0, summary.Data!.TotalEnrolledCount);
    }
}
