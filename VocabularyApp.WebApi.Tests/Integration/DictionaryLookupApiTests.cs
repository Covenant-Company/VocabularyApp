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
    [Fact]
    public async Task CacheHitReturnsPersistedWordWithoutProviderRequest()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("cached");
        await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, wordText, "Cached definition");
        using var authenticated = await ApiTestClientHelper
            .RegisterAndCreateAuthenticatedClientAsync(factory);
        var client = authenticated.Client;

        using var response = await client.GetAsync($"/api/words/lookup/{wordText}");
        var lookup = ReadData<WordLookupResponse>(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(lookup.WasFoundInCache);
        Assert.Equal(wordText, lookup.Word?.Text);
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Fact]
    public async Task CacheMissUsesConfiguredProviderAndPersistsMappedDefinition()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("provider");
        RegisterProviderResponse(
            factory,
            wordText,
            HttpStatusCode.OK,
            ProviderJson(wordText, "verb", "Provider definition"));
        using var authenticated = await ApiTestClientHelper
            .RegisterAndCreateAuthenticatedClientAsync(factory);
        var client = authenticated.Client;

        using var response = await client.GetAsync($"/api/words/lookup/{wordText}");
        var lookup = ReadData<WordLookupResponse>(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(lookup.WasFoundInCache);
        Assert.Equal(wordText, lookup.Word?.Text);
        Assert.Single(factory.DictionaryHandler.Requests);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await context.Words
            .Include(word => word.WordDefinitions)
            .SingleAsync(word => word.Text == wordText);
        Assert.Collection(
            persisted.WordDefinitions,
            definition => Assert.Equal("Provider definition", definition.Definition));
    }

    [Fact]
    public async Task ProviderNotFoundMapsToCurrentNotFoundContractWithoutPersistence()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("not-found");
        RegisterProviderResponse(factory, wordText, HttpStatusCode.NotFound, "{}");
        using var authenticated = await ApiTestClientHelper
            .RegisterAndCreateAuthenticatedClientAsync(factory);
        var client = authenticated.Client;

        using var response = await client.GetAsync($"/api/words/lookup/{wordText}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(factory.DictionaryHandler.Requests);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await context.Words.AnyAsync(word => word.Text == wordText));
    }

    [Fact]
    public async Task ProviderServerFailureMapsToCurrentNotFoundContractWithoutPersistence()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("provider-error");
        RegisterProviderResponse(factory, wordText, HttpStatusCode.InternalServerError, "{}");
        using var authenticated = await ApiTestClientHelper
            .RegisterAndCreateAuthenticatedClientAsync(factory);
        var client = authenticated.Client;

        using var response = await client.GetAsync($"/api/words/lookup/{wordText}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(factory.DictionaryHandler.Requests);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await context.Words.AnyAsync(word => word.Text == wordText));
    }

    [Fact]
    public async Task UnknownProviderPartOfSpeechFallsBackToNoun()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = UniqueWord("unknown-pos");
        RegisterProviderResponse(
            factory,
            wordText,
            HttpStatusCode.OK,
            ProviderJson(wordText, "unmapped-provider-pos", "Fallback definition"));
        using var authenticated = await ApiTestClientHelper
            .RegisterAndCreateAuthenticatedClientAsync(factory);
        var client = authenticated.Client;

        using var response = await client.GetAsync($"/api/words/lookup/{wordText}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var partOfSpeech = await context.WordDefinitions
            .Where(definition => definition.Word.Text == wordText)
            .Select(definition => definition.PartOfSpeech.Name)
            .SingleAsync();
        Assert.Equal("Noun", partOfSpeech);
    }

    private static void RegisterProviderResponse(
        VocabularyAppWebApplicationFactory factory,
        string word,
        HttpStatusCode statusCode,
        string json) =>
        factory.DictionaryHandler.RegisterJson(
            $"/api/v2/entries/en/{Uri.EscapeDataString(word)}",
            statusCode,
            json);

    private static string ProviderJson(string word, string partOfSpeech, string definition) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                word,
                phonetic = $"/{word}/",
                phonetics = Array.Empty<object>(),
                meanings = new[]
                {
                    new
                    {
                        partOfSpeech,
                        definitions = new[] { new { definition, example = "Provider example" } }
                    }
                }
            }
        });

    private static T ReadData<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Deserialize<T>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("API response data was null.");
    }

    private static string UniqueWord(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
