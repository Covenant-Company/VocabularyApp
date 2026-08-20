using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class QuizPersistenceFailureInterceptor : SaveChangesInterceptor
{
    private bool _armed;

    public void Arm() => _armed = true;

    public void Disarm() => _armed = false;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!_armed)
        {
            return ValueTask.FromResult(result);
        }

        _armed = false;
        throw new DbUpdateException("Controlled quiz persistence failure.");
    }
}
