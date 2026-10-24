# F6 — Phase 3 implementation results

Date: 2026-10-13 (America/New_York). Scope: **Backend Scheduling and Review Services only**. Assessment: **CONDITIONAL COMPLETE** — authorized local backend implementation and verification are complete; SQL Server rehearsal and P6–P7 remain release gates.

## 1. Executive summary

Implemented the four authenticated review routes, interval-v1 orchestration, durable sessions and snapshots, atomic answer/history/receipt persistence, protected backlog cursors, independent runtime flags, transactional new-word enrollment, preferred-meaning resets and controlled canonical-content maintenance. Existing quiz statistics and R7 response contracts remain separate and unchanged.

Verification: a broader filter selected **661 backend tests, all passed**. After subsequent targeted refinements and additional tests, **343 focused tests passed**; the final maintenance-gate change then passed **5 content tests**. These runs overlap and must not be summed as distinct test coverage. SQL Server was never connected to; no production readiness is claimed.

## 2. Authorized scope and repository baseline

Only Phase 3 backend code, focused tests, OpenAPI metadata and documentation were implemented. No Angular, deployment workflow, dependency, schema or migration change was made. No migration/enrollment SQL was executed. No automatic migration execution, commit, push, branch change, merge, deployment or production flag/configuration change occurred.

`git branch --show-current`, `git status --short` and `git log -5 --oneline` were rejected by the ownership safety check. No Git safety override or global configuration change was made. Read-only `.git/HEAD`, branch ref and decompressed loose commit objects established:

- Branch: `feature/f6-spaced-repetition`.
- Head: `ebb0e8a43d515eda30ed5e099a787d8ff265cf0b`, “Complete F6 Phase 2 database schema and migration design”.
- Direct parent: `99fdbc54e46ece48bb7be1d36ac9e07cebb556c6`, the approved Phase 1 commit.

A read-only Git index v2 comparison hashed tracked files as Git blobs, allowing CRLF normalization. It found only the intended tracked source changes and no missing tracked files. This is a safe preservation check, not an assertion that normal Git status succeeded or that the initial checkout was clean. The index and Git objects were not written.

## 3. Approved decisions honored

D1/P1: the unchanged Phase 1 scheduler supplies every enrollment, reset and answer transition; intervals remain 1/3/7/14/30/60/90 elapsed UTC days, Reviewing begins after three successes, failures increment lifetime lapses. D2/P2: one effective meaning per saved word, genuine changes reset without synthetic history. D3: reviews do not write quiz fields/results. D4: four usable saved words, distinct unambiguous options, non-due distractors and safe availability. D5/P3/P4: the existing four persistence entities provide durable snapshots, recovery, immutable receipts and retention. D6: exactly four added routes and existing envelopes. D7: full due backlog, no hidden daily limit or invented mastery. D8: schema-first deployment and separate SQL Server rehearsal/release authorization. D9: binary outcomes and interval-v1 history; no future algorithm or frontend scope added. P5 feature separation remains intact. P6–P7 are unresolved production gates.

## 4. Files created and modified

Created under `VocabularyApp.WebApi`:

- `Controllers/ReviewsController.cs`.
- `Services/IReviewService.cs`, `ReviewService.cs`, `ReviewStateCoordinator.cs`, `ReviewContentChangeService.cs`.

Created under `VocabularyApp.WebApi.Tests`:

- `Integration/ReviewApiTests.cs`, `ReviewSessionTests.cs`, `ReviewEnrollmentTests.cs`, `ReviewContentChangeTests.cs`, `ReviewConcurrencyTests.cs`, `ReviewContractTests.cs`.
- `Infrastructure/ControllableTimeProvider.cs`, `ReviewTestHost.cs`, `ReviewPersistenceFailureInterceptor.cs`, `ReviewSynchronizationInterceptor.cs`.

Created this results document. Modified:

- `VocabularyApp.WebApi/Program.cs` — DI, options, TimeProvider and Data Protection registration.
- `VocabularyApp.WebApi/Services/WordService.cs` — enrollment and preferred-definition coordination; bounded retries when coordination is enabled; deterministic fallback tie-breaker for adds.
- `VocabularyApp.WebApi/Helpers/ApiErrorResults.cs` — approved review error allowlist pairs.
- `VocabularyApp.WebApi/Swagger/ApiContractOperationFilter.cs`, `ApiContractSchemaFilter.cs` — review action/request metadata and defaults.
- `VocabularyApp.WebApi/DTOs/ReviewDTOs.cs`, `Configuration/ReviewOptions.cs` — comments updated; approved DTO members/defaults unchanged.
- `VocabularyApp.WebApi.Tests/Infrastructure/IndependentConnectionDatabaseFixture.cs` — expose isolated connection string for test hosts with separate connections.
- `VocabularyApp.WebApi.Tests/Integration/ApiContractMetadataTests.cs`, `OpenApiContractTests.cs` — preserve original action assertions and add four review actions.
- `Docs/Updates/F6-Spaced-Repetition/F6-review-api-contract-reference.md` — link implemented Phase 3 status while preserving the original contract specifications.

Generated bin/obj/test outputs changed through compilation. TRX evidence resides in `VocabularyApp.WebApi.Tests/TestResults/` and is not deployment input.

## 5. Review service architecture

Scoped IReviewService/ReviewService orchestrates the existing DbContext, singleton IReviewScheduler and TimeProvider, live IOptionsMonitor flags, Data Protection and independently scoped recovery contexts. ReviewStateCoordinator owns shared effective-meaning/usability rules and domain/entity mapping. ReviewsController handles authenticated identity, validation/error/success projection and safe failure logging; scheduling and database business logic stay in services.

Selection uses a valid explicit preferred definition in the saved POS; an explicitly blank preferred definition is blocked. Otherwise it chooses the first usable definition in saved POS ordered by DisplayOrder then ID. Unicode whitespace checks match .NET semantics. Content comparison uses FormKC normalization and collapsed whitespace; option ambiguity also compares case-insensitively.

## 6. Endpoint behavior and exact contracts

| Route | Implemented behavior |
|---|---|
| GET `/api/reviews/due` | Owner-scoped inclusive due backlog, blocked rows included with isUsable=false, oldest due then UserWordId; default 20, maximum 100. Protected owner/asOf/last-order cursor valid for less than 30 minutes. Invalid/tampered/cross-owner/expired cursors use validation_failed. |
| GET `/api/reviews/summary` | Due includes blocked; New counts all New states; overdue is strictly earlier; blocked counts unusable due meanings; usableDue=due-blocked; total counts all enrolled; nextReviewAt is the earliest future instant or null. No quiz-derived mastery/progress. |
| POST `/api/reviews/start` | Empty/unavailable are successful availability DTOs without a persisted session. Ready contains up to requested 1–20 usable due targets, four distinct choices and immutable snapshots. Optional owner-scoped resume returns existing items and stored accepted receipts. |
| POST `/api/reviews/submit` | Validate owner/session/item/option/expiry/current source meanings/revisions/target schedule; score the stored option key; atomically persist one scheduler transition, history, item acceptance/receipt and session progress. Exact accepted key replays original receipt. |

Success is exactly SuccessResponse<T> with success/data. Pending questions reveal only public optionId/text, never source IDs, revisions or answer keys. Zero is a valid selectedOptionId. Binding/annotation validation retains extended ValidationProblemDetails and problem+json. Application failures retain R7's six-field envelope and allowlisted safe messages. All routes advertise Bearer/401 and safe 500; disabled routes advertise 503; resume/submit advertise 404/409; applicable actions document validation and business 400 media types.

The due contract provides selected-meaning availability through isUsable; it deliberately has no public definition identity/text fields. The implementation follows the approved DTO rather than adding properties implied by broader task wording.

## 7. Scheduling integration

Enrollment/reset/review call only IntervalV1ReviewScheduler through its interface. Server UTC validation time anchors acceptance. EF's existing UTC converters and review DTO converters preserve UTC Kind and Z output. ScheduleVersion advances once for a newly accepted answer or genuine reset and never for session creation, replay or same-effective-meaning selection. History stores the complete before/after scheduling state, elapsed ticks, immutable outcome and interval-v1 version.

## 8. Enrollment behavior

With EnrollmentEnabled=true, a genuinely new tracked saved word and exactly one initial state are committed together under a serializable transaction. Scheduler enrollment is New, immediately due, version 1, no history. Duplicate adds retain original vocabulary acknowledgements and return the existing identity before selection changes. Recognized identity races and bounded transient retries reread the winner; the state primary key enforces one state per saved word. With enrollment disabled, no state or review enrollment hook is invoked. Legacy enrollment scripts remain separate operator operations.

## 9. Preferred-definition resets

With coordination enabled, preferred selection/POS and any schedule reset share a serializable transaction. Compare effective definition IDs before/after; explicitly selecting the current fallback is a scheduling no-op. A genuine change resets to New at server UTC time, advances version, records selected identity/revision and preserves enrollment instant, lifetime lapses, history, notes, favorite and every ordinary quiz field. State-version/selection checks invalidate pending snapshots without rewriting them. Failed reset transactions preserve the old preference and state.

## 10. ContentRevision coordination

ReviewContentChangeService is a DI maintenance entry point with methods for definition text/POS/membership/example/order changes, definition addition/removal and word-text changes. It is not exposed by HTTP and has no background/import caller. Genuine semantic edits increment ContentRevision and reset affected enrolled selections atomically; normalized whitespace/example-only edits preserve revision/scheduling. Fallback reorder/add/removal resets only changed effective selections; removals clear/repoint current references while preserving historical snapshots. Word-text changes advance associated definition revisions.

Maintenance fails closed when EnrollmentEnabled=false, preventing content mutations that would silently leave enrolled schedules stale while review hooks are gated. A separately authorized maintenance caller must use a fresh scope and enable coordination in an appropriate environment; large fan-outs require a maintenance window. No production operator interface or canonical editing UI was introduced. Unknown maintenance commit outcomes must be inspected in a fresh scope; this service does not blindly retry non-idempotent additions/removals.

Target and distractor selected IDs/revisions/text are checked on pending resume/submit. Normalized snapshot comparison additionally detects out-of-band definition text edits without revision advancement. Direct SQL edits while reviews are active remain unsupported; they require separately authorized reconciliation and coverage verification before activation, not merely a session restart.

## 11. Session lifecycle and retention

Sessions expire exactly 30 minutes after creation; now>=expiry rejects new answers and resume. Resume never extends expiry or regenerates questions. Accepted items expose persisted receipts, pending items remain unanswered, completion means all items accepted. Abandonment/expiry creates no outcome or transition. Complete or partially accepted sessions/items/history are retained. New successful session/answer operations opportunistically clean up at most 100 expired sessions having zero accepted items, in a separate transaction, items before sessions. Cleanup failure cannot negate a committed answer; GET performs no cleanup mutation. No saved-word/account deletion endpoint was added.

## 12. Idempotency and concurrency strategy

Receipt keys are `(UserId,SubmissionId)` plus immutable session/item/option identity. Replay precedes expiry/content/schedule validation. Reusing a key for another payload or an accepted item with another key yields review_submission_conflict. Persisted receipts are checked against their history identity/outcome and returned without recomputing time, due state or summary.

Answers use serializable transactions and the existing EF ScheduleVersion/session Version tokens, definition revisions, owner FKs and history/item uniqueness. Reads validate canonical sources before saved-word/state mutations; provider execution strategy wraps answer transactions. Current SQL Server registration has no provider automatic retry policy; the service applies at most three attempts with jitter for concurrency losses, recognized accepted-key uniqueness, SQL Server deadlock/transient codes and SQLite BUSY/LOCKED. No process lock is correctness authority. Retries retain the logical submission key and reload tracking. Independent items may retry session-version loss; concurrent sessions targeting one state leave the loser stale.

Every thrown mutation/commit attempt performs a fresh scoped receipt lookup before a retry decision. A committed receipt recovers even a lost commit acknowledgement; unknown failure with no receipt returns safe infrastructure failure rather than assuming rollback or inventing a conflict. Database-unavailable recovery also remains a safe 500; clients must preserve the original request key for later replay.

## 13. Transaction boundaries and query limits

Each new answer writes state/history/item/receipt/session together. Start stores session/items through one atomic SaveChanges; target/source validation is repeated at acceptance, so starting reserves nothing. Enrollment stores vocabulary/state together. Preferred changes and content maintenance coordinate their own serializable transactions. Summary uses a consistent read transaction so concurrent rescheduling cannot make blocked/due counts inconsistent; all count/earliest-future queries run server-side. Unicode usability checks inspect only bounded due batches. Due/start use keyset batches; distractor search stops when four valid choices are found and does not retain the entire vocabulary. These scans can still inspect a large ambiguous backlog; production query plans, lock durations and representative-volume timings remain rehearsal work.

## 14. Security and ownership

Existing JWT authentication, Authorize and NameIdentifier conventions apply. Every session/item/state/history lookup and source vocabulary read is owner-scoped. Absent, nonowned and wrong-session items are concealed as review_session_unavailable. Correctness, intervals, versions and timestamps are server-authoritative. Provider exception text, connection strings, raw SQL and user content are not returned by review failures; logs contain only exception type for review orchestration. Controlled maintenance is not a public editing authorization surface.

## 15. Feature flags and deployment safeguards

Reviews:Enabled and Reviews:EnrollmentEnabled remain false by default, bound to the existing options class without altering any appsettings/production configuration. All four review routes return 503 reviews_disabled before querying review data when Enabled=false. EnrollmentEnabled independently gates new enrollment and preferred-definition coordination; controlled content maintenance rejects disabled coordination. Tests enable flags only in isolated hosts. Test Data Protection uses per-host ephemeral keys and never requires profile-key writes.

**The new binary requires the additive F6 schema, including WordDefinition.ContentRevision, even with both flags false.** The unchanged migration `20261013014406_AddSpacedRepetition` must precede a separately authorized application release. Flags suppress behavior, not mapped-column requirements. Production key storage, enrollment coverage, writer coordination and P6–P7 remain activation prerequisites.

## 16. Tests and exact verification results

Executed from the repository root using existing restored assets; no package upgrades, live dictionary calls or production data:

```powershell
dotnet build VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --no-restore
```

Initial build: exit 0, 0 errors, 6 existing xUnit analyzer warnings.

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --no-restore --filter 'FullyQualifiedName~Review|FullyQualifiedName~QuizApiTests|FullyQualifiedName~Vocabulary|FullyQualifiedName~ApiContract|FullyQualifiedName~OpenApiContract|FullyQualifiedName~ApiValidation|FullyQualifiedName~ApiSafeError|FullyQualifiedName~ApiErrorBoundary|FullyQualifiedName~Dictionary' --logger 'trx;LogFileName=f6-phase3-focused.trx'
```

Exit 0: **661 passed, 0 failed, 0 skipped**, reported duration 1m23s. The Vocabulary substring matches the namespace, selecting the full backend suite at this checkpoint. This includes all original R7 actions, quiz/dictionary/authorization regressions and Phase 1/2 scheduling/schema tests.

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --no-restore --filter 'FullyQualifiedName~Review|FullyQualifiedName~VocabularyContractTests|FullyQualifiedName~VocabularyOwnershipApiTests|FullyQualifiedName~ApiContractMetadataTests|FullyQualifiedName~OpenApiContractTests|FullyQualifiedName~QuizApiTests' --logger 'trx;LogFileName=f6-phase3-final.trx'
```

After refinements/additional races/contracts: exit 0, **343 passed, 0 failed, 0 skipped**, duration 48s.

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --no-restore --filter 'FullyQualifiedName~ReviewContentChangeTests' --logger 'trx;LogFileName=f6-phase3-content-final.trx'
```

After the last fail-closed maintenance change: exit 0, **5 passed, 0 failed, 0 skipped**, duration 5s. Each test command compiled current source. No additional unrelated rerun was needed after this isolated final change.

New integration coverage includes all routes/authentication/owner isolation/exact shapes, disabled behavior, validation, full backlog/Unicode-blocked words/cursor protection, availability/minimum/non-due distractors, option zero, expiry equality/abandonment/cleanup, durable recovery across a new host, original receipt/summary replay, conflicting keys/payloads, scheduler ladder/cap/failure persistence, preference no-op/reset/history/lapse/quiz preservation, content normalization/revision/fallback/distractor staleness, out-of-band text guards, actual-write rollback and commit acknowledgement loss.

Separate-connection SQLite cases cover identical answers, different keys/same item, same key/different payload, different sessions/same target, independent items/same session, concurrent vocabulary adds, and preference/content changes during an in-flight request. Gates use task barriers, not timing sleeps. Controlled BUSY failures verify success after two retries and safe exhaustion after three failed attempts. Existing Phase 2 tests cover unique/FK/check enforcement. Actual relational command failures after state/history/item writes and before commit verify no partial state/history/receipt/session writes; a postcommit injected exception verifies receipt recovery.

Development runs also executed the narrower API/session and Review filters. Intermediate outcomes were 23/24, 198/200 and 286/287 passes; corrected defects were SQLite aggregate translation, test-only profile Data Protection access, double deletion in removal coordination and an incorrect test assumption that flattened query DTOs appear as OpenAPI components. Two transient new-test compile errors were corrected. The final TRX file overwrote an earlier failing run and emitted an overwrite warning. Earlier failure artifacts are not evidence of the final result.

Six pre-existing warnings remain: xUnit2031 in ApiContractMetadataTests (two) and ApiContractShapeTests (three), and xUnit1026 in ApiContractMetadataTests (one). No new final production-code warning remains. No Release build, Angular test or SQL Server execution ran.

## 17. SQL Server verification limitations

No explicitly authorized disposable SQL Server database was supplied. The Phase 2 live migration/enrollment rehearsal remains **pending**, as do SQL Server-specific filtered uniqueness, serializable lock/deadlock races, transient retry/commit ambiguity, datetime2 precision, query plans, backup/restore and old/new binary additive-schema compatibility. SQLite independent connections prove the tested SQLite behaviors, not SQL Server concurrency correctness. Follow `../Phase-2/F6-phase-2-sql-server-rehearsal.md` with separate target authorization and extend it with the Phase 3 answer/preference/content/session race matrix.

## 18. Discrepancies and technical debt

No schema defect, replacement algorithm or change to approved wire contracts was required. The corrected schema-before-binary requirement supersedes the old plan's incompatible pre-F6-schema smoke expectation. Historical Phase 1/2 scope/authorization wording remains historical; this assignment authorized Phase 3 only.

Maintenance defaults to fail-closed coordination, not an invented public editor. Large fan-out operator tooling, direct-SQL reconciliation and separately authorized account/word deletion workflows remain unavailable. Maintenance unknown-commit recovery is operator inspection, not automatic duplicate mutations. Production Data Protection key persistence/permissions and query/lock tuning remain P6/P7/rehearsal work. Normal Git status and initial staged/unstaged classification remain blocked by ownership checks, although read-only branch ancestry and index preservation were verified.

## 19. Phase 4 prerequisites

Developer review of the backend implementation and this report, followed by separate Phase 4 authorization. Angular should use the existing exact DTOs, explicit empty/unavailable/error states, immutable submission keys, replay-before-resume recovery and stored accepted receipts. Production activation is still contingent on additive-schema rehearsal, enrollment/writer coverage, SQL Server race evidence and P6–P7; backend local acceptance does not authorize those operations.

## 20. Final readiness assessment and stop point

**CONDITIONAL COMPLETE — Phase 3 only.** Backend implementation and safe local verification are complete. Outstanding SQL Server rehearsal and production-release evidence prevent a production-readiness claim. Development stops here for developer review. No Phase 4, commit/push/merge, deployment, production migration/enrollment or flag activation is authorized or performed.
