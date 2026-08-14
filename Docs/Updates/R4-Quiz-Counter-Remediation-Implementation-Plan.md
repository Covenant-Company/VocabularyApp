# R4 Quiz Counter Remediation — Implementation Plan

## 1. Executive Summary

R4 is implementation-ready. The approved behavior is now unambiguous: every question in a submitted quiz is one review attempt; an omitted answer is an incorrect attempt; malformed question/option identifiers reject the entire submission; and durable duplicate protection will be enforced with a unique `QuizResult` index on `(UserId, QuizSessionId, UserWordId)` after production data is audited.

Implementation should remain in seven small phases. The sequence from the analysis remains sound, with one refinement: Phase 1 is explicitly a test-contract/red phase, and Phase 5 contains a mandatory audit/deployment gate before its migration can be applied. Each production phase must leave valid submissions coherent, malformed submissions mutation-free, and failed database work retryable. Historical reconciliation is a separate, approval-gated activity and is not part of live submission.

The core design is:

1. Use only the authenticated user, server-owned session, server-owned questions, and hidden `UserWord.Id` values as the trust chain.
2. Strictly validate all supplied identifiers before mutation. Omission is valid; fabrication is not.
3. Load only the exact distinct session `UserWordId` values, constrained by authenticated `UserId`, and require all to exist.
4. Capture one `DateTime.UtcNow` value and use it for result and aggregate timestamps.
5. Add one `QuizResult` and increment one tracked `UserWord` for every session question.
6. Save results and aggregates together inside one explicit transaction.
7. Use a minimal atomic in-process submission state plus the unique result index. A unique-key loser rolls back its aggregate changes as part of the same transaction.
8. Remove/complete the in-memory session only after commit; restore retryability after validation/database failure where nothing committed.

No production code, tests, migrations, database state, or Git state are changed by this planning task. Only this plan is created, and no tests are run.

## 2. Confirmed R4 Product and Engineering Decisions

### Unanswered questions: Rule A

For every question in a submitted session:

- increment `TotalAttempts` once;
- create one `QuizResult`;
- set `LastReviewedAt` to the quiz submission UTC timestamp;
- if no answer was supplied, persist it as incorrect with `UserAnswer = null`;
- do not increment `CorrectAnswers` for an unanswered question; and
- do not change its existing `LastCorrectAt`.

An omitted answer is valid. A submitted unknown question, question from another session, duplicate question row, or option not belonging to that question is malformed and rejects the whole submission.

### Durable duplicate protection is part of R4

After a duplicate-data audit, add a unique database index equivalent to:

```text
QuizResults(UserId, QuizSessionId, UserWordId)
```

This protects one result per word per user/session. It does not persist quiz sessions and does not implement R12.

### Historical handling is audit-first

Historical data is not rewritten automatically. Phase 6 first produces a read-only report. Reconciliation requires explicit approval after the report is reviewed. If approved, it assigns values derived from surviving results; it never adds derived values to existing counters.

### Smallest compatible API behavior

R4 will continue using the current `ServiceResult` and controller envelope. Malformed and duplicate submissions may return the current `400 Bad Request` style with a narrowly descriptive service message. R4 will not introduce `ProblemDetails`, controller-wide exception refactoring, or R7 contract work.

## 3. Current-State Defects Being Corrected

- `QuizService.SubmitQuizAsync` inserts `QuizResult` records but never changes `UserWord.CorrectAnswers`, `TotalAttempts`, `LastReviewedAt`, or `LastCorrectAt`.
- `UserVocabularyItemDto.AccuracyRate` consequently reads stale counters.
- unknown option IDs are accepted as incorrect/null answers;
- unknown and cross-session question IDs are silently ignored;
- duplicate submitted question IDs use the first value silently;
- the service queries all caller-owned `UserWords` instead of the exact session set;
- stale/deleted session words are skipped while other results commit and the response still succeeds;
- no explicit transaction defines the future result/aggregate unit of work;
- session lookup and removal are not an atomic submit claim;
- concurrent requests can insert duplicate result sets and would double-increment aggregates;
- the existing non-unique index `(UserId, QuizSessionId, AttemptedAt)` does not enforce idempotency; and
- historical result data may contain duplicates or ownership anomalies that prevent a blind index/backfill.

Two existing integration tests deliberately characterize behavior that R4 must reverse:

- `UnknownOptionIsAcceptedAsIncorrectWithoutTouchingOtherUsersData` must become a rejection/zero-mutation/retryability test.
- `AnswerForAnotherSessionIsIgnoredAndCurrentSessionIsScoredUnanswered` must become a rejection/zero-mutation/retryability test.

`ValidSubmissionPersistsCallerOwnedResultsAndDuplicateIsRejected` remains conceptually valid but must add aggregate assertions and later be complemented by deterministic concurrent coverage.

## 4. Target R4 Behavior

For a valid submission with `N` session questions:

- exactly `N` result rows exist for `(UserId, QuizSessionId)`;
- every result points to the exact server-held, caller-owned `UserWord.Id`;
- every affected row receives `TotalAttempts += 1` once;
- correct rows receive `CorrectAnswers += 1` and `LastCorrectAt = submittedAtUtc`;
- incorrect/unanswered rows preserve `CorrectAnswers` and the prior `LastCorrectAt`;
- every affected row receives `LastReviewedAt = submittedAtUtc`;
- every result receives the same `AttemptedAt = submittedAtUtc`;
- the response score and durable results agree;
- results and aggregate updates commit or roll back together; and
- no sequential or concurrent duplicate can make a second durable change.

Validation failures create no result, counter, or timestamp mutation and do not consume an otherwise valid session. A database failure creates no durable mutation and makes the session retryable. A successful commit consumes/removes the current process-local session.

## 5. Scope

R4 includes:

- submit payload/session validation;
- exact owner-constrained `UserWord` loading;
- scoring consistency under Rule A;
- result creation plus aggregate/timestamp mutation;
- explicit transactional persistence;
- a minimal in-process submission gate;
- the narrow result uniqueness index and migration;
- duplicate/conflict handling;
- focused backend tests and test seams;
- a read-only historical audit and optional separately approved reconciliation; and
- final R4 documentation.

## 6. Explicit Non-Goals

R4 does not include:

- one-`UserWord`-per-`(UserId, WordId)` migration (R5);
- API envelope/error standardization (R7);
- global exception handling (R8);
- a `QuizSession` table or persisted questions/options (R12);
- restart-resumable quizzes, durable expiry, cleanup jobs, or response replay;
- mastery, weakness, streak, scheduling, or spaced-repetition fields;
- new quiz modes, response-time correctness, or `QuizType` redesign;
- accuracy formula/presentation rounding;
- Angular redesign; or
- unrelated entity/model cleanup.

## 7. Architectural Constraints

- `QuizSessions` is a static `ConcurrentDictionary<Guid, QuizSessionState>` and remains so until R12.
- `QuizService` is scoped, so an in-process gate must live with static session state or inside each shared session object, not on the scoped service instance.
- Session/question IDs and option IDs are returned to the client; `UserWordId` and `CorrectOptionId` remain private server state.
- the production provider is SQL Server; integration tests use a shared SQLite in-memory database created with `EnsureCreated`.
- one HTTP request receives one scoped `ApplicationDbContext`.
- SQL Server `datetime2` does not preserve `DateTime.Kind`; code must create UTC values consistently and tests should verify the instant/value rather than depend solely on `Kind` after reload.
- the existing generator groups candidates by `WordId`, selects one `UserWord`, and creates at most one question for it in a session. Therefore the approved unique key does not reject a legitimate current quiz.
- `QuizResult.UserId` and `UserWordId` are independent foreign keys; service ownership validation remains mandatory.
- EF execution strategy behavior must be considered when adding an explicit transaction. Do not manually begin a transaction outside a retrying execution-strategy delegate if the configured provider strategy requires the complete operation to be retried as a unit.

## 8. R4/R5 Boundary

All R4 aggregate work must use the exact hidden `QuizQuestionState.UserWordId`, then load it with:

```csharp
requiredIds.Contains(uw.Id) && uw.UserId == userId
```

R4 must not find or update aggregates by `(UserId, WordId, PartOfSpeechId)`. It must not add `PartOfSpeechId` to the result uniqueness key or new quiz logic. This deliberately avoids deepening the current identity model and allows R5 to merge/reassign `UserWord` rows and dependent results later.

The current `StartQuizAsync` grouping by `WordId` is relevant only to proving that one session does not legitimately repeat a `UserWordId`. R4 should not refactor that grouping or resolve the wider duplicate-meaning behavior.

## 9. R4/R12 Boundary

R4 may add a small atomic state to the existing private `QuizSessionState`, for example `Available -> Submitting`, using `Interlocked.CompareExchange`. A successful submit removes the session after commit; failure returns it to `Available`; a simultaneous request fails without entering the database path.

R4 may also enforce one result per `(UserId, QuizSessionId, UserWordId)` in the database. This is event idempotency, not session persistence.

R4 must not create `QuizSession`/`QuizQuestion` tables, persist answer keys, survive restart, resume sessions, persist expiry/completion, add session cleanup jobs, or support cross-instance session routing. Those remain R12.

## 10. Transaction and Idempotency Strategy

### Validation before mutation

Validate the request shape and server session relationships first. Acquire the in-process submission claim before database work, but release it on every pre-commit failure. Do not modify tracked aggregates until all submitted IDs and all exact caller-owned `UserWord` rows are valid.

### Database unit of work

The preferred operation is one explicit transaction containing:

1. optional database duplicate precheck for a clearer response (not relied on for race safety);
2. exact tracked `UserWord` load constrained by owner;
3. one common UTC timestamp capture;
4. construction of all results;
5. tracked aggregate/timestamp changes;
6. one `SaveChangesAsync`; and
7. commit.

One save is sufficient to send inserts and updates as a single EF unit. The unique index is the final race-safe arbiter. If another writer has already inserted a matching key, the losing save/transaction must roll back its `UserWord` updates along with its failed result inserts.

### Execution strategies and retry safety

At implementation time, inspect `_db.Database.CreateExecutionStrategy()`. If SQL Server retries are enabled, execute the entire transaction delegate—including duplicate check, reload, increments, inserts, save, and commit—inside `ExecuteAsync`. Each retry must use database-reloaded aggregate values, not reuse already-incremented tracked instances. Clear/recreate tracking as necessary between retries. The unique key must participate in the same transaction so a retry cannot commit a second logical submission.

SQLite tests may not exercise SQL Server retry behavior. Add unit/integration coverage for rollback and unique conflicts, and include target-provider manual verification.

### Session completion ordering

- malformed/ownership failure: release `Submitting -> Available`; keep session usable;
- database failure before commit: rollback/dispose transaction, reset tracking as needed, release to `Available`;
- unique conflict/already durable: do not increment; treat as duplicate and remove/complete the local session because the database proves the logical result exists;
- successful commit: remove/complete session only after commit;
- simultaneous local request: return the smallest compatible failure (“submission already in progress” or equivalent), without consuming or mutating anything.

## 11. Database Migration Strategy

### Pre-migration audit

Before generating or applying the migration, run a read-only duplicate query against the target database:

```sql
SELECT UserId, QuizSessionId, UserWordId, COUNT(*) AS DuplicateCount,
       MIN(Id) AS FirstResultId, MAX(Id) AS LastResultId
FROM QuizResults
GROUP BY UserId, QuizSessionId, UserWordId
HAVING COUNT(*) > 1
ORDER BY DuplicateCount DESC, UserId, QuizSessionId, UserWordId;
```

Also count affected rows/groups and retain the report. A duplicate is more than one result for the same user, session, and `UserWord`.

### Expected schema change

Change `ApplicationDbContext` to add a unique index on `{ UserId, QuizSessionId, UserWordId }`. Keep or deliberately assess the existing analytics index `{ UserId, QuizSessionId, AttemptedAt }`; the new unique index serves a different purpose, so do not remove it without query-plan evidence. Generate one migration containing only the new unique index and corresponding snapshot/designer changes.

### Behavior when duplicates exist

Do not apply the unique index while duplicates remain. Stop deployment, review the audit, and choose a separately approved cleanup based on result/session evidence. The migration must not silently delete or merge result rows. Prefer a pre-deployment gate and, if practical, a migration guard that throws a clear error before index creation when duplicates exist.

Any cleanup must decide how duplicate result removal affects already-existing counters. Because current production code does not update counters, result cleanup and historical reconciliation still remain separate decisions.

### Migration verification and rollback

After application in a staging/target-provider database:

- verify the index exists, is unique, and has columns in the intended key;
- verify inserting a duplicate key fails;
- verify different `UserWordId` values in one session succeed;
- verify the existing quiz-history query remains supported/functionally correct; and
- verify the migration contains no unrelated schema changes.

Rollback drops only the new unique index. Rolling back reopens the duplicate risk and should be paired with application rollback/traffic control. It does not restore deleted data because the migration itself must not delete data.

### Production ordering

1. Back up/confirm recovery capability.
2. Run and review duplicate and ownership audits.
3. Resolve blockers under explicit approval.
4. Apply the narrow migration during controlled deployment.
5. Verify the unique index.
6. Deploy compatible application logic, or deploy in an ordering where old code remains valid with the new constraint. Because old concurrent submissions could hit the constraint and return a generic failure, minimize the gap.
7. Run focused smoke tests and monitor unique-conflict/error logs.

## 12. Historical Data Strategy

Phase 6 begins read-only and remains separate from request handling and the uniqueness migration’s minimum prerequisite.

Audit:

- duplicate `(UserId, QuizSessionId, UserWordId)` groups;
- `QuizResult.UserId != UserWord.UserId` joins;
- per-`UserWord` stored counters/timestamps versus result-derived values;
- count/rate of `UserAnswer IS NULL`, split by correctness where useful;
- sessions with suspicious repeated words, mixed users, unusually large counts, or inconsistent timestamps;
- the session-ID backfill limitation (legacy grouping by exact `AttemptedAt`);
- cascade-deleted history that cannot be observed/reconstructed; and
- current `UserWord` rows with nonzero counters but no surviving results.

Under Rule A, the reconstruction formula is:

```text
TotalAttempts  = COUNT(QuizResult rows for UserWordId)
CorrectAnswers = COUNT(rows where IsCorrect = true)
LastReviewedAt = MAX(AttemptedAt)
LastCorrectAt  = MAX(AttemptedAt where IsCorrect = true), otherwise null
```

If approved, reconciliation assigns those values. It does not increment current values. The operation must be rerunnable with no further change for unchanged history, must report counts before/after, and should be delivered as a separately reviewed maintenance operation or migration step with backup/rollback guidance.

**Approval gate: do not perform historical reconciliation unless the audit results are reviewed and explicitly approved.**

If history is not trustworthy, document a deployment cutoff and begin accurate live counting after that instant. Decide whether pre-cutoff displayed counters remain unchanged, are reset, or are labeled as incomplete; do not silently claim reconstructed lifetime accuracy.

## 13. Existing Test Infrastructure to Reuse

- `VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs`: existing API-level quiz coverage and helpers.
- `VocabularyAppWebApplicationFactory`: authenticated in-process host, shared relational SQLite database, and service replacement point.
- `ApiTestClientHelper`: registration/login/bearer clients and two-user setup.
- `IntegrationTestSeeder`: word/definition/`UserWord` creation and initial `correctAnswers`/`totalAttempts` support.
- `QuizApiCollection` and `QuizApiTestBase`: serialization and static-session cleanup.
- `RelationalDatabaseFixture`: multiple contexts plus EF interceptor attachment.
- existing `SaveChangesInterceptor` patterns in authentication tests: model for an R4 failure seam.
- existing separate-context concurrency tests: model for concurrent access, supplemented with a deterministic barrier for simultaneous HTTP submits.

For deterministic correct/incorrect answers, test helpers should retain each seeded word text, definition, and `UserWordId`. Start quizzes in `word-to-definition` mode, parse the word from the known prompt or map the prompt to the seeded record, then choose the option whose text equals the seeded definition for correct, and a different valid option for incorrect. This avoids exposing the private answer key or changing production APIs. A narrow internal session test accessor is a fallback only if prompt mapping proves too brittle.

The API factory will likely need an optional test-only EF interceptor/service-configuration hook for save failure and a submission synchronization hook for deterministic concurrency. Keep these test seams opt-in so unrelated tests are unchanged.

## 14. Implementation Phase Overview

| Phase | Coherent checkpoint | Primary outcome |
|---|---|---|
| 1 | Behavioral contract/tests | R4 expectations are explicit; known current failures are recorded. |
| 2 | Strict validation/ownership | Malformed/stale/foreign submissions produce zero mutation and remain retryable. |
| 3 | Counters/timestamps | A valid submission updates each word correctly using one UTC instant. |
| 4 | Transaction/rollback | Result and aggregate writes succeed or fail together. |
| 5 | Duplicate/concurrency + migration | In-process races are gated and database uniqueness prevents durable duplicates. |
| 6 | Historical audit/optional reconciliation | History quality is reported; rewriting requires a separate approval. |
| 7 | Regression/documentation | R4 is proven complete without R5/R12 expansion. |

Phase 1 is intentionally expected to produce failing R4 assertions against current production behavior. It may be committed as an explicit red test specification if the repository workflow accepts red-phase commits; otherwise review it without merging and implement Phase 2/3 immediately before the first green checkpoint. No phase should be represented as passing until the user manually runs the listed tests.

## 15. Phase 1 — Behavioral Contract and Tests

### Objective

Establish deterministic executable specifications for all approved R4 behavior before production changes.

### Why This Phase Comes Here

Current tests encode two defective trust-boundary behaviors and do not inspect aggregates. Changing them first makes later diffs demonstrably behavior-driven and prevents accidental preservation of accepted-invalid input.

### Files Expected to Change

- `VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/IntegrationTestSeeder.cs`
- optionally a new `Infrastructure/QuizTestData.cs` or focused quiz assertion/helper file
- optionally `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs` only to prepare opt-in hooks; prefer deferring functional hooks to Phases 4/5

No production or migration file changes.

### Detailed Implementation Steps

1. Refactor only test helpers so seeded quiz vocabulary returns word text, definition, and `UserWordId` mappings.
2. Add deterministic helpers to start a fixed `word-to-definition` quiz and choose a known correct or known incorrect valid option.
3. Add fresh-scope database assertion helpers that read `QuizResult` and `UserWord` after HTTP calls.
4. Add tests for correct, incorrect, preexisting `3/5` counters, unanswered/empty answers, and several independently updated words.
5. Add validation specifications for unknown question, cross-session question, duplicate question IDs, invalid option, cross-user session, and stale/deleted `UserWord`.
6. For each rejection test, assert zero results, unchanged counters/timestamps, and a subsequent valid retry when the session should remain usable.
7. Extend sequential duplicate assertions to include counters/timestamps.
8. Add accuracy verification through the vocabulary API after known aggregate updates (`4/6` yields approximately `66.666...`; use an appropriate floating tolerance).
9. Define/sketch concurrency and failure tests now, but add complex opt-in synchronization/failure infrastructure in their owning phases if implementing it here would obscure the contract-only diff.

### Tests to Add or Modify

- correct answer: one event, `+1/+1`, both timestamps;
- incorrect answer: one event, attempts only, review timestamp only;
- existing counts: `3/5 -> 4/6` and separately `3/5 -> 3/6`;
- unanswered: incorrect/null result, attempts/review only;
- mixed result quiz: per-word independent deltas;
- invalid/foreign/duplicate question IDs: `400`, zero mutations, valid retry works;
- invalid option: `400`, zero mutations, valid retry works;
- cross-user: zero mutation for both, owner retry works;
- stale word: entire submission fails with zero mutation to surviving words;
- sequential duplicate: exactly one event/delta set;
- accuracy DTO: persisted counters drive expected value.

### Existing Tests Affected

- Rewrite `UnknownOptionIsAcceptedAsIncorrectWithoutTouchingOtherUsersData` to expect rejection, zero mutation, and retryability.
- Rewrite `AnswerForAnotherSessionIsIgnoredAndCurrentSessionIsScoredUnanswered` similarly.
- Extend `AnotherUserCannotSubmitOwnedSessionAndOwnerCanStillSubmitIt` with aggregate/timestamp zero-mutation checks.
- Extend `ValidSubmissionPersistsCallerOwnedResultsAndDuplicateIsRejected` with exactly-once aggregate checks.
- Retain anonymous, answer-key non-disclosure, unknown-session, and history isolation tests.

### Manual Tests to Run

After reviewing the test-only diff:

```powershell
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~QuizApiTests"
```

Expected at this red phase: legacy unaffected tests pass; new counter/validation/stale tests fail in the documented ways. If the project requires a green main branch, do not commit/merge Phase 1 independently—continue directly to the production phase that satisfies each group.

### Expected Results

The failure list should correspond to current defects, not test nondeterminism. Correct-option selection must be repeatable despite option shuffling.

### Risks

- brittle prompt parsing;
- accidental reliance on option position;
- shared static sessions leaking across tests;
- treating red tests as a production regression rather than a planned specification.

### Rollback/Recovery Considerations

Test-only changes can be reverted independently. Preserve existing valid security/history coverage. If deterministic prompt mapping is brittle, replace only the helper with a narrow internal accessor rather than weakening assertions.

### Definition of Done

Every required behavior has a named deterministic test/specification, the two defective tests are identified for reversal, and observed red failures match repository evidence.

### Suggested Commit Message

`R4 Phase 1 - Define quiz counter remediation behavior`

Use only if intentional red commits are accepted; otherwise combine the first green checkpoint with the satisfying production phase.

### Codex Implementation Prompt Requirements

- State “tests only; do not change production code.”
- Require deterministic correct/incorrect option selection without exposing the answer key.
- List every scenario and exact database assertions above.
- Require rewriting the two named defective tests.
- Instruct Codex not to run tests; the user will run the focused command.
- Require reporting expected red failures and all changed test files.

## 16. Phase 2 — Strict Validation and Ownership

### Objective

Reject malformed/session-inconsistent input and stale/foreign `UserWord` state before constructing results or mutating tracked aggregates.

### Why This Phase Comes Here

All-or-nothing validation must be correct before counter updates are introduced; otherwise Phase 3 would make currently tolerated manipulation alter learning state.

### Files Expected to Change

- `VocabularyApp.WebApi/Services/QuizService.cs`
- `VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs`
- possibly `VocabularyApp.WebApi/DTOs/QuizDTOs.cs` only if a minimal nullable contract adjustment is needed; prefer service validation over broad API changes

### Detailed Implementation Steps

1. Treat `request.Answers == null` as malformed and return failure without consuming the session.
2. After owner/expiry checks, verify submitted `QuestionId` values are unique; reject duplicates.
3. Build a dictionary of server session questions and reject every submitted question ID absent from it. This rejects fabricated and cross-session IDs.
4. For each supplied answer, require `SelectedOptionId` to match an option on that exact server question. Reject invalid option IDs.
5. Do not require an answer for every session question. Missing entries are valid unanswered questions under Rule A.
6. Derive exact distinct `UserWordId` values only from `session.Questions`.
7. Query tracked rows with both required-ID membership and `uw.UserId == userId`; do not query all caller vocabulary.
8. Require returned distinct count/set equality with required IDs. If any is deleted or ownership-invalid, fail the entire submission.
9. Perform these checks before `QuizResult` construction and before any counter mutation.
10. On all validation failures, return the smallest current-compatible service failure and leave the session available. Avoid answer-key or foreign-data detail in messages/logs.

### Tests to Add or Modify

Make Phase 1 validation tests green: null answers, duplicate question, unknown question, foreign-session question, invalid option, stale word, and cross-user cases. Assert a valid retry succeeds after malformed payloads.

### Existing Tests Affected

The two named accepted-invalid tests now permanently expect rejection. `AnotherUserCannotSubmitOwnedSessionAndOwnerCanStillSubmitIt` should remain green. Valid unanswered tests must prove omission is not mistaken for malformed input.

### Manual Tests to Run

```powershell
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~QuizApiTests&FullyQualifiedName~Invalid|FullyQualifiedName~QuizApiTests&FullyQualifiedName~AnotherSession|FullyQualifiedName~QuizApiTests&FullyQualifiedName~CrossUser|FullyQualifiedName~QuizApiTests&FullyQualifiedName~Stale|FullyQualifiedName~QuizApiTests&FullyQualifiedName~DuplicateSubmitted"
```

Because filter-name composition is sensitive to final test names, the reliable fallback is:

```powershell
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~QuizApiTests"
```

### Expected Results

All validation/ownership cases pass. Counter behavior tests may remain red until Phase 3, but no accepted malformed request may persist a result.

### Risks

- accidentally rejecting a valid omitted question;
- mutating/consuming session before validation completes;
- comparing counts without detecting duplicate IDs in server state;
- leaking whether a foreign session/question exists; and
- attaching broad model validation that changes unrelated controller behavior.

### Rollback/Recovery Considerations

Rollback is limited to service validation/test expectation changes. No schema/data changes occur. If a compatibility issue appears, retain ownership/exact-row validation and adjust only the narrow request-shape rule after review.

### Definition of Done

Every invalid identifier and stale/owner mismatch causes zero database mutation, omissions remain valid, exact rows are owner-constrained, and failed validation leaves the session retryable.

### Suggested Commit Message

`R4 Phase 2 - Validate quiz submissions before persistence`

### Codex Implementation Prompt Requirements

- Limit production changes to strict validation/exact loading.
- Specify Rule A omission versus malformed identifier distinction.
- Require exact `UserWord.Id` plus authenticated `UserId`; forbid part-of-speech identity logic.
- Require zero mutation and session retryability tests.
- Name the two legacy tests to rewrite.
- Forbid counters, transaction, gate, migration, R5, and R12 work in this phase.
- Instruct Codex not to run tests and provide the user’s focused command.

## 17. Phase 3 — Counters and Review Timestamps

### Objective

Correct the live learning aggregates for every accepted question using tracked owner-validated `UserWord` rows and one UTC timestamp.

### Why This Phase Comes Here

Strict validation is already established, so accepted inputs can safely affect learning state. Transaction mechanics are isolated to Phase 4 for a smaller review diff, while this phase must still use one `SaveChangesAsync` so current relational save atomicity is preserved.

### Files Expected to Change

- `VocabularyApp.WebApi/Services/QuizService.cs`
- `VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs`
- optionally `IntegrationTestSeeder.cs` for initial timestamps as well as counters

### Detailed Implementation Steps

1. Retain tracked exact `UserWord` entities in a dictionary keyed by `Id`.
2. After all validation succeeds, capture one `attemptedAtUtc = DateTime.UtcNow`.
3. Iterate every server session question, not merely submitted answer rows.
4. Determine `hasAnswer`, selected valid option, and correctness from the validated answer lookup and hidden correct option.
5. Create exactly one `QuizResult` per question. For omission, set `IsCorrect = false` and `UserAnswer = null`.
6. For its tracked `UserWord`, always execute `TotalAttempts += 1` and `LastReviewedAt = attemptedAtUtc`.
7. Only when correct, execute `CorrectAnswers += 1` and `LastCorrectAt = attemptedAtUtc`.
8. For incorrect/unanswered, do not assign `LastCorrectAt`; preserve its prior value.
9. Never assign absolute counter values in the live path.
10. Add all results and call `SaveChangesAsync` once. Do not add mastery/scheduling logic.
11. Build/return scoring from the same per-question evaluation used for persistence to avoid divergent duplicate calculations.

### Tests to Add or Modify

Make correct, incorrect, unanswered, mixed, timestamp, existing-count, history-consistency, and accuracy tests green. Verify all results and changed rows use one timestamp. Use tolerances/ranges only where the clock cannot be injected; prefer equality between persisted result and aggregate timestamps.

### Existing Tests Affected

Extend valid submission/history tests to assert counters. Existing history count/score behavior should remain unchanged. No DTO formula change is expected.

### Manual Tests to Run

```powershell
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~QuizApiTests"
```

Optional manual API/UI smoke after focused tests:

1. Start a one-question quiz from four saved words.
2. Submit one known answer.
3. call `GET /api/words/vocabulary` (using the repository’s actual vocabulary route) and inspect that word’s counters/accuracy.
4. Repeat with an incorrect answer and confirm `LastCorrectAt` is preserved in the database even though it is not exposed by `UserVocabularyItemDto`.

### Expected Results

Valid submissions now create matching event/aggregate state. `3/5` becomes `4/6` or `3/6` as appropriate. Rule A is consistent across result, score, and aggregate.

### Risks

- incrementing the same tracked word twice if session generation ever repeats `UserWordId`;
- overwriting `LastCorrectAt` on incorrect answers;
- separate score/persistence correctness calculations drifting;
- timestamp assertions depending on SQLite `DateTime.Kind`; and
- session removal after a save failure still requiring Phase 4 hardening.

### Rollback/Recovery Considerations

This phase changes live data semantics. Rollback stops future increments but does not undo already-written correct aggregates/results. During staged implementation, use non-production data until Phase 4/5 are complete. Any data correction must be explicit, never an automatic rollback side effect.

### Definition of Done

Every valid question produces one result and the exact Rule A aggregate/timestamp delta, prior counts are incremented rather than replaced, and all Phase 1 functional counter tests pass manually.

### Suggested Commit Message

`R4 Phase 3 - Update quiz counters and review timestamps`

### Codex Implementation Prompt Requirements

- Require tracked exact `UserWord` entities from Phase 2.
- State the always/correct-only/incorrect rules explicitly.
- Require one UTC timestamp shared across results and rows.
- Require one result per session question and one save.
- Forbid transaction/gate/migration/backfill/future learning fields in this phase.
- Require focused tests but instruct Codex not to execute them.

## 18. Phase 4 — Transactional Persistence and Rollback

### Objective

Make all result inserts and all aggregate/timestamp updates an explicit all-or-nothing database transaction and prove failed work leaves the session retryable.

### Why This Phase Comes Here

The complete mutation set exists after Phase 3, so the transaction boundary can be reviewed against real operations before concurrency and unique-conflict handling are layered on.

### Files Expected to Change

- `VocabularyApp.WebApi/Services/QuizService.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs` or a new opt-in test factory subclass/configuration hook
- a new test-only save/command/transaction failure interceptor as needed
- `VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs` or a focused `QuizSubmissionTransactionTests.cs`

### Detailed Implementation Steps

1. Put exact-row load, mutation, result insertion, one save, and commit in one explicit transaction scope.
2. Keep request/session identifier validation before entity mutation. Recheck database-owned rows inside the transaction.
3. Integrate with `CreateExecutionStrategy().ExecuteAsync` if the provider strategy retries; make the whole transaction one retriable delegate.
4. Ensure each retry reloads unmodified database state and does not increment a previously incremented tracked entity a second time.
5. Commit before consuming/removing the process-local session.
6. On save/commit exception, roll back/dispose, clear poisoned tracking if the scoped context could be reused, return the narrow existing failure, and preserve/release session retryability.
7. Add an opt-in test failure seam that triggers during quiz persistence without affecting seeding/login setup. Prefer a controllable interceptor armed immediately before submit.
8. After injected failure, query with a fresh scope/context and assert no event, counter, or timestamp change; disarm and retry the same session successfully.

### Tests to Add or Modify

- save failure rolls back results and all aggregate fields;
- multi-question failure rolls back every row, not a subset;
- same session succeeds after the failure seam is removed;
- successful path still commits all changes;
- if commit failure can be injected reliably, cover it separately; otherwise document provider/test limitation and verify manually against target provider.

### Existing Tests Affected

All Phase 1–3 tests should remain green. Reuse the existing `SaveChangesInterceptor` pattern, but do not globally modify unrelated authentication tests or their fixture behavior.

### Manual Tests to Run

```powershell
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~QuizSubmissionTransactionTests|FullyQualifiedName~QuizApiTests"
```

Then run the backend regression suite:

```powershell
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj
```

### Expected Results

The injected failure leaves the database identical to its pre-submit learning state, and the same session can be submitted successfully after the fault is removed.

### Risks

- user-started transaction incompatible with a retrying execution strategy;
- reused changed tracked entities causing replayed increments;
- interceptor firing during setup rather than submit;
- in-memory session consumed before commit; and
- SQLite behavior differing from SQL Server commit/locking behavior.

### Rollback/Recovery Considerations

Rollback the transaction wrapper and opt-in test seam together if needed; the Phase 3 one-save behavior remains the fallback. No schema change occurs. Do not deploy the fallback as fully R4-complete because explicit failure/retry guarantees would be absent.

### Definition of Done

One explicit transaction covers the complete mutation set, a controlled failure leaves zero durable mutation, retry succeeds, and relevant/full backend tests pass when manually run.

### Suggested Commit Message

`R4 Phase 4 - Make quiz learning updates transactional`

### Codex Implementation Prompt Requirements

- Define the exact transaction boundary and commit/session ordering.
- Require execution-strategy-safe implementation and freshly loaded state on retry.
- Add a narrowly armed failure seam and fresh-context rollback assertions.
- Require failed-session retry coverage.
- Forbid uniqueness migration, concurrency gate, and historical work.
- Instruct Codex not to run tests; list focused and full commands for the user.

## 19. Phase 5 — Duplicate and Concurrent Submission Protection

### Objective

Prevent sequential and concurrent double counting with a minimal in-process submit claim and the approved unique result invariant.

### Why This Phase Comes Here

The unique constraint is safe only after duplicate audit, and its conflict must roll back the complete transaction from Phase 4. The gate is easier to reason about once success/failure session ordering is established.

### Files Expected to Change

- `VocabularyApp.WebApi/Services/QuizService.cs`
- `VocabularyApp.Data/ApplicationDbContext.cs`
- new `VocabularyApp.Data/Migrations/<timestamp>_AddQuizResultSubmissionUniqueness.cs`
- generated migration designer and `ApplicationDbContextModelSnapshot.cs`
- `VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs` and/or new concurrency tests
- `VocabularyApp.WebApi.Tests/Infrastructure/QuizApiCollection.cs` or a narrow concurrency synchronization helper
- optional read-only SQL audit file under `Docs/Updates` or `VocabularyApp.Data` if the repository’s deployment convention approves it

### Detailed Implementation Steps

#### Layer 1: in-process gate

1. Add the smallest private submission state to `QuizSessionState`, initially available.
2. Use `Interlocked.CompareExchange` (or an equally atomic primitive) to transition `Available -> Submitting` after session owner/expiry checks and before validation/database work.
3. If already submitting, reject the second request immediately with a narrow compatible failure and no database access/mutation.
4. On malformed input, stale ownership, or rolled-back database failure, transition back to available if the session is still valid.
5. On successful commit or a confirmed durable duplicate, remove/complete the dictionary entry.
6. Ensure `finally` logic cannot reopen a successfully committed session. Track commit/completion explicitly.
7. Update expiry cleanup so it does not leak gate state. An expired available session can be removed; a request holding a submitting object may finish/rollback safely, but it must not be reintroduced after expiry.
8. Keep `ClearQuizSessionsForTesting` effective; no new unbounded parallel dictionary/timer framework.

#### Layer 2: database idempotency

1. Run the duplicate audit before migration creation/application and retain results.
2. Confirm no current generator/session intentionally repeats a `UserWordId`; add a test asserting uniqueness of generated question mappings if an internal accessor is available, or prove through result/index integration tests.
3. Configure unique index `{ UserId, QuizSessionId, UserWordId }`.
4. Generate a migration containing only this index/snapshot update.
5. If duplicates exist, stop. Do not make the migration delete data. Resolve under a separate approved cleanup.
6. Add an optional precheck for existing session results to return a clear duplicate response, while treating the unique constraint—not the precheck—as race protection.
7. Recognize the specific unique-key `DbUpdateException` narrowly. Never swallow unrelated constraint/database failures as duplicates.
8. Ensure the conflicting transaction rolls back all aggregate updates. A duplicate loser must not retry non-idempotent increments as though transient.
9. Mark/remove the local session after a confirmed durable duplicate; there is nothing safe to retry for that logical session.
10. Log the user/session duplicate event without answer keys or sensitive payloads.

### Tests to Add or Modify

- sequential duplicate produces one result set and one aggregate delta;
- two requests released through a deterministic barrier produce exactly one committed logical submission;
- the other request is in-progress/duplicate failure, not a second successful mutation;
- a direct relational duplicate key insert fails;
- same user/session with different `UserWordId` succeeds;
- same `UserWordId` in a different session succeeds;
- unique conflict in submit rolls back aggregate changes;
- failed non-duplicate submission reopens the local gate;
- successful submission does not reopen it;
- expired/cleanup path does not leak a permanent submitting state.

### Existing Tests Affected

`ValidSubmissionPersistsCallerOwnedResultsAndDuplicateIsRejected` remains but gains exact counter assertions. Static-session test serialization remains for ordinary tests; the dedicated concurrency test intentionally creates two simultaneous requests within one test.

### Manual Tests to Run

Pre-migration audit against the intended database (replace the connection workflow with the project’s approved database client):

```sql
SELECT UserId, QuizSessionId, UserWordId, COUNT(*) AS DuplicateCount
FROM QuizResults
GROUP BY UserId, QuizSessionId, UserWordId
HAVING COUNT(*) > 1;
```

Inspect the generated migration without applying it, then manually run:

```powershell
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~QuizApiTests|FullyQualifiedName~QuizSubmissionConcurrencyTests"
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj
```

For migration validation in an approved non-production SQL Server database:

```powershell
dotnet ef database update --project .\VocabularyApp.Data\VocabularyApp.Data.csproj --startup-project .\VocabularyApp.WebApi\VocabularyApp.WebApi.csproj
```

Then inspect the index metadata and perform the controlled duplicate/different-key checks described in Section 11. Do not run the migration against production until the audit is clean and deployment is approved.

### Expected Results

Exactly one concurrent request changes the database; all other attempts leave counters untouched. The new index rejects only repeated user/session/word results. All backend regression tests remain green.

### Risks

- historical duplicates block migration;
- broad `DbUpdateException` handling hides genuine failures;
- gate is not reset after rollback or is reset after commit;
- execution strategy retries a duplicate loser incorrectly;
- test barrier does not actually overlap requests;
- SQLite and SQL Server report unique violations differently; and
- old application instances briefly encounter the new constraint during rolling deployment.

### Rollback/Recovery Considerations

Application rollback must be coordinated with index rollback. Keeping the index while rolling back application code is data-safe but old concurrent requests may receive generic failures. Dropping it restores prior duplicate risk. The migration rollback drops only the new index. No automatic duplicate deletion belongs in rollback.

### Definition of Done

The audit is clean/approved, migration is narrow, unique conflict handling is specific, local gating restores retryability correctly, and sequential/concurrent/manual target-provider tests prove one durable result/delta per logical question.

### Suggested Commit Message

`R4 Phase 5 - Prevent duplicate quiz submissions`

If operational audit artifacts and migration policy warrant separation, use two commits after verification:

- `R4 Phase 5A - Gate concurrent quiz submissions`
- `R4 Phase 5B - Enforce quiz result submission uniqueness`

### Codex Implementation Prompt Requirements

- Require the two-layer design and forbid persisted sessions.
- Specify atomic state transitions and every success/failure reset rule.
- Require the exact unique key without `PartOfSpeechId`.
- Require read-only audit before migration and no automatic cleanup.
- Require provider-specific narrow unique-conflict recognition and transactional rollback.
- Require deterministic concurrent tests and direct uniqueness tests.
- Instruct Codex not to run tests/migration; provide commands for user execution.

## 20. Phase 6 — Historical Audit and Conditional Reconciliation

### Objective

Measure historical reliability, report anomalies, and prepare—but do not execute without approval—an idempotent reconciliation option.

### Why This Phase Comes Here

Live writes are now correct and protected, providing a stable deployment cutoff. Historical work can be evaluated without moving the live request path or blocking its correctness, except that duplicate cleanup needed for Phase 5’s index must occur earlier under its own explicit gate.

### Files Expected to Change

Read-only audit subphase:

- a documented SQL audit script/report location agreed for the repository, such as `Docs/Updates/R4-Historical-Quiz-Data-Audit.sql`
- `Docs/Updates/R4-Historical-Quiz-Data-Audit-Results.md` populated by the user/operator after execution

Optional reconciliation subphase, only after approval:

- separately scoped SQL/maintenance operation;
- tests for derivation/idempotency;
- deployment documentation.

Do not modify `SubmitQuizAsync` for historical reconciliation.

### Detailed Implementation Steps

1. Establish/document the R4 deployment cutoff timestamp and application version/commit.
2. Create read-only queries for duplicate result keys, owner mismatch, stored-versus-derived aggregates, null answers, suspicious session sizes/repetitions/timestamps, and nonzero counters without results.
3. Report counts and representative IDs without exposing sensitive answer text unnecessarily.
4. Explain unmeasurable cascade-deleted history and the legacy timestamp-to-session backfill limitation.
5. Classify anomalies as blocking, explainable, repairable, or irrecoverable.
6. Stop for explicit review and approval.
7. If approved, implement assignment-based reconciliation with owner-consistent, deduplicated/approved source rows.
8. Capture before/after values and affected row counts; make a second run a no-op.
9. If not approved, document cutoff-based forward-only accuracy and the chosen handling of old counter values.

### Tests to Add or Modify

For an approved reconciliation operation only:

- zero results derives `0/0` and null timestamps according to the approved reset policy;
- mixed history derives count/correct/max values;
- owner-mismatch/duplicate rows are rejected or handled exactly as approved;
- preexisting wrong values are assigned derived values;
- a second execution changes zero values;
- rows after/before any defined cutoff are handled correctly.

### Existing Tests Affected

No live quiz test should change. Add separate maintenance/audit tests so submission remains isolated.

### Manual Tests to Run

Audit subphase: execute only the read-only SQL against a restored/staging copy first, review the generated result report, and confirm that every statement is `SELECT`/read-only.

If reconciliation is later approved, run its focused tests (final name to match implementation), for example:

```powershell
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~QuizHistoricalReconciliationTests"
```

Then run the operation in a restorable staging copy twice and verify the second execution changes zero rows before production approval.

### Expected Results

The audit provides enough evidence for an explicit backfill or forward-only decision. No historical value changes during the audit subphase.

### Risks

- audit queries accidentally mutate data;
- result text exposes user data in reports;
- duplicate removal assumptions corrupt history;
- R5 later merges rows and changes aggregate interpretation;
- treating missing cascade-deleted history as zero history; and
- reconciliation is executed without approval.

### Rollback/Recovery Considerations

Read-only audit needs no data rollback. Any approved reconciliation requires backup, before-value capture or a reverse script, affected-row verification, and a stop threshold. Assignment-based reruns are idempotent but are not a substitute for recovery capability.

### Definition of Done

Audit results are documented and reviewed. Either an explicitly approved idempotent reconciliation is verified, or a forward-only cutoff decision is recorded. No implicit rewrite occurs.

### Suggested Commit Message

Audit only:

`R4 Phase 6 - Add historical quiz data audit`

Optional later approved work:

`R4 Phase 6 - Reconcile historical quiz aggregates`

### Codex Implementation Prompt Requirements

- First prompt must be read-only audit artifacts only.
- List every required query/anomaly and privacy constraint.
- Include the explicit “do not reconcile without approval” stop.
- A later reconciliation prompt must include reviewed audit findings and exact approved anomaly policy.
- Require assignment, idempotency, before/after reporting, cutoff semantics, and rollback.
- Forbid live submit-path and R5 changes.
- Instruct Codex not to execute database operations/tests.

## 21. Phase 7 — Final Regression and Documentation

### Objective

Prove R4’s final behavior, audit scope, and create the implementation completion record.

### Why This Phase Comes Here

Only after live logic, migration, concurrency, and historical decision are verified can documentation accurately describe guarantees and residual R12 limitations.

### Files Expected to Change

- tests only if a final audit reveals a missing regression case;
- `Docs/Updates/R4-Quiz-Counter-Remediation-Completion.md`;
- optionally update the Plan of Action status only if the project’s process explicitly calls for it.

### Detailed Implementation Steps

1. Review the complete R4 diff for unrelated R5/R7/R8/R12 work.
2. Trace every accepted submission from session validation through commit and removal.
3. Trace every validation, duplicate, and failure path for zero mutation/retryability.
4. Verify the unique index and migration contain no data cleanup or unrelated schema changes.
5. Verify timestamp/counter semantics and accuracy projection.
6. Review logs for useful context without answer keys/user-answer leakage.
7. Run focused, full backend, target-provider migration, concurrency, and manual API/UI checks.
8. Create the completion document only after the user reports passing results.

The completion document should contain:

- summary and final semantics;
- exact production/test/migration files changed;
- final transaction and idempotency design;
- migration/audit/deployment results;
- historical reconciliation or cutoff decision;
- focused/full/manual test commands and user-reported results;
- known limitations explicitly assigned to R5/R7/R8/R12;
- rollback/deployment notes; and
- final definition-of-done checklist.

### Tests to Add or Modify

Only fill proven gaps. Do not rewrite stable tests for cosmetic consistency.

### Existing Tests Affected

All quiz integration tests, transaction/concurrency tests, vocabulary ownership/security tests, and backend regressions must remain green.

### Manual Tests to Run

```powershell
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~Quiz"
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj
dotnet build .\VocabularyApp.sln
```

Then perform target SQL Server migration verification and an authenticated smoke test covering correct, incorrect, unanswered API payload, malformed IDs, sequential retry, and two concurrent submits. Confirm vocabulary API accuracy from persisted counters.

### Expected Results

All required tests/build/manual checks pass as reported by the user, the schema invariant is verified, historical handling is explicit, and no later-roadmap architecture was introduced.

### Risks

- documenting success before manual results exist;
- silently accepting SQLite-only concurrency confidence;
- scope creep during cleanup; and
- updating roadmap status before deployment readiness is established.

### Rollback/Recovery Considerations

The completion phase should not materially change behavior. Revert only documentation or gap tests independently. Production rollback follows the Phase 5 coordinated application/index guidance and preserves database backups/audit artifacts.

### Definition of Done

The full Section 26 checklist is evidenced by code, tests, migration inspection, user-run results, and the completion document.

### Suggested Commit Message

`R4 Phase 7 - Complete quiz counter remediation review`

### Codex Implementation Prompt Requirements

- Require a read-only first audit of final code/diff.
- Require no implementation except evidence-backed test gaps.
- Require the user—not Codex—to run commands and provide results.
- Create the completion document only after results are supplied.
- Require explicit R5/R12/historical limitations and migration evidence.

## 22. Manual Test Matrix

| Behavior | Focused automated/manual verification | Expected durable state |
|---|---|---|
| Correct | Submit known correct option | 1 result; attempts +1; correct +1; both timestamps equal attempt time. |
| Incorrect | Submit a different valid option | 1 result; attempts +1; correct unchanged; reviewed advances; correct time unchanged. |
| Existing `3/5` | Correct and incorrect cases on separate rows/sessions | `4/6` or `3/6`, never replacement. |
| Unanswered | Submit empty answer list for one-question quiz | incorrect/null result; attempts/review only. |
| Mixed | Multi-question mapped choices | each exact `UserWord.Id` receives its own one delta. |
| Unknown question | Submit fabricated GUID | `400`; no state change; valid retry works. |
| Cross-session question | Use question from session B in A | `400`; neither session/DB mutates; valid retry works. |
| Duplicate question rows | Repeat same question twice | `400`; no state change; valid retry works. |
| Invalid option | Submit `int.MaxValue` | `400`; no state change; valid retry works. |
| Cross-user session | user B submits user A session | no state change; A can submit. |
| Stale word | Delete one session `UserWord`, then submit | whole submit fails; no surviving row/result changes. |
| Sequential duplicate | repeat accepted payload | one result/delta set only. |
| Concurrent duplicate | barrier-release two submits | one result/delta set only; one logical winner. |
| Save/commit failure | arm failure seam | no result/counter/timestamp; retry succeeds. |
| Accuracy | retrieve vocabulary after known totals | DTO equals floating formula, e.g. `4/6 * 100`. |
| Unique index | direct duplicate insert | duplicate fails; distinct word/session keys succeed. |
| History audit | run read-only queries | anomaly report only; no changed rows. |
| Reconciliation, if approved | run twice in staging | first assigns derived values; second changes zero rows. |

## 23. Migration/Deployment Checklist

- [ ] Confirm backup/recovery and target database identity.
- [ ] Run duplicate-key audit read-only.
- [ ] Run result-owner mismatch audit read-only.
- [ ] Record counts and review anomalies.
- [ ] Stop if duplicates exist; obtain cleanup approval.
- [ ] Inspect model change: unique `(UserId, QuizSessionId, UserWordId)` only.
- [ ] Inspect migration/designer/snapshot for unrelated changes.
- [ ] Generate/review SQL script if required by deployment process.
- [ ] Apply first to a restored/staging SQL Server database.
- [ ] Verify index metadata and uniqueness behavior.
- [ ] Run target-provider submit/concurrency smoke tests.
- [ ] Coordinate application and migration ordering; minimize mixed-version window.
- [ ] Monitor unique violations and submit failures after deployment.
- [ ] Record deployment cutoff for historical strategy.
- [ ] Do not run reconciliation until its separate approval gate is satisfied.
- [ ] Retain rollback command/script and backup reference.

## 24. Git Commit Strategy

Recommended verified checkpoints:

1. `R4 Phase 1 - Define quiz counter remediation behavior` — only if red test commits are accepted.
2. `R4 Phase 2 - Validate quiz submissions before persistence`.
3. `R4 Phase 3 - Update quiz counters and review timestamps`.
4. `R4 Phase 4 - Make quiz learning updates transactional`.
5. `R4 Phase 5A - Gate concurrent quiz submissions`.
6. `R4 Phase 5B - Enforce quiz result submission uniqueness` after clean audit/migration verification.
7. `R4 Phase 6 - Add historical quiz data audit`.
8. Optional approved `R4 Phase 6 - Reconcile historical quiz aggregates`.
9. `R4 Phase 7 - Complete quiz counter remediation review`.

Each commit should follow: Codex implements only that phase → user reviews diff → user runs prescribed tests/validation → user reports results → commit → next phase. Do not mix audit-approved destructive data work into a code/migration commit.

## 25. Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Counters remain inconsistent with events | Critical | Same evaluation, tracked rows, one save/transaction, matching assertions. |
| Cross-user mutation | High | Hidden `UserWordId`; exact `Id` plus authenticated `UserId`; all-or-nothing set equality. |
| Malformed IDs become attempts | High | Validate every supplied question/option; distinguish omission explicitly. |
| Concurrent double increment | High | Atomic local gate plus unique database key in same transaction. |
| Unique loser retains aggregate updates | Critical | Result insert and aggregate updates share one transaction; assert rollback. |
| Execution-strategy replay doubles increments | High | Retry entire unit with reloaded state; unique claim participates; never reuse incremented tracker. |
| Session lost after failure | High | Consume only after commit; reset gate on pre-commit failure if unexpired. |
| Session reopened after commit | High | Explicit committed flag/state; `finally` cannot reset successful completion. |
| Historical duplicates block migration | High | Mandatory audit/stop; no blind cleanup in migration. |
| Wrong unique key rejects legitimate quiz | Medium | Verify generator and tests; current one-question-per-`UserWord` evidence supports key. |
| SQLite gives false concurrency confidence | Medium | deterministic tests plus staging SQL Server verification. |
| Timestamp `Kind` assertions are brittle | Low | compare stored values/instants and same-timestamp equality; use UTC creation. |
| R5 coupling | Medium | use `UserWord.Id` + owner; exclude word/POS identity from aggregate/idempotency logic. |
| R12 scope expansion | Medium | private process-local state only; no new session persistence. |
| Historical reconciliation corrupts data | High | read-only audit, explicit approval, assignment/idempotency, backup and staging rerun. |
| API scope expansion | Low | current `ServiceResult`/400 pattern; defer contract standardization to R7. |

## 26. Definition of Done

R4 is complete only when user-run evidence confirms:

- Rule A is implemented for every submitted session question;
- each accepted question creates exactly one immutable `QuizResult`;
- each accepted question increments `TotalAttempts` exactly once;
- only correct answers increment `CorrectAnswers` and update `LastCorrectAt`;
- all accepted questions update `LastReviewedAt` with the common UTC attempt timestamp;
- existing counts are incremented, not overwritten;
- null answer lists are rejected safely, while empty/omitted question answers follow Rule A;
- invalid/duplicate/foreign question IDs and invalid options cause zero mutation;
- cross-user and stale-word cases cause zero mutation;
- validation/database failure does not consume a retryable valid session;
- all result and aggregate changes commit or roll back together;
- sequential and concurrent duplicates cannot double-count;
- the unique result index is audited, applied, and verified against SQL Server;
- unique conflicts are distinguished from unrelated database failures and roll back aggregates;
- accuracy reflects stored counters without a formula rewrite;
- historical audit results and reconciliation/cutoff decision are documented;
- any approved reconciliation is assignment-based and idempotent;
- focused and full backend tests/build pass when manually run;
- the completion document records actual evidence;
- no `PartOfSpeechId` identity coupling or R5 migration was added; and
- no persisted session architecture or other R12 feature was introduced.

## 27. Expected Files Changed by Phase

| Phase | Production | Tests/infrastructure | Migration/data/docs |
|---|---|---|---|
| 1 | none | `QuizApiTests.cs`, `IntegrationTestSeeder.cs`, optional quiz test helper | none |
| 2 | `QuizService.cs`; possibly minimal `QuizDTOs.cs` | validation cases in `QuizApiTests.cs` | none |
| 3 | `QuizService.cs` | quiz tests/seeder timestamp support | none |
| 4 | `QuizService.cs` | factory opt-in hook, failure interceptor, transaction tests | none |
| 5 | `QuizService.cs`, `ApplicationDbContext.cs` | concurrency/uniqueness tests and helper | new unique-index migration/designer/snapshot; optional audit SQL |
| 6 | no live-path change; optional approved maintenance code | separate reconciliation tests only if approved | audit SQL/results; optional approved reconciliation artifact |
| 7 | only evidence-backed fixes | final regression gaps only | `R4-Quiz-Counter-Remediation-Completion.md` after reported results |

Angular production files, `UserWord` identity/schema, and persisted quiz-session entities are not expected to change.

## 28. Final Recommended Execution Order

1. Implement **Phase 1 — Behavioral Contract and Tests** first. Review deterministic test data and expected red failures; do not mistake current failures for completed behavior.
2. Implement Phase 2 and manually make strict-validation tests green.
3. Implement Phase 3 and manually make aggregate/timestamp/accuracy tests green.
4. Implement Phase 4 and prove rollback plus retry.
5. Run the pre-migration audit. If clean/approved, implement Phase 5 gate and unique index, then verify on SQL Server-like infrastructure.
6. Establish the deployment cutoff and implement Phase 6’s read-only audit. Stop for approval before any reconciliation.
7. Complete Phase 7 only after all user-run results and historical decisions are available.

No new technical blocker was found. The only operational gate is expected and explicit: existing production duplicates must be audited and resolved before the unique index is applied. Historical reconciliation has a separate approval gate and does not block correcting future live submissions once Phases 1–5 are complete.

R4 is ready to implement. The first implementation prompt should target **Phase 1 — Behavioral Contract and Tests**, with tests-only scope, deterministic correct/incorrect selection, the two named defective tests rewritten, and no test execution by Codex.
