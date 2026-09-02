using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class DictionaryLookupApiTests
{
    private const string ResolvedAudioUrl =
        "https://media.merriam-webster.com/audio/prons/en/us/mp3/t/test0001.mp3";

    [Fact]
    public async Task CacheHitReturnsPersistedWordWithoutProviderRequest()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("cached");
        await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, wordText, "Cached definition");
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        var lookup = ReadData<WordLookupResponse>(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(lookup.WasFoundInCache);
        Assert.Equal(wordText, lookup.Word?.Text);
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Fact]
    public async Task CacheHitWithHistoricalAudioPreservesItAndSkipsAudioProvider()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("historical-audio");
        const string historicalUrl = "https://legacy.example.test/audio/word.mp3";
        await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, wordText, "Cached definition", audioUrl: historicalUrl);
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);

        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        var lookup = ReadData<WordLookupResponse>(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(historicalUrl, lookup.Word?.AudioUrl);
        Assert.Empty(factory.PronunciationAudioService.Requests);
    }

    [Fact]
    public async Task CacheHitWithMissingAudioResolvesAndPersistsAudio()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("cached-audio");
        await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, wordText, "Cached definition");
        factory.PronunciationAudioService.RegisterAudioUrl(wordText, ResolvedAudioUrl);
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);

        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        var lookup = ReadData<WordLookupResponse>(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ResolvedAudioUrl, lookup.Word?.AudioUrl);
        Assert.Single(factory.PronunciationAudioService.Requests);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(ResolvedAudioUrl,
            await context.Words.Where(word => word.Text == wordText)
                .Select(word => word.AudioUrl).SingleAsync());
    }

    [Fact]
    public async Task CacheMissUsesWordsApiAndPersistsMappedDefinition()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("provider");
        RegisterProviderResponse(factory, wordText, HttpStatusCode.OK,
            ProviderJson(wordText, "verb", "Provider definition"));
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        var lookup = ReadData<WordLookupResponse>(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(lookup.WasFoundInCache);
        Assert.Equal(wordText, lookup.Word?.Text);
        Assert.Single(factory.DictionaryHandler.Requests);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await context.Words.Include(word => word.WordDefinitions)
            .SingleAsync(word => word.Text == wordText);
        Assert.Collection(persisted.WordDefinitions, definition =>
        {
            Assert.Equal("Provider definition", definition.Definition);
            Assert.Equal("Provider example", definition.Example);
        });
    }

    [Fact]
    public async Task CacheMissPersistsLexicalDataWhenAudioProviderFails()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("optional-audio");
        RegisterProviderResponse(factory, wordText, HttpStatusCode.OK,
            ProviderJson(wordText, "noun", "Provider definition"));
        factory.PronunciationAudioService.RegisterException(
            wordText, new HttpRequestException("Simulated audio failure."));
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);

        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        var lookup = ReadData<WordLookupResponse>(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(lookup.Word?.AudioUrl);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await context.Words.AnyAsync(word => word.Text == wordText));
    }

    [Fact]
    public async Task CacheMissResolvesAndPersistsAudioWithLexicalData()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("new-audio");
        RegisterProviderResponse(factory, wordText, HttpStatusCode.OK,
            ProviderJson(wordText, "noun", "Provider definition"));
        factory.PronunciationAudioService.RegisterAudioUrl(wordText, ResolvedAudioUrl);
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);

        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        var lookup = ReadData<WordLookupResponse>(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ResolvedAudioUrl, lookup.Word?.AudioUrl);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(ResolvedAudioUrl,
            await context.Words.Where(word => word.Text == wordText)
                .Select(word => word.AudioUrl).SingleAsync());
    }

    [Fact]
    public async Task ProviderNotFoundReturnsNotFoundWithoutPersistence()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("not-found");
        RegisterProviderResponse(factory, wordText, HttpStatusCode.NotFound, "{}");
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNotPersisted(factory, wordText);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ProviderFailuresReturnServiceUnavailable(HttpStatusCode statusCode)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("provider-failure");
        RegisterProviderResponse(factory, wordText, statusCode, "{}");
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("temporarily unavailable", await response.Content.ReadAsStringAsync());
        await AssertNotPersisted(factory, wordText);
    }

    [Fact]
    public async Task ProviderNetworkFailureReturnsServiceUnavailable()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("network-error");
        factory.DictionaryHandler.RegisterException(
            ProviderPath(wordText), new HttpRequestException("Simulated network failure."));
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertNotPersisted(factory, wordText);
    }

    [Fact]
    public async Task ProviderTimeoutReturnsServiceUnavailable()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("timeout");
        factory.DictionaryHandler.RegisterException(
            ProviderPath(wordText), new TaskCanceledException("Simulated provider timeout."));
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertNotPersisted(factory, wordText);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task MalformedOrEmptyProviderResponseReturnsServiceUnavailable(string json)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("malformed");
        RegisterProviderResponse(factory, wordText, HttpStatusCode.OK, json);
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertNotPersisted(factory, wordText);
    }

    [Fact]
    public async Task UnknownPartOfSpeechIsNotMisclassifiedAsNoun()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("unknown-pos");
        RegisterProviderResponse(factory, wordText, HttpStatusCode.OK,
            ProviderJson(wordText, "unmapped-provider-pos", "Definition"));
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertNotPersisted(factory, wordText);
    }

    [Fact]
    public async Task ApiKeyIsSentToProviderAndNeverReturnedToClient()
    {
        const string apiKey = "integration-test-words-api-key";
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("secret-boundary");
        RegisterProviderResponse(factory, wordText, HttpStatusCode.OK,
            ProviderJson(wordText, "noun", "Safe definition"));
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await authenticated.Client.GetAsync($"/api/words/lookup/{wordText}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(apiKey, factory.DictionaryHandler.ApiKeys);
        Assert.DoesNotContain(apiKey, body, StringComparison.Ordinal);
    }

    private static void RegisterProviderResponse(
        VocabularyAppWebApplicationFactory factory, string word,
        HttpStatusCode statusCode, string json) =>
        factory.DictionaryHandler.RegisterJson(ProviderPath(word), statusCode, json);

    private static string ProviderPath(string word) => $"/words/{Uri.EscapeDataString(word)}";

    private static string ProviderJson(string? word, string partOfSpeech, string definition) =>
        JsonSerializer.Serialize(new
        {
            word,
            pronunciation = new Dictionary<string, string> { ["all"] = $"/{word}/" },
            results = new[] { new { partOfSpeech, definition, examples = new[] { "Provider example" } } }
        });

    private static T ReadData<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Deserialize<T>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("API response data was null.");
    }

    private static async Task AssertNotPersisted(
        VocabularyAppWebApplicationFactory factory, string wordText)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await context.Words.AnyAsync(word => word.Text == wordText));
    }

    private static string UniqueWord(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
