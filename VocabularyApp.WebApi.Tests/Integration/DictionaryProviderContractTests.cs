using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;
using static VocabularyApp.WebApi.Tests.Infrastructure.JsonContractAssert;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class DictionaryProviderContractTests
{
    private const string Unavailable = "Dictionary service is temporarily unavailable. Please try again.";
    private const string Valid = """{"definition":" Meaning ","partOfSpeech":" nOuN "}""";

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{invalid")]
    [InlineData("{}")]
    [InlineData("""{"results":[{"definition":"Meaning","partOfSpeech":"noun"}]}""")]
    [InlineData("""{"word":null,"results":[]}""")]
    [InlineData("""{"word":" ","results":[]}""")]
    [InlineData("""{"word":42,"results":[]}""")]
    [InlineData("""{"word":"boundary"}""")]
    [InlineData("""{"word":"boundary","results":null}""")]
    [InlineData("""{"word":"boundary","results":[]}""")]
    [InlineData("""{"word":"boundary","results":{}}""")]
    [InlineData("""{"word":"boundary","results":[null]}""")]
    [InlineData("""{"word":"boundary","results":[{}]}""")]
    [InlineData("""{"word":"boundary","results":[{"definition":null,"partOfSpeech":"noun"}]}""")]
    [InlineData("""{"word":"boundary","results":[{"definition":" ","partOfSpeech":"noun","examples":["Not a definition"],"synonyms":["Not a definition"]}]}""")]
    [InlineData("""{"word":"boundary","results":[{"definition":"Meaning","partOfSpeech":"unsupported"}]}""")]
    [InlineData("""{"word":"boundary","results":[{"definition":"Meaning"}]}""")]
    public async Task UnusablePayloadReturnsSafeUnavailableWithoutWrites(string json)
    {
        await AssertFailureAsync(json);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"definition":"Other","partOfSpeech":"noun","examples":null}""")]
    [InlineData("""{"definition":"Other","partOfSpeech":"unsupported","examples":null}""")]
    [InlineData("""{"definition":" ","examples":null}""")]
    [InlineData("""{"definition":"Other","partOfSpeech":"noun","examples":{}}""")]
    [InlineData("""{"definition":"Other","partOfSpeech":"noun","examples":[42]}""")]
    [InlineData("""{"definition":42,"partOfSpeech":"noun"}""")]
    [InlineData("""{"definition":"Other","partOfSpeech":[]}""")]
    public async Task MalformedEntryRejectsEntireResponseEvenAfterValidEntry(string invalid)
    {
        await AssertFailureAsync($$"""{"word":"boundary","results":[{{Valid}},{{invalid}}]}""");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("""{"all":{}}""")]
    public async Task MalformedPronunciationRejectsOtherwiseValidResponse(string pronunciation)
    {
        await AssertFailureAsync($$"""{"word":"boundary","pronunciation":{{pronunciation}},"results":[{{Valid}}]}""");
    }

    [Theory]
    [InlineData(404)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(400)]
    [InlineData(302)]
    public async Task UnsuccessfulStatusUsesSafeEnvelopeAndLeavesAllDictionaryTablesUnchanged(int status)
    {
        await AssertFailureAsync(ApiErrorContractAssert.Sentinel, (HttpStatusCode)status);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("cancelled")]
    [InlineData("stream")]
    public async Task TransportFailureIsSafeInResponseAndLogs(string kind)
    {
        var logger = new CapturingLogger<WordService>();
        using var factory = new VocabularyAppWebApplicationFactory
        {
            ConfigureTestServices = services => services.AddSingleton<ILogger<WordService>>(logger)
        };
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var before = await PartsAsync(factory);
        var secret = ApiErrorContractAssert.Sentinel + " https://provider.invalid/key=integration-test-words-api-key";
        Exception exception = kind switch
        {
            "network" => new HttpRequestException(secret),
            "timeout" => new TaskCanceledException(secret),
            "cancelled" => new OperationCanceledException(secret),
            _ => new IOException(secret)
        };
        factory.DictionaryHandler.RegisterException("/words/boundary", exception);
        using var response = await user.Client.GetAsync("/api/words/lookup/boundary");
        await AssertUnavailableAsync(response);
        await AssertNoWritesAsync(factory, before);
        Assert.Single(factory.DictionaryHandler.Requests);
        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(ApiErrorContractAssert.Sentinel, entry.Message);
            Assert.DoesNotContain("provider.invalid", entry.Message);
            Assert.DoesNotContain("integration-test-words-api-key", entry.Message);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ValidMixedContentPreservesPublicShapePersistenceAndCache(int optionalMode)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var before = await PartsAsync(factory);
        var optional = optionalMode > 0 ? ""","pronunciation":{}""" : "";
        var supported = optionalMode switch
        {
            1 => """{"definition":" Meaning ","partOfSpeech":" nOuN ","examples":[]}""",
            2 => """{"definition":" Meaning ","partOfSpeech":" nOuN ","examples":[null," "," Example "]}""",
            _ => Valid
        };
        factory.DictionaryHandler.RegisterJson("/words/boundary", HttpStatusCode.OK,
            $$"""{"word":" boundary "{{optional}},"results":[{{supported}},{"definition":"Ignore","partOfSpeech":"unsupported"},{"definition":null,"partOfSpeech":"noun"},{"definition":" ","partOfSpeech":"verb"}]}""");
        using var response = await user.Client.GetAsync("/api/words/lookup/boundary");
        using var json = await ReadSuccessAsync(response);
        var data = SuccessData(json.RootElement);
        Properties(data, "success", "errorMessage", "word", "wasFoundInCache", "isInUserVocabulary");
        Property(data, "success", JsonValueKind.True);
        Property(data, "errorMessage", JsonValueKind.Null);
        Property(data, "wasFoundInCache", JsonValueKind.False);
        Property(data, "isInUserVocabulary", JsonValueKind.False);
        var word = Property(data, "word", JsonValueKind.Object);
        Properties(word, "id", "text", "pronunciation", "audioUrl", "createdAt", "definitions");
        Assert.Equal("boundary", word.GetProperty("text").GetString());
        Assert.True(word.GetProperty("id").GetInt32() > 0);
        Assert.True(word.GetProperty("createdAt").TryGetDateTime(out _));
        Property(word, "audioUrl", JsonValueKind.Null);
        Property(word, "pronunciation", JsonValueKind.Null);
        var definition = Assert.Single(word.GetProperty("definitions").EnumerateArray());
        Properties(definition, "id", "definition", "example", "partOfSpeech", "partOfSpeechAbbreviation", "displayOrder");
        Assert.True(definition.GetProperty("id").GetInt32() > 0);
        Assert.Equal("Meaning", definition.GetProperty("definition").GetString());
        Assert.Equal("Noun", definition.GetProperty("partOfSpeech").GetString());
        Assert.False(string.IsNullOrWhiteSpace(definition.GetProperty("partOfSpeechAbbreviation").GetString()));
        Assert.Equal(1, definition.GetProperty("displayOrder").GetInt32());
        Assert.Equal(optionalMode == 2 ? "Example" : null, definition.GetProperty("example").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = Assert.Single(await db.Words.ToListAsync());
            Assert.Equal(word.GetProperty("id").GetInt32(), stored.Id);
            Assert.Null(stored.AudioUrl);
            var storedDefinition = Assert.Single(await db.WordDefinitions.ToListAsync());
            Assert.Equal(stored.Id, storedDefinition.WordId);
            Assert.Equal("Meaning", storedDefinition.Definition);
            Assert.Equal(optionalMode == 2 ? "Example" : null, storedDefinition.Example);
            Assert.Empty(await db.UserWords.ToListAsync());
        }
        Assert.Equal(before, await PartsAsync(factory));
        // A subsequent lookup must bypass even an unavailable provider.
        factory.DictionaryHandler.RegisterException("/words/boundary", new HttpRequestException("Unavailable"));
        using var cached = await user.Client.GetAsync("/api/words/lookup/boundary");
        using var cachedJson = await ReadSuccessAsync(cached);
        var cachedData = SuccessData(cachedJson.RootElement);
        Property(cachedData, "wasFoundInCache", JsonValueKind.True);
        var cachedWord = cachedData.GetProperty("word");
        Properties(cachedWord, "id", "text", "pronunciation", "audioUrl", "createdAt", "definitions");
        // SQLite preserves the timestamp value, but not DateTimeKind/the JSON Z suffix.
        // UTC normalization is explicitly outside R7; keep every other field exact.
        foreach (var property in word.EnumerateObject())
        {
            var cachedProperty = cachedWord.GetProperty(property.Name);
            if (property.Name == "createdAt")
            {
                Property(cachedWord, "createdAt", JsonValueKind.String);
                Assert.Equal(property.Value.GetDateTime().Ticks, cachedProperty.GetDateTime().Ticks);
            }
            else Assert.Equal(property.Value.GetRawText(), cachedProperty.GetRawText());
        }
        Assert.Single(factory.DictionaryHandler.Requests);
    }

    private static async Task AssertFailureAsync(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var before = await PartsAsync(factory);
        factory.DictionaryHandler.RegisterJson("/words/boundary", status, json);
        using var response = await user.Client.GetAsync("/api/words/lookup/boundary");
        if (status == HttpStatusCode.NotFound)
            await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.NotFound,
                "word_not_found", "No definitions found.");
        else
            await AssertUnavailableAsync(response);
        Assert.Single(factory.DictionaryHandler.Requests);
        await AssertNoWritesAsync(factory, before);
    }

    private static Task AssertUnavailableAsync(HttpResponseMessage response) =>
        ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.ServiceUnavailable,
            "dictionary_unavailable", Unavailable, "integration-test-words-api-key", "wordsapiv1.p.rapidapi.com");

    private static async Task<string[]> PartsAsync(VocabularyAppWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.PartsOfSpeech.OrderBy(part => part.Id).ToListAsync())
            .Select(part => $"{part.Id}:{part.Name}:{part.Abbreviation}").ToArray();
    }

    private static async Task AssertNoWritesAsync(VocabularyAppWebApplicationFactory factory, string[] before)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Words.ToListAsync());
        Assert.Empty(await db.WordDefinitions.ToListAsync());
        Assert.Empty(await db.UserWords.ToListAsync());
        Assert.Equal(before, await PartsAsync(factory));
    }
}
