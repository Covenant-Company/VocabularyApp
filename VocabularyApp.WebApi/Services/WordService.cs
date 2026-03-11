using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.DTOs.External;
using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Services
{
    public class WordService : IWordService
    {
        private static readonly ConcurrentDictionary<Guid, QuizSessionState> QuizSessions = new();
        private readonly ApplicationDbContext _db;
        private readonly HttpClient _http;
        private readonly ILogger<WordService> _logger;

        public WordService(ApplicationDbContext db, HttpClient http, ILogger<WordService> logger)
        {
            _db = db;
            _http = http;
            _logger = logger;
        }

        public async Task<ServiceResult<object>> LookupWordAsync(string term, int? userId = null)
        {
            if (string.IsNullOrWhiteSpace(term))
                return ServiceResult<object>.Failure("Word is required.");

            var normalized = term.Trim();
            bool isInUserVocabulary = false;

            try
            {
                // 1) Try local canonical dictionary first
                var word = await _db.Words
                    .Include(w => w.WordDefinitions)
                        .ThenInclude(d => d.PartOfSpeech)
                    .FirstOrDefaultAsync(w => w.Text == normalized);

                if (word != null)
                {
                    var dto = MapToDto(word);

                    // Check if word is in user's vocabulary
                    if (userId.HasValue)
                    {
                        isInUserVocabulary = await _db.UserWords
                            .AnyAsync(uw => uw.UserId == userId.Value && uw.WordId == word.Id);
                    }

                    var resp = new WordLookupResponse
                    {
                        Success = true,
                        Word = dto,
                        WasFoundInCache = true,
                        IsInUserVocabulary = isInUserVocabulary
                    };
                    return ServiceResult<object>.Success(resp);
                }

                // 2) Fetch from external dictionary API and persist
                var apiUrl = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(normalized)}";
                DictionaryApiResponse[]? apiData = null;
                try
                {
                    apiData = await _http.GetFromJsonAsync<DictionaryApiResponse[]>(apiUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "External dictionary API call failed for '{Word}'", normalized);
                }

                if (apiData == null || apiData.Length == 0)
                {
                    return ServiceResult<object>.Failure("No definitions found.");
                }

                var first = apiData[0];

                // Extract audio URL from phonetics array
                var audioUrl = first.Phonetics?
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Audio))?.Audio;

                // Create canonical Word
                var newWord = new Word
                {
                    Text = first.Word ?? normalized,
                    Pronunciation = first.Phonetic,
                    AudioUrl = audioUrl
                };
                _db.Words.Add(newWord);
                await _db.SaveChangesAsync();

                // Insert definitions
                int order = 1;
                foreach (var meaning in first.Meanings)
                {
                    // Map part of speech
                    var posName = (meaning.PartOfSpeech ?? "").Trim();
                    var pos = await _db.PartsOfSpeech.FirstOrDefaultAsync(p => p.Name.ToLower() == posName.ToLower());
                    if (pos == null)
                    {
                        // fallback to Noun if not matched
                        pos = await _db.PartsOfSpeech.FirstOrDefaultAsync(p => p.Name == "Noun");
                    }

                    foreach (var def in meaning.Definitions)
                    {
                        var wd = new WordDefinition
                        {
                            WordId = newWord.Id,
                            PartOfSpeechId = pos?.Id ?? 1,
                            Definition = def.DefinitionText,
                            Example = def.Example,
                            DisplayOrder = order++
                        };
                        _db.WordDefinitions.Add(wd);
                    }
                }

                await _db.SaveChangesAsync();

                // Reload with relationships for DTO mapping
                var saved = await _db.Words
                    .Include(w => w.WordDefinitions)
                        .ThenInclude(d => d.PartOfSpeech)
                    .FirstAsync(w => w.Id == newWord.Id);

                var savedDto = MapToDto(saved);

                // Check if word is in user's vocabulary (should be false for newly fetched words)
                if (userId.HasValue)
                {
                    isInUserVocabulary = await _db.UserWords
                        .AnyAsync(uw => uw.UserId == userId.Value && uw.WordId == saved.Id);
                }

                var response = new WordLookupResponse
                {
                    Success = true,
                    Word = savedDto,
                    WasFoundInCache = false,
                    IsInUserVocabulary = isInUserVocabulary
                };
                return ServiceResult<object>.Success(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LookupWordAsync failed for '{Word}'", term);
                return ServiceResult<object>.Failure("Internal server error");
            }
        }

        public async Task<ServiceResult<object>> AddWordAsync(AddWordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Word))
                return ServiceResult<object>.Failure("Word is required.");

            try
            {
                // Ensure canonical word exists; if not, create a minimal entry
                var word = await _db.Words.FirstOrDefaultAsync(w => w.Text == request.Word);
                if (word == null)
                {
                    word = new Word { Text = request.Word!, Pronunciation = request.Pronunciation };
                    _db.Words.Add(word);
                    await _db.SaveChangesAsync();
                }

                // Optionally add a canonical definition if provided
                if (!string.IsNullOrWhiteSpace(request.Definition))
                {
                    var pos = await ResolvePartOfSpeechAsync(request.PartOfSpeech);
                    var def = new WordDefinition
                    {
                        WordId = word.Id,
                        PartOfSpeechId = pos.Id,
                        Definition = request.Definition!,
                        Example = request.Example,
                        DisplayOrder = 1
                    };
                    _db.WordDefinitions.Add(def);
                    await _db.SaveChangesAsync();
                }

                return ServiceResult<object>.Success(new { message = "Word added successfully", wordId = word.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding word '{Word}'", request.Word);
                return ServiceResult<object>.Failure("Failed to add word");
            }
        }

        public async Task<ServiceResult<object>> AddToVocabularyAsync(int userId, AddWordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Word))
                return ServiceResult<object>.Failure("Word is required.");

            try
            {
                // Ensure canonical word exists
                var word = await _db.Words.FirstOrDefaultAsync(w => w.Text == request.Word);
                if (word == null)
                {
                    // Create minimal canonical entry
                    word = new Word { Text = request.Word!, Pronunciation = request.Pronunciation };
                    _db.Words.Add(word);
                    await _db.SaveChangesAsync();
                }

                var pos = await ResolvePartOfSpeechAsync(request.PartOfSpeech);

                // Check if already exists for this user/word/part-of-speech
                var exists = await _db.UserWords
                    .AnyAsync(uw => uw.UserId == userId && uw.WordId == word.Id && uw.PartOfSpeechId == pos.Id);
                if (exists)
                {
                    return ServiceResult<object>.Success(new { message = "Word already in your vocabulary" });
                }

                var userWord = new UserWord
                {
                    UserId = userId,
                    WordId = word.Id,
                    PartOfSpeechId = pos.Id,
                    // CreatedAt, CustomDefinition, IsFavorite, DifficultyLevel are not mapped in DB currently
                    // Use AddedAt (mapped) for timestamp
                    AddedAt = DateTime.UtcNow,
                    PersonalNotes = null,
                    TotalAttempts = 0,
                    CorrectAnswers = 0
                };
                _db.UserWords.Add(userWord);
                await _db.SaveChangesAsync();

                return ServiceResult<object>.Success(new { message = "Word added to your vocabulary", wordId = word.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding word to vocabulary for user {UserId}: '{Word}'", userId, request.Word);
                return ServiceResult<object>.Failure("Failed to add to vocabulary");
            }
        }

        private async Task<PartOfSpeech> ResolvePartOfSpeechAsync(string? partOfSpeech)
        {
            if (string.IsNullOrWhiteSpace(partOfSpeech))
            {
                return await _db.PartsOfSpeech.FirstAsync(p => p.Name == "Noun");
            }

            var normalized = partOfSpeech.Trim();
            var pos = await _db.PartsOfSpeech.FirstOrDefaultAsync(p => p.Name.ToLower() == normalized.ToLower()
                                                                       || p.Abbreviation.ToLower() == normalized.ToLower());
            if (pos != null) return pos;
            return await _db.PartsOfSpeech.FirstAsync(p => p.Name == "Noun");
        }

        public async Task<ServiceResult<UserVocabularyResponseDto>> GetUserVocabularyAsync(int userId, int page = 1, int pageSize = 20)
        {
            try
            {
                var query = _db.UserWords
                    .Include(uw => uw.Word)
                        .ThenInclude(w => w.WordDefinitions)
                    .Include(uw => uw.PartOfSpeech)
                    .Where(uw => uw.UserId == userId)
                    .OrderBy(uw => uw.Word.Text);

                var totalCount = await query.CountAsync();
                var items = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var vocabularyItems = items.Select(uw =>
                {
                    // Get the primary definition for this part of speech
                    var definition = uw.Word.WordDefinitions
                        .Where(wd => wd.PartOfSpeechId == uw.PartOfSpeechId)
                        .OrderBy(wd => wd.DisplayOrder)
                        .FirstOrDefault();

                    return new UserVocabularyItemDto
                    {
                        Id = uw.Id,
                        Word = uw.Word.Text,
                        Definition = definition?.Definition ?? "No definition available",
                        Example = definition?.Example,
                        PartOfSpeech = uw.PartOfSpeech?.Name ?? "Unknown",
                        Pronunciation = uw.Word.Pronunciation,
                        AudioUrl = uw.Word.AudioUrl,
                        AddedAt = uw.AddedAt,
                        PersonalNotes = uw.PersonalNotes,
                        CorrectAnswers = uw.CorrectAnswers,
                        TotalAttempts = uw.TotalAttempts
                    };
                }).ToList();

                var response = new UserVocabularyResponseDto
                {
                    Words = vocabularyItems,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                };

                return ServiceResult<UserVocabularyResponseDto>.Success(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user vocabulary for userId {UserId}", userId);
                return ServiceResult<UserVocabularyResponseDto>.Failure("Failed to retrieve vocabulary list");
            }
        }

        public async Task<ServiceResult<UserVocabularyResponseDto>> SearchUserVocabularyAsync(int userId, string searchTerm, int maxResults = 5)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return ServiceResult<UserVocabularyResponseDto>.Success(new UserVocabularyResponseDto
                    {
                        Words = new List<UserVocabularyItemDto>(),
                        TotalCount = 0,
                        Page = 1,
                        PageSize = maxResults
                    });
                }

                var normalizedTerm = searchTerm.Trim().ToLower();

                var items = await _db.UserWords
                    .Include(uw => uw.Word)
                        .ThenInclude(w => w.WordDefinitions)
                    .Include(uw => uw.PartOfSpeech)
                    .Where(uw => uw.UserId == userId && uw.Word.Text.ToLower().StartsWith(normalizedTerm))
                    .OrderBy(uw => uw.Word.Text)
                    .Take(maxResults)
                    .ToListAsync();

                var vocabularyItems = items.Select(uw =>
                {
                    var definitions = uw.Word.WordDefinitions
                        .Where(wd => wd.PartOfSpeechId == uw.PartOfSpeechId)
                        .OrderBy(wd => wd.DisplayOrder)
                        .ToList();

                    var definitionTexts = definitions
                        .Select(d => d.Definition)
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .ToList();

                    var aggregatedDefinition = definitionTexts.Count > 0
                        ? string.Join("; ", definitionTexts)
                        : "No definition available";

                    string? example = null;
                    foreach (var def in definitions)
                    {
                        if (!string.IsNullOrWhiteSpace(def.Example))
                        {
                            example = def.Example;
                            break;
                        }
                    }

                    return new UserVocabularyItemDto
                    {
                        Id = uw.Id,
                        Word = uw.Word.Text,
                        Definition = aggregatedDefinition,
                        Example = example,
                        PartOfSpeech = uw.PartOfSpeech?.Name ?? "Unknown",
                        Pronunciation = uw.Word.Pronunciation,
                        AudioUrl = uw.Word.AudioUrl,
                        AddedAt = uw.AddedAt,
                        PersonalNotes = uw.PersonalNotes,
                        CorrectAnswers = uw.CorrectAnswers,
                        TotalAttempts = uw.TotalAttempts
                    };
                }).ToList();

                var response = new UserVocabularyResponseDto
                {
                    Words = vocabularyItems,
                    TotalCount = vocabularyItems.Count,
                    Page = 1,
                    PageSize = maxResults
                };

                return ServiceResult<UserVocabularyResponseDto>.Success(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching user vocabulary for userId {UserId} with term '{SearchTerm}'", userId, searchTerm);
                return ServiceResult<UserVocabularyResponseDto>.Failure("Failed to search vocabulary");
            }
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
                var answerLookup = request.Answers
                    .GroupBy(answer => answer.QuestionId)
                    .Select(group => group.First())
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
                var skippedStaleQuestions = 0;
                var validUserWordIdsList = await _db.UserWords
                    .Where(uw => uw.UserId == userId)
                    .Select(uw => uw.Id)
                    .ToListAsync();
                var validUserWordIds = new HashSet<int>(validUserWordIdsList);

                foreach (var question in session.Questions)
                {
                    if (!validUserWordIds.Contains(question.UserWordId))
                    {
                        skippedStaleQuestions++;
                        continue;
                    }

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

                if (skippedStaleQuestions > 0)
                {
                    _logger.LogWarning(
                        "Skipped {SkippedCount} stale quiz question result(s) for user {UserId} in session {SessionId} because UserWord references no longer existed.",
                        skippedStaleQuestions,
                        userId,
                        request.SessionId);
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

        private static WordDto MapToDto(Word word)
        {
            var dto = new WordDto
            {
                Id = word.Id,
                Text = word.Text,
                Pronunciation = word.Pronunciation,
                AudioUrl = word.AudioUrl,
                CreatedAt = word.CreatedAt,
                Definitions = new List<WordDefinitionDto>()
            };

            foreach (var d in word.WordDefinitions
                         .OrderBy(wd => wd.PartOfSpeechId)
                         .ThenBy(wd => wd.DisplayOrder))
            {
                dto.Definitions.Add(new WordDefinitionDto
                {
                    Id = d.Id,
                    Definition = d.Definition,
                    Example = d.Example,
                    PartOfSpeech = d.PartOfSpeech?.Name ?? string.Empty,
                    PartOfSpeechAbbreviation = d.PartOfSpeech?.Abbreviation ?? string.Empty,
                    DisplayOrder = d.DisplayOrder
                });
            }

            return dto;
        }
    }
}
