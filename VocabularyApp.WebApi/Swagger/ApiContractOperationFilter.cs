using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using VocabularyApp.WebApi.DTOs;

namespace VocabularyApp.WebApi.Swagger;

/// <summary>Describes the existing R7 contracts without changing HTTP behavior.</summary>
public sealed class ApiContractOperationFilter : IOperationFilter
{
    private static readonly HashSet<string> ValidationOnly = new(StringComparer.Ordinal)
    {
        "Login", "ChangePassword", "GetUserVocabulary", "SetFavorite", "GetQuizHistory"
    };

    private static readonly Dictionary<string, string> ActionErrors = new(StringComparer.Ordinal)
    {
        ["Register"] = "400: username_taken, email_taken; 500: internal_error.",
        ["Login"] = "401: invalid_credentials; 409: credentials_changed; 500: internal_error.",
        ["GetProfile"] = "401: invalid_token; 404: user_not_found; 500: internal_error.",
        ["ChangePassword"] = "401: invalid_token, user_unavailable, current_password_incorrect; 409: credentials_changed; 500: internal_error.",
        ["ValidateToken"] = "401: invalid_token, user_unavailable; 500: internal_error.",
        ["LookupWord"] = "400: invalid_request; 404: word_not_found; 503: dictionary_unavailable; 500: internal_error.",
        ["AddToVocabulary"] = "400: invalid_request, canonical_word_required, invalid_preferred_definition; 401: invalid_token; 500: internal_error.",
        ["GetUserVocabulary"] = "401: invalid_token; 500: internal_error.",
        ["SearchUserVocabulary"] = "401: invalid_token; 500: internal_error.",
        ["SetFavorite"] = "401: invalid_token; 404: vocabulary_not_found; 500: internal_error.",
        ["SetPreferredDefinition"] = "400: invalid_preferred_definition; 401: invalid_token; 404: vocabulary_not_found; 500: internal_error.",
        ["StartQuiz"] = "400: quiz_unavailable; 401: invalid_token; 500: internal_error.",
        ["SubmitQuiz"] = "400: invalid_request, invalid_quiz_answers; 401: invalid_token; 404: quiz_session_unavailable; 409: quiz_submission_conflict, quiz_vocabulary_changed; 500: internal_error.",
        ["GetQuizHistory"] = "401: invalid_token; 500: internal_error."
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var action = context.MethodInfo.Name;
        if (!ActionErrors.TryGetValue(action, out var codes)) return;

        var anonymous = context.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute), true)
            || context.MethodInfo.DeclaringType!.IsDefined(typeof(AllowAnonymousAttribute), true);
        operation.Security = new List<OpenApiSecurityRequirement>();
        if (anonymous)
        {
            // This serializer omits an empty list. An empty requirement is the
            // OpenAPI anonymous alternative and overrides the document's Bearer rule.
            operation.Security.Add(new OpenApiSecurityRequirement());
        }
        else
        {
            // Includes lookup, which is protected by the existing fallback policy.
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                }] = Array.Empty<string>()
            });
        }

        operation.Description = (operation.Description ?? "") + " Stable application errors: " + codes;
        foreach (var response in operation.Responses.Where(pair => pair.Key != "200"))
        {
            response.Value.Description += " Application-classified failures use the six-field ApiErrorResponse (application/json).";
        }

        if (operation.Responses.TryGetValue("400", out var badRequest))
        {
            if (ValidationOnly.Contains(action)) badRequest.Content.Clear();
            badRequest.Content["application/problem+json"] = new OpenApiMediaType
            {
                Schema = ValidationSchema()
            };
            badRequest.Description = ValidationOnly.Contains(action)
                ? "Binding/validation failure: extended ValidationProblemDetails, code validation_failed."
                : "Either application/json ApiErrorResponse for business rejection, or application/problem+json extended ValidationProblemDetails for binding/validation (validation_failed).";
        }

        if (!anonymous && operation.Responses.TryGetValue("401", out var unauthorized))
        {
            unauthorized.Description = action == "LookupWord"
                ? "Framework Bearer challenge: empty body; no application envelope."
                : "Framework Bearer challenge may have an empty body. If the action rejects an authenticated principal, application/json ApiErrorResponse is returned; see the documented codes.";
            if (action == "LookupWord") unauthorized.Content.Clear();
        }
    }

    private static OpenApiSchema ValidationSchema()
    {
        var schema = new OpenApiSchema
        {
            Type = "object",
            Description = "ValidationProblemDetails with sanitized field errors and R7 compatibility extensions. Optional standard ProblemDetails members may also occur.",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["type"] = new() { Type = "string" },
                ["title"] = new() { Type = "string" },
                ["status"] = new() { Type = "integer", Enum = new List<IOpenApiAny> { new OpenApiInteger(400) } },
                ["detail"] = new() { Type = "string", Nullable = true },
                ["instance"] = new() { Type = "string", Nullable = true },
                ["errors"] = new()
                {
                    Type = "object", AdditionalPropertiesAllowed = true,
                    AdditionalProperties = new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "string" } }
                },
                ["traceId"] = new() { Type = "string" },
                ["success"] = new() { Type = "boolean", Enum = new List<IOpenApiAny> { new OpenApiBoolean(false) } },
                ["data"] = new() { Type = "object", Nullable = true, Enum = new List<IOpenApiAny> { new OpenApiNull() } },
                ["error"] = new() { Type = "string" },
                ["message"] = new() { Type = "string" },
                ["code"] = new() { Type = "string", Enum = new List<IOpenApiAny> { new OpenApiString("validation_failed") } }
            },
            Required = new HashSet<string> { "type", "title", "status", "errors", "traceId", "success", "data", "error", "message", "code" }
        };
        return schema;
    }
}
