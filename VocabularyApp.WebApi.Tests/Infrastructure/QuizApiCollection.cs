using VocabularyApp.WebApi.Services;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class QuizApiCollection
{
    public const string Name = "Quiz API static sessions";
}

public abstract class QuizApiTestBase : IDisposable
{
    protected QuizApiTestBase() => QuizService.ClearQuizSessionsForTesting();

    public void Dispose()
    {
        QuizService.ClearQuizSessionsForTesting();
        GC.SuppressFinalize(this);
    }
}
