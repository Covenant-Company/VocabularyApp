using System.Data;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class RelationalHostSafetyTests
{
    [Fact]
    public async Task ConcurrentFactoriesCanRegisterIdenticalCredentialsIndependently()
    {
        var credentials = new TestUserCredentials("isolated-user", "isolated@example.test", "Integration password!");
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var factory = new VocabularyAppWebApplicationFactory();
            using var client = factory.CreateClient();
            var registration = await ApiTestClientHelper.RegisterAsync(client, credentials);
            Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
            Assert.True(registration.Success);
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await context.Users.CountAsync());
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactoryDisposalClosesAndDestroysItsDatabase(bool asynchronously)
    {
        var factory = new VocabularyAppWebApplicationFactory();
        SqliteConnection connection;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            connection = Assert.IsType<SqliteConnection>(context.Database.GetDbConnection());
            Assert.Equal(8, await context.PartsOfSpeech.CountAsync());
        }

        if (asynchronously)
            await factory.DisposeAsync();
        else
            factory.Dispose();

        Assert.Equal(ConnectionState.Closed, connection.State);
        // Reopening the same private in-memory connection must create an empty database.
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task UniqueIndexRejectsDuplicateCanonicalWords()
    {
        await using var factory = new VocabularyAppWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Words.Add(new Word { Text = "duplicate" });
        await context.SaveChangesAsync();
        context.Words.Add(new Word { Text = "duplicate" });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(2067, Assert.IsType<SqliteException>(exception.InnerException).SqliteExtendedErrorCode);
        Assert.Equal(1, await context.Words.CountAsync());
    }

    [Fact]
    public async Task ForeignKeyRejectsDefinitionWithoutCanonicalWord()
    {
        await using var factory = new VocabularyAppWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.WordDefinitions.Add(new WordDefinition
        {
            WordId = int.MaxValue,
            PartOfSpeechId = await context.PartsOfSpeech.Select(part => part.Id).FirstAsync(),
            Definition = "Orphan definition"
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(exception.InnerException).SqliteExtendedErrorCode);
        Assert.Equal(0, await context.WordDefinitions.CountAsync());
    }

    [Fact]
    public async Task RolledBackTransactionLeavesNoPersistedWord()
    {
        await using var factory = new VocabularyAppWebApplicationFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Words.Add(new Word { Text = "rolled-back" });
            await context.SaveChangesAsync();
            Assert.Equal(1, await context.Words.CountAsync());
            await transaction.RollbackAsync();
        }

        using var verificationScope = factory.Services.CreateScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await verification.Words.CountAsync());
    }
}
