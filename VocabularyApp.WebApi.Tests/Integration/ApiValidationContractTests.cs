using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ApiValidationContractTests
{
    [Theory]
    [InlineData("POST", "/api/users/register", "{}")]
    [InlineData("POST", "/api/users/register", "{\"username\":\"ab\",\"email\":\"invalid\",\"password\":\"short\"}")]
    [InlineData("POST", "/api/users/login", "null")]
    [InlineData("POST", "/api/users/login", "")]
    [InlineData("POST", "/api/users/login", "{\"password\":{")]
    [InlineData("POST", "/api/users/login", "{\"private-secret-sql-provider-connection-sentinel\": !}")]
    [InlineData("POST", "/api/users/login", "{\"username\":null,\"password\":null}")]
    [InlineData("POST", "/api/users/change-password", "{}")]
    [InlineData("GET", "/api/words/vocabulary?page=private-secret-sql-provider-connection-sentinel", null)]
    [InlineData("GET", "/api/quiz/history?take=private-secret-sql-provider-connection-sentinel", null)]
    [InlineData("PUT", "/api/words/vocabulary/1/favorite", "{\"isFavorite\":\"private-secret-sql-provider-connection-sentinel\"}")]
    [InlineData("PUT", "/api/words/vocabulary/1/preferred-definition", "{\"preferredWordDefinitionId\":\"private-secret-sql-provider-connection-sentinel\"}")]
    [InlineData("POST", "/api/quiz/submit", "{\"sessionId\":\"private-secret-sql-provider-connection-sentinel\"}")]
    public async Task InvalidRequestsRetainSanitizedProblemDetails(string method, string route, string? body)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Headers.Add("X-Trace-Id", ApiErrorContractAssert.Sentinel);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await authenticated.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        JsonContractAssert.Properties(root, "type", "title", "status", "errors", "traceId", "success", "data", "error", "message", "code");
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        JsonContractAssert.Property(root, "success", JsonValueKind.False);
        JsonContractAssert.Property(root, "data", JsonValueKind.Null);
        Assert.Equal("validation_failed", root.GetProperty("code").GetString());
        Assert.Equal("One or more request fields are invalid.", root.GetProperty("error").GetString());
        Assert.Equal(root.GetProperty("error").GetString(), root.GetProperty("message").GetString());
        var fields = root.GetProperty("errors").EnumerateObject().ToArray();
        Assert.NotEmpty(fields);
        foreach (var field in fields)
        {
            Assert.False(string.IsNullOrWhiteSpace(field.Name));
            Assert.Equal(JsonValueKind.Array, field.Value.ValueKind);
            Assert.NotEmpty(field.Value.EnumerateArray());
            foreach (var error in field.Value.EnumerateArray())
                Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
        }
        if (body == "{}" && route.EndsWith("register"))
            Assert.Contains("The Username field is required.", raw);
        if (route.Contains(ApiErrorContractAssert.Sentinel) || (body?.Contains(ApiErrorContractAssert.Sentinel) ?? false))
            Assert.Contains("Invalid value.", raw);
        Assert.DoesNotContain(ApiErrorContractAssert.Sentinel, raw);
        Assert.DoesNotContain("System.", raw);
        Assert.DoesNotContain("Exception", raw);
        Assert.DoesNotContain(authenticated.User.Token, raw);
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Theory]
    [InlineData(3, 6, 20, true)]
    [InlineData(100, 100, 200, true)]
    [InlineData(2, 6, 20, false)]
    [InlineData(101, 6, 20, false)]
    [InlineData(3, 5, 20, false)]
    [InlineData(3, 101, 20, false)]
    [InlineData(3, 6, 201, false)]
    public async Task RegistrationAnnotationBoundsRemainEnforced(int usernameLength, int passwordLength, int emailLength, bool valid)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        // EmailAddress validates syntax, while StringLength owns the existing 200 limit.
        var request = new CreateUserRequest
        {
            Username = new string('u', usernameLength),
            Password = new string('p', passwordLength),
            Email = "user@" + new string('e', emailLength - 10) + ".test"
        };
        using var response = await client.PostAsJsonAsync("/api/users/register", request);
        if (valid)
        {
            using var json = await JsonContractAssert.ReadSuccessAsync(response);
            var data = JsonContractAssert.SuccessData(json.RootElement, authEnvelope: true);
            Assert.True(data.GetProperty("success").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("token").GetString()));
            Assert.Equal(request.Username, data.GetProperty("user").GetProperty("username").GetString());
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("validation_failed", json.RootElement.GetProperty("code").GetString());
            var field = usernameLength is < 3 or > 100 ? "Username" : passwordLength is < 6 or > 100 ? "Password" : "Email";
            var messages = json.RootElement.GetProperty("errors").GetProperty(field).EnumerateArray().ToArray();
            Assert.NotEmpty(messages);
            Assert.All(messages, message =>
            {
                Assert.Contains(field, message.GetString()!);
                Assert.DoesNotContain(request.Password, message.GetString()!);
            });
        }
        using var scope = factory.Services.CreateScope();
        Assert.Equal(valid ? 1 : 0, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Fact]
    public async Task UnsupportedMediaRemainsFramework415()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/users/login", new StringContent("not json", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(415, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("code", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-bearer-sentinel")]
    public async Task BearerChallengeRetainsEmptyBody(string? token)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var response = await client.GetAsync("/api/users/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }
}
