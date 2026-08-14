using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Services
{
  public class QuizService : IQuizService
  {
    private static readonly ConcurrentDictionary<Guid, QuizSessionState> QuizSessions = new();
    private readonly ApplicationDbContext _db;
    private readonly ILogger<QuizService> _logger;

    public QuizService(ApplicationDbContext db, ILogger<QuizService> logger)
    {
      _db = db;
      _logger = logger;
    }

    public async Task<ServiceResult<QuizStartResponseDto>> StartQuizAsync(int userId, StartQuizRequestDto request)
    {
      try
      {
        var questionCount = Math.Clamp(request.QuestionCount <= 0 ? 10 : request.QuestionCount, 1, 20);
        var mode = NormalizeMode(request.Mode);

        var vocabularyEntries = await _db.UserWords
            .Include(uw => uw.Word)
                .ThenInclude(w => w.WordDefinitions)
            .Where(uw => uw.UserId == userId)
            .Select(uw => new
            {
              UserWordId = uw.Id,
              WordId = uw.WordId,
              Word = uw.Word.Text,
              Definition = uw.Word.WordDefinitions
              .Where(wd => wd.PartOfSpeechId == uw.PartOfSpeechId && uw.PreferredWordDefinitionId.HasValue && wd.Id == uw.PreferredWordDefinitionId.Value)
              .Select(wd => wd.Definition)
              .FirstOrDefault()
              ?? uw.Word.WordDefinitions
                .Where(wd => wd.PartOfSpeechId == uw.PartOfSpeechId)
                .OrderBy(wd => wd.DisplayOrder)
                .Select(wd => wd.Definition)
                .FirstOrDefault()
            })
            .ToListAsync();

        var uniqueEntries = vocabularyEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Word) && !string.IsNullOrWhiteSpace(entry.Definition))
            .GroupBy(entry => entry.WordId)
            .Select(group => new QuizVocabularyEntry
            {
              UserWordId = group.First().UserWordId,
              Word = group.First().Word.Trim(),
              Definition = group.First().Definition!.Trim()
            })
            .ToList();

        if (uniqueEntries.Count < 4)
        {
          return ServiceResult<QuizStartResponseDto>.Failure("You need at least 4 saved words with definitions to start a quiz.");
        }

        var selectedWords = Shuffle(uniqueEntries).Take(Math.Min(questionCount, uniqueEntries.Count)).ToList();
        var questions = new List<QuizQuestionState>();

        foreach (var selectedWord in selectedWords)
        {
          var questionType = ResolveQuestionType(mode);
          var distractors = Shuffle(uniqueEntries.Where(item => item.Word != selectedWord.Word)).Take(3).ToList();

          if (distractors.Count < 3)
          {
            continue;
          }

          var optionTexts = questionType == "word-to-definition"
              ? distractors.Select(item => item.Definition).Append(selectedWord.Definition).ToList()
              : distractors.Select(item => item.Word).Append(selectedWord.Word).ToList();

          optionTexts = Shuffle(optionTexts);
          var options = optionTexts
              .Select((optionText, index) => new QuizOptionDto { OptionId = index, Text = optionText })
              .ToList();

          var correctAnswerText = questionType == "word-to-definition" ? selectedWord.Definition : selectedWord.Word;
          var correctOption = options.First(option => option.Text == correctAnswerText);

          questions.Add(new QuizQuestionState
          {
            QuestionId = Guid.NewGuid(),
            UserWordId = selectedWord.UserWordId,
            QuestionType = questionType,
            Prompt = questionType == "word-to-definition"
                  ? $"Choose the correct definition for \"{selectedWord.Word}\""
                  : $"Choose the correct word for this definition: \"{selectedWord.Definition}\"",
            Options = options,
            CorrectOptionId = correctOption.OptionId
          });
        }

        if (questions.Count == 0)
        {
          return ServiceResult<QuizStartResponseDto>.Failure("Unable to generate quiz questions from your current vocabulary.");
        }

        var sessionId = Guid.NewGuid();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(30);
        QuizSessions[sessionId] = new QuizSessionState
        {
          SessionId = sessionId,
          UserId = userId,
          ExpiresAtUtc = expiresAtUtc,
          Questions = questions
        };

        CleanupExpiredQuizSessions();

        var response = new QuizStartResponseDto
        {
          SessionId = sessionId,
          Mode = mode,
          QuestionCount = questions.Count,
          ExpiresAtUtc = expiresAtUtc,
          Questions = questions.Select(question => new QuizQuestionDto
          {
            QuestionId = question.QuestionId,
            QuestionType = question.QuestionType,
            Prompt = question.Prompt,
            Options = question.Options
          }).ToList()
        };

        return ServiceResult<QuizStartResponseDto>.Success(response);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error starting quiz for user {UserId}", userId);
        return ServiceResult<QuizStartResponseDto>.Failure("Failed to start quiz.");
      }
    }

    public async Task<ServiceResult<QuizSubmitResponseDto>> SubmitQuizAsync(int userId, QuizSubmitRequestDto request)
    {
      if (request.SessionId == Guid.Empty)
      {
        return ServiceResult<QuizSubmitResponseDto>.Failure("SessionId is required.");
      }

      if (!QuizSessions.TryGetValue(request.SessionId, out var session))
      {
        return ServiceResult<QuizSubmitResponseDto>.Failure("Quiz session not found or expired.");
      }

      if (session.UserId != userId)
      {
        return ServiceResult<QuizSubmitResponseDto>.Failure("You are not authorized for this quiz session.");
      }

      if (session.ExpiresAtUtc < DateTime.UtcNow)
      {
        QuizSessions.TryRemove(request.SessionId, out _);
        return ServiceResult<QuizSubmitResponseDto>.Failure("Quiz session has expired. Please start a new quiz.");
      }

      try
      {
        if (request.Answers == null)
        {
          return ServiceResult<QuizSubmitResponseDto>.Failure("Answers are required.");
        }

        var duplicateQuestionId = request.Answers
            .GroupBy(answer => answer.QuestionId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateQuestionId != null)
        {
          return ServiceResult<QuizSubmitResponseDto>.Failure("Each quiz question may only be answered once.");
        }

        var sessionQuestionLookup = session.Questions
            .ToDictionary(question => question.QuestionId);

        foreach (var answer in request.Answers)
        {
          if (!sessionQuestionLookup.TryGetValue(answer.QuestionId, out var question))
          {
            return ServiceResult<QuizSubmitResponseDto>.Failure("An answer does not belong to this quiz session.");
          }

          if (!question.Options.Any(option => option.OptionId == answer.SelectedOptionId))
          {
            return ServiceResult<QuizSubmitResponseDto>.Failure("An answer contains an invalid option.");
          }
        }

        var requiredUserWordIds = session.Questions
            .Select(question => question.UserWordId)
            .Distinct()
            .ToList();
        var ownedUserWords = await _db.UserWords
            .Where(uw => requiredUserWordIds.Contains(uw.Id) && uw.UserId == userId)
            .ToListAsync();
        var ownedUserWordIds = ownedUserWords
            .Select(uw => uw.Id)
            .ToHashSet();

        if (ownedUserWordIds.Count != requiredUserWordIds.Count ||
            requiredUserWordIds.Any(id => !ownedUserWordIds.Contains(id)))
        {
          return ServiceResult<QuizSubmitResponseDto>.Failure("Quiz vocabulary is no longer available.");
        }

        var answerLookup = request.Answers
            .ToDictionary(answer => answer.QuestionId, answer => answer.SelectedOptionId);

        var questionResults = new List<QuizQuestionResultDto>();
        var correctAnswers = 0;

        foreach (var question in session.Questions)
        {
          var hasAnswer = answerLookup.TryGetValue(question.QuestionId, out var selectedOptionId);
          var selectedOption = hasAnswer
              ? question.Options.FirstOrDefault(option => option.OptionId == selectedOptionId)
              : null;
          var correctOption = question.Options.First(option => option.OptionId == question.CorrectOptionId);
          var isCorrect = hasAnswer && selectedOptionId == question.CorrectOptionId;

          if (isCorrect)
          {
            correctAnswers++;
          }

          questionResults.Add(new QuizQuestionResultDto
          {
            QuestionId = question.QuestionId,
            QuestionType = question.QuestionType,
            Prompt = question.Prompt,
            CorrectAnswer = correctOption.Text,
            SelectedAnswer = selectedOption?.Text,
            IsCorrect = isCorrect
          });
        }

        var totalQuestions = session.Questions.Count;
        var scorePercentage = totalQuestions > 0
            ? Math.Round((double)correctAnswers / totalQuestions * 100, 2)
            : 0;

        var response = new QuizSubmitResponseDto
        {
          TotalQuestions = totalQuestions,
          CorrectAnswers = correctAnswers,
          ScorePercentage = scorePercentage,
          QuestionResults = questionResults
        };

        var attemptedAtUtc = DateTime.UtcNow;
        var persistedResults = new List<QuizResult>();

        foreach (var question in session.Questions)
        {
          var hasAnswer = answerLookup.TryGetValue(question.QuestionId, out var selectedOptionId);
          var selectedOption = hasAnswer
              ? question.Options.FirstOrDefault(option => option.OptionId == selectedOptionId)
              : null;
          var correctOption = question.Options.First(option => option.OptionId == question.CorrectOptionId);

          persistedResults.Add(new QuizResult
          {
            UserId = userId,
            UserWordId = question.UserWordId,
            QuizSessionId = request.SessionId,
            QuizType = QuizType.Definition,
            IsCorrect = hasAnswer && selectedOptionId == question.CorrectOptionId,
            UserAnswer = selectedOption?.Text,
            CorrectAnswer = correctOption.Text,
            ResponseTimeSeconds = 0,
            AttemptedAt = attemptedAtUtc
          });
        }

        if (persistedResults.Count > 0)
        {
          _db.QuizResults.AddRange(persistedResults);
          await _db.SaveChangesAsync();
        }

        QuizSessions.TryRemove(request.SessionId, out _);
        return ServiceResult<QuizSubmitResponseDto>.Success(response);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error submitting quiz for user {UserId}", userId);
        return ServiceResult<QuizSubmitResponseDto>.Failure("Failed to submit quiz.");
      }
    }

    public async Task<ServiceResult<QuizHistoryResponseDto>> GetRecentQuizHistoryAsync(int userId, int take = 5)
    {
      try
      {
        var normalizedTake = Math.Clamp(take <= 0 ? 5 : take, 1, 20);

        var groupedResults = await _db.QuizResults
            .Where(qr => qr.UserId == userId)
            .GroupBy(qr => qr.QuizSessionId)
            .OrderByDescending(group => group.Max(item => item.AttemptedAt))
            .Take(normalizedTake)
            .Select(group => new QuizHistoryItemDto
            {
              AttemptedAtUtc = group.Max(item => item.AttemptedAt),
              TotalQuestions = group.Count(),
              CorrectAnswers = group.Count(item => item.IsCorrect),
              ScorePercentage = group.Count() > 0
                    ? Math.Round((double)group.Count(item => item.IsCorrect) / group.Count() * 100, 2)
                    : 0
            })
            .ToListAsync();

        return ServiceResult<QuizHistoryResponseDto>.Success(new QuizHistoryResponseDto
        {
          Items = groupedResults
        });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error retrieving quiz history for user {UserId}", userId);
        return ServiceResult<QuizHistoryResponseDto>.Failure("Failed to retrieve quiz history.");
      }
    }

    private static string NormalizeMode(string? mode)
    {
      if (string.IsNullOrWhiteSpace(mode))
      {
        return "mixed";
      }

      var normalized = mode.Trim().ToLowerInvariant();
      return normalized is "word-to-definition" or "definition-to-word" ? normalized : "mixed";
    }

    private static string ResolveQuestionType(string mode)
    {
      if (mode == "mixed")
      {
        return Random.Shared.Next(0, 2) == 0 ? "word-to-definition" : "definition-to-word";
      }

      return mode;
    }

    private static List<T> Shuffle<T>(IEnumerable<T> source)
    {
      return source.OrderBy(_ => Random.Shared.Next()).ToList();
    }

    private static void CleanupExpiredQuizSessions()
    {
      var now = DateTime.UtcNow;
      var expiredSessionIds = QuizSessions
          .Where(session => session.Value.ExpiresAtUtc < now)
          .Select(session => session.Key)
          .ToList();

      foreach (var expiredSessionId in expiredSessionIds)
      {
        QuizSessions.TryRemove(expiredSessionId, out _);
      }
    }

    internal static void ClearQuizSessionsForTesting() => QuizSessions.Clear();

    private class QuizVocabularyEntry
    {
      public int UserWordId { get; set; }
      public string Word { get; set; } = string.Empty;
      public string Definition { get; set; } = string.Empty;
    }

    private class QuizQuestionState
    {
      public Guid QuestionId { get; set; }
      public int UserWordId { get; set; }
      public string QuestionType { get; set; } = string.Empty;
      public string Prompt { get; set; } = string.Empty;
      public List<QuizOptionDto> Options { get; set; } = new();
      public int CorrectOptionId { get; set; }
    }

    private class QuizSessionState
    {
      public Guid SessionId { get; set; }
      public int UserId { get; set; }
      public DateTime ExpiresAtUtc { get; set; }
      public List<QuizQuestionState> Questions { get; set; } = new();
    }
  }
}
