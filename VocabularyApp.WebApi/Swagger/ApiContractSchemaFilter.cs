using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Swagger;

/// <summary>Preserves wire presence, nullable values and compatibility defaults in Swagger.</summary>
public sealed class ApiContractSchemaFilter : ISchemaFilter
{
    private static readonly HashSet<Type> Requests = new()
    {
        typeof(CreateUserRequest), typeof(LoginRequest), typeof(UpdateFavoriteRequestDto),
        typeof(UpdatePreferredDefinitionRequestDto), typeof(StartQuizRequestDto),
        typeof(QuizSubmitRequestDto), typeof(QuizAnswerSubmissionDto)
    };

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        // Reference-extension schemas inherit their component's contract metadata.
        if (schema.Properties.Count == 0) return;

        // All properties of these response DTOs are serialized, including explicit nulls.
        // Required means present; Nullable independently describes allowed values.
        if (context.Type.Namespace == typeof(UserDto).Namespace && !Requests.Contains(context.Type))
        {
            foreach (var name in schema.Properties.Keys) schema.Required.Add(name);
        }

        foreach (var property in context.Type.GetProperties())
        {
            var name = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!schema.Properties.TryGetValue(name, out var propertySchema)) continue;
            if (property.IsDefined(typeof(JsonRequiredAttribute)))
            {
                schema.Required.Add(name);
                propertySchema.Nullable = false;
            }
        }

        if (context.Type == typeof(AddWordRequest))
        {
            foreach (var name in new[] { "definition", "example", "pronunciation" })
            {
                schema.Properties[name].Deprecated = true;
                schema.Properties[name].Description = "Accepted for compatibility and ignored. Cannot author canonical dictionary content.";
            }
            schema.Properties["word"].Description = "Must identify an existing canonical database word exactly. Missing/blank values are business-rejected; lookup precedes add.";
            schema.Properties["partOfSpeech"].Description = "Retained selection fallback when preferredWordDefinitionId is absent; does not define identity or author canonical content.";
            schema.Properties["preferredWordDefinitionId"].Description = "When supplied on a new add, must belong to the canonical word. Existing (UserId, WordId) rows are returned successfully before selection checks.";
        }
        if (context.Type == typeof(StartQuizRequestDto))
        {
            schema.Properties["questionCount"].Default = new OpenApiInteger(10);
            schema.Properties["questionCount"].Description = "Nonpositive values normalize to 10, then clamp to 1..20. Actual question count is limited by usable vocabulary.";
            schema.Properties["mode"].Default = new OpenApiString("mixed");
            schema.Properties["mode"].Description = "mixed, word-to-definition or definition-to-word; unsupported text normalizes to mixed.";
        }
        if (context.Type == typeof(QuizSubmitRequestDto))
        {
            schema.Required.Remove("answers");
            schema.Properties["answers"].Nullable = false;
            schema.Properties["answers"].Default = new OpenApiArray();
            schema.Properties["answers"].Description = "Omission/[] means all questions unanswered. Explicit null, null elements, duplicate/foreign question IDs or invalid options are rejected. Each supplied answer must include selectedOptionId.";
        }
        if (context.Type == typeof(QuizAnswerSubmissionDto))
        {
            schema.Properties["selectedOptionId"].Minimum = 0;
            schema.Properties["selectedOptionId"].Maximum = 3;
        }
        if (context.Type == typeof(UpdateFavoriteRequestDto))
            schema.Properties["isFavorite"].Description = "Explicit presence required. false is a valid update; omission/null/type mismatch yields validation_failed.";
        if (context.Type == typeof(ApiErrorResponse))
        {
            schema.Properties["success"].Enum = new List<IOpenApiAny> { new OpenApiBoolean(false) };
            schema.Properties["data"].Nullable = true;
            schema.Properties["data"].Enum = new List<IOpenApiAny> { new OpenApiNull() };
            schema.Properties["message"].Description = "Same safe public text as error.";
            schema.Properties["traceId"].Description = "Server HttpContext.TraceIdentifier; caller trace headers are not echoed.";
        }
    }
}
