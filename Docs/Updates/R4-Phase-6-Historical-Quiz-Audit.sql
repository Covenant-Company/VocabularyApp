/*
R4 Phase 6 historical quiz audit (SQL Server)

READ-ONLY: apart from the session-local SET NOCOUNT option, this script contains
SELECT statements only. Run it first against a restored/staging copy of the
intended database. Do not apply the Phase 5 unique index while Section A returns
any rows. The script deliberately does not select answer text.
*/

SET NOCOUNT ON;

/* 0. Inventory and reconstructability of surviving rows. */
SELECT
    COUNT_BIG(*) AS QuizResultCount,
    COUNT(DISTINCT UserWordId) AS UserWordsWithResults,
    MIN(AttemptedAt) AS EarliestAttemptedAt,
    MAX(AttemptedAt) AS LatestAttemptedAt,
    SUM(CASE WHEN QuizSessionId = '00000000-0000-0000-0000-000000000000' THEN 1 ELSE 0 END)
        AS EmptySessionIdCount
FROM QuizResults;

/* A. Duplicate keys that block the Phase 5 unique index. */
SELECT
    UserId,
    QuizSessionId,
    UserWordId,
    COUNT_BIG(*) AS DuplicateCount,
    MIN(Id) AS FirstResultId,
    MAX(Id) AS LastResultId,
    MIN(AttemptedAt) AS FirstAttemptedAt,
    MAX(AttemptedAt) AS LastAttemptedAt
FROM QuizResults
GROUP BY UserId, QuizSessionId, UserWordId
HAVING COUNT_BIG(*) > 1
ORDER BY DuplicateCount DESC, UserId, QuizSessionId, UserWordId;

/* B. Result ownership does not match the referenced UserWord owner. */
SELECT
    qr.Id AS QuizResultId,
    qr.UserId AS ResultUserId,
    qr.UserWordId,
    uw.UserId AS UserWordOwnerId,
    qr.QuizSessionId,
    qr.AttemptedAt
FROM QuizResults AS qr
INNER JOIN UserWords AS uw ON uw.Id = qr.UserWordId
WHERE qr.UserId <> uw.UserId
ORDER BY qr.Id;

/* C. Orphaned references. Foreign keys should prevent these in a consistent DB. */
SELECT
    qr.Id AS QuizResultId,
    qr.UserId,
    qr.UserWordId,
    qr.QuizSessionId,
    CASE WHEN u.Id IS NULL THEN 1 ELSE 0 END AS MissingUser,
    CASE WHEN uw.Id IS NULL THEN 1 ELSE 0 END AS MissingUserWord
FROM QuizResults AS qr
LEFT JOIN Users AS u ON u.Id = qr.UserId
LEFT JOIN UserWords AS uw ON uw.Id = qr.UserWordId
WHERE u.Id IS NULL OR uw.Id IS NULL
ORDER BY qr.Id;

/* D. Stored and result-derived aggregate values for every surviving UserWord. */
WITH Derived AS
(
    SELECT
        qr.UserWordId,
        COUNT_BIG(*) AS DerivedTotalAttempts,
        SUM(CASE WHEN qr.IsCorrect = 1 THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
            AS DerivedCorrectAnswers,
        MAX(qr.AttemptedAt) AS DerivedLastReviewedAt,
        MAX(CASE WHEN qr.IsCorrect = 1 THEN qr.AttemptedAt END) AS DerivedLastCorrectAt
    FROM QuizResults AS qr
    INNER JOIN UserWords AS ownerCheck
        ON ownerCheck.Id = qr.UserWordId
       AND ownerCheck.UserId = qr.UserId
    GROUP BY qr.UserWordId
)
SELECT
    uw.Id AS UserWordId,
    uw.UserId,
    uw.TotalAttempts AS StoredTotalAttempts,
    COALESCE(d.DerivedTotalAttempts, 0) AS DerivedTotalAttempts,
    uw.CorrectAnswers AS StoredCorrectAnswers,
    COALESCE(d.DerivedCorrectAnswers, 0) AS DerivedCorrectAnswers,
    uw.LastReviewedAt AS StoredLastReviewedAt,
    d.DerivedLastReviewedAt,
    uw.LastCorrectAt AS StoredLastCorrectAt,
    d.DerivedLastCorrectAt,
    CASE WHEN
        CONVERT(bigint, uw.TotalAttempts) <> COALESCE(d.DerivedTotalAttempts, 0) OR
        CONVERT(bigint, uw.CorrectAnswers) <> COALESCE(d.DerivedCorrectAnswers, 0) OR
        (uw.LastReviewedAt <> d.DerivedLastReviewedAt) OR
        (uw.LastReviewedAt IS NULL AND d.DerivedLastReviewedAt IS NOT NULL) OR
        (uw.LastReviewedAt IS NOT NULL AND d.DerivedLastReviewedAt IS NULL) OR
        (uw.LastCorrectAt <> d.DerivedLastCorrectAt) OR
        (uw.LastCorrectAt IS NULL AND d.DerivedLastCorrectAt IS NOT NULL) OR
        (uw.LastCorrectAt IS NOT NULL AND d.DerivedLastCorrectAt IS NULL)
        THEN 1 ELSE 0
    END AS HasAggregateMismatch
FROM UserWords AS uw
LEFT JOIN Derived AS d ON d.UserWordId = uw.Id
ORDER BY HasAggregateMismatch DESC, uw.UserId, uw.Id;

/* E. Impossible stored counters. */
SELECT
    Id AS UserWordId,
    UserId,
    CorrectAnswers,
    TotalAttempts
FROM UserWords
WHERE CorrectAnswers < 0
   OR TotalAttempts < 0
   OR CorrectAnswers > TotalAttempts
ORDER BY UserId, Id;

/* F1. Stored timestamp relationships that are internally inconsistent. */
SELECT
    Id AS UserWordId,
    UserId,
    TotalAttempts,
    CorrectAnswers,
    LastReviewedAt,
    LastCorrectAt
FROM UserWords
WHERE (LastCorrectAt IS NOT NULL AND CorrectAnswers = 0)
   OR (LastCorrectAt IS NOT NULL AND LastReviewedAt IS NULL)
   OR LastCorrectAt > LastReviewedAt
   OR (LastReviewedAt IS NOT NULL AND TotalAttempts = 0)
ORDER BY UserId, Id;

/* F2. Stored timestamps disagree with owner-consistent surviving history. */
WITH DerivedTimestamps AS
(
    SELECT
        qr.UserWordId,
        MAX(qr.AttemptedAt) AS DerivedLastReviewedAt,
        MAX(CASE WHEN qr.IsCorrect = 1 THEN qr.AttemptedAt END) AS DerivedLastCorrectAt,
        SUM(CASE WHEN qr.IsCorrect = 1 THEN 1 ELSE 0 END) AS DerivedCorrectCount
    FROM QuizResults AS qr
    INNER JOIN UserWords AS uw
        ON uw.Id = qr.UserWordId
       AND uw.UserId = qr.UserId
    GROUP BY qr.UserWordId
)
SELECT
    uw.Id AS UserWordId,
    uw.UserId,
    uw.LastReviewedAt AS StoredLastReviewedAt,
    dt.DerivedLastReviewedAt,
    uw.LastCorrectAt AS StoredLastCorrectAt,
    dt.DerivedLastCorrectAt,
    dt.DerivedCorrectCount
FROM UserWords AS uw
INNER JOIN DerivedTimestamps AS dt ON dt.UserWordId = uw.Id
WHERE uw.LastReviewedAt <> dt.DerivedLastReviewedAt
   OR (uw.LastReviewedAt IS NULL AND dt.DerivedLastReviewedAt IS NOT NULL)
   OR (uw.LastReviewedAt IS NOT NULL AND dt.DerivedLastReviewedAt IS NULL)
   OR uw.LastCorrectAt <> dt.DerivedLastCorrectAt
   OR (uw.LastCorrectAt IS NULL AND dt.DerivedLastCorrectAt IS NOT NULL)
   OR (uw.LastCorrectAt IS NOT NULL AND dt.DerivedLastCorrectAt IS NULL)
ORDER BY uw.UserId, uw.Id;

/* G. Null/empty answers are reported, not classified as corruption (Rule A). */
SELECT
    IsCorrect,
    COUNT_BIG(*) AS ResultCount,
    SUM(CASE WHEN UserAnswer IS NULL THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
        AS NullAnswerCount,
    SUM(CASE WHEN UserAnswer IS NOT NULL AND LTRIM(RTRIM(UserAnswer)) = ''
             THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
        AS EmptyAnswerCount
FROM QuizResults
GROUP BY IsCorrect
ORDER BY IsCorrect;

/* H1. Session IDs associated with multiple users. */
SELECT
    QuizSessionId,
    COUNT(DISTINCT UserId) AS UserCount,
    COUNT_BIG(*) AS ResultCount,
    MIN(AttemptedAt) AS FirstAttemptedAt,
    MAX(AttemptedAt) AS LastAttemptedAt
FROM QuizResults
GROUP BY QuizSessionId
HAVING COUNT(DISTINCT UserId) > 1
ORDER BY UserCount DESC, ResultCount DESC, QuizSessionId;

/* H2. Suspicious session sizes or timestamp spans. Current quizzes contain <= 20 results. */
SELECT
    UserId,
    QuizSessionId,
    COUNT_BIG(*) AS ResultCount,
    COUNT(DISTINCT UserWordId) AS DistinctUserWordCount,
    MIN(AttemptedAt) AS FirstAttemptedAt,
    MAX(AttemptedAt) AS LastAttemptedAt
FROM QuizResults
GROUP BY UserId, QuizSessionId
HAVING COUNT_BIG(*) > 20
    OR COUNT_BIG(*) <> COUNT(DISTINCT UserWordId)
    OR MIN(AttemptedAt) <> MAX(AttemptedAt)
ORDER BY ResultCount DESC, UserId, QuizSessionId;

/* H3. Results dated before the referenced vocabulary entry was added. */
SELECT
    qr.Id AS QuizResultId,
    qr.UserId,
    qr.UserWordId,
    uw.AddedAt,
    qr.AttemptedAt
FROM QuizResults AS qr
INNER JOIN UserWords AS uw ON uw.Id = qr.UserWordId
WHERE qr.AttemptedAt < uw.AddedAt
ORDER BY qr.AttemptedAt, qr.Id;

/* H4. Nonzero stored counters with no surviving result history. */
SELECT
    uw.Id AS UserWordId,
    uw.UserId,
    uw.CorrectAnswers,
    uw.TotalAttempts,
    uw.LastReviewedAt,
    uw.LastCorrectAt
FROM UserWords AS uw
WHERE NOT EXISTS
    (SELECT 1 FROM QuizResults AS qr WHERE qr.UserWordId = uw.Id)
  AND (uw.CorrectAnswers <> 0
       OR uw.TotalAttempts <> 0
       OR uw.LastReviewedAt IS NOT NULL
       OR uw.LastCorrectAt IS NOT NULL)
ORDER BY uw.UserId, uw.Id;
