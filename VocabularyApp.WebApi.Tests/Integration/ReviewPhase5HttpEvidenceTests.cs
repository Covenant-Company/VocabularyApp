using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ReviewPhase5HttpEvidenceTests
{
    [Fact]
    public async Task CaptureActualHttpAndOpenApiContractsForAngularAcceptance()
    {
        using var host = new ReviewTestHost(); await host.SeedAsync();
        var responses = new Dictionary<string, object>();
        async Task<JsonElement> Capture(string name, HttpResponseMessage response, int status = 200)
        {
            using (response)
            {
                Assert.Equal(status, (int)response.StatusCode);
                var text = await response.Content.ReadAsStringAsync();
                var body = string.IsNullOrEmpty(text) ? JsonSerializer.SerializeToElement<object?>(null) : JsonDocument.Parse(text).RootElement.Clone();
                responses[name] = new { status, contentType = response.Content.Headers.ContentType?.MediaType, body };
                return body;
            }
        }
        await Capture("summary", await host.Client.GetAsync("/api/reviews/summary"));
        await Capture("due", await host.Client.GetAsync("/api/reviews/due?take=2"));
        var ready = await Capture("ready", await host.Client.PostAsJsonAsync("/api/reviews/start", new { questionCount = 2 }));
        var session = JsonSerializer.Deserialize<ReviewStartResponseDto>(ready.GetProperty("data").GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var staleSession = await host.StartAsync();
        var answer = new ReviewSubmitRequestDto { SessionId = session.SessionId!.Value, ItemId = session.Items[0].ItemId, SubmissionId = Guid.NewGuid(), SelectedOptionId = 0 };
        var accepted = await Capture("receipt", await host.Client.PostAsJsonAsync("/api/reviews/submit", answer));
        var replay = await Capture("replay", await host.Client.PostAsJsonAsync("/api/reviews/submit", answer));
        Assert.Equal(accepted.GetRawText(), replay.GetRawText());
        await Capture("resume", await host.Client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = session.SessionId }));
        await Capture("stateChanged", await host.Client.PostAsJsonAsync("/api/reviews/submit", host.Answer(staleSession)), 409);
        answer.SelectedOptionId = 1;
        await Capture("conflict", await host.Client.PostAsJsonAsync("/api/reviews/submit", answer), 409);
        await Capture("validation", await host.Client.PostAsJsonAsync("/api/reviews/submit", new { sessionId = answer.SessionId, itemId = answer.ItemId, submissionId = answer.SubmissionId }), 400);
        await Capture("missing", await host.Client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = Guid.NewGuid() }), 404);
        await Capture("completedReceipt", await host.Client.PostAsJsonAsync("/api/reviews/submit", host.Answer(session, 1)));
        await Capture("completed", await host.Client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = session.SessionId }));
        host.Clock.Now = host.Clock.Now.AddMinutes(30);
        await Capture("expired", await host.Client.PostAsJsonAsync("/api/reviews/start", new { resumeSessionId = session.SessionId }), 409);
        var failedSession = await host.StartAsync(); host.Failure.FailAfterTable = "UserWordReviewStates";
        await Capture("internal", await host.Client.PostAsJsonAsync("/api/reviews/submit", host.Answer(failedSession)), 500);
        using (var anonymous = host.Factory.CreateClient()) await Capture("unauthorized", await anonymous.GetAsync("/api/reviews/due"), 401);
        using (var disabled = new ReviewTestHost(enabled: false)) { await disabled.SeedAsync(0); await Capture("disabled", await disabled.Client.GetAsync("/api/reviews/summary"), 503); }
        using (var empty = new ReviewTestHost()) { await empty.SeedAsync(0); await Capture("empty", await empty.Client.PostAsJsonAsync("/api/reviews/start", new { })); }
        using (var insufficient = new ReviewTestHost()) { await insufficient.SeedAsync(1); await Capture("insufficient", await insufficient.Client.PostAsJsonAsync("/api/reviews/start", new { })); }
        using (var blocked = new ReviewTestHost())
        {
            await blocked.SeedAsync(0); await blocked.AddWordAsync("blocked-evidence", "\u2003\t");
            await Capture("blocked", await blocked.Client.PostAsJsonAsync("/api/reviews/start", new { }));
            await Capture("blockedDue", await blocked.Client.GetAsync("/api/reviews/due"));
        }
        using (var ambiguous = new ReviewTestHost())
        {
            await ambiguous.SeedAsync();
            await ambiguous.WithDbAsync(async db => { foreach (var d in await db.WordDefinitions.ToListAsync()) d.Definition = "Same seeded meaning"; await db.SaveChangesAsync(); });
            await Capture("ambiguous", await ambiguous.Client.PostAsJsonAsync("/api/reviews/start", new { }));
        }
        var openApi = await Capture("openApi", await host.Client.GetAsync("/swagger/v1/swagger.json"));
        responses.Remove("openApi");
        var evidence = new { source = "WebApplicationFactory HTTP + isolated SQLite; no production data or JWT values", responses,
            submission = new { sessionId = session.SessionId, itemId = session.Items[0].ItemId, submissionId = answer.SubmissionId, selectedOptionId = 0 },
            schemas = openApi.GetProperty("components").GetProperty("schemas").EnumerateObject()
                .Where(p => p.Name.Contains("Review", StringComparison.Ordinal) || p.Name == "ApiErrorResponse").ToDictionary(p => p.Name, p => p.Value.Clone()),
            paths = openApi.GetProperty("paths").EnumerateObject().Where(p => p.Name.StartsWith("/api/reviews/", StringComparison.Ordinal)).ToDictionary(p => p.Name, p => p.Value.Clone()) };
        var output = Environment.GetEnvironmentVariable("F6_PHASE5_HTTP_EVIDENCE");
        if (output != null)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "VocabularyApp.sln"))) root = root.Parent;
            Assert.NotNull(root);
            var expected = Path.GetFullPath(Path.Combine(root!.FullName, "VocabularyApp.UI", "src", "app", "testing", "phase5-http.evidence.ts"));
            Assert.Equal(expected, Path.GetFullPath(output));
            var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(expected, "// Generated from actual isolated ASP.NET HTTP responses by ReviewPhase5HttpEvidenceTests.\n// Test-only: imported by specs, never by application code. No JWTs or production data.\nexport const phase5HttpEvidence = " + json + ";\n");
        }
    }
}
