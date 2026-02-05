using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.DTOs;

namespace VocabularyApp.WebApi.Services
{
    public interface IWordService
    {
        Task<ServiceResult<object>> LookupWordAsync(string term, int? userId = null);
        Task<ServiceResult<object>> AddWordAsync(AddWordRequest request);
        Task<ServiceResult<object>> AddToVocabularyAsync(int userId, AddWordRequest request);
        Task<ServiceResult<UserVocabularyResponseDto>> GetUserVocabularyAsync(int userId, int page = 1, int pageSize = 20);
        Task<ServiceResult<UserVocabularyResponseDto>> SearchUserVocabularyAsync(int userId, string searchTerm, int maxResults = 5);
    }
}
