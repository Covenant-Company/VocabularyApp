using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Services
{
  public interface IQuizService
  {
    Task<ServiceResult<QuizStartResponseDto>> StartQuizAsync(int userId, StartQuizRequestDto request);
    Task<ServiceResult<QuizSubmitResponseDto>> SubmitQuizAsync(int userId, QuizSubmitRequestDto request);
    Task<ServiceResult<QuizHistoryResponseDto>> GetRecentQuizHistoryAsync(int userId, int take = 5);
  }
}
