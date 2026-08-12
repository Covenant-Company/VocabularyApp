using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class VocabularyOwnershipApiTests
{
    [Fact]
    public async Task AuthenticatedUserSavesVocabularyWithExpectedRelationships()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var seededWord = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory,
            UniqueWord("save"),
            "A saved definition");
        var wordText = await GetWordTextAsync(factory, seededWord.WordId);

        using var response = await AddVocabularyAsync(user.Client, wordText, "Noun");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var vocabulary = await GetVocabularyAsync(user.Client, "/api/words/vocabulary");
        Assert.Collection(vocabulary.Words, item => Assert.Equal(wordText, item.Word));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await context.UserWords.SingleAsync();
        Assert.Equal(user.User.User.Id, persisted.UserId);
        Assert.Equal(seededWord.WordId, persisted.WordId);
        Assert.Equal(seededWord.PartOfSpeechId, persisted.PartOfSpeechId);
        Assert.Equal(seededWord.WordDefinitionId, persisted.PreferredWordDefinitionId);
    }

    [Fact]
    public async Task MissingCanonicalWordCannotBeCreatedThroughPersonalVocabularySave()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var wordText = UniqueWord("missing-canonical");
        const string callerDefinition = "Caller-authored definition must not become canonical.";

        using var response = await user.Client.PostAsJsonAsync(
            "/api/words/vocabulary/add",
            new AddWordRequest
            {
                Word = wordText,
                Pronunciation = "caller-pronunciation",
                Definition = callerDefinition,
                PartOfSpeech = "Noun"
            });
        var envelope = await response.Content.ReadFromJsonAsync<VocabularyEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.False(string.IsNullOrWhiteSpace(envelope.Error));
        Assert.Contains("not available", envelope.Error, StringComparison.OrdinalIgnoreCase);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await context.Words.AnyAsync(word => word.Text == wordText));
        Assert.False(await context.WordDefinitions.AnyAsync(
            definition => definition.Definition == callerDefinition));
        Assert.False(await context.UserWords.AnyAsync(
            userWord => userWord.UserId == user.User.User.Id));
    }

    [Fact]
    public async Task VocabularyListsReturnOnlyAuthenticatedUsersRows()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        var wordA = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, UniqueWord("owner-a"), "Definition A");
        var wordB = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, UniqueWord("owner-b"), "Definition B");
        await IntegrationTestSeeder.SeedUserWordAsync(factory, users.UserA.User.User.Id, wordA);
        await IntegrationTestSeeder.SeedUserWordAsync(factory, users.UserB.User.User.Id, wordB);
        var wordAText = await GetWordTextAsync(factory, wordA.WordId);
        var wordBText = await GetWordTextAsync(factory, wordB.WordId);

        var listA = await GetVocabularyAsync(users.UserA.Client, "/api/words/vocabulary");
        var listB = await GetVocabularyAsync(users.UserB.Client, "/api/words/vocabulary");

        Assert.Collection(listA.Words, item => Assert.Equal(wordAText, item.Word));
        Assert.Collection(listB.Words, item => Assert.Equal(wordBText, item.Word));
        Assert.DoesNotContain(listA.Words, item => item.Word == wordBText);
        Assert.DoesNotContain(listB.Words, item => item.Word == wordAText);
    }

    [Fact]
    public async Task DuplicateCurrentLogicalSaveIsIdempotentForSamePartOfSpeech()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, UniqueWord("duplicate"), "Duplicate definition");
        var wordText = await GetWordTextAsync(factory, word.WordId);

        using var first = await AddVocabularyAsync(user.Client, wordText, "Noun");
        using var second = await AddVocabularyAsync(user.Client, wordText, "Noun");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(
            1,
            await context.UserWords.CountAsync(candidate =>
                candidate.UserId == user.User.User.Id &&
                candidate.WordId == word.WordId &&
                candidate.PartOfSpeechId == word.PartOfSpeechId));
    }

    [Fact]
    public async Task FavoriteStateIsOwnedPerUserForSameCanonicalWord()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, UniqueWord("favorite"), "Favorite definition");
        var userWordA = await IntegrationTestSeeder.SeedUserWordAsync(
            factory, users.UserA.User.User.Id, word);
        var userWordB = await IntegrationTestSeeder.SeedUserWordAsync(
            factory, users.UserB.User.User.Id, word);

        using var ownResponse = await users.UserA.Client.PutAsJsonAsync(
            $"/api/words/vocabulary/{userWordA}/favorite",
            new UpdateFavoriteRequestDto { IsFavorite = true });
        using var attackResponse = await users.UserB.Client.PutAsJsonAsync(
            $"/api/words/vocabulary/{userWordA}/favorite",
            new UpdateFavoriteRequestDto { IsFavorite = false });

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, attackResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await context.UserWords.SingleAsync(item => item.Id == userWordA)).IsFavorite);
        Assert.False((await context.UserWords.SingleAsync(item => item.Id == userWordB)).IsFavorite);
    }

    [Fact]
    public async Task PreferredDefinitionsRemainIndependentAndCrossUserMutationIsRejected()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, UniqueWord("preference"), "First definition");
        var secondDefinitionId = await IntegrationTestSeeder.SeedDefinitionAsync(
            factory, word.WordId, "Second definition");
        var userWordA = await IntegrationTestSeeder.SeedUserWordAsync(
            factory, users.UserA.User.User.Id, word);
        var userWordB = await IntegrationTestSeeder.SeedUserWordAsync(
            factory, users.UserB.User.User.Id, word);

        using var ownResponse = await users.UserA.Client.PutAsJsonAsync(
            $"/api/words/vocabulary/{userWordA}/preferred-definition",
            new UpdatePreferredDefinitionRequestDto
            {
                PreferredWordDefinitionId = secondDefinitionId
            });
        using var attackResponse = await users.UserB.Client.PutAsJsonAsync(
            $"/api/words/vocabulary/{userWordA}/preferred-definition",
            new UpdatePreferredDefinitionRequestDto
            {
                PreferredWordDefinitionId = word.WordDefinitionId
            });

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, attackResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(
            secondDefinitionId,
            (await context.UserWords.SingleAsync(item => item.Id == userWordA))
                .PreferredWordDefinitionId);
        Assert.Equal(
            word.WordDefinitionId,
            (await context.UserWords.SingleAsync(item => item.Id == userWordB))
                .PreferredWordDefinitionId);
    }

    [Fact]
    public async Task PreferredDefinitionFromAnotherWordIsRejectedWithoutMutation()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var ownedWord = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, UniqueWord("owned"), "Owned definition");
        var foreignWord = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, UniqueWord("foreign"), "Foreign definition");
        var userWordId = await IntegrationTestSeeder.SeedUserWordAsync(
            factory, user.User.User.Id, ownedWord);

        using var response = await user.Client.PutAsJsonAsync(
            $"/api/words/vocabulary/{userWordId}/preferred-definition",
            new UpdatePreferredDefinitionRequestDto
            {
                PreferredWordDefinitionId = foreignWord.WordDefinitionId
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(
            ownedWord.WordDefinitionId,
            (await context.UserWords.SingleAsync(item => item.Id == userWordId))
                .PreferredWordDefinitionId);
    }

    [Fact]
    public async Task MissingVocabularyAndDefinitionIdsFailWithoutMutation()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, UniqueWord("missing"), "Original definition");
        var userWordId = await IntegrationTestSeeder.SeedUserWordAsync(
            factory, user.User.User.Id, word);

        using var missingFavorite = await user.Client.PutAsJsonAsync(
            "/api/words/vocabulary/2147483647/favorite",
            new UpdateFavoriteRequestDto { IsFavorite = true });
        using var missingDefinition = await user.Client.PutAsJsonAsync(
            $"/api/words/vocabulary/{userWordId}/preferred-definition",
            new UpdatePreferredDefinitionRequestDto
            {
                PreferredWordDefinitionId = int.MaxValue
            });
        using var invalidDefinition = await user.Client.PutAsJsonAsync(
            $"/api/words/vocabulary/{userWordId}/preferred-definition",
            new UpdatePreferredDefinitionRequestDto { PreferredWordDefinitionId = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, missingFavorite.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingDefinition.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDefinition.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await context.UserWords.SingleAsync(item => item.Id == userWordId);
        Assert.False(persisted.IsFavorite);
        Assert.Equal(word.WordDefinitionId, persisted.PreferredWordDefinitionId);
    }

    [Fact]
    public async Task SearchAndListFiltersDoNotRevealOtherUsersVocabulary()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        var sharedPrefix = $"alpha{Guid.NewGuid():N}";
        var alphaA = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, $"{sharedPrefix}-a", "Owner A searchable definition");
        var betaA = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, UniqueWord("beta"), "Owner A other definition");
        var alphaB = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory, $"{sharedPrefix}-private", "Owner B private definition");
        await IntegrationTestSeeder.SeedUserWordAsync(factory, users.UserA.User.User.Id, alphaA);
        await IntegrationTestSeeder.SeedUserWordAsync(factory, users.UserA.User.User.Id, betaA);
        await IntegrationTestSeeder.SeedUserWordAsync(factory, users.UserB.User.User.Id, alphaB);

        var searchA = await GetVocabularyAsync(
            users.UserA.Client,
            $"/api/words/vocabulary/search?term={sharedPrefix}");
        var filteredA = await GetVocabularyAsync(
            users.UserA.Client,
            $"/api/words/vocabulary?term={sharedPrefix}&startsWithLetter=a");
        var searchB = await GetVocabularyAsync(
            users.UserB.Client,
            $"/api/words/vocabulary/search?term={sharedPrefix}");

        Assert.Collection(searchA.Words, item => Assert.Equal($"{sharedPrefix}-a", item.Word));
        Assert.Collection(filteredA.Words, item => Assert.Equal($"{sharedPrefix}-a", item.Word));
        Assert.Collection(searchB.Words, item => Assert.Equal($"{sharedPrefix}-private", item.Word));
    }

    [Fact]
    public async Task RepresentativeVocabularyRoutesRejectAnonymousRequests()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();

        using var listResponse = await client.GetAsync("/api/words/vocabulary");
        using var mutationResponse = await client.PutAsJsonAsync(
            "/api/words/vocabulary/1/favorite",
            new UpdateFavoriteRequestDto { IsFavorite = true });

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, mutationResponse.StatusCode);
    }

    [Fact]
    public async Task DirectCanonicalWordAddIsUnavailableToAnonymousAndAuthenticatedUsers()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var anonymousClient = factory.CreateClient();
        using var authenticated = await ApiTestClientHelper
            .RegisterAndCreateAuthenticatedClientAsync(factory);
        var wordText = UniqueWord("anonymous-canonical");
        var request = new AddWordRequest
        {
            Word = wordText,
            Definition = "Caller-authored canonical definition",
            PartOfSpeech = "Noun"
        };

        using var anonymousResponse = await anonymousClient.PostAsJsonAsync(
            "/api/words/add", request);
        using var authenticatedResponse = await authenticated.Client.PostAsJsonAsync(
            "/api/words/add", request);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Contains(
            authenticatedResponse.StatusCode,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await context.Words.AnyAsync(word => word.Text == wordText));
        Assert.False(await context.WordDefinitions.AnyAsync(
            definition => definition.Definition == request.Definition));
    }

    private static Task<HttpResponseMessage> AddVocabularyAsync(
        HttpClient client,
        string word,
        string partOfSpeech) =>
        client.PostAsJsonAsync(
            "/api/words/vocabulary/add",
            new AddWordRequest { Word = word, PartOfSpeech = partOfSpeech });

    private static async Task<UserVocabularyResponseDto> GetVocabularyAsync(
        HttpClient client,
        string requestUri)
    {
        using var response = await client.GetAsync(requestUri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<VocabularyEnvelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success);
        return Assert.IsType<UserVocabularyResponseDto>(envelope.Data);
    }

    private static async Task<string> GetWordTextAsync(
        VocabularyAppWebApplicationFactory factory,
        int wordId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Words
            .Where(word => word.Id == wordId)
            .Select(word => word.Text)
            .SingleAsync();
    }

    private static string UniqueWord(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}";

    private sealed class VocabularyEnvelope
    {
        public bool Success { get; set; }
        public UserVocabularyResponseDto? Data { get; set; }
        public string? Error { get; set; }
    }
}
