using Microsoft.AspNetCore.Identity;
using VocabularyApp.Data.Models;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class ControlledPasswordHasher : IPasswordHasher<User>
{
    private readonly IPasswordHasher<User>? _inner;

    public ControlledPasswordHasher(IPasswordHasher<User>? inner = null)
    {
        _inner = inner;
    }

    public string? HashPasswordResult { get; set; }

    public Func<User, string, string>? HashPasswordFactory { get; set; }

    public PasswordVerificationResult? VerificationResult { get; set; }

    public Exception? VerificationException { get; set; }

    public int HashPasswordCallCount { get; private set; }

    public int VerifyHashedPasswordCallCount { get; private set; }

    public string HashPassword(User user, string password)
    {
        HashPasswordCallCount++;

        if (HashPasswordFactory is not null)
        {
            return HashPasswordFactory(user, password);
        }

        if (HashPasswordResult is not null)
        {
            return HashPasswordResult;
        }

        return _inner?.HashPassword(user, password)
            ?? throw new InvalidOperationException("No controlled or inner hash result was configured.");
    }

    public PasswordVerificationResult VerifyHashedPassword(
        User user,
        string hashedPassword,
        string providedPassword)
    {
        VerifyHashedPasswordCallCount++;

        if (VerificationException is not null)
        {
            throw VerificationException;
        }

        if (VerificationResult.HasValue)
        {
            return VerificationResult.Value;
        }

        return _inner?.VerifyHashedPassword(user, hashedPassword, providedPassword)
            ?? throw new InvalidOperationException("No controlled or inner verification result was configured.");
    }
}
