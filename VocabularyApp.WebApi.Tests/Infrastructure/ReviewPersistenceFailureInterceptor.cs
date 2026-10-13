using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class ReviewPersistenceFailureInterceptor : DbCommandInterceptor
{
    public string? FailAfterTable { get; set; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (FailAfterTable != null && (command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) || command.CommandText.StartsWith("INSERT", StringComparison.Ordinal)) &&
            command.CommandText.Contains('"' + FailAfterTable + '"', StringComparison.Ordinal))
        {
            FailAfterTable = null;
            result.Dispose();
            throw new IOException("Controlled failure after relational write.");
        }
        return ValueTask.FromResult(result);
    }
}

public sealed class ReviewCommitFailureInterceptor : DbTransactionInterceptor
{
    public bool BeforeCommit { get; set; }
    public bool AfterCommit { get; set; }
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (BeforeCommit) { BeforeCommit = false; throw new IOException("Controlled precommit failure."); }
        return ValueTask.FromResult(result);
    }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (AfterCommit) { AfterCommit = false; throw new IOException("Controlled lost commit acknowledgement."); }
        return Task.CompletedTask;
    }
}
