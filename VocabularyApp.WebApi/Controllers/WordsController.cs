using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.Services;       // AddWordRequest (the DTO that lives under Models)

namespace VocabularyApp.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class WordsController : ControllerBase
    {
        private readonly IWordService _wordService;
        private readonly ILogger<WordsController> _logger;

        public WordsController(IWordService wordService, ILogger<WordsController> logger)
        {
            _wordService = wordService;
            _logger = logger;
        }

        /// <summary>
        /// Lookup a word (canonical/local dictionary)
        /// GET: /api/words/lookup/{word}
        /// </summary>
        [HttpGet("lookup/{word}")]
        public async Task<IActionResult> LookupWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return BadRequest(new { success = false, error = "Word is required." });

            try
            {
                // Try to get userId from token if user is authenticated
                int? userId = null;
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim != null && int.TryParse(userIdClaim, out var parsedUserId))
                {
                    userId = parsedUserId;
                }

                var result = await _wordService.LookupWordAsync(word, userId);
                if (!result.IsSuccess)
                {
                    return NotFound(new { success = false, error = result.Message ?? "Word not found." });
                }
                return Ok(new { success = true, data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during LookupWord for '{Word}'", word);
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        /// <summary>
        /// Add word to canonical dictionary (admin endpoint)
        /// POST: /api/words/add
        /// </summary>
        [HttpPost("add")]
        public async Task<IActionResult> AddWord([FromBody] AddWordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Word))
                return BadRequest(new { success = false, error = "Word is required." });

            try
            {
                var result = await _wordService.AddWordAsync(request);

                // if (!result.Success.Equals(false))
                //     return BadRequest(new { success = false, error = result.Message ?? "Failed to add word." });

                return Ok(new { success = true, data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding word '{Word}'", request?.Word);
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        /// <summary>
        /// Add a word to the *user's* vocabulary (example path your Angular uses earlier)
        /// POST: /api/words/vocabulary/add
        /// </summary>
        [HttpPost("vocabulary/add")]
        [Authorize]
        public async Task<IActionResult> AddToVocabulary([FromBody] AddWordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Word))
                return BadRequest(new { success = false, error = "Word is required." });

            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized(new { success = false, error = "Invalid token" });
                }

                var result = await _wordService.AddToVocabularyAsync(userId, request);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { success = false, error = result.Message ?? "Failed to add to vocabulary." });
                }

                return Ok(new { success = true, data = result.Data ?? new { message = "Word added to vocabulary" } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding word to vocabulary '{Word}'", request.Word);
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get user's vocabulary list with pagination
        /// GET: /api/words/vocabulary?page=1&pageSize=20
        /// </summary>
        [HttpGet("vocabulary")]
        [Authorize]
        public async Task<IActionResult> GetUserVocabulary([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized(new { success = false, error = "Invalid token" });
                }

                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 10000) pageSize = 20; // Increased max to 10000 for vocabulary search

                var result = await _wordService.GetUserVocabularyAsync(userId, page, pageSize);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { success = false, error = result.Message ?? "Failed to retrieve vocabulary." });
                }

                return Ok(new { success = true, data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user vocabulary");
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        /// <summary>
        /// Search user's vocabulary for autocomplete
        /// GET: /api/words/vocabulary/search?term=abc
        /// </summary>
        [HttpGet("vocabulary/search")]
        [Authorize]
        public async Task<IActionResult> SearchUserVocabulary([FromQuery] string term)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized(new { success = false, error = "Invalid token" });
                }

                if (string.IsNullOrWhiteSpace(term))
                {
                    return Ok(new { success = true, data = new { words = new List<object>() } });
                }

                var result = await _wordService.SearchUserVocabularyAsync(userId, term, maxResults: 5);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { success = false, error = result.Message ?? "Failed to search vocabulary." });
                }

                return Ok(new { success = true, data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching user vocabulary");
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }
    }
}
