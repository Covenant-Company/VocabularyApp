using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class ControllableDictionaryHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, StubResponse> _responses = new();
    private readonly ConcurrentQueue<Uri> _requests = new();
    private readonly ConcurrentQueue<string?> _apiKeys = new();

    public IReadOnlyCollection<Uri> Requests => _requests.ToArray();
    public IReadOnlyCollection<string?> ApiKeys => _apiKeys.ToArray();

    public void RegisterJson(
        string absolutePath,
        HttpStatusCode statusCode,
        string json) =>
        _responses[absolutePath] = new StubResponse(statusCode, json);

    public void RegisterException(string absolutePath, Exception exception) =>
        _responses[absolutePath] = new StubResponse(null, null, exception);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestUri = request.RequestUri
            ?? throw new InvalidOperationException("Dictionary request did not contain a URI.");
        _requests.Enqueue(requestUri);
        _apiKeys.Enqueue(request.Headers.TryGetValues("X-RapidAPI-Key", out var values)
            ? values.SingleOrDefault()
            : null);

        if (!_responses.TryGetValue(requestUri.AbsolutePath, out var response))
        {
            throw new InvalidOperationException(
                $"Unexpected outbound dictionary request during an integration test: {request.Method} {requestUri}");
        }

        if (response.Exception is not null)
        {
            return Task.FromException<HttpResponseMessage>(response.Exception);
        }

        return Task.FromResult(new HttpResponseMessage(response.StatusCode!.Value)
        {
            Content = new StringContent(response.Json!, Encoding.UTF8, "application/json"),
            RequestMessage = request
        });
    }

    private sealed record StubResponse(
        HttpStatusCode? StatusCode,
        string? Json,
        Exception? Exception = null);
}
