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

    private static UserService CreateUserService(ApplicationDbContext context)
    {
        var jwtHelper = new JwtHelper(
            TestJwtSettingsFactory.Create(),
            new CapturingLogger<JwtHelper>());
        var passwordService = new PasswordService(
            new PasswordHasher<User>(),
            new LegacyPasswordVerifier());

        return new UserService(
            context,
            jwtHelper,
            passwordService,
            new CapturingLogger<UserService>());
    }
}
