using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Helpers;
using VocabularyApp.WebApi.Security;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Services;

public sealed class AuthenticationLoggingTests : IClassFixture<RelationalDatabaseFixture>
{
    private readonly RelationalDatabaseFixture _fixture;

    public AuthenticationLoggingTests(RelationalDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SuccessfulRegistrationDoesNotLogPlaintextPassword()
    {
        const string password = "registration-plaintext-sentinel-8f7625";
        var suffix = Guid.NewGuid().ToString("N");
        var logger = new CapturingLogger<UserService>();

        await using var context = _fixture.CreateContext();
        var result = await CreateUserService(context, logger).CreateUserAsync(new CreateUserRequest
        {
            Username = $"logging-registration-{suffix}",
            Email = $"logging-registration-{suffix}@example.test",
            Password = password
        });

        Assert.True(result.Success);
        AssertLogsExclude(logger, password);
    }

    [Fact]
    public async Task SuccessfulLoginDoesNotLogPlaintextPasswordOrStoredHash()
    {
        const string password = "login-plaintext-sentinel-96bff1";
        var hasher = new PasswordHasher<User>();
        var user = CreateTestUser("placeholder");
        user.PasswordHash = hasher.HashPassword(user, password);
        var storedHash = user.PasswordHash;
        await SeedUserAsync(user);
        var logger = new CapturingLogger<UserService>();

        await using var context = _fixture.CreateContext();
        var result = await CreateUserService(context, logger).LoginAsync(new LoginRequest
        {
            Username = user.Username,
            Password = password
        });

        Assert.True(result.Success);
        AssertLogsExclude(logger, password, storedHash);
    }

    [Fact]
    public async Task FailedLoginDoesNotLogSubmittedPasswordOrStoredHash()
    {
        const string correctPassword = "correct-password-sentinel-a38387";
        const string wrongPassword = "wrong-password-sentinel-bd27c2";
        var hasher = new PasswordHasher<User>();
        var user = CreateTestUser("placeholder");
        user.PasswordHash = hasher.HashPassword(user, correctPassword);
        var storedHash = user.PasswordHash;
        await SeedUserAsync(user);
        var logger = new CapturingLogger<UserService>();

        await using var context = _fixture.CreateContext();
        var result = await CreateUserService(context, logger).LoginAsync(new LoginRequest
        {
            Username = user.Username,
            Password = wrongPassword
        });

        Assert.False(result.Success);
        AssertLogsExclude(logger, correctPassword, wrongPassword, storedHash);
    }

    [Fact]
    public async Task PasswordChangeDoesNotLogCurrentNewOrStoredCredentials()
    {
        const string currentPassword = "current-password-sentinel-c0aae4";
        const string newPassword = "new-password-sentinel-e3ee7e";
        var hasher = new PasswordHasher<User>();
        var user = CreateTestUser("placeholder");
        user.PasswordHash = hasher.HashPassword(user, currentPassword);
        var storedHash = user.PasswordHash;
        await SeedUserAsync(user);
        var logger = new CapturingLogger<UserService>();

        await using var context = _fixture.CreateContext();
        var result = await CreateUserService(context, logger)
            .ChangePasswordAsync(user.Id, currentPassword, newPassword);

        Assert.True(result);
        AssertLogsExclude(logger, currentPassword, newPassword, storedHash);
    }

    [Fact]
    public async Task MalformedCredentialLoggingDoesNotExposeStoredValueOrPassword()
    {
        const string malformedHash = "malformed-hash-sentinel:67b70a";
        const string password = "malformed-password-sentinel-98591e";
        var user = CreateTestUser(malformedHash);
        await SeedUserAsync(user);
        var logger = new CapturingLogger<UserService>();

        await using var context = _fixture.CreateContext();
        var result = await CreateUserService(context, logger).LoginAsync(new LoginRequest
        {
            Username = user.Username,
            Password = password
        });

        Assert.False(result.Success);
        AssertLogsExclude(logger, malformedHash, password);
    }

    [Fact]
    public async Task LegacyMigrationFailureLogsOnlySafeCredentialIdentifiers()
    {
        const string password = "migration-password-sentinel-0752ce";
        const string replacementHash = "replacement-hash-sentinel-c79279";
        var user = CreateTestUser(CreateHistoricalHash(password));
        var storedHash = user.PasswordHash;
        await SeedUserAsync(user);
        var logger = new CapturingLogger<UserService>();
        var controlledHasher = new ControlledPasswordHasher
        {
            HashPasswordResult = replacementHash
        };

        await using var context = _fixture.CreateContext(new ThrowOnSaveInterceptor());
        var result = await CreateUserService(
                context,
                logger,
                new PasswordService(controlledHasher, new LegacyPasswordVerifier()))
            .LoginAsync(new LoginRequest { Username = user.Username, Password = password });

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains(user.Id.ToString()));
        AssertLogsExclude(logger, password, storedHash, replacementHash);
    }

    [Fact]
    public async Task RehashFailureDoesNotLogStoredOrReplacementCredentials()
    {
        const string password = "rehash-password-sentinel-079b74";
        const string storedHash = "stored-hash-sentinel-84c63f";
        const string replacementHash = "replacement-hash-sentinel-fbc275";
        var user = CreateTestUser(storedHash);
        await SeedUserAsync(user);
        var logger = new CapturingLogger<UserService>();
        var controlledHasher = new ControlledPasswordHasher
        {
            VerificationResult = PasswordVerificationResult.SuccessRehashNeeded,
            HashPasswordResult = replacementHash
        };

        await using var context = _fixture.CreateContext(new ThrowOnSaveInterceptor());
        var result = await CreateUserService(
                context,
                logger,
                new PasswordService(controlledHasher, new LegacyPasswordVerifier()))
            .LoginAsync(new LoginRequest { Username = user.Username, Password = password });

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains(user.Id.ToString()));
        AssertLogsExclude(logger, password, storedHash, replacementHash);
    }

    [Fact]
    public async Task ConcurrencyLoggingDoesNotExposeCredentialValues()
    {
        const string password = "concurrency-password-sentinel-a21695";
        const string replacementHash = "stale-replacement-sentinel-d53d24";
        const string newerHash = "newer-hash-sentinel-654891";
        var user = CreateTestUser(CreateHistoricalHash(password));
        var storedHash = user.PasswordHash;
        await SeedUserAsync(user);
        var logger = new CapturingLogger<UserService>();
        var controlledHasher = new ControlledPasswordHasher
        {
            HashPasswordFactory = (_, _) =>
            {
                using var newerContext = _fixture.CreateContext();
                var newerUser = newerContext.Users.Single(candidate => candidate.Id == user.Id);
                newerUser.PasswordHash = newerHash;
                newerContext.SaveChanges();
                return replacementHash;
            }
        };

        await using var staleContext = _fixture.CreateContext();
        var result = await CreateUserService(
                staleContext,
                logger,
                new PasswordService(controlledHasher, new LegacyPasswordVerifier()))
            .LoginAsync(new LoginRequest { Username = user.Username, Password = password });

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("concurrently", StringComparison.OrdinalIgnoreCase)
                && entry.Message.Contains(user.Id.ToString()));
        AssertLogsExclude(logger, password, storedHash, replacementHash, newerHash);
    }

    private static UserService CreateUserService(
        ApplicationDbContext context,
        CapturingLogger<UserService> logger,
        IPasswordService? passwordService = null)
    {
        passwordService ??= new PasswordService(
            new PasswordHasher<User>(),
            new LegacyPasswordVerifier());

        return new UserService(
            context,
            new JwtHelper(TestJwtSettingsFactory.Create(), new CapturingLogger<JwtHelper>()),
            passwordService,
            logger);
    }

    private static void AssertLogsExclude(
        CapturingLogger<UserService> logger,
        params string[] secrets)
    {
        foreach (var entry in logger.Entries)
        {
            var exceptionText = entry.Exception?.ToString() ?? string.Empty;
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, exceptionText, StringComparison.Ordinal);
            }
        }
    }

    private async Task SeedUserAsync(User user)
    {
        await using var context = _fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    private static User CreateTestUser(string passwordHash)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new User
        {
            Username = $"authentication-logging-{suffix}",
            Email = $"authentication-logging-{suffix}@example.test",
            PasswordHash = passwordHash
        };
    }

    private static string CreateHistoricalHash(string password)
    {
        var saltBytes = Enumerable.Range(128, 32).Select(value => (byte)value).ToArray();
        var saltBase64Text = Convert.ToBase64String(saltBytes);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(password + saltBase64Text));
        return $"{saltBase64Text}:{Convert.ToBase64String(digest)}";
    }

    private sealed class ThrowOnSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Controlled persistence failure without credential data.");
    }
}
