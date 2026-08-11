using System.Diagnostics;

namespace VocabularyApp.WebApi.Security;

[DebuggerDisplay("{Status}")]
public sealed class PasswordVerificationOutcome
{
    private PasswordVerificationOutcome(
        PasswordVerificationStatus status,
        string? replacementHash = null)
    {
        Status = status;
        ReplacementHash = replacementHash;
    }

    public PasswordVerificationStatus Status { get; }

    public string? ReplacementHash { get; }

    public bool RequiresReplacement =>
        Status is PasswordVerificationStatus.SucceededRehashRequired
            or PasswordVerificationStatus.SucceededLegacyMigrationRequired;

    public static PasswordVerificationOutcome Failure() =>
        new(PasswordVerificationStatus.Failed);

    public static PasswordVerificationOutcome Success() =>
        new(PasswordVerificationStatus.Succeeded);

    public static PasswordVerificationOutcome RehashRequired(string replacementHash) =>
        CreateRequired(PasswordVerificationStatus.SucceededRehashRequired, replacementHash);

    public static PasswordVerificationOutcome LegacyMigrationRequired(string replacementHash) =>
        CreateRequired(PasswordVerificationStatus.SucceededLegacyMigrationRequired, replacementHash);

    public static PasswordVerificationOutcome MalformedOrUnknown() =>
        new(PasswordVerificationStatus.MalformedOrUnknown);

    public override string ToString() => Status.ToString();

    private static PasswordVerificationOutcome CreateRequired(
        PasswordVerificationStatus status,
        string replacementHash)
    {
        if (string.IsNullOrWhiteSpace(replacementHash))
        {
            throw new ArgumentException(
                "A replacement hash is required for this verification outcome.",
                nameof(replacementHash));
        }

        return new PasswordVerificationOutcome(status, replacementHash);
    }
}
