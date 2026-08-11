using System.Security.Cryptography;
using System.Text;
using VocabularyApp.WebApi.Security;

namespace VocabularyApp.WebApi.Tests.Security;

public sealed class LegacyPasswordVerifierTests
{
    private const string Password = "Correct horse battery staple!";
    private static readonly byte[] SaltBytes = Enumerable.Range(0, 32)
        .Select(value => (byte)value)
        .ToArray();

    private readonly LegacyPasswordVerifier _verifier = new();

    [Fact]
    public void ExactHistoricalPasswordAndHashSucceed()
    {
        var storedHash = CreateHistoricalHash(Password, SaltBytes);

        Assert.True(_verifier.IsLegacyFormat(storedHash));
        Assert.True(_verifier.Verify(Password, storedHash));
    }

    [Fact]
    public void WrongPasswordFails()
    {
        var storedHash = CreateHistoricalHash(Password, SaltBytes);

        Assert.False(_verifier.Verify("incorrect password", storedHash));
    }

    [Fact]
    public void MissingColonFailsRecognition()
    {
        var storedHash = CreateHistoricalHash(Password, SaltBytes).Replace(":", string.Empty);

        Assert.False(_verifier.IsLegacyFormat(storedHash));
    }

    [Fact]
    public void ExtraColonFailsRecognition()
    {
        var storedHash = CreateHistoricalHash(Password, SaltBytes) + ":extra";

        Assert.False(_verifier.IsLegacyFormat(storedHash));
    }

    [Fact]
    public void InvalidBase64SaltFailsRecognition()
    {
        var parts = CreateHistoricalHash(Password, SaltBytes).Split(':');
        var storedHash = $"!{parts[0][1..]}:{parts[1]}";

        Assert.False(_verifier.IsLegacyFormat(storedHash));
    }

    [Fact]
    public void InvalidBase64DigestFailsRecognition()
    {
        var parts = CreateHistoricalHash(Password, SaltBytes).Split(':');
        var storedHash = $"{parts[0]}:!{parts[1][1..]}";

        Assert.False(_verifier.IsLegacyFormat(storedHash));
    }

    [Fact]
    public void EncodedSaltWithWrongLengthFailsRecognition()
    {
        var parts = CreateHistoricalHash(Password, SaltBytes).Split(':');
        var storedHash = $"{parts[0][1..]}:{parts[1]}";

        Assert.False(_verifier.IsLegacyFormat(storedHash));
    }

    [Fact]
    public void EncodedDigestWithWrongLengthFailsRecognition()
    {
        var parts = CreateHistoricalHash(Password, SaltBytes).Split(':');
        var storedHash = $"{parts[0]}:{parts[1][1..]}";

        Assert.False(_verifier.IsLegacyFormat(storedHash));
    }

    [Fact]
    public void DecodedSaltLengthOtherThan32BytesFailsRecognition()
    {
        var shortSalt = Convert.ToBase64String(new byte[31]);
        var digest = Convert.ToBase64String(new byte[32]);

        Assert.Equal(44, shortSalt.Length);
        Assert.False(_verifier.IsLegacyFormat($"{shortSalt}:{digest}"));
    }

    [Fact]
    public void DecodedDigestLengthOtherThan32BytesFailsRecognition()
    {
        var salt = Convert.ToBase64String(new byte[32]);
        var longDigest = Convert.ToBase64String(new byte[33]);

        Assert.Equal(44, longDigest.Length);
        Assert.False(_verifier.IsLegacyFormat($"{salt}:{longDigest}"));
    }

    [Fact]
    public void NoncanonicalBase64FailsRecognition()
    {
        var parts = CreateHistoricalHash(Password, SaltBytes).Split(':');
        var noncanonicalSalt = CreateNoncanonicalEncoding(parts[0]);

        Assert.Equal(SaltBytes, Convert.FromBase64String(noncanonicalSalt));
        Assert.NotEqual(parts[0], noncanonicalSalt);
        Assert.False(_verifier.IsLegacyFormat($"{noncanonicalSalt}:{parts[1]}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData(":")]
    [InlineData("one:two:three")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!:!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void MalformedInputDoesNotThrow(string? storedHash)
    {
        var recognitionException = Record.Exception(() => _verifier.IsLegacyFormat(storedHash));
        var verificationException = Record.Exception(() => _verifier.Verify(Password, storedHash));

        Assert.Null(recognitionException);
        Assert.Null(verificationException);
        Assert.False(_verifier.IsLegacyFormat(storedHash));
        Assert.False(_verifier.Verify(Password, storedHash));
    }

    [Fact]
    public void NullPasswordFailsWithoutThrowing()
    {
        var storedHash = CreateHistoricalHash(Password, SaltBytes);

        var exception = Record.Exception(() => _verifier.Verify(null, storedHash));

        Assert.Null(exception);
        Assert.False(_verifier.Verify(null, storedHash));
    }

    [Fact]
    public void HashUsingRawSaltBytesDoesNotVerify()
    {
        var saltText = Convert.ToBase64String(SaltBytes);
        var passwordBytes = Encoding.UTF8.GetBytes(Password);
        var input = new byte[passwordBytes.Length + SaltBytes.Length];
        passwordBytes.CopyTo(input, 0);
        SaltBytes.CopyTo(input, passwordBytes.Length);
        var incorrectDigest = SHA256.HashData(input);
        var storedHash = $"{saltText}:{Convert.ToBase64String(incorrectDigest)}";

        Assert.True(_verifier.IsLegacyFormat(storedHash));
        Assert.False(_verifier.Verify(Password, storedHash));
    }

    [Fact]
    public void HashUsingSaltBeforePasswordDoesNotVerify()
    {
        var saltText = Convert.ToBase64String(SaltBytes);
        var incorrectInput = Encoding.UTF8.GetBytes(saltText + Password);
        var incorrectDigest = SHA256.HashData(incorrectInput);
        var storedHash = $"{saltText}:{Convert.ToBase64String(incorrectDigest)}";

        Assert.True(_verifier.IsLegacyFormat(storedHash));
        Assert.False(_verifier.Verify(Password, storedHash));
    }

    private static string CreateHistoricalHash(string password, byte[] saltBytes)
    {
        var saltBase64Text = Convert.ToBase64String(saltBytes);
        var historicalInput = Encoding.UTF8.GetBytes(password + saltBase64Text);
        var digest = SHA256.HashData(historicalInput);

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
}
