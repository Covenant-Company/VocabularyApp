-- Deployment prerequisite for R4 Phase 5.
-- Run read-only against the target database before applying
-- 20260819000000_AddQuizResultSubmissionUniqueness.
-- Any returned row blocks the migration and requires separately approved cleanup.

SELECT
    UserId,
    QuizSessionId,
    UserWordId,
    COUNT(*) AS DuplicateCount,
    MIN(Id) AS FirstResultId,
    MAX(Id) AS LastResultId
FROM QuizResults
GROUP BY UserId, QuizSessionId, UserWordId
HAVING COUNT(*) > 1
ORDER BY DuplicateCount DESC, UserId, QuizSessionId, UserWordId;
