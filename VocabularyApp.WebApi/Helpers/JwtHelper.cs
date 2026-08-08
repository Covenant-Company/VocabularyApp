using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.DTOs;

namespace VocabularyApp.WebApi.Helpers;

public class JwtHelper
{
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<JwtHelper> _logger;

    public JwtHelper(JwtSettings jwtSettings, ILogger<JwtHelper> logger)
    {
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    /// <summary>
    /// Generates a JWT token for the authenticated user
    /// </summary>
    public string GenerateToken(UserDto user)
    {
        var key = _jwtSettings.CreateSigningKey();
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("jti", Guid.NewGuid().ToString()) // JWT ID for token uniqueness
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Validates a JWT token and extracts user information
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = _jwtSettings.CreateTokenValidationParameters();

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    /// <summary>
    /// Extracts user ID from JWT token claims
    /// </summary>
    public int? GetUserIdFromToken(string token)
    {
        var principal = ValidateToken(token);
        var userIdClaim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
