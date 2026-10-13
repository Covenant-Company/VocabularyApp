using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class ReviewSynchronizationInterceptor(string queryTable = "ReviewHistories") : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<Guid, byte> _arrived = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Armed { get; set; }
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (Armed && command.CommandText.Contains("FROM \"" + queryTable + "\"", StringComparison.Ordinal) && eventData.Context != null && _arrived.TryAdd(eventData.Context.ContextId.InstanceId, 0))
        {
            if (_arrived.Count >= 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        return result;
    }
}

public sealed class ReviewRequestPauseInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Armed { get; set; }
    public Task Entered => _entered.Task;
    public void Release() => _release.TrySetResult();
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (Armed && command.CommandText.Contains("FROM \"ReviewHistories\"", StringComparison.Ordinal))
        {
            Armed = false; _entered.TrySetResult(); await _release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        return result;
    }
}

public sealed class ReviewBusyInterceptor : SaveChangesInterceptor
{
    public int FailuresRemaining { get; set; }
    public int Attempts { get; private set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailuresRemaining > 0 && eventData.Context!.ChangeTracker.Entries<VocabularyApp.Data.Models.ReviewHistory>().Any(e => e.State == EntityState.Added))
        {
            FailuresRemaining--; Attempts++;
            throw new SqliteException("Controlled SQLITE_BUSY.", 5);
        }
        return ValueTask.FromResult(result);
    }
}
