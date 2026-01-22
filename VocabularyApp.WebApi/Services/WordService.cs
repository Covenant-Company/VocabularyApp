using Microsoft.EntityFrameworkCore;
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
