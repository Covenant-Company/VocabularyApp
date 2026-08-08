using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace VocabularyApp.WebApi.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";
    private const int MinimumSecretKeyBytes = 32;

    public string SecretKey { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int ExpirationMinutes { get; init; }

    public static JwtSettings BindAndValidate(IConfiguration configuration)
    {
        var settings = configuration.GetSection(SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException($"JWT configuration section '{SectionName}' is missing.");

        if (string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvalidOperationException(
                $"JWT configuration '{SectionName}:SecretKey' is missing or blank. Supply it through external configuration.");
        }

        if (Encoding.UTF8.GetByteCount(settings.SecretKey) < MinimumSecretKeyBytes)
        {
            throw new InvalidOperationException(
                $"JWT configuration '{SectionName}:SecretKey' must contain at least {MinimumSecretKeyBytes} bytes for HS256.");
        }

        if (string.IsNullOrWhiteSpace(settings.Issuer))
        {
            throw new InvalidOperationException($"JWT configuration '{SectionName}:Issuer' is missing or blank.");
        }

        if (string.IsNullOrWhiteSpace(settings.Audience))
        {
            throw new InvalidOperationException($"JWT configuration '{SectionName}:Audience' is missing or blank.");
        }

        if (settings.ExpirationMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"JWT configuration '{SectionName}:ExpirationMinutes' must be a positive whole number.");
        }

        return settings;
    }

    public SymmetricSecurityKey CreateSigningKey() =>
        new(Encoding.UTF8.GetBytes(SecretKey));

    public TokenValidationParameters CreateTokenValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateSigningKey(),
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
    };
}
