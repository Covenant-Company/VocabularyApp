using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.Security;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Security;

public sealed class PasswordServiceTests
{
    private const string Password = "Modern test password!";
    private static readonly byte[] LegacySalt = Enumerable.Range(32, 32)
        .Select(value => (byte)value)
        .ToArray();

    private readonly LegacyPasswordVerifier _legacyVerifier = new();
    private readonly User _user = new()
    {
        Id = 42,
        Username = "password-service-user",
        Email = "password-service-user@example.test",
        PasswordHash = "unused"
    };

    [Fact]
    public void HashPasswordCreatesFrameworkHashThatIsModernAndVerifiable()
    {
        var modernHasher = new PasswordHasher<User>();
        var service = new PasswordService(modernHasher, _legacyVerifier);

        var storedHash = service.HashPassword(_user, Password);

        Assert.False(string.IsNullOrWhiteSpace(storedHash));
        Assert.False(_legacyVerifier.IsLegacyFormat(storedHash));
        Assert.Equal(
            PasswordVerificationResult.Success,
            modernHasher.VerifyHashedPassword(_user, storedHash, Password));
    }

    [Fact]
    public void ModernHashWithCorrectPasswordReturnsOrdinarySuccess()
    {
        var modernHasher = new PasswordHasher<User>();
        var service = new PasswordService(modernHasher, _legacyVerifier);
        var storedHash = modernHasher.HashPassword(_user, Password);

        var outcome = service.Verify(_user, storedHash, Password);

        Assert.Equal(PasswordVerificationStatus.Succeeded, outcome.Status);
        Assert.False(outcome.RequiresReplacement);
        Assert.Null(outcome.ReplacementHash);
    }

    [Fact]
    public void ModernHashWithWrongPasswordReturnsFailureWithoutReplacement()
    {
        var modernHasher = new PasswordHasher<User>();
        var service = new PasswordService(modernHasher, _legacyVerifier);
        var storedHash = modernHasher.HashPassword(_user, Password);

        var outcome = service.Verify(_user, storedHash, "wrong password");

        Assert.Equal(PasswordVerificationStatus.Failed, outcome.Status);
        Assert.False(outcome.RequiresReplacement);
        Assert.Null(outcome.ReplacementHash);
    }

    [Fact]
    public void SuccessRehashNeededReturnsControlledReplacement()
    {
        const string replacementHash = "controlled-modern-replacement";
        var modernHasher = new ControlledPasswordHasher
        {
            VerificationResult = PasswordVerificationResult.SuccessRehashNeeded,
            HashPasswordResult = replacementHash
        };
        var service = new PasswordService(modernHasher, _legacyVerifier);

        var outcome = service.Verify(_user, "no-colon-modern-value", Password);

        Assert.Equal(PasswordVerificationStatus.SucceededRehashRequired, outcome.Status);
        Assert.True(outcome.RequiresReplacement);
        Assert.Equal(replacementHash, outcome.ReplacementHash);
        Assert.Equal(1, modernHasher.VerifyHashedPasswordCallCount);
        Assert.Equal(1, modernHasher.HashPasswordCallCount);
    }

    [Fact]
    public void LegacySuccessReturnsVerifiableModernReplacementWithoutModernFallback()
    {
        var realModernHasher = new PasswordHasher<User>();
        var trackingModernHasher = new ControlledPasswordHasher(realModernHasher);
        var service = new PasswordService(trackingModernHasher, _legacyVerifier);
        var storedHash = CreateHistoricalHash(Password, LegacySalt);

        var outcome = service.Verify(_user, storedHash, Password);

        Assert.Equal(
            PasswordVerificationStatus.SucceededLegacyMigrationRequired,
            outcome.Status);
        Assert.True(outcome.RequiresReplacement);
        Assert.NotNull(outcome.ReplacementHash);
        Assert.False(_legacyVerifier.IsLegacyFormat(outcome.ReplacementHash));
        Assert.Equal(0, trackingModernHasher.VerifyHashedPasswordCallCount);
        Assert.Equal(1, trackingModernHasher.HashPasswordCallCount);
        Assert.Equal(
            PasswordVerificationResult.Success,
            realModernHasher.VerifyHashedPassword(_user, outcome.ReplacementHash, Password));
    }

    [Fact]
    public void LegacyWrongPasswordReturnsFailureWithoutModernFallback()
    {
        var modernHasher = new ControlledPasswordHasher
        {
            VerificationException = new InvalidOperationException(
                "Modern verification must not run for a strict legacy value.")
        };
        var service = new PasswordService(modernHasher, _legacyVerifier);
        var storedHash = CreateHistoricalHash(Password, LegacySalt);

        var outcome = service.Verify(_user, storedHash, "wrong password");

        Assert.Equal(PasswordVerificationStatus.Failed, outcome.Status);
        Assert.False(outcome.RequiresReplacement);
        Assert.Null(outcome.ReplacementHash);
        Assert.Equal(0, modernHasher.VerifyHashedPasswordCallCount);
        Assert.Equal(0, modernHasher.HashPasswordCallCount);
    }

    [Theory]
    [MemberData(nameof(MalformedColonBearingValues))]
    public void MalformedColonBearingValueReturnsUnknownWithoutModernVerification(
        string storedHash)
    {
        var modernHasher = new ControlledPasswordHasher
        {
            VerificationException = new InvalidOperationException(
                "Modern verification must not run for colon-bearing malformed data.")
        };
        var service = new PasswordService(modernHasher, _legacyVerifier);

        var outcome = service.Verify(_user, storedHash, Password);

        Assert.Equal(PasswordVerificationStatus.MalformedOrUnknown, outcome.Status);
        Assert.False(outcome.RequiresReplacement);
        Assert.Null(outcome.ReplacementHash);
        Assert.Equal(0, modernHasher.VerifyHashedPasswordCallCount);
        Assert.Equal(0, modernHasher.HashPasswordCallCount);
    }

    [Fact]
    public void UnsupportedNoColonValueReturningFailedMapsToFailure()
    {
        var modernHasher = new ControlledPasswordHasher
        {
            VerificationResult = PasswordVerificationResult.Failed
        };
        var service = new PasswordService(modernHasher, _legacyVerifier);

        var outcome = service.Verify(_user, "unsupported-no-colon-value", Password);

        Assert.Equal(PasswordVerificationStatus.Failed, outcome.Status);
        Assert.Null(outcome.ReplacementHash);
        Assert.Equal(1, modernHasher.VerifyHashedPasswordCallCount);
    }

    [Fact]
    public void MalformedModernFormatExceptionMapsToUnknownWithoutReplacement()
    {
        var modernHasher = new ControlledPasswordHasher
        {
            VerificationException = new FormatException("Malformed encoded payload.")
        };
        var service = new PasswordService(modernHasher, _legacyVerifier);

        var outcome = service.Verify(_user, "malformed-modern-payload", Password);

        Assert.Equal(PasswordVerificationStatus.MalformedOrUnknown, outcome.Status);
        Assert.False(outcome.RequiresReplacement);
        Assert.Null(outcome.ReplacementHash);
        Assert.Equal(1, modernHasher.VerifyHashedPasswordCallCount);
        Assert.Equal(0, modernHasher.HashPasswordCallCount);
    }

    [Fact]
    public void RealMalformedModernPayloadFailsSafely()
    {
        var service = new PasswordService(new PasswordHasher<User>(), _legacyVerifier);

        var exception = Record.Exception(() =>
            service.Verify(_user, "not-valid-base64!", Password));
        var outcome = service.Verify(_user, "not-valid-base64!", Password);

        Assert.Null(exception);
        Assert.Equal(PasswordVerificationStatus.MalformedOrUnknown, outcome.Status);
        Assert.Null(outcome.ReplacementHash);
    }

    [Fact]
    public void UnexpectedModernVerifierExceptionIsNotSwallowed()
    {
        var modernHasher = new ControlledPasswordHasher
        {
            VerificationException = new InvalidOperationException("Unexpected failure.")
        };
        var service = new PasswordService(modernHasher, _legacyVerifier);

        Assert.Throws<InvalidOperationException>(() =>
            service.Verify(_user, "modern-looking-value", Password));
    }

    [Fact]
    public void NullStoredHashReturnsUnknownWithoutCallingEitherVerifier()
    {
        var legacyVerifier = new ControlledLegacyPasswordVerifier();
        var modernHasher = new ControlledPasswordHasher
        {
            VerificationException = new InvalidOperationException("Must not be called.")
        };
        var service = new PasswordService(modernHasher, legacyVerifier);

        var outcome = service.Verify(_user, null!, Password);

        Assert.Equal(PasswordVerificationStatus.MalformedOrUnknown, outcome.Status);
        Assert.Equal(0, legacyVerifier.IsLegacyFormatCallCount);
        Assert.Equal(0, legacyVerifier.VerifyCallCount);
        Assert.Equal(0, modernHasher.VerifyHashedPasswordCallCount);
    }

    public static TheoryData<string> MalformedColonBearingValues
    {
        get
        {
            var canonicalHash = CreateHistoricalHash(Password, LegacySalt);
            var parts = canonicalHash.Split(':');

            return new TheoryData<string>
            {
                $"!{parts[0][1..]}:{parts[1]}",
                $"{parts[0][1..]}:{parts[1]}",
                canonicalHash + ":extra",
                $"{CreateNoncanonicalEncoding(parts[0])}:{parts[1]}"
            };
        }
    }

    private static string CreateHistoricalHash(string password, byte[] saltBytes)
    {
        var saltBase64Text = Convert.ToBase64String(saltBytes);
        var input = Encoding.UTF8.GetBytes(password + saltBase64Text);
        var digest = SHA256.HashData(input);

        return $"{saltBase64Text}:{Convert.ToBase64String(digest)}";
    }

    private static string CreateNoncanonicalEncoding(string canonical)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var characters = canonical.ToCharArray();
        var finalDataIndex = canonical.IndexOf('=') - 1;
        var canonicalValue = alphabet.IndexOf(characters[finalDataIndex]);
        characters[finalDataIndex] = alphabet[canonicalValue + 1];

        return new string(characters);
    }

    private sealed class ControlledLegacyPasswordVerifier : ILegacyPasswordVerifier
    {
        public int IsLegacyFormatCallCount { get; private set; }

        public int VerifyCallCount { get; private set; }

        public bool IsLegacyFormat(string? storedHash)
        {
            IsLegacyFormatCallCount++;
            return false;
        }

        public bool Verify(string? password, string? storedHash)
        {
            VerifyCallCount++;
            return false;
        }
    }
}
