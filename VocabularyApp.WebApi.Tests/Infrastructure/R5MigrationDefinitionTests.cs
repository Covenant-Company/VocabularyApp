using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VocabularyApp.Data.Migrations;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class R5MigrationDefinitionTests
{
    [Fact]
    public void UpFailsFastThenReplacesCompositeIdentityWithCanonicalWordIdentity()
    {
        var migration = new CorrectUserWordIdentity();
        var operations = migration.UpOperations;

        var precondition = Assert.IsType<SqlOperation>(operations[0]);
        Assert.Contains("GROUP BY UserId, WordId", precondition.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51000", precondition.Sql, StringComparison.Ordinal);

        var dropped = Assert.IsType<DropIndexOperation>(operations[1]);
        Assert.Equal("IX_UserWords_UserId_WordId_PartOfSpeechId", dropped.Name);
        Assert.Equal("UserWords", dropped.Table);

        var created = Assert.IsType<CreateIndexOperation>(operations[2]);
        Assert.Equal("IX_UserWords_UserId_WordId", created.Name);
        Assert.Equal(new[] { "UserId", "WordId" }, created.Columns);
        Assert.True(created.IsUnique);
    }

    [Fact]
    public void DownRestoresCompositeIdentity()
    {
        var migration = new CorrectUserWordIdentity();
        var operations = migration.DownOperations;

        var dropped = Assert.IsType<DropIndexOperation>(operations[0]);
        Assert.Equal("IX_UserWords_UserId_WordId", dropped.Name);

        var created = Assert.IsType<CreateIndexOperation>(operations[1]);
        Assert.Equal("IX_UserWords_UserId_WordId_PartOfSpeechId", created.Name);
        Assert.Equal(new[] { "UserId", "WordId", "PartOfSpeechId" }, created.Columns);
        Assert.True(created.IsUnique);
    }
}
