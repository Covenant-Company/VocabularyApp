using VocabularyApp.WebApi.Models;

namespace VocabularyApp.WebApi.Services;

public interface IReviewScheduler
{
    ReviewScheduleState Enroll(DateTime enrollmentAtUtc);
    ReviewScheduleState Reset(ReviewScheduleState state, DateTime resetAtUtc);
    ReviewTransition Review(ReviewScheduleState state, bool isCorrect, DateTime acceptedReviewAtUtc);
    bool IsDue(ReviewScheduleState state, DateTime asOfUtc);
}
