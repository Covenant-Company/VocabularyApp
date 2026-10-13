using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Services;

/// <summary>Pure interval-v1 rules. Callers supply validated server instants, never client clocks.</summary>
public sealed class IntervalV1ReviewScheduler : IReviewScheduler
{
    public const string AlgorithmVersion = "interval-v1";
    private static readonly int[] Intervals = [1, 3, 7, 14, 30, 60, 90];

    public ReviewScheduleState Enroll(DateTime enrollmentAtUtc)
    {
        RequireUtc(enrollmentAtUtc, nameof(enrollmentAtUtc));
        return new(enrollmentAtUtc, enrollmentAtUtc, null, 0, 0, 0, ReviewStatus.New, 1, AlgorithmVersion);
    }

    /// <summary>Call only for a genuine meaning/content reset; same-meaning writes retain the state.</summary>
    public ReviewScheduleState Reset(ReviewScheduleState state, DateTime resetAtUtc)
    {
        Validate(state);
        RequireUtc(resetAtUtc, nameof(resetAtUtc));
        RequireNotBeforePreviousReview(state, resetAtUtc, nameof(resetAtUtc));
        // A New state may already have been reset; do not move that epoch's due time backwards.
        if (state.ReviewStatus == ReviewStatus.New && resetAtUtc < state.NextReviewAt)
            throw new ArgumentOutOfRangeException(nameof(resetAtUtc), "Reset time cannot precede the current meaning epoch.");

        return state with
        {
            NextReviewAt = resetAtUtc,
            LastReviewedAt = null,
            ReviewIntervalDays = 0,
            ConsecutiveCorrectCount = 0,
            ReviewStatus = ReviewStatus.New,
            ScheduleVersion = checked(state.ScheduleVersion + 1)
        };
    }

    public ReviewTransition Review(ReviewScheduleState state, bool isCorrect, DateTime acceptedReviewAtUtc)
    {
        Validate(state);
        RequireUtc(acceptedReviewAtUtc, nameof(acceptedReviewAtUtc));
        RequireNotBeforePreviousReview(state, acceptedReviewAtUtc, nameof(acceptedReviewAtUtc));
        if (acceptedReviewAtUtc < state.NextReviewAt)
            throw new ArgumentOutOfRangeException(nameof(acceptedReviewAtUtc), "The review is not yet due.");

        var count = isCorrect ? Math.Min(state.ConsecutiveCorrectCount + 1, Intervals.Length) : 0;
        var interval = isCorrect ? Intervals[count - 1] : 1;
        var next = state with
        {
            NextReviewAt = acceptedReviewAtUtc.AddDays(interval),
            LastReviewedAt = acceptedReviewAtUtc,
            ReviewIntervalDays = interval,
            ConsecutiveCorrectCount = count,
            LapseCount = isCorrect ? state.LapseCount : checked(state.LapseCount + 1),
            ReviewStatus = count >= 3 ? ReviewStatus.Reviewing : ReviewStatus.Learning,
            ScheduleVersion = checked(state.ScheduleVersion + 1)
        };
        return new(state, isCorrect, acceptedReviewAtUtc, next);
    }

    public bool IsDue(ReviewScheduleState state, DateTime asOfUtc)
    {
        Validate(state);
        RequireUtc(asOfUtc, nameof(asOfUtc));
        return state.NextReviewAt <= asOfUtc;
    }

    private static void Validate(ReviewScheduleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        RequireUtc(state.EnrollmentAt, nameof(state));
        RequireUtc(state.NextReviewAt, nameof(state));
        if (state.LastReviewedAt is { } last) RequireUtc(last, nameof(state));
        if (state.AlgorithmVersion != AlgorithmVersion || state.ScheduleVersion < 1 ||
            state.LapseCount < 0 || state.ConsecutiveCorrectCount is < 0 or > 7 ||
            !Enum.IsDefined(state.ReviewStatus) || state.NextReviewAt < state.EnrollmentAt)
            throw new ArgumentException("Invalid interval-v1 scheduling state.", nameof(state));

        if (state.ReviewStatus == ReviewStatus.New)
        {
            if (state.ConsecutiveCorrectCount != 0 || state.ReviewIntervalDays != 0 || state.LastReviewedAt != null)
                throw new ArgumentException("New state must have no current-epoch reviews or progression.", nameof(state));
            return;
        }

        var count = state.ConsecutiveCorrectCount;
        var expectedInterval = count == 0 ? 1 : Intervals[count - 1];
        var expectedStatus = count >= 3 ? ReviewStatus.Reviewing : ReviewStatus.Learning;
        if (state.LastReviewedAt is not { } reviewedAt || reviewedAt < state.EnrollmentAt ||
            state.ReviewIntervalDays != expectedInterval || state.ReviewStatus != expectedStatus ||
            (count == 0 && state.LapseCount == 0))
            throw new ArgumentException("Review progression, status and timestamps are inconsistent.", nameof(state));

        if (reviewedAt > DateTime.MaxValue.AddDays(-expectedInterval) ||
            state.NextReviewAt != reviewedAt.AddDays(expectedInterval))
            throw new ArgumentException("The due time must match the last accepted review and interval.", nameof(state));
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("An explicit UTC timestamp is required.", parameterName);
    }

    private static void RequireNotBeforePreviousReview(ReviewScheduleState state, DateTime value, string parameterName)
    {
        if (value < state.EnrollmentAt || (state.LastReviewedAt is { } last && value < last))
            throw new ArgumentOutOfRangeException(parameterName, "The timestamp precedes enrollment or the last accepted review.");
    }
}
