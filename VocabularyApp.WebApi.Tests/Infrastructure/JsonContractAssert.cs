using System.Net;
using System.Text.Json;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

internal static class JsonContractAssert
{
    public static async Task<JsonDocument> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public static void Properties(JsonElement value, params string[] names)
    {
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal),
            value.EnumerateObject().Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    public static JsonElement Property(JsonElement value, string name, JsonValueKind kind)
    {
        var property = value.GetProperty(name);
        Assert.Equal(kind, property.ValueKind);
        return property;
    }

    public static JsonElement SuccessData(JsonElement root, bool authEnvelope = false)
    {
        Properties(root, authEnvelope ? ["success", "data", "error"] : ["success", "data"]);
        Property(root, "success", JsonValueKind.True);
        if (authEnvelope) Property(root, "error", JsonValueKind.Null);
        return Property(root, "data", JsonValueKind.Object);
    }
}
