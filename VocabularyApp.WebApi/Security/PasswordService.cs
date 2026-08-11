using Microsoft.AspNetCore.Identity;
using VocabularyApp.Data.Models;

namespace VocabularyApp.WebApi.Security;

public sealed class PasswordService : IPasswordService
{
    private readonly IPasswordHasher<User> _modernPasswordHasher;
    private readonly ILegacyPasswordVerifier _legacyPasswordVerifier;

    public PasswordService(
        IPasswordHasher<User> modernPasswordHasher,
        ILegacyPasswordVerifier legacyPasswordVerifier)
    {
        _modernPasswordHasher = modernPasswordHasher
            ?? throw new ArgumentNullException(nameof(modernPasswordHasher));
        _legacyPasswordVerifier = legacyPasswordVerifier
            ?? throw new ArgumentNullException(nameof(legacyPasswordVerifier));
    }

    public string HashPassword(User user, string password) =>
        _modernPasswordHasher.HashPassword(user, password);

    public PasswordVerificationOutcome Verify(
        User user,
        string storedHash,
        string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        if (storedHash is null)
        {
            return PasswordVerificationOutcome.MalformedOrUnknown();
        }

        if (_legacyPasswordVerifier.IsLegacyFormat(storedHash))
        {
            if (!_legacyPasswordVerifier.Verify(password, storedHash))
            {
                return PasswordVerificationOutcome.Failure();
            }

            var replacementHash = HashPassword(user, password);
            return PasswordVerificationOutcome.LegacyMigrationRequired(replacementHash);
        }

        if (storedHash.Contains(':'))
        {
            return PasswordVerificationOutcome.MalformedOrUnknown();
        }

        PasswordVerificationResult modernResult;
        try
        {
            modernResult = _modernPasswordHasher.VerifyHashedPassword(
                user,
                storedHash,
                password);
        }
        catch (FormatException)
        {
            return PasswordVerificationOutcome.MalformedOrUnknown();
        }

        return modernResult switch
        {
            PasswordVerificationResult.Failed => PasswordVerificationOutcome.Failure(),
            PasswordVerificationResult.Success => PasswordVerificationOutcome.Success(),
            PasswordVerificationResult.SuccessRehashNeeded =>
                PasswordVerificationOutcome.RehashRequired(HashPassword(user, password)),
            _ => throw new InvalidOperationException(
                $"Unsupported password verification result: {modernResult}.")
        };
    }
}
