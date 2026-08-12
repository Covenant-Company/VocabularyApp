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
    /// <param name="request">User registration details</param>
    /// <returns>User information and JWT token if successful</returns>
    /// <response code="200">User created successfully</response>
    /// <response code="400">Invalid registration data or user already exists</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResult<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResult<object>), 400)]
    public async Task<ActionResult<ApiResult<AuthResponse>>> Register([FromBody] CreateUserRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                _logger.LogWarning("Invalid registration request: {Errors}", string.Join(", ", errors));
                return BadRequest(ApiResult<AuthResponse>.ErrorResult($"Validation failed: {string.Join(", ", errors)}"));
            }

            _logger.LogInformation("Registration attempt for username: {Username}", request.Username);
            var result = await _userService.CreateUserAsync(request);

            if (result.Success)
            {
                _logger.LogInformation("User registered successfully: {Username}", request.Username);
                return Ok(ApiResult<AuthResponse>.SuccessResult(result));
            }
            else
            {
                _logger.LogWarning("Registration failed for username: {Username}, Error: {Error}", request.Username, result.ErrorMessage);
                return BadRequest(ApiResult<AuthResponse>.ErrorResult(result.ErrorMessage ?? "Registration failed"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during user registration: {Username}", request.Username);
            return StatusCode(500, ApiResult<AuthResponse>.ErrorResult("An internal error occurred"));
        }
    }

    /// <summary>
    /// Authenticates a user and returns a JWT token
    /// </summary>
    /// <param name="request">Login credentials</param>
    /// <returns>User information and JWT token if successful</returns>
    /// <response code="200">Login successful</response>
    /// <response code="401">Invalid credentials</response>
    /// <response code="400">Invalid login data</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResult<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResult<object>), 401)]
    [ProducesResponseType(typeof(ApiResult<object>), 400)]
    public async Task<ActionResult<ApiResult<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(ApiResult<AuthResponse>.ErrorResult($"Validation failed: {string.Join(", ", errors)}"));
            }

            _logger.LogInformation("Login attempt for username: {Username}", request.Username);
            var result = await _userService.LoginAsync(request);

            if (result.Success)
            {
                _logger.LogInformation("Login successful for username: {Username}", request.Username);
                return Ok(ApiResult<AuthResponse>.SuccessResult(result));
            }
            else
            {
                _logger.LogWarning("Login failed for username: {Username}", request.Username);
                return Unauthorized(ApiResult<AuthResponse>.ErrorResult(result.ErrorMessage ?? "Invalid credentials"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during login: {Username}", request.Username);
            return StatusCode(500, ApiResult<AuthResponse>.ErrorResult("An internal error occurred"));
        }
    }

    /// <summary>
    /// Gets the current user's profile information
    /// </summary>
    /// <returns>Current user information</returns>
    /// <response code="200">User profile retrieved successfully</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">User not found</response>
    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResult<UserDto>), 200)]
    [ProducesResponseType(typeof(ApiResult<object>), 401)]
    [ProducesResponseType(typeof(ApiResult<object>), 404)]
    public async Task<ActionResult<ApiResult<UserDto>>> GetProfile()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("Invalid user ID claim in token");
                return Unauthorized(ApiResult<UserDto>.ErrorResult("Invalid token"));
            }

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User not found for ID: {UserId}", userId);
                return NotFound(ApiResult<UserDto>.ErrorResult("User not found"));
            }

            return Ok(ApiResult<UserDto>.SuccessResult(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user profile");
            return StatusCode(500, ApiResult<UserDto>.ErrorResult("An internal error occurred"));
        }
    }

    /// <summary>
    /// Changes the current user's password
    /// </summary>
    /// <param name="request">Current and new password</param>
    /// <returns>Success confirmation</returns>
    /// <response code="200">Password changed successfully</response>
    /// <response code="400">Invalid password data</response>
    /// <response code="401">Not authenticated or invalid current password</response>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResult), 200)]
    [ProducesResponseType(typeof(ApiResult), 400)]
    [ProducesResponseType(typeof(ApiResult), 401)]
    public async Task<ActionResult<ApiResult>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(ApiResult.ErrorResult($"Validation failed: {string.Join(", ", errors)}"));
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(ApiResult.ErrorResult("Invalid token"));
            }

            _logger.LogInformation("Password change attempt for user: {UserId}", userId);
            var success = await _userService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);

            if (success)
            {
                _logger.LogInformation("Password changed successfully for user: {UserId}", userId);
                return Ok(ApiResult.SuccessResult());
            }
            else
            {
                _logger.LogWarning("Password change failed for user: {UserId}", userId);
                return Unauthorized(ApiResult.ErrorResult("Current password is incorrect"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return StatusCode(500, ApiResult.ErrorResult("An internal error occurred"));
        }
    }

    /// <summary>
    /// Validates the current JWT token
    /// </summary>
    /// <returns>Token validation result</returns>
    /// <response code="200">Token is valid</response>
    /// <response code="401">Token is invalid or expired</response>
    [HttpGet("validate-token")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResult<UserDto>), 200)]
    [ProducesResponseType(typeof(ApiResult), 401)]
    public async Task<ActionResult<ApiResult<UserDto>>> ValidateToken()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(ApiResult<UserDto>.ErrorResult("Invalid token"));
            }

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized(ApiResult<UserDto>.ErrorResult("User no longer exists"));
            }

            return Ok(ApiResult<UserDto>.SuccessResult(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return StatusCode(500, ApiResult<UserDto>.ErrorResult("An internal error occurred"));
        }
    }
}

public class ApiResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }

    public static ApiResult<T> SuccessResult(T data)
    {
        return new ApiResult<T> { Success = true, Data = data };
    }

    public static ApiResult<T> ErrorResult(string error)
    {
        return new ApiResult<T> { Success = false, Error = error };
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
