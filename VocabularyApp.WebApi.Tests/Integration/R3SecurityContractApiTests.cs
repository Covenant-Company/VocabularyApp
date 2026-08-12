using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VocabularyApp.Data;
using VocabularyApp.WebApi.Controllers;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class R3SecurityContractApiTests
{
    private static readonly string[] ClassifiedControllerActions =
    [
        "QuizController.GetQuizHistory:AuthenticationRequired",
        "QuizController.StartQuiz:AuthenticationRequired",
        "QuizController.SubmitQuiz:AuthenticationRequired",
        "UsersController.ChangePassword:AuthenticationRequired",
        "UsersController.GetProfile:AuthenticationRequired",
        "UsersController.Login:AnonymousByDesign",
        "UsersController.Register:AnonymousByDesign",
        "UsersController.ValidateToken:AuthenticationRequired",
        "WordsController.AddToVocabulary:AuthenticationRequired",
        "WordsController.GetUserVocabulary:AuthenticationRequired",
        "WordsController.LookupWord:AuthenticationRequired",
        "WordsController.SearchUserVocabulary:AuthenticationRequired",
        "WordsController.SetFavorite:AuthenticationRequired",
        "WordsController.SetPreferredDefinition:AuthenticationRequired"
    ];

    public static TheoryData<HttpMethod, string, object?> AuthenticationRequiredRoutes =>
        new()
        {
            { HttpMethod.Get, "/api/users/profile", null },
            {
                HttpMethod.Post,
                "/api/users/change-password",
                new { currentPassword = "Current password!", newPassword = "Replacement password!" }
            },
            { HttpMethod.Get, "/api/users/validate-token", null },
            { HttpMethod.Get, "/api/words/lookup/security-contract", null },
            {
                HttpMethod.Post,
                "/api/words/vocabulary/add",
                new { word = "security-contract", partOfSpeech = "Noun" }
            },
            { HttpMethod.Get, "/api/words/vocabulary", null },
            { HttpMethod.Get, "/api/words/vocabulary/search?term=security", null },
            {
                HttpMethod.Put,
                "/api/words/vocabulary/1/favorite",
                new { isFavorite = true }
            },
            {
                HttpMethod.Put,
                "/api/words/vocabulary/1/preferred-definition",
                new { preferredWordDefinitionId = 1 }
            },
            { HttpMethod.Post, "/api/quiz/start", new { questionCount = 5 } },
            { HttpMethod.Post, "/api/quiz/submit", new { quizSessionId = "missing", answers = Array.Empty<object>() } },
            { HttpMethod.Get, "/api/quiz/history?take=5", null }
        };

    [Theory]
    [MemberData(nameof(AuthenticationRequiredRoutes))]
    public async Task AnonymousApplicationRoutesReturnUnauthorized(
        HttpMethod method,
        string route,
        object? body)
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void EveryControllerActionHasAnExplicitR3SecurityClassification()
    {
        var controllerTypes = new[]
        {
            typeof(QuizController),
            typeof(UsersController),
            typeof(WordsController)
        };
        var classificationsByAction = ClassifiedControllerActions
            .Select(value => value.Split(':', 2)[0])
            .OrderBy(value => value)
            .ToArray();
        var discoveredActions = controllerTypes
            .SelectMany(controllerType => controllerType
                .GetMethods(System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes(inherit: true)
                    .Any(attribute => attribute is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute))
                .Select(method => $"{controllerType.Name}.{method.Name}"))
            .OrderBy(value => value)
            .ToArray();

        Assert.Equal(classificationsByAction, discoveredActions);
    }

    [Fact]
    public async Task AnonymousRegistrationAndLoginRemainReachable()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        using var client = factory.CreateClient();
        var credentials = TestUserCredentials.CreateUnique("r3-anonymous");

        var registration = await ApiTestClientHelper.RegisterAsync(client, credentials);
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/users/login",
            new LoginRequest
            {
                Username = credentials.Username,
                Password = credentials.Password
            });

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task AnonymousLookupCannotContactProviderOrPersistCanonicalData()
    {
        using var factory = new VocabularyAppWebApplicationFactory();
        var wordText = $"anonymous-lookup-{Guid.NewGuid():N}";
        factory.DictionaryHandler.RegisterJson(
            $"/api/v2/entries/en/{Uri.EscapeDataString(wordText)}",
            HttpStatusCode.OK,
            "[]");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/words/lookup/{wordText}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.DictionaryHandler.Requests);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await context.Words.AnyAsync(word => word.Text == wordText));
    }
}
