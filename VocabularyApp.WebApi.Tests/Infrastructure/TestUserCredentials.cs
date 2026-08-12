namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed record TestUserCredentials(
    string Username,
    string Email,
    string Password)
{
    public static TestUserCredentials CreateUnique(string prefix = "integration-user")
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new TestUserCredentials(
            $"{prefix}-{suffix}",
            $"{prefix}-{suffix}@example.test",
            "Integration test password!");
    }
}
