using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.DTOs;

namespace VocabularyApp.WebApi.Tests.Services;

public sealed class ReviewDtoContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTime Now = new(2026, 10, 12, 12, 30, 0, DateTimeKind.Utc);
    private const string Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryOptionIncludingZeroIsPresentAndValid(int option)
    {
        var dto = JsonSerializer.Deserialize<ReviewSubmitRequestDto>(SubmitJson(option.ToString()), JsonOptions)!;
        Assert.Equal(option, dto.SelectedOptionId);
        Assert.Empty(Validate(dto));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonOptions));
        Assert.Equal(option, json.RootElement.GetProperty("selectedOptionId").GetInt32());
        AssertProperties(json.RootElement, "sessionId", "itemId", "submissionId", "selectedOptionId");
    }

    [Theory]
    [InlineData("sessionId")]
    [InlineData("itemId")]
    [InlineData("submissionId")]
    [InlineData("selectedOptionId")]
    public void OmittedSubmitMembersAreRejectedByJsonRequired(string member)
    {
        var values = new Dictionary<string, object>
        {
            ["sessionId"] = Id, ["itemId"] = Id, ["submissionId"] = Id, ["selectedOptionId"] = 0
        };
        values.Remove(member);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReviewSubmitRequestDto>(JsonSerializer.Serialize(values), JsonOptions));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"zero\"")]
    [InlineData("true")]
    [InlineData("0.5")]
    public void NullOrTypeInvalidOptionsAreRejectedDuringBinding(string value)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReviewSubmitRequestDto>(SubmitJson(value), JsonOptions));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void OutOfRangeOptionIsAnAnnotationFailure(int option)
    {
        var dto = JsonSerializer.Deserialize<ReviewSubmitRequestDto>(SubmitJson(option.ToString()), JsonOptions)!;
        Assert.Contains(Validate(dto), error => error.MemberNames.Contains("SelectedOptionId"));
    }

    [Theory]
    [InlineData("sessionId", "SessionId")]
    [InlineData("itemId", "ItemId")]
    [InlineData("submissionId", "SubmissionId")]
    public void EmptyRequiredGuidIsRejectedWithoutTreatingOtherGuidsAsInvalid(string jsonName, string memberName)
    {
        var json = SubmitJson("0").Replace($"\"{jsonName}\":\"{Id}\"", $"\"{jsonName}\":\"{Guid.Empty}\"");
        var dto = JsonSerializer.Deserialize<ReviewSubmitRequestDto>(json, JsonOptions)!;
        Assert.Equal(memberName, Assert.Single(Assert.Single(Validate(dto)).MemberNames));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"invalid-guid\"")]
    [InlineData("123")]
    public void MalformedRequiredGuidIsABindingFailure(string value)
    {
        var json = SubmitJson("0").Replace($"\"sessionId\":\"{Id}\"", $"\"sessionId\":{value}");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReviewSubmitRequestDto>(json, JsonOptions));
    }

    [Fact]
    public void LaunchDefaultsAndOptionalResumeMatchTheContract()
    {
        var start = JsonSerializer.Deserialize<ReviewStartRequestDto>("{}", JsonOptions)!;
        Assert.Equal(10, start.QuestionCount);
        Assert.Null(start.ResumeSessionId);
        Assert.Empty(Validate(start));
        start.ResumeSessionId = Guid.Empty;
        Assert.Single(Validate(start));
        start.ResumeSessionId = Guid.Parse(Id);
        Assert.Empty(Validate(start));
        var due = new ReviewDueQueryDto();
        Assert.Equal(20, due.Take);
        Assert.Null(due.Cursor);
        Assert.Empty(Validate(due));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(0, false)]
    [InlineData(21, false)]
    public void ReviewQuestionCountRejectsRatherThanClampsInvalidBounds(int count, bool valid)
    {
        Assert.Equal(valid, Validate(new ReviewStartRequestDto { QuestionCount = count }).Count == 0);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    public void DuePageBoundsAreValidated(int take, bool valid)
    {
        Assert.Equal(valid, Validate(new ReviewDueQueryDto { Take = take }).Count == 0);
    }

    [Fact]
    public void CursorLengthIsBounded()
    {
        Assert.Empty(Validate(new ReviewDueQueryDto { Cursor = new string('a', 2048) }));
        Assert.Single(Validate(new ReviewDueQueryDto { Cursor = new string('a', 2049) }));
    }

    [Fact]
    public void EmptyAvailabilityRetainsAllNullableFieldsAndSuccessEnvelopeShape()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new SuccessResponse<ReviewStartResponseDto>
        {
            Data = new ReviewStartResponseDto()
        }, JsonOptions));
        AssertProperties(document.RootElement, "success", "data");
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        AssertProperties(data, "availability", "reason", "sessionId", "expiresAtUtc", "items", "sessionSummary");
        Assert.Equal("empty", data.GetProperty("availability").GetString());
        foreach (var field in new[] { "reason", "sessionId", "expiresAtUtc", "sessionSummary" })
            Assert.Equal(JsonValueKind.Null, data.GetProperty(field).ValueKind);
        Assert.Empty(data.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void PendingItemsDoNotExposeAnswerKeysAndAcceptedReceiptRoundTrips()
    {
        var item = new ReviewItemDto
        {
            ItemId = Guid.Parse(Id), UserWordId = 10, Prompt = "Choose the correct definition.",
            Options = Enumerable.Range(0, 4).Select(i => new ReviewOptionDto { OptionId = i, Text = $"Meaning {i}" }).ToList()
        };
        using var pending = JsonDocument.Parse(JsonSerializer.Serialize(item, JsonOptions));
        AssertProperties(pending.RootElement, "itemId", "userWordId", "questionType", "prompt", "options", "acceptedReceipt");
        Assert.Equal(JsonValueKind.Null, pending.RootElement.GetProperty("acceptedReceipt").ValueKind);
        Assert.False(pending.RootElement.TryGetProperty("correctOptionId", out _));
        foreach (var option in pending.RootElement.GetProperty("options").EnumerateArray())
            AssertProperties(option, "optionId", "text");

        item.AcceptedReceipt = Receipt();
        var restored = JsonSerializer.Deserialize<ReviewItemDto>(JsonSerializer.Serialize(item, JsonOptions), JsonOptions)!;
        Assert.NotNull(restored.AcceptedReceipt);
        Assert.Equal(0, restored.AcceptedReceipt.CorrectOptionId);
        Assert.Equal(Now, restored.AcceptedReceipt.ReviewedAtUtc);
        Assert.Equal(DateTimeKind.Utc, restored.AcceptedReceipt.NextReviewAt.Kind);
        Assert.Equal(1, restored.AcceptedReceipt.SessionSummary.AcceptedCount);
    }

    [Fact]
    public void DueSummaryAndReceiptHaveExactWirePropertiesAndUtcInstants()
    {
        AssertShape(new ReviewDueQueryDto(), "take", "cursor");
        AssertShape(new ReviewStartRequestDto(), "questionCount", "resumeSessionId");
        AssertShape(new ReviewDueItemDto { NextReviewAt = Now }, "userWordId", "word", "nextReviewAt", "reviewStatus", "isUsable");
        AssertShape(new ReviewDueResponseDto { AsOfUtc = Now }, "items", "nextCursor", "asOfUtc");
        var summary = new ReviewSummaryResponseDto { AsOfUtc = Now, NextReviewAt = Now.AddDays(1) };
        AssertShape(summary, "dueCount", "newCount", "overdueCount", "blockedCount", "usableDueCount", "totalEnrolledCount", "nextReviewAt", "asOfUtc");
        AssertShape(new ReviewSessionSummaryDto(), "totalItems", "acceptedCount", "correctCount", "incorrectCount", "pendingCount", "isComplete");
        var receipt = Receipt();
        AssertShape(receipt, "sessionId", "itemId", "submissionId", "reviewedAtUtc", "isCorrect", "selectedOptionId", "correctOptionId", "selectedAnswer", "correctAnswer", "nextReviewAt", "reviewIntervalDays", "reviewStatus", "sessionSummary");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(receipt, JsonOptions));
        Assert.EndsWith("Z", json.RootElement.GetProperty("reviewedAtUtc").GetString());
        Assert.EndsWith("Z", json.RootElement.GetProperty("nextReviewAt").GetString());
        Assert.DoesNotContain("scheduleVersion", json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(DateTimeKind.Utc, JsonSerializer.Deserialize<ReviewSummaryResponseDto>(JsonSerializer.Serialize(summary, JsonOptions), JsonOptions)!.NextReviewAt!.Value.Kind);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ReviewDtosRejectNonUtcOutputInsteadOfSilentlyRelabelingIt(DateTimeKind kind)
    {
        var invalid = DateTime.SpecifyKind(Now, kind);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new ReviewDueItemDto { NextReviewAt = invalid }, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new ReviewStartResponseDto { ExpiresAtUtc = invalid }, JsonOptions));
    }

    [Theory]
    [InlineData("2026-10-12T12:30:00")]
    [InlineData("2026-10-12T12:30:00-04:00")]
    [InlineData("not-a-date")]
    public void ReviewDtosRejectNonUtcInput(string timestamp)
    {
        var json = $"{{\"asOfUtc\":\"{timestamp}\"}}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReviewDueResponseDto>(json, JsonOptions));
    }

    [Fact]
    public void AllowedWireLiteralsAreDistinctFromUnimplementedModesAndStatuses()
    {
        foreach (var status in new[] { "new", "learning", "reviewing" })
            Assert.Empty(Validate(new ReviewDueItemDto { ReviewStatus = status }));
        foreach (var status in new[] { "learning", "reviewing" })
            Assert.Empty(Validate(new ReviewSubmitResponseDto { ReviewStatus = status }));
        Assert.NotEmpty(Validate(new ReviewDueItemDto { ReviewStatus = "suspended" }));
        Assert.NotEmpty(Validate(new ReviewSubmitResponseDto { ReviewStatus = "new" }));
        Assert.NotEmpty(Validate(new ReviewItemDto { QuestionType = "self-rating" }));
        foreach (var availability in new[] { "ready", "empty", "unavailable" })
            Assert.Empty(Validate(new ReviewStartResponseDto { Availability = availability }));
        foreach (var reason in new[] { "insufficient_usable_vocabulary", "ambiguous_options", "no_usable_due_words" })
            Assert.Empty(Validate(new ReviewStartResponseDto { Availability = "unavailable", Reason = reason }));
        Assert.NotEmpty(Validate(new ReviewStartResponseDto { Availability = "unknown" }));
        Assert.NotEmpty(Validate(new ReviewStartResponseDto { Reason = "typed_recall" }));
    }

    [Fact]
    public void ReviewFeatureSettingsAreDisabledByDefault()
    {
        var options = new ReviewOptions();
        Assert.Equal("Reviews", ReviewOptions.SectionName);
        Assert.False(options.EnrollmentEnabled);
        Assert.False(options.Enabled);
    }

    private static ReviewSubmitResponseDto Receipt() => new()
    {
        SessionId = Guid.Parse(Id), ItemId = Guid.Parse(Id), SubmissionId = Guid.Parse(Id),
        ReviewedAtUtc = Now, IsCorrect = true, SelectedOptionId = 0, CorrectOptionId = 0,
        SelectedAnswer = "Meaning", CorrectAnswer = "Meaning", NextReviewAt = Now.AddDays(1),
        ReviewIntervalDays = 1, ReviewStatus = "learning",
        SessionSummary = new ReviewSessionSummaryDto { TotalItems = 2, AcceptedCount = 1, CorrectCount = 1, PendingCount = 1 }
    };

    private static string SubmitJson(string option) =>
        $"{{\"sessionId\":\"{Id}\",\"itemId\":\"{Id}\",\"submissionId\":\"{Id}\",\"selectedOptionId\":{option}}}";

    private static List<ValidationResult> Validate(object dto)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), errors, validateAllProperties: true);
        return errors;
    }

    private static void AssertShape<T>(T dto, params string[] properties)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonOptions));
        AssertProperties(json.RootElement, properties);
    }

    private static void AssertProperties(JsonElement element, params string[] properties) =>
        Assert.Equal(properties.OrderBy(p => p), element.EnumerateObject().Select(p => p.Name).OrderBy(p => p));
}
