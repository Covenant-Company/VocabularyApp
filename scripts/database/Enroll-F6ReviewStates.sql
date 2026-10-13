/* F6 SQL Server enrollment only; NEVER part of the EF schema migration.
   Review and authorize the target separately. Reviews must remain disabled.
   Set EnrollmentAtUtc to the captured rollout UTC instant (do not change it on reruns).
   Initial and catch-up use the same insert-only operation. Catch-up normally starts at 0;
   resuming a failed run may start at its last reported committed AfterUserWordId.
   StopAtUserWordId defaults to MAX(Id) at run start, so ongoing inserts cannot prolong this run.
   Rerun catch-up from 0 after old writers stop; then run Verify-F6Enrollment.sql.
   No preference, quiz, history, session, or existing review-state rows are updated. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Mode nvarchar(16) = N'initial'; -- initial or catch-up
DECLARE @EnrollmentAtUtc datetime2(7) = NULL; -- REQUIRED: explicit approved UTC reference instant
DECLARE @BatchSize int = 1000;
DECLARE @AfterUserWordId int = 0;
DECLARE @StopAtUserWordId int = NULL;
DECLARE @MaximumBatches int = 100; -- bounded run; resume/rerun to finish larger vocabularies

IF @Mode IS NULL OR @Mode NOT IN (N'initial', N'catch-up') OR @EnrollmentAtUtc IS NULL
    THROW 51100, 'Specify initial/catch-up mode and an explicit captured enrollment UTC instant.', 1;
IF @BatchSize IS NULL OR @BatchSize NOT BETWEEN 1 AND 10000
    OR @MaximumBatches IS NULL OR @MaximumBatches NOT BETWEEN 1 AND 10000
    OR @AfterUserWordId IS NULL OR @AfterUserWordId < 0
    OR (@StopAtUserWordId IS NOT NULL AND @StopAtUserWordId < @AfterUserWordId)
    THROW 51101, 'Invalid enrollment batch/range parameters.', 1;
IF @@TRANCOUNT <> 0
    THROW 51102, 'Run enrollment outside an enclosing transaction; each batch commits independently.', 1;
IF OBJECT_ID(N'dbo.UserWordReviewStates', N'U') IS NULL
    OR COL_LENGTH(N'dbo.WordDefinitions', N'ContentRevision') IS NULL
    THROW 51103, 'F6 schema must be installed and verified before enrollment.', 1;

-- Unicode whitespace recognized by .NET IsNullOrWhiteSpace. BIN2 avoids linguistic equivalence.
DECLARE @Whitespace nvarchar(64) = NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)
    +NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)
    +NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)
    +NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288);
DECLARE @UsablePattern nvarchar(80) = N'%[^' + @Whitespace + N']%';

SET @StopAtUserWordId = COALESCE(@StopAtUserWordId, (SELECT MAX(Id) FROM dbo.UserWords), 0);
DECLARE @BatchNumber int = 0, @Inserted int, @TotalInserted bigint = 0, @LastInBatch int;
DECLARE @Batch TABLE (Id int PRIMARY KEY);

WHILE @AfterUserWordId < @StopAtUserWordId AND @BatchNumber < @MaximumBatches
BEGIN
    DELETE FROM @Batch;
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT INTO @Batch (Id)
        SELECT TOP (@BatchSize) Id FROM dbo.UserWords
        WHERE Id > @AfterUserWordId AND Id <= @StopAtUserWordId ORDER BY Id;
        SET @LastInBatch = (SELECT MAX(Id) FROM @Batch);
        IF @LastInBatch IS NULL
        BEGIN
            COMMIT TRANSACTION;
            BREAK;
        END;

        INSERT INTO dbo.UserWordReviewStates
            (UserWordId, UserId, ScheduledWordDefinitionId, ScheduledDefinitionRevision,
             EnrollmentAt, NextReviewAt, LastReviewedAt, ConsecutiveCorrectCount,
             ReviewIntervalDays, LapseCount, ReviewStatus, AlgorithmVersion, ScheduleVersion)
        SELECT uw.Id, uw.UserId,
            CASE WHEN w.Text COLLATE Latin1_General_100_BIN2 LIKE @UsablePattern THEN selectedMeaning.Id END,
            CASE WHEN w.Text COLLATE Latin1_General_100_BIN2 LIKE @UsablePattern THEN selectedMeaning.ContentRevision END,
            @EnrollmentAtUtc, @EnrollmentAtUtc, NULL, 0, 0, 0, 0, N'interval-v1', 1
        FROM @Batch b
        JOIN dbo.UserWords uw ON uw.Id = b.Id
        JOIN dbo.Words w ON w.Id = uw.WordId
        OUTER APPLY (
            SELECT d.Id, d.Definition FROM dbo.WordDefinitions d
            WHERE d.Id = uw.PreferredWordDefinitionId AND d.WordId = uw.WordId
                AND d.PartOfSpeechId = uw.PartOfSpeechId
        ) preferred
        OUTER APPLY (
            SELECT TOP (1) d.Id, d.ContentRevision FROM dbo.WordDefinitions d
            WHERE d.WordId = uw.WordId AND d.PartOfSpeechId = uw.PartOfSpeechId
                AND d.Definition COLLATE Latin1_General_100_BIN2 LIKE @UsablePattern
                -- A valid explicit blank preferred meaning is blocked, never replaced by fallback.
                AND (preferred.Id IS NULL OR d.Id = preferred.Id)
            ORDER BY d.DisplayOrder, d.Id
        ) selectedMeaning
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.UserWordReviewStates state WITH (UPDLOCK, HOLDLOCK)
            WHERE state.UserWordId = uw.Id
        );
        SET @Inserted = @@ROWCOUNT;
        COMMIT TRANSACTION;

        SET @AfterUserWordId = @LastInBatch;
        SET @BatchNumber += 1;
        SET @TotalInserted += @Inserted;
        SELECT @Mode AS Mode, @BatchNumber AS CommittedBatch, @AfterUserWordId AS AfterUserWordId,
            @StopAtUserWordId AS StopAtUserWordId, @Inserted AS InsertedStates,
            @EnrollmentAtUtc AS EnrollmentAtUtc;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        -- Do not suppress arbitrary FK/check/unique/deadlock errors. Prior committed batches survive.
        -- A competing newer application insert/deadlock may require rerunning this idempotent batch.
        THROW;
    END CATCH;
END;

SELECT @TotalInserted AS TotalInsertedStates, @AfterUserWordId AS LastCommittedUserWordId,
    @StopAtUserWordId AS CapturedUpperBound,
    (SELECT COUNT_BIG(*) FROM dbo.UserWords uw LEFT JOIN dbo.UserWordReviewStates s ON s.UserWordId = uw.Id
        WHERE s.UserWordId IS NULL) AS MissingStatesNow;
