using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.Services;

namespace VocabularyApp.WebApi.Tests.Infrastructure;

public sealed class VocabularyAppWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly JwtSettings TestJwtSettings = TestJwtSettingsFactory.Create();
    private readonly SqliteConnection _connection;

    static VocabularyAppWebApplicationFactory()
    {
        // WebApplicationFactory executes the top-level entry point before its later
        // configuration callbacks. Standard ASP.NET Core environment keys make the
        // test-only settings visible when WebApplication.CreateBuilder first runs.
        // Keep the deterministic values for the test-process lifetime so disposing one
        // factory cannot remove configuration required by another active factory.
        Environment.SetEnvironmentVariable(
            "JwtSettings__SecretKey",
            TestJwtSettings.SecretKey);
        Environment.SetEnvironmentVariable(
            "JwtSettings__Issuer",
            TestJwtSettings.Issuer);
        Environment.SetEnvironmentVariable(
            "JwtSettings__Audience",
            TestJwtSettings.Audience);
        Environment.SetEnvironmentVariable(
            "JwtSettings__ExpirationMinutes",
            TestJwtSettings.ExpirationMinutes.ToString(CultureInfo.InvariantCulture));
    }

    public VocabularyAppWebApplicationFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Data Source=:memory:;Mode=Memory;Cache=Private",
                ["JwtSettings:SecretKey"] = TestJwtSettings.SecretKey,
                ["JwtSettings:Issuer"] = TestJwtSettings.Issuer,
                ["JwtSettings:Audience"] = TestJwtSettings.Audience,
                ["JwtSettings:ExpirationMinutes"] =
                    TestJwtSettings.ExpirationMinutes.ToString(CultureInfo.InvariantCulture),
                ["Cors:AllowedOrigins:0"] = "https://integration-tests.example"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(_connection));

            services.RemoveAll<IWordService>();
            services.AddHttpClient<IWordService, WordService>()
                .ConfigurePrimaryHttpMessageHandler(
                    static () => new NoNetworkDictionaryHandler());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }

    private sealed class NoNetworkDictionaryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected outbound dictionary request during an integration test: {request.Method} {request.RequestUri}");
    }
}
