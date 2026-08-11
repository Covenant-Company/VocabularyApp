namespace VocabularyApp.WebApi.Security;

public enum PasswordVerificationStatus
{
    Failed,
    Succeeded,
    SucceededRehashRequired,
    SucceededLegacyMigrationRequired,
    MalformedOrUnknown
}
