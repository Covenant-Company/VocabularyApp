using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VocabularyApp.Data.Models;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class VocabularyPersistenceFailureInterceptor : SaveChangesInterceptor
{
    private int _armed;

    public void Arm() => Volatile.Write(ref _armed, 1);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var addsUserWord = eventData.Context?.ChangeTracker
            .Entries<UserWord>()
            .Any(entry => entry.State == EntityState.Added) == true;

        if (!addsUserWord || Interlocked.Exchange(ref _armed, 0) == 0)
        {
            return ValueTask.FromResult(result);
        }

        throw new DbUpdateException("Controlled unrelated vocabulary persistence failure.");
    }
}
