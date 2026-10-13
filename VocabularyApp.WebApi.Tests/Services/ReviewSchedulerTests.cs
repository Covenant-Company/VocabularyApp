using VocabularyApp.WebApi.Models;
using VocabularyApp.WebApi.Services;

namespace VocabularyApp.WebApi.Tests.Services;

public sealed class ReviewSchedulerTests
{
    private readonly IReviewScheduler _scheduler = new IntervalV1ReviewScheduler();
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EnrollmentIsNewAndImmediatelyDueWithoutHistoricalReviews()
    {
        var state = _scheduler.Enroll(Start);
        Assert.Equal(Start, state.EnrollmentAt);
        Assert.Equal(Start, state.NextReviewAt);
        Assert.Null(state.LastReviewedAt);
        Assert.Equal(0, state.ReviewIntervalDays);
        Assert.Equal(0, state.ConsecutiveCorrectCount);
        Assert.Equal(0, state.LapseCount);
        Assert.Equal(ReviewStatus.New, state.ReviewStatus);
        Assert.Equal(1, state.ScheduleVersion);
        Assert.Equal("interval-v1", state.AlgorithmVersion);
        Assert.True(_scheduler.IsDue(state, Start));
    }

    [Theory]
    [InlineData(1, 1, ReviewStatus.Learning)]
    [InlineData(2, 3, ReviewStatus.Learning)]
    [InlineData(3, 7, ReviewStatus.Reviewing)]
    [InlineData(4, 14, ReviewStatus.Reviewing)]
    [InlineData(5, 30, ReviewStatus.Reviewing)]
    [InlineData(6, 60, ReviewStatus.Reviewing)]
    [InlineData(7, 90, ReviewStatus.Reviewing)]
    [InlineData(8, 90, ReviewStatus.Reviewing)]
    [InlineData(12, 90, ReviewStatus.Reviewing)]
    public void EveryCorrectTransitionUsesTheApprovedLadder(int successes, int days, ReviewStatus status)
    {
        var state = AfterSuccesses(successes - 1);
        var transition = _scheduler.Review(state, true, state.NextReviewAt);
        Assert.Same(state, transition.PreviousState);
        Assert.True(transition.IsCorrect);
        Assert.Equal(state.NextReviewAt, transition.AcceptedReviewAtUtc);
        Assert.Equal(state.NextReviewAt.AddDays(days), transition.NextState.NextReviewAt);
        Assert.Equal(state.NextReviewAt, transition.NextState.LastReviewedAt);
        Assert.Equal(Math.Min(successes, 7), transition.NextState.ConsecutiveCorrectCount);
        Assert.Equal(days, transition.NextState.ReviewIntervalDays);
        Assert.Equal(status, transition.NextState.ReviewStatus);
        Assert.Equal(state.ScheduleVersion + 1, transition.NextState.ScheduleVersion);
        Assert.Equal(state.AlgorithmVersion, transition.NextState.AlgorithmVersion);
        Assert.Equal(state.EnrollmentAt, transition.NextState.EnrollmentAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void IncorrectAtEveryStepResetsProgressAndIncrementsLifetimeLapses(int successes)
    {
        var state = AfterSuccesses(successes) with { LapseCount = 5 };
        var result = _scheduler.Review(state, false, state.NextReviewAt);
        Assert.False(result.IsCorrect);
        Assert.Equal(0, result.NextState.ConsecutiveCorrectCount);
        Assert.Equal(1, result.NextState.ReviewIntervalDays);
        Assert.Equal(ReviewStatus.Learning, result.NextState.ReviewStatus);
        Assert.Equal(6, result.NextState.LapseCount);
        Assert.Equal(state.NextReviewAt.AddHours(24), result.NextState.NextReviewAt);
        Assert.Equal(state.ScheduleVersion + 1, result.NextState.ScheduleVersion);
        Assert.Equal(5, state.LapseCount); // Input remains immutable.
    }

    [Fact]
    public void RepeatedFailuresAndTheNextCorrectAnswerEachScheduleOneDay()
    {
        var first = _scheduler.Review(_scheduler.Enroll(Start), false, Start).NextState;
        var second = _scheduler.Review(first, false, first.NextReviewAt).NextState;
        var correct = _scheduler.Review(second, true, second.NextReviewAt).NextState;
        Assert.Equal(Start.AddDays(1), first.NextReviewAt);
        Assert.Equal(Start.AddDays(2), second.NextReviewAt);
        Assert.Equal(Start.AddDays(3), correct.NextReviewAt);
        Assert.Equal(2, correct.LapseCount);
        Assert.Equal(1, correct.ConsecutiveCorrectCount);
        Assert.Equal(ReviewStatus.Learning, correct.ReviewStatus);
    }

    [Fact]
    public void ResetPreservesEnrollmentAndLifetimeLapsesAndStartsANewMeaningEpoch()
    {
        var previous = AfterSuccesses(4) with { LapseCount = 9 };
        var resetAt = previous.LastReviewedAt!.Value.AddHours(1); // Reset is allowed before next due.
        var reset = _scheduler.Reset(previous, resetAt);
        Assert.Equal(ReviewStatus.New, reset.ReviewStatus);
        Assert.Equal(previous.EnrollmentAt, reset.EnrollmentAt);
        Assert.Equal(9, reset.LapseCount);
        Assert.Null(reset.LastReviewedAt);
        Assert.Equal(0, reset.ConsecutiveCorrectCount);
        Assert.Equal(0, reset.ReviewIntervalDays);
        Assert.Equal(resetAt, reset.NextReviewAt);
        Assert.Equal(previous.ScheduleVersion + 1, reset.ScheduleVersion);
        Assert.Equal("interval-v1", reset.AlgorithmVersion);
        var firstAnswer = _scheduler.Review(reset, true, resetAt).NextState;
        Assert.Equal(resetAt.AddDays(1), firstAnswer.NextReviewAt);
        Assert.Equal(9, firstAnswer.LapseCount);
        Assert.Equal(ReviewStatus.Learning, firstAnswer.ReviewStatus);
    }

    [Fact]
    public void ResetOfNewStateIsAVersionedGenuineReset()
    {
        var state = _scheduler.Enroll(Start);
        var reset = _scheduler.Reset(state, Start.AddHours(1));
        Assert.Equal(2, reset.ScheduleVersion);
        Assert.Equal(Start.AddHours(1), reset.NextReviewAt);
        Assert.Equal(ReviewStatus.New, reset.ReviewStatus);
        Assert.Throws<ArgumentOutOfRangeException>(() => _scheduler.Reset(reset, Start));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void DueBoundaryIsInclusiveAtTickPrecision(long ticks, bool due)
    {
        var state = AfterSuccesses(1);
        Assert.Equal(due, _scheduler.IsDue(state, state.NextReviewAt.AddTicks(ticks)));
    }

    [Fact]
    public void EarlyAnswerIsRejectedAndOverdueAnswerAnchorsToActualAcceptedTime()
    {
        var state = AfterSuccesses(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => _scheduler.Review(state, true, state.NextReviewAt.AddTicks(-1)));
        var acceptedAt = state.NextReviewAt.AddDays(50);
        Assert.True(_scheduler.IsDue(state, acceptedAt));
        var next = _scheduler.Review(state, true, acceptedAt).NextState;
        Assert.Equal(acceptedAt.AddDays(3), next.NextReviewAt);
        Assert.Equal(2, next.ConsecutiveCorrectCount); // No bonus for overdue time.
    }

    [Theory]
    [InlineData(2026, 3, 7)]
    [InlineData(2026, 10, 31)]
    public void OneDayRemainsTwentyFourHoursAcrossNewYorkDstTransitions(int year, int month, int day)
    {
        var acceptedAt = new DateTime(year, month, day, 17, 0, 0, DateTimeKind.Utc);
        var next = _scheduler.Review(_scheduler.Enroll(acceptedAt), true, acceptedAt).NextState;
        Assert.Equal(TimeSpan.FromHours(24), next.NextReviewAt - acceptedAt);
        // Verify the fixture actually crosses a DST offset change, without using local machine time.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.NotEqual(zone.GetUtcOffset(acceptedAt), zone.GetUtcOffset(next.NextReviewAt));
        Assert.Equal(DateTimeKind.Utc, next.NextReviewAt.Kind);
    }

    [Fact]
    public void LeapDayIsElapsedTimeRatherThanCalendarRounding()
    {
        var acceptedAt = new DateTime(2028, 2, 28, 23, 59, 59, DateTimeKind.Utc).AddTicks(123);
        var first = _scheduler.Review(_scheduler.Enroll(acceptedAt), true, acceptedAt).NextState;
        Assert.Equal(new DateTime(2028, 2, 29, 23, 59, 59, DateTimeKind.Utc).AddTicks(123), first.NextReviewAt);
        var second = _scheduler.Review(first, true, first.NextReviewAt).NextState;
        Assert.Equal(new DateTime(2028, 3, 3, 23, 59, 59, DateTimeKind.Utc).AddTicks(123), second.NextReviewAt);
    }

    public static IEnumerable<object[]> InvalidStates()
    {
        var initial = new IntervalV1ReviewScheduler().Enroll(Start);
        var reviewed = new IntervalV1ReviewScheduler().Review(initial, true, Start).NextState;
        yield return [initial with { AlgorithmVersion = "fsrs" }];
        yield return [initial with { AlgorithmVersion = null! }];
        yield return [initial with { ScheduleVersion = 0 }];
        yield return [initial with { LapseCount = -1 }];
        yield return [initial with { ConsecutiveCorrectCount = -1 }];
        yield return [initial with { ConsecutiveCorrectCount = 8 }];
        yield return [initial with { ReviewStatus = (ReviewStatus)3 }];
        yield return [initial with { NextReviewAt = Start.AddTicks(-1) }];
        yield return [initial with { EnrollmentAt = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified) }];
        yield return [initial with { NextReviewAt = DateTime.SpecifyKind(Start, DateTimeKind.Local) }];
        yield return [initial with { LastReviewedAt = Start }];
        yield return [initial with { ConsecutiveCorrectCount = 1 }];
        yield return [initial with { ReviewIntervalDays = 1 }];
        yield return [reviewed with { LastReviewedAt = null }];
        yield return [reviewed with { LastReviewedAt = Start.AddTicks(-1) }];
        yield return [reviewed with { LastReviewedAt = DateTime.SpecifyKind(Start, DateTimeKind.Local) }];
        yield return [reviewed with { ReviewIntervalDays = 3 }];
        yield return [reviewed with { ReviewStatus = ReviewStatus.Reviewing }];
        yield return [reviewed with { NextReviewAt = reviewed.NextReviewAt.AddTicks(1) }];
        yield return [reviewed with { ConsecutiveCorrectCount = 0, LapseCount = 0 }];
        yield return [reviewed with { LastReviewedAt = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) }];
    }

    [Theory]
    [MemberData(nameof(InvalidStates))]
    public void EveryOperationRejectsInvalidState(ReviewScheduleState state)
    {
        Assert.ThrowsAny<ArgumentException>(() => _scheduler.Review(state, true, Start.AddYears(1)));
        Assert.ThrowsAny<ArgumentException>(() => _scheduler.Reset(state, Start.AddYears(1)));
        Assert.ThrowsAny<ArgumentException>(() => _scheduler.IsDue(state, Start.AddYears(1)));
    }

    [Fact]
    public void NullStateIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _scheduler.Review(null!, true, Start));
        Assert.Throws<ArgumentNullException>(() => _scheduler.Reset(null!, Start));
        Assert.Throws<ArgumentNullException>(() => _scheduler.IsDue(null!, Start));
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void AllExplicitInstantsMustHaveUtcKind(DateTimeKind kind)
    {
        var timestamp = DateTime.SpecifyKind(Start, kind);
        var state = _scheduler.Enroll(Start);
        Assert.Throws<ArgumentException>(() => _scheduler.Enroll(timestamp));
        Assert.Throws<ArgumentException>(() => _scheduler.Review(state, true, timestamp));
        Assert.Throws<ArgumentException>(() => _scheduler.Reset(state, timestamp));
        Assert.Throws<ArgumentException>(() => _scheduler.IsDue(state, timestamp));
    }

    [Fact]
    public void ResetAndReviewCannotPrecedeEnrollmentOrPreviousAcceptedReview()
    {
        var state = AfterSuccesses(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => _scheduler.Reset(state, Start.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _scheduler.Review(state, false, Start.AddTicks(-1)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DateOverflowCannotProduceATransition(bool isCorrect)
    {
        var maximumUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var state = _scheduler.Enroll(maximumUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => _scheduler.Review(state, isCorrect, maximumUtc));
        Assert.Equal(1, state.ScheduleVersion);
    }

    [Fact]
    public void MaximumDateWithExactlyOneDayRemainingIsAccepted()
    {
        var maximumUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var accepted = maximumUtc.AddDays(-1);
        var state = _scheduler.Review(_scheduler.Enroll(accepted), true, accepted).NextState;
        Assert.Equal(maximumUtc, state.NextReviewAt);
        Assert.True(_scheduler.IsDue(state, maximumUtc));
    }

    [Fact]
    public void VersionAndLapseArithmeticIsCheckedWithoutWrapping()
    {
        var state = _scheduler.Enroll(Start) with { ScheduleVersion = long.MaxValue };
        Assert.Throws<OverflowException>(() => _scheduler.Review(state, true, Start));
        Assert.Throws<OverflowException>(() => _scheduler.Reset(state, Start));
        var maxLapses = _scheduler.Enroll(Start) with { LapseCount = int.MaxValue };
        Assert.Throws<OverflowException>(() => _scheduler.Review(maxLapses, false, Start));
        Assert.Equal(int.MaxValue, _scheduler.Review(maxLapses, true, Start).NextState.LapseCount);
        Assert.Equal(int.MaxValue, _scheduler.Reset(maxLapses, Start).LapseCount);
    }

    [Fact]
    public void RepeatedExecutionIsDeterministicAndDoesNotMutatePreviousState()
    {
        var state = AfterSuccesses(3) with { LapseCount = 8 };
        var original = state with { };
        var acceptedAt = state.NextReviewAt.AddTicks(123);
        Assert.Equal(_scheduler.Review(state, true, acceptedAt), _scheduler.Review(state, true, acceptedAt));
        Assert.Equal(_scheduler.Review(state, false, acceptedAt), _scheduler.Review(state, false, acceptedAt));
        Assert.Equal(_scheduler.Reset(state, acceptedAt), _scheduler.Reset(state, acceptedAt));
        Assert.Equal(original, state);
    }

    private ReviewScheduleState AfterSuccesses(int count)
    {
        var state = _scheduler.Enroll(Start);
        for (var i = 0; i < count; i++)
            state = _scheduler.Review(state, true, state.NextReviewAt).NextState;
        return state;
    }
}
