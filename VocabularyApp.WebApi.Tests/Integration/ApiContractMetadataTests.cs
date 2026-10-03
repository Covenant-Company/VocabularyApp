using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using VocabularyApp.WebApi.Controllers;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Swagger;

namespace VocabularyApp.WebApi.Tests.Integration;

// Authored in Phase 7; execute only in Phase 8. No app host, HTTP, provider or database.
public sealed class ApiContractMetadataTests
{
    public static IEnumerable<object[]> SuccessContracts()
    {
        yield return new object[] { typeof(UsersController), "Register", typeof(ApiResult<AuthResponse>) };
        yield return new object[] { typeof(UsersController), "Login", typeof(ApiResult<AuthResponse>) };
        yield return new object[] { typeof(UsersController), "GetProfile", typeof(ApiResult<UserDto>) };
        yield return new object[] { typeof(UsersController), "ChangePassword", typeof(ApiResult) };
        yield return new object[] { typeof(UsersController), "ValidateToken", typeof(ApiResult<UserDto>) };
        yield return new object[] { typeof(WordsController), "LookupWord", typeof(SuccessResponse<WordLookupResponse>) };
        yield return new object[] { typeof(WordsController), "AddToVocabulary", typeof(SuccessResponse<AddToVocabularyResultDto>) };
        yield return new object[] { typeof(WordsController), "GetUserVocabulary", typeof(SuccessResponse<UserVocabularyResponseDto>) };
        yield return new object[] { typeof(WordsController), "SearchUserVocabulary", typeof(SuccessResponse<UserVocabularyResponseDto>) };
        yield return new object[] { typeof(WordsController), "SetFavorite", typeof(SuccessResponse<FavoriteUpdateResponseDto>) };
        yield return new object[] { typeof(WordsController), "SetPreferredDefinition", typeof(SuccessResponse<PreferredDefinitionUpdateResponseDto>) };
        yield return new object[] { typeof(QuizController), "StartQuiz", typeof(SuccessResponse<QuizStartResponseDto>) };
        yield return new object[] { typeof(QuizController), "SubmitQuiz", typeof(SuccessResponse<QuizSubmitResponseDto>) };
        yield return new object[] { typeof(QuizController), "GetQuizHistory", typeof(SuccessResponse<QuizHistoryResponseDto>) };
    }

    [Theory]
    [MemberData(nameof(SuccessContracts))]
    public void AllActionsDeclareTheirExistingTypedSuccess(Type controller, string action, Type responseType)
    {
        var method = controller.GetMethod(action)!;
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        Assert.Equal(responseType, Assert.Single(responses.Where(response => response.StatusCode == 200)).Type);
        Assert.Equal(typeof(ApiErrorResponse), Assert.Single(responses.Where(response => response.StatusCode == 500)).Type);
    }

    [Theory]
    [MemberData(nameof(SuccessContracts))]
    public void AnonymousOverridesAndFallbackLookupSecurityAreExplicit(Type controller, string action, Type responseType)
    {
        var operation = OperationFor(controller, action);
        ApplyOperationFilter(operation, controller, action);
        if (action is "Register" or "Login") Assert.Empty(Assert.Single(operation.Security));
        else Assert.Equal("Bearer", Assert.Single(Assert.Single(operation.Security).Keys).Reference.Id);
    }

    [Fact]
    public void ValidationAndBusiness400HaveDistinctContentTypesAndExtensions()
    {
        var favorite = OperationFor(typeof(WordsController), "SetFavorite");
        ApplyOperationFilter(favorite, typeof(WordsController), "SetFavorite");
        Assert.Equal("application/problem+json", Assert.Single(favorite.Responses["400"].Content).Key);
        var validation = favorite.Responses["400"].Content["application/problem+json"].Schema;
        foreach (var extension in new[] { "traceId", "success", "data", "error", "message", "code" })
            Assert.Contains(extension, validation.Required);
        Assert.Contains("errors", validation.Properties.Keys);

        var submit = OperationFor(typeof(QuizController), "SubmitQuiz");
        ApplyOperationFilter(submit, typeof(QuizController), "SubmitQuiz");
        Assert.Equal(new[] { "application/json", "application/problem+json" }, submit.Responses["400"].Content.Keys.OrderBy(key => key).ToArray());
    }

    [Fact]
    public void Lookup401DoesNotPromiseAnApplicationErrorBody()
    {
        var operation = OperationFor(typeof(WordsController), "LookupWord");
        ApplyOperationFilter(operation, typeof(WordsController), "LookupWord");
        Assert.Empty(operation.Responses["401"].Content);
        Assert.Contains("empty body", operation.Responses["401"].Description);
    }

    [Theory]
    [InlineData(typeof(UpdateFavoriteRequestDto), "isFavorite")]
    [InlineData(typeof(QuizAnswerSubmissionDto), "selectedOptionId")]
    public void JsonRequiredValueFieldsArePresentAndNonNullable(Type request, string name)
    {
        var schema = new OpenApiSchema { Properties = new Dictionary<string, OpenApiSchema> { [name] = new() { Nullable = true } } };
        new ApiContractSchemaFilter().Apply(schema, new SchemaFilterContext(request, null!, new SchemaRepository()));
        Assert.Contains(name, schema.Required);
        Assert.False(schema.Properties[name].Nullable);
    }

    [Fact]
    public void AnswersStayOptionalButNotNullableWhileNullableResponseKeysStayPresent()
    {
        var request = new OpenApiSchema
        {
            Properties = new Dictionary<string, OpenApiSchema> { ["sessionId"] = new(), ["answers"] = new() { Nullable = true } },
            Required = new HashSet<string> { "answers" }
        };
        new ApiContractSchemaFilter().Apply(request, new SchemaFilterContext(typeof(QuizSubmitRequestDto), null!, new SchemaRepository()));
        Assert.DoesNotContain("answers", request.Required);
        Assert.False(request.Properties["answers"].Nullable);

        var response = new OpenApiSchema
        {
            Properties = new Dictionary<string, OpenApiSchema> { ["selectedAnswer"] = new() { Type = "string", Nullable = true } }
        };
        new ApiContractSchemaFilter().Apply(response, new SchemaFilterContext(typeof(QuizQuestionResultDto), null!, new SchemaRepository()));
        Assert.Contains("selectedAnswer", response.Required);
        Assert.True(response.Properties["selectedAnswer"].Nullable);
    }

    private static OpenApiOperation OperationFor(Type controller, string action)
    {
        var operation = new OpenApiOperation { Responses = new OpenApiResponses() };
        foreach (var attribute in controller.GetMethod(action)!.GetCustomAttributes<ProducesResponseTypeAttribute>())
        {
            operation.Responses[attribute.StatusCode.ToString()] = new OpenApiResponse
            {
                Description = "Declared response",
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = new OpenApiSchema() } }
            };
        }
        return operation;
    }

    private static void ApplyOperationFilter(OpenApiOperation operation, Type controller, string action)
    {
        new ApiContractOperationFilter().Apply(operation,
            new OperationFilterContext(new ApiDescription(), null!, new SchemaRepository(), controller.GetMethod(action)!));
    }
}
