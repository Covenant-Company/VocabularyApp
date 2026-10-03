using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

// B11: authored here; host/document execution is explicitly deferred to Phase 8.
public sealed class OpenApiContractTests
{
    [Fact]
    public async Task GeneratedDocumentContainsExactlyTheFourteenCurrentActionsAndSecurityOverrides()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var expected = new Dictionary<string, string>
        {
            ["/api/Users/register"] = "post", ["/api/Users/login"] = "post",
            ["/api/Users/profile"] = "get", ["/api/Users/change-password"] = "post",
            ["/api/Users/validate-token"] = "get", ["/api/Words/lookup/{word}"] = "get",
            ["/api/Words/vocabulary/add"] = "post", ["/api/Words/vocabulary"] = "get",
            ["/api/Words/vocabulary/search"] = "get", ["/api/Words/vocabulary/{userWordId}/favorite"] = "put",
            ["/api/Words/vocabulary/{userWordId}/preferred-definition"] = "put",
            ["/api/quiz/start"] = "post", ["/api/quiz/submit"] = "post", ["/api/quiz/history"] = "get"
        };
        var paths = root.GetProperty("paths");
        Assert.Equal(expected.Count, paths.EnumerateObject().Count());
        foreach (var pair in expected)
        {
            var path = paths.GetProperty(pair.Key);
            var operation = path.GetProperty(pair.Value);
            var security = operation.GetProperty("security");
            if (pair.Key.EndsWith("/register") || pair.Key.EndsWith("/login"))
                Assert.Empty(Assert.Single(security.EnumerateArray()).EnumerateObject());
            else Assert.True(security[0].TryGetProperty("Bearer", out _));
            var responses = operation.GetProperty("responses");
            var success = Resolve(root, responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
            Assert.True(success.GetProperty("properties").TryGetProperty("data", out _));
            Assert.Contains("success", success.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
            Assert.True(responses.GetProperty("500").GetProperty("content").TryGetProperty("application/json", out _));
        }
        Assert.DoesNotContain(root.GetProperty("components").GetProperty("schemas").EnumerateObject(),
            schema => schema.Name.Contains("WordsApi", StringComparison.Ordinal));

        var lookup401 = paths.GetProperty("/api/Words/lookup/{word}").GetProperty("get").GetProperty("responses").GetProperty("401");
        Assert.False(lookup401.TryGetProperty("content", out var challengeContent) && challengeContent.EnumerateObject().Any());
        Assert.False(paths.GetProperty("/api/Words/vocabulary/search").GetProperty("get").GetProperty("responses").TryGetProperty("400", out _));
    }

    [Fact]
    public async Task GeneratedSchemaPreservesValidationVariantsPresenceNullabilityAndCompatibilityFields()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var favorite400 = paths.GetProperty("/api/Words/vocabulary/{userWordId}/favorite").GetProperty("put").GetProperty("responses").GetProperty("400").GetProperty("content");
        Assert.Equal("application/problem+json", Assert.Single(favorite400.EnumerateObject()).Name);
        var validation = favorite400.GetProperty("application/problem+json").GetProperty("schema").GetProperty("properties");
        foreach (var key in new[] { "errors", "traceId", "success", "data", "error", "message", "code" }) Assert.True(validation.TryGetProperty(key, out _));
        var submit400 = paths.GetProperty("/api/quiz/submit").GetProperty("post").GetProperty("responses").GetProperty("400").GetProperty("content");
        Assert.True(submit400.TryGetProperty("application/json", out _));
        Assert.True(submit400.TryGetProperty("application/problem+json", out _));

        var schemas = root.GetProperty("components").GetProperty("schemas");
        foreach (var pair in new[] { ("UpdateFavoriteRequestDto", "isFavorite"), ("QuizAnswerSubmissionDto", "selectedOptionId") })
        {
            var schema = schemas.GetProperty(pair.Item1);
            Assert.Contains(pair.Item2, schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
            var property = schema.GetProperty("properties").GetProperty(pair.Item2);
            Assert.False(property.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean());
        }
        var submit = schemas.GetProperty("QuizSubmitRequestDto");
        Assert.False(submit.TryGetProperty("required", out var required) && required.EnumerateArray().Any(item => item.GetString() == "answers"));
        var result = schemas.GetProperty("QuizQuestionResultDto");
        Assert.Contains("selectedAnswer", result.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.True(result.GetProperty("properties").GetProperty("selectedAnswer").GetProperty("nullable").GetBoolean());
        Assert.True(schemas.GetProperty("UserDto").GetProperty("properties").GetProperty("lastLoginAt").GetProperty("nullable").GetBoolean());
        var add = schemas.GetProperty("AddWordRequest").GetProperty("properties");
        foreach (var key in new[] { "definition", "example", "pronunciation" }) Assert.True(add.GetProperty(key).GetProperty("deprecated").GetBoolean());
        Assert.True(add.TryGetProperty("partOfSpeech", out _));
        Assert.True(add.TryGetProperty("preferredWordDefinitionId", out _));
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
            return root.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]);
        if (schema.TryGetProperty("allOf", out var allOf)) return Resolve(root, allOf[0]);
        return schema;
    }
}
