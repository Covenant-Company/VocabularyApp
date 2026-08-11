using System.Security.Cryptography;
using System.Text;

namespace VocabularyApp.WebApi.Security;

/// <summary>
/// Verification-only compatibility component for historical password hashes.
/// Remove after the documented legacy migration completion criteria are met.
/// </summary>
public sealed class LegacyPasswordVerifier : ILegacyPasswordVerifier
{
    private const int EncodedSegmentLength = 44;
    private const int DecodedSegmentLength = 32;

    public bool IsLegacyFormat(string? storedHash) =>
        TryParse(storedHash, out _, out _);

    public bool Verify(string? password, string? storedHash)
    {
        if (password is null ||
            !TryParse(storedHash, out var saltBase64Text, out var storedDigest))
        {
            return false;
        }

        var passwordAndSaltBytes = Encoding.UTF8.GetBytes(password + saltBase64Text);
        var computedDigest = SHA256.HashData(passwordAndSaltBytes);

        return CryptographicOperations.FixedTimeEquals(computedDigest, storedDigest);
    }

    private static bool TryParse(
        string? storedHash,
        out string saltBase64Text,
        out byte[] storedDigest)
    {
        saltBase64Text = string.Empty;
        storedDigest = Array.Empty<byte>();

        if (string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var separatorIndex = storedHash.IndexOf(':');
        if (separatorIndex != EncodedSegmentLength ||
            separatorIndex != storedHash.LastIndexOf(':') ||
            storedHash.Length != (EncodedSegmentLength * 2) + 1)
        {
            return false;
        }

        var saltText = storedHash[..separatorIndex];
        var digestText = storedHash[(separatorIndex + 1)..];

        if (!TryDecodeCanonicalSegment(saltText, out var saltBytes) ||
            !TryDecodeCanonicalSegment(digestText, out var digestBytes) ||
            saltBytes.Length != DecodedSegmentLength ||
            digestBytes.Length != DecodedSegmentLength)
        {
            return false;
        }

        saltBase64Text = saltText;
        storedDigest = digestBytes;
        return true;
    }

    private static bool TryDecodeCanonicalSegment(string encoded, out byte[] decoded)
    {
        decoded = new byte[DecodedSegmentLength + 1];

        if (!Convert.TryFromBase64String(encoded, decoded, out var bytesWritten))
        {
            decoded = Array.Empty<byte>();
            return false;
        }

        Array.Resize(ref decoded, bytesWritten);

        if (!string.Equals(
                Convert.ToBase64String(decoded),
                encoded,
                StringComparison.Ordinal))
        {
            decoded = Array.Empty<byte>();
            return false;
        }

        return true;
    }
}
