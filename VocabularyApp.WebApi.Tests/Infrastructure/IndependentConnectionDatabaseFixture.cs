using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VocabularyApp.Data;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

/// <summary>Isolated named SQLite memory database; every context owns a separate connection.</summary>
public sealed class IndependentConnectionDatabaseFixture : IAsyncLifetime
{
    private SqliteConnection? _keeper;
    private readonly string _connectionString = $"Data Source=f6-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True;Pooling=False";

    public async Task InitializeAsync()
    {
        _keeper = new SqliteConnection(_connectionString);
        await _keeper.OpenAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connectionString).Options);

    public async Task DisposeAsync()
    {
        if (_keeper != null) await _keeper.DisposeAsync();
    }
}
