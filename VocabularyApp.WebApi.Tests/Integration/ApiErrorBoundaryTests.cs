using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Helpers;
using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.Services;
using VocabularyApp.WebApi.Tests.Infrastructure;

namespace VocabularyApp.WebApi.Tests.Integration;

public sealed class ApiErrorBoundaryTests
{
    [Theory]
    [InlineData("POST", "/api/users/register", "{\"username\":\"test-user\",\"email\":\"test@example.test\",\"password\":\"password\"}")]
    [InlineData("POST", "/api/users/login", "{\"username\":\"test-user\",\"password\":\"password\"}")]
    [InlineData("POST", "/api/users/change-password", "{\"currentPassword\":\"password\",\"newPassword\":\"password-new\"}")]
    [InlineData("GET", "/api/words/lookup/test", null)]
    [InlineData("POST", "/api/words/vocabulary/add", "{\"word\":\"test\"}")]
    [InlineData("GET", "/api/words/vocabulary", null)]
    [InlineData("GET", "/api/words/vocabulary/search?term=test", null)]
    [InlineData("PUT", "/api/words/vocabulary/1/favorite", "{\"isFavorite\":true}")]
    [InlineData("PUT", "/api/words/vocabulary/1/preferred-definition", "{\"preferredWordDefinitionId\":1}")]
    [InlineData("POST", "/api/quiz/start", "{}")]
    [InlineData("POST", "/api/quiz/submit", "{}")]
    [InlineData("GET", "/api/quiz/history", null)]
    public async Task MissingSuccessPayloadAndThrownServiceExceptionsFailSafely(string method, string route, string? body)
    {
        foreach (var throws in new[] { false, true })
        {
            var stub = new BoundaryServices { Throws = throws, SuccessWithoutData = true };
            using var factory = Factory(stub);
            using var client = AuthorizedClient(factory);
            using var request = new HttpRequestMessage(new HttpMethod(method), route);
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            await ApiErrorContractAssert.InternalAsync(response);
        }
    }

    [Theory]
    [InlineData(ServiceFailureType.None, null)]
    [InlineData((ServiceFailureType)999, "invalid_request")]
    [InlineData(ServiceFailureType.Validation, null)]
    [InlineData(ServiceFailureType.Validation, "unknown_code")]
    [InlineData(ServiceFailureType.Validation, "invalid_credentials")]
    [InlineData(ServiceFailureType.NotFound, "dictionary_unavailable")]
    [InlineData(ServiceFailureType.InternalError, "invalid_request")]
    [InlineData(ServiceFailureType.InternalError, "internal_error")]
    [InlineData(ServiceFailureType.Validation, ApiErrorContractAssert.Sentinel)]
    public async Task UnknownOrInvalidFailureCombinationsNeverBecomeClientErrors(ServiceFailureType type, string? code)
    {
        using var factory = Factory(new BoundaryServices { Type = type, Code = code });
        using var client = AuthorizedClient(factory);
        using var response = await client.GetAsync("/api/words/lookup/test");
        await ApiErrorContractAssert.InternalAsync(response);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    public async Task IncompleteNestedAuthSuccessFailsSafely(string action)
    {
        var user = new UserDto { Id = 1, Username = "boundary", Email = "boundary@example.test" };
        foreach (var data in new[]
        {
            new AuthResponse { Success = false, User = user, Token = "fixture-token" },
            new AuthResponse { Success = true, User = null, Token = "fixture-token" },
            new AuthResponse { Success = true, User = user, Token = null },
            new AuthResponse { Success = true, User = user, Token = " " }
        })
        {
            using var factory = Factory(new BoundaryServices { AuthData = data });
            using var client = factory.CreateClient();
            using var response = await client.PostAsync("/api/users/" + action,
                new StringContent("""{"username":"boundary","email":"boundary@example.test","password":"password"}""",
                    Encoding.UTF8, "application/json"));
            await ApiErrorContractAssert.InternalAsync(response, "fixture-token");
        }
    }

    private static VocabularyAppWebApplicationFactory Factory(BoundaryServices stub) => new()
    {
        ConfigureTestServices = services =>
        {
            services.RemoveAll<IUserService>();
            services.RemoveAll<IWordService>();
            services.RemoveAll<IQuizService>();
            services.AddSingleton<IUserService>(stub);
            services.AddSingleton<IWordService>(stub);
            services.AddSingleton<IQuizService>(stub);
        }
    };

    private static HttpClient AuthorizedClient(VocabularyAppWebApplicationFactory factory)
    {
        var token = new JwtHelper(TestJwtSettingsFactory.Create(), NullLogger<JwtHelper>.Instance)
            .GenerateToken(new UserDto { Id = 1, Username = "boundary", Email = "boundary@example.test" });
        return ApiTestClientHelper.CreateClientWithBearerToken(factory, token);
    }

    // Used only for impossible service invariants and controller catches. Persistence
    // classifications are separately exercised through the real services/SQLite host.
    private sealed class BoundaryServices : IUserService, IWordService, IQuizService
    {
        public bool Throws { get; init; }
        public bool SuccessWithoutData { get; init; }
        public ServiceFailureType Type { get; init; }
        public string? Code { get; init; }
        public AuthResponse? AuthData { get; init; }
        private Task<ServiceResult<T>> Result<T>()
        {
            if (Throws) throw new InvalidOperationException(ApiErrorContractAssert.Sentinel);
            return Task.FromResult(SuccessWithoutData ? ServiceResult<T>.Success(default!) :
                ServiceResult<T>.Failure(ApiErrorContractAssert.Sentinel, Type, Code));
        }
        public Task<ServiceResult<AuthResponse>> CreateUserAsync(CreateUserRequest request) => AuthResult();
        public Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request) => AuthResult();
        private Task<ServiceResult<AuthResponse>> AuthResult() => AuthData is null ? Result<AuthResponse>() :
            Task.FromResult(ServiceResult<AuthResponse>.Success(AuthData));
        public Task<ServiceResult<bool>> ChangePasswordAsync(int id, string currentPassword, string newPassword) => Result<bool>();
        public Task<UserDto?> GetUserByIdAsync(int id) => throw new InvalidOperationException(ApiErrorContractAssert.Sentinel);
        public Task<UserDto?> GetUserByUsernameAsync(string username) => throw new NotSupportedException();
        public Task<UserDto?> ValidateTokenAsync(string token) => throw new NotSupportedException();
        public Task<ServiceResult<WordLookupResponse>> LookupWordAsync(string term, int? userId = null) => Result<WordLookupResponse>();
        public Task<ServiceResult<AddToVocabularyResultDto>> AddToVocabularyAsync(int id, AddWordRequest request) => Result<AddToVocabularyResultDto>();
        public Task<ServiceResult<FavoriteUpdateResponseDto>> SetFavoriteAsync(int id, int wordId, bool favorite) => Result<FavoriteUpdateResponseDto>();
        public Task<ServiceResult<PreferredDefinitionUpdateResponseDto>> SetPreferredDefinitionAsync(int id, int wordId, int definitionId) => Result<PreferredDefinitionUpdateResponseDto>();
        public Task<ServiceResult<UserVocabularyResponseDto>> GetUserVocabularyAsync(int id, int page = 1, int pageSize = 20, string? searchTerm = null, string? startsWithLetter = null) => Result<UserVocabularyResponseDto>();
        public Task<ServiceResult<UserVocabularyResponseDto>> SearchUserVocabularyAsync(int id, string? term, int maxResults = 5) => Result<UserVocabularyResponseDto>();
        public Task<ServiceResult<QuizStartResponseDto>> StartQuizAsync(int id, StartQuizRequestDto request) => Result<QuizStartResponseDto>();
        public Task<ServiceResult<QuizSubmitResponseDto>> SubmitQuizAsync(int id, QuizSubmitRequestDto request) => Result<QuizSubmitResponseDto>();
        public Task<ServiceResult<QuizHistoryResponseDto>> GetRecentQuizHistoryAsync(int id, int take = 5) => Result<QuizHistoryResponseDto>();
    }
}
