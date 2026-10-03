using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VocabularyApp.WebApi.Controllers;
using VocabularyApp.WebApi.DTOs;

namespace VocabularyApp.WebApi.Helpers;

public static class ApiValidationResponses
{
    public const string Summary = "One or more request fields are invalid.";

    // Only names and annotation messages from our declared contracts are public.
    // Never interpolate attempted values or publish model-binding exceptions.
    private static readonly PropertyInfo[] Properties = typeof(CreateUserRequest).Assembly.GetTypes()
        .Where(t => t.Namespace == typeof(CreateUserRequest).Namespace || t == typeof(ChangePasswordRequest))
        .SelectMany(t => t.GetProperties()).ToArray();
    private static readonly HashSet<string> Fields = new(
        Properties.Select(p => p.Name).Concat(new[] { "request", "term", "page", "pageSize", "take", "userWordId" }),
        StringComparer.OrdinalIgnoreCase);

    public static IActionResult Create(ActionContext context)
    {
        var safeMessages = Properties.SelectMany(p => p.GetCustomAttributes<ValidationAttribute>()
                .Select(a => a.FormatErrorMessage(p.Name)))
            .Concat(Fields.Select(f => $"The {f} field is required."))
            .Append("A non-empty request body is required.")
            .ToHashSet(StringComparer.Ordinal);
        var sanitized = new ModelStateDictionary();
        foreach (var entry in context.ModelState)
        {
            var key = SafeField(entry.Key);
            foreach (var error in entry.Value.Errors)
            {
                var message = error.Exception is null && safeMessages.Contains(error.ErrorMessage)
                    ? error.ErrorMessage : "Invalid value.";
                sanitized.AddModelError(key, message);
            }
        }
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, sanitized, statusCode: 400);
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        problem.Extensions["success"] = false;
        problem.Extensions["data"] = null;
        problem.Extensions["error"] = Summary;
        problem.Extensions["message"] = Summary;
        problem.Extensions["code"] = "validation_failed";
        // JsonResult keeps problem+json even on controllers declaring Produces(application/json).
        return new JsonResult(problem) { StatusCode = 400, ContentType = "application/problem+json" };
    }

    private static string SafeField(string key)
    {
        if (key.Length == 0 || key == "$") return "$";
        var path = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        var segments = path.Split('.');
        return segments.All(segment =>
        {
            var match = Regex.Match(segment, @"^([A-Za-z][A-Za-z0-9]*)(\[[0-9]{1,9}\])?$");
            return match.Success && Fields.Contains(match.Groups[1].Value);
        }) ? key : "$";
    }
}
