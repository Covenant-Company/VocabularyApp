using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Services;

public interface IReviewService
{
    Task<ServiceResult<ReviewDueResponseDto>> DueAsync(int userId, ReviewDueQueryDto query);
    Task<ServiceResult<ReviewSummaryResponseDto>> SummaryAsync(int userId);
    Task<ServiceResult<ReviewStartResponseDto>> StartAsync(int userId, ReviewStartRequestDto request);
    Task<ServiceResult<ReviewSubmitResponseDto>> SubmitAsync(int userId, ReviewSubmitRequestDto request);
}
