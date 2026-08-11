using VocabularyApp.WebApi.DTOs;

namespace VocabularyApp.WebApi.Services;

public interface IUserService
{
    /// <summary>
    /// Creates a new user account with hashed password
    /// </summary>
    Task<AuthResponse> CreateUserAsync(CreateUserRequest request);
    
    /// <summary>
    /// Authenticates user credentials and returns JWT token
    /// </summary>
    Task<AuthResponse> LoginAsync(LoginRequest request);
    
    /// <summary>
    /// Gets user information by ID
    /// </summary>
    Task<UserDto?> GetUserByIdAsync(int userId);
    
    /// <summary>
    /// Gets user information by username
    /// </summary>
    Task<UserDto?> GetUserByUsernameAsync(string username);
    
    /// <summary>
    /// Validates if a JWT token is valid and returns user info
    /// </summary>
    Task<UserDto?> ValidateTokenAsync(string token);
    
    /// <summary>
    /// Changes user password (requires current password verification)
    /// </summary>
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}
