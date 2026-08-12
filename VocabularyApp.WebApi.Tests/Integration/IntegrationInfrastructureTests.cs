using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class IntegrationInfrastructureTests
{
    [Fact]
    public async Task IndependentFactoriesDoNotShareRegisteredUsers()
    {
        var credentials = TestUserCredentials.CreateUnique("factory-isolation");

        using (var firstFactory = new VocabularyAppWebApplicationFactory())
        using (var firstClient = firstFactory.CreateClient())
        {
            var registration = await ApiTestClientHelper.RegisterAsync(firstClient, credentials);
            Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
            Assert.True(registration.Success);
        }

        using var secondFactory = new VocabularyAppWebApplicationFactory();
        using var scope = secondFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(await context.Users.AnyAsync(user => user.Username == credentials.Username));
    }

    [Fact]
    public async Task ClientsFromSameFactoryShareRegisteredUserState()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var credentials = TestUserCredentials.CreateUnique("shared-factory");

        using (var registrationClient = factory.CreateClient())
        {
            var registration = await ApiTestClientHelper.RegisterAsync(
                registrationClient,
                credentials);
            Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
            Assert.True(registration.Success);
        }

        using var loginClient = factory.CreateClient();
        var authenticatedUser = await ApiTestClientHelper.LoginAsync(loginClient, credentials);

        Assert.False(string.IsNullOrWhiteSpace(authenticatedUser.Token));
        Assert.Equal(credentials.Username, authenticatedUser.User.Username);
    }

    [Fact]
    public async Task TwoUsersRegisterLoginAndUseIndependentAuthenticatedClients()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var users = await ApiTestClientHelper.CreateTwoAuthenticatedUsersAsync(factory);

        using var userAProfile = await users.UserA.Client.GetAsync("/api/users/profile");
        using var userBProfile = await users.UserB.Client.GetAsync("/api/users/profile");

        Assert.Equal(HttpStatusCode.OK, userAProfile.StatusCode);
        Assert.Equal(HttpStatusCode.OK, userBProfile.StatusCode);
        Assert.NotEqual(users.UserA.User.User.Id, users.UserB.User.User.Id);
        Assert.False(string.IsNullOrWhiteSpace(users.UserA.User.Token));
        Assert.False(string.IsNullOrWhiteSpace(users.UserB.User.Token));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await context.Users.CountAsync());
    }

    [Fact]
    public async Task DirectlySeededModernUserCanLoginThroughApi()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var credentials = TestUserCredentials.CreateUnique("direct-seed");
        var seededUser = await IntegrationTestSeeder.SeedModernUserAsync(factory, credentials);

        using var client = factory.CreateClient();
        var authenticatedUser = await ApiTestClientHelper.LoginAsync(client, credentials);

        Assert.Equal(seededUser.Id, authenticatedUser.User.Id);
        Assert.False(string.IsNullOrWhiteSpace(authenticatedUser.Token));
    }
}
