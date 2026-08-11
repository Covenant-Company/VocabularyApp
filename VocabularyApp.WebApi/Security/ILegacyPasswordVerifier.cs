namespace VocabularyApp.WebApi.Security;

/// <summary>
/// Temporarily verifies password hashes created by the application's historical
/// salted SHA-256 implementation during the migration window.
/// </summary>
public interface ILegacyPasswordVerifier
{
    bool IsLegacyFormat(string? storedHash);

    bool Verify(string? password, string? storedHash);
}
