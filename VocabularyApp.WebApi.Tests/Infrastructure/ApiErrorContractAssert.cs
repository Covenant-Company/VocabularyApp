using System.Net;
using System.Text.Json;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

internal static class ApiErrorContractAssert
{
    public const string Sentinel = "private-secret-sql-provider-connection-sentinel";
    public const string InternalMessage = "An internal error occurred. Please try again.";

    public static async Task ApplicationAsync(HttpResponseMessage response, HttpStatusCode status,
        string code, string message, params string[] secrets)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        JsonContractAssert.Properties(root, "success", "data", "error", "message", "code", "traceId");
        JsonContractAssert.Property(root, "success", JsonValueKind.False);
        JsonContractAssert.Property(root, "data", JsonValueKind.Null);
        Assert.Equal(message, root.GetProperty("error").GetString());
        Assert.Equal(message, root.GetProperty("message").GetString());
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(Sentinel, raw);
        foreach (var secret in secrets) Assert.DoesNotContain(secret, raw);
        Assert.DoesNotContain("Exception", raw);
        Assert.DoesNotContain("System.", raw);
    }

    public static Task InternalAsync(HttpResponseMessage response, params string[] secrets) =>
        ApplicationAsync(response, HttpStatusCode.InternalServerError, "internal_error", InternalMessage, secrets);
}
