using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.Tests.Infrastructure;
using static VocabularyApp.WebApi.Tests.Infrastructure.JsonContractAssert;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class VocabularyContractTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"isFavorite":null}""")]
    [InlineData("""{"isFavorite":"private-secret-sql-provider-connection-sentinel"}""")]
    [InlineData("""{"isFavorite":0}""")]
    [InlineData("""{"isFavorite":{}}""")]
    [InlineData("""{"isFavorite":[]}""")]
    public async Task InvalidFavoritePresenceOrTypeReturnsValidationWithoutChangingSavedRow(string body)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "favoriteword", "Meaning");
        var id = await IntegrationTestSeeder.SeedUserWordAsync(factory, user.User.User.Id, word, isFavorite: true);
        using var response = await user.Client.PutAsync($"/api/words/vocabulary/{id}/favorite",
            new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        Properties(root, "type", "title", "status", "errors", "traceId", "success", "data", "error", "message", "code");
        Property(root, "success", JsonValueKind.False);
        Property(root, "data", JsonValueKind.Null);
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("validation_failed", root.GetProperty("code").GetString());
        Assert.Equal("One or more request fields are invalid.", root.GetProperty("error").GetString());
        Assert.Equal(root.GetProperty("error").GetString(), root.GetProperty("message").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.NotEmpty(root.GetProperty("errors").EnumerateObject());
        Assert.DoesNotContain(ApiErrorContractAssert.Sentinel, raw);
        Assert.DoesNotContain("System.", raw);
        Assert.DoesNotContain("Exception", raw);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = Assert.Single(await db.UserWords.ToListAsync());
        Assert.Equal(id, saved.Id);
        Assert.True(saved.IsFavorite);
        Assert.Equal(word.WordId, saved.WordId);
        Assert.Equal(word.WordDefinitionId, saved.PreferredWordDefinitionId);
        Assert.Equal(word.PartOfSpeechId, saved.PartOfSpeechId);
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("?term=", false)]
    [InlineData("?term=%20%20%20", false)]
    [InlineData("?term=contract", true)]
    [InlineData("?term=%20contract%20", true)]
    [InlineData("?term=no-match", false)]
    public async Task SearchAlwaysReturnsCompleteTypedPageAndOnlyOwnedResults(string query, bool hasResult)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        var owned = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "contractowned", "Owned meaning");
        var other = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "contractprivate", "Private meaning");
        var id = await IntegrationTestSeeder.SeedUserWordAsync(factory, users.UserA.User.User.Id, owned);
        await IntegrationTestSeeder.SeedUserWordAsync(factory, users.UserB.User.User.Id, other);
        using var response = await users.UserA.Client.GetAsync("/api/words/vocabulary/search" + query);
        using var json = await ReadSuccessAsync(response);
        var data = SuccessData(json.RootElement);
        Properties(data, "words", "totalCount", "page", "pageSize", "totalPages");
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(5, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(hasResult ? 1 : 0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(hasResult ? 1 : 0, data.GetProperty("totalPages").GetInt32());
        var items = Property(data, "words", JsonValueKind.Array).EnumerateArray().ToArray();
        if (hasResult)
        {
            var item = Assert.Single(items);
            Properties(item, "id", "word", "definition", "preferredWordDefinitionId", "example", "partOfSpeech",
                "pronunciation", "audioUrl", "addedAt", "isFavorite", "personalNotes", "correctAnswers", "totalAttempts", "accuracyRate");
            Assert.Equal(id, item.GetProperty("id").GetInt32());
            Assert.Equal("contractowned", item.GetProperty("word").GetString());
            Assert.Equal("Owned meaning", item.GetProperty("definition").GetString());
            Assert.Equal(owned.WordDefinitionId, item.GetProperty("preferredWordDefinitionId").GetInt32());
            foreach (var name in new[] { "example", "pronunciation", "audioUrl", "personalNotes", "accuracyRate" })
                Property(item, name, JsonValueKind.Null);
            Assert.True(item.GetProperty("addedAt").TryGetDateTime(out _));
            Assert.Equal(0, item.GetProperty("totalAttempts").GetInt32());
            Assert.Equal(0, item.GetProperty("correctAnswers").GetInt32());
            Property(item, "isFavorite", JsonValueKind.False);
        }
        else Assert.Empty(items);
        Assert.DoesNotContain("contractprivate", json.RootElement.GetRawText());
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Theory]
    [InlineData("", 1, 20, 3)]
    [InlineData("?page=0&pageSize=0", 1, 20, 3)]
    [InlineData("?page=-1&pageSize=-1", 1, 20, 3)]
    [InlineData("?page=1&pageSize=10001", 1, 20, 3)]
    [InlineData("?page=1&pageSize=10000", 1, 10000, 3)]
    [InlineData("?page=2&pageSize=2", 2, 2, 1)]
    public async Task ListPreservesPagingDefaultsBoundsAndTotalCount(string query, int page, int pageSize, int count)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        foreach (var text in new[] { "pagealpha", "pagebeta", "pagegamma" })
        {
            var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, text, "Meaning");
            await IntegrationTestSeeder.SeedUserWordAsync(factory, user.User.User.Id, word);
        }
        using var response = await user.Client.GetAsync("/api/words/vocabulary" + query);
        using var json = await ReadSuccessAsync(response);
        var data = SuccessData(json.RootElement);
        Properties(data, "words", "totalCount", "page", "pageSize", "totalPages");
        Assert.Equal(page, data.GetProperty("page").GetInt32());
        Assert.Equal(pageSize, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal((int)Math.Ceiling(3d / pageSize), data.GetProperty("totalPages").GetInt32());
        Assert.Equal(count, data.GetProperty("words").GetArrayLength());
        if (page == 2) Assert.Equal("pagegamma", data.GetProperty("words")[0].GetProperty("word").GetString());
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Fact]
    public async Task EmptyListReturnsCompleteSuccessfulPage()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await user.Client.GetAsync("/api/words/vocabulary");
        using var json = await ReadSuccessAsync(response);
        var data = SuccessData(json.RootElement);
        Properties(data, "words", "totalCount", "page", "pageSize", "totalPages");
        Assert.Empty(data.GetProperty("words").EnumerateArray());
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Fact]
    public async Task LargeVocabularyKeepsDistinctPagesAndCappedSearchMetadata()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pos = await db.PartsOfSpeech.SingleAsync(p => p.Name == "Noun");
            // Bulk fixture authoring avoids 1001 separate seeder scopes/save sequences.
            for (var index = 0; index < 1001; index++)
            {
                var word = new Word { Text = $"pageword{index:D4}", CreatedAt = IntegrationTestSeeder.SeedTimestamp };
                var definition = new WordDefinition { Word = word, PartOfSpeechId = pos.Id,
                    Definition = "Page meaning", CreatedAt = IntegrationTestSeeder.SeedTimestamp };
                db.UserWords.Add(new UserWord { UserId = users.UserA.User.User.Id, Word = word,
                    PartOfSpeechId = pos.Id, PreferredWordDefinition = definition, AddedAt = IntegrationTestSeeder.SeedTimestamp });
            }
            await db.SaveChangesAsync();
        }
        var foreign = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "pagewordprivate", "Private meaning");
        await IntegrationTestSeeder.SeedUserWordAsync(factory, users.UserB.User.User.Id, foreign);
        var firstIds = new HashSet<int>();
        foreach (var page in new[] { 1, 2 })
        {
            using var response = await users.UserA.Client.GetAsync($"/api/words/vocabulary?page={page}&pageSize=1000");
            using var json = await ReadSuccessAsync(response);
            var data = SuccessData(json.RootElement);
            Properties(data, "words", "totalCount", "page", "pageSize", "totalPages");
            Assert.Equal(1001, data.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, data.GetProperty("totalPages").GetInt32());
            Assert.Equal(page, data.GetProperty("page").GetInt32());
            Assert.Equal(1000, data.GetProperty("pageSize").GetInt32());
            Assert.Equal(page == 1 ? 1000 : 1, data.GetProperty("words").GetArrayLength());
            foreach (var item in data.GetProperty("words").EnumerateArray())
            {
                var id = item.GetProperty("id").GetInt32();
                if (page == 1) Assert.True(firstIds.Add(id));
                else Assert.DoesNotContain(id, firstIds);
            }
            Assert.DoesNotContain("pagewordprivate", json.RootElement.GetRawText());
        }
        using var search = await users.UserA.Client.GetAsync("/api/words/vocabulary/search?term=pageword");
        using var searchJson = await ReadSuccessAsync(search);
        var matches = SuccessData(searchJson.RootElement);
        Properties(matches, "words", "totalCount", "page", "pageSize", "totalPages");
        Assert.Equal(5, matches.GetProperty("words").GetArrayLength());
        Assert.Equal(5, matches.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, matches.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, matches.GetProperty("page").GetInt32());
        Assert.Equal(1, matches.GetProperty("totalPages").GetInt32());
        Assert.DoesNotContain("pagewordprivate", searchJson.RootElement.GetRawText());
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Theory]
    [InlineData("{}", "invalid_request")]
    [InlineData("""{"word":null}""", "invalid_request")]
    [InlineData("""{"word":""}""", "invalid_request")]
    [InlineData("""{"word":"   "}""", "invalid_request")]
    [InlineData("""{"word":" exactword "}""", "canonical_word_required")]
    public async Task AddPreservesRequiredWordAndExactCanonicalText(string body, string code)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "exactword", "Canonical meaning");
        using var response = await user.Client.PostAsync("/api/words/vocabulary/add",
            new StringContent(body, Encoding.UTF8, "application/json"));
        await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.BadRequest, code,
            code == "invalid_request" ? "The request is invalid." :
                "This word is not available. Look it up before adding it to your vocabulary.");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.UserWords.ToListAsync());
        Assert.Equal("exactword", (await db.Words.SingleAsync()).Text);
        Assert.Equal("Canonical meaning", (await db.WordDefinitions.SingleAsync()).Definition);
        Assert.Empty(factory.DictionaryHandler.Requests);
    }
}
