using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.DTOs.External;

namespace VocabularyApp.WebApi.Services;

public sealed partial class MerriamWebsterPronunciationService : IPronunciationAudioService
{
    private const string MediaBaseUrl =
        "https://media.merriam-webster.com/audio/prons/en/us/mp3";

    private readonly HttpClient _httpClient;
    private readonly MerriamWebsterOptions _options;
    private readonly ILogger<MerriamWebsterPronunciationService> _logger;

    public MerriamWebsterPronunciationService(
        HttpClient httpClient,
        MerriamWebsterOptions options,
        ILogger<MerriamWebsterPronunciationService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<string?> GetAudioUrlAsync(
        string word,
        CancellationToken cancellationToken = default)
    {
        var normalizedWord = NormalizeMatchValue(word);
        if (string.IsNullOrWhiteSpace(normalizedWord) ||
            string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return null;
        }

        try
        {
            var requestPath =
                $"api/v3/references/collegiate/json/{Uri.EscapeDataString(word.Trim())}" +
                $"?key={Uri.EscapeDataString(_options.ApiKey)}";
            using var response = await _httpClient.GetAsync(requestPath, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogProviderStatus(response.StatusCode, normalizedWord);
                return null;
            }

            await using var responseStream =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document =
                await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "Merriam-Webster returned an unexpected response shape for '{Word}'",
                    normalizedWord);
                return null;
            }

            var entries = new List<(MerriamWebsterEntry Entry, int Rank, int Order)>();
            var order = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    // A string array is Merriam-Webster's spelling-suggestion response.
                    return null;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var entry = item.Deserialize<MerriamWebsterEntry>();
                if (entry is null)
                {
                    continue;
                }

                var rank = GetMatchRank(entry, normalizedWord);
                if (rank > 0)
                {
                    entries.Add((entry, rank, order));
                }

                order++;
            }

            foreach (var candidate in entries
                         .OrderBy(item => item.Rank)
                         .ThenBy(item => item.Order))
            {
                var audioId = candidate.Entry.HeadwordInformation?.Pronunciations
                    ?.Select(pronunciation => pronunciation.Sound?.Audio?.Trim())
                    .FirstOrDefault(IsValidAudioIdentifier);
                if (audioId is not null)
                {
                    return BuildAudioUrl(audioId);
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Merriam-Webster audio lookup timed out for '{Word}'",
                normalizedWord);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or JsonException
                                   or NotSupportedException
                                   or InvalidOperationException)
        {
            _logger.LogWarning(
                "Merriam-Webster audio lookup failed for '{Word}': {FailureType}",
                normalizedWord,
                ex.GetType().Name);
            return null;
        }
    }

    internal static string? BuildAudioUrl(string? audioIdentifier)
    {
        var audioId = audioIdentifier?.Trim();
        if (!IsValidAudioIdentifier(audioId))
        {
            return null;
        }

        var validatedAudioId = audioId!;

        string subdirectory;
        if (validatedAudioId.StartsWith("bix", StringComparison.OrdinalIgnoreCase))
        {
            subdirectory = "bix";
        }
        else if (validatedAudioId.StartsWith("gg", StringComparison.OrdinalIgnoreCase))
        {
            subdirectory = "gg";
        }
        else if (!char.IsLetter(validatedAudioId[0]))
        {
            subdirectory = "number";
        }
        else
        {
            subdirectory = char.ToLowerInvariant(validatedAudioId[0]).ToString();
        }

        return $"{MediaBaseUrl}/{subdirectory}/{validatedAudioId}.mp3";
    }

    private static int GetMatchRank(
        MerriamWebsterEntry entry,
        string normalizedWord)
    {
        var headword = NormalizeMatchValue(entry.HeadwordInformation?.Headword);
        var metadataId = NormalizeMetadataId(entry.Metadata?.Id);
        if (headword == normalizedWord || metadataId == normalizedWord)
        {
            return 1;
        }

        return entry.Metadata?.Stems?.Any(stem =>
            NormalizeMatchValue(stem) == normalizedWord) == true
            ? 2
            : 0;
    }

    private static string NormalizeMetadataId(string? value)
    {
        var normalized = NormalizeMatchValue(value);
        var homographSeparator = normalized.LastIndexOf(':');
        return homographSeparator > 0 &&
               int.TryParse(normalized[(homographSeparator + 1)..], out _)
            ? normalized[..homographSeparator]
            : normalized;
    }

    private static string NormalizeMatchValue(string? value) =>
        (value ?? string.Empty)
        .Trim()
        .Replace("*", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant();

    private static bool IsValidAudioIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && AudioIdentifierPattern().IsMatch(value);

    private void LogProviderStatus(HttpStatusCode statusCode, string word)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(
                "Merriam-Webster rejected audio-provider credentials with status {StatusCode} for '{Word}'",
                (int)statusCode,
                word);
            return;
        }

        _logger.LogWarning(
            "Merriam-Webster audio lookup returned status {StatusCode} for '{Word}'",
            (int)statusCode,
            word);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AudioIdentifierPattern();
}
