using Microsoft.AspNetCore.Mvc;
using VocabularyApp.WebApi.DTOs;
using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Helpers;

public static class ApiErrorResults
{
    public const string InternalMessage = "An internal error occurred. Please try again.";
    public const string DictionaryMessage = "Dictionary service is temporarily unavailable. Please try again.";

    public static ObjectResult Internal(HttpContext context) =>
        Create(context, ServiceFailureType.InternalError, "internal_error");

    public static ObjectResult FromFailure<T>(HttpContext context, ServiceResult<T> result) =>
        result.IsSuccess ? Internal(context) : Create(context, result.FailureType, result.Code);

    // Only explicit category/code pairs authorize a public message. Service messages
    // and exception text are never used to construct the HTTP response.
    public static ObjectResult Create(HttpContext context, ServiceFailureType type, string? code)
    {
        var (status, message) = (type, code) switch
        {
            (ServiceFailureType.Validation, "username_taken") => (400, "Username is already taken"),
            (ServiceFailureType.Validation, "email_taken") => (400, "Email is already registered"),
            (ServiceFailureType.Validation, "invalid_request") => (400, "The request is invalid."),
            (ServiceFailureType.Validation, "canonical_word_required") => (400, "This word is not available. Look it up before adding it to your vocabulary."),
            (ServiceFailureType.Validation, "invalid_preferred_definition") => (400, "Selected definition is not valid for this word."),
            (ServiceFailureType.Validation, "quiz_unavailable") => (400, "Not enough vocabulary is available to create a quiz."),
            (ServiceFailureType.Validation, "invalid_quiz_answers") => (400, "The quiz answers are invalid."),
            (ServiceFailureType.NotFound, "quiz_session_unavailable") => (404, "This quiz session is unavailable. Please start a new quiz."),
            (ServiceFailureType.Conflict, "quiz_submission_conflict") => (409, "This quiz submission is already being processed or has been submitted."),
            (ServiceFailureType.Conflict, "quiz_vocabulary_changed") => (409, "Your vocabulary changed. Please start a new quiz."),
            (ServiceFailureType.Unauthorized, "invalid_credentials") => (401, "Invalid username or password"),
            (ServiceFailureType.Unauthorized, "invalid_token") => (401, "Invalid token"),
            (ServiceFailureType.Unauthorized, "user_unavailable") => (401, "Your account is unavailable. Please sign in again."),
            (ServiceFailureType.Unauthorized, "current_password_incorrect") => (401, "Current password is incorrect"),
            (ServiceFailureType.NotFound, "user_not_found") => (404, "User not found"),
            (ServiceFailureType.NotFound, "word_not_found") => (404, "No definitions found."),
            (ServiceFailureType.NotFound, "vocabulary_not_found") => (404, "Word not found in your vocabulary."),
            (ServiceFailureType.Conflict, "credentials_changed") => (409, "Credentials changed. Please sign in again."),
            (ServiceFailureType.ServiceUnavailable, "dictionary_unavailable") => (503, DictionaryMessage),
            _ => (500, InternalMessage)
        };
        return new ObjectResult(new ApiErrorResponse
        {
            Error = message,
            Code = status == 500 ? "internal_error" : code!,
            TraceId = context.TraceIdentifier
        }) { StatusCode = status };
    }
}
