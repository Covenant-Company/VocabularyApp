using VocabularyApp.WebApi.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.Services;

namespace VocabularyApp.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Registers a new user account
    /// </summary>
    /// <remarks>Anonymous. Nested auth success. Binding failures are extended ValidationProblemDetails 400; duplicate username/email are application 400; internal failures are safe 500.</remarks>
    /// <param name="request">User registration details</param>
    /// <returns>User information and JWT token if successful</returns>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResult<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<ActionResult<ApiResult<AuthResponse>>> Register([FromBody] CreateUserRequest request)
    {
        try
        {
            _logger.LogInformation("Registration attempt for username: {Username}", request.Username);
            var result = await _userService.CreateUserAsync(request);

            if (result.IsSuccess)
            {
                _logger.LogInformation("User registered successfully: {Username}", request.Username);
                if (result.Data is null || !result.Data.Success || result.Data.User is null || string.IsNullOrWhiteSpace(result.Data.Token)) return ApiErrorResults.Internal(HttpContext);
                return Ok(ApiResult<AuthResponse>.SuccessResult(result.Data));
            }
            else
            {
                _logger.LogWarning("Registration failed for username: {Username}, Error: {Error}", request.Username, result.Code);
                return ApiErrorResults.FromFailure(HttpContext, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during user registration: {Username}", request.Username);
            return ApiErrorResults.Internal(HttpContext);
        }
    }

    /// <summary>
    /// Authenticates a user and returns a JWT token
    /// </summary>
    /// <remarks>Anonymous. Nested auth success. Validation 400; invalid_credentials 401; credentials_changed 409; safe internal_error 500.</remarks>
    /// <param name="request">Login credentials</param>
    /// <returns>User information and JWT token if successful</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResult<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<ActionResult<ApiResult<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        try
        {
            _logger.LogInformation("Login attempt for username: {Username}", request.Username);
            var result = await _userService.LoginAsync(request);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Login successful for username: {Username}", request.Username);
                if (result.Data is null || !result.Data.Success || result.Data.User is null || string.IsNullOrWhiteSpace(result.Data.Token)) return ApiErrorResults.Internal(HttpContext);
                return Ok(ApiResult<AuthResponse>.SuccessResult(result.Data));
            }
            else
            {
                _logger.LogWarning("Login failed for username: {Username}", request.Username);
                return ApiErrorResults.FromFailure(HttpContext, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during login: {Username}", request.Username);
            return ApiErrorResults.Internal(HttpContext);
        }
    }

    /// <summary>
    /// Gets the current user's profile information
    /// </summary>
    /// <remarks>Bearer required. Empty framework challenge differs from application invalid_token 401. Missing account is user_not_found 404; internal failures are safe 500.</remarks>
    /// <returns>Current user information</returns>
    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResult<UserDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<ActionResult<ApiResult<UserDto>>> GetProfile()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("Invalid user ID claim in token");
                return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
            }

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User not found for ID: {UserId}", userId);
                return ApiErrorResults.Create(HttpContext, ServiceFailureType.NotFound, "user_not_found");
            }

            return Ok(ApiResult<UserDto>.SuccessResult(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user profile");
            return ApiErrorResults.Internal(HttpContext);
        }
    }

    /// <summary>
    /// Changes the current user's password
    /// </summary>
    /// <remarks>Bearer required. Non-generic success wrapper retains null data/error. Validation 400; account/current-password application 401; credentials_changed 409; safe internal_error 500.</remarks>
    /// <param name="request">Current and new password</param>
    /// <returns>Success confirmation</returns>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResult), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<ActionResult<ApiResult>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            {
                return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
            }

            _logger.LogInformation("Password change attempt for user: {UserId}", userId);
            var result = await _userService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Password changed successfully for user: {UserId}", userId);
                if (!result.Data) return ApiErrorResults.Internal(HttpContext);
                return Ok(ApiResult.SuccessResult());
            }
            else
            {
                _logger.LogWarning("Password change failed for user: {UserId}", userId);
                return ApiErrorResults.FromFailure(HttpContext, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return ApiErrorResults.Internal(HttpContext);
        }
    }

    /// <summary>
    /// Validates the current JWT token
    /// </summary>
    /// <remarks>Bearer required. Returns UserDto without issuing a token. Empty framework challenge differs from invalid_token/user_unavailable application 401; safe internal_error 500.</remarks>
    /// <returns>Token validation result</returns>
    [HttpGet("validate-token")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResult<UserDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<ActionResult<ApiResult<UserDto>>> ValidateToken()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            {
                return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "invalid_token");
            }

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return ApiErrorResults.Create(HttpContext, ServiceFailureType.Unauthorized, "user_unavailable");
            }

            return Ok(ApiResult<UserDto>.SuccessResult(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return ApiErrorResults.Internal(HttpContext);
        }
    }
}

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;
}
