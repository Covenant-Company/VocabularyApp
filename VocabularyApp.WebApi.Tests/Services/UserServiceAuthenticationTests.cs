using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Helpers;
using VocabularyApp.WebApi.Security;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Services;

public sealed class UserServiceAuthenticationTests : IClassFixture<RelationalDatabaseFixture>
{
    private readonly RelationalDatabaseFixture _fixture;

    public UserServiceAuthenticationTests(RelationalDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateUserAsyncPersistsModernHashAndPreservesRegistrationResponse()
    {
        const string password = "Registration test password!";
        var suffix = Guid.NewGuid().ToString("N");
        var request = new CreateUserRequest
        {
            Username = $"registration-{suffix}",
            Email = $"registration-{suffix}@example.test",
            Password = password
        };

        AuthResponse result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            var service = CreateUserService(serviceContext);
            result = await service.CreateUserAsync(request);
        }

        Assert.True(result.Success);
        Assert.NotNull(result.User);
        Assert.Equal(request.Username, result.User.Username);
        Assert.Equal(request.Email, result.User.Email);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));

        await using var verificationContext = _fixture.CreateContext();
        var persistedUser = await verificationContext.Users
            .SingleAsync(user => user.Username == request.Username);
        var modernHasher = new PasswordHasher<User>();
        var legacyVerifier = new LegacyPasswordVerifier();

        Assert.Equal(request.Username, persistedUser.Username);
        Assert.Equal(request.Email, persistedUser.Email);
        Assert.False(string.IsNullOrWhiteSpace(persistedUser.PasswordHash));
        Assert.NotEqual(password, persistedUser.PasswordHash);
        Assert.False(legacyVerifier.IsLegacyFormat(persistedUser.PasswordHash));
        Assert.Equal(
            PasswordVerificationResult.Success,
            modernHasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, password));
    }

    [Fact]
    public async Task CreateUserAsyncWithDuplicateUsernamePreservesExistingFailure()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var existingUser = new User
        {
            Username = $"duplicate-user-{suffix}",
            Email = $"existing-user-{suffix}@example.test",
            PasswordHash = "existing-test-hash"
        };

        await using (var seedContext = _fixture.CreateContext())
        {
            seedContext.Users.Add(existingUser);
            await seedContext.SaveChangesAsync();
        }

        var request = new CreateUserRequest
        {
            Username = existingUser.Username.ToUpperInvariant(),
            Email = $"new-email-{suffix}@example.test",
            Password = "Registration test password!"
        };

        AuthResponse result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            result = await CreateUserService(serviceContext).CreateUserAsync(request);
        }

        Assert.False(result.Success);
        Assert.Equal("Username is already taken", result.ErrorMessage);
        Assert.Null(result.Token);

        await using var verificationContext = _fixture.CreateContext();
        Assert.Equal(
            1,
            await verificationContext.Users.CountAsync(user =>
                user.Username.ToLower() == existingUser.Username.ToLower()));
    }

    [Fact]
    public async Task CreateUserAsyncWithDuplicateEmailPreservesExistingFailure()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var existingUser = new User
        {
            Username = $"existing-email-user-{suffix}",
            Email = $"duplicate-email-{suffix}@example.test",
            PasswordHash = "existing-test-hash"
        };

        await using (var seedContext = _fixture.CreateContext())
        {
            seedContext.Users.Add(existingUser);
            await seedContext.SaveChangesAsync();
        }

        var request = new CreateUserRequest
        {
            Username = $"new-user-{suffix}",
            Email = existingUser.Email.ToUpperInvariant(),
            Password = "Registration test password!"
        };

        AuthResponse result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            result = await CreateUserService(serviceContext).CreateUserAsync(request);
        }

        Assert.False(result.Success);
        Assert.Equal("Email is already registered", result.ErrorMessage);
        Assert.Null(result.Token);

        await using var verificationContext = _fixture.CreateContext();
        Assert.Equal(
            1,
            await verificationContext.Users.CountAsync(user =>
                user.Email.ToLower() == existingUser.Email.ToLower()));
    }

    [Fact]
    public async Task ChangePasswordAsyncWithLegacyCurrentPasswordStoresOnlyModernNewPassword()
    {
        const string currentPassword = "Legacy current password!";
        const string newPassword = "Modern replacement password!";
        var userId = await SeedUserAsync(CreateHistoricalHash(currentPassword));

        bool result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            result = await CreateUserService(serviceContext)
                .ChangePasswordAsync(userId, currentPassword, newPassword);
        }

        var persistedUser = await ReloadUserAsync(userId);
        var modernHasher = new PasswordHasher<User>();

        Assert.True(result);
        Assert.False(new LegacyPasswordVerifier().IsLegacyFormat(persistedUser.PasswordHash));
        Assert.Equal(
            PasswordVerificationResult.Success,
            modernHasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, newPassword));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            modernHasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, currentPassword));
    }

    [Fact]
    public async Task ChangePasswordAsyncWithModernCurrentPasswordStoresModernNewPassword()
    {
        const string currentPassword = "Current modern password!";
        const string newPassword = "New modern password!";
        var seedUser = CreateTestUser("placeholder");
        var modernHasher = new PasswordHasher<User>();
        seedUser.PasswordHash = modernHasher.HashPassword(seedUser, currentPassword);
        var userId = await SeedUserAsync(seedUser);

        bool result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            result = await CreateUserService(serviceContext)
                .ChangePasswordAsync(userId, currentPassword, newPassword);
        }

        var persistedUser = await ReloadUserAsync(userId);

        Assert.True(result);
        Assert.False(new LegacyPasswordVerifier().IsLegacyFormat(persistedUser.PasswordHash));
        Assert.Equal(
            PasswordVerificationResult.Success,
            modernHasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, newPassword));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            modernHasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, currentPassword));
    }

    [Fact]
    public async Task ChangePasswordAsyncWithWrongLegacyCurrentPasswordDoesNotMutateHash()
    {
        var originalHash = CreateHistoricalHash("Correct legacy password!");
        var userId = await SeedUserAsync(originalHash);

        bool result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            result = await CreateUserService(serviceContext)
                .ChangePasswordAsync(userId, "wrong password", "unused new password");
        }

        Assert.False(result);
        Assert.Equal(originalHash, (await ReloadUserAsync(userId)).PasswordHash);
    }

    [Fact]
    public async Task ChangePasswordAsyncWithWrongModernCurrentPasswordDoesNotMutateHash()
    {
        var seedUser = CreateTestUser("placeholder");
        seedUser.PasswordHash = new PasswordHasher<User>()
            .HashPassword(seedUser, "Correct modern password!");
        var originalHash = seedUser.PasswordHash;
        var userId = await SeedUserAsync(seedUser);

        bool result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            result = await CreateUserService(serviceContext)
                .ChangePasswordAsync(userId, "wrong password", "unused new password");
        }

        Assert.False(result);
        Assert.Equal(originalHash, (await ReloadUserAsync(userId)).PasswordHash);
    }

    [Fact]
    public async Task ChangePasswordAsyncWithMalformedStoredHashDoesNotMutateHash()
    {
        const string malformedHash = "invalid:legacy-looking-value";
        var userId = await SeedUserAsync(malformedHash);

        bool result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            result = await CreateUserService(serviceContext)
                .ChangePasswordAsync(userId, "any password", "unused new password");
        }

        Assert.False(result);
        Assert.Equal(malformedHash, (await ReloadUserAsync(userId)).PasswordHash);
    }

    [Fact]
    public async Task ChangePasswordAsyncWithUnknownStoredHashDoesNotMutateHash()
    {
        const string unknownHash = "unsupported-no-colon-stored-value";
        var userId = await SeedUserAsync(unknownHash);

        bool result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            result = await CreateUserService(serviceContext)
                .ChangePasswordAsync(userId, "any password", "unused new password");
        }

        Assert.False(result);
        Assert.Equal(unknownHash, (await ReloadUserAsync(userId)).PasswordHash);
    }

    [Fact]
    public async Task ChangePasswordAsyncIgnoresOldPasswordRehashAndStoresHashOfNewPassword()
    {
        const string currentPassword = "Current password requiring rehash!";
        const string newPassword = "New password after rehash result!";
        var userId = await SeedUserAsync("controlled-modern-current-hash");
        var realModernHasher = new PasswordHasher<User>();
        var hashedPasswords = new List<(string Password, string Hash)>();
        var controlledHasher = new ControlledPasswordHasher
        {
            VerificationResult = PasswordVerificationResult.SuccessRehashNeeded,
            HashPasswordFactory = (user, password) =>
            {
                var hash = realModernHasher.HashPassword(user, password);
                hashedPasswords.Add((password, hash));
                return hash;
            }
        };
        var passwordService = new PasswordService(
            controlledHasher,
            new LegacyPasswordVerifier());

        bool result;
        await using (var serviceContext = _fixture.CreateContext())
        {
            result = await CreateUserService(serviceContext, passwordService)
                .ChangePasswordAsync(userId, currentPassword, newPassword);
        }

        var persistedUser = await ReloadUserAsync(userId);

        Assert.True(result);
        Assert.Equal(2, controlledHasher.HashPasswordCallCount);
        Assert.Collection(
            hashedPasswords,
            oldReplacement => Assert.Equal(currentPassword, oldReplacement.Password),
            newCredential => Assert.Equal(newPassword, newCredential.Password));
        Assert.NotEqual(hashedPasswords[0].Hash, persistedUser.PasswordHash);
        Assert.Equal(hashedPasswords[1].Hash, persistedUser.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            realModernHasher.VerifyHashedPassword(persistedUser, persistedUser.PasswordHash, newPassword));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            realModernHasher.VerifyHashedPassword(
                persistedUser,
                persistedUser.PasswordHash,
                currentPassword));
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
            Username = $"password-change-{suffix}",
            Email = $"password-change-{suffix}@example.test",
            PasswordHash = passwordHash
        };
    }

    private static string CreateHistoricalHash(string password)
    {
        var saltBytes = Enumerable.Range(64, 32).Select(value => (byte)value).ToArray();
        var saltBase64Text = Convert.ToBase64String(saltBytes);
        var input = Encoding.UTF8.GetBytes(password + saltBase64Text);
        var digest = SHA256.HashData(input);

        return $"{saltBase64Text}:{Convert.ToBase64String(digest)}";
    }
}
