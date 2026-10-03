using VocabularyApp.WebApi.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.Services;

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
        /// <remarks>Bearer required by fallback policy. Canonical dictionary success; genuine miss is word_not_found 404; unavailable/untrustworthy provider is dictionary_unavailable 503; internal_error 500. Framework 401 has no envelope.</remarks>
        [HttpGet("lookup/{word}")]
        [ProducesResponseType(typeof(SuccessResponse<WordLookupResponse>), 200)]
        [ProducesResponseType(typeof(ApiErrorResponse), 400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(typeof(ApiErrorResponse), 404)]
        [ProducesResponseType(typeof(ApiErrorResponse), 500)]
        [ProducesResponseType(typeof(ApiErrorResponse), 503)]
        public async Task<IActionResult> LookupWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return ApiErrorResults.Create(HttpContext, ServiceFailureType.Validation, "invalid_request");

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
                    return ApiErrorResults.FromFailure(HttpContext, result);
                }
                if (result.Data is null) return ApiErrorResults.Internal(HttpContext);
                return Ok(new SuccessResponse<WordLookupResponse> { Data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during LookupWord for '{Word}'", word);
                return ApiErrorResults.Internal(HttpContext);
            }
        }

        /// <summary>
        /// Add a canonical word to the user's vocabulary; duplicate adds succeed without changing the existing row.
        /// POST: /api/words/vocabulary/add
        /// </summary>
        /// <remarks>Bearer required. Identity is (UserId, WordId); duplicate add succeeds without changing the row. Definition/example/pronunciation remain accepted but ignored; POS is a selection fallback. Validation/business 400; invalid_token 401; internal_error 500.</remarks>
        [HttpPost("vocabulary/add")]
        [Authorize]
        [ProducesResponseType(typeof(SuccessResponse<AddToVocabularyResultDto>), 200)]
        [ProducesResponseType(typeof(ApiErrorResponse), 400)]
        [ProducesResponseType(typeof(ApiErrorResponse), 401)]
        [ProducesResponseType(typeof(ApiErrorResponse), 500)]
        public async Task<IActionResult> AddToVocabulary([FromBody] AddWordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Word))
                return ApiErrorResults.Create(HttpContext, ServiceFailureType.Validation, "invalid_request");

            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
                {
                    return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
                }

                var result = await _wordService.AddToVocabularyAsync(userId, request);
                if (!result.IsSuccess)
                {
                    return ApiErrorResults.FromFailure(HttpContext, result);
                }

                if (result.Data is null) return ApiErrorResults.Internal(HttpContext);
                return Ok(new SuccessResponse<AddToVocabularyResultDto> { Data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding word to vocabulary '{Word}'", request.Word);
                return ApiErrorResults.Internal(HttpContext);
            }
        }

        /// <summary>
        /// Get user's vocabulary list with pagination and optional filters
        /// GET: /api/words/vocabulary?page=1&amp;pageSize=20&amp;term=abc&amp;startsWithLetter=A
        /// </summary>
        /// <remarks>Bearer required. Full typed paginated response. page below 1 normalizes to 1; pageSize outside 1..10000 to 20. Integer-binding validation 400; invalid_token 401; internal_error 500.</remarks>
        [HttpGet("vocabulary")]
        [Authorize]
        [ProducesResponseType(typeof(SuccessResponse<UserVocabularyResponseDto>), 200)]
        [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
        [ProducesResponseType(typeof(ApiErrorResponse), 401)]
        [ProducesResponseType(typeof(ApiErrorResponse), 500)]
        public async Task<IActionResult> GetUserVocabulary([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? term = null, [FromQuery] string? startsWithLetter = null)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
                {
                    return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
                }

                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 10000) pageSize = 20; // Increased max to 10000 for vocabulary search

                var result = await _wordService.GetUserVocabularyAsync(userId, page, pageSize, term, startsWithLetter);
                if (!result.IsSuccess)
                {
                    return ApiErrorResults.FromFailure(HttpContext, result);
                }

                if (result.Data is null) return ApiErrorResults.Internal(HttpContext);
                return Ok(new SuccessResponse<UserVocabularyResponseDto> { Data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user vocabulary");
                return ApiErrorResults.Internal(HttpContext);
            }
        }

        /// <summary>
        /// Search user's vocabulary for autocomplete
        /// GET: /api/words/vocabulary/search?term=abc
        /// </summary>
        /// <remarks>Bearer required. At most five full items ordered by relevance. Absent/blank term returns a full empty response, page 1/pageSize 5. invalid_token 401; internal_error 500.</remarks>
        [HttpGet("vocabulary/search")]
        [Authorize]
        [ProducesResponseType(typeof(SuccessResponse<UserVocabularyResponseDto>), 200)]
        [ProducesResponseType(typeof(ApiErrorResponse), 401)]
        [ProducesResponseType(typeof(ApiErrorResponse), 500)]
        public async Task<IActionResult> SearchUserVocabulary([FromQuery] string? term = null)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
                {
                    return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
                }

                var result = await _wordService.SearchUserVocabularyAsync(userId, term, maxResults: 5);
                if (!result.IsSuccess)
                {
                    return ApiErrorResults.FromFailure(HttpContext, result);
                }

                if (result.Data is null) return ApiErrorResults.Internal(HttpContext);
                return Ok(new SuccessResponse<UserVocabularyResponseDto> { Data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching user vocabulary");
                return ApiErrorResults.Internal(HttpContext);
            }
        }

        /// <summary>
        /// Update favorite state for a user's vocabulary entry
        /// PUT: /api/words/vocabulary/{userWordId}/favorite
        /// </summary>
        /// <remarks>Bearer required. isFavorite presence required; false valid. Binding/presence validation 400. Missing/nonowned rows share vocabulary_not_found 404; invalid_token 401; internal_error 500.</remarks>
        [HttpPut("vocabulary/{userWordId:int}/favorite")]
        [Authorize]
        [ProducesResponseType(typeof(SuccessResponse<FavoriteUpdateResponseDto>), 200)]
        [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
        [ProducesResponseType(typeof(ApiErrorResponse), 401)]
        [ProducesResponseType(typeof(ApiErrorResponse), 404)]
        [ProducesResponseType(typeof(ApiErrorResponse), 500)]
        public async Task<IActionResult> SetFavorite(int userWordId, [FromBody] UpdateFavoriteRequestDto request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
                {
                    return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
                }

                var result = await _wordService.SetFavoriteAsync(userId, userWordId, request.IsFavorite);
                if (!result.IsSuccess)
                {
                    return ApiErrorResults.FromFailure(HttpContext, result);
                }

                if (result.Data is null) return ApiErrorResults.Internal(HttpContext);
                return Ok(new SuccessResponse<FavoriteUpdateResponseDto> { Data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating favorite state for userWordId {UserWordId}", userWordId);
                return ApiErrorResults.Internal(HttpContext);
            }
        }

        /// <summary>
        /// Update preferred definition for a user's vocabulary entry
        /// PUT: /api/words/vocabulary/{userWordId}/preferred-definition
        /// </summary>
        /// <remarks>Bearer required. Definition must belong to the canonical word; updates the same row and derived POS. Validation/invalid selection 400; missing/nonowned vocabulary_not_found 404; invalid_token 401; internal_error 500.</remarks>
        [HttpPut("vocabulary/{userWordId:int}/preferred-definition")]
        [Authorize]
        [ProducesResponseType(typeof(SuccessResponse<PreferredDefinitionUpdateResponseDto>), 200)]
        [ProducesResponseType(typeof(ApiErrorResponse), 400)]
        [ProducesResponseType(typeof(ApiErrorResponse), 401)]
        [ProducesResponseType(typeof(ApiErrorResponse), 404)]
        [ProducesResponseType(typeof(ApiErrorResponse), 500)]
        public async Task<IActionResult> SetPreferredDefinition(int userWordId, [FromBody] UpdatePreferredDefinitionRequestDto request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
                {
                    return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
                }

                if (request == null || request.PreferredWordDefinitionId <= 0)
                {
                    return ApiErrorResults.Create(HttpContext, ServiceFailureType.Validation, "invalid_preferred_definition");
                }

                var result = await _wordService.SetPreferredDefinitionAsync(userId, userWordId, request.PreferredWordDefinitionId);
                if (!result.IsSuccess)
                {
                    return ApiErrorResults.FromFailure(HttpContext, result);
                }

                if (result.Data is null) return ApiErrorResults.Internal(HttpContext);
                return Ok(new SuccessResponse<PreferredDefinitionUpdateResponseDto> { Data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating preferred definition for userWordId {UserWordId}", userWordId);
                return ApiErrorResults.Internal(HttpContext);
            }
        }

    }
}
