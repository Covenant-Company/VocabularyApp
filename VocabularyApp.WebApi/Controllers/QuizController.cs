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
    [HttpPost("start")]
    public async Task<IActionResult> StartQuiz([FromBody] StartQuizRequestDto request)
    {
      try
      {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        {
          return Unauthorized(new { success = false, error = "Invalid token" });
        }

        var quizRequest = request ?? new StartQuizRequestDto();
        var result = await _quizService.StartQuizAsync(userId, quizRequest);
        if (!result.IsSuccess)
        {
          return BadRequest(new { success = false, error = result.Message ?? "Failed to start quiz." });
        }

        return Ok(new { success = true, data = result.Data });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error starting quiz");
        return StatusCode(500, new { success = false, error = "Internal server error" });
      }
    }

    /// <summary>
    /// Submit quiz answers for scoring
    /// POST: /api/quiz/submit
    /// </summary>
    [HttpPost("submit")]
    public async Task<IActionResult> SubmitQuiz([FromBody] QuizSubmitRequestDto request)
    {
      if (request == null)
      {
        return BadRequest(new { success = false, error = "Request body is required." });
      }

      try
      {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        {
          return Unauthorized(new { success = false, error = "Invalid token" });
        }

        var result = await _quizService.SubmitQuizAsync(userId, request);
        if (!result.IsSuccess)
        {
          return BadRequest(new { success = false, error = result.Message ?? "Failed to submit quiz." });
        }

        return Ok(new { success = true, data = result.Data });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error submitting quiz");
        return StatusCode(500, new { success = false, error = "Internal server error" });
      }
    }

    /// <summary>
    /// Get recent quiz history for the current user
    /// GET: /api/quiz/history?take=5
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetQuizHistory([FromQuery] int take = 5)
    {
      try
      {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        {
          return Unauthorized(new { success = false, error = "Invalid token" });
        }

        var result = await _quizService.GetRecentQuizHistoryAsync(userId, take);
        if (!result.IsSuccess)
        {
          return BadRequest(new { success = false, error = result.Message ?? "Failed to fetch quiz history." });
        }

        return Ok(new { success = true, data = result.Data });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error retrieving quiz history");
        return StatusCode(500, new { success = false, error = "Internal server error" });
      }
    }
  }
}
