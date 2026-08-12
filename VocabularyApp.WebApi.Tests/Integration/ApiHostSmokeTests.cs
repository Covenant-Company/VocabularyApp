using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ApiHostSmokeTests
{
    [Fact]
    public async Task ProtectedApiRouteTraversesHostAndRejectsAnonymousRequest()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/users/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void ApiHostResolvesApplicationDbContextWithSqliteProvider()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
        Assert.True(context.Database.CanConnect());
    }

    [Fact]
    public void IndependentFactoriesOwnDifferentSqliteConnections()
    {
        using var firstFactory = new VocabularyAppWebApplicationFactory();
        using var secondFactory = new VocabularyAppWebApplicationFactory();
        using var firstScope = firstFactory.Services.CreateScope();
        using var secondScope = secondFactory.Services.CreateScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.NotSame(
            firstContext.Database.GetDbConnection(),
            secondContext.Database.GetDbConnection());
        Assert.True(firstContext.Database.CanConnect());
        Assert.True(secondContext.Database.CanConnect());
    }
}
