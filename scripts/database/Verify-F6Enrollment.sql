/* Read-only SQL Server verification. No enrollment, updates, schema repair or transactions.
   Run before/after rehearsal and after old writers stop. Quiesce vocabulary/content writes
   for the final activation gate; these individual queries are not one snapshot transaction.
   Compare personal/quiz/dependent baselines to PRE-enrollment evidence captured externally. */
SET NOCOUNT ON;

SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT (SELECT COUNT_BIG(*) FROM dbo.UserWords) AS SavedWords,
    (SELECT COUNT_BIG(*) FROM dbo.UserWordReviewStates) AS EnrolledStates,
    (SELECT COUNT_BIG(*) FROM dbo.UserWords uw LEFT JOIN dbo.UserWordReviewStates s ON s.UserWordId = uw.Id
        WHERE s.UserWordId IS NULL) AS MissingStates,
    (SELECT COUNT_BIG(*) FROM dbo.UserWordReviewStates s LEFT JOIN dbo.UserWords uw
        ON uw.Id = s.UserWordId AND uw.UserId = s.UserId WHERE uw.Id IS NULL) AS OrphanOrWrongOwnerStates,
    (SELECT COUNT_BIG(*) FROM dbo.ReviewHistories) AS ReviewHistoryRows,
    (SELECT COUNT_BIG(*) FROM dbo.ReviewSessions) AS ReviewSessionRows,
    (SELECT COUNT_BIG(*) FROM dbo.ReviewSessionItems) AS ReviewSessionItemRows;

SELECT TOP (100) uw.Id AS MissingUserWordId FROM dbo.UserWords uw
LEFT JOIN dbo.UserWordReviewStates s ON s.UserWordId = uw.Id
WHERE s.UserWordId IS NULL ORDER BY uw.Id;
SELECT UserWordId, COUNT_BIG(*) AS DuplicateCount FROM dbo.UserWordReviewStates
GROUP BY UserWordId HAVING COUNT_BIG(*) > 1;
SELECT UserId, WordId, COUNT_BIG(*) AS DuplicateIdentityCount FROM dbo.UserWords
GROUP BY UserId, WordId HAVING COUNT_BIG(*) > 1;
SELECT TOP (100) uw.Id AS UserWordId, uw.PreferredWordDefinitionId AS InvalidPreferredDefinitionId
FROM dbo.UserWords uw LEFT JOIN dbo.WordDefinitions d ON d.Id = uw.PreferredWordDefinitionId
WHERE uw.PreferredWordDefinitionId IS NOT NULL
    AND (d.Id IS NULL OR d.WordId <> uw.WordId OR d.PartOfSpeechId <> uw.PartOfSpeechId)
ORDER BY uw.Id;

DECLARE @Whitespace nvarchar(64) = NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)
    +NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)
    +NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)
    +NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288);
DECLARE @UsablePattern nvarchar(80) = N'%[^' + @Whitespace + N']%';

-- Stale meaning/revision mismatches require controlled reconciliation; enrollment never resets them.
SELECT TOP (100) uw.Id AS UserWordId, s.ScheduledWordDefinitionId, expected.Id AS ExpectedDefinitionId,
    s.ScheduledDefinitionRevision, expected.ContentRevision AS ExpectedRevision
FROM dbo.UserWords uw
JOIN dbo.Words w ON w.Id = uw.WordId
JOIN dbo.UserWordReviewStates s ON s.UserWordId = uw.Id
OUTER APPLY (SELECT d.Id FROM dbo.WordDefinitions d WHERE d.Id = uw.PreferredWordDefinitionId
    AND d.WordId = uw.WordId AND d.PartOfSpeechId = uw.PartOfSpeechId) preferred
OUTER APPLY (
    SELECT TOP (1) d.Id, d.ContentRevision FROM dbo.WordDefinitions d
    WHERE d.WordId = uw.WordId AND d.PartOfSpeechId = uw.PartOfSpeechId
        AND w.Text COLLATE Latin1_General_100_BIN2 LIKE @UsablePattern
        AND d.Definition COLLATE Latin1_General_100_BIN2 LIKE @UsablePattern
        AND (preferred.Id IS NULL OR d.Id = preferred.Id)
    ORDER BY d.DisplayOrder, d.Id
) expected
WHERE ISNULL(s.ScheduledWordDefinitionId, -1) <> ISNULL(expected.Id, -1)
    OR ISNULL(s.ScheduledDefinitionRevision, -1) <> ISNULL(expected.ContentRevision, -1)
ORDER BY uw.Id;

SELECT TOP (100) s.UserWordId FROM dbo.UserWordReviewStates s
WHERE s.ReviewStatus <> 0 OR s.ConsecutiveCorrectCount <> 0 OR s.ReviewIntervalDays <> 0
    OR s.LapseCount <> 0 OR s.LastReviewedAt IS NOT NULL OR s.AlgorithmVersion <> N'interval-v1'
    OR s.ScheduleVersion <> 1 OR s.NextReviewAt <> s.EnrollmentAt
ORDER BY s.UserWordId; -- Must be empty immediately after insert-only enrollment with reviews disabled.

SELECT COUNT_BIG(*) AS InvalidContentRevisions FROM dbo.WordDefinitions WHERE ContentRevision < 1;
SELECT COUNT_BIG(*) AS QuizResultRows FROM dbo.QuizResults;
SELECT COUNT_BIG(*) AS SampleSentenceRows FROM dbo.SampleSentences;
SELECT COUNT_BIG(*) AS UserRows FROM dbo.Users;
SELECT COUNT_BIG(*) AS WordRows FROM dbo.Words;
SELECT COUNT_BIG(*) AS DefinitionRows FROM dbo.WordDefinitions;
SELECT SUM(CONVERT(bigint, TotalAttempts)) AS TotalQuizAttempts,
    SUM(CONVERT(bigint, CorrectAnswers)) AS CorrectQuizAnswers,
    SUM(CASE WHEN IsFavorite = 1 THEN CONVERT(bigint, 1) ELSE 0 END) AS Favorites,
    SUM(CASE WHEN PersonalNotes IS NOT NULL THEN CONVERT(bigint, 1) ELSE 0 END) AS RowsWithNotes
FROM dbo.UserWords;

-- Use owner-approved secure export/row-by-row comparison for full preservation evidence.
-- Counts alone do not prove notes, selections, legacy dates, quiz answers or relationships unchanged.
SELECT fk.name, OBJECT_NAME(fk.parent_object_id) AS ChildTable, fk.delete_referential_action_desc,
    fk.is_disabled, fk.is_not_trusted
FROM sys.foreign_keys fk WHERE OBJECT_NAME(fk.parent_object_id)
    IN (N'UserWordReviewStates', N'ReviewSessions', N'ReviewSessionItems', N'ReviewHistories');
SELECT OBJECT_NAME(i.object_id) AS TableName, i.name, i.is_unique, i.filter_definition
FROM sys.indexes i WHERE OBJECT_NAME(i.object_id)
    IN (N'UserWords', N'UserWordReviewStates', N'ReviewSessions', N'ReviewSessionItems', N'ReviewHistories')
ORDER BY TableName, i.name;
