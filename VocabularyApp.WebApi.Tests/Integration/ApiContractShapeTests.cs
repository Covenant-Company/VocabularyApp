using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Tests.Infrastructure;
using static VocabularyApp.WebApi.Tests.Infrastructure.JsonContractAssert;

namespace VocabularyApp.WebApi.Tests.Integration;

// Phase 1 pins successful wire contracts, not the error statuses changing in R7.
// Reuse R6 relational hosts and the existing isolation for process-local quiz sessions.
[Collection(QuizApiCollection.Name)]
public sealed class ApiContractShapeTests : QuizApiTestBase
{
    [Fact]
    public async Task SuccessfulRegistrationPreservesNestedAuthAndNullLastLoginContract()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        var credentials = TestUserCredentials.CreateUnique("wire-register");
        using var response = await client.PostAsJsonAsync("/api/users/register", new
        {
            credentials.Username, credentials.Email, credentials.Password
        });
        using var json = await ReadSuccessAsync(response);
        var auth = AssertAuth(json.RootElement, credentials, hasLastLogin: false);

        // A real registration token also permits profile/validation before any login.
        using var authenticated = ApiTestClientHelper.CreateClientWithBearerToken(
            factory, auth.GetProperty("token").GetString()!);
        foreach (var path in new[] { "/api/users/profile", "/api/users/validate-token" })
        {
            using var profileResponse = await authenticated.GetAsync(path);
            using var profileJson = await ReadSuccessAsync(profileResponse);
            var profile = SuccessData(profileJson.RootElement, authEnvelope: true);
            AssertUser(profile, credentials, hasLastLogin: false);
            Assert.Equal(auth.GetProperty("user").GetProperty("id").GetInt32(), profile.GetProperty("id").GetInt32());
        }
    }

    [Fact]
    public async Task SuccessfulLoginPreservesCurrentAuthContractWithoutExpiresAt()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var credentials = TestUserCredentials.CreateUnique("wire-login");
        await IntegrationTestSeeder.SeedModernUserAsync(factory, credentials);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/users/login", new
        {
            credentials.Username, credentials.Password
        });
        using var json = await ReadSuccessAsync(response);
        var auth = AssertAuth(json.RootElement, credentials, hasLastLogin: true);
        Assert.False(auth.TryGetProperty("expiresAt", out _));
    }

    [Theory]
    [InlineData("/api/users/profile")]
    [InlineData("/api/users/validate-token")]
    public async Task AuthenticatedUserEndpointsPreserveUserDtoContract(string path)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await user.Client.GetAsync(path);
        using var json = await ReadSuccessAsync(response);
        var data = SuccessData(json.RootElement, authEnvelope: true);
        AssertUser(data, user.User.Credentials, hasLastLogin: true);
        Assert.Equal(user.User.User.Id, data.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task SuccessfulPasswordChangePreservesAcknowledgementAndNullFields()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await user.Client.PostAsJsonAsync("/api/users/change-password", new
        {
            currentPassword = user.User.Credentials.Password, newPassword = "Replacement wire password!"
        });
        using var json = await ReadSuccessAsync(response);
        Properties(json.RootElement, "success", "message", "data", "error");
        Property(json.RootElement, "success", JsonValueKind.True);
        Assert.Equal("Request succeeded.", Property(json.RootElement, "message", JsonValueKind.String).GetString());
        Property(json.RootElement, "data", JsonValueKind.Null);
        Property(json.RootElement, "error", JsonValueKind.Null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedLookupPreservesNestedWordNullsAndMembershipFlag(bool saved)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "contractword", "Canonical definition");
        if (saved) await IntegrationTestSeeder.SeedUserWordAsync(factory, user.User.User.Id, word);
        using var response = await user.Client.GetAsync("/api/words/lookup/contractword");
        using var json = await ReadSuccessAsync(response);
        var data = SuccessData(json.RootElement);
        Properties(data, "success", "errorMessage", "word", "wasFoundInCache", "isInUserVocabulary");
        Property(data, "success", JsonValueKind.True);
        Property(data, "errorMessage", JsonValueKind.Null);
        Property(data, "wasFoundInCache", JsonValueKind.True);
        Property(data, "isInUserVocabulary", saved ? JsonValueKind.True : JsonValueKind.False);
        var value = Property(data, "word", JsonValueKind.Object);
        Properties(value, "id", "text", "pronunciation", "audioUrl", "createdAt", "definitions");
        Assert.Equal(word.WordId, Property(value, "id", JsonValueKind.Number).GetInt32());
        Assert.Equal("contractword", Property(value, "text", JsonValueKind.String).GetString());
        Property(value, "pronunciation", JsonValueKind.Null);
        Property(value, "audioUrl", JsonValueKind.Null);
        AssertDate(value, "createdAt");
        var definition = Assert.Single(Property(value, "definitions", JsonValueKind.Array).EnumerateArray());
        Properties(definition, "id", "definition", "example", "partOfSpeech", "partOfSpeechAbbreviation", "displayOrder");
        Assert.Equal(word.WordDefinitionId, Property(definition, "id", JsonValueKind.Number).GetInt32());
        Assert.Equal("Canonical definition", Property(definition, "definition", JsonValueKind.String).GetString());
        Assert.Equal("Noun", Property(definition, "partOfSpeech", JsonValueKind.String).GetString());
        Assert.False(string.IsNullOrWhiteSpace(Property(definition, "partOfSpeechAbbreviation", JsonValueKind.String).GetString()));
        Assert.Equal(1, Property(definition, "displayOrder", JsonValueKind.Number).GetInt32());
        Property(definition, "example", JsonValueKind.Null);
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Fact]
    public async Task VocabularySuccessContractsPreserveStableIdentityAcrossRepeatedAddAndPosUpdate()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "contractword", "Canonical noun");
        var verbId = await IntegrationTestSeeder.SeedDefinitionAsync(factory, word.WordId, "Canonical verb", "Verb");
        using var addResponse = await user.Client.PostAsJsonAsync("/api/words/vocabulary/add", new
        {
            word = "contractword", preferredWordDefinitionId = word.WordDefinitionId,
            partOfSpeech = "Verb", definition = "Ignored definition", example = "Ignored example", pronunciation = "Ignored pronunciation"
        });
        using var addJson = await ReadSuccessAsync(addResponse);
        var added = AssertAdd(addJson.RootElement, word.WordId, alreadyExisted: false);
        var id = added.GetProperty("userWordId").GetInt32();

        // The existing row wins even over an invalid new preference; do not validate it first.
        foreach (var preference in new[] { verbId, int.MaxValue })
        {
            using var repeatResponse = await user.Client.PostAsJsonAsync("/api/words/vocabulary/add", new
            {
                word = "contractword", preferredWordDefinitionId = preference, partOfSpeech = "Verb"
            });
            using var repeatJson = await ReadSuccessAsync(repeatResponse);
            Assert.Equal(id, AssertAdd(repeatJson.RootElement, word.WordId, alreadyExisted: true).GetProperty("userWordId").GetInt32());
        }

        foreach (var path in new[] { "/api/words/vocabulary?page=1&pageSize=20", "/api/words/vocabulary/search?term=contract" })
        {
            using var listResponse = await user.Client.GetAsync(path);
            using var listJson = await ReadSuccessAsync(listResponse);
            var data = SuccessData(listJson.RootElement);
            AssertPage(data, path.Contains("search?") ? 5 : 20);
            var item = Assert.Single(data.GetProperty("words").EnumerateArray());
            AssertVocabularyItem(item, id, word.WordDefinitionId);
        }

        foreach (var favorite in new[] { true, false })
        {
            using var favoriteResponse = await user.Client.PutAsJsonAsync($"/api/words/vocabulary/{id}/favorite", new { isFavorite = favorite });
            using var favoriteJson = await ReadSuccessAsync(favoriteResponse);
            var data = SuccessData(favoriteJson.RootElement);
            Properties(data, "message", "userWordId", "isFavorite");
            Assert.Equal(id, Property(data, "userWordId", JsonValueKind.Number).GetInt32());
            Property(data, "isFavorite", favorite ? JsonValueKind.True : JsonValueKind.False);
            Assert.Equal(favorite ? "Word marked as favorite" : "Word removed from favorites", Property(data, "message", JsonValueKind.String).GetString());
        }

        using var preferredResponse = await user.Client.PutAsJsonAsync($"/api/words/vocabulary/{id}/preferred-definition", new { preferredWordDefinitionId = verbId });
        using var preferredJson = await ReadSuccessAsync(preferredResponse);
        var preferred = SuccessData(preferredJson.RootElement);
        Properties(preferred, "message", "userWordId", "preferredWordDefinitionId");
        Assert.Equal("Preferred definition updated", Property(preferred, "message", JsonValueKind.String).GetString());
        Assert.Equal(id, Property(preferred, "userWordId", JsonValueKind.Number).GetInt32());
        Assert.Equal(verbId, Property(preferred, "preferredWordDefinitionId", JsonValueKind.Number).GetInt32());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(await db.UserWords.Where(x => x.UserId == user.User.User.Id && x.WordId == word.WordId).ToListAsync());
        Assert.Equal(id, row.Id);
        Assert.Equal(verbId, row.PreferredWordDefinitionId);
        Assert.Equal(await db.WordDefinitions.Where(x => x.Id == verbId).Select(x => x.PartOfSpeechId).SingleAsync(), row.PartOfSpeechId);
        Assert.Equal(0, row.TotalAttempts);
        Assert.Equal(0, row.CorrectAnswers);
        Assert.Null(row.PersonalNotes);
        Assert.False(row.IsFavorite);
        var canonical = await db.Words.Include(x => x.WordDefinitions).SingleAsync(x => x.Id == word.WordId);
        Assert.Null(canonical.Pronunciation);
        Assert.Equal(2, canonical.WordDefinitions.Count);
        Assert.All(canonical.WordDefinitions, definition => Assert.Null(definition.Example));
        Assert.Equal("Canonical noun", canonical.WordDefinitions.Single(x => x.Id == word.WordDefinitionId).Definition);
        Assert.Equal("Canonical verb", canonical.WordDefinitions.Single(x => x.Id == verbId).Definition);
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Theory]
    [InlineData(null, "Noun")]
    [InlineData("unsupported-pos", "Noun")]
    [InlineData("nOuN", "Noun")]
    [InlineData("vErB", "Verb")]
    [InlineData("V.", "Verb")]
    public async Task CurrentLegacyAddResolvesPosNameAbbreviationAndNounFallback(string? requestedPos, string expectedPos)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "selectionword", "Noun meaning");
        var verbId = await IntegrationTestSeeder.SeedDefinitionAsync(factory, word.WordId, "Verb meaning", "Verb");
        using var response = await user.Client.PostAsJsonAsync("/api/words/vocabulary/add", new
        {
            word = "selectionword", partOfSpeech = requestedPos
        });
        using var json = await ReadSuccessAsync(response);
        var id = AssertAdd(json.RootElement, word.WordId, alreadyExisted: false).GetProperty("userWordId").GetInt32();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.UserWords.Include(x => x.PartOfSpeech).SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal(expectedPos, row.PartOfSpeech.Name);
        Assert.Equal(expectedPos == "Verb" ? verbId : word.WordDefinitionId, row.PreferredWordDefinitionId);
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unsupported-pos")]
    public async Task CurrentLegacyPosFallbackCanSaveNullPreferenceWithoutInventingDefinition(string? partOfSpeech)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "verbword", "Only verb meaning", "Verb");
        using var response = await user.Client.PostAsJsonAsync("/api/words/vocabulary/add", new { word = "verbword", partOfSpeech });
        using var json = await ReadSuccessAsync(response);
        var id = AssertAdd(json.RootElement, word.WordId, alreadyExisted: false).GetProperty("userWordId").GetInt32();
        using var listResponse = await user.Client.GetAsync("/api/words/vocabulary");
        using var listJson = await ReadSuccessAsync(listResponse);
        var data = SuccessData(listJson.RootElement);
        AssertPage(data, 20);
        var item = Assert.Single(data.GetProperty("words").EnumerateArray());
        AssertVocabularyItem(item, id, null, "verbword", "No definition available");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.UserWords.Include(x => x.PartOfSpeech).SingleAsync();
        Assert.Null(row.PreferredWordDefinitionId);
        Assert.Equal("Noun", row.PartOfSpeech.Name);
        Assert.Equal(1, await db.WordDefinitions.CountAsync());
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Theory]
    [InlineData(0, 10, 21)]
    [InlineData(1, 1, 4)]
    [InlineData(99, 20, 21)]
    [InlineData(10, 4, 4)]
    public async Task QuizQuestionCountPreservesCurrentDefaultsAndEligibleVocabularyLimit(int requested, int expected, int vocabularyCount)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        for (var index = 0; index < vocabularyCount; index++)
        {
            var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, $"countword{index}", $"Count meaning {index}");
            await IntegrationTestSeeder.SeedUserWordAsync(factory, user.User.User.Id, word);
        }
        using var response = await user.Client.PostAsJsonAsync("/api/quiz/start", new { questionCount = requested });
        using var json = await ReadSuccessAsync(response);
        var data = SuccessData(json.RootElement);
        Assert.Equal("mixed", Property(data, "mode", JsonValueKind.String).GetString());
        Assert.Equal(expected, Property(data, "questionCount", JsonValueKind.Number).GetInt32());
        Assert.Equal(expected, Property(data, "questions", JsonValueKind.Array).GetArrayLength());
    }

    [Fact]
    public async Task QuizStillRequiresFourUsableWordsWithoutPersistingResults()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        for (var index = 0; index < 3; index++)
        {
            var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, $"minimumword{index}", $"Minimum meaning {index}");
            await IntegrationTestSeeder.SeedUserWordAsync(factory, user.User.User.Id, word);
        }
        using var response = await user.Client.PostAsJsonAsync("/api/quiz/start", new { questionCount = 1 });
        // This validation 400 is explicitly preserved by D2; do not pin the legacy error envelope.
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.QuizResults.ToListAsync());
        Assert.All(await db.UserWords.ToListAsync(), row =>
        {
            Assert.Equal(0, row.TotalAttempts);
            Assert.Equal(0, row.CorrectAnswers);
        });
    }

    [Theory]
    [InlineData("word-to-definition")]
    [InlineData("definition-to-word")]
    [InlineData("mixed")]
    public async Task QuizSuccessContractsPreserveNoAnswerLeakageScoringNullAnswersAndHistory(string mode)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var meanings = new Dictionary<string, string>();
        for (var index = 0; index < 4; index++)
        {
            var text = $"quizword{index}";
            var definition = $"Meaning number {index}";
            meanings.Add(text, definition);
            var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, text, definition);
            await IntegrationTestSeeder.SeedUserWordAsync(factory, user.User.User.Id, word);
        }
        using var startResponse = await user.Client.PostAsJsonAsync("/api/quiz/start", new { questionCount = 2, mode });
        using var startJson = await ReadSuccessAsync(startResponse);
        var start = SuccessData(startJson.RootElement);
        Properties(start, "sessionId", "mode", "questionCount", "expiresAtUtc", "questions");
        var sessionId = Property(start, "sessionId", JsonValueKind.String).GetGuid();
        Assert.NotEqual(Guid.Empty, sessionId);
        Assert.Equal(mode, Property(start, "mode", JsonValueKind.String).GetString());
        Assert.Equal(2, Property(start, "questionCount", JsonValueKind.Number).GetInt32());
        AssertDate(start, "expiresAtUtc");
        var questions = Property(start, "questions", JsonValueKind.Array).EnumerateArray().ToArray();
        Assert.Equal(2, questions.Length);
        Assert.Equal(2, questions.Select(x => x.GetProperty("questionId").GetGuid()).Distinct().Count());
        foreach (var question in questions)
        {
            // Exact key sets at every level exclude correctOptionId/correctAnswer/isCorrect.
            Properties(question, "questionId", "questionType", "prompt", "options");
            Assert.NotEqual(Guid.Empty, Property(question, "questionId", JsonValueKind.String).GetGuid());
            var type = Property(question, "questionType", JsonValueKind.String).GetString();
            Assert.Contains(type, new[] { "word-to-definition", "definition-to-word" });
            if (mode != "mixed") Assert.Equal(mode, type);
            Property(question, "prompt", JsonValueKind.String);
            var options = Property(question, "options", JsonValueKind.Array).EnumerateArray().ToArray();
            Assert.Equal(new[] { 0, 1, 2, 3 }, options.Select(x => Property(x, "optionId", JsonValueKind.Number).GetInt32()).OrderBy(x => x));
            foreach (var option in options)
            {
                Properties(option, "optionId", "text");
                Property(option, "text", JsonValueKind.String);
            }
        }
        // Answer one correctly using seeded meanings, independent of shuffled order; leave one unanswered.
        string CorrectText(JsonElement question)
        {
            var isWordPrompt = question.GetProperty("questionType").GetString() == "word-to-definition";
            var meaning = meanings.Single(x => question.GetProperty("prompt").GetString()!.Contains($"\"{(isWordPrompt ? x.Key : x.Value)}\"", StringComparison.Ordinal));
            return isWordPrompt ? meaning.Value : meaning.Key;
        }
        var correctText = CorrectText(questions[0]);
        var optionId = questions[0].GetProperty("options").EnumerateArray().Single(x => x.GetProperty("text").GetString() == correctText).GetProperty("optionId").GetInt32();
        using var submitResponse = await user.Client.PostAsJsonAsync("/api/quiz/submit", new
        {
            sessionId, answers = new[] { new { questionId = questions[0].GetProperty("questionId").GetGuid(), selectedOptionId = optionId } }
        });
        using var submitJson = await ReadSuccessAsync(submitResponse);
        var result = SuccessData(submitJson.RootElement);
        Properties(result, "totalQuestions", "correctAnswers", "scorePercentage", "questionResults");
        AssertScore(result);
        var results = Property(result, "questionResults", JsonValueKind.Array).EnumerateArray().ToArray();
        Assert.Equal(2, results.Length);
        foreach (var question in questions)
        {
            var questionId = question.GetProperty("questionId").GetGuid();
            var detail = Assert.Single(results.Where(x => x.GetProperty("questionId").GetGuid() == questionId));
            Properties(detail, "questionId", "questionType", "prompt", "correctAnswer", "selectedAnswer", "isCorrect");
            Property(detail, "questionId", JsonValueKind.String);
            Assert.Equal(question.GetProperty("questionType").GetString(), Property(detail, "questionType", JsonValueKind.String).GetString());
            Assert.Equal(question.GetProperty("prompt").GetString(), Property(detail, "prompt", JsonValueKind.String).GetString());
            Assert.Equal(CorrectText(question), Property(detail, "correctAnswer", JsonValueKind.String).GetString());
            var answered = questionId == questions[0].GetProperty("questionId").GetGuid();
            Property(detail, "isCorrect", answered ? JsonValueKind.True : JsonValueKind.False);
            var selected = Property(detail, "selectedAnswer", answered ? JsonValueKind.String : JsonValueKind.Null);
            if (answered) Assert.Equal(correctText, selected.GetString());
        }
        using var historyResponse = await user.Client.GetAsync("/api/quiz/history?take=5");
        using var historyJson = await ReadSuccessAsync(historyResponse);
        var history = SuccessData(historyJson.RootElement);
        Properties(history, "items");
        var item = Assert.Single(Property(history, "items", JsonValueKind.Array).EnumerateArray());
        Properties(item, "attemptedAtUtc", "totalQuestions", "correctAnswers", "scorePercentage");
        AssertDate(item, "attemptedAtUtc");
        AssertScore(item);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.QuizResults.Where(x => x.QuizSessionId == sessionId).ToListAsync();
        Assert.Equal(2, persisted.Count);
        Assert.Single(persisted.Where(x => x.IsCorrect));
        Assert.Single(persisted.Where(x => x.UserAnswer == null));
        var savedWords = await db.UserWords.Where(x => x.UserId == user.User.User.Id).ToListAsync();
        Assert.Equal(2, savedWords.Sum(x => x.TotalAttempts));
        Assert.Equal(1, savedWords.Sum(x => x.CorrectAnswers));
    }

    private static JsonElement AssertAuth(JsonElement root, TestUserCredentials credentials, bool hasLastLogin)
    {
        var auth = SuccessData(root, authEnvelope: true);
        Properties(auth, "success", "errorMessage", "user", "token");
        Property(auth, "success", JsonValueKind.True);
        Property(auth, "errorMessage", JsonValueKind.Null);
        Assert.False(string.IsNullOrWhiteSpace(Property(auth, "token", JsonValueKind.String).GetString()));
        AssertUser(Property(auth, "user", JsonValueKind.Object), credentials, hasLastLogin);
        return auth;
    }

    private static void AssertUser(JsonElement user, TestUserCredentials credentials, bool hasLastLogin)
    {
        Properties(user, "id", "username", "email", "createdAt", "lastLoginAt");
        Assert.True(Property(user, "id", JsonValueKind.Number).GetInt32() > 0);
        Assert.Equal(credentials.Username, Property(user, "username", JsonValueKind.String).GetString());
        Assert.Equal(credentials.Email, Property(user, "email", JsonValueKind.String).GetString());
        AssertDate(user, "createdAt");
        if (hasLastLogin) AssertDate(user, "lastLoginAt");
        else Property(user, "lastLoginAt", JsonValueKind.Null);
    }

    private static JsonElement AssertAdd(JsonElement root, int wordId, bool alreadyExisted)
    {
        var data = SuccessData(root);
        Properties(data, "userWordId", "wordId", "alreadyExisted", "message");
        Assert.True(Property(data, "userWordId", JsonValueKind.Number).GetInt32() > 0);
        Assert.Equal(wordId, Property(data, "wordId", JsonValueKind.Number).GetInt32());
        Property(data, "alreadyExisted", alreadyExisted ? JsonValueKind.True : JsonValueKind.False);
        Assert.Equal(alreadyExisted ? "Word already in your vocabulary" : "Word added to your vocabulary", Property(data, "message", JsonValueKind.String).GetString());
        return data;
    }

    private static void AssertPage(JsonElement data, int pageSize)
    {
        Properties(data, "words", "totalCount", "page", "pageSize", "totalPages");
        Property(data, "words", JsonValueKind.Array);
        Assert.Equal(1, Property(data, "totalCount", JsonValueKind.Number).GetInt32());
        Assert.Equal(1, Property(data, "page", JsonValueKind.Number).GetInt32());
        Assert.Equal(pageSize, Property(data, "pageSize", JsonValueKind.Number).GetInt32());
        Assert.Equal(1, Property(data, "totalPages", JsonValueKind.Number).GetInt32());
    }

    private static void AssertVocabularyItem(JsonElement item, int id, int? preferredId,
        string word = "contractword", string definition = "Canonical noun")
    {
        Properties(item, "id", "word", "definition", "preferredWordDefinitionId", "example", "partOfSpeech",
            "pronunciation", "audioUrl", "addedAt", "isFavorite", "personalNotes", "correctAnswers", "totalAttempts", "accuracyRate");
        Assert.Equal(id, Property(item, "id", JsonValueKind.Number).GetInt32());
        Assert.Equal(word, Property(item, "word", JsonValueKind.String).GetString());
        Assert.Equal(definition, Property(item, "definition", JsonValueKind.String).GetString());
        Assert.Equal("Noun", Property(item, "partOfSpeech", JsonValueKind.String).GetString());
        if (preferredId.HasValue) Assert.Equal(preferredId.Value, Property(item, "preferredWordDefinitionId", JsonValueKind.Number).GetInt32());
        else Property(item, "preferredWordDefinitionId", JsonValueKind.Null);
        foreach (var name in new[] { "example", "pronunciation", "audioUrl", "personalNotes", "accuracyRate" })
            Property(item, name, JsonValueKind.Null);
        AssertDate(item, "addedAt");
        Property(item, "isFavorite", JsonValueKind.False);
        Assert.Equal(0, Property(item, "correctAnswers", JsonValueKind.Number).GetInt32());
        Assert.Equal(0, Property(item, "totalAttempts", JsonValueKind.Number).GetInt32());
    }

    private static void AssertDate(JsonElement value, string name) =>
        Assert.True(Property(value, name, JsonValueKind.String).TryGetDateTime(out _));

    private static void AssertScore(JsonElement value)
    {
        Assert.Equal(2, Property(value, "totalQuestions", JsonValueKind.Number).GetInt32());
        Assert.Equal(1, Property(value, "correctAnswers", JsonValueKind.Number).GetInt32());
        Assert.Equal(50d, Property(value, "scorePercentage", JsonValueKind.Number).GetDouble());
    }
}
