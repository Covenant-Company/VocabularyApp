# R4 Quiz Counter Remediation — Analysis

## 1. Executive Summary

R4 is necessary and is ready for implementation after two product/engineering decisions are confirmed: unanswered-question semantics and the strength of duplicate protection that R4 must provide before R12 persists quiz sessions.

The current defect is direct: `QuizService.SubmitQuizAsync` creates `QuizResult` rows, calls `SaveChangesAsync`, and removes the process-local quiz session, but never loads tracked `UserWord` entities or changes `CorrectAnswers`, `TotalAttempts`, `LastReviewedAt`, or `LastCorrectAt`. `UserVocabularyItemDto.AccuracyRate` is calculated solely from the two stale counters, so it commonly remains `null` even after completed quizzes.

The current submit path has additional correctness defects relevant to R4:

- unknown option IDs are accepted and persisted as an incorrect result with `UserAnswer = null`;
- answer rows for unknown questions, including questions from another session, are silently ignored;
- duplicate answer rows are silently collapsed with “first value wins” semantics;
- a `UserWord` deleted after quiz creation causes only that question to be skipped while the response still scores it and the session is completed;
- sequential duplicate submission is rejected only because the in-memory session is removed after a successful save;
- concurrent duplicate requests can both observe the same session and both insert results because lookup and removal are not atomic;
- restart or another application instance loses the in-memory completion signal, while the database has no uniqueness constraint preventing duplicate results;
- there is no explicit transaction, although the current single `SaveChangesAsync` is atomic for its result inserts. Adding aggregate changes to that same save would also be atomic at the EF save boundary, but would not by itself solve concurrent double counting or coordinate process-local session completion.

Ownership is better than the Plan’s summary might suggest: the session is bound to the authenticated user, generated questions contain server-side `UserWordId` values that are never returned to the client, and submission rechecks that each referenced `UserWord` is still owned by the caller. A client therefore cannot directly nominate another user’s `UserWord`. Nevertheless, the database permits a logically inconsistent `QuizResult.UserId`/`UserWordId` pair because those are independent foreign keys; R4 should preserve and tighten the service-level ownership chain.

Recommendation: validate the entire submission before mutation; load the exact affected `UserWord` rows by server-held IDs plus authenticated `UserId`; treat every session question in a submitted quiz as one attempt and an omitted answer as incorrect (product confirmation required); use one captured UTC timestamp; add all immutable results and aggregate updates in one database transaction/save; and add both an in-process atomic submission gate and a database-backed idempotency invariant. A narrow unique index on `(UserId, QuizSessionId, UserWordId)` is the smallest durable invariant available without implementing a `QuizSession` entity, but it requires an R4 migration during implementation. If schema change is declined, R4 can prevent duplicates only within one running process and must explicitly document that limitation until R12.

Historical reconciliation is technically possible from surviving `QuizResult` rows, grouped by `UserWordId`, but should be validated before any backfill. Deleted `UserWord` rows cascade-delete their history, concurrent duplicate submissions may already have duplicated events, old counters may contain manual/unidentified values, and a null `UserAnswer` cannot distinguish omission from an invalid option. Exact recomputation is safe only if the confirmed rule counts every persisted result as an attempt.

## 2. R4 Scope

R4 should:

- define the four aggregate fields’ semantics;
- reject malformed or session-inconsistent submissions before any write;
- preserve the authenticated-user-to-session-to-question-to-`UserWord` ownership chain;
- write one result event and update one word’s aggregates for each counted session question;
- make those database changes atomic;
- prevent a quiz session from changing aggregates more than once within the present architecture;
- characterize and, if approved, reconcile historical aggregates; and
- add focused integration/service tests.

R4 should not introduce persisted quiz-session lifecycle state, change saved-word identity, standardize all API errors, add global exception handling, implement mastery/spaced repetition/review queues, or redesign the quiz UI.

## 3. Current Quiz Architecture

### End-to-end flow

| Stage | Current implementation | State source/type |
|---|---|---|
| Start quiz | `QuizController.StartQuiz` extracts the JWT `NameIdentifier` and calls `QuizService.StartQuizAsync`. | Authenticated user ID is server-derived; question count and mode are client-supplied. |
| Select vocabulary | `StartQuizAsync` queries caller-owned `UserWords`, their `Word`, and definitions. It selects the preferred definition only when it matches the row’s part of speech, otherwise the first definition by `DisplayOrder`. | Database state. |
| Deduplicate/select | Entries missing word/definition text are removed. Remaining entries are grouped by `WordId`; the first `UserWordId` in each group is used. At least four unique words are required. Requested questions are clamped to 1–20 (default 10). | Process-local computation from database data. |
| Generate questions | Words are shuffled; each question receives three distractors and a random/server-selected direction for mixed mode. Options use per-question integer IDs `0..3`; question IDs are GUIDs. | Server-generated process-local state. |
| Store session | A static `ConcurrentDictionary<Guid, QuizSessionState>` stores session ID, owner user ID, expiry, questions, hidden `UserWordId`, options, and correct option ID. Expiry is 30 minutes. | Process-local, shared only within the current application process. Not persisted. |
| Return quiz | Response exposes session ID, expiry, question IDs, prompts, and options, but not `UserWordId` or the answer key. | Client-held copy of the public subset. |
| Gather answers | Angular stores selected option IDs in a `Map<questionId, optionId>`. Its normal UI blocks moving forward without selecting and submits only mapped answers. | Client state; API callers can omit or manipulate it. |
| Submit | `QuizController.SubmitQuiz` derives the user ID from the JWT and passes the body to `SubmitQuizAsync`. | Session ID, question IDs, and option IDs are client-supplied. |
| Score | Service matches answers to server-held questions, compares submitted option IDs with server-held correct IDs, and scores every session question. | Server answer key plus client choices. |
| Persist | Service creates `QuizResult` rows for still-owned `UserWord` IDs and calls one `SaveChangesAsync`. | Database state. |
| Complete | After the save, service removes the session from the static dictionary and returns the precomputed result. | Process-local removal; no durable completion record. |

### Important architecture observations

- The static dictionary survives across scoped `QuizService` instances in one process but not an application restart, deployment, or a separate server instance.
- Cleanup occurs only after a successful quiz start. Expired sessions may remain until another start or an attempted submission.
- `UserWordId` is the correct present mapping from a generated question back to a learning aggregate. It is stored only in private server session state.
- Both quiz directions are persisted as `QuizType.Definition`; mode/direction and response time are not faithfully captured (`ResponseTimeSeconds` is always zero). These are existing history limitations, not required R4 fixes.

## 4. Current SubmitQuizAsync Behavior

### Inputs and preconditions (lines 145–166)

The method accepts authenticated `userId` and `QuizSubmitRequestDto`, containing `SessionId` and a list of `(QuestionId, SelectedOptionId)` pairs.

1. Empty session ID fails.
2. Missing dictionary entry fails as “not found or expired.”
3. A session owned by a different authenticated user fails. It is not removed, so its owner can still submit it.
4. An expired session is removed and fails.

The controller rejects a null request body. `Answers` is initialized to an empty list for ordinary model binding, but explicit JSON `"answers": null` can leave it null; the later LINQ call then throws, is caught, returns a generic failure, and leaves the session available.

### Answer lookup and scoring (lines 168–214)

- Answers are grouped by submitted `QuestionId`; the first duplicate is kept and all later duplicates are discarded without error.
- Extra/unknown question IDs are placed in the lookup but never iterated, so they are silently ignored.
- For each server session question, absence from the lookup means unanswered.
- If present, an option is looked up for display. An unknown option produces `selectedOption = null`, but correctness is determined separately by raw integer equality.
- Correctness is `hasAnswer && selectedOptionId == CorrectOptionId`. Thus omitted and unknown-option answers are both incorrect.
- Score denominator is all session questions, not answered questions. Percentage is rounded to two decimal places.
- The response is constructed before checking whether every question can still be persisted.

### Persistence (lines 216–268)

- One `attemptedAtUtc = DateTime.UtcNow` is captured for all rows.
- The service queries all caller-owned `UserWord` IDs, rather than the exact required IDs.
- Each question whose hidden `UserWordId` is absent is skipped and logged; other questions continue.
- Each retained question produces a `QuizResult` with authenticated `UserId`, server-held `UserWordId`, submitted session ID, `QuizType.Definition`, correctness, selected option text (or null), correct option text, response time zero, and the common UTC timestamp.
- One `AddRange` and one `SaveChangesAsync` persist all retained results. No `UserWord` aggregate is updated.
- If every question is stale, no save occurs, yet the session is removed and a successful scored response is returned.
- After persistence, `TryRemove` is called without checking whether another request already removed the entry.

### Exceptions and failure behavior

Any exception inside the `try` is logged and converted to `ServiceResult.Failure("Failed to submit quiz.")`. The session is not removed on a thrown exception, so a retry is possible. With the current single save, EF/provider transaction semantics prevent a partial subset of that save’s inserts from committing. If a future implementation used separate saves for results and aggregates, a failure between them would create permanent inconsistency.

### Manipulated/stale input findings

| Input | Actual behavior | Required R4 behavior |
|---|---|---|
| Empty/unknown/expired session | Rejected before writes. | Retain. |
| Other user’s session | Rejected before writes. | Retain; preferably use a consistent authorization/not-found policy later under R7. |
| Unknown question | Silently ignored; real session question becomes unanswered. | Reject the whole submission before mutation. |
| Question from another session | Silently ignored. | Reject the whole submission. |
| Duplicate question rows | First wins. | Reject as malformed/ambiguous. |
| Unknown option | Accepted as incorrect with null answer. | Reject the whole submission. |
| Omitted question | Counted incorrect in score and persisted as incorrect/null if the word still exists. | Apply the confirmed unanswered rule consistently to score, event, and aggregate. |
| Deleted/stale `UserWord` | That result is skipped; response still includes/scorers it; session succeeds and is removed. | Fail the complete submission without writes. |
| Fabricated `UserWordId`, word ID, or definition ID | DTO exposes none of these. | Continue to derive these only from server session state. |

## 5. Current UserWord Counter and Timestamp Semantics

Repository-wide search found no production writer for `LastReviewedAt` or `LastCorrectAt`, and no writer for the counters after initialization. `WordService.AddToVocabularyAsync` explicitly initializes `TotalAttempts` and `CorrectAnswers` to zero. Vocabulary list/search projections merely copy both values into DTOs.

Recommended semantics, based on the existing result-per-question history and all-question score denominator:

- `TotalAttempts`: lifetime count of submitted/scored quiz questions for this `UserWord`. Under the recommended Rule A, an unanswered question in a submitted quiz is scored and therefore counts. Quiz generation alone is not an attempt.
- `CorrectAnswers`: lifetime count of those attempts whose server-determined result was correct. It is not the current quiz score.
- `LastReviewedAt`: UTC time of the latest counted submitted question for the word, whether correct or incorrect (and, under Rule A, whether answered or omitted). Starting/generating a quiz must not update it.
- `LastCorrectAt`: UTC time of the latest correct submitted answer only. Incorrect or omitted submissions must leave it unchanged.

The model defaults `AddedAt`, `Word.CreatedAt`, `WordDefinition.CreatedAt`, and `QuizResult.AttemptedAt` with `DateTime.UtcNow`; quiz session expiry and submit timestamps also use UTC. Seed timestamps are explicitly UTC. The reviewed fields have no current writer, so no current violation was found. SQL Server `datetime2` does not itself persist `DateTime.Kind`; implementation/tests should compare values as UTC instants and consistently name/capture UTC timestamps.

## 6. AccuracyRate Analysis

`UserVocabularyItemDto.AccuracyRate` is a computed getter:

```text
TotalAttempts > 0 ? (double)CorrectAnswers / TotalAttempts * 100 : null
```

The cast occurs before division, so this is floating-point, not integer division. There is no rounding in this DTO. Representative values are:

| Correct / attempts | Result |
|---|---:|
| 0 / 0 | `null` |
| 1 / 1 | `100` |
| 1 / 2 | `50` |
| 2 / 3 | approximately `66.66666666666666` |

`WordService.GetUserVocabularyAsync` and `SearchUserVocabularyAsync` project the stored counters into this DTO. The Angular `UserVocabularyItem` model accepts the server-provided optional `accuracyRate`; no frontend code recalculates it, and the current templates do not display it. Quiz score/history percentages are separate calculations and are rounded to two decimals in `QuizService`.

R4 does not require a formula change. It requires reliable denominator/numerator data. Display rounding, if desired, is presentation scope rather than counter remediation.

## 7. Unanswered-Question Analysis

The submit contracts have no nullable “answer” row: omission of a question from `Answers` represents unanswered. Angular’s normal forward flow requires an option, but the backend supports empty/partial lists. The backend currently scores omitted questions as incorrect, persists an incorrect `QuizResult` with `UserAnswer = null`, and includes all questions in the score denominator. An invalid option currently produces the same stored shape, which R4 should eliminate by validation.

### Rule A — one attempt, incorrect

Benefits:

- matches current score denominator and persisted-result behavior;
- preserves one event per question in a submitted quiz;
- prevents selectively omitting hard questions to protect per-word accuracy;
- makes quiz history totals, per-word totals, and submitted quiz size reconcilable; and
- treats submission as the learner finalizing the presented review opportunity.

Cost: accidental submission or UI/network omissions penalize accuracy, so the UI should keep its confirmation/selection safeguards.

### Rule B — no attempt

Benefits: measures only explicit responses and avoids penalizing unanswered items.

Costs: requires either no `QuizResult` for omissions or a new way to distinguish “presented but not attempted”; current `QuizResult.IsCorrect` cannot represent a third state. It makes the current all-question score denominator differ from per-word attempt denominators and allows omission to avoid weak-word evidence.

### Recommendation — product decision requiring confirmation

Adopt Rule A: once a quiz is submitted, each session question is one attempt; omission is incorrect; `LastReviewedAt` advances; `LastCorrectAt` does not. This is the smallest change and best fits existing scoring/history semantics and future weak-word/review goals. Reject invalid option/question identifiers rather than treating them as unanswered.

## 8. Ownership and Trust Boundaries

| Identifier | Origin/classification | Validation today |
|---|---|---|
| Authenticated user ID | Server-derived from signed JWT claim | Parsed by controller. |
| Session ID | Server-generated, then client-supplied | Must exist, match user, and be unexpired. |
| Question ID | Server-generated, then client-supplied | Used only if it matches a session question; foreign/unknown values are silently ignored. Insufficient validation. |
| Option ID | Server-generated per question, then client-supplied | Compared to hidden correct ID; membership in that question’s option list is not required. Insufficient validation. |
| `UserWordId` | Server-generated database ID held only in private session state | At submit, checked against all `UserWord` IDs currently owned by authenticated user. |
| Word/definition IDs | Not in submit DTO | Derived indirectly from the server-created session; not client-controllable during submit. |

Required ownership chain:

```text
JWT user ID
  -> owns live session (`session.UserId`)
  -> session contains the exact question ID
  -> question contains the server-held UserWord ID and option set
  -> UserWord exists with both that ID and JWT user ID
  -> selected option belongs to that exact question
  -> correctness uses only the server-held correct option
  -> QuizResult.UserId and updated UserWord share the same owner
```

Under current contracts, a malicious client cannot nominate another user’s `UserWord`, write a result for an out-of-session word, or substitute a definition ID. It can submit foreign/fabricated question and option IDs, but these currently degrade its own stored results rather than target another user. A second user cannot submit the first user’s session. The database alone does not guarantee `QuizResult.UserId == QuizResult.UserWord.UserId`, so the service validation remains security-critical.

R4 should validate the complete set before constructing/mutating tracked entities: no duplicate submitted question IDs, no unknown question IDs, every supplied option belongs to its question, and every hidden `UserWordId` resolves in a query constrained by authenticated `UserId`. Omission should be allowed only as the explicit product rule, not confused with invalid input.

## 9. Transaction and Failure Analysis

Current submission has:

1. one read of owned `UserWord` IDs;
2. one `AddRange` into the EF change tracker;
3. zero or one `SaveChangesAsync`; and
4. one process-local session removal.

No explicit `BeginTransaction` or execution strategy is used. A single relational EF Core `SaveChangesAsync` is transactional for all commands it emits, so the current result rows ordinarily commit or fail together. There are currently no aggregate updates to coordinate.

Concrete current inconsistency: a two-question session references word A and word B. The user deletes B after starting. Submit scores both questions, writes only A’s result, logs B as stale, removes the session, and returns `TotalQuestions = 2`. Quiz history later reports only one persisted question for that session. The successful response and durable history disagree, and no counter is updated for either word.

Concrete future failure if implemented with separate saves: save two `QuizResult` rows, then increment counters in a second save. If the second save fails, history says two attempts while both `UserWord` rows retain old aggregates. Retrying could insert duplicate events before applying aggregates.

R4’s database transaction boundary should begin after all request/session shape validation and encompass:

- reloading/validating all exact caller-owned `UserWord` rows;
- durable duplicate detection/idempotency claim;
- adding all `QuizResult` rows;
- incrementing all counters and applying timestamps; and
- committing once only after all database mutations succeed.

All validation should occur before entity mutation where practical. One `SaveChangesAsync` inside the transaction is sufficient for the inserts and tracked aggregate updates; an explicit transaction is still useful when duplicate checks/locking form part of the database operation. Process-local session completion cannot be rolled back by EF and must be coordinated: claim/gate submission before work, remove/mark complete only after commit, and release or restore the retryable state on failure.

## 10. Duplicate/Concurrent Submission Analysis

Sequential duplicate protection works only in the happy path: the first successful request saves and then removes the session, so a later request gets “not found.” Browser refresh or client retry can resend the request; if it arrives after removal it is rejected, but if the first response was lost the client cannot distinguish success from expiry/not-found.

Concurrent duplicate submission is unsafe. Both requests can call `TryGetValue` before either reaches `TryRemove`; each uses a separate scoped context and each inserts a full result set. With R4 counter increments, both would increment every word twice. `ConcurrentDictionary` makes individual dictionary operations safe, not the lookup-process-remove sequence.

Restart/multi-instance behavior is also unsafe as an idempotency model. A restart loses open sessions, so they cannot be submitted; a different instance cannot see the session. Existing database rows carry `QuizSessionId`, but no unique constraint prevents duplicate `(user, session, word)` events.

### Minimum R4 protection

R4 should use two layers:

1. an atomic in-process per-session submission gate/state transition so only one request can process a live session; failed database work releases/restores retryability and successful work leaves a completed tombstone for a bounded period; and
2. a durable database invariant, preferably a unique index on `(UserId, QuizSessionId, UserWordId)`, plus duplicate-key handling as “already submitted,” so races/retries cannot create two events or double-update aggregates.

The unique key is compatible with the current generator because it produces at most one question per canonical `WordId` and selects one `UserWord` per group. It should be verified against historical duplicates before creation. If R4 is forbidden from adding this narrow constraint, serializing per-session work in the static architecture is the minimum acceptable implementation, but guarantees would be process-local only and R4 should remain partially mitigated rather than fully idempotent.

### R4 versus R12

- R4 owns correct, atomic, non-double-counted handling of a submission in the current application and a narrow result idempotency invariant.
- R12 owns a durable `QuizSession`/question/answer lifecycle, cross-instance routing, restart survivability, abandonment/completion states, durable idempotent response replay, and replacement of static memory.

## 11. Historical Data and Backfill Feasibility

Surviving `QuizResult` rows contain `UserWordId`, `UserId`, `IsCorrect`, `AttemptedAt`, and now non-null `QuizSessionId`. The initial schema already stored all fields except session ID. Migration `20260227145921_AddQuizSessionIdToQuizResult` assigned one GUID to all rows sharing exactly the same `AttemptedAt`; current submissions deliberately share one timestamp across their question rows.

Under Rule A, exact aggregate reconstruction is technically straightforward:

- grouping key: `QuizResult.UserWordId` (with an ownership validation that result user equals current `UserWord.UserId`);
- `TotalAttempts`: count of rows in the group;
- `CorrectAnswers`: count where `IsCorrect` is true;
- `LastReviewedAt`: maximum `AttemptedAt` in the group;
- `LastCorrectAt`: maximum `AttemptedAt` where `IsCorrect` is true, otherwise null.

An idempotent reconciliation would assign these computed values, not increment existing counters. Re-running it produces the same result for unchanged event history.

Reliability concerns require a validation phase:

- concurrent past submissions may have inserted duplicate event sets because no unique constraint exists;
- the session-ID migration grouped all records globally by identical timestamp, which could merge unrelated legacy sessions that happened to share an exact timestamp (per-word aggregation is unaffected, but duplicate/session analysis is less certain);
- cascade delete from `UserWord` deletes its result history, so removed/recreated vocabulary cannot be reconstructed;
- stale questions were intentionally skipped, leaving no event to reconstruct;
- null `UserAnswer` conflates omissions with accepted invalid options, though both count as incorrect under Rule A;
- there is no repository code that updates counters after creation, but production/manual data could contain nonzero counters not represented by results;
- independent foreign keys allow anomalous rows whose `UserId` differs from the linked `UserWord` owner; and
- R5 may later merge duplicate `(UserId, WordId)` entries and reassign results.

Recommendation: **validate first, then conditionally backfill**. Audit mismatched ownership, duplicate `(UserId, QuizSessionId, UserWordId)` groups, counters versus event-derived values, null-answer prevalence, and orphan/lifecycle limitations. If anomalies are understood/cleaned, set aggregates from grouped history in an idempotent maintenance operation. Do not blindly add historical counts to existing counters. If validation cannot establish trust, leave historical values untouched (or explicitly reset by product decision) and begin accurate counting at deployment with a cutoff; silently presenting mixed “lifetime” data would be misleading.

## 12. Existing Test Infrastructure

The `VocabularyApp.WebApi.Tests` .NET 8/xUnit project provides:

- `VocabularyAppWebApplicationFactory`, an in-process ASP.NET Core host with JWT configuration, a shared open SQLite in-memory connection, `EnsureCreated`, and controllable dictionary HTTP handler;
- `ApiTestClientHelper` for registration, login, bearer clients, and two-user scenarios;
- `IntegrationTestSeeder` for users, canonical words/definitions, `UserWord` rows, and initial counter values;
- `QuizApiCollection`/`QuizApiTestBase`, which disables parallelization for static quiz sessions and clears them before/after each quiz test;
- `RelationalDatabaseFixture`, which creates fresh EF contexts against a shared SQLite in-memory database and can attach EF interceptors;
- existing `SaveChangesInterceptor` failure patterns in authentication tests;
- existing concurrency test patterns using separate contexts; and
- `QuizApiTests`, covering anonymous access, answer-key non-disclosure, cross-user session rejection, sequential duplicate rejection, current acceptance of unknown options, current ignoring of foreign questions, unknown sessions, and per-user history.

This foundation can query persisted results/aggregates through fresh scopes and seed existing counters. It is suitable for most R4 API integration tests. Missing pieces are a deterministic way to know/select the correct option, direct seeding/access to a controlled session answer key, injection of a failing context/interceptor into the API factory, and synchronization hooks/barriers for deterministic concurrent submit tests. SQLite proves relational behavior but not SQL Server-specific locking/isolation or migration SQL; the unique constraint and concurrency behavior should also receive a SQL Server-targeted verification where feasible. No test currently demonstrates transaction rollback of quiz writes or concurrent duplicate submission.

No tests were run during this analysis, per instruction.

## 13. Required Test Matrix

| Scenario | Required assertions |
|---|---|
| Correct answer | One result; attempts +1; correct +1; reviewed timestamp set to captured UTC instant; correct timestamp set to same instant. |
| Incorrect answer | One result; attempts +1; correct unchanged; reviewed advances; correct timestamp unchanged. |
| Mixed quiz | Each server-mapped `UserWord` receives only its own result/counter/timestamps; quiz totals agree. |
| Unanswered | Under recommended Rule A: incorrect result with null answer, attempts +1, correct unchanged, reviewed advances, correct timestamp unchanged. |
| Empty answer list | Every question consistently follows Rule A; no malformed-input crash. |
| Invalid question ID | Whole request rejected; no results or aggregate/timestamp mutations; session remains retryable. |
| Foreign-session question | Same all-or-nothing rejection. |
| Duplicate question IDs | Rejected before mutation rather than first-wins. |
| Invalid option ID | Whole request rejected; no null/incorrect fabricated event; session remains retryable. |
| Cross-user session | No result or aggregate mutation for either user; owner can subsequently submit. |
| Stale/deleted `UserWord` | Whole submission fails; no surviving question is persisted or counted. |
| Sequential duplicate | Second submit cannot insert or increment; ideally returns a stable duplicate outcome. |
| Concurrent duplicate | Release two requests together; exactly one logical result set and one increment per word; other request is duplicate/in-progress, never success with a second count. |
| Database failure | Inject failure at save/commit; fresh context sees neither results nor aggregates; session can retry once. |
| Existing counts | Example `3/5` becomes `4/6` on correct and `3/6` on incorrect; never overwritten. |
| Timestamp monotonicity | Older values advance on review; incorrect leaves prior `LastCorrectAt`; all timestamps are based on one UTC submission instant. |
| Accuracy | Verify null at 0/0 and expected floating values after persisted increments. |
| History consistency | Persisted question count/correct total matches accepted submission and aggregate deltas. |
| Historical reconciliation | If approved: derives count/max values, rejects or reports anomalies, and a second execution makes no changes. |
| Durable uniqueness | Duplicate `(UserId, SessionId, UserWordId)` is rejected; valid different words/sessions remain allowed. |

Tests that need a known correct answer should avoid inferring correctness from random option order. Add a test seam/helper that inspects the private server session through an internal test-only accessor, or construct controlled `QuizService` session state through a narrow internal helper. Do not expose the answer key in the production API.

## 14. R4/R5 Interaction

Today `UserWord` identity is `(UserId, WordId, PartOfSpeechId)`, despite contradictory “removed” comments. Quiz generation groups by `WordId` and arbitrarily selects the first row’s `UserWordId`, so duplicate saved meanings do not each appear in one quiz.

R4 should update by the stable server-held `UserWord.Id` and constrain that row by `UserId`. It should not locate aggregates through `(UserId, WordId, PartOfSpeechId)`, copy part-of-speech into idempotency keys, or introduce new dependencies on `PartOfSpeechId`. That lets R5 merge/reassign rows and their `QuizResult` history later. Any R4 unique result invariant should use `UserWordId`, not current logical identity fields.

R4 must not perform R5’s merge, uniqueness migration, preferred-definition identity decision, or dependent-row reassignment. Historical reconciliation should occur with awareness that R5 may later sum or recompute merged rows; documenting its cutoff and source query is essential.

## 15. R4/R12 Boundary

R4 may add a minimal atomic state/gate around the existing dictionary and a result-level database uniqueness constraint because counters cannot be correct if one session is counted twice.

R12 remains responsible for replacing `QuizSessions` with durable entities, persisting generated questions/options/answer keys, surviving restarts, supporting multiple application instances, recording started/completed/expired/abandoned states, replaying completed responses, and cleaning durable sessions. R4 should not design those tables or move the full session into EF.

## 16. Findings by Severity

| Severity | Finding/evidence | Impact | Mitigation |
|---|---|---|---|
| Critical | Lines 253–257 persist results; no code writes any four learning fields. | Per-word learning state and accuracy are false. | Transactionally update result events and exact owned `UserWord` aggregates. |
| High | Lookup and removal of static session are separated; no result uniqueness. | Concurrent requests double-insert and future counters double-increment. | Atomic in-process gate plus durable unique invariant. |
| High | Stale `UserWord` questions are skipped while others commit and success is returned. | Response, history, and aggregates diverge. | Validate all exact rows first; fail all-or-nothing. |
| High | Unknown options and foreign/unknown questions are not rejected. | Manipulated/malformed data becomes misleading history. | Strict full-request validation before writes. |
| High | Future separate result/aggregate saves would partially succeed; no current transaction design covers duplicate claim. | History and aggregates can diverge. | One transaction and one save for all database state. |
| Medium | Sequential duplicate protection is process-local and response-loss unfriendly. | Retry may be rejected ambiguously; restart/multi-instance has no durable completion model. | R4 unique result key; durable replay waits for R12. |
| Medium | Timestamps are never updated. | Review recency and scheduling cannot work. | One captured UTC submission timestamp with defined rules. |
| Medium | Historical results may contain duplicates/anomalies and deleted history cannot be recovered. | Blind backfill can legitimize bad totals. | Validate, report, conditionally recompute idempotently. |
| Medium | DB permits `QuizResult.UserId` to disagree with linked `UserWord.UserId`. | A future/alternate writer could create cross-owner history. | Preserve service ownership query; consider broader relational invariant separately. |
| Low | Accuracy has no DTO rounding. | UI may receive long decimal representations. | Presentation decision; no R4 formula change required. |
| Medium | R4 could locate rows by part-of-speech/current composite identity. | Deepens coupling and complicates R5. | Use server-held `UserWord.Id` plus owner only. |
| Medium | Duplicate work could expand into persisted sessions. | Delays/remixes R4 and R12. | Limit R4 to gate + result idempotency; defer durable lifecycle. |

## 17. Recommended Remediation Design

1. At entry, validate non-null answers, nonempty/owned/unexpired session, unique submitted question IDs, membership of every submitted question in the session, and membership of each selected option in that question.
2. Apply an atomic per-session submit gate. A second request must not enter scoring/persistence concurrently.
3. Derive all affected `UserWordId` values exclusively from server session questions.
4. Begin the database transaction required by the chosen duplicate strategy.
5. Query the exact distinct IDs with `uw.UserId == authenticatedUserId`; require the returned count to match. Any stale/missing/foreign row fails the whole operation.
6. Check/claim durable idempotency. Prefer a prevalidated unique `(UserId, QuizSessionId, UserWordId)` constraint as the ultimate race-safe guard; treat a conflict as duplicate, never as a reason to retry increments.
7. Capture `nowUtc` once after validation. For every session question, resolve the optional submitted answer and correctness exclusively against the server-held option/correct ID.
8. Under recommended Rule A, create exactly one immutable `QuizResult` per session question. Omission is incorrect/null answer.
9. For the matched `UserWord`, increment `TotalAttempts`; increment `CorrectAnswers` only when correct; set `LastReviewedAt = nowUtc`; set `LastCorrectAt = nowUtc` only when correct.
10. Add results and save all tracked changes once. Commit the transaction.
11. Only after commit, mark/remove the process-local session as completed. On validation failure, release the gate without consuming the session; on database failure, roll back and restore retryability. Keep a bounded completed marker if needed to distinguish duplicates from unknown sessions.
12. Log stale/invalid/duplicate cases without disclosing answer keys or foreign identifiers.
13. Run a separate, explicitly approved historical audit. Conditionally recompute aggregates from trustworthy result rows; never blend this silently into request submission.

An EF execution strategy must not replay a non-idempotent aggregate increment without the unique idempotency claim participating in the same retried transaction. The implementation should design retry behavior explicitly.

## 18. Recommended Implementation Phases

### Phase 1 — Lock semantics and characterize behavior

- Objective: confirm Rule A and durable-idempotency requirement; turn current defects into explicit characterization/replacement test cases.
- Production files: none.
- Test files: `Integration/QuizApiTests.cs`; possibly new quiz service test/support files.
- Expected behavior: no production change; agreed contracts are executable specifications.
- Risks: preserving insecure behavior as desired behavior instead of marking it for replacement.
- Manual verification: product confirms unanswered behavior and duplicate guarantee.

### Phase 2 — Strict validation and ownership

- Objective: reject unknown/duplicate questions, invalid options, null answer collections, and any stale/foreign server-held `UserWord` atomically.
- Production files: `Services/QuizService.cs`; possibly private/internal session validation helpers.
- Tests: invalid question/option/duplicate/cross-user/stale cases.
- Expected behavior: malformed submissions cause zero writes and remain retryable.
- Risks: changing current tests that intentionally expect unknown inputs to be accepted/ignored.
- Manual verification: valid UI payload still submits; errors do not consume session.

### Phase 3 — Aggregate and timestamp updates

- Objective: create result events and increment exact owned aggregates with one captured UTC instant and confirmed unanswered rule.
- Production files: primarily `Services/QuizService.cs`.
- Tests: correct, incorrect, mixed, unanswered, existing counts, timestamps, accuracy.
- Expected behavior: history and per-word state agree after one valid submission.
- Risks: random answer order makes tests nondeterministic; accidental overwrite rather than increment.
- Manual verification: submit known quiz and inspect vocabulary API/database counters.

### Phase 4 — Atomic persistence and failure recovery

- Objective: formalize transaction boundary and prove rollback/retry behavior.
- Production files: `Services/QuizService.cs`; test-host service registration only if needed.
- Tests: injected save/commit failure and fresh-context zero-mutation assertions.
- Expected behavior: results and aggregates commit or roll back together; failed session can retry.
- Risks: mishandled EF execution strategy; consuming in-memory session before commit.
- Manual verification: forced failure leaves no rows/deltas.

### Phase 5 — Duplicate and concurrent protection

- Objective: serialize/claim current session submission and add durable event uniqueness if approved.
- Production files: `Services/QuizService.cs`, `ApplicationDbContext.cs`; new R4 migration if durable uniqueness is approved.
- Tests: sequential duplicate, concurrent barrier test, unique-key relational test.
- Expected behavior: exactly one logical submission changes the database.
- Risks: deadlocks/leaked gates, lost retryability, preexisting duplicates blocking migration, SQLite/SQL Server differences.
- Manual verification: audit duplicates before migration; issue two simultaneous submits against SQL Server-like environment.

### Phase 6 — Historical audit and conditional reconciliation

- Objective: report anomalies and, only after approval, idempotently assign event-derived aggregates.
- Production files: preferably a separately reviewed maintenance/migration operation, not request-path code.
- Tests: derivation, anomaly handling, rerun idempotency, cutoff behavior.
- Expected behavior: trustworthy surviving history and aggregates agree.
- Risks: treating duplicated/incomplete history as truth; interaction with later R5 merges.
- Manual verification: compare counts and sampled users before/after; retain rollback plan.

### Phase 7 — Regression and documentation

- Objective: audit scope, API behavior, history, ownership, and roadmap boundaries.
- Production files: only fixes proven necessary by tests.
- Tests: full relevant backend suite plus SQL Server-targeted concurrency/migration checks during implementation (not this analysis).
- Expected behavior: R4 definition of done satisfied without R5/R12 implementation.
- Risks: incidental refactoring/scope expansion.
- Manual verification: end-to-end correct/incorrect/unanswered/duplicate flows and vocabulary accuracy.

## 19. Risks and Open Decisions

1. **Product confirmation required:** adopt Rule A (submitted omission = one incorrect attempt) or Rule B. Rule A is recommended.
2. **Engineering/product confirmation required:** whether R4 may add the narrow unique result index. Without it, guarantees stop at a single process and concurrent database writers remain an acknowledged gap until R12.
3. **Data-owner decision required:** whether the historical audit can overwrite existing aggregates from result history after anomalies are reported. “Validate, then conditionally backfill” is recommended.
4. Decide duplicate API semantics (generic bad request versus explicit already-submitted response). Full API contract standardization remains R7.
5. SQL Server-specific isolation/index behavior cannot be fully certified by the current SQLite host; plan one target-provider verification.

## 20. Definition of Done

R4 implementation is done when:

- each accepted/countable session question creates exactly one immutable result and exactly one attempt increment for its server-mapped, caller-owned `UserWord`;
- correct responses alone increment `CorrectAnswers` and update `LastCorrectAt`;
- all counted reviews update `LastReviewedAt` using UTC;
- unanswered behavior matches the confirmed product rule everywhere;
- invalid, foreign, duplicate, stale, or manipulated identifiers cause no partial mutation;
- results and aggregates commit or roll back together;
- sequential and concurrent duplicate submission cannot double-count within the explicitly agreed R4 guarantee;
- failure leaves the session safely retryable where no commit occurred;
- accuracy examples are correct from persisted aggregates;
- historical reconciliation has either been validated/performed idempotently or explicitly deferred with a documented cutoff;
- all R4 matrix tests pass in implementation, including a target-provider check for the durable concurrency invariant; and
- no R5 identity migration or R12 persisted-session redesign has entered the change.

## 21. Files Likely to Change During Implementation

Core:

- `VocabularyApp.WebApi/Services/QuizService.cs`
- `VocabularyApp.Data/ApplicationDbContext.cs` (only if durable uniqueness is approved)
- a new narrowly scoped R4 EF migration (only if approved)
- `VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/QuizApiCollection.cs` or a new quiz test helper
- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs` (only for failure/concurrency injection)
- `VocabularyApp.WebApi.Tests/Infrastructure/IntegrationTestSeeder.cs`

Potentially, but only if evidence during implementation requires it:

- `VocabularyApp.WebApi/DTOs/QuizDTOs.cs` for validation annotations/contract clarity;
- a dedicated quiz service test file and save-failure interceptor;
- a separately approved historical audit/reconciliation operation and tests.

No Angular production change is required for the recommended Rule A because its normal flow already requires an answer. UI error messaging may be adjusted only if new validation responses need presentation.

## 22. Files Reviewed

Production and schema:

- `VocabularyApp.WebApi/Services/QuizService.cs`
- `VocabularyApp.WebApi/Services/IQuizService.cs`
- `VocabularyApp.WebApi/Controllers/QuizController.cs`
- `VocabularyApp.WebApi/DTOs/QuizDTOs.cs`
- `VocabularyApp.WebApi/DTOs/UserVocabularyDTOs.cs`
- `VocabularyApp.WebApi/DTOs/UserWordDTOs.cs`
- `VocabularyApp.WebApi/Services/WordService.cs`
- `VocabularyApp.WebApi/Program.cs`
- `VocabularyApp.Data/ApplicationDbContext.cs`
- `VocabularyApp.Data/Models/QuizResult.cs`
- `VocabularyApp.Data/Models/UserWord.cs`
- `VocabularyApp.Data/Models/Word.cs`
- `VocabularyApp.Data/Models/WordDefinition.cs`
- initial migration, quiz-session migration, model snapshot, and relevant later migrations/search results.

Frontend:

- `VocabularyApp.UI/src/app/models/quiz.model.ts`
- `VocabularyApp.UI/src/app/models/word-lookup.model.ts`
- `VocabularyApp.UI/src/app/components/quiz/quiz.component.ts`
- `VocabularyApp.UI/src/app/components/quiz/quiz.component.html`
- `VocabularyApp.UI/src/app/services/api.service.ts`
- repository-wide frontend searches for accuracy/counter/timestamp use.

Tests and documentation:

- `VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs`
- `RelationalDatabaseFixture.cs` and its tests
- `IntegrationTestSeeder.cs`
- `ApiTestClientHelper.cs`
- `QuizApiCollection.cs`
- existing interceptor/concurrency patterns across service tests
- test project file
- R4/R5/R6/R12-relevant Plan of Action and existing testing documentation.

Repository-wide searches covered every production assignment, increment, projection, migration/schema occurrence, and frontend use of `CorrectAnswers`, `TotalAttempts`, `LastReviewedAt`, `LastCorrectAt`, `AccuracyRate`, and `QuizResult` creation.

## 23. Final Recommendation

Proceed with R4 implementation after confirming Rule A and authorizing (or explicitly declining) the narrow durable uniqueness constraint. Use the seven reviewable phases above. The immediate safe core is strict all-or-nothing validation, exact owner-constrained `UserWord` loading, one event plus aggregate update per counted question, one UTC timestamp, and one transactional save. Add an atomic session gate and database uniqueness so retries/concurrency cannot double-count without dragging durable quiz-session lifecycle into R4.

Historical data should not be blindly trusted or incrementally merged. Run a read-only anomaly audit first; if it validates sufficiently, recompute aggregates by assignment from surviving events in a separate idempotent operation. R4 is otherwise implementation-ready, and no full R5 or R12 work is required to correct the current counters.
