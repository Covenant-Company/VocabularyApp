using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.DTOs;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class ReviewTestHost : IDisposable
{
    public VocabularyAppWebApplicationFactory Factory { get; } = new();
    public ControllableTimeProvider Clock { get; } = new();
    public ReviewPersistenceFailureInterceptor Failure { get; } = new();
    public ReviewCommitFailureInterceptor CommitFailure { get; } = new();
    public AuthenticatedApiClient Owner { get; private set; } = null!;
    public List<(SeededWord Word, int SavedId)> Words { get; } = new();
    public HttpClient Client => Owner.Client;
    public ReviewTestHost(bool enabled = true, bool enrollment = true, Action<IServiceCollection>? configureDatabase = null)
    {
        Factory.AdditionalInterceptors = [Failure, CommitFailure];
        Factory.ConfigureTestServices = services =>
        {
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IDataProtectionProvider>(); services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.PostConfigure<ReviewOptions>(o => { o.Enabled = enabled; o.EnrollmentEnabled = enrollment; });
            configureDatabase?.Invoke(services);
        };
    }
    public async Task SeedAsync(int count = 4)
    {
        Owner = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(Factory);
        for (var i = 0; i < count; i++) await AddWordAsync($"review-word-{i}", $"Meaning number {i}");
    }
    public async Task AddWordAsync(string text, string definition)
    {
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(Factory, text, definition);
        var result = await ApiTestClientHelper.ReadApiResultAsync<AddToVocabularyResultDto>(await Client.PostAsJsonAsync("/api/words/vocabulary/add", new { word = text, preferredWordDefinitionId = word.WordDefinitionId }));
        Assert.True(result.Success, result.RawContent);
        Words.Add((word, result.Data!.UserWordId));
    }
    public async Task<T> GetAsync<T>(string path) => await DataAsync<T>(await Client.GetAsync(path));
    public async Task<ReviewStartResponseDto> StartAsync(int count = 1, Guid? resume = null) =>
        await DataAsync<ReviewStartResponseDto>(await Client.PostAsJsonAsync("/api/reviews/start", new ReviewStartRequestDto { QuestionCount = count, ResumeSessionId = resume }));
    public ReviewSubmitRequestDto Answer(ReviewStartResponseDto session, int index = 0, bool correct = true)
    {
        var item = session.Items[index];
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var key = db.ReviewSessionItems.Single(i => i.Id == item.ItemId).CorrectOptionId;
        return new() { SessionId = session.SessionId!.Value, ItemId = item.ItemId, SubmissionId = Guid.NewGuid(), SelectedOptionId = correct ? key : (key + 1) % 4 };
    }
    public async Task<ReviewSubmitResponseDto> SubmitAsync(ReviewSubmitRequestDto request) => await DataAsync<ReviewSubmitResponseDto>(await Client.PostAsJsonAsync("/api/reviews/submit", request));
    public async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope(); await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
    private static async Task<T> DataAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            var result = await ApiTestClientHelper.ReadApiResultAsync<T>(response);
            Assert.True(result.Success, result.RawContent);
            return result.Data!;
        }
    }
    public static async Task AssertErrorAsync(HttpResponseMessage response, int status, string code)
    {
        using (response)
        {
            Assert.Equal(status, (int)response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
            if (code != "validation_failed")
                Assert.Equal(new[] { "code", "data", "error", "message", "success", "traceId" }, json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        }
    }
    public void Dispose() { Owner?.Dispose(); Factory.Dispose(); }
}
