using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VocabularyApp.Data;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ReviewContractTests
{
    [Fact]
    public async Task ExactRuntimeShapesAndNullableFields()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync();
        async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using (response)
            {
                response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Shape(json.RootElement, "success", "data"); return json.RootElement.GetProperty("data").Clone();
            }
        }
        var due = await Read(await host.Client.GetAsync("/api/reviews/due")); Shape(due, "items", "nextCursor", "asOfUtc");
        Shape(due.GetProperty("items")[0], "userWordId", "word", "nextReviewAt", "reviewStatus", "isUsable");
        Assert.Equal(JsonValueKind.Null, due.GetProperty("nextCursor").ValueKind);
        var summary = await Read(await host.Client.GetAsync("/api/reviews/summary"));
        Shape(summary, "dueCount", "newCount", "overdueCount", "blockedCount", "usableDueCount", "totalEnrolledCount", "nextReviewAt", "asOfUtc");
        var start = await Read(await host.Client.PostAsJsonAsync("/api/reviews/start", new { questionCount = 1 }));
        Shape(start, "availability", "reason", "sessionId", "expiresAtUtc", "items", "sessionSummary");
        var item = start.GetProperty("items")[0]; Shape(item, "itemId", "userWordId", "questionType", "prompt", "options", "acceptedReceipt");
        Shape(item.GetProperty("options")[0], "optionId", "text"); Assert.Equal(JsonValueKind.Null, item.GetProperty("acceptedReceipt").ValueKind);
        var session = JsonSerializer.Deserialize<ReviewStartResponseDto>(start.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var receipt = await Read(await host.Client.PostAsJsonAsync("/api/reviews/submit", host.Answer(session)));
        Shape(receipt, "sessionId", "itemId", "submissionId", "reviewedAtUtc", "isCorrect", "selectedOptionId", "correctOptionId", "selectedAnswer", "correctAnswer", "nextReviewAt", "reviewIntervalDays", "reviewStatus", "sessionSummary");
        Shape(receipt.GetProperty("sessionSummary"), "totalItems", "acceptedCount", "correctCount", "incorrectCount", "pendingCount", "isComplete");
        Assert.EndsWith("Z", receipt.GetProperty("reviewedAtUtc").GetString());
    }
    private static void Shape(JsonElement value, params string[] names) => Assert.Equal(names.Order(), value.EnumerateObject().Select(p => p.Name).Order());

    [Fact]
    public async Task OpenApiReviewPresenceNullabilityAndBothValidationMediaTypes()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync(0);
        using var response = await host.Client.GetAsync("/swagger/v1/swagger.json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root = json.RootElement;
        var schema = root.GetProperty("components").GetProperty("schemas").GetProperty("ReviewSubmitRequestDto");
        Assert.Equal(new[] { "itemId", "selectedOptionId", "sessionId", "submissionId" }, schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).Order().ToArray());
        var requests = root.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "ReviewStartRequestDto" })
            Assert.False(requests.GetProperty(name).TryGetProperty("required", out var required) && required.GetArrayLength() != 0);
        var query = root.GetProperty("paths").GetProperty("/api/reviews/due").GetProperty("get").GetProperty("parameters");
        Assert.Equal(new[] { "cursor", "take" }, query.EnumerateArray().Select(p => p.GetProperty("name").GetString()!.ToLowerInvariant()).Order().ToArray());
        var bad = root.GetProperty("paths").GetProperty("/api/reviews/submit").GetProperty("post").GetProperty("responses").GetProperty("400").GetProperty("content");
        Assert.True(bad.TryGetProperty("application/json", out _)); Assert.True(bad.TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public async Task NewHostResumesDurableSnapshotsAndReceipts()
    {
        await using var database = new IndependentConnectionDatabaseFixture(); await database.InitializeAsync();
        void Configure(IServiceCollection services)
        {
            services.RemoveAll<ApplicationDbContext>(); services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(database.ConnectionString));
        }
        Guid id; Guid item; string token; Guid acceptedSubmission;
        using (var first = new ReviewTestHost(configureDatabase: Configure))
        {
            await first.SeedAsync(); var session = await first.StartAsync(2); id = session.SessionId!.Value; item = session.Items[1].ItemId;
            acceptedSubmission = (await first.SubmitAsync(first.Answer(session))).SubmissionId; token = first.Owner.User.Token;
        }
        using var second = new ReviewTestHost(configureDatabase: Configure);
        using var client = ApiTestClientHelper.CreateClientWithBearerToken(second.Factory, token);
        using var response = await client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = id });
        var result = await ApiTestClientHelper.ReadApiResultAsync<ReviewStartResponseDto>(response); Assert.True(result.Success, result.RawContent);
        Assert.Equal(item, result.Data!.Items[1].ItemId); Assert.Null(result.Data.Items[1].AcceptedReceipt);
        Assert.Equal(acceptedSubmission, result.Data.Items[0].AcceptedReceipt!.SubmissionId);
    }
}
