using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class QuizSubmissionSynchronizationInterceptor : SaveChangesInterceptor
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
        if (Interlocked.Exchange(ref _armed, 0) == 0)
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
