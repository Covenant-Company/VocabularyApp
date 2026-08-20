# R4 Quiz Counter Remediation — Completion

## 1. Executive Summary

R4 is implementation-complete in source and has a human-verified backend baseline of 22 passing `QuizApiTests` and 152 passing backend tests. Valid submissions now update result history and per-word learning aggregates consistently, malformed submissions cause no mutation, persistence is atomic and retryable after failure, and sequential/concurrent duplicate submissions cannot double-count within the implemented protections.

R4 is **not deployment-complete** for an existing target database. The Phase 5 unique-index migration exists in source but has not been applied. The Phase 5 and Phase 6 audits must be run and reviewed before that migration is approved. Historical reconciliation remains conditional and has not been performed.

## 2. Original R4 Problem

Quiz submission originally persisted `QuizResult` rows without updating `UserWord.TotalAttempts`, `CorrectAnswers`, `LastReviewedAt`, or `LastCorrectAt`. Accuracy therefore read stale aggregates. The submit path also accepted or ignored malformed identifiers, allowed partial success when a session word became stale, lacked an explicit result/aggregate transaction boundary, and allowed concurrent requests to process the same in-memory session.

Historical data also lacked a durable result-level duplicate invariant, so existing rows cannot be assumed clean enough for either the new unique index or aggregate reconstruction.

## 3. Final Behavior

For every server-held question in a valid submitted quiz:

- one `QuizResult` is created;
- the exact server-held, authenticated-user-owned `UserWord.Id` is updated;
- `TotalAttempts` increments once;
- `CorrectAnswers` increments only when the selected server-owned option is correct;
- one UTC submission timestamp is used for results and aggregate timestamps; and
- response scoring, result rows, and aggregate changes use the same correctness decision.

The final `SubmitQuizAsync` flow is:

1. Require and find the process-local session.
2. Verify authenticated session ownership and expiry.
3. Atomically claim the session submission gate.
4. Validate the answer collection, question IDs, duplicate question rows, and option IDs.
5. Load the exact distinct session `UserWord.Id` values constrained by authenticated `UserId` and require a complete set.
6. Evaluate every server-side question and build untracked response/result templates using one UTC timestamp.
7. Enter the provider execution strategy and explicit database transaction.
8. Clear earlier validation tracking and reload the exact owner-constrained `UserWord` rows inside the transaction.
9. Apply counters/timestamps and add fresh `QuizResult` entities.
10. Call one `SaveChangesAsync` and commit.
11. Remove the local session only after commit.
12. On any uncommitted failure, clear tracking and release the gate in `finally`, leaving the session retryable.

No tracked learning-state mutation or durable write occurs before validation succeeds. A recognized database duplicate is rolled back, classified narrowly, and treated as an already-completed session.

## 4. Unanswered-Question Rule

R4 adopts Rule A: submission of the quiz makes every server-side question one attempt, including omitted questions.

An unanswered question:

- counts in the score denominator;
- creates an incorrect `QuizResult` with `UserAnswer = NULL`;
- increments `TotalAttempts` once;
- updates `LastReviewedAt`;
- does not increment `CorrectAnswers`; and
- preserves the prior `LastCorrectAt`.

Unknown question and option identifiers are malformed input, not unanswered questions, and reject the complete submission.

## 5. Validation and Ownership Protections

The client cannot nominate a `UserWord`, word, definition, or answer key. `UserWordId` and the correct option remain in private server session state.

Submission requires:

- a known, unexpired session owned by the authenticated user;
- no repeated submitted question ID;
- every submitted question to belong to that session;
- every submitted option to belong to its question; and
- every server-held session `UserWord.Id` to resolve through an exact query also constrained by authenticated `UserId`.

Fabricated, cross-session, invalid-option, stale-word, and cross-user submissions produce no result, counter, or timestamp mutation. A foreign user cannot claim or consume the owner's submission gate.

## 6. Counter and Timestamp Semantics

- `TotalAttempts` is the count of submitted/scored questions for that `UserWord`, including unanswered questions under Rule A.
- `CorrectAnswers` is the count of those attempts determined correct by server-held answer state.
- `LastReviewedAt` is the UTC timestamp of the latest counted attempt, regardless of correctness.
- `LastCorrectAt` is the UTC timestamp of the latest correct attempt and is unchanged by incorrect or unanswered attempts.

`AccuracyRate` continues to derive from persisted `CorrectAnswers / TotalAttempts`; R4 corrected its source data rather than changing the formula.

## 7. Transaction and Rollback Behavior

All `QuizResult` inserts and all affected `UserWord` counter/timestamp updates are made in one `SaveChangesAsync` inside one explicit transaction. The entire transaction delegate is executed through EF Core's provider execution strategy. Every delegate execution reloads database state and creates fresh result entities, preventing replay of previously incremented tracked objects.

An uncommitted save or commit failure disposes the transaction, clears the scoped context's changed tracking state, leaves no partial result/aggregate state, and releases the session gate. A later HTTP request uses a new scoped context and may retry the same valid session. Session removal occurs only after commit.

## 8. Duplicate and Concurrent Submission Protection

R4 uses two layers:

1. Each process-local session has an atomic `Available -> Submitting` claim implemented with `Interlocked.CompareExchange`. A simultaneous same-process request fails immediately instead of entering persistence. Validation and persistence failures release the claim; success removes the session.
2. The database model defines a unique result key on `(UserId, QuizSessionId, UserWordId)`. This is the durable final guard for races that bypass one process-local gate, including multiple application instances.

A sequential resubmission cannot find the successfully removed session. A concurrent same-process loser cannot enter persistence. A cross-process database loser receives a unique-key failure within the Phase 4 transaction, so its result inserts and aggregate changes roll back together.

Duplicate classification is intentionally narrow. It requires the expected index/column signature and a known provider duplicate code: SQL Server 2601/2627 or SQLite extended code 2067. Other `DbUpdateException` instances retain the ordinary persistence-failure behavior.

## 9. Database Uniqueness Constraint

The model defines the unique index:

```text
IX_QuizResults_UserId_QuizSessionId_UserWordId
(UserId, QuizSessionId, UserWordId)
```

The key is compatible with current quiz generation because a session selects each grouped vocabulary candidate at most once and therefore cannot legitimately repeat one `UserWordId`.

Migration `20260819000000_AddQuizResultSubmissionUniqueness` is narrowly scoped:

- `Up()` creates only this unique index with the three intended columns.
- `Down()` drops only this index.
- `ApplicationDbContextModelSnapshot` contains the matching unique index.
- No `UserWord` identity or unrelated schema change is included.

## 10. Historical Data Findings

Surviving, owner-consistent results contain enough fields to derive attempts, correct answers, last review, and last correct timestamps. They do not necessarily represent complete lifetime history.

Important limitations include historical duplicate submissions, possible result/word ownership mismatch, cascade-deleted result history when a `UserWord` was deleted, nonzero counters without surviving results, and pre-R4 behavior that persisted results without updating aggregates. The February 2026 session migration also grouped legacy rows by exact `AttemptedAt` alone, so its reconstructed session IDs can span unrelated users whose timestamps coincided.

Both historical audit files are read-only. Neither contains executable `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `CREATE`, `ALTER`, `DROP`, `TRUNCATE`, or automatic cleanup statements. They intentionally avoid returning answer text.

## 11. Historical Reconciliation Status

No historical reconciliation was implemented or executed. No production history or learning aggregate was modified.

The approved recommendation remains: **audit first, then conditionally reconcile**. Reconciliation requires real audit output, an approved duplicate/anomaly policy, assignment-based idempotent implementation, staging verification, before-value capture, and explicit authorization.

If history is materially incomplete or unreliable, a documented forward-only R4 deployment cutoff is acceptable. In that case, aggregates must not be represented as reconstructed lifetime totals.

## 12. R4/R5 Boundary

R4 identifies learning state using the exact private `UserWord.Id`. Submission does not locate aggregates by `(UserId, WordId, PartOfSpeechId)`, and `PartOfSpeechId` is not part of the result uniqueness key.

Existing pre-R4 coupling remains in vocabulary identity and quiz generation: the data model currently has a unique `(UserId, WordId, PartOfSpeechId)` index, and quiz generation groups candidates by `WordId` while choosing one `UserWord` from each group. R4 did not deepen or resolve that coupling. R5 remains responsible for moving to one `UserWord` per `(UserId, WordId)` and for any dependent result reassignment or merging.

## 13. R4/R12 Boundary

R4 retains the existing static, process-local session dictionary and adds only a narrow per-session submission gate. It did not add a `QuizSession` table, persisted questions, restart-safe sessions, resume behavior, durable expiry/completion state, response replay, or cleanup jobs.

The database uniqueness index protects result idempotency; it does not persist session lifecycle. R12 remains responsible for replacing the process-local architecture and providing restart/multi-instance session routing and recovery.

## 14. Test Coverage

The final `QuizApiTests` suite covers:

- correct answers and existing aggregate increments;
- incorrect answers and preservation of prior correct state;
- unanswered Rule A behavior;
- mixed correct/incorrect/unanswered outcomes;
- fabricated question IDs;
- questions from another session;
- duplicate submitted question rows;
- invalid option IDs;
- cross-user session access and owner retryability;
- stale/deleted `UserWord` rejection;
- sequential duplicate submission;
- persisted accuracy behavior;
- deterministic persistence failure rollback;
- retry of the same session after uncommitted failure;
- deterministic overlapping concurrent submissions;
- direct relational uniqueness enforcement;
- authenticated history isolation; and
- anonymous/unknown-session behavior.

The rollback test verifies zero failed-session results and unchanged counters/timestamps through fresh contexts before retrying successfully. The concurrency test pauses the winner at `SavingChangesAsync`, submits the same session while it is demonstrably in flight, and verifies one result/update set. No Phase 7 test gap required another test.

## 15. Verified Test Baseline

The human operator reported the following verified baseline after Phases 5 and 6:

```text
QuizApiTests:
22 passed
0 failed

Full backend suite:
152 passed
0 failed
```

Codex did not run these tests during Phase 7.

## 16. Migration Status

Source implementation is complete: the model, migration source, and snapshot contain the unique index.

Database deployment is incomplete: `20260819000000_AddQuizResultSubmissionUniqueness` has not been applied to an existing target database. R4 must not be described as fully deployed until the audits, approval gates, migration application, verification, compatible application deployment, and smoke checks are complete.

## 17. Deployment Prerequisites

1. Back up the target database.
2. Verify the restore/recovery procedure.
3. Record the R4 deployment source version and cutoff timestamp.
4. Run the Phase 5 duplicate audit.
5. Run the Phase 6 historical audit.
6. Review every blocking and ambiguous finding.
7. If duplicates exist, stop and approve a cleanup policy; do not let the migration choose implicitly.
8. If reconciliation is approved, implement and test it separately with assignment semantics and recovery evidence.
9. Re-run both audits after approved cleanup/reconciliation.
10. Require zero duplicate uniqueness groups.
11. Apply the Phase 5 migration in an approved non-production environment.
12. Verify the unique index exists and rejects only repeated user/session/word keys.
13. Apply the migration to production using the approved controlled process.
14. Deploy compatible application code.
15. Perform post-deployment smoke testing and monitoring.

None of these operational steps was executed by Codex.

## 18. Required Database Audit Procedure

Run, in order:

1. `docs/Updates/R4-Phase-5-Quiz-Result-Duplicate-Audit.sql`
2. `docs/Updates/R4-Phase-6-Historical-Quiz-Audit.sql`

Run them against a restored/staging copy first. Preserve counts and representative identifiers without exporting answer text or unnecessary user data. Treat duplicate uniqueness keys, ownership mismatches, or orphaned references as blockers. Review aggregate/timestamp mismatches and suspicious session shapes before approving any interpretation of history.

After any approved cleanup, run both scripts again. The Phase 5 duplicate query must return zero rows before unique-index application.

## 19. Remaining Operational Actions

- Run and approve both historical audits.
- Decide explicitly between conditional reconciliation and forward-only cutoff semantics.
- If necessary, design and approve duplicate cleanup; none exists in R4 source.
- Validate and apply the Phase 5 migration through the approved environment sequence.
- Verify index metadata and duplicate/different-key behavior on SQL Server.
- Deploy the compatible application.
- Run the backend regression commands and post-deployment checks.
- Record audit results, approvals, migration execution, deployment version, cutoff, and smoke-test evidence.

## 20. Known Limitations

- Sessions remain process-local and are lost on restart; R12 owns persistence and resume behavior.
- The local gate does not coordinate processes; the database index is required for the durable cross-instance guarantee.
- A client whose successful response is lost cannot replay and receive the original response; it receives a duplicate/not-found-style failure.
- Historical result data may be incomplete, duplicated, or ownership-inconsistent.
- Deleted result history cannot be reconstructed.
- Legacy `QuizSessionId` backfill used exact timestamps and does not prove original session boundaries.
- Both quiz directions continue to use the existing `QuizType.Definition`, and response time remains zero; these pre-existing history-fidelity limitations were outside R4.
- Current `UserWord` identity remains coupled to `PartOfSpeechId`; R5 owns that correction.
- SQL Server target-provider migration and duplicate-conflict behavior still require approved deployment validation.

## 21. Definition of Done

| Requirement | Status |
|---|---|
| New valid submissions keep per-word totals consistent with persisted results | Satisfied in source and verified integration tests |
| Results and aggregate changes commit or roll back together | Satisfied in source and rollback coverage |
| Sequential and concurrent submissions are idempotent | Satisfied in source/tests within current architecture; durable cross-instance protection depends on applying the migration |
| Rule A is documented and implemented | Satisfied |
| Validation and ownership failures produce zero mutation | Satisfied in source/tests |
| Correctness, malformed input, rollback, retry, concurrency, and uniqueness have integration coverage | Satisfied |
| Historical aggregates are reconciled | Intentionally deferred pending audit and approval |
| Phase 5 uniqueness migration is deployed to the target | Outstanding operational action |
| R5 identity and R12 persisted sessions | Intentionally deferred to their respective remediations |

R4 is implementation-complete. It becomes deployment-complete only after the outstanding database and operational steps are performed and recorded.

## 22. Files Changed Across R4

The completed R4 work is represented primarily by:

- `VocabularyApp.WebApi/Services/QuizService.cs`
- `VocabularyApp.Data/ApplicationDbContext.cs`
- `VocabularyApp.Data/Migrations/20260819000000_AddQuizResultSubmissionUniqueness.cs`
- `VocabularyApp.Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/QuizPersistenceFailureInterceptor.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/QuizSubmissionSynchronizationInterceptor.cs`
- `docs/Updates/R4-Phase-5-Quiz-Result-Duplicate-Audit.sql`
- `docs/Updates/R4-Phase-6-Historical-Quiz-Audit.sql`
- `docs/Updates/R4-Phase-6-Historical-Data-Audit-and-Reconciliation.md`
- `docs/Updates/R4-Quiz-Counter-Remediation-Implementation-Plan.md`
- `docs/Updates/R4-Quiz-Counter-Remediation-Completion.md`

No R5, R7, R8, R12, mastery, review-scheduling, progress-dashboard, or Angular implementation was introduced by R4.

## 23. Final Recommendation

Accept R4 as **implementation-complete but not deployment-complete**.

Proceed through the documented audit and migration approval sequence. Do not apply the unique index if duplicate groups exist, do not reconcile history without an approved anomaly policy, and use a documented forward-only cutoff if historical completeness cannot be established. Mark R4 fully deployed only after the target migration, index verification, compatible application deployment, regression tests, and smoke checks are complete.

### Post-deployment verification checklist

- A correct answer increments attempts and correct answers once and advances both timestamps.
- An incorrect answer increments attempts only, advances `LastReviewedAt`, and preserves `LastCorrectAt`.
- An unanswered question follows Rule A and persists one incorrect/null-answer result.
- Each submitted server question produces one history row.
- A sequential duplicate does not create results or aggregate increments.
- Controlled concurrent duplicate submissions produce one committed logical result set.
- The target database contains unique index `IX_QuizResults_UserId_QuizSessionId_UserWordId` on the intended columns.
- A controlled duplicate key is rejected while different valid keys remain accepted.
- Cross-user and malformed submissions remain mutation-free.
- Application logs contain no unexpected persistence, transaction, or uniqueness errors.

