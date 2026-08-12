using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class ControllableDictionaryHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, StubResponse> _responses = new();
    private readonly ConcurrentQueue<Uri> _requests = new();

    public IReadOnlyCollection<Uri> Requests => _requests.ToArray();

    public void RegisterJson(
        string absolutePath,
        HttpStatusCode statusCode,
        string json) =>
        _responses[absolutePath] = new StubResponse(statusCode, json);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestUri = request.RequestUri
            ?? throw new InvalidOperationException("Dictionary request did not contain a URI.");
        _requests.Enqueue(requestUri);

        if (!_responses.TryGetValue(requestUri.AbsolutePath, out var response))
        {
            throw new InvalidOperationException(
                $"Unexpected outbound dictionary request during an integration test: {request.Method} {requestUri}");
        }

        return Task.FromResult(new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(response.Json, Encoding.UTF8, "application/json"),
            RequestMessage = request
        });
    }

    private sealed record StubResponse(HttpStatusCode StatusCode, string Json);
}
