using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VocabularyApp.WebApi.Controllers;
using VocabularyApp.WebApi.DTOs;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public static class ApiTestClientHelper
{
    private static readonly JsonSerializerOptions WebJsonOptions =
        new(JsonSerializerDefaults.Web);

    public static async Task<ApiCallResult<AuthResponse>> RegisterAsync(
        HttpClient client,
        TestUserCredentials credentials)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/users/register",
            new CreateUserRequest
            {
                Username = credentials.Username,
                Email = credentials.Email,
                Password = credentials.Password
            });

        return await ReadApiResultAsync<AuthResponse>(response);
    }

    public static async Task<AuthenticatedTestUser> LoginAsync(
        HttpClient client,
        TestUserCredentials credentials)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/users/login",
            new LoginRequest
            {
                Username = credentials.Username,
                Password = credentials.Password
            });
        var result = await ReadApiResultAsync<AuthResponse>(response);
        var auth = result.Data;

        if (result.StatusCode != HttpStatusCode.OK ||
            !result.Success ||
            auth?.User is null ||
            string.IsNullOrWhiteSpace(auth.Token))
        {
            throw new InvalidOperationException(
                $"API login did not return a usable token. Status: {(int)result.StatusCode}; " +
                $"Error: {result.Error ?? "none"}; Body: {result.RawContent}");
        }

        return new AuthenticatedTestUser(credentials, auth.User, auth.Token);
    }

    public static HttpClient CreateClientWithBearerToken(
        VocabularyAppWebApplicationFactory factory,
        string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("A bearer token is required.", nameof(token));
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<AuthenticatedApiClient> RegisterAndCreateAuthenticatedClientAsync(
        VocabularyAppWebApplicationFactory factory,
        TestUserCredentials? credentials = null)
    {
        credentials ??= TestUserCredentials.CreateUnique();
        using (var registrationClient = factory.CreateClient())
        {
            var registration = await RegisterAsync(registrationClient, credentials);
            if (registration.StatusCode != HttpStatusCode.OK || !registration.Success)
            {
                throw new InvalidOperationException(
                    $"API registration failed. Status: {(int)registration.StatusCode}; " +
                    $"Error: {registration.Error ?? "none"}; Body: {registration.RawContent}");
            }
        }

        using var loginClient = factory.CreateClient();
        var user = await LoginAsync(loginClient, credentials);
        return new AuthenticatedApiClient(
            CreateClientWithBearerToken(factory, user.Token),
            user);
    }

    public static async Task<TwoAuthenticatedUsers> CreateTwoAuthenticatedUsersAsync(
        VocabularyAppWebApplicationFactory factory)
    {
        var userA = await RegisterAndCreateAuthenticatedClientAsync(
            factory,
            TestUserCredentials.CreateUnique("user-a"));

        try
        {
            var userB = await RegisterAndCreateAuthenticatedClientAsync(
                factory,
                TestUserCredentials.CreateUnique("user-b"));
            return new TwoAuthenticatedUsers(userA, userB);
        }
        catch
        {
            userA.Dispose();
            throw;
        }
    }

    public static async Task<ApiCallResult<T>> ReadApiResultAsync<T>(
        HttpResponseMessage response)
    {
        var rawContent = await response.Content.ReadAsStringAsync();
        ApiResult<T>? envelope = null;

        if (!string.IsNullOrWhiteSpace(rawContent))
        {
            try
            {
                envelope = JsonSerializer.Deserialize<ApiResult<T>>(
                    rawContent,
                    WebJsonOptions);
            }
            catch (JsonException)
            {
                // Preserve the raw response so infrastructure failures remain diagnosable.
            }
        }

        var data = envelope is null ? default : envelope.Data;

        return new ApiCallResult<T>(
            response.StatusCode,
            envelope?.Success ?? false,
            data,
            envelope?.Error,
            rawContent);
    }
}

public sealed record ApiCallResult<T>(
    HttpStatusCode StatusCode,
    bool Success,
    T? Data,
    string? Error,
    string RawContent);

public sealed record AuthenticatedTestUser(
    TestUserCredentials Credentials,
    UserDto User,
    string Token);

public sealed class AuthenticatedApiClient : IDisposable
{
    public AuthenticatedApiClient(HttpClient client, AuthenticatedTestUser user)
    {
        Client = client;
        User = user;
    }

    public HttpClient Client { get; }

    public AuthenticatedTestUser User { get; }

    public void Dispose() => Client.Dispose();
}

public sealed class TwoAuthenticatedUsers : IDisposable
{
    public TwoAuthenticatedUsers(
        AuthenticatedApiClient userA,
        AuthenticatedApiClient userB)
    {
        UserA = userA;
        UserB = userB;
    }

    public AuthenticatedApiClient UserA { get; }

    public AuthenticatedApiClient UserB { get; }

    public void Dispose()
    {
        UserB.Dispose();
        UserA.Dispose();
    }
}
