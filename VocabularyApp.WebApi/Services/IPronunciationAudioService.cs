namespace VocabularyApp.WebApi.Services;

public interface IPronunciationAudioService
{
    Task<string?> GetAudioUrlAsync(
        string word,
        CancellationToken cancellationToken = default);
}
