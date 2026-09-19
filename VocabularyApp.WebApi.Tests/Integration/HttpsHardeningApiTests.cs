using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class HttpsHardeningApiTests
{
    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("https://localhost:4200")]
    public async Task DevelopmentAllowsConfiguredAngularPreflight(string origin)
    {
        await using var factory = new VocabularyAppWebApplicationFactory("Development");
        using var client = CreateClient(factory, "http://localhost");
        Assert.Equal("Development", factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        var origins = factory.Services.GetRequiredService<IConfiguration>()
            .GetSection("Cors:AllowedOrigins").Get<string[]>();
        Assert.Equal(new[] { "http://localhost:4200", "https://localhost:4200" }, origins);
        using var request = Preflight(origin);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
        var headers = response.Headers.GetValues("Access-Control-Allow-Headers").Single().ToLowerInvariant();
        Assert.Contains("authorization", headers);
        Assert.Contains("content-type", headers);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("Production", "http://localhost:4200")]
    [InlineData("Production", "https://localhost:4200")]
    [InlineData("Production", "http://ripcody-001-site1.anytempurl.com")]
    [InlineData("Testing", "http://localhost:4200")]
    [InlineData("Staging", "http://localhost:4200")]
    [InlineData("Development", "https://unlisted.example")]
    public async Task CrossOriginPermissionIsNotGrantedOutsideDevelopmentAllowlist(string environment, string origin)
    {
        await using var factory = new VocabularyAppWebApplicationFactory(environment);
        // Even a stale external origin configuration cannot activate a Production policy.
        await using var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                if (environment == "Production")
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Cors:AllowedOrigins:0"] = origin
                    });
            }));
        using var client = CreateClient(configuredFactory, "https://myvocabularybuilder.org");
        Assert.Equal(environment, configuredFactory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        if (environment == "Production")
            Assert.Equal(origin, configuredFactory.Services.GetRequiredService<IConfiguration>()["Cors:AllowedOrigins:0"]);
        using var preflight = Preflight(origin);
        using var preflightResponse = await client.SendAsync(preflight);
        Assert.False(preflightResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(preflightResponse.Headers.Contains("Access-Control-Allow-Credentials"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/profile");
        request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("Development", "http://localhost")]
    [InlineData("Testing", "http://localhost")]
    [InlineData("Production", "https://myvocabularybuilder.org")]
    public async Task TransportPolicyKeepsAuthentication(string environment, string baseAddress)
    {
        await using var factory = new VocabularyAppWebApplicationFactory(environment);
        using var client = CreateClient(factory, baseAddress);
        Assert.Equal(environment, factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var response = await client.GetAsync("/api/users/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        Assert.Null(response.Headers.Location);
        if (environment == "Production")
            Assert.Equal("max-age=300", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
        else
            Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        // TestServer intentionally does not simulate the IIS transport rules.
    }

    [Theory]
    [InlineData("/swagger/index.html", HttpStatusCode.OK)]
    [InlineData("/swagger/v1/swagger.json", HttpStatusCode.OK)]
    [InlineData("/api/users/profile", HttpStatusCode.Unauthorized)]
    public async Task ProductionHttpsIncludesOnlyShortHstsPolicy(string path, HttpStatusCode status)
    {
        await using var factory = new VocabularyAppWebApplicationFactory("Production");
        // TestServer has no TLS listener: BaseAddress supplies Request.Scheme/Host.
        // The canonical host also avoids middleware's built-in localhost exclusion.
        using var client = CreateClient(factory, "https://MYVOCABULARYBUILDER.ORG");
        using var response = await client.GetAsync(path);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("max-age=300", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("Development", "https://myvocabularybuilder.org")]
    [InlineData("Development", "http://localhost")]
    [InlineData("Testing", "https://myvocabularybuilder.org")]
    [InlineData("Staging", "https://myvocabularybuilder.org")]
    [InlineData("Production", "http://myvocabularybuilder.org")]
    [InlineData("Production", "https://localhost")]
    [InlineData("Production", "https://127.0.0.1")]
    [InlineData("Production", "https://ripcody-001-site1.anytempurl.com")]
    [InlineData("Production", "https://sub.myvocabularybuilder.org")]
    public async Task HstsIsAbsentOutsideProductionCanonicalHttps(string environment, string address)
    {
        await using var factory = new VocabularyAppWebApplicationFactory(environment);
        using var client = CreateClient(factory, address);
        using var response = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ProductionHttpsStaticFilesAndSpaFallbackIncludeHsts()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "psh1-hsts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<app-root>HSTS fixture</app-root>");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "fixture.js"), "const hstsFixture = true;");
            await using var factory = new VocabularyAppWebApplicationFactory("Production");
            await using var configuredFactory = factory.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));
            using var client = CreateClient(configuredFactory, "https://myvocabularybuilder.org");
            foreach (var path in new[] { "/", "/login", "/fixture.js" })
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains(path.EndsWith(".js") ? "hstsFixture" : "HSTS fixture", await response.Content.ReadAsStringAsync());
                Assert.Equal("max-age=300", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
                Assert.Null(response.Headers.Location);
            }
        }
        finally
        {
            File.Delete(Path.Combine(webRoot, "index.html"));
            File.Delete(Path.Combine(webRoot, "fixture.js"));
            Directory.Delete(webRoot);
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string address) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(address), AllowAutoRedirect = false
        });

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/users/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return request;
    }
}
