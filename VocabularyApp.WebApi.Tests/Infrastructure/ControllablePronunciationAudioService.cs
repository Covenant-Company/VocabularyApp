using System.Collections.Concurrent;
using VocabularyApp.WebApi.Services;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class ControllablePronunciationAudioService : IPronunciationAudioService
{
    private readonly ConcurrentDictionary<string, string?> _audioUrls =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Exception> _exceptions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _requests = new();

    public IReadOnlyCollection<string> Requests => _requests.ToArray();

    public void RegisterAudioUrl(string word, string? audioUrl) =>
        _audioUrls[word] = audioUrl;

    public void RegisterException(string word, Exception exception) =>
        _exceptions[word] = exception;

    public Task<string?> GetAudioUrlAsync(
        string word,
        CancellationToken cancellationToken = default)
    {
        _requests.Enqueue(word);
        cancellationToken.ThrowIfCancellationRequested();

        if (_exceptions.TryGetValue(word, out var exception))
        {
            return Task.FromException<string?>(exception);
        }

        _audioUrls.TryGetValue(word, out var audioUrl);
        return Task.FromResult(audioUrl);
    }
}
