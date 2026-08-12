using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Controllers;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class AuthenticationApiTests
{
    [Fact]
    public async Task ValidRegistrationPersistsNonPlaintextPasswordAndReturnsUsableToken()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        var credentials = TestUserCredentials.CreateUnique("registration-success");

        var registration = await ApiTestClientHelper.RegisterAsync(client, credentials);

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        Assert.True(registration.Success);
        Assert.NotNull(registration.Data?.User);
        Assert.False(string.IsNullOrWhiteSpace(registration.Data?.Token));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedUser = await context.Users
            .SingleAsync(user => user.Username == credentials.Username);
        Assert.Equal(credentials.Email, persistedUser.Email);
        Assert.NotEqual(credentials.Password, persistedUser.PasswordHash);
        Assert.DoesNotContain(credentials.Password, persistedUser.PasswordHash);

        using var authenticatedClient = ApiTestClientHelper.CreateClientWithBearerToken(
            factory,
            registration.Data!.Token!);
        using var profileResponse = await authenticatedClient.GetAsync("/api/users/profile");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
    }

    [Fact]
    public async Task DuplicateUsernameReturnsBadRequestAndDoesNotCreateSecondUser()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        var original = TestUserCredentials.CreateUnique("duplicate-username");
        var duplicate = new TestUserCredentials(
            original.Username.ToUpperInvariant(),
            $"different-{Guid.NewGuid():N}@example.test",
            original.Password);
        Assert.True((await ApiTestClientHelper.RegisterAsync(client, original)).Success);

        var result = await ApiTestClientHelper.RegisterAsync(client, duplicate);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.False(result.Success);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(
            1,
            await context.Users.CountAsync(user =>
                user.Username.ToLower() == original.Username.ToLower()));
    }

    [Fact]
    public async Task DuplicateEmailReturnsBadRequestAndDoesNotCreateSecondUser()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        var original = TestUserCredentials.CreateUnique("duplicate-email");
        var duplicate = new TestUserCredentials(
            $"different-{Guid.NewGuid():N}",
            original.Email.ToUpperInvariant(),
            original.Password);
        Assert.True((await ApiTestClientHelper.RegisterAsync(client, original)).Success);

        var result = await ApiTestClientHelper.RegisterAsync(client, duplicate);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.False(result.Success);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(
            1,
            await context.Users.CountAsync(user =>
                user.Email.ToLower() == original.Email.ToLower()));
    }

    [Fact]
    public async Task InvalidRegistrationIsRejectedByModelValidation()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/users/register",
            new CreateUserRequest
            {
                Username = "ab",
                Email = "not-an-email",
                Password = "short"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await context.Users.CountAsync());
    }

    [Fact]
    public async Task ValidLoginReturnsTokenAcceptedByProtectedProfile()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var credentials = TestUserCredentials.CreateUnique("login-success");
        using (var registrationClient = factory.CreateClient())
        {
            Assert.True((await ApiTestClientHelper.RegisterAsync(
                registrationClient,
                credentials)).Success);
        }

        using var loginClient = factory.CreateClient();
        var authenticatedUser = await ApiTestClientHelper.LoginAsync(loginClient, credentials);
        using var protectedClient = ApiTestClientHelper.CreateClientWithBearerToken(
            factory,
            authenticatedUser.Token);
        using var profileResponse = await protectedClient.GetAsync("/api/users/profile");
        var profile = await ApiTestClientHelper.ReadApiResultAsync<UserDto>(profileResponse);

        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.True(profile.Success);
        Assert.Equal(authenticatedUser.User.Id, profile.Data?.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidCredentialsReturnUnauthorizedWithoutToken(bool unknownUsername)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var credentials = TestUserCredentials.CreateUnique("invalid-login");
        using (var registrationClient = factory.CreateClient())
        {
            Assert.True((await ApiTestClientHelper.RegisterAsync(
                registrationClient,
                credentials)).Success);
        }

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/users/login",
            new LoginRequest
            {
                Username = unknownUsername
                    ? $"unknown-{Guid.NewGuid():N}"
                    : credentials.Username,
                Password = "Definitely wrong password!"
            });
        var result = await ApiTestClientHelper.ReadApiResultAsync<AuthResponse>(response);

        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.False(result.Success);
        Assert.True(string.IsNullOrWhiteSpace(result.Data?.Token));
    }

    [Fact]
    public async Task MalformedBearerTokenIsRejected()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "definitely-not-a-valid-jwt");

        using var response = await client.GetAsync("/api/users/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TamperedBearerTokenIsRejected()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var authenticated = await ApiTestClientHelper
            .RegisterAndCreateAuthenticatedClientAsync(factory);
        var token = authenticated.User.Token;
        var tokenSegments = token.Split('.');
        var replacement = tokenSegments[2][0] == 'a' ? 'b' : 'a';
        tokenSegments[2] = replacement + tokenSegments[2][1..];
        var tamperedToken = string.Join('.', tokenSegments);
        using var client = ApiTestClientHelper.CreateClientWithBearerToken(factory, tamperedToken);

        using var response = await client.GetAsync("/api/users/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredSignedTokenIsRejected()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = ApiTestClientHelper.CreateClientWithBearerToken(
            factory,
            CreateSignedToken(
                userId: 123,
                expiresUtc: DateTime.UtcNow.AddMinutes(-1)));

        using var response = await client.GetAsync("/api/users/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedProfileResolvesEachAuthenticatedUserFromClaims()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);

        using var userAResponse = await users.UserA.Client.GetAsync("/api/users/profile");
        using var userBResponse = await users.UserB.Client.GetAsync("/api/users/profile");
        var userAProfile = await ApiTestClientHelper.ReadApiResultAsync<UserDto>(userAResponse);
        var userBProfile = await ApiTestClientHelper.ReadApiResultAsync<UserDto>(userBResponse);

        Assert.Equal(users.UserA.User.User.Id, userAProfile.Data?.Id);
        Assert.Equal(users.UserA.User.Credentials.Username, userAProfile.Data?.Username);
        Assert.Equal(users.UserB.User.User.Id, userBProfile.Data?.Id);
        Assert.Equal(users.UserB.User.Credentials.Username, userBProfile.Data?.Username);
        Assert.NotEqual(userAProfile.Data?.Id, userBProfile.Data?.Id);
    }

    [Fact]
    public async Task ValidTokenForMissingUserReturnsNotFoundFromProfile()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = ApiTestClientHelper.CreateClientWithBearerToken(
            factory,
            CreateSignedToken(
                userId: int.MaxValue,
                expiresUtc: DateTime.UtcNow.AddMinutes(5)));

        using var response = await client.GetAsync("/api/users/profile");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousPasswordChangeIsRejected()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/users/change-password",
            new ChangePasswordRequest
            {
                CurrentPassword = "Current password!",
                NewPassword = "Replacement password!"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedPasswordChangeInvalidatesOldPasswordAndDoesNotAffectOtherUser()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);
        const string newPassword = "New owner password!";

        using var changeResponse = await users.UserA.Client.PostAsJsonAsync(
            "/api/users/change-password",
            new ChangePasswordRequest
            {
                CurrentPassword = users.UserA.User.Credentials.Password,
                NewPassword = newPassword
            });
        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        using var loginClient = factory.CreateClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ApiTestClientHelper.LoginAsync(loginClient, users.UserA.User.Credentials));
        var changedCredentials = users.UserA.User.Credentials with { Password = newPassword };
        var changedUser = await ApiTestClientHelper.LoginAsync(loginClient, changedCredentials);
        var unchangedUserB = await ApiTestClientHelper.LoginAsync(
            loginClient,
            users.UserB.User.Credentials);

        Assert.Equal(users.UserA.User.User.Id, changedUser.User.Id);
        Assert.Equal(users.UserB.User.User.Id, unchangedUserB.User.Id);
    }

    [Fact]
    public async Task WrongCurrentPasswordReturnsUnauthorizedAndOriginalPasswordStillWorks()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var authenticated = await ApiTestClientHelper
            .RegisterAndCreateAuthenticatedClientAsync(factory);

        using var response = await authenticated.Client.PostAsJsonAsync(
            "/api/users/change-password",
            new ChangePasswordRequest
            {
                CurrentPassword = "Wrong current password!",
                NewPassword = "Unused replacement password!"
            });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var loginClient = factory.CreateClient();
        var loggedIn = await ApiTestClientHelper.LoginAsync(
            loginClient,
            authenticated.User.Credentials);
        Assert.Equal(authenticated.User.User.Id, loggedIn.User.Id);
    }

    private static string CreateSignedToken(int userId, DateTime expiresUtc)
    {
        var settings = TestJwtSettingsFactory.Create();
        var credentials = new SigningCredentials(
            settings.CreateSigningKey(),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, $"token-user-{userId}"),
                new Claim(ClaimTypes.Email, $"token-user-{userId}@example.test")
            ],
            notBefore: expiresUtc.AddMinutes(-5),
            expires: expiresUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
