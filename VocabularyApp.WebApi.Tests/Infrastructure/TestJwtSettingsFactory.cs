using VocabularyApp.WebApi.Configuration;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public static class TestJwtSettingsFactory
{
    public static JwtSettings Create() => new()
    {
        SecretKey = "fake-test-signing-key-that-is-at-least-thirty-two-bytes-long",
        Issuer = "VocabularyApp.Tests",
        Audience = "VocabularyApp.Tests",
        ExpirationMinutes = 15
    };
}
