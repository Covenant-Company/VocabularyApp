/*
R4 Phase 6 historical quiz reconciliation — SQL Server

MANUAL EXECUTION ONLY. This script has not been executed by Codex.

Approved scope:
- Reconstruct UserWords learning aggregates from owner-consistent SURVIVING
  QuizResults in the reviewed development database.
- Leave UserWords with no surviving QuizResults untouched.
- Never change QuizResults, delete data, alter schema, or apply migrations.

Historical limitation:
This operation cannot restore cascade-deleted QuizResults, attempts that were
never persisted, or other missing history. Null/empty UserAnswer is not treated
as corruption and is not used in the derivation.

Safety workflow:
1. Keep @ApplyChanges = 0 and run the complete script to inspect guards,
   mismatch count, and PREVIEW output. The transaction will be rolled back.
2. Review and retain the preview output.
3. Only after backup/recovery verification and explicit execution approval,
   change @ApplyChanges to 1 and run the complete script in a maintenance window.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

DECLARE @ApplyChanges bit = 0; -- PREVIEW ONLY by default. Set to 1 only after approval.
DECLARE @MismatchCountBefore int;
DECLARE @MismatchCountAfter int;
DECLARE @RowsUpdated int;

BEGIN TRY
    BEGIN TRANSACTION;

    /* PRECONDITION / BLOCKING CHECKS */

    IF EXISTS
    (
        SELECT 1
        FROM QuizResults
        GROUP BY UserId, QuizSessionId, UserWordId
        HAVING COUNT_BIG(*) > 1
    )
    BEGIN
        THROW 51000,
            'Reconciliation blocked: duplicate (UserId, QuizSessionId, UserWordId) QuizResult groups exist.',
            1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM QuizResults AS qr
        INNER JOIN UserWords AS uw ON uw.Id = qr.UserWordId
        WHERE qr.UserId <> uw.UserId
    )
    BEGIN
        THROW 51001,
            'Reconciliation blocked: QuizResult/UserWord ownership mismatches exist.',
            1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM QuizResults AS qr
        LEFT JOIN Users AS u ON u.Id = qr.UserId
        LEFT JOIN UserWords AS uw ON uw.Id = qr.UserWordId
        WHERE u.Id IS NULL OR uw.Id IS NULL
    )
    BEGIN
        THROW 51002,
            'Reconciliation blocked: orphaned QuizResult user or UserWord references exist.',
            1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM QuizResults AS qr
        INNER JOIN UserWords AS uw
            ON uw.Id = qr.UserWordId
           AND uw.UserId = qr.UserId
        GROUP BY qr.UserWordId
        HAVING COUNT_BIG(*) > 2147483647
            OR SUM(CASE WHEN qr.IsCorrect = 1
                        THEN CONVERT(bigint, 1)
                        ELSE CONVERT(bigint, 0)
                   END) > 2147483647
    )
    BEGIN
        THROW 51003,
            'Reconciliation blocked: a derived count exceeds the UserWords int column range.',
            1;
    END;

    /* CALCULATE DERIVED VALUES FIRST */

    DECLARE @DerivedHistory TABLE
    (
        UserWordId int NOT NULL PRIMARY KEY,
        UserId int NOT NULL,
        DerivedTotalAttempts int NOT NULL,
        DerivedCorrectAnswers int NOT NULL,
        DerivedLastReviewedAt datetime2 NOT NULL,
        DerivedLastCorrectAt datetime2 NULL
    );

    INSERT INTO @DerivedHistory
    (
        UserWordId,
        UserId,
        DerivedTotalAttempts,
        DerivedCorrectAnswers,
        DerivedLastReviewedAt,
        DerivedLastCorrectAt
    )
    SELECT
        qr.UserWordId,
        uw.UserId,
        CONVERT(int, COUNT_BIG(*)),
        CONVERT(int, SUM(CASE WHEN qr.IsCorrect = 1
                              THEN CONVERT(bigint, 1)
                              ELSE CONVERT(bigint, 0)
                         END)),
        MAX(qr.AttemptedAt),
        MAX(CASE WHEN qr.IsCorrect = 1 THEN qr.AttemptedAt END)
    FROM QuizResults AS qr
    INNER JOIN UserWords AS uw
        ON uw.Id = qr.UserWordId
       AND uw.UserId = qr.UserId
    GROUP BY qr.UserWordId, uw.UserId;

    SELECT @MismatchCountBefore = COUNT(*)
    FROM UserWords AS uw
    INNER JOIN @DerivedHistory AS d ON d.UserWordId = uw.Id
    WHERE uw.UserId <> d.UserId
       OR uw.TotalAttempts <> d.DerivedTotalAttempts
       OR uw.CorrectAnswers <> d.DerivedCorrectAnswers
       OR uw.LastReviewedAt <> d.DerivedLastReviewedAt
       OR (uw.LastReviewedAt IS NULL AND d.DerivedLastReviewedAt IS NOT NULL)
       OR (uw.LastReviewedAt IS NOT NULL AND d.DerivedLastReviewedAt IS NULL)
       OR uw.LastCorrectAt <> d.DerivedLastCorrectAt
       OR (uw.LastCorrectAt IS NULL AND d.DerivedLastCorrectAt IS NOT NULL)
       OR (uw.LastCorrectAt IS NOT NULL AND d.DerivedLastCorrectAt IS NULL);

    /* PREVIEW — every UserWord with owner-consistent surviving history. */

    SELECT
        uw.Id AS UserWordId,
        uw.UserId,
        uw.TotalAttempts AS StoredTotalAttempts,
        d.DerivedTotalAttempts,
        uw.CorrectAnswers AS StoredCorrectAnswers,
        d.DerivedCorrectAnswers,
        uw.LastReviewedAt AS StoredLastReviewedAt,
        d.DerivedLastReviewedAt,
        uw.LastCorrectAt AS StoredLastCorrectAt,
        d.DerivedLastCorrectAt,
        CASE WHEN
            uw.TotalAttempts <> d.DerivedTotalAttempts OR
            uw.CorrectAnswers <> d.DerivedCorrectAnswers OR
            uw.LastReviewedAt <> d.DerivedLastReviewedAt OR
            (uw.LastReviewedAt IS NULL AND d.DerivedLastReviewedAt IS NOT NULL) OR
            (uw.LastReviewedAt IS NOT NULL AND d.DerivedLastReviewedAt IS NULL) OR
            uw.LastCorrectAt <> d.DerivedLastCorrectAt OR
            (uw.LastCorrectAt IS NULL AND d.DerivedLastCorrectAt IS NOT NULL) OR
            (uw.LastCorrectAt IS NOT NULL AND d.DerivedLastCorrectAt IS NULL)
            THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0)
        END AS WillChange
    FROM UserWords AS uw
    INNER JOIN @DerivedHistory AS d
        ON d.UserWordId = uw.Id
       AND d.UserId = uw.UserId
    ORDER BY WillChange DESC, uw.UserId, uw.Id;

    SELECT
        @ApplyChanges AS ApplyChanges,
        (SELECT COUNT(*) FROM @DerivedHistory) AS UserWordsWithSurvivingHistory,
        @MismatchCountBefore AS MismatchCountBefore;

    IF @ApplyChanges = 0
    BEGIN
        ROLLBACK TRANSACTION;
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
        PRINT 'PREVIEW ONLY: no UserWords were changed. Set @ApplyChanges = 1 only after review and approval.';
        RETURN;
    END;

    /* RECONCILIATION — assignment only; zero-history UserWords are absent. */

    UPDATE uw
    SET
        TotalAttempts = d.DerivedTotalAttempts,
        CorrectAnswers = d.DerivedCorrectAnswers,
        LastReviewedAt = d.DerivedLastReviewedAt,
        LastCorrectAt = d.DerivedLastCorrectAt
    FROM UserWords AS uw
    INNER JOIN @DerivedHistory AS d
        ON d.UserWordId = uw.Id
       AND d.UserId = uw.UserId
    WHERE uw.TotalAttempts <> d.DerivedTotalAttempts
       OR uw.CorrectAnswers <> d.DerivedCorrectAnswers
       OR uw.LastReviewedAt <> d.DerivedLastReviewedAt
       OR (uw.LastReviewedAt IS NULL AND d.DerivedLastReviewedAt IS NOT NULL)
       OR (uw.LastReviewedAt IS NOT NULL AND d.DerivedLastReviewedAt IS NULL)
       OR uw.LastCorrectAt <> d.DerivedLastCorrectAt
       OR (uw.LastCorrectAt IS NULL AND d.DerivedLastCorrectAt IS NOT NULL)
       OR (uw.LastCorrectAt IS NOT NULL AND d.DerivedLastCorrectAt IS NULL);

    SET @RowsUpdated = @@ROWCOUNT;

    /* VALIDATION — any remaining mismatch aborts and rolls back the update. */

    SELECT @MismatchCountAfter = COUNT(*)
    FROM UserWords AS uw
    INNER JOIN @DerivedHistory AS d
        ON d.UserWordId = uw.Id
       AND d.UserId = uw.UserId
    WHERE uw.TotalAttempts <> d.DerivedTotalAttempts
       OR uw.CorrectAnswers <> d.DerivedCorrectAnswers
       OR uw.LastReviewedAt <> d.DerivedLastReviewedAt
       OR (uw.LastReviewedAt IS NULL AND d.DerivedLastReviewedAt IS NOT NULL)
       OR (uw.LastReviewedAt IS NOT NULL AND d.DerivedLastReviewedAt IS NULL)
       OR uw.LastCorrectAt <> d.DerivedLastCorrectAt
       OR (uw.LastCorrectAt IS NULL AND d.DerivedLastCorrectAt IS NOT NULL)
       OR (uw.LastCorrectAt IS NOT NULL AND d.DerivedLastCorrectAt IS NULL);

    SELECT
        @MismatchCountBefore AS MismatchCountBefore,
        @RowsUpdated AS UserWordsUpdated,
        @MismatchCountAfter AS MismatchCountAfter;

    SELECT
        uw.Id AS UserWordId,
        uw.UserId,
        uw.TotalAttempts AS StoredTotalAttempts,
        d.DerivedTotalAttempts,
        uw.CorrectAnswers AS StoredCorrectAnswers,
        d.DerivedCorrectAnswers,
        uw.LastReviewedAt AS StoredLastReviewedAt,
        d.DerivedLastReviewedAt,
        uw.LastCorrectAt AS StoredLastCorrectAt,
        d.DerivedLastCorrectAt
    FROM UserWords AS uw
    INNER JOIN @DerivedHistory AS d
        ON d.UserWordId = uw.Id
       AND d.UserId = uw.UserId
    WHERE uw.TotalAttempts <> d.DerivedTotalAttempts
       OR uw.CorrectAnswers <> d.DerivedCorrectAnswers
       OR uw.LastReviewedAt <> d.DerivedLastReviewedAt
       OR (uw.LastReviewedAt IS NULL AND d.DerivedLastReviewedAt IS NOT NULL)
       OR (uw.LastReviewedAt IS NOT NULL AND d.DerivedLastReviewedAt IS NULL)
       OR uw.LastCorrectAt <> d.DerivedLastCorrectAt
       OR (uw.LastCorrectAt IS NULL AND d.DerivedLastCorrectAt IS NOT NULL)
       OR (uw.LastCorrectAt IS NOT NULL AND d.DerivedLastCorrectAt IS NULL)
    ORDER BY uw.UserId, uw.Id;

    IF @MismatchCountAfter <> 0
    BEGIN
        THROW 51004,
            'Reconciliation validation failed: stored and derived aggregate values still differ.',
            1;
    END;

    /* COMMIT */

    COMMIT TRANSACTION;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    PRINT 'Reconciliation committed. Review the counts and validation result sets above.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    THROW;
END CATCH;
