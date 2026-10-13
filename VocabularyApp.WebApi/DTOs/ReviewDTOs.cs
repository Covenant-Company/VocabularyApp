using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VocabularyApp.WebApi.DTOs;

// Phase 1 wire contracts only. No review endpoint is implemented yet.
public class ReviewDueQueryDto
{
    [Range(1, 100)]
    public int Take { get; set; } = 20;

    [StringLength(2048)]
    public string? Cursor { get; set; }
}

public class ReviewDueItemDto
{
    public int UserWordId { get; set; }
    public string Word { get; set; } = string.Empty;
    [JsonConverter(typeof(ReviewUtcDateTimeConverter))]
    public DateTime NextReviewAt { get; set; }
    [RegularExpression("^(new|learning|reviewing)$")]
    public string ReviewStatus { get; set; } = "new";
    public bool IsUsable { get; set; }
}

public class ReviewDueResponseDto
{
    public List<ReviewDueItemDto> Items { get; set; } = new();
    public string? NextCursor { get; set; }
    [JsonConverter(typeof(ReviewUtcDateTimeConverter))]
    public DateTime AsOfUtc { get; set; }
}

public class ReviewSummaryResponseDto
{
    public int DueCount { get; set; }
    public int NewCount { get; set; }
    public int OverdueCount { get; set; }
    public int BlockedCount { get; set; }
    public int UsableDueCount { get; set; }
    public int TotalEnrolledCount { get; set; }
    [JsonConverter(typeof(ReviewUtcDateTimeConverter))]
    public DateTime? NextReviewAt { get; set; }
    [JsonConverter(typeof(ReviewUtcDateTimeConverter))]
    public DateTime AsOfUtc { get; set; }
}

public class ReviewStartRequestDto
{
    [Range(1, 20)]
    public int QuestionCount { get; set; } = 10;
    [NonEmptyReviewGuid]
    public Guid? ResumeSessionId { get; set; }
}

public class ReviewOptionDto
{
    [Range(0, 3)]
    public int OptionId { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class ReviewItemDto
{
    public Guid ItemId { get; set; }
    public int UserWordId { get; set; }
    [RegularExpression("^word-to-definition$")]
    public string QuestionType { get; set; } = "word-to-definition";
    public string Prompt { get; set; } = string.Empty;
    public List<ReviewOptionDto> Options { get; set; } = new();
    public ReviewSubmitResponseDto? AcceptedReceipt { get; set; }
}

public class ReviewStartResponseDto
{
    [RegularExpression("^(ready|empty|unavailable)$")]
    public string Availability { get; set; } = "empty";
    [RegularExpression("^(insufficient_usable_vocabulary|ambiguous_options|no_usable_due_words)$")]
    public string? Reason { get; set; }
    public Guid? SessionId { get; set; }
    [JsonConverter(typeof(ReviewUtcDateTimeConverter))]
    public DateTime? ExpiresAtUtc { get; set; }
    public List<ReviewItemDto> Items { get; set; } = new();
    public ReviewSessionSummaryDto? SessionSummary { get; set; }
}

public class ReviewSubmitRequestDto
{
    [JsonRequired, NonEmptyReviewGuid]
    public Guid SessionId { get; set; }
    [JsonRequired, NonEmptyReviewGuid]
    public Guid ItemId { get; set; }
    [JsonRequired, NonEmptyReviewGuid]
    public Guid SubmissionId { get; set; }
    [JsonRequired, Range(0, 3)]
    public int SelectedOptionId { get; set; }
}

public class ReviewSessionSummaryDto
{
    public int TotalItems { get; set; }
    public int AcceptedCount { get; set; }
    public int CorrectCount { get; set; }
    public int IncorrectCount { get; set; }
    public int PendingCount { get; set; }
    public bool IsComplete { get; set; }
}

public class ReviewSubmitResponseDto
{
    public Guid SessionId { get; set; }
    public Guid ItemId { get; set; }
    public Guid SubmissionId { get; set; }
    [JsonConverter(typeof(ReviewUtcDateTimeConverter))]
    public DateTime ReviewedAtUtc { get; set; }
    public bool IsCorrect { get; set; }
    public int SelectedOptionId { get; set; }
    public int CorrectOptionId { get; set; }
    public string SelectedAnswer { get; set; } = string.Empty;
    public string CorrectAnswer { get; set; } = string.Empty;
    [JsonConverter(typeof(ReviewUtcDateTimeConverter))]
    public DateTime NextReviewAt { get; set; }
    public int ReviewIntervalDays { get; set; }
    [RegularExpression("^(learning|reviewing)$")]
    public string ReviewStatus { get; set; } = "learning";
    public ReviewSessionSummaryDto SessionSummary { get; set; } = new();
}

/// <summary>Optional GUIDs may be null; any supplied GUID must be nonempty.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NonEmptyReviewGuidAttribute : ValidationAttribute
{
    public NonEmptyReviewGuidAttribute() : base("The {0} field must be a non-empty GUID.") { }

    public override bool IsValid(object? value) => value is null || value is Guid guid && guid != Guid.Empty;
}

/// <summary>Scoped to review DTOs; does not change any existing R7 timestamp serialization.</summary>
public sealed class ReviewUtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetDateTime(out var value) ||
            value.Kind != DateTimeKind.Utc)
            throw new JsonException("Review timestamps must be ISO UTC timestamps ending in Z.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new JsonException("Review timestamps must have UTC kind.");
        writer.WriteStringValue(value);
    }
}
