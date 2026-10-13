namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class ControllableTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
