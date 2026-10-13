using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VocabularyApp.Data;
using VocabularyApp.Data.Migrations;
using VocabularyApp.Data.Models;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

/// <summary>Offline SQL Server provider checks. These do not apply a migration or open a connection.</summary>
public sealed class ReviewMigrationDefinitionTests
{
    [Fact]
    public void MigrationAddsOnlyFourTablesRevisionOwnershipAndConstraintsWithoutEnrollment()
    {
        var operations = new AddSpacedRepetition().UpOperations;
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "ReviewHistories", "ReviewSessionItems", "ReviewSessions", "UserWordReviewStates" }, tables.Select(t => t.Name).OrderBy(n => n));
        var column = Assert.Single(operations.OfType<AddColumnOperation>());
        Assert.Equal("WordDefinitions", column.Table); Assert.Equal("ContentRevision", column.Name);
        Assert.Equal(typeof(long), column.ClrType); Assert.False(column.IsNullable); Assert.Equal(1L, column.DefaultValue);
        var key = Assert.Single(operations.OfType<AddUniqueConstraintOperation>());
        Assert.Equal("UserWords", key.Table); Assert.Equal(new[] { "UserId", "Id" }, key.Columns);
        Assert.All(operations, operation => Assert.True(operation is AddColumnOperation or AddUniqueConstraintOperation or CreateTableOperation or AddCheckConstraintOperation or CreateIndexOperation));
        Assert.DoesNotContain(operations, o => o is SqlOperation or InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
        Assert.DoesNotContain(operations.OfType<CreateIndexOperation>(), i => i.Name == "IX_UserWords_UserId_WordId");
    }

    [Fact]
    public void MigrationForeignKeysEnforceOwnershipAndHaveOneNewCascade()
    {
        var tables = new AddSpacedRepetition().UpOperations.OfType<CreateTableOperation>().ToArray();
        var cascades = tables.SelectMany(t => t.ForeignKeys).Where(f => f.OnDelete == ReferentialAction.Cascade).ToArray();
        var cascade = Assert.Single(cascades);
        Assert.Equal("UserWordReviewStates", cascade.Table);
        Assert.Equal(new[] { "UserId", "UserWordId" }, cascade.Columns);
        Assert.Equal(new[] { "UserId", "Id" }, cascade.PrincipalColumns);
        var item = Assert.Single(tables, t => t.Name == "ReviewSessionItems");
        Assert.Equal(2, item.ForeignKeys.Count);
        Assert.All(item.ForeignKeys, f => Assert.Equal(ReferentialAction.NoAction, f.OnDelete));
        var history = Assert.Single(tables, t => t.Name == "ReviewHistories");
        Assert.Contains(history.ForeignKeys, f => f.PrincipalTable == "ReviewSessionItems" && f.Columns.SequenceEqual(new[] { "UserId", "SessionItemId", "UserWordId" }));
        Assert.DoesNotContain(history.ForeignKeys, f => f.PrincipalTable == "WordDefinitions");
    }

    [Fact]
    public void FilteredAcceptanceAndHistoryUniquenessAreInTheMigration()
    {
        var indexes = new AddSpacedRepetition().UpOperations.OfType<CreateIndexOperation>().ToArray();
        var receipt = Assert.Single(indexes, i => i.Name == "IX_ReviewSessionItems_UserId_AcceptedSubmissionId");
        Assert.True(receipt.IsUnique); Assert.Equal("[AcceptedSubmissionId] IS NOT NULL", receipt.Filter);
        Assert.Contains(indexes, i => i.Table == "ReviewHistories" && i.IsUnique && i.Columns.SequenceEqual(new[] { "UserId", "SubmissionId" }));
        Assert.Contains(indexes, i => i.Table == "ReviewHistories" && i.IsUnique && i.Columns.SequenceEqual(new[] { "SessionItemId" }));
        Assert.Contains(indexes, i => i.Table == "UserWordReviewStates" && i.Columns.SequenceEqual(new[] { "UserId", "NextReviewAt", "UserWordId" }));
    }

    [Fact]
    public void RollbackDropsDependentsFirstAndRestoresOnlyTheAdditiveSchema()
    {
        var operations = new AddSpacedRepetition().DownOperations;
        var tables = operations.OfType<DropTableOperation>().Select(o => o.Name).ToArray();
        Assert.Equal(new[] { "ReviewHistories", "UserWordReviewStates", "ReviewSessionItems", "ReviewSessions" }, tables);
        Assert.Equal("ContentRevision", Assert.Single(operations.OfType<DropColumnOperation>()).Name);
        Assert.Equal("AK_UserWords_UserId_Id", Assert.Single(operations.OfType<DropUniqueConstraintOperation>()).Name);
        Assert.DoesNotContain(operations, o => o is DropIndexOperation or SqlOperation);
    }

    [Fact]
    public void CurrentSqlServerModelAndSnapshotAndMigrationTargetHaveNoSchemaDifference()
    {
        using var context = new ApplicationDbContextDesignFactory().CreateDbContext([]);
        var initializer = context.GetService<IModelRuntimeInitializer>();
        var snapshot = initializer.Initialize(context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model, designTime: true);
        var target = initializer.Initialize(new AddSpacedRepetition().TargetModel, designTime: true);
        var current = context.GetService<IDesignTimeModel>().Model;
        var differ = context.GetService<IMigrationsModelDiffer>();
        Assert.False(differ.HasDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel()));
        Assert.False(differ.HasDifferences(target.GetRelationalModel(), current.GetRelationalModel()));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void OfflineSqlServerScriptsContainAdditiveUpAndOrderedDown()
    {
        using var context = new ApplicationDbContextDesignFactory().CreateDbContext([]);
        var migrationId = typeof(AddSpacedRepetition).GetCustomAttribute<MigrationAttribute>()!.Id;
        var migrator = context.GetService<IMigrator>();
        var up = migrator.GenerateScript("20260829155134_CorrectUserWordIdentity", migrationId, MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE [ReviewHistories]", up);
        Assert.Contains("datetime2(7)", up);
        Assert.Contains("DEFAULT CAST(1 AS bigint)", up);
        Assert.Contains("WHERE [AcceptedSubmissionId] IS NOT NULL", up);
        Assert.DoesNotContain("DROP TABLE", up);
        Assert.DoesNotContain("INSERT INTO [UserWordReviewStates]", up);
        var down = migrator.GenerateScript(migrationId, "20260829155134_CorrectUserWordIdentity");
        Assert.Contains("DROP TABLE [ReviewHistories]", down);
        Assert.True(down.IndexOf("DROP TABLE [ReviewHistories]", StringComparison.Ordinal) < down.IndexOf("DROP TABLE [ReviewSessionItems]", StringComparison.Ordinal));
        Assert.DoesNotContain("DROP TABLE [UserWords]", down);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void NewDefinitionEntityQueriesRequireTheSchemaEvenBeforeReviewActivation()
    {
        using var context = new ApplicationDbContextDesignFactory().CreateDbContext([]);
        var sql = context.WordDefinitions.AsNoTracking().ToQueryString();
        Assert.Contains("[ContentRevision]", sql);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void SqlServerModelUsesExplicitDatesBoundsAndPortableTokensWithoutShadowColumns()
    {
        using var context = new ApplicationDbContextDesignFactory().CreateDbContext([]);
        var model = context.Model;
        foreach (var type in new[] { typeof(UserWordReviewState), typeof(ReviewSession), typeof(ReviewSessionItem), typeof(ReviewHistory) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.DoesNotContain(entity.GetProperties(), p => p.IsShadowProperty());
            foreach (var date in entity.GetProperties().Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
                Assert.Equal("datetime2(7)", date.GetColumnType());
            Assert.DoesNotContain(entity.GetProperties(), p => p.GetColumnType() == "rowversion");
        }
        Assert.True(model.FindEntityType(typeof(UserWordReviewState))!.FindProperty("ScheduleVersion")!.IsConcurrencyToken);
        Assert.True(model.FindEntityType(typeof(ReviewSession))!.FindProperty("Version")!.IsConcurrencyToken);
        var revision = model.FindEntityType(typeof(WordDefinition))!.FindProperty("ContentRevision")!;
        Assert.True(revision.IsConcurrencyToken); Assert.Equal(1L, revision.GetDefaultValue());
        var item = model.FindEntityType(typeof(ReviewSessionItem))!;
        Assert.Equal(1000, item.FindProperty("DefinitionText")!.GetMaxLength());
        Assert.Equal(1500, item.FindProperty("Prompt")!.GetMaxLength());
        Assert.Equal("nvarchar(max)", item.FindProperty("ReceiptJson")!.GetColumnType());
        Assert.True(item.FindProperty("ReceiptJson")!.IsNullable);
        Assert.False(item.FindProperty("OptionsJson")!.IsNullable);
        var userWord = model.FindEntityType(typeof(UserWord))!;
        Assert.Contains(userWord.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "UserId", "WordId" }));
    }
}
