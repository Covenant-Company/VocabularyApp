using Microsoft.Extensions.Options;
using VocabularyApp.WebApi.Configuration;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

// Isolated test-only runtime toggles; never writes application configuration.
public sealed class MutableReviewOptionsMonitor : IOptionsMonitor<ReviewOptions>
{
    public ReviewOptions CurrentValue { get; } = new() { Enabled = true, EnrollmentEnabled = true };
    public ReviewOptions Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<ReviewOptions, string?> listener) => null;
}
