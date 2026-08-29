using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.DTOs.External;
using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Services
{
    public class WordService : IWordService
    {
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
                var apiUrl = $"words/{Uri.EscapeDataString(normalized)}";
                WordsApiResponse? apiData;
                try
                {
                    using var providerResponse = await _http.GetAsync(apiUrl);
                    if (providerResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        return ServiceResult<object>.Failure(
                            "No definitions found.",
                            ServiceFailureType.NotFound);
                    }

                    if (!providerResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning(
                            "WordsAPI returned status {StatusCode} for '{Word}'",
                            (int)providerResponse.StatusCode,
                            normalized);
                        return DictionaryUnavailable();
                    }

                    apiData = await providerResponse.Content
                        .ReadFromJsonAsync<WordsApiResponse>();
                }
                catch (Exception ex) when (ex is HttpRequestException
                                           or TaskCanceledException
                                           or JsonException
                                           or NotSupportedException)
                {
                    _logger.LogWarning(ex, "WordsAPI call failed for '{Word}'", normalized);
                    return DictionaryUnavailable();
                }

                if (apiData == null)
                {
                    _logger.LogWarning("WordsAPI returned an empty response for '{Word}'", normalized);
                    return DictionaryUnavailable();
                }

                var providerWord = apiData.Word?.Trim();
                if (string.IsNullOrWhiteSpace(providerWord))
                {
                    _logger.LogWarning("WordsAPI returned invalid canonical word data for '{Word}'", normalized);
                    return DictionaryUnavailable();
                }

                var partsOfSpeech = await _db.PartsOfSpeech.ToListAsync();
                var mappedDefinitions = apiData.Results
                    .Where(result => !string.IsNullOrWhiteSpace(result.Definition))
                    .Select(result => new
                    {
                        Result = result,
                        PartOfSpeech = partsOfSpeech.FirstOrDefault(pos =>
                            string.Equals(pos.Name, result.PartOfSpeech?.Trim(),
                                StringComparison.OrdinalIgnoreCase))
                    })
                    .Where(mapped => mapped.PartOfSpeech != null)
                    .ToList();

                foreach (var unknown in apiData.Results
                    .Where(result => !string.IsNullOrWhiteSpace(result.Definition)
                                     && !partsOfSpeech.Any(pos => string.Equals(
                                         pos.Name,
                                         result.PartOfSpeech?.Trim(),
                                         StringComparison.OrdinalIgnoreCase))))
                {
                    _logger.LogWarning(
                        "Skipping WordsAPI definition for '{Word}' with unsupported part of speech '{PartOfSpeech}'",
                        providerWord,
                        unknown.PartOfSpeech);
                }

                if (mappedDefinitions.Count == 0)
                {
                    _logger.LogWarning("WordsAPI returned no usable definitions for '{Word}'", normalized);
                    return DictionaryUnavailable();
                }

                var pronunciation = apiData.Pronunciation
                    .FirstOrDefault(pair => string.Equals(pair.Key, "all", StringComparison.OrdinalIgnoreCase))
                    .Value
                    ?? apiData.Pronunciation.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                // Create canonical Word
                var newWord = new Word
                {
                    Text = providerWord,
                    Pronunciation = pronunciation,
                    AudioUrl = null
                };
                _db.Words.Add(newWord);

                // Insert definitions
                int order = 1;
                foreach (var mapped in mappedDefinitions)
                {
                    var wd = new WordDefinition
                    {
                        Word = newWord,
                        PartOfSpeechId = mapped.PartOfSpeech!.Id,
                        Definition = mapped.Result.Definition!.Trim(),
                        Example = mapped.Result.Examples
                            .FirstOrDefault(example => !string.IsNullOrWhiteSpace(example))?
                            .Trim(),
                        DisplayOrder = order++
                    };
                    _db.WordDefinitions.Add(wd);
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

        private static ServiceResult<object> DictionaryUnavailable() =>
            ServiceResult<object>.Failure(
                "Dictionary service is temporarily unavailable. Please try again.",
                ServiceFailureType.ServiceUnavailable);

        public async Task<ServiceResult<object>> AddToVocabularyAsync(int userId, AddWordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Word))
                return ServiceResult<object>.Failure("Word is required.");

            try
            {
                // Personal vocabulary may only reference provider-backed canonical data.
                var word = await _db.Words.FirstOrDefaultAsync(w => w.Text == request.Word);
                if (word == null)
                {
                    return ServiceResult<object>.Failure(
                        "Word is not available in the canonical dictionary. Look it up before adding it to your vocabulary.");
                }

                // Saved-word identity is UserId + WordId. A repeated add never changes
                // the existing entry's selected meaning or user-owned state.
                var existing = await _db.UserWords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(uw => uw.UserId == userId && uw.WordId == word.Id);
                if (existing != null)
                {
                    return ExistingVocabularyEntry(existing);
                }

                int partOfSpeechId;
                int? preferredWordDefinitionId;

                if (request.PreferredWordDefinitionId.HasValue)
                {
                    var requestedDefinition = await _db.WordDefinitions
                        .Where(wd =>
                            wd.Id == request.PreferredWordDefinitionId.Value &&
                            wd.WordId == word.Id)
                        .Select(wd => new { wd.Id, wd.PartOfSpeechId })
                        .FirstOrDefaultAsync();

                    if (requestedDefinition == null)
                    {
                        return ServiceResult<object>.Failure(
                            "Selected definition is not valid for this word.");
                    }

                    preferredWordDefinitionId = requestedDefinition.Id;
                    partOfSpeechId = requestedDefinition.PartOfSpeechId;
                }
                else
                {
                    var pos = await ResolvePartOfSpeechAsync(request.PartOfSpeech);
                    partOfSpeechId = pos.Id;
                    preferredWordDefinitionId = await ResolvePreferredDefinitionIdAsync(word.Id, pos.Id);
                }

                var userWord = new UserWord
                {
                    UserId = userId,
                    WordId = word.Id,
                    PartOfSpeechId = partOfSpeechId,
                    PreferredWordDefinitionId = preferredWordDefinitionId,
                    // CreatedAt, CustomDefinition, IsFavorite, DifficultyLevel are not mapped in DB currently
                    // Use AddedAt (mapped) for timestamp
                    AddedAt = DateTime.UtcNow,
                    PersonalNotes = null,
                    TotalAttempts = 0,
                    CorrectAnswers = 0
                };
                _db.UserWords.Add(userWord);
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (IsUserWordIdentityDuplicate(ex))
                {
                    _db.Entry(userWord).State = EntityState.Detached;
                    var winner = await _db.UserWords
                        .AsNoTracking()
                        .FirstOrDefaultAsync(uw => uw.UserId == userId && uw.WordId == word.Id);

                    if (winner != null)
                    {
                        return ExistingVocabularyEntry(winner);
                    }

                    throw;
                }

                return ServiceResult<object>.Success(new AddToVocabularyResultDto
                {
                    UserWordId = userWord.Id,
                    WordId = word.Id,
                    AlreadyExisted = false,
                    Message = "Word added to your vocabulary"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding word to vocabulary for user {UserId}: '{Word}'", userId, request.Word);
                return ServiceResult<object>.Failure("Failed to add to vocabulary");
            }
        }

        public async Task<ServiceResult<object>> SetPreferredDefinitionAsync(int userId, int userWordId, int preferredWordDefinitionId)
        {
            try
            {
                if (preferredWordDefinitionId <= 0)
                {
                    return ServiceResult<object>.Failure("A valid preferred definition is required.");
                }

                var userWord = await _db.UserWords
                    .FirstOrDefaultAsync(uw => uw.Id == userWordId && uw.UserId == userId);

                if (userWord == null)
                {
                    return ServiceResult<object>.Failure("Word not found in your vocabulary.");
                }

                var selectedDefinition = await _db.WordDefinitions
                    .Where(wd =>
                        wd.Id == preferredWordDefinitionId &&
                        wd.WordId == userWord.WordId)
                    .Select(wd => new
                    {
                        wd.Id,
                        wd.PartOfSpeechId
                    })
                    .FirstOrDefaultAsync();

                if (selectedDefinition == null)
                {
                    return ServiceResult<object>.Failure("Selected definition is not valid for this word.");
                }

                // Preferred definition is the selected meaning; POS remains synchronized
                // compatibility state and does not move or replace UserWord identity.
                userWord.PartOfSpeechId = selectedDefinition.PartOfSpeechId;
                userWord.PreferredWordDefinitionId = preferredWordDefinitionId;
                await _db.SaveChangesAsync();

                return ServiceResult<object>.Success(new
                {
                    message = "Preferred definition updated",
                    userWordId,
                    preferredWordDefinitionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating preferred definition for user {UserId}, userWord {UserWordId}", userId, userWordId);
                return ServiceResult<object>.Failure("Failed to update preferred definition.");
            }
        }

        public async Task<ServiceResult<object>> SetFavoriteAsync(int userId, int userWordId, bool isFavorite)
        {
            try
            {
                var userWord = await _db.UserWords
                    .FirstOrDefaultAsync(uw => uw.Id == userWordId && uw.UserId == userId);

                if (userWord == null)
                {
                    return ServiceResult<object>.Failure("Word not found in your vocabulary.");
                }

                userWord.IsFavorite = isFavorite;
                await _db.SaveChangesAsync();

                return ServiceResult<object>.Success(new
                {
                    message = isFavorite ? "Word marked as favorite" : "Word removed from favorites",
                    userWordId,
                    isFavorite
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating favorite state for user {UserId}, userWord {UserWordId}", userId, userWordId);
                return ServiceResult<object>.Failure("Failed to update favorite state.");
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

        public async Task<ServiceResult<UserVocabularyResponseDto>> GetUserVocabularyAsync(int userId, int page = 1, int pageSize = 20, string? searchTerm = null, string? startsWithLetter = null)
        {
            try
            {
                var query = _db.UserWords
                    .Include(uw => uw.Word)
                        .ThenInclude(w => w.WordDefinitions)
                    .Include(uw => uw.PartOfSpeech)
                    .Where(uw => uw.UserId == userId);

                if (!string.IsNullOrWhiteSpace(startsWithLetter))
                {
                    var normalizedLetter = startsWithLetter.Trim().Substring(0, 1).ToLower();
                    query = query.Where(uw => uw.Word.Text.ToLower().StartsWith(normalizedLetter));
                }

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var normalizedSearchTerm = searchTerm.Trim().ToLower();
                    query = query.Where(uw =>
                        uw.Word.Text.ToLower().Contains(normalizedSearchTerm) ||
                        uw.Word.WordDefinitions.Any(wd =>
                            (!string.IsNullOrEmpty(wd.Definition) && wd.Definition.ToLower().Contains(normalizedSearchTerm)) ||
                            (!string.IsNullOrEmpty(wd.Example) && wd.Example.ToLower().Contains(normalizedSearchTerm))));
                }

                query = query.OrderBy(uw => uw.Word.Text);

                var totalCount = await query.CountAsync();
                var items = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var vocabularyItems = items.Select(uw =>
                {
                    var definitions = uw.Word.WordDefinitions
                        .Where(wd => wd.PartOfSpeechId == uw.PartOfSpeechId)
                        .OrderBy(wd => wd.DisplayOrder)
                        .ToList();
                    var preferredDefinition = SelectPreferredDefinition(uw, definitions);

                    return new UserVocabularyItemDto
                    {
                        Id = uw.Id,
                        Word = uw.Word.Text,
                        Definition = preferredDefinition?.Definition ?? BuildAggregatedDefinitionText(definitions),
                        PreferredWordDefinitionId = preferredDefinition?.Id,
                        Example = preferredDefinition?.Example ?? PickFirstExample(definitions),
                        PartOfSpeech = uw.PartOfSpeech?.Name ?? "Unknown",
                        Pronunciation = uw.Word.Pronunciation,
                        AudioUrl = uw.Word.AudioUrl,
                        AddedAt = uw.AddedAt,
                        IsFavorite = uw.IsFavorite,
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
                    .Where(uw => uw.UserId == userId && (
                        uw.Word.Text.ToLower().Contains(normalizedTerm) ||
                        uw.Word.WordDefinitions.Any(wd =>
                            (!string.IsNullOrEmpty(wd.Definition) && wd.Definition.ToLower().Contains(normalizedTerm)) ||
                            (!string.IsNullOrEmpty(wd.Example) && wd.Example.ToLower().Contains(normalizedTerm)))))
                    .OrderBy(uw => uw.Word.Text.ToLower() == normalizedTerm ? 0 : 1)
                    .ThenBy(uw => uw.Word.Text.ToLower().StartsWith(normalizedTerm) ? 0 : 1)
                    .ThenBy(uw => uw.Word.Text.ToLower().Contains(normalizedTerm) ? 0 : 1)
                    .ThenBy(uw => uw.Word.WordDefinitions.Any(wd =>
                        !string.IsNullOrEmpty(wd.Definition) && wd.Definition.ToLower().StartsWith(normalizedTerm)) ? 0 : 1)
                    .ThenBy(uw => uw.Word.WordDefinitions.Any(wd =>
                        !string.IsNullOrEmpty(wd.Definition) && wd.Definition.ToLower().Contains(normalizedTerm)) ? 0 : 1)
                    .ThenBy(uw => uw.Word.WordDefinitions.Any(wd =>
                        !string.IsNullOrEmpty(wd.Example) && wd.Example.ToLower().Contains(normalizedTerm)) ? 0 : 1)
                    .ThenBy(uw => uw.Word.Text)
                    .Take(maxResults)
                    .ToListAsync();

                var vocabularyItems = items.Select(uw =>
                {
                    var definitions = uw.Word.WordDefinitions
                        .Where(wd => wd.PartOfSpeechId == uw.PartOfSpeechId)
                        .OrderBy(wd => wd.DisplayOrder)
                        .ToList();
                    var preferredDefinition = SelectPreferredDefinition(uw, definitions);

                    return new UserVocabularyItemDto
                    {
                        Id = uw.Id,
                        Word = uw.Word.Text,
                        Definition = preferredDefinition?.Definition ?? BuildAggregatedDefinitionText(definitions),
                        PreferredWordDefinitionId = preferredDefinition?.Id,
                        Example = preferredDefinition?.Example ?? PickFirstExample(definitions),
                        PartOfSpeech = uw.PartOfSpeech?.Name ?? "Unknown",
                        Pronunciation = uw.Word.Pronunciation,
                        AudioUrl = uw.Word.AudioUrl,
                        AddedAt = uw.AddedAt,
                        IsFavorite = uw.IsFavorite,
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

        private static string BuildAggregatedDefinitionText(IEnumerable<WordDefinition> definitions)
        {
            var definitionTexts = definitions
                .Select(d => d.Definition?.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return definitionTexts.Count > 0
                ? string.Join("; ", definitionTexts)
                : "No definition available";
        }

        private static string? PickFirstExample(IEnumerable<WordDefinition> definitions)
        {
            foreach (var def in definitions)
            {
                if (!string.IsNullOrWhiteSpace(def.Example))
                {
                    return def.Example;
                }
            }

            return null;
        }

        private static WordDefinition? SelectPreferredDefinition(UserWord userWord, IReadOnlyCollection<WordDefinition> definitions)
        {
            if (definitions.Count == 0)
            {
                return null;
            }

            if (userWord.PreferredWordDefinitionId.HasValue)
            {
                var explicitPreferred = definitions.FirstOrDefault(d => d.Id == userWord.PreferredWordDefinitionId.Value);
                if (explicitPreferred != null)
                {
                    return explicitPreferred;
                }
            }

            return definitions.OrderBy(d => d.DisplayOrder).FirstOrDefault();
        }

        private async Task<int?> ResolvePreferredDefinitionIdAsync(int wordId, int partOfSpeechId)
        {
            return await _db.WordDefinitions
                .Where(wd => wd.WordId == wordId && wd.PartOfSpeechId == partOfSpeechId)
                .OrderBy(wd => wd.DisplayOrder)
                .Select(wd => (int?)wd.Id)
                .FirstOrDefaultAsync();
        }

        private static ServiceResult<object> ExistingVocabularyEntry(UserWord userWord) =>
            ServiceResult<object>.Success(new AddToVocabularyResultDto
            {
                UserWordId = userWord.Id,
                WordId = userWord.WordId,
                AlreadyExisted = true,
                Message = "Word already in your vocabulary"
            });

        private static bool IsUserWordIdentityDuplicate(DbUpdateException exception)
        {
            const string indexName = "IX_UserWords_UserId_WordId";

            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                var identifiesIndex = current.Message.Contains(
                    indexName,
                    StringComparison.OrdinalIgnoreCase);
                var identifiesSqliteColumns =
                    current.Message.Contains("UserWords.UserId", StringComparison.OrdinalIgnoreCase) &&
                    current.Message.Contains("UserWords.WordId", StringComparison.OrdinalIgnoreCase);

                if (!identifiesIndex && !identifiesSqliteColumns)
                {
                    continue;
                }

                var exceptionType = current.GetType();
                if (exceptionType.FullName == "Microsoft.Data.SqlClient.SqlException")
                {
                    var number = exceptionType.GetProperty("Number")?.GetValue(current) as int?;
                    return number is 2601 or 2627;
                }

                if (exceptionType.FullName == "Microsoft.Data.Sqlite.SqliteException")
                {
                    var extendedErrorCode = exceptionType
                        .GetProperty("SqliteExtendedErrorCode")?
                        .GetValue(current) as int?;
                    return extendedErrorCode == 2067;
                }
            }

            return false;
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
