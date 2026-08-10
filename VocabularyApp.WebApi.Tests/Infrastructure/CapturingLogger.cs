using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed record CapturedLogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception);

public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _entries.Enqueue(new CapturedLogEntry(
            logLevel,
            eventId,
            formatter(state, exception),
            exception));
    }
}
