using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Helpers;
using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.Services;

namespace VocabularyApp.WebApi.Controllers;

[ApiController, Authorize, Route("api/reviews"), Produces("application/json")]
public sealed class ReviewsController(IReviewService service, ILogger<ReviewsController> logger) : ControllerBase
{
    [HttpGet("due")]
    [ProducesResponseType(typeof(SuccessResponse<ReviewDueResponseDto>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 503)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public Task<IActionResult> GetDue([FromQuery] ReviewDueQueryDto query) => Run(id => service.DueAsync(id, query));

    [HttpGet("summary")]
    [ProducesResponseType(typeof(SuccessResponse<ReviewSummaryResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 503)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public Task<IActionResult> GetReviewSummary() => Run(service.SummaryAsync);

    [HttpPost("start")]
    [ProducesResponseType(typeof(SuccessResponse<ReviewStartResponseDto>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 503)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public Task<IActionResult> StartReview([FromBody] ReviewStartRequestDto request) => Run(id => service.StartAsync(id, request));

    [HttpPost("submit")]
    [ProducesResponseType(typeof(SuccessResponse<ReviewSubmitResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 503)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public Task<IActionResult> SubmitReview([FromBody] ReviewSubmitRequestDto request) => Run(id => service.SubmitAsync(id, request));

    private async Task<IActionResult> Run<T>(Func<int, Task<ServiceResult<T>>> action)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var owner))
            return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
        try
        {
            var result = await action(owner);
            if (!result.IsSuccess)
            {
                if (result.Code == "validation_failed")
                {
                    ModelState.AddModelError("cursor", "Invalid value.");
                    return ApiValidationResponses.Create(ControllerContext);
                }
                return ApiErrorResults.FromFailure(HttpContext, result);
            }
            return result.Data == null ? ApiErrorResults.Internal(HttpContext) : Ok(new SuccessResponse<T> { Data = result.Data });
        }
        catch (Exception ex)
        {
            logger.LogError("Review operation failed ({ExceptionType}).", ex.GetType().Name);
            return ApiErrorResults.Internal(HttpContext);
        }
    }
}
