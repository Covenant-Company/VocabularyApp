using VocabularyApp.WebApi.Security;

namespace VocabularyApp.WebApi.Tests.Security;

public sealed class PasswordVerificationOutcomeTests
{
    [Theory]
    [MemberData(nameof(OutcomesWithoutReplacement))]
    public void NonreplacementOutcomesContainNoReplacementHash(
        PasswordVerificationOutcome outcome,
        PasswordVerificationStatus expectedStatus)
    {
        Assert.Equal(expectedStatus, outcome.Status);
        Assert.False(outcome.RequiresReplacement);
        Assert.Null(outcome.ReplacementHash);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiredOutcomeContainsReplacementWithoutExposingItThroughToString(
        bool isLegacyMigration)
    {
        const string replacementHash = "future-modern-replacement-placeholder";
        var outcome = isLegacyMigration
            ? PasswordVerificationOutcome.LegacyMigrationRequired(replacementHash)
            : PasswordVerificationOutcome.RehashRequired(replacementHash);

        Assert.True(outcome.RequiresReplacement);
        Assert.Equal(replacementHash, outcome.ReplacementHash);
        Assert.DoesNotContain(replacementHash, outcome.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiredOutcomeRejectsMissingReplacementHash(string? replacementHash)
    {
        Assert.Throws<ArgumentException>(() =>
            PasswordVerificationOutcome.RehashRequired(replacementHash!));
        Assert.Throws<ArgumentException>(() =>
            PasswordVerificationOutcome.LegacyMigrationRequired(replacementHash!));
    }

    public static TheoryData<PasswordVerificationOutcome, PasswordVerificationStatus>
        OutcomesWithoutReplacement => new()
        {
            { PasswordVerificationOutcome.Failure(), PasswordVerificationStatus.Failed },
            { PasswordVerificationOutcome.Success(), PasswordVerificationStatus.Succeeded },
            {
                PasswordVerificationOutcome.MalformedOrUnknown(),
                PasswordVerificationStatus.MalformedOrUnknown
            }
        };
}
