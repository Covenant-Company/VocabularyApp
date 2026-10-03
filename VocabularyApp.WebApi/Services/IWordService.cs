using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.DTOs;

namespace VocabularyApp.WebApi.Services
{
    public interface IWordService
    {
        Task<ServiceResult<WordLookupResponse>> LookupWordAsync(string term, int? userId = null);
        Task<ServiceResult<AddToVocabularyResultDto>> AddToVocabularyAsync(int userId, AddWordRequest request);
        Task<ServiceResult<FavoriteUpdateResponseDto>> SetFavoriteAsync(int userId, int userWordId, bool isFavorite);
        Task<ServiceResult<PreferredDefinitionUpdateResponseDto>> SetPreferredDefinitionAsync(int userId, int userWordId, int preferredWordDefinitionId);
        Task<ServiceResult<UserVocabularyResponseDto>> GetUserVocabularyAsync(int userId, int page = 1, int pageSize = 20, string? searchTerm = null, string? startsWithLetter = null);
        Task<ServiceResult<UserVocabularyResponseDto>> SearchUserVocabularyAsync(int userId, string? searchTerm, int maxResults = 5);
    }
}
