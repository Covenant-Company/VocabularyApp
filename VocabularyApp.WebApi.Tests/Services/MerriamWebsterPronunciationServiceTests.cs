using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Services;

public sealed class MerriamWebsterPronunciationServiceTests
{
    private const string ApiKey = "test-merriam-webster-key";

    [Theory]
    [InlineData("test0001", "t")]
    [InlineData("bixword1", "bix")]
    [InlineData("ggword01", "gg")]
    [InlineData("3d000001", "number")]
    [InlineData("_entry01", "number")]
    public async Task BuildsDocumentedMp3Url(string audioId, string subdirectory)
    {
        var handler = new StubHandler(_ => JsonResponse(
            EntryResponse("test", audioId)));
        var service = CreateService(handler);

        var result = await service.GetAudioUrlAsync("test");

        Assert.Equal(
            $"https://media.merriam-webster.com/audio/prons/en/us/mp3/{subdirectory}/{audioId}.mp3",
            result);
    }

    [Fact]
    public async Task UsesFirstPronunciationFromFirstExactEntryDeterministically()
    {
        var json = """
            [
              {"meta":{"id":"test:1","stems":["test"]},"hwi":{"hw":"test","prs":[{"sound":{"audio":"test0001"}},{"sound":{"audio":"test0002"}}]}},
              {"meta":{"id":"test:2","stems":["test"]},"hwi":{"hw":"test","prs":[{"sound":{"audio":"test0003"}}]}}
            ]
            """;
        var service = CreateService(new StubHandler(_ => JsonResponse(json)));

        var result = await service.GetAudioUrlAsync("test");

        Assert.EndsWith("/t/test0001.mp3", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactEntryRanksBeforeEarlierStemOnlyEntry()
    {
        var json = """
            [
              {"meta":{"id":"testing","stems":["test"]},"hwi":{"hw":"testing","prs":[{"sound":{"audio":"testing1"}}]}},
              {"meta":{"id":"test:1","stems":["test"]},"hwi":{"hw":"test","prs":[{"sound":{"audio":"test0001"}}]}}
            ]
            """;
        var service = CreateService(new StubHandler(_ => JsonResponse(json)));

        var result = await service.GetAudioUrlAsync("test");

        Assert.EndsWith("/t/test0001.mp3", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[\"tester\",\"testing\"]")]
    [InlineData("[{\"meta\":{\"id\":\"unrelated\",\"stems\":[\"unrelated\"]},\"hwi\":{\"hw\":\"unrelated\",\"prs\":[{\"sound\":{\"audio\":\"unrel01\"}}]}}]")]
    [InlineData("[{\"meta\":{\"id\":\"test\",\"stems\":[\"test\"]},\"hwi\":{\"hw\":\"test\",\"prs\":[]}}]")]
    [InlineData("[{\"meta\":{\"id\":\"test\",\"stems\":null},\"hwi\":{\"hw\":\"test\",\"prs\":null}}]")]
    [InlineData("[{\"meta\":{\"id\":\"test\",\"stems\":[\"test\"]},\"hwi\":{\"hw\":\"test\",\"prs\":[{\"sound\":{\"audio\":\"../secret\"}}]}}]")]
    public async Task ReturnsNullForSuggestionsUnrelatedNoAudioOrInvalidAudio(string json)
    {
        var service = CreateService(new StubHandler(_ => JsonResponse(json)));

        Assert.Null(await service.GetAudioUrlAsync("test"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReturnsNullForProviderFailureStatus(HttpStatusCode statusCode)
    {
        var logger = new CapturingLogger<MerriamWebsterPronunciationService>();
        var service = CreateService(
            new StubHandler(_ => new HttpResponseMessage(statusCode)), logger);

        Assert.Null(await service.GetAudioUrlAsync("test"));
        Assert.DoesNotContain(logger.Entries,
            entry => entry.Message.Contains(ApiKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{}")]
    public async Task ReturnsNullForMalformedOrUnexpectedJson(string json)
    {
        var service = CreateService(new StubHandler(_ => JsonResponse(json)));

        Assert.Null(await service.GetAudioUrlAsync("test"));
    }

    [Fact]
    public async Task ReturnsNullForTimeout()
    {
        var service = CreateService(new StubHandler(_ =>
            throw new TaskCanceledException("Simulated timeout.")));

        Assert.Null(await service.GetAudioUrlAsync("test"));
    }

    [Fact]
    public async Task SendsEncodedWordAndKeyWithoutReturningOrLoggingKey()
    {
        Uri? capturedUri = null;
        var logger = new CapturingLogger<MerriamWebsterPronunciationService>();
        var handler = new StubHandler(request =>
        {
            capturedUri = request.RequestUri;
            return JsonResponse(EntryResponse("ice cream", "icecr01"));
        });
        var service = CreateService(handler, logger);

        var result = await service.GetAudioUrlAsync("ice cream");

        Assert.NotNull(result);
        Assert.Contains("ice%20cream", capturedUri?.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains($"key={ApiKey}", capturedUri?.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, result, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Entries,
            entry => entry.Message.Contains(ApiKey, StringComparison.Ordinal));
    }

    private static MerriamWebsterPronunciationService CreateService(
        HttpMessageHandler handler,
        ILogger<MerriamWebsterPronunciationService>? logger = null)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(MerriamWebsterOptions.DefaultBaseUrl),
            Timeout = TimeSpan.FromSeconds(2)
        };
        return new MerriamWebsterPronunciationService(
            client,
            new MerriamWebsterOptions { ApiKey = ApiKey },
            logger ?? new CapturingLogger<MerriamWebsterPronunciationService>());
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string EntryResponse(string word, string audioId) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                meta = new { id = word, stems = new[] { word } },
                hwi = new
                {
                    hw = word.Replace(" ", "*", StringComparison.Ordinal),
                    prs = new[] { new { sound = new { audio = audioId } } }
                }
            }
        });

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(responseFactory(request));
            }
            catch (Exception exception)
            {
                return Task.FromException<HttpResponseMessage>(exception);
            }
        }
    }
}
