namespace VocabularyApp.WebApi.Models;

/// <summary>Scheduling statuses; due eligibility is calculated separately.</summary>
public enum ReviewStatus
{
    New = 0,
    Learning = 1,
    Reviewing = 2
}

/// <summary>Immutable scheduler input/output, independent of persistence and transport.</summary>
public sealed record ReviewScheduleState(
    DateTime EnrollmentAt,
    DateTime NextReviewAt,
    DateTime? LastReviewedAt,
    int ReviewIntervalDays,
    int ConsecutiveCorrectCount,
    int LapseCount,
    ReviewStatus ReviewStatus,
    long ScheduleVersion,
    string AlgorithmVersion);

/// <summary>A genuine binary review and its complete before/after scheduling state.</summary>
public sealed record ReviewTransition(
    ReviewScheduleState PreviousState,
    bool IsCorrect,
    DateTime AcceptedReviewAtUtc,
    ReviewScheduleState NextState);
