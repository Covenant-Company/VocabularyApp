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
    private static readonly DateTime PreviousReviewUtc =
        new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static readonly DateTime PreviousCorrectUtc =
        new(2025, 1, 1, 2, 3, 4, DateTimeKind.Utc);

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
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        await SeedQuizVocabularyAsync(factory, user.User.User.Id, "answer-key");

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
    public async Task CorrectAnswerPersistsResultAndIncrementsExistingLearningState()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(
            factory,
            user.User.User.Id,
            "correct",
            correctAnswers: 3,
            totalAttempts: 5,
            lastReviewedAt: PreviousReviewUtc,
            lastCorrectAt: PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 1);
        var question = start.Questions.Single();
        var word = FindSeededWord(question, words);

        using var response = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateSubmission(start.SessionId, CreateAnswer(question, word, isCorrect: true)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await LoadLearningStateAsync(factory, word.UserWordId);
        var result = Assert.Single(await LoadSessionResultsAsync(factory, start.SessionId));
        Assert.True(result.IsCorrect);
        Assert.Equal(word.UserWordId, result.UserWordId);
        Assert.Equal(4, state.CorrectAnswers);
        Assert.Equal(6, state.TotalAttempts);
        Assert.NotNull(state.LastReviewedAt);
        Assert.NotNull(state.LastCorrectAt);
        Assert.Equal(result.AttemptedAt, state.LastReviewedAt!.Value);
        Assert.Equal(result.AttemptedAt, state.LastCorrectAt!.Value);
        Assert.True(state.LastReviewedAt > PreviousReviewUtc);
        Assert.True(state.LastCorrectAt > PreviousCorrectUtc);
    }

    [Fact]
    public async Task IncorrectAnswerPersistsResultAndPreservesExistingCorrectState()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(
            factory,
            user.User.User.Id,
            "incorrect",
            correctAnswers: 3,
            totalAttempts: 5,
            lastReviewedAt: PreviousReviewUtc,
            lastCorrectAt: PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 1);
        var question = start.Questions.Single();
        var word = FindSeededWord(question, words);

        using var response = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateSubmission(start.SessionId, CreateAnswer(question, word, isCorrect: false)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await LoadLearningStateAsync(factory, word.UserWordId);
        var result = Assert.Single(await LoadSessionResultsAsync(factory, start.SessionId));
        Assert.False(result.IsCorrect);
        Assert.Equal(3, state.CorrectAnswers);
        Assert.Equal(6, state.TotalAttempts);
        Assert.NotNull(state.LastReviewedAt);
        Assert.Equal(result.AttemptedAt, state.LastReviewedAt!.Value);
        Assert.Equal(PreviousCorrectUtc, state.LastCorrectAt);
        Assert.True(state.LastReviewedAt > PreviousReviewUtc);
    }

    [Fact]
    public async Task UnansweredQuestionCountsAsIncorrectAttemptAndPreservesLastCorrectAt()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(
            factory,
            user.User.User.Id,
            "unanswered",
            correctAnswers: 3,
            totalAttempts: 5,
            lastReviewedAt: PreviousReviewUtc,
            lastCorrectAt: PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 1);
        var word = FindSeededWord(start.Questions.Single(), words);

        using var response = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            new QuizSubmitRequestDto { SessionId = start.SessionId, Answers = [] });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await LoadLearningStateAsync(factory, word.UserWordId);
        var result = Assert.Single(await LoadSessionResultsAsync(factory, start.SessionId));
        Assert.False(result.IsCorrect);
        Assert.Null(result.UserAnswer);
        Assert.Equal(3, state.CorrectAnswers);
        Assert.Equal(6, state.TotalAttempts);
        Assert.NotNull(state.LastReviewedAt);
        Assert.Equal(result.AttemptedAt, state.LastReviewedAt!.Value);
        Assert.Equal(PreviousCorrectUtc, state.LastCorrectAt);
    }

    [Fact]
    public async Task MixedQuizUpdatesEachUserWordIndependently()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(
            factory,
            user.User.User.Id,
            "mixed",
            correctAnswers: 3,
            totalAttempts: 5,
            lastReviewedAt: PreviousReviewUtc,
            lastCorrectAt: PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 3);
        var questions = start.Questions;
        var correctWord = FindSeededWord(questions[0], words);
        var incorrectWord = FindSeededWord(questions[1], words);
        var unansweredWord = FindSeededWord(questions[2], words);
        var untouchedWord = words.Single(candidate =>
            candidate.UserWordId != correctWord.UserWordId &&
            candidate.UserWordId != incorrectWord.UserWordId &&
            candidate.UserWordId != unansweredWord.UserWordId);

        using var response = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateSubmission(
                start.SessionId,
                CreateAnswer(questions[0], correctWord, isCorrect: true),
                CreateAnswer(questions[1], incorrectWord, isCorrect: false)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await LoadSessionResultsAsync(factory, start.SessionId);
        Assert.Equal(3, results.Count);

        var correctState = await LoadLearningStateAsync(factory, correctWord.UserWordId);
        AssertLearningState(correctState, 4, 6, lastReviewedChanged: true, PreviousCorrectUtc, lastCorrectChanged: true);
        Assert.Equal(results.Single(item => item.UserWordId == correctWord.UserWordId).AttemptedAt, correctState.LastReviewedAt);
        Assert.Equal(correctState.LastReviewedAt, correctState.LastCorrectAt);

        var incorrectState = await LoadLearningStateAsync(factory, incorrectWord.UserWordId);
        AssertLearningState(incorrectState, 3, 6, lastReviewedChanged: true, PreviousCorrectUtc, lastCorrectChanged: false);

        var unansweredState = await LoadLearningStateAsync(factory, unansweredWord.UserWordId);
        AssertLearningState(unansweredState, 3, 6, lastReviewedChanged: true, PreviousCorrectUtc, lastCorrectChanged: false);
        var unansweredResult = results.Single(item => item.UserWordId == unansweredWord.UserWordId);
        Assert.False(unansweredResult.IsCorrect);
        Assert.Null(unansweredResult.UserAnswer);

        var untouchedState = await LoadLearningStateAsync(factory, untouchedWord.UserWordId);
        AssertLearningState(untouchedState, 3, 5, lastReviewedChanged: false, PreviousCorrectUtc, lastCorrectChanged: false);
    }

    [Fact]
    public async Task FabricatedQuestionIsRejectedWithoutMutationAndSessionRemainsRetryable()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "fabricated");
        var start = await StartQuizAsync(user.Client, 1);
        var before = await LoadLearningStatesAsync(factory, words.Select(word => word.UserWordId));
        var request = CreateSubmission(
            start.SessionId,
            new QuizAnswerSubmissionDto
            {
                QuestionId = Guid.NewGuid(),
                SelectedOptionId = start.Questions.Single().Options.First().OptionId
            });

        using var rejected = await user.Client.PostAsJsonAsync("/api/quiz/submit", request);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));

        using var retry = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateCorrectSubmission(start, words));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task AnswerForAnotherSessionIsRejectedWithoutMutationAndSessionRemainsRetryable()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "foreign-question");
        var sessionA = await StartQuizAsync(user.Client, 1);
        var sessionB = await StartQuizAsync(user.Client, 1);
        var before = await LoadLearningStatesAsync(factory, words.Select(word => word.UserWordId));
        var foreignQuestion = sessionB.Questions.Single();
        var foreignWord = FindSeededWord(foreignQuestion, words);

        using var rejected = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateSubmission(sessionA.SessionId, CreateAnswer(foreignQuestion, foreignWord, isCorrect: true)));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(await LoadSessionResultsAsync(factory, sessionA.SessionId));
        Assert.Empty(await LoadSessionResultsAsync(factory, sessionB.SessionId));
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));

        using var retry = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateCorrectSubmission(sessionA, words));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task DuplicateSubmittedQuestionIsRejectedWithoutMutationAndSessionRemainsRetryable()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "duplicate-question");
        var start = await StartQuizAsync(user.Client, 1);
        var question = start.Questions.Single();
        var word = FindSeededWord(question, words);
        var before = await LoadLearningStatesAsync(factory, words.Select(item => item.UserWordId));
        var correct = CreateAnswer(question, word, isCorrect: true);
        var incorrect = CreateAnswer(question, word, isCorrect: false);

        using var rejected = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateSubmission(start.SessionId, correct, incorrect));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));

        using var retry = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateCorrectSubmission(start, words));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task UnknownOptionIsRejectedWithoutMutationAndSessionRemainsRetryable()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        var wordsA = await SeedQuizVocabularyAsync(factory, users.UserA.User.User.Id, "invalid-option-a");
        var wordsB = await SeedQuizVocabularyAsync(factory, users.UserB.User.User.Id, "invalid-option-b");
        var start = await StartQuizAsync(users.UserA.Client, 1);
        var allIds = wordsA.Concat(wordsB).Select(word => word.UserWordId);
        var before = await LoadLearningStatesAsync(factory, allIds);
        var request = CreateSubmission(
            start.SessionId,
            new QuizAnswerSubmissionDto
            {
                QuestionId = start.Questions.Single().QuestionId,
                SelectedOptionId = int.MaxValue
            });

        using var rejected = await users.UserA.Client.PostAsJsonAsync("/api/quiz/submit", request);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));

        using var retry = await users.UserA.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateCorrectSubmission(start, wordsA));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task AnotherUserCannotSubmitOwnedSessionAndOwnerCanStillSubmitIt()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        var wordsA = await SeedQuizVocabularyAsync(factory, users.UserA.User.User.Id, "owner-a");
        var wordsB = await SeedQuizVocabularyAsync(factory, users.UserB.User.User.Id, "owner-b");
        var start = await StartQuizAsync(users.UserA.Client, 1);
        var submission = CreateCorrectSubmission(start, wordsA);
        var allIds = wordsA.Concat(wordsB).Select(word => word.UserWordId);
        var before = await LoadLearningStatesAsync(factory, allIds);

        using var attack = await users.UserB.Client.PostAsJsonAsync("/api/quiz/submit", submission);

        Assert.Equal(HttpStatusCode.BadRequest, attack.StatusCode);
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));

        using var owner = await users.UserA.Client.PostAsJsonAsync("/api/quiz/submit", submission);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        var results = await LoadSessionResultsAsync(factory, start.SessionId);
        Assert.Single(results);
        Assert.All(results, result => Assert.Equal(users.UserA.User.User.Id, result.UserId));
    }

    [Fact]
    public async Task DeletedSessionUserWordRejectsEntireSubmissionWithoutChangingSurvivors()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(
            factory,
            user.User.User.Id,
            "stale",
            correctAnswers: 2,
            totalAttempts: 4,
            lastReviewedAt: PreviousReviewUtc,
            lastCorrectAt: PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 2);
        var staleWord = FindSeededWord(start.Questions[0], words);
        var survivingWord = FindSeededWord(start.Questions[1], words);
        var beforeSurvivor = await LoadLearningStateAsync(factory, survivingWord.UserWordId);

        await DeleteUserWordAsync(factory, staleWord.UserWordId);

        using var response = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateCorrectSubmission(start, words));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        var afterSurvivor = await LoadLearningStateAsync(factory, survivingWord.UserWordId);
        AssertLearningStateUnchanged(beforeSurvivor, afterSurvivor);
    }

    [Fact]
    public async Task ValidSubmissionPersistsAndIncrementsOnceWhenSubmittedSequentiallyTwice()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(
            factory,
            user.User.User.Id,
            "sequential",
            correctAnswers: 3,
            totalAttempts: 5,
            lastReviewedAt: PreviousReviewUtc,
            lastCorrectAt: PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 2);
        var submission = CreateCorrectSubmission(start, words);

        using var first = await user.Client.PostAsJsonAsync("/api/quiz/submit", submission);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var afterFirst = await LoadLearningStatesAsync(
            factory,
            start.Questions.Select(question => FindSeededWord(question, words).UserWordId));

        using var duplicate = await user.Client.PostAsJsonAsync("/api/quiz/submit", submission);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var results = await LoadSessionResultsAsync(factory, start.SessionId);
        Assert.Equal(start.QuestionCount, results.Count);
        var afterDuplicate = await LoadLearningStatesAsync(factory, afterFirst.Keys);
        AssertLearningStatesUnchanged(afterFirst, afterDuplicate);
        Assert.All(afterFirst.Values, state =>
            AssertLearningState(state, 4, 6, lastReviewedChanged: true, PreviousCorrectUtc, lastCorrectChanged: true));
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(1, 1, 100d)]
    [InlineData(1, 2, 50d)]
    [InlineData(4, 6, 66.66666666666667d)]
    public async Task VocabularyAccuracyRateUsesPersistedCounters(
        int correctAnswers,
        int totalAttempts,
        double? expectedAccuracy)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var seededWord = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
            factory,
            $"accuracy-{Guid.NewGuid():N}",
            $"Accuracy definition {Guid.NewGuid():N}");
        var userWordId = await IntegrationTestSeeder.SeedUserWordAsync(
            factory,
            user.User.User.Id,
            seededWord,
            correctAnswers: correctAnswers,
            totalAttempts: totalAttempts);

        using var response = await user.Client.GetAsync("/api/words/vocabulary?page=1&pageSize=20");
        var vocabulary = ReadData<UserVocabularyResponseDto>(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(vocabulary.Words, word => word.Id == userWordId);
        if (expectedAccuracy.HasValue)
        {
            Assert.NotNull(item.AccuracyRate);
            Assert.Equal(expectedAccuracy.Value, item.AccuracyRate.Value, precision: 10);
        }
        else
        {
            Assert.Null(item.AccuracyRate);
        }
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
        var wordsA = await SeedQuizVocabularyAsync(factory, users.UserA.User.User.Id, "history-a");
        var wordsB = await SeedQuizVocabularyAsync(factory, users.UserB.User.User.Id, "history-b");
        var sessionA = await StartQuizAsync(users.UserA.Client, 1);
        var sessionB = await StartQuizAsync(users.UserB.Client, 2);
        using var submitA = await users.UserA.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateCorrectSubmission(sessionA, wordsA));
        using var submitB = await users.UserB.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateCorrectSubmission(sessionB, wordsB));
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

    private static async Task<IReadOnlyList<SeededQuizWord>> SeedQuizVocabularyAsync(
        VocabularyAppWebApplicationFactory factory,
        int userId,
        string prefix,
        int correctAnswers = 0,
        int totalAttempts = 0,
        DateTime? lastReviewedAt = null,
        DateTime? lastCorrectAt = null)
    {
        var words = new List<SeededQuizWord>();
        for (var index = 0; index < 4; index++)
        {
            var text = $"{prefix}-{index}-{Guid.NewGuid():N}";
            var definition = $"Definition {prefix} {index} {Guid.NewGuid():N}";
            var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(
                factory,
                text,
                definition);
            var userWordId = await IntegrationTestSeeder.SeedUserWordAsync(
                factory,
                userId,
                word,
                correctAnswers: correctAnswers,
                totalAttempts: totalAttempts,
                lastReviewedAt: lastReviewedAt,
                lastCorrectAt: lastCorrectAt);
            words.Add(new SeededQuizWord(
                text,
                definition,
                userWordId,
                correctAnswers,
                totalAttempts,
                lastReviewedAt,
                lastCorrectAt));
        }

        return words;
    }

    private static async Task<QuizStartResponseDto> StartQuizAsync(HttpClient client, int questionCount)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/quiz/start",
            new StartQuizRequestDto { QuestionCount = questionCount, Mode = "word-to-definition" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ReadData<QuizStartResponseDto>(await response.Content.ReadAsStringAsync());
    }

    private static SeededQuizWord FindSeededWord(
        QuizQuestionDto question,
        IReadOnlyList<SeededQuizWord> words) =>
        words.Single(word =>
            question.Prompt.Contains($"\"{word.Text}\"", StringComparison.Ordinal));

    private static QuizAnswerSubmissionDto CreateAnswer(
        QuizQuestionDto question,
        SeededQuizWord word,
        bool isCorrect)
    {
        var option = isCorrect
            ? question.Options.Single(candidate => candidate.Text == word.Definition)
            : question.Options.First(candidate => candidate.Text != word.Definition);

        return new QuizAnswerSubmissionDto
        {
            QuestionId = question.QuestionId,
            SelectedOptionId = option.OptionId
        };
    }

    private static QuizSubmitRequestDto CreateCorrectSubmission(
        QuizStartResponseDto start,
        IReadOnlyList<SeededQuizWord> words) =>
        new()
        {
            SessionId = start.SessionId,
            Answers = start.Questions
                .Select(question => CreateAnswer(
                    question,
                    FindSeededWord(question, words),
                    isCorrect: true))
                .ToList()
        };

    private static QuizSubmitRequestDto CreateSubmission(
        Guid sessionId,
        params QuizAnswerSubmissionDto[] answers) =>
        new()
        {
            SessionId = sessionId,
            Answers = answers.ToList()
        };

    private static async Task<List<QuizResultSnapshot>> LoadSessionResultsAsync(
        VocabularyAppWebApplicationFactory factory,
        Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.QuizResults
            .AsNoTracking()
            .Where(result => result.QuizSessionId == sessionId)
            .OrderBy(result => result.Id)
            .Select(result => new QuizResultSnapshot(
                result.UserId,
                result.UserWordId,
                result.IsCorrect,
                result.UserAnswer,
                result.AttemptedAt))
            .ToListAsync();
    }

    private static async Task<UserWordLearningState> LoadLearningStateAsync(
        VocabularyAppWebApplicationFactory factory,
        int userWordId)
    {
        var states = await LoadLearningStatesAsync(factory, [userWordId]);
        return states[userWordId];
    }

    private static async Task<IReadOnlyDictionary<int, UserWordLearningState>> LoadLearningStatesAsync(
        VocabularyAppWebApplicationFactory factory,
        IEnumerable<int> userWordIds)
    {
        var ids = userWordIds.Distinct().ToList();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.UserWords
            .AsNoTracking()
            .Where(userWord => ids.Contains(userWord.Id))
            .Select(userWord => new UserWordLearningState(
                userWord.Id,
                userWord.CorrectAnswers,
                userWord.TotalAttempts,
                userWord.LastReviewedAt,
                userWord.LastCorrectAt))
            .ToDictionaryAsync(state => state.UserWordId);
    }

    private static async Task DeleteUserWordAsync(
        VocabularyAppWebApplicationFactory factory,
        int userWordId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userWord = await context.UserWords.SingleAsync(item => item.Id == userWordId);
        context.UserWords.Remove(userWord);
        await context.SaveChangesAsync();
    }

    private static void AssertLearningStatesUnchanged(
        IReadOnlyDictionary<int, UserWordLearningState> before,
        IReadOnlyDictionary<int, UserWordLearningState> after)
    {
        Assert.Equal(before.Keys.OrderBy(id => id), after.Keys.OrderBy(id => id));
        foreach (var (userWordId, previous) in before)
        {
            AssertLearningStateUnchanged(previous, after[userWordId]);
        }
    }

    private static void AssertLearningStateUnchanged(
        UserWordLearningState before,
        UserWordLearningState after) =>
        Assert.Equal(before, after);

    private static void AssertLearningState(
        UserWordLearningState state,
        int expectedCorrectAnswers,
        int expectedTotalAttempts,
        bool lastReviewedChanged,
        DateTime? previousLastCorrectAt,
        bool lastCorrectChanged)
    {
        Assert.Equal(expectedCorrectAnswers, state.CorrectAnswers);
        Assert.Equal(expectedTotalAttempts, state.TotalAttempts);
        if (lastReviewedChanged)
        {
            Assert.NotNull(state.LastReviewedAt);
            Assert.True(state.LastReviewedAt > PreviousReviewUtc);
        }
        else
        {
            Assert.Equal(PreviousReviewUtc, state.LastReviewedAt);
        }

        if (lastCorrectChanged)
        {
            Assert.NotNull(state.LastCorrectAt);
            Assert.True(state.LastCorrectAt > previousLastCorrectAt);
        }
        else
        {
            Assert.Equal(previousLastCorrectAt, state.LastCorrectAt);
        }
    }

    private static T ReadData<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Deserialize<T>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("API response data was null.");
    }

    private sealed record SeededQuizWord(
        string Text,
        string Definition,
        int UserWordId,
        int InitialCorrectAnswers,
        int InitialTotalAttempts,
        DateTime? InitialLastReviewedAt,
        DateTime? InitialLastCorrectAt);

    private sealed record UserWordLearningState(
        int UserWordId,
        int CorrectAnswers,
        int TotalAttempts,
        DateTime? LastReviewedAt,
        DateTime? LastCorrectAt);

    private sealed record QuizResultSnapshot(
        int UserId,
        int UserWordId,
        bool IsCorrect,
        string? UserAnswer,
        DateTime AttemptedAt);
}
