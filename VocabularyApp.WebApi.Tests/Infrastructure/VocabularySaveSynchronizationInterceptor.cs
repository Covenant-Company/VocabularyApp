using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VocabularyApp.Data.Models;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class VocabularySaveSynchronizationInterceptor : SaveChangesInterceptor
{
    private TaskCompletionSource _entered = CreateCompletionSource();
    private TaskCompletionSource _release = CreateCompletionSource();
    private int _armed;

    public void Arm()
    {
        _entered = CreateCompletionSource();
        _release = CreateCompletionSource();
        Volatile.Write(ref _armed, 1);
    }

    public Task WaitUntilBlockedAsync() => _entered.Task;

    public void Release()
    {
        Volatile.Write(ref _armed, 0);
        _release.TrySetResult();
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var addsUserWord = eventData.Context?.ChangeTracker
            .Entries<UserWord>()
            .Any(entry => entry.State == EntityState.Added) == true;

        if (!addsUserWord || Interlocked.Exchange(ref _armed, 0) == 0)
        {
            return result;
        }

        _entered.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        return result;
    }

    private static TaskCompletionSource CreateCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
