using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VocabularyApp.Data;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class RelationalDatabaseFixture : IAsyncLifetime
{
    private SqliteConnection? _connection;
    private DbContextOptions<ApplicationDbContext>? _options;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public ApplicationDbContext CreateContext()
    {
        if (_options is null)
        {
            throw new InvalidOperationException("The database fixture has not been initialized.");
        }

        return new ApplicationDbContext(_options);
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
