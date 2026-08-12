using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

[Collection(QuizApiCollection.Name)]
public sealed class QuizApiTests : QuizApiTestBase
{
    [Fact]
    public async Task AnonymousQuizRoutesAreRejected()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();

        using var startResponse = await client.PostAsJsonAsync(
            "/api/quiz/start",
            new StartQuizRequestDto { QuestionCount = 1 });
        using var submitResponse = await client.PostAsJsonAsync(
            "/api/quiz/submit",
            new QuizSubmitRequestDto { SessionId = Guid.NewGuid() });
        using var historyResponse = await client.GetAsync("/api/quiz/history");

        Assert.Equal(HttpStatusCode.Unauthorized, startResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, submitResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, historyResponse.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedQuizCreationReturnsQuestionsWithoutAnswerKey()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await CreateQuizReadyUserAsync(factory);

        using var response = await user.Client.PostAsJsonAsync(
            "/api/quiz/start",
            new StartQuizRequestDto { QuestionCount = 2, Mode = "word-to-definition" });
        var rawJson = await response.Content.ReadAsStringAsync();
        var start = ReadData<QuizStartResponseDto>(rawJson);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, start.SessionId);
        Assert.Equal(2, start.Questions.Count);
        Assert.All(start.Questions, question => Assert.Equal(4, question.Options.Count));
        Assert.DoesNotContain("correctOptionId", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isCorrect", rawJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnotherUserCannotSubmitOwnedSessionAndOwnerCanStillSubmitIt()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        await SeedQuizVocabularyAsync(factory, users.UserA.User.User.Id, "owner");
        var start = await StartQuizAsync(users.UserA.Client, 1);
        var submission = CreateValidSubmission(start);

        using var attack = await users.UserB.Client.PostAsJsonAsync("/api/quiz/submit", submission);
        using var owner = await users.UserA.Client.PostAsJsonAsync("/api/quiz/submit", submission);

        Assert.Equal(HttpStatusCode.BadRequest, attack.StatusCode);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.All(
            await context.QuizResults.ToListAsync(),
            result => Assert.Equal(users.UserA.User.User.Id, result.UserId));
    }

    [Fact]
    public async Task ValidSubmissionPersistsCallerOwnedResultsAndDuplicateIsRejected()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await CreateQuizReadyUserAsync(factory);
        var start = await StartQuizAsync(user.Client, 2);
        var submission = CreateValidSubmission(start);

        using var first = await user.Client.PostAsJsonAsync("/api/quiz/submit", submission);
        using var duplicate = await user.Client.PostAsJsonAsync("/api/quiz/submit", submission);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var results = await context.QuizResults
            .Where(result => result.QuizSessionId == start.SessionId)
            .ToListAsync();
        Assert.Equal(start.QuestionCount, results.Count);
        Assert.All(results, result => Assert.Equal(user.User.User.Id, result.UserId));
    }

    [Fact]
    public async Task UnknownOptionIsAcceptedAsIncorrectWithoutTouchingOtherUsersData()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        await SeedQuizVocabularyAsync(factory, users.UserA.User.User.Id, "invalid-option");
        var start = await StartQuizAsync(users.UserA.Client, 1);
        var request = new QuizSubmitRequestDto
        {
            SessionId = start.SessionId,
            Answers =
            [
                new QuizAnswerSubmissionDto
                {
                    QuestionId = start.Questions.Single().QuestionId,
                    SelectedOptionId = int.MaxValue
                }
            ]
        };

        using var response = await users.UserA.Client.PostAsJsonAsync("/api/quiz/submit", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var result = await context.QuizResults.SingleAsync();
        Assert.Equal(users.UserA.User.User.Id, result.UserId);
        Assert.False(result.IsCorrect);
        Assert.Null(result.UserAnswer);
        Assert.DoesNotContain(await context.QuizResults.ToListAsync(), item => item.UserId == users.UserB.User.User.Id);
    }

    [Fact]
    public async Task AnswerForAnotherSessionIsIgnoredAndCurrentSessionIsScoredUnanswered()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await CreateQuizReadyUserAsync(factory);
        var sessionA = await StartQuizAsync(user.Client, 1);
        var sessionB = await StartQuizAsync(user.Client, 1);
        var foreignQuestion = sessionB.Questions.Single();
        var request = new QuizSubmitRequestDto
        {
            SessionId = sessionA.SessionId,
            Answers =
            [
                new QuizAnswerSubmissionDto
                {
                    QuestionId = foreignQuestion.QuestionId,
                    SelectedOptionId = foreignQuestion.Options.First().OptionId
                }
            ]
        };

        using var response = await user.Client.PostAsJsonAsync("/api/quiz/submit", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var result = await context.QuizResults.SingleAsync(item => item.QuizSessionId == sessionA.SessionId);
        Assert.False(result.IsCorrect);
        Assert.Null(result.UserAnswer);
    }

    [Fact]
    public async Task UnknownSessionFailsWithoutPersistingResults()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);

        using var response = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            new QuizSubmitRequestDto { SessionId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await context.QuizResults.ToListAsync());
    }

    [Fact]
    public async Task QuizHistoryContainsOnlyAuthenticatedUsersSessions()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        await SeedQuizVocabularyAsync(factory, users.UserA.User.User.Id, "history-a");
        await SeedQuizVocabularyAsync(factory, users.UserB.User.User.Id, "history-b");
        var sessionA = await StartQuizAsync(users.UserA.Client, 1);
        var sessionB = await StartQuizAsync(users.UserB.Client, 2);
        using var submitA = await users.UserA.Client.PostAsJsonAsync("/api/quiz/submit", CreateValidSubmission(sessionA));
        using var submitB = await users.UserB.Client.PostAsJsonAsync("/api/quiz/submit", CreateValidSubmission(sessionB));
        Assert.Equal(HttpStatusCode.OK, submitA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, submitB.StatusCode);

        using var responseA = await users.UserA.Client.GetAsync("/api/quiz/history");
        using var responseB = await users.UserB.Client.GetAsync("/api/quiz/history");
        var historyA = ReadData<QuizHistoryResponseDto>(await responseA.Content.ReadAsStringAsync());
        var historyB = ReadData<QuizHistoryResponseDto>(await responseB.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        Assert.Collection(historyA.Items, item => Assert.Equal(1, item.TotalQuestions));
        Assert.Collection(historyB.Items, item => Assert.Equal(2, item.TotalQuestions));
    }

    private static async Task<AuthenticatedApiClient> CreateQuizReadyUserAsync(
        VocabularyAppWebApplicationFactory factory)
    {
        var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        await SeedQuizVocabularyAsync(factory, user.User.User.Id, "quiz");
        return user;
    }

    private static async Task SeedQuizVocabularyAsync(
        VocabularyAppWebApplicationFactory factory,
        int userId,
        string prefix)
    {
        for (var index = 0; index < 4; index++)
        {
            var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
                factory,
                $"{prefix}-{index}-{Guid.NewGuid():N}",
                $"Definition {prefix} {index} {Guid.NewGuid():N}");
            await IntegrationTestSeeder.SeedUserWordAsync(factory, userId, word);
        }
    }

    private static async Task<QuizStartResponseDto> StartQuizAsync(HttpClient client, int questionCount)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/quiz/start",
            new StartQuizRequestDto { QuestionCount = questionCount, Mode = "word-to-definition" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ReadData<QuizStartResponseDto>(await response.Content.ReadAsStringAsync());
    }

    private static QuizSubmitRequestDto CreateValidSubmission(QuizStartResponseDto start) =>
        new()
        {
            SessionId = start.SessionId,
            Answers = start.Questions
                .Select(question => new QuizAnswerSubmissionDto
                {
                    QuestionId = question.QuestionId,
                    SelectedOptionId = question.Options.First().OptionId
                })
                .ToList()
        };

    private static T ReadData<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Deserialize<T>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("API response data was null.");
    }
}
