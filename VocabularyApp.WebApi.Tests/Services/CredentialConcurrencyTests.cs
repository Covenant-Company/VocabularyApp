using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.Helpers;
using VocabularyApp.WebApi.Security;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Services;

public sealed class CredentialConcurrencyTests : IClassFixture<RelationalDatabaseFixture>
{
    private readonly RelationalDatabaseFixture _fixture;

    public CredentialConcurrencyTests(RelationalDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StalePasswordChangeCannotOverwriteNewerPassword()
    {
        var userId = await SeedUserAsync("original-hash");

        await using var staleContext = _fixture.CreateContext();
        await using var newerContext = _fixture.CreateContext();
        var staleUser = await staleContext.Users.SingleAsync(user => user.Id == userId);
        var newerUser = await newerContext.Users.SingleAsync(user => user.Id == userId);

        newerUser.PasswordHash = "newer-hash";
        await newerContext.SaveChangesAsync();

        staleUser.PasswordHash = "stale-replacement-hash";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => staleContext.SaveChangesAsync());

        await using var verificationContext = _fixture.CreateContext();
        var persistedHash = await verificationContext.Users
            .Where(user => user.Id == userId)
            .Select(user => user.PasswordHash)
            .SingleAsync();

        Assert.Equal("newer-hash", persistedHash);
        Assert.NotEqual("stale-replacement-hash", persistedHash);
    }

    [Fact]
    public async Task ChangePasswordAsyncReturnsFalseWhenCredentialsChangeConcurrently()
    {
        const string currentPassword = "Current password!";
        const string newerPassword = "Newer password!";
        const string staleReplacementPassword = "Stale replacement password!";
        var hasher = new PasswordHasher<User>();
        var user = CreateTestUser("placeholder");
        user.PasswordHash = hasher.HashPassword(user, currentPassword);
        var userId = await SeedUserAsync(user);
        string? newerHash = null;

        var controlledHasher = new ControlledPasswordHasher(hasher)
        {
            HashPasswordFactory = (_, password) =>
            {
                using var newerContext = _fixture.CreateContext();
                var newerUser = newerContext.Users.Single(candidate => candidate.Id == userId);
                newerHash = hasher.HashPassword(newerUser, newerPassword);
                newerUser.PasswordHash = newerHash;
                newerContext.SaveChanges();

                return hasher.HashPassword(user, password);
            }
        };
        var passwordService = new PasswordService(
            controlledHasher,
            new LegacyPasswordVerifier());

        bool result;
        await using (var staleContext = _fixture.CreateContext())
        {
            result = await CreateUserService(staleContext, passwordService)
                .ChangePasswordAsync(userId, currentPassword, staleReplacementPassword);
        }

        await using var verificationContext = _fixture.CreateContext();
        var persistedUser = await verificationContext.Users.SingleAsync(candidate => candidate.Id == userId);

        Assert.False(result);
        Assert.Equal(newerHash, persistedUser.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, newerPassword));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(
                persistedUser,
                persistedUser.PasswordHash,
                staleReplacementPassword));
    }

    [Fact]
    public async Task ChangePasswordAsyncStillSucceedsWithoutConcurrentCredentialChange()
    {
        const string currentPassword = "Current password!";
        const string newPassword = "New password!";
        var hasher = new PasswordHasher<User>();
        var user = CreateTestUser("placeholder");
        user.PasswordHash = hasher.HashPassword(user, currentPassword);
        var userId = await SeedUserAsync(user);

        bool result;
        await using (var context = _fixture.CreateContext())
        {
            result = await CreateUserService(context)
                .ChangePasswordAsync(userId, currentPassword, newPassword);
        }

        await using var verificationContext = _fixture.CreateContext();
        var persistedUser = await verificationContext.Users.SingleAsync(candidate => candidate.Id == userId);

        Assert.True(result);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, newPassword));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, currentPassword));
    }

    private static UserService CreateUserService(
        ApplicationDbContext context,
        IPasswordService? passwordService = null)
    {
        var jwtHelper = new JwtHelper(
            TestJwtSettingsFactory.Create(),
            new CapturingLogger<JwtHelper>());
        passwordService ??= new PasswordService(
            new PasswordHasher<User>(),
            new LegacyPasswordVerifier());

        return new UserService(
            context,
            jwtHelper,
            passwordService,
            new CapturingLogger<UserService>());
    }

    private async Task<int> SeedUserAsync(string passwordHash) =>
        await SeedUserAsync(CreateTestUser(passwordHash));

    private async Task<int> SeedUserAsync(User user)
    {
        await using var context = _fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private static User CreateTestUser(string passwordHash)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new User
        {
            Username = $"credential-concurrency-{suffix}",
            Email = $"credential-concurrency-{suffix}@example.test",
            PasswordHash = passwordHash
        };
    }
}
