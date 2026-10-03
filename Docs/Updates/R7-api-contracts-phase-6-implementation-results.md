# R7 Phase 6 — Quiz Contract Remediation

Date: 2026-10-02

Status: **IMPLEMENTED — EXECUTION VERIFICATION PENDING**

This record follows approved D1–D4 in the [implementation plan](R7-api-contracts-remediation-implementation-plan.md), the [analysis](R7-api-contracts-remediation-analysis.md), and the user's Phase 6 instructions. [Phase 1](R7-api-contracts-phase-1-implementation-results.md), [Phase 2](R7-api-contracts-phase-2-implementation-results.md), [Phase 3](R7-api-contracts-phase-3-implementation-results.md), [Phase 4](R7-api-contracts-phase-4-implementation-results.md), and [Phase 5](R7-api-contracts-phase-5-implementation-results.md) remain historical implementation checkpoints. No overall R7 completion or execution result is claimed.

## 1. Objective

Complete B07/B08/U05 quiz contract boundaries: explicit answer-property presence, safe null handling, approved session statuses, compatible successful payloads, bounded Angular recovery, and focused protection of existing R4 integrity guarantees.

## 2. Scope implemented

Inspected the actual working tree, authoritative records, DTOs/entities/controller/service, process-local session state, transaction and lock logic, Angular models/component/ApiService/normalizer, Phase 1 success contracts, Phase 2 groundwork, Phase 3 errors, and R4/R6 tests before editing. Existing Phase 1–5 changes were preserved. Implementation adds request presence/structure checks, classifies existing session and vocabulary branches, uses the existing named success envelope, and completes focused Angular behavior/tests. No quiz feature redesign was performed.

## 3. Backend production files changed

Four files relative to `VocabularyApp.WebApi/`:

| File | Phase 6 contribution |
|---|---|
| `DTOs/QuizDTOs.cs` | `[JsonRequired]` on the existing nonnullable integer `SelectedOptionId`. |
| `Services/QuizService.cs` | Whole-collection null guard before locking/projection; concealed unavailable sessions; conflict classifications; dedicated internal exception for the existing transactional vocabulary recheck. |
| `Helpers/ApiErrorResults.cs` | Three category/code allowlist entries for quiz 404/409 errors and safe fixed messages. |
| `Controllers/QuizController.cs` | Existing `SuccessResponse<T>` for start/submit/history, preserving exactly `success,data`. |

Phase 3 service failures, controller error routing, missing-success-data guards, and safe internal failures were already present and remain in place. No new error envelope or service architecture was introduced.

## 4. Angular production files changed

Three files relative to `VocabularyApp.UI/src/app/`:

| File | Phase 6 contribution |
|---|---|
| `components/quiz/quiz.component.ts` | In-flight method guards; typed answer construction without assertion or undefined IDs; preserves failed submission state; explicit restart recovery for session unavailability/vocabulary change. |
| `components/quiz/quiz.component.html` | Disabled options while submitting; disabled submit during restart recovery; explicit “Start a new quiz” action. |
| `services/api-error.ts` | Quiz-submit contextual 404/409 fallbacks; conflict wording includes both processing and already-submitted states. |

`models/quiz.model.ts` and `services/api.service.ts` already satisfy the Phase 6 transport contract from Phase 2 and were reused without further edits. Existing success guards and normalization for all three quiz operations were retained.

## 5. Backend tests changed/added

`VocabularyApp.WebApi.Tests/Integration/QuizApiTests.cs` extends the existing collection, relational factory, seeding/snapshot helpers, and interceptors. No second quiz testing architecture was created.

Static authored addition count: **31 cases across 11 methods**: 5 Facts and 26 InlineData rows across 6 Theories. Cases cover ten invalid raw answer structures (including nonexistent option), omitted/empty answers, explicit zero, deterministic expiry, persisted unique-constraint conflict/rollback, six history defaults/bounds, empty history, three mode fallbacks/lifetime, transactional vocabulary recheck/retry, three invalid session IDs, and two null start binding cases. Each relevant rejection protects results, attempt/correct counters, and review/correct timestamps; invalid answer cases also retry successfully.

**Five existing methods modified** for ownership, deleted vocabulary, sequential repeat, concurrent lock contention, and unknown session. Their integrity assertions remain; unknown-session coverage now also snapshots seeded learning state. Existing Phase 3 persistence-failure 500/secrecy/rollback/retry coverage was retained unchanged in this phase.

Reused unchanged: `ApiContractShapeTests` pins exact start/submit/history fields, casing, no answer-key leakage, all three modes, count defaults/cap/eligible limit, selected-answer nullability and scoring; `ApiSafeErrorTests` covers start/history query faults; `ApiErrorBoundaryTests` covers thrown services and absent success data; R4 tests retain mixed/incorrect/unanswered scoring, forged/foreign/duplicate question IDs, invalid options, rollback, unique constraints and ownership/history isolation.

## 6. Angular tests changed/added

| File | Phase 6 contribution |
|---|---|
| `components/quiz/quiz.component.spec.ts` | **19 new cases**: three modes/start double-click guard; successful explicit-zero submit/in-flight guards/rendering; nullable unanswered submit/rendering; partial request/navigation; required UI selection; seven failure/status/code combinations; explicit recovery; populated/empty history; failed initial history; malformed submit success. Three Phase 2 cases retained. |
| `services/api.service.spec.ts` | **10 new cases**: typed start, two typed submit requests, typed history, and six error-channel cases; route/method/body/Bearer header checked. Earlier transport cases retained. |
| `services/api-error.spec.ts` | **2 new contextual cases** for quiz-submit 404/409. Prior normalization/secrecy cases retained. |
| `testing/api-contract.fixtures.ts` | Safe AE quiz fixtures and extended validation ProblemDetails fixture; existing successful fixtures retained and their quiz return shapes explicitly typed against the transport models. |

Static authored Angular addition count: **31 cases**. Total Phase 6 addition count: **62 cases**, plus five modified backend methods and retained/expanded Angular setup. These are source counts, not discovery or execution counts. No real backend is called by Angular specs. Private submission transport is exercised only in tests for valid empty/partial encoding; the normal UI still requires a selection before advancing.

## 7. selectedOptionId presence behavior

The approved plan explicitly chooses `[JsonRequired]`, retaining integer option IDs. Omission fails JSON binding with sanitized validation 400; explicit null, malformed strings, objects and fractional numbers are invalid binding inputs. Explicit zero remains present and legitimate. A well-formed nonexistent option returns 400 `invalid_quiz_answers`. No persistent field nullability or migration changed.

## 8. Null-answer-element behavior

Any null element, including a valid answer followed by null, returns 400 `invalid_quiz_answers`. The entire collection is checked before session locking, GroupBy, answer iteration/projection or scoring. No result, counter or timestamp mutation occurs.

## 9. Missing/null/empty answers behavior

Missing `answers` keeps the initialized empty list; `answers:[]` is also valid. Both intentionally score all session questions as unanswered/incorrect and persist their ordinary single attempts. Missing questions in partial submissions have the same behavior. These valid submissions therefore have intentional mutations, not rejection/no-mutation semantics. Explicit `answers:null` is invalid 400 under existing MVC implicit required validation; the service also rejects null defensively. The array itself is not marked JsonRequired.

## 10. Session 404 semantics

Unknown, nonowned, expired, and removed-after-success sessions return identical AE `404 quiz_session_unavailable`: “This quiz session is unavailable. Please start a new quiz.” Owner/existence/internal dictionary details are concealed. No database lookup or tombstone is added to distinguish previously consumed sessions.

## 11. Session 409 semantics

A detected active submission lock or recognized persisted unique-constraint duplicate returns AE `409 quiz_submission_conflict`: “This quiz submission is already being processed or has been submitted.” Missing required vocabulary returns the separate 409 code described below. Malformed submissions remain 400 rather than being indiscriminately recategorized as conflicts.

## 12. Expired-session behavior

Creation still sets UTC expiry to 30 minutes; expiry removes the session and returns concealed 404. Availability remains process-local/best-effort and can be lost on restart or cleanup; no durable availability, renewal or extended lifetime is promised. The expiry test uses test-local reflection to change the existing private session timestamp, with no arbitrary sleep and no added production hook. QuizApiCollection isolation remains.

## 13. Repeat-submission behavior

After success the existing session is removed, so sequential repeats return 404 and do not persist results, increment counters, or change timestamps again. A persisted duplicate detected while an active session is submitted returns 409 and removes that session; a later repeat is then 404. No repeat-result cache was introduced.

## 14. Concurrent-submission behavior

The existing Interlocked lock remains. The deterministic blocked-save test expects one success and one 409, with one logical result per question and one counter/timestamp update. Outside that controlled timing, a competing request arriving after removal may receive 404; the service does not promise every competing request returns 409. Existing database uniqueness remains the persistence backstop.

## 15. Vocabulary-changed behavior

Both the existing pre-scoring ownership query and the transactional requery return AE `409 quiz_vocabulary_changed`: “Your vocabulary changed. Please start a new quiz.” The latter now throws a private dedicated exception rather than an undifferentiated InvalidOperationException; transaction disposal, tracker clearing and lock release remain. No vocabulary is regenerated or quiz automatically resubmitted. A test-only one-shot command interceptor makes the second owned-word query empty to cover this branch and subsequent valid retry; it adds no production hook or actual deletion race.

## 16. Validation-before-mutation behavior

JSON binding/presence validation runs before controller/service execution. Service null guards run before locking. Duplicate, foreign/fabricated question IDs and nonexistent options are validated before scoring or persistent mutation. Empty/missing session IDs remain 400 `invalid_request`; malformed GUIDs remain validation 400. Existing lock-acquiring business-validation paths stay inside try/finally.

## 17. Scoring preservation

Question selection/randomization, prompts/options, four-word minimum, two question types/mixed mode, unknown-mode normalization, default question count 10/cap 20 and eligible-vocabulary limits remain unchanged. Correct answers remain absent from start responses. Submission still computes correctness from session option IDs, includes unanswered questions as incorrect, and rounds percentage to two decimals. Start null body/mode implicit binding behavior is preserved.

## 18. UserWord counter preservation

Each persisted question increments TotalAttempts once and sets LastReviewedAt to the submission's common UTC attempt timestamp. Correct answers alone increment CorrectAnswers and set LastCorrectAt. Incorrect/unanswered answers retain prior LastCorrectAt. Rejected submissions and rolled-back attempts leave all these fields unchanged. No R5 identity or vocabulary mutation behavior changed.

## 19. QuizResult persistence guarantees

Successful submissions still persist one row per question, correct user/session/UserWord IDs, existing QuizType, nullable unanswered UserAnswer, correct answer text and shared attempted timestamp. Existing database uniqueness and FK mappings remain unchanged. Duplicate rejection preserves prior rows and rolls back all attempted new rows/learning updates.

## 20. Transaction/rollback preservation

Existing execution strategy, tracker clearing before each transactional attempt, owned-word requery, explicit transaction, cloned result insertion, SaveChanges and Commit order remain. Failed transactions are disposed/rolled back before service catches clear the tracker. Generic persistence failures remain safe `500 internal_error`; the existing injected failure test still requires no partial mutation and a successful deliberate retry. No transaction logic was replaced.

## 21. Session-lock preservation

TryBeginSubmission/Interlocked, submissionCompleted, finally and ReleaseSubmission remain. Pre-lock failures acquire no lock; in-progress rejection leaves the owning request's lock alone; validation/vocabulary/internal failures after acquisition release it in finally. Completed success or detected persisted duplicate removes the session using the existing terminal behavior. No extra locking/state system was added.

## 22. Angular transport alignment

Existing typed start/submit/history generics and required DTO fields remain authoritative. Submitted answers explicitly include questionId and numeric selectedOptionId, including zero; unselected questions are omitted from the answer list rather than encoded with undefined. `selectedAnswer:string|null`, GUID/date strings, `scorePercentage`, `attemptedAtUtc`, and the unchanged `success,data` envelopes are retained. No active quiz transport any, new date conversion or API service redesign was added.

## 23. Angular error behavior

Shared Phase 2 normalization handles validation/business 400, session 404, both conflict 409 codes, authentication 401 and safe internal 500. Contextual fallbacks also handle code-less quiz-submit 404/409. Failed submit retains session, selections and absence of a result; loading flags reset. Session 404 or vocabulary conflict disables resubmission and offers explicit restart. Generic submission conflict shows its own message and leaves deliberate user recovery available. Failed history retains previous items and shows an error, never a successful empty-history state. Raw exceptions are not rendered.

## 24. No-auto-retry guarantee

No RxJS/HTTP retry, background resubmission, auto-restart or replacement-session request was added. Both public navigation/start methods and private submission guard in-flight requests; option buttons disable while submitting. Restart makes a request only on explicit user action. Existing backend concurrency protection remains independently necessary.

## 25. Existing expectations intentionally changed

Relative to the Phase 6 entry state: nonowner and unknown session 400→404; repeat after success 400→404; known in-progress submission 400→409; deleted required vocabulary 400→409. All five changes add exact safe AE/code assertions and preserve integrity assertions. Persistence failure 400→500 was already a Phase 3 change and was not redone or weakened. Newly rejected omitted selectedOptionId and null elements are approved contract corrections. Successful field names/casing/nulls are unchanged.

## 26. Phase 7+ items deferred

No Phase 7 cleanup, dormant DTO removal, legacy AddWordRequest removal, broad OpenAPI/XML-comment revision, or final contract reference was begun. Phase 8 restores/builds/tests/full regression remain deferred. Durable quiz sessions, guaranteed retry results, UTC timestamp redesign, new expiry refresh, account/provider/vocabulary redesign and CI/CD changes remain outside scope.

## 27. Blockers/discrepancies

No implementation blocker identified by static inspection. The user's explicit prohibition on added production expiry hooks overrides the plan's suggested internal helper; narrowly scoped test-local reflection provides deterministic expiry instead. That test intentionally depends on existing private names and must be reviewed if session implementation later changes. The transactional recheck test assumes the existing two owned-word reads and SQLite SQL shape; it asserts its injection triggered. Neither path has been executed. Framework binding, compilation, relational behavior, interceptor SQL and Angular template behavior await Phase 8 verification.

## 28. Test execution status

**NO TESTS WERE RUN.** No backend/Angular test, filtered test, Jasmine/Karma, PowerShell/CI test or equivalent command was executed. Authored runtime assertions are pending, not proven passes.

## 29. Build/restore status

**NO BUILDS WERE RUN. NO RESTORES OR NPM INSTALLS WERE RUN.** No standalone TypeScript compilation, package installation/update, app startup, HTTP request, provider call, database verification, EF or deployment command was executed. Inspection was limited to source/document reads, hashes, edits and permitted Git inspection.

## 30. Database/migration status

No database entities, mappings, indexes, migrations, snapshots or schema files changed in Phase 6. Test fixture source additions do not constitute database execution. No database was opened or verified by this task.

## 31. CI/CD/dependency status

No CI/CD, production release guard, deployment, environment protection or concurrency configuration changed. No NuGet/npm dependency was added or updated.

## 32. Git operations

Only status/diff/diff --check inspection; no staging, commit, push, merge, rebase, reset, revert, clean or deployment. Repository-local git-dir/work-tree flags were used for inspection because the installed Git reports an ownership mismatch through its ordinary discovery path; no repository/global Git configuration was changed. Entry hashes distinguish Phase 6 files from existing Phase 1–5 changes; unrelated baseline files remain unchanged.

## 33. Phase 6 readiness assessment

Static review covers the entire Phase 6 file inventory and confirms compatible success fields, presence/null handling, approved statuses, unchanged R4 scoring/transaction/mutation/lock mechanisms, typed Angular transport, shared safe errors and no automatic retry. Whitespace diff inspection is the only executable Git check; it is not compilation or runtime verification. Phase 6 is **IMPLEMENTED — EXECUTION VERIFICATION PENDING**. Phases 7 and 8 have not begun; no R7 completion or pass claim is made.

## Later verification — R7 Phase 8 (October 2, 2026)

The pending/no-execution statements above describe this phase at its original completion and remain historical evidence. The cumulative R7 implementation was subsequently execution-verified in Phase 8: final full backend suite **454 passed, 0 failed, 0 skipped**; final full Angular suite **193 passed, 0 failed, 0 skipped**; Release solution and Angular production builds passed. Phase 8 corrected anonymous Swagger serialization, stale owned-definition and SQLite timestamp assertions, and malformed XML documentation; see the complete failure history and warnings in [R7 Phase 8 verification results](R7-api-contracts-phase-8-verification-results.md).

Current cumulative status: **R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED**, within the local verification limits recorded there. Existing dependency vulnerabilities remain a separate release-review concern. No production access, live dictionary-provider calls, commit, push or deployment occurred. This later note does not claim execution took place during the original phase.
