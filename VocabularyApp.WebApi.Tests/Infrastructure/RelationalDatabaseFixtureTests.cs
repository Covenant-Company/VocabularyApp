using Microsoft.EntityFrameworkCore;
using VocabularyApp.Data.Models;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class RelationalDatabaseFixtureTests : IClassFixture<RelationalDatabaseFixture>
{
    private readonly RelationalDatabaseFixture _fixture;

    public RelationalDatabaseFixtureTests(RelationalDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UserPersistsAndReloadsFromFreshContext()
    {
        const string username = "fixture-user";
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(new User
            {
                Username = username,
                Email = "fixture-user@example.test",
                PasswordHash = "test-hash",
                CreatedAt = createdAt
            });

            await writeContext.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var persistedUser = await readContext.Users.SingleAsync(user => user.Username == username);

        Assert.Equal("fixture-user@example.test", persistedUser.Email);
        Assert.Equal("test-hash", persistedUser.PasswordHash);
        Assert.Equal(createdAt, persistedUser.CreatedAt);
    }
}
