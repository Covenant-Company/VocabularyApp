using Microsoft.EntityFrameworkCore;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Helpers;

namespace VocabularyApp.WebApi.Services;

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;
    private readonly JwtHelper _jwtHelper;
    private readonly ILogger<UserService> _logger;

    public UserService(ApplicationDbContext context, JwtHelper jwtHelper, ILogger<UserService> logger)
    {
        _context = context;
        _jwtHelper = jwtHelper;
        _logger = logger;
    }

    public async Task<AuthResponse> CreateUserAsync(CreateUserRequest request)
    {
        try
        {
            // Check if username already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
            
            if (existingUser != null)
            {
                return new AuthResponse
                {
                    Success = false,
                    ErrorMessage = "Username is already taken"
                };
            }

            // Check if email already exists
            var existingEmail = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
            
            if (existingEmail != null)
            {
                return new AuthResponse
                {
                    Success = false,
                    ErrorMessage = "Email is already registered"
                };
            }

            // Create new user
            var hashedPassword = PasswordHelper.HashPassword(request.Password);
            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = hashedPassword,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("New user created: {Username} ({Email})", user.Username, user.Email);

            // Generate JWT token
            var userDto = MapUserToDto(user);
            var token = _jwtHelper.GenerateToken(userDto);

            return new AuthResponse
            {
                Success = true,
                User = userDto,
                Token = token
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user: {Username}", request.Username);
            return new AuthResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        try
        {
            // Find user by username
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());

            if (user == null)
            {
                _logger.LogWarning("Login attempt with non-existent username: {Username}", request.Username);
                return new AuthResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid username or password"
                };
            }

            // Verify password
            if (!PasswordHelper.VerifyPassword(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("Invalid password attempt for user: {Username}", request.Username);
                return new AuthResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid username or password"
                };
            }

            // Update last login
            await UpdateLastLoginAsync(user.Id);

            _logger.LogInformation("Successful login for user: {Username}", user.Username);

            // Generate JWT token
            var userDto = MapUserToDto(user);
            var token = _jwtHelper.GenerateToken(userDto);

            return new AuthResponse
            {
                Success = true,
                User = userDto,
                Token = token
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for username: {Username}", request.Username);
            return new AuthResponse
            {
                Success = false,
                ErrorMessage = "An error occurred during login"
            };
        }
    }

    public async Task<UserDto?> GetUserByIdAsync(int userId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);

        return user != null ? MapUserToDto(user) : null;
    }

    public async Task<UserDto?> GetUserByUsernameAsync(string username)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        return user != null ? MapUserToDto(user) : null;
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user != null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating last login for user: {UserId}", userId);
            // Don't throw - this is not critical for user experience
        }
    }

    public async Task<UserDto?> ValidateTokenAsync(string token)
    {
        try
        {
            var userId = _jwtHelper.GetUserIdFromToken(token);
            if (userId == null)
                return null;

            return await GetUserByIdAsync(userId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return false;

            // Verify current password
            if (!PasswordHelper.VerifyPassword(currentPassword, user.PasswordHash))
            {
                _logger.LogWarning("Invalid current password provided for user: {UserId}", userId);
                return false;
            }

            // Hash new password and update
            user.PasswordHash = PasswordHelper.HashPassword(newPassword);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Password changed successfully for user: {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for user: {UserId}", userId);
            return false;
        }
    }

    private static UserDto MapUserToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        };
    }
}