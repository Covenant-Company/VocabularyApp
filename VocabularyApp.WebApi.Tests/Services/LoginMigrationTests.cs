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

public sealed class LoginMigrationTests : IClassFixture<RelationalDatabaseFixture>
{
    private readonly RelationalDatabaseFixture _fixture;

    public LoginMigrationTests(RelationalDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LegacyLoginSucceedsAndPersistsModernReplacementBeforeReturningToken()
    {
        const string password = "Legacy login password!";
        var originalHash = CreateHistoricalHash(password);
        var user = CreateTestUser(originalHash);
        await SeedUserAsync(user);

        var result = await LoginAsync(user.Username, password);
        var persistedUser = await ReloadUserAsync(user.Id);

        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.NotNull(persistedUser.LastLoginAt);
        Assert.NotEqual(originalHash, persistedUser.PasswordHash);
        Assert.False(new LegacyPasswordVerifier().IsLegacyFormat(persistedUser.PasswordHash));
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(
                persistedUser,
                persistedUser.PasswordHash,
                password));
    }

    [Fact]
    public async Task WrongLegacyPasswordDoesNotMutateCredentialsOrTimestamp()
    {
        var originalHash = CreateHistoricalHash("Correct legacy password!");
        var user = CreateTestUser(originalHash);
        await SeedUserAsync(user);

        var result = await LoginAsync(user.Username, "wrong password");
        var persistedUser = await ReloadUserAsync(user.Id);

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal(originalHash, persistedUser.PasswordHash);
        Assert.Null(persistedUser.LastLoginAt);
    }

    [Fact]
    public async Task ModernLoginSucceedsWithoutReplacingHashAndUpdatesTimestamp()
    {
        const string password = "Modern login password!";
        var hasher = new PasswordHasher<User>();
        var user = CreateTestUser("placeholder");
        user.PasswordHash = hasher.HashPassword(user, password);
        var originalHash = user.PasswordHash;
        await SeedUserAsync(user);

        var result = await LoginAsync(user.Username, password);
        var persistedUser = await ReloadUserAsync(user.Id);

        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(originalHash, persistedUser.PasswordHash);
        Assert.NotNull(persistedUser.LastLoginAt);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, password));
    }

    [Fact]
    public async Task WrongModernPasswordDoesNotMutateCredentialsOrTimestamp()
    {
        var hasher = new PasswordHasher<User>();
        var user = CreateTestUser("placeholder");
        user.PasswordHash = hasher.HashPassword(user, "Correct modern password!");
        var originalHash = user.PasswordHash;
        await SeedUserAsync(user);

        var result = await LoginAsync(user.Username, "wrong password");
        var persistedUser = await ReloadUserAsync(user.Id);

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal(originalHash, persistedUser.PasswordHash);
        Assert.Null(persistedUser.LastLoginAt);
    }

    [Fact]
    public async Task ModernRehashRequiredPersistsReplacementAndUpdatesTimestamp()
    {
        const string replacementHash = "controlled-modern-replacement-hash";
        var user = CreateTestUser("controlled-current-modern-hash");
        await SeedUserAsync(user);
        var controlledHasher = new ControlledPasswordHasher
        {
            VerificationResult = PasswordVerificationResult.SuccessRehashNeeded,
            HashPasswordResult = replacementHash
        };
        var passwordService = new PasswordService(controlledHasher, new LegacyPasswordVerifier());

        AuthResponse result;
        await using (var context = _fixture.CreateContext())
        {
            result = await CreateUserService(context, passwordService)
                .LoginAsync(new LoginRequest { Username = user.Username, Password = "password" });
        }

        var persistedUser = await ReloadUserAsync(user.Id);
        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(replacementHash, persistedUser.PasswordHash);
        Assert.NotNull(persistedUser.LastLoginAt);
    }

    [Theory]
    [InlineData("invalid:colon-bearing-value")]
    [InlineData("unsupported-no-colon-value")]
    public async Task MalformedOrUnknownHashFailsWithoutMutation(string storedHash)
    {
        var user = CreateTestUser(storedHash);
        await SeedUserAsync(user);

        var result = await LoginAsync(user.Username, "any password");
        var persistedUser = await ReloadUserAsync(user.Id);

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal("Invalid username or password", result.ErrorMessage);
        Assert.Equal(storedHash, persistedUser.PasswordHash);
        Assert.Null(persistedUser.LastLoginAt);
    }

    [Fact]
    public async Task RequiredLegacyMigrationSaveFailureReturnsNoToken()
    {
        const string password = "Legacy save failure password!";
        var originalHash = CreateHistoricalHash(password);
        var user = CreateTestUser(originalHash);
        await SeedUserAsync(user);

        AuthResponse result;
        await using (var context = _fixture.CreateContext(new ThrowOnSaveInterceptor()))
        {
            result = await CreateUserService(context)
                .LoginAsync(new LoginRequest { Username = user.Username, Password = password });
        }

        var persistedUser = await ReloadUserAsync(user.Id);
        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal(originalHash, persistedUser.PasswordHash);
        Assert.Null(persistedUser.LastLoginAt);
    }

    [Fact]
    public async Task ConcurrentPasswordChangeRejectsStaleLegacyMigrationAndPreservesNewerHash()
    {
        const string password = "Legacy concurrent login password!";
        const string newerPassword = "Newer concurrent password!";
        var user = CreateTestUser(CreateHistoricalHash(password));
        await SeedUserAsync(user);
        var modernHasher = new PasswordHasher<User>();
        string? newerHash = null;
        var controlledHasher = new ControlledPasswordHasher
        {
            HashPasswordFactory = (verifiedUser, verifiedPassword) =>
            {
                using var newerContext = _fixture.CreateContext();
                var newerUser = newerContext.Users.Single(candidate => candidate.Id == user.Id);
                newerHash = modernHasher.HashPassword(newerUser, newerPassword);
                newerUser.PasswordHash = newerHash;
                newerContext.SaveChanges();
                return modernHasher.HashPassword(verifiedUser, verifiedPassword);
            }
        };
        var passwordService = new PasswordService(controlledHasher, new LegacyPasswordVerifier());

        AuthResponse result;
        await using (var staleContext = _fixture.CreateContext())
        {
            result = await CreateUserService(staleContext, passwordService)
                .LoginAsync(new LoginRequest { Username = user.Username, Password = password });
        }

        var persistedUser = await ReloadUserAsync(user.Id);
        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal(newerHash, persistedUser.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            modernHasher.VerifyHashedPassword(
                persistedUser,
                persistedUser.PasswordHash,
                newerPassword));
    }

    private async Task<AuthResponse> LoginAsync(string username, string password)
    {
        await using var context = _fixture.CreateContext();
        return await CreateUserService(context)
            .LoginAsync(new LoginRequest { Username = username, Password = password });
    }

    private static UserService CreateUserService(
        ApplicationDbContext context,
        IPasswordService? passwordService = null)
    {
        passwordService ??= new PasswordService(
            new PasswordHasher<User>(),
            new LegacyPasswordVerifier());

        return new UserService(
            context,
            new JwtHelper(TestJwtSettingsFactory.Create(), new CapturingLogger<JwtHelper>()),
            passwordService,
            new CapturingLogger<UserService>());
    }

    private async Task SeedUserAsync(User user)
    {
        await using var context = _fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    private async Task<User> ReloadUserAsync(int userId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Users.SingleAsync(user => user.Id == userId);
    }

    private static User CreateTestUser(string passwordHash)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new User
        {
            Username = $"login-migration-{suffix}",
            Email = $"login-migration-{suffix}@example.test",
            PasswordHash = passwordHash
        };
    }

    private static string CreateHistoricalHash(string password)
    {
        var saltBytes = Enumerable.Range(96, 32).Select(value => (byte)value).ToArray();
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
            throw new DbUpdateException("Controlled test persistence failure.");
    }
}
