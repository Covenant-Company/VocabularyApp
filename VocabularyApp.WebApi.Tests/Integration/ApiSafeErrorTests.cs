using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Helpers;
using VocabularyApp.WebApi.Security;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

[Collection(QuizApiCollection.Name)]
public sealed class ApiSafeErrorTests : QuizApiTestBase
{
    [Theory]
    [InlineData("query")]
    [InlineData("hash")]
    [InlineData("save")]
    [InlineData("token")]
    public async Task RegistrationFaultsAreSafeAndRespectPersistenceBoundary(string stage)
    {
        var save = new SaveFault();
        var query = new QueryFault();
        using var factory = new VocabularyAppWebApplicationFactory
        {
            AdditionalInterceptors = [save, query],
            ConfigureTestServices = services =>
            {
                if (stage == "hash")
                {
                    services.RemoveAll<IPasswordService>();
                    services.AddSingleton<IPasswordService>(new HashFault());
                }
                if (stage == "token")
                {
                    services.RemoveAll<JwtHelper>();
                    services.AddScoped(_ => new JwtHelper(new JwtSettings
                    {
                        SecretKey = "short", Issuer = "test", Audience = "test", ExpirationMinutes = 15
                    }, NullLogger<JwtHelper>.Instance));
                }
            }
        };
        using var client = factory.CreateClient();
        save.Armed = stage == "save";
        query.Armed = stage == "query";
        var credentials = TestUserCredentials.CreateUnique();
        using var response = await client.PostAsJsonAsync("/api/users/register", new
        {
            credentials.Username, credentials.Email, credentials.Password
        });
        await ApiErrorContractAssert.InternalAsync(response, credentials.Password);
        save.Armed = query.Armed = false;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(stage == "token" ? 1 : 0, await db.Users.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateRegistrationHasSafeBusinessCode(bool email)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var credentials = TestUserCredentials.CreateUnique();
        await IntegrationTestSeeder.SeedModernUserAsync(factory, credentials);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/users/register", new
        {
            Username = email ? "another-user" : credentials.Username,
            Email = email ? credentials.Email : "another@example.test", credentials.Password
        });
        await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.BadRequest,
            email ? "email_taken" : "username_taken",
            email ? "Email is already registered" : "Username is already taken", credentials.Password);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrong")]
    [InlineData("malformed")]
    public async Task InvalidCredentialsHaveIdenticalPublicContract(string kind)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var credentials = TestUserCredentials.CreateUnique();
        var user = await IntegrationTestSeeder.SeedModernUserAsync(factory, credentials);
        if (kind == "malformed")
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Users.SingleAsync()).PasswordHash = ApiErrorContractAssert.Sentinel;
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/users/login", new
        {
            Username = kind == "unknown" ? "unknown-user" : user.Username,
            Password = kind == "wrong" ? "wrong-password" : credentials.Password
        });
        await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.Unauthorized,
            "invalid_credentials", "Invalid username or password", credentials.Password);
        using var verificationScope = factory.Services.CreateScope();
        Assert.Null((await verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync()).LastLoginAt);
    }

    [Theory]
    [InlineData("modern", false)]
    [InlineData("legacy", false)]
    [InlineData("rehash", false)]
    [InlineData("modern", true)]
    [InlineData("legacy", true)]
    [InlineData("rehash", true)]
    public async Task LoginSaveFailureSeparatesBookkeepingMigrationAndConcurrency(string credential, bool concurrency)
    {
        var fault = new SaveFault { Concurrency = concurrency };
        var logger = new CapturingLogger<UserService>();
        var hasher = new ControlledPasswordHasher(new Microsoft.AspNetCore.Identity.PasswordHasher<VocabularyApp.Data.Models.User>());
        using var factory = new VocabularyAppWebApplicationFactory
        {
            AdditionalInterceptors = [fault],
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IPasswordService>();
                services.AddSingleton<IPasswordService>(new PasswordService(hasher, new LegacyPasswordVerifier()));
                services.AddSingleton<ILogger<UserService>>(logger);
            }
        };
        var credentials = TestUserCredentials.CreateUnique();
        await IntegrationTestSeeder.SeedModernUserAsync(factory, credentials);
        string originalHash;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync();
            if (credential == "legacy")
            {
                var salt = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
                user.PasswordHash = salt + ":" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.Password + salt)));
                await db.SaveChangesAsync();
            }
            originalHash = user.PasswordHash;
        }
        if (credential == "rehash")
            hasher.VerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded;
        fault.Armed = true;
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/users/login", new { credentials.Username, credentials.Password });
        if (concurrency)
            await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.Conflict,
                "credentials_changed", "Credentials changed. Please sign in again.", originalHash, credentials.Password);
        else if (credential != "modern")
            await ApiErrorContractAssert.InternalAsync(response, originalHash, credentials.Password);
        else
        {
            using var json = await JsonContractAssert.ReadSuccessAsync(response);
            var auth = JsonContractAssert.SuccessData(json.RootElement, authEnvelope: true);
            Assert.True(auth.GetProperty("success").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(auth.GetProperty("token").GetString()));
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error
                && entry.Message.Contains("Error updating last login", StringComparison.Ordinal));
        }
        foreach (var entry in logger.Entries)
        {
            Assert.DoesNotContain(credentials.Password, entry.Message + entry.Exception);
            Assert.DoesNotContain(originalHash, entry.Message + entry.Exception);
        }
        fault.Armed = false;
        using var verify = factory.Services.CreateScope();
        var persisted = await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync();
        Assert.Equal(originalHash, persisted.PasswordHash);
        Assert.Null(persisted.LastLoginAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredReplacementGenerationFailureReturnsInternalWithoutTokenOrMutation(bool emptyReplacement)
    {
        var hasher = new ControlledPasswordHasher(new Microsoft.AspNetCore.Identity.PasswordHasher<VocabularyApp.Data.Models.User>());
        using var factory = new VocabularyAppWebApplicationFactory
        {
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IPasswordService>();
                services.AddSingleton<IPasswordService>(new PasswordService(hasher, new LegacyPasswordVerifier()));
            }
        };
        var credentials = TestUserCredentials.CreateUnique();
        var user = await IntegrationTestSeeder.SeedModernUserAsync(factory, credentials);
        hasher.VerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded;
        hasher.HashPasswordFactory = (_, _) => emptyReplacement
            ? " " : throw new InvalidOperationException(ApiErrorContractAssert.Sentinel);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/users/login", new { credentials.Username, credentials.Password });
        await ApiErrorContractAssert.InternalAsync(response, user.PasswordHash, credentials.Password);
        using var scope = factory.Services.CreateScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync();
        Assert.Equal(user.PasswordHash, persisted.PasswordHash);
        Assert.Null(persisted.LastLoginAt);
    }

    [Theory]
    [InlineData("wrong", 401, "current_password_incorrect", "Current password is incorrect")]
    [InlineData("missing", 401, "user_unavailable", "Your account is unavailable. Please sign in again.")]
    [InlineData("concurrency", 409, "credentials_changed", "Credentials changed. Please sign in again.")]
    [InlineData("save", 500, "internal_error", ApiErrorContractAssert.InternalMessage)]
    public async Task PasswordChangeFailuresRetainCredentialState(string kind, int status, string code, string message)
    {
        var fault = new SaveFault { Concurrency = kind == "concurrency" };
        using var factory = new VocabularyAppWebApplicationFactory { AdditionalInterceptors = [fault] };
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        string originalHash;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync();
            originalHash = user.PasswordHash;
            if (kind == "missing") { db.Users.Remove(user); await db.SaveChangesAsync(); }
        }
        fault.Armed = kind is "save" or "concurrency";
        using var response = await authenticated.Client.PostAsJsonAsync("/api/users/change-password", new
        {
            CurrentPassword = kind == "wrong" ? "wrong-password" : authenticated.User.Credentials.Password,
            NewPassword = "new-password-sentinel"
        });
        await ApiErrorContractAssert.ApplicationAsync(response, (HttpStatusCode)status, code, message,
            originalHash, authenticated.User.Token, "new-password-sentinel");
        fault.Armed = false;
        using var verify = factory.Services.CreateScope();
        var persisted = await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleOrDefaultAsync();
        if (kind == "missing") Assert.Null(persisted);
        else Assert.Equal(originalHash, persisted!.PasswordHash);
    }

    [Theory]
    [InlineData("profile", 404, "user_not_found", "User not found")]
    [InlineData("validate-token", 401, "user_unavailable", "Your account is unavailable. Please sign in again.")]
    public async Task MissingAuthenticatedAccountHasEndpointSpecificContract(string route, int status, string code, string message)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Remove(await db.Users.SingleAsync());
            await db.SaveChangesAsync();
        }
        using var response = await authenticated.Client.GetAsync("/api/users/" + route);
        await ApiErrorContractAssert.ApplicationAsync(response, (HttpStatusCode)status, code, message, authenticated.User.Token);
    }

    [Theory]
    [InlineData("profile", false)]
    [InlineData("profile", true)]
    [InlineData("validate-token", false)]
    [InlineData("validate-token", true)]
    public async Task ValidBearerWithMissingOrMalformedApplicationClaimUsesSafeApplicationError(string route, bool malformed)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        var settings = TestJwtSettingsFactory.Create();
        var claims = malformed ? new[] { new Claim(ClaimTypes.NameIdentifier, ApiErrorContractAssert.Sentinel) } : Array.Empty<Claim>();
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        client.DefaultRequestHeaders.Add("X-Trace-Id", ApiErrorContractAssert.Sentinel);
        using var response = await client.GetAsync("/api/users/" + route);
        await ApiErrorContractAssert.ApplicationAsync(response, HttpStatusCode.Unauthorized, "invalid_token", "Invalid token");
    }

    [Theory]
    [InlineData("/api/users/profile")]
    [InlineData("/api/users/validate-token")]
    [InlineData("/api/words/vocabulary")]
    [InlineData("/api/words/vocabulary/search?term=test")]
    [InlineData("/api/words/lookup/test")]
    [InlineData("/api/quiz/history")]
    public async Task UnexpectedQueryFailuresReachSharedInternalBoundary(string route)
    {
        var fault = new QueryFault();
        using var factory = new VocabularyAppWebApplicationFactory { AdditionalInterceptors = [fault] };
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        fault.Armed = true;
        using var response = await authenticated.Client.GetAsync(route);
        await ApiErrorContractAssert.InternalAsync(response, authenticated.User.Token);
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Theory]
    [InlineData(404, 404, "word_not_found", "No definitions found.")]
    [InlineData(401, 503, "dictionary_unavailable", ApiErrorResults.DictionaryMessage)]
    [InlineData(403, 503, "dictionary_unavailable", ApiErrorResults.DictionaryMessage)]
    [InlineData(429, 503, "dictionary_unavailable", ApiErrorResults.DictionaryMessage)]
    [InlineData(500, 503, "dictionary_unavailable", ApiErrorResults.DictionaryMessage)]
    public async Task RecognizedProviderStatusesUseSafeCatalog(int provider, int status, string code, string message)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        factory.DictionaryHandler.RegisterJson("/words/sentinelword", (HttpStatusCode)provider, ApiErrorContractAssert.Sentinel);
        using var response = await authenticated.Client.GetAsync("/api/words/lookup/sentinelword");
        await ApiErrorContractAssert.ApplicationAsync(response, (HttpStatusCode)status, code, message);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Words.ToListAsync());
    }

    [Theory]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("favorite", false)]
    [InlineData("favorite", true)]
    [InlineData("preferred-definition", false)]
    [InlineData("preferred-definition", true)]
    public async Task VocabularyQueryAndSaveFailuresAreInternalWithoutMutation(string operation, bool saveFailure)
    {
        var query = new QueryFault();
        var save = new SaveFault();
        using var factory = new VocabularyAppWebApplicationFactory { AdditionalInterceptors = [query, save] };
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        var word = await IntegrationTestSeeder.SeedWordWithDefinitionAsync(factory, "faultword", "Original definition");
        var otherDefinition = await IntegrationTestSeeder.SeedDefinitionAsync(factory, word.WordId, "Other definition");
        var userWordId = operation == "add" ? 0 : await IntegrationTestSeeder.SeedUserWordAsync(
            factory, authenticated.User.User.Id, word, correctAnswers: 2, totalAttempts: 3);
        query.Armed = !saveFailure;
        save.Armed = saveFailure;

        using var response = operation == "add"
            ? await authenticated.Client.PostAsJsonAsync("/api/words/vocabulary/add", new { Word = "faultword" })
            : operation == "favorite"
                ? await authenticated.Client.PutAsJsonAsync($"/api/words/vocabulary/{userWordId}/favorite", new { IsFavorite = true })
                : await authenticated.Client.PutAsJsonAsync($"/api/words/vocabulary/{userWordId}/preferred-definition",
                    new { PreferredWordDefinitionId = otherDefinition });
        await ApiErrorContractAssert.InternalAsync(response, authenticated.User.Token);
        query.Armed = save.Armed = false;
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserWords.ToListAsync();
        if (operation == "add") Assert.Empty(rows);
        else
        {
            var row = Assert.Single(rows);
            Assert.False(row.IsFavorite);
            Assert.Equal(word.WordDefinitionId, row.PreferredWordDefinitionId);
            Assert.Equal(word.PartOfSpeechId, row.PartOfSpeechId);
            Assert.Equal(2, row.CorrectAnswers);
            Assert.Equal(3, row.TotalAttempts);
        }
        Assert.Empty(factory.DictionaryHandler.Requests);
    }

    [Fact]
    public async Task LookupCanonicalSaveFailureIsInternalNotProviderUnavailable()
    {
        var save = new SaveFault();
        using var factory = new VocabularyAppWebApplicationFactory { AdditionalInterceptors = [save] };
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        factory.DictionaryHandler.RegisterJson("/words/faultword", HttpStatusCode.OK,
            """{"word":"faultword","results":[{"definition":"A valid definition","partOfSpeech":"noun"}]}""");
        save.Armed = true;
        using var response = await authenticated.Client.GetAsync("/api/words/lookup/faultword");
        await ApiErrorContractAssert.InternalAsync(response, authenticated.User.Token);
        save.Armed = false;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Words.ToListAsync());
        Assert.Empty(await db.WordDefinitions.ToListAsync());
        Assert.Single(factory.DictionaryHandler.Requests);
    }

    [Fact]
    public async Task QuizStartQueryFailureIsInternalWithoutPersistedResults()
    {
        var query = new QueryFault();
        using var factory = new VocabularyAppWebApplicationFactory { AdditionalInterceptors = [query] };
        using var authenticated = await ApiTestClientHelper.RegisterAndCreateAuthenticatedClientAsync(factory);
        query.Armed = true;
        using var response = await authenticated.Client.PostAsJsonAsync("/api/quiz/start", new { QuestionCount = 1 });
        await ApiErrorContractAssert.InternalAsync(response, authenticated.User.Token);
        query.Armed = false;
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().QuizResults.ToListAsync());
    }

    [Theory]
    [InlineData("login", "query")]
    [InlineData("login", "verify")]
    [InlineData("login", "token")]
    [InlineData("change-password", "query")]
    [InlineData("change-password", "verify")]
    [InlineData("change-password", "hash")]
    public async Task UnexpectedAuthenticationFaultsAreInternal(string operation, string stage)
    {
        var query = new QueryFault();
        var hasher = new ControlledPasswordHasher(new Microsoft.AspNetCore.Identity.PasswordHasher<VocabularyApp.Data.Models.User>());
        using var factory = new VocabularyAppWebApplicationFactory
        {
            AdditionalInterceptors = [query],
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IPasswordService>();
                services.AddSingleton<IPasswordService>(new PasswordService(hasher, new LegacyPasswordVerifier()));
                if (stage == "token")
                {
                    services.RemoveAll<JwtHelper>();
                    services.AddScoped(_ => new JwtHelper(new JwtSettings
                    {
                        SecretKey = "short", Issuer = "test", Audience = "test", ExpirationMinutes = 15
                    }, NullLogger<JwtHelper>.Instance));
                }
            }
        };
        var credentials = TestUserCredentials.CreateUnique();
        var user = await IntegrationTestSeeder.SeedModernUserAsync(factory, credentials);
        // Mint the fixture's Bearer with the unchanged test key, independently of
        // the deliberately broken token-generation dependency used in one case.
        var token = new JwtHelper(TestJwtSettingsFactory.Create(), NullLogger<JwtHelper>.Instance)
            .GenerateToken(new UserDto { Id = user.Id, Username = user.Username, Email = user.Email });
        using var client = ApiTestClientHelper.CreateClientWithBearerToken(factory, token);
        query.Armed = stage == "query";
        if (stage == "verify") hasher.VerificationException = new InvalidOperationException(ApiErrorContractAssert.Sentinel);
        if (stage == "hash") hasher.HashPasswordFactory = (_, _) => throw new InvalidOperationException(ApiErrorContractAssert.Sentinel);
        using var response = operation == "login"
            ? await client.PostAsJsonAsync("/api/users/login", new { credentials.Username, credentials.Password })
            : await client.PostAsJsonAsync("/api/users/change-password",
                new { CurrentPassword = credentials.Password, NewPassword = "new-password-sentinel" });
        await ApiErrorContractAssert.InternalAsync(response, token, credentials.Password, user.PasswordHash, "new-password-sentinel");
        query.Armed = false;
        using var scope = factory.Services.CreateScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync();
        Assert.Equal(user.PasswordHash, persisted.PasswordHash);
        if (stage == "token") Assert.NotNull(persisted.LastLoginAt);
        else Assert.Null(persisted.LastLoginAt);
    }

    private sealed class SaveFault : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Concurrency { get; init; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                if (Concurrency) throw new DbUpdateConcurrencyException(ApiErrorContractAssert.Sentinel);
                throw new DbUpdateException(ApiErrorContractAssert.Sentinel);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class QueryFault : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed) throw new InvalidOperationException(ApiErrorContractAssert.Sentinel);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class HashFault : IPasswordService
    {
        public string HashPassword(VocabularyApp.Data.Models.User user, string password) =>
            throw new InvalidOperationException(ApiErrorContractAssert.Sentinel);
        public PasswordVerificationOutcome Verify(VocabularyApp.Data.Models.User user, string storedHash, string password) =>
            throw new InvalidOperationException(ApiErrorContractAssert.Sentinel);
    }
}
