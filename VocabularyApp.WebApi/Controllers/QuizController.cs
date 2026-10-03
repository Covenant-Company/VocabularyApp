using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Services;

namespace VocabularyApp.WebApi.Controllers
{
  [ApiController]
  [Route("api/quiz")]
  [Produces("application/json")]
  [Authorize]
  public class QuizController : ControllerBase
  {
    private readonly IQuizService _quizService;
    private readonly ILogger<QuizController> _logger;

    public QuizController(IQuizService quizService, ILogger<QuizController> logger)
    {
      _quizService = quizService;
      _logger = logger;
    }

    /// <summary>
    /// Start a new quiz session from the user's vocabulary
    /// POST: /api/quiz/start
    /// </summary>
    /// <remarks>Bearer required. Expiring process-local session; no answer key. Validation/quiz_unavailable 400; invalid_token 401; internal_error 500.</remarks>
    [HttpPost("start")]
    [ProducesResponseType(typeof(SuccessResponse<QuizStartResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> StartQuiz([FromBody] StartQuizRequestDto request)
    {
      try
      {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        {
          return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
        }

        var quizRequest = request ?? new StartQuizRequestDto();
        var result = await _quizService.StartQuizAsync(userId, quizRequest);
        if (!result.IsSuccess)
        {
          return ApiErrorResults.FromFailure(HttpContext, result);
        }

        if (result.Data is null) return ApiErrorResults.Internal(HttpContext);
        return Ok(new SuccessResponse<QuizStartResponseDto> { Data = result.Data });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error starting quiz");
        return ApiErrorResults.Internal(HttpContext);
      }
    }

    /// <summary>
    /// Submit quiz answers for scoring
    /// POST: /api/quiz/submit
    /// </summary>
    /// <remarks>Bearer required. selectedOptionId presence required; zero valid. Omitted/empty answers count as unanswered. Invalid answers are safe 400. Unavailable session 404; concurrent/recognized persisted duplicate submission or changed vocabulary 409; internal_error 500. Removed sessions replay as 404.</remarks>
    [HttpPost("submit")]
    [ProducesResponseType(typeof(SuccessResponse<QuizSubmitResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> SubmitQuiz([FromBody] QuizSubmitRequestDto request)
    {
      if (request == null)
      {
        return ApiErrorResults.Create(HttpContext, ServiceFailureType.Validation, "invalid_request");
      }

      try
      {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        {
          return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
        }

        var result = await _quizService.SubmitQuizAsync(userId, request);
        if (!result.IsSuccess)
        {
          return ApiErrorResults.FromFailure(HttpContext, result);
        }

        if (result.Data is null) return ApiErrorResults.Internal(HttpContext);
        return Ok(new SuccessResponse<QuizSubmitResponseDto> { Data = result.Data });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error submitting quiz");
        return ApiErrorResults.Internal(HttpContext);
      }
    }

    /// <summary>
    /// Get recent quiz history for the current user
    /// GET: /api/quiz/history?take=5
    /// </summary>
    /// <remarks>Bearer required. Items have no sessionId. take defaults/nonpositive normalizes to 5; maximum 20. Integer-binding validation 400; invalid_token 401; internal_error 500.</remarks>
    [HttpGet("history")]
    [ProducesResponseType(typeof(SuccessResponse<QuizHistoryResponseDto>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> GetQuizHistory([FromQuery] int take = 5)
    {
      try
      {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        {
          return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
        }

        var result = await _quizService.GetRecentQuizHistoryAsync(userId, take);
        if (!result.IsSuccess)
        {
          return ApiErrorResults.FromFailure(HttpContext, result);
        }

        if (result.Data is null) return ApiErrorResults.Internal(HttpContext);
        return Ok(new SuccessResponse<QuizHistoryResponseDto> { Data = result.Data });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error retrieving quiz history");
        return ApiErrorResults.Internal(HttpContext);
      }
    }
  }
}
