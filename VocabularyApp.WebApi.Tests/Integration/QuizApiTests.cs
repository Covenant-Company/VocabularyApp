using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Services;
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

        Assert.Equal(HttpStatusCode.NotFound, attack.StatusCode);
        await AssertSessionUnavailableAsync(attack);
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
        var remainingIds = words.Where(word => word.UserWordId != staleWord.UserWordId).Select(word => word.UserWordId).ToArray();
        var beforeRemaining = await LoadLearningStatesAsync(factory, remainingIds);

        await DeleteUserWordAsync(factory, staleWord.UserWordId);

        using var response = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateCorrectSubmission(start, words));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.Conflict,
            "quiz_vocabulary_changed", "Your vocabulary changed. Please start a new quiz.");
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        var afterSurvivor = await LoadLearningStateAsync(factory, survivingWord.UserWordId);
        AssertLearningStateUnchanged(beforeSurvivor, afterSurvivor);
        AssertLearningStatesUnchanged(beforeRemaining, await LoadLearningStatesAsync(factory, remainingIds));
        using var repeated = await user.Client.PostAsJsonAsync("/api/quiz/submit", CreateCorrectSubmission(start, words));
        await ApiErrorContractAssert.ApplicationAsync(repeated, HttpStatusCode.Conflict,
            "quiz_vocabulary_changed", "Your vocabulary changed. Please start a new quiz.");
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        AssertLearningStatesUnchanged(beforeRemaining, await LoadLearningStatesAsync(factory, remainingIds));
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

        Assert.Equal(HttpStatusCode.NotFound, duplicate.StatusCode);
        await AssertSessionUnavailableAsync(duplicate);
        var results = await LoadSessionResultsAsync(factory, start.SessionId);
        Assert.Equal(start.QuestionCount, results.Count);
        var afterDuplicate = await LoadLearningStatesAsync(factory, afterFirst.Keys);
        AssertLearningStatesUnchanged(afterFirst, afterDuplicate);
        Assert.All(afterFirst.Values, state =>
            AssertLearningState(state, 4, 6, lastReviewedChanged: true, PreviousCorrectUtc, lastCorrectChanged: true));
    }

    [Fact]
    public async Task PersistenceFailureRollsBackResultsAndLearningStateAndSessionRemainsRetryable()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(
            factory,
            user.User.User.Id,
            "rollback",
            correctAnswers: 3,
            totalAttempts: 5,
            lastReviewedAt: PreviousReviewUtc,
            lastCorrectAt: PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 2);
        var submission = CreateCorrectSubmission(start, words);
        var affectedIds = start.Questions
            .Select(question => FindSeededWord(question, words).UserWordId)
            .ToList();
        var before = await LoadLearningStatesAsync(factory, affectedIds);

        factory.QuizPersistenceFailure.Arm();
        try
        {
            using var failed = await user.Client.PostAsJsonAsync("/api/quiz/submit", submission);

            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            await ApiErrorContractAssert.InternalAsync(failed, user.User.Token);
            Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
            AssertLearningStatesUnchanged(
                before,
                await LoadLearningStatesAsync(factory, affectedIds));
        }
        finally
        {
            factory.QuizPersistenceFailure.Disarm();
        }

        using var retry = await user.Client.PostAsJsonAsync("/api/quiz/submit", submission);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var results = await LoadSessionResultsAsync(factory, start.SessionId);
        Assert.Equal(start.QuestionCount, results.Count);
        var afterRetry = await LoadLearningStatesAsync(factory, affectedIds);
        Assert.All(afterRetry.Values, state =>
            AssertLearningState(
                state,
                expectedCorrectAnswers: 4,
                expectedTotalAttempts: 6,
                lastReviewedChanged: true,
                PreviousCorrectUtc,
                lastCorrectChanged: true));
        Assert.All(results, result =>
        {
            Assert.True(result.IsCorrect);
            Assert.Equal(result.AttemptedAt, afterRetry[result.UserWordId].LastReviewedAt);
            Assert.Equal(result.AttemptedAt, afterRetry[result.UserWordId].LastCorrectAt);
        });
    }

    [Fact]
    public async Task ConcurrentSubmissionsPersistOneLogicalResultAndAggregateUpdate()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(
            factory,
            user.User.User.Id,
            "concurrent",
            correctAnswers: 3,
            totalAttempts: 5,
            lastReviewedAt: PreviousReviewUtc,
            lastCorrectAt: PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 2);
        var submission = CreateCorrectSubmission(start, words);
        var affectedIds = start.Questions
            .Select(question => FindSeededWord(question, words).UserWordId)
            .ToList();

        factory.QuizSubmissionSynchronization.Arm();
        var firstSubmission = user.Client.PostAsJsonAsync("/api/quiz/submit", submission);
        HttpResponseMessage? secondResponse = null;
        HttpResponseMessage? firstResponse = null;
        try
        {
            await factory.QuizSubmissionSynchronization
                .WaitUntilBlockedAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));

            secondResponse = await user.Client
                .PostAsJsonAsync("/api/quiz/submit", submission)
                .WaitAsync(TimeSpan.FromSeconds(5));

            factory.QuizSubmissionSynchronization.Release();
            firstResponse = await firstSubmission.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
            await ApiErrorContractAssert.ApplicationAsync(secondResponse, HttpStatusCode.Conflict,
                "quiz_submission_conflict", "This quiz submission is already being processed or has been submitted.");
        }
        finally
        {
            factory.QuizSubmissionSynchronization.Release();
            firstResponse?.Dispose();
            secondResponse?.Dispose();
        }

        var results = await LoadSessionResultsAsync(factory, start.SessionId);
        Assert.Equal(start.QuestionCount, results.Count);
        Assert.Equal(
            start.QuestionCount,
            results.Select(result => result.UserWordId).Distinct().Count());

        var after = await LoadLearningStatesAsync(factory, affectedIds);
        Assert.All(after.Values, state =>
            AssertLearningState(
                state,
                expectedCorrectAnswers: 4,
                expectedTotalAttempts: 6,
                lastReviewedChanged: true,
                PreviousCorrectUtc,
                lastCorrectChanged: true));
        Assert.All(results, result =>
        {
            Assert.Equal(result.AttemptedAt, after[result.UserWordId].LastReviewedAt);
            Assert.Equal(result.AttemptedAt, after[result.UserWordId].LastCorrectAt);
        });
    }

    [Fact]
    public async Task QuizResultSubmissionUniquenessRejectsDuplicateDatabaseRow()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "unique-index");
        var start = await StartQuizAsync(user.Client, 1);

        using var submissionResponse = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            CreateCorrectSubmission(start, words));
        Assert.Equal(HttpStatusCode.OK, submissionResponse.StatusCode);

        var persisted = Assert.Single(await LoadSessionResultsAsync(factory, start.SessionId));
        var before = await LoadLearningStateAsync(factory, persisted.UserWordId);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.QuizResults.Add(new()
            {
                UserId = persisted.UserId,
                UserWordId = persisted.UserWordId,
                QuizSessionId = start.SessionId,
                QuizType = VocabularyApp.Data.Models.QuizType.Definition,
                IsCorrect = persisted.IsCorrect,
                UserAnswer = persisted.UserAnswer,
                CorrectAnswer = "Duplicate index test answer",
                ResponseTimeSeconds = 0,
                AttemptedAt = persisted.AttemptedAt
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        Assert.Single(await LoadSessionResultsAsync(factory, start.SessionId));
        AssertLearningStateUnchanged(
            before,
            await LoadLearningStateAsync(factory, persisted.UserWordId));
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
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "unknown-session",
            2, 5, PreviousReviewUtc, PreviousCorrectUtc);
        var before = await LoadLearningStatesAsync(factory, words.Select(word => word.UserWordId));

        using var response = await user.Client.PostAsJsonAsync(
            "/api/quiz/submit",
            new QuizSubmitRequestDto { SessionId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertSessionUnavailableAsync(response);
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));
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

    [Theory]
    [InlineData("omitted-option", false)]
    [InlineData("null-option", false)]
    [InlineData("string-option", false)]
    [InlineData("fractional-option", false)]
    [InlineData("object-option", false)]
    [InlineData("unknown-option", true)]
    [InlineData("malformed-question", false)]
    [InlineData("null-answers", false)]
    [InlineData("null-element", true)]
    [InlineData("mixed-null-element", true)]
    public async Task InvalidRawAnswerStructuresDoNotMutateAndPermitValidRetry(string kind, bool applicationError)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "raw-invalid",
            2, 5, PreviousReviewUtc, PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 2);
        var before = await LoadLearningStatesAsync(factory, words.Select(word => word.UserWordId));
        var answer = new Dictionary<string, object?> { ["questionId"] = start.Questions[0].QuestionId };
        if (kind != "omitted-option")
            answer["selectedOptionId"] = kind switch
            {
                "null-option" => null,
                "string-option" => ApiErrorContractAssert.Sentinel,
                "fractional-option" => 0.5,
                "object-option" => new { value = 0 },
                "unknown-option" => 999,
                _ => 0
            };
        if (kind == "malformed-question") answer["questionId"] = ApiErrorContractAssert.Sentinel;
        object? answers = kind switch
        {
            "null-answers" => null,
            "null-element" => new object?[] { null },
            "mixed-null-element" => new object?[] { answer, null },
            _ => new object[] { answer }
        };
        using var content = new StringContent(JsonSerializer.Serialize(new { sessionId = start.SessionId, answers }),
            Encoding.UTF8, "application/json");
        using var response = await user.Client.PostAsync("/api/quiz/submit", content);
        if (applicationError)
            await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.BadRequest,
                "invalid_quiz_answers", "The quiz answers are invalid.");
        else
            await AssertQuizValidationAsync(response);
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));

        using var retry = await user.Client.PostAsJsonAsync("/api/quiz/submit", CreateCorrectSubmission(start, words));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(2, (await LoadSessionResultsAsync(factory, start.SessionId)).Count);
        var after = await LoadLearningStatesAsync(factory, before.Keys);
        foreach (var question in start.Questions)
        {
            var id = FindSeededWord(question, words).UserWordId;
            AssertLearningState(after[id], 3, 6, true, PreviousCorrectUtc, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrEmptyAnswersAreValidUnansweredSubmissions(bool includeAnswers)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "empty-answers",
            2, 5, PreviousReviewUtc, PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 2);
        var payload = new Dictionary<string, object?> { ["sessionId"] = start.SessionId };
        if (includeAnswers) payload["answers"] = Array.Empty<object>();
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await user.Client.PostAsync("/api/quiz/submit", content);
        using var json = await JsonContractAssert.ReadSuccessAsync(response);
        var data = JsonContractAssert.SuccessData(json.RootElement);
        JsonContractAssert.Properties(data, "totalQuestions", "correctAnswers", "scorePercentage", "questionResults");
        Assert.Equal(2, data.GetProperty("totalQuestions").GetInt32());
        Assert.Equal(0, data.GetProperty("correctAnswers").GetInt32());
        Assert.Equal(0, data.GetProperty("scorePercentage").GetDouble());
        Assert.Equal(2, data.GetProperty("questionResults").GetArrayLength());
        foreach (var result in data.GetProperty("questionResults").EnumerateArray())
        {
            JsonContractAssert.Properties(result, "questionId", "questionType", "prompt", "selectedAnswer", "correctAnswer", "isCorrect");
            JsonContractAssert.Property(result, "selectedAnswer", JsonValueKind.Null);
            JsonContractAssert.Property(result, "isCorrect", JsonValueKind.False);
        }
        var persisted = await LoadSessionResultsAsync(factory, start.SessionId);
        Assert.Equal(2, persisted.Count);
        Assert.All(persisted, result => { Assert.False(result.IsCorrect); Assert.Null(result.UserAnswer); });
        var states = await LoadLearningStatesAsync(factory, words.Select(word => word.UserWordId));
        foreach (var question in start.Questions)
            AssertLearningState(states[FindSeededWord(question, words).UserWordId], 2, 6, true, PreviousCorrectUtc, false);
        Assert.All(persisted, result => Assert.Equal(result.AttemptedAt, states[result.UserWordId].LastReviewedAt));
    }

    [Fact]
    public async Task ExplicitRawOptionZeroIsAcceptedAndScoredNormally()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "option-zero");
        var start = await StartQuizAsync(user.Client, 1);
        var question = Assert.Single(start.Questions);
        var selected = question.Options.Single(option => option.OptionId == 0);
        var correct = selected.Text == FindSeededWord(question, words).Definition;
        using var content = new StringContent(JsonSerializer.Serialize(new
        {
            sessionId = start.SessionId,
            answers = new[] { new { questionId = question.QuestionId, selectedOptionId = 0 } }
        }), Encoding.UTF8, "application/json");
        using var response = await user.Client.PostAsync("/api/quiz/submit", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = ReadData<QuizSubmitResponseDto>(await response.Content.ReadAsStringAsync());
        Assert.Equal(selected.Text, Assert.Single(result.QuestionResults).SelectedAnswer);
        Assert.Equal(correct ? 1 : 0, result.CorrectAnswers);
        var persisted = Assert.Single(await LoadSessionResultsAsync(factory, start.SessionId));
        Assert.Equal(correct, persisted.IsCorrect);
        Assert.Equal(selected.Text, persisted.UserAnswer);
        var state = await LoadLearningStateAsync(factory, persisted.UserWordId);
        Assert.Equal(1, state.TotalAttempts);
        Assert.Equal(correct ? 1 : 0, state.CorrectAnswers);
        Assert.NotNull(state.LastReviewedAt);
        Assert.Equal(correct, state.LastCorrectAt.HasValue);
    }

    [Fact]
    public async Task ExpiredSessionIsConcealedAndDoesNotMutate()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "expiry",
            2, 5, PreviousReviewUtc, PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 2);
        var before = await LoadLearningStatesAsync(factory, words.Select(word => word.UserWordId));
        // Test-local reflection controls the existing timestamp; no production expiry hook or sleep.
        var sessions = Assert.IsAssignableFrom<IDictionary>(typeof(QuizService)
            .GetField("QuizSessions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        var state = sessions[start.SessionId]!;
        state.GetType().GetProperty("ExpiresAtUtc")!.SetValue(state, DateTime.UtcNow.AddMinutes(-1));
        var submission = CreateCorrectSubmission(start, words);
        using var expired = await user.Client.PostAsJsonAsync("/api/quiz/submit", submission);
        await AssertSessionUnavailableAsync(expired);
        using var removed = await user.Client.PostAsJsonAsync("/api/quiz/submit", submission);
        await AssertSessionUnavailableAsync(removed);
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));
        var replacement = await StartQuizAsync(user.Client, 1);
        using var success = await user.Client.PostAsJsonAsync("/api/quiz/submit", CreateCorrectSubmission(replacement, words));
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
    }

    [Fact]
    public async Task PersistedDuplicateReturnsConflictAndRollsBackAllAttemptedChanges()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "persisted-duplicate",
            2, 5, PreviousReviewUtc, PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 2);
        var word = FindSeededWord(start.Questions[0], words);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.QuizResults.Add(new QuizResult
            {
                UserId = user.User.User.Id, UserWordId = word.UserWordId, QuizSessionId = start.SessionId,
                QuizType = QuizType.Definition, IsCorrect = true, UserAnswer = word.Definition,
                CorrectAnswer = word.Definition, AttemptedAt = PreviousReviewUtc
            });
            await db.SaveChangesAsync();
        }
        var before = await LoadLearningStatesAsync(factory, words.Select(item => item.UserWordId));
        var resultsBefore = await LoadSessionResultsAsync(factory, start.SessionId);
        using var response = await user.Client.PostAsJsonAsync("/api/quiz/submit", CreateCorrectSubmission(start, words));
        await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.Conflict,
            "quiz_submission_conflict", "This quiz submission is already being processed or has been submitted.");
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));
        Assert.Equal(resultsBefore, await LoadSessionResultsAsync(factory, start.SessionId));
        using var repeat = await user.Client.PostAsJsonAsync("/api/quiz/submit", CreateCorrectSubmission(start, words));
        await AssertSessionUnavailableAsync(repeat);
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("?take=0", 5)]
    [InlineData("?take=-1", 5)]
    [InlineData("?take=1", 1)]
    [InlineData("?take=20", 20)]
    [InlineData("?take=100", 20)]
    public async Task HistoryPreservesDefaultBoundsOrderingAndFields(string query, int expectedCount)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "history-bounds");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < 21; index++)
                db.QuizResults.Add(new QuizResult
                {
                    UserId = user.User.User.Id, UserWordId = words[0].UserWordId, QuizSessionId = Guid.NewGuid(),
                    QuizType = QuizType.Definition, IsCorrect = index % 2 == 0,
                    UserAnswer = index % 2 == 0 ? words[0].Definition : null,
                    CorrectAnswer = words[0].Definition, AttemptedAt = PreviousReviewUtc.AddMinutes(index)
                });
            await db.SaveChangesAsync();
        }
        using var response = await user.Client.GetAsync("/api/quiz/history" + query);
        using var json = await JsonContractAssert.ReadSuccessAsync(response);
        var data = JsonContractAssert.SuccessData(json.RootElement);
        JsonContractAssert.Properties(data, "items");
        var items = data.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(expectedCount, items.Length);
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            JsonContractAssert.Properties(item, "attemptedAtUtc", "totalQuestions", "correctAnswers", "scorePercentage");
            Assert.Equal(PreviousReviewUtc.AddMinutes(20 - index), item.GetProperty("attemptedAtUtc").GetDateTime());
            Assert.Equal(1, item.GetProperty("totalQuestions").GetInt32());
            Assert.Equal(index % 2 == 0 ? 1 : 0, item.GetProperty("correctAnswers").GetInt32());
            Assert.Equal(index % 2 == 0 ? 100 : 0, item.GetProperty("scorePercentage").GetDouble());
        }
    }

    [Fact]
    public async Task EmptyHistoryIsACompleteSuccessfulEmptyCollection()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var response = await user.Client.GetAsync("/api/quiz/history");
        using var json = await JsonContractAssert.ReadSuccessAsync(response);
        var data = JsonContractAssert.SuccessData(json.RootElement);
        JsonContractAssert.Properties(data, "items");
        JsonContractAssert.Property(data, "items", JsonValueKind.Array);
        Assert.Empty(data.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UnknownModesRemainMixedAndSessionLifetimeRemainsThirtyMinutes(string mode)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        await SeedQuizVocabularyAsync(factory, user.User.User.Id, "mode-fallback");
        var before = DateTime.UtcNow;
        using var response = await user.Client.PostAsJsonAsync("/api/quiz/start", new StartQuizRequestDto
        { QuestionCount = 1, Mode = mode });
        var after = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var start = ReadData<QuizStartResponseDto>(await response.Content.ReadAsStringAsync());
        Assert.Equal("mixed", start.Mode);
        Assert.InRange(start.ExpiresAtUtc, before.AddMinutes(30), after.AddMinutes(30));
        Assert.Equal(1, start.QuestionCount);
        Assert.Single(start.Questions);
    }

    [Fact]
    public async Task VocabularyMissingAtTransactionalRecheckReturnsConflictAndReleasesLock()
    {
        var recheck = new MissingQuizVocabularyAtRecheck();
        using var factory = new VocabularyAppWebApplicationFactory
        { AdditionalInterceptors = [recheck] };
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "transactional-recheck",
            2, 5, PreviousReviewUtc, PreviousCorrectUtc);
        var start = await StartQuizAsync(user.Client, 2);
        var before = await LoadLearningStatesAsync(factory, words.Select(word => word.UserWordId));
        recheck.Arm();
        using var response = await user.Client.PostAsJsonAsync("/api/quiz/submit", CreateCorrectSubmission(start, words));
        await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.Conflict,
            "quiz_vocabulary_changed", "Your vocabulary changed. Please start a new quiz.");
        Assert.True(recheck.Triggered);
        Assert.Empty(await LoadSessionResultsAsync(factory, start.SessionId));
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));
        // The injected missing read is one-shot; a valid retry proves lock/tracker recovery.
        using var retry = await user.Client.PostAsJsonAsync("/api/quiz/submit", CreateCorrectSubmission(start, words));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(2, (await LoadSessionResultsAsync(factory, start.SessionId)).Count);
        var after = await LoadLearningStatesAsync(factory, before.Keys);
        foreach (var question in start.Questions)
            AssertLearningState(after[FindSeededWord(question, words).UserWordId], 3, 6, true, PreviousCorrectUtc, true);
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("empty-guid", false)]
    [InlineData("malformed", true)]
    public async Task InvalidSessionIdentifiersRemainSafeBadRequestsWithoutMutation(string kind, bool bindingFailure)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "invalid-session",
            2, 5, PreviousReviewUtc, PreviousCorrectUtc);
        var before = await LoadLearningStatesAsync(factory, words.Select(word => word.UserWordId));
        var payload = new Dictionary<string, object?> { ["answers"] = Array.Empty<object>() };
        if (kind != "missing") payload["sessionId"] = kind == "empty-guid" ? Guid.Empty.ToString() : ApiErrorContractAssert.Sentinel;
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await user.Client.PostAsync("/api/quiz/submit", content);
        if (bindingFailure) await AssertQuizValidationAsync(response);
        else await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.BadRequest, "invalid_request", "The request is invalid.");
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().QuizResults.ToListAsync());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"mode\":null}")]
    public async Task NullStartBodyOrModePreservesImplicitRequiredBinding(string payload)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var user = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var words = await SeedQuizVocabularyAsync(factory, user.User.User.Id, "null-start",
            2, 5, PreviousReviewUtc, PreviousCorrectUtc);
        var before = await LoadLearningStatesAsync(factory, words.Select(word => word.UserWordId));
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await user.Client.PostAsync("/api/quiz/start", content);
        await AssertQuizValidationAsync(response);
        AssertLearningStatesUnchanged(before, await LoadLearningStatesAsync(factory, before.Keys));
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().QuizResults.ToListAsync());
    }

    private sealed class MissingQuizVocabularyAtRecheck : DbCommandInterceptor
    {
        private int remainingReads;
        public bool Triggered { get; private set; }
        public void Arm() => remainingReads = 2;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (remainingReads > 0 && command.CommandText.Contains("FROM \"UserWords\"", StringComparison.Ordinal)
                && --remainingReads == 0)
            {
                // Preserve the relational reader/schema while making the second owned-word read empty.
                command.CommandText = $"SELECT * FROM ({command.CommandText}) AS quiz_recheck WHERE 0 = 1";
                Triggered = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    private static Task AssertSessionUnavailableAsync(HttpResponseMessage response) =>
        ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.NotFound,
            "quiz_session_unavailable", "This quiz session is unavailable. Please start a new quiz.");

    private static async Task AssertQuizValidationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        JsonContractAssert.Properties(root, "type", "title", "status", "errors", "traceId", "success", "data", "error", "message", "code");
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.Equal("validation_failed", root.GetProperty("code").GetString());
        Assert.Equal("One or more request fields are invalid.", root.GetProperty("error").GetString());
        Assert.Equal(root.GetProperty("error").GetString(), root.GetProperty("message").GetString());
        JsonContractAssert.Property(root, "success", JsonValueKind.False);
        JsonContractAssert.Property(root, "data", JsonValueKind.Null);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        var fields = root.GetProperty("errors").EnumerateObject().ToArray();
        Assert.NotEmpty(fields);
        foreach (var field in fields)
        {
            Assert.False(string.IsNullOrWhiteSpace(field.Name));
            Assert.Equal(JsonValueKind.Array, field.Value.ValueKind);
            Assert.NotEmpty(field.Value.EnumerateArray());
            foreach (var error in field.Value.EnumerateArray())
                Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
        }
        Assert.DoesNotContain(ApiErrorContractAssert.Sentinel, raw);
        Assert.DoesNotContain("Exception", raw);
        Assert.DoesNotContain("System.", raw);
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
