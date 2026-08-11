using VocabularyApp.Data.Models;

namespace VocabularyApp.WebApi.Security;

public interface IPasswordService
{
    string HashPassword(User user, string password);

    PasswordVerificationOutcome Verify(
        User user,
        string storedHash,
        string password);
}
