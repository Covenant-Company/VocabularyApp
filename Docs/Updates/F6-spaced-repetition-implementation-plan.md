# F6 — Spaced Repetition Implementation Plan

Date: 2026-10-12. Repository: Covenant-Company/VocabularyApp. Working branch recorded in `.git/HEAD`: `feature/f6-spaced-repetition`.

Original status: **PLANNING ONLY — CONDITIONAL GO**. The original planning assignment changed only this document and ran no builds/tests or migrations. **Approval update, 2026-10-12:** the developer formally approved D1–D9 and P1–P5 and authorized Phase 1 implementation and focused tests only. P6–P7 remain unresolved production-release verification gates. Phase 2 and later implementation, migrations, deployments, commits and pushes remain unauthorized. Commands in later phases remain proposals, not execution records; Phase 1 execution is recorded in [its results](F6-phase-1-implementation-results.md).

## 1. Executive Summary

Add an independent, server-authoritative review lifecycle using the approved `interval-v1` ladder and four durable entities. Existing saved-word identity, preferred meanings, quiz statistics, QuizResults and all 14 R7 endpoint contracts remain intact. Reviews persist one answer at a time, with atomic scheduling/history/receipt writes and portable database concurrency tokens.

The developer has approved the P1–P5 product/technical choices in section 18 and authorized Phase 1 only. SQL Server rehearsal, P6–P7 and production release authorization remain separate gates. The production pipeline deploys master automatically but does not apply migrations; schema preparation must precede the application release, and review activation must follow enrollment verification.

## 2. Approved Architectural Decisions

These decisions are binding, supplied by the developer rather than inferred from the earlier analysis:

| Decision | Required implementation behavior |
|---|---|
| D1 | Deterministic versioned intervals 1, 3, 7, 14, 30, 60, 90 elapsed UTC days; success advances, failure resets to a one-day delay, maximum 90 days; accepted server time anchors the next interval. |
| D2 | One selected meaning per UserWord; genuine preferred-definition changes reset scheduling, preserve history and quiz statistics, retain `(UserId,WordId)` identity; same-definition updates preserve progress. |
| D3 | Dedicated reviews and ordinary quizzes have independent statistics and persistence; preserve R7. |
| D4 | Multiple choice, four usable saved words minimum, non-due distractors allowed, distinct unambiguous options, safe availability result. |
| D5 | UserWordReviewState, ReviewHistory, ReviewSession and ReviewSessionItem; relational ownership/integrity, durable sessions, portable concurrency and idempotency; SQL Server and SQLite final-model compatibility. |
| D6 | Add only GET due, GET summary, POST start and POST submit under `/api/reviews`; finalize DTOs and lifecycle using R7 conventions. |
| D7 | Existing vocabulary enrolls as New; no mastery inference or fabricated history; preserve personal data and relationships; full backlog, bounded sessions, idempotent catch-up. |
| D8 | SmarterASP.NET/SQL Server; explicit migration owner, backup/rehearsal, coordinated release and schema-before-activation. No production migration authorization. |
| D9 | Responsive and accessible interaction; true binary outcomes and versioned history; explicit retention/deletion policy; future algorithms remain possible without implementation now. |

The original proposed technical specifications below are preserved. **P1–P5 are now approved** alongside D1–D9; P6–P7 remain unresolved as recorded in section 18. Approval of the design authorizes implementation of Phase 1 only under the current assignment.

## 3. Verified Repository Baseline

The complete [analysis](F6-spaced-repetition-analysis.md) was read, and the following current sources were reinspected. Paths in that analysis abbreviated `UI` and `WebApi.Tests`; actual directories are `VocabularyApp.UI` and `VocabularyApp.WebApi.Tests`.

| Current evidence | Verified baseline and consequence |
|---|---|
| `VocabularyApp.Data/Models/UserWord.cs`, `Models/WordDefinition.cs`, `ApplicationDbContext.cs` | UserWord has mapped preferred meaning, notes, favorite and quiz counters/timestamps; DifficultyLevel/CustomDefinition/CreatedAt are unmapped. Unique identity is `(UserId,WordId)`. Definition length is 1000. No review entities or content revision exist. |
| `VocabularyApp.Data/Migrations/ApplicationDbContextModelSnapshot.cs` | EF model uses ProductVersion 8.0.10 and existing identity/quiz uniqueness relationships. Additive mapping and snapshot changes are required. |
| `Migrations/20260829155134_CorrectUserWordIdentity.cs`, `20260819000000_AddQuizResultSubmissionUniqueness.cs`, preferred-definition migration | R5 migration includes SQL Server IF/THROW; SQLite cannot be assumed to execute the historical chain. Existing IDs must not be rewritten. |
| `VocabularyApp.WebApi/Services/WordService.cs` | Add returns an existing entry before selection changes; recognized identity races reread the winner. Preferred update writes the same row and POS without scheduling. Lookup inserts canonical definitions; no canonical edit or saved-word deletion API was found. Fallback ordering lacks an ID tie-breaker. |
| `Services/QuizService.cs`, `Controllers/QuizController.cs`, `DTOs/QuizDTOs.cs` | Static process-local 30-minute sessions; max 20 questions; distractor selection does not ensure distinct definitions. Submit writes quiz counters/timestamps and QuizResults transactionally. Reviews must not call quiz submission or alter its lifecycle. |
| `Controllers/WordsController.cs`, `Controllers/UsersController.cs`, DTOs, `Models/ServiceResult.cs` | Existing controller/DTO surface corresponds to R7's 14 actions; ServiceFailureType already supports Conflict and ServiceUnavailable. |
| `Helpers/ApiErrorResults.cs`, `ApiValidationResponses.cs`, `Swagger/ApiContractOperationFilter.cs`, `ApiContractSchemaFilter.cs` | Error category/code allowlist is explicit; unknown codes become 500. Request-type classification and action metadata need additions. Validation sanitizes field names/messages. Required zero-valued option IDs need presence validation. |
| `Program.cs`, project files | .NET 8, EF SQL Server, scoped services, typed HTTP WordService, static Angular hosting and Swagger; no runtime Migrate/EnsureCreated. TimeProvider can use .NET 8 without a package. |
| `VocabularyApp.UI/src/app/app.routes.ts`, dashboard and quiz components/templates/specs | Standalone guarded routes; dashboard has no real review counts. Quiz submits at session end and supports backward navigation. Dedicated per-answer review component is needed. |
| Angular `services/api.service.ts`, `api-error.ts`, their specs, `testing/api-contract.fixtures.ts`, `package.json` | Typed get/post with bearer headers and error normalization; Angular 18/Jasmine/Karma/ChromeHeadless. Add review operation contexts and safe validation fields. |
| `VocabularyApp.WebApi.Tests/Infrastructure/RelationalDatabaseFixture.cs`, `VocabularyAppWebApplicationFactory.cs` | FK-enabled private in-memory SQLite and EnsureCreated; hooks permit service replacement and failure injection. Shared connection fixtures do not prove cross-connection concurrency. |
| Integration `QuizApiTests.cs`, `ApiContractMetadataTests.cs`, `OpenApiContractTests.cs` and existing error/shape/validation tests | Preserve regression coverage; exact 14-path assertions must deliberately become 18 while retaining the original 14 assertions. |
| `.github/workflows/backend-tests.yml` | Master push runs backend/Angular tests, builds/stages/publishes, then automatically Web Deploys with a serialized production job and HTTPS acceptance. Node 22 is the workflow baseline. |
| `scripts/ci/Publish-VocabularyApp.ps1`, `Deploy-SmarterAsp.ps1`, `Docs/Deployment/SmarterASP-Manual-Deployment.md` | Artifact/deploy safeguards reject SQL payloads; no migration step. Publish pins appsettings.json to a reviewed digest; deploy uses AppOffline and destination-file preservation. Older manual guide details are historical where they differ from current scripts/workflow. |

Git status and branch commands were rejected by the repository ownership safety check. No global, persistent or command-local safe-directory override was added. `.git/HEAD` supplies the branch name, but the exact commit and clean working tree are **unverified**. No AGENTS.md was found by repository listing or checked ancestor paths. Production data, EF history, permissions, capacity and backup capabilities are unknown. Historical test results in R7 documentation are not verification performed here.

## 4. Scope and Non-Goals

Scope: additive review model, pure scheduler, dedicated authenticated APIs, transactional vocabulary enrollment/reset integration, guarded Angular review experience, meaningful tests, SQL Server rehearsal and a separately approved release runbook.

Non-goals: SM-2/FSRS, grades inferred from correctness or speed, typed recall, self-rating, multiple independently scheduled meanings, daily new-word caps, hidden/staggered backlog, suspension UI, a new worker/queue, quiz redesign, public history/deletion/content-edit endpoints, broad frontend refactoring, automated production migrations or dependency auditing. Do not add optional scheduling fields to existing wire DTOs. Existing quiz concurrency and process-local session limitations remain separate technical debt.

## 5. Final Proposed Scheduling Rules

Use one pure `IReviewScheduler` implementation, `IntervalV1ReviewScheduler`. Inputs are validated prior state, genuine binary outcome and explicit UTC accepted time; output is a transition value, without EF, clocks, randomization or network dependencies. Inject TimeProvider into orchestration, not into the algorithm.

| Event | ConsecutiveCorrectCount | ReviewIntervalDays | NextReviewAt | Status |
|---|---:|---:|---|---|
| Enrollment/reset | 0 | 0 | enrollment/reset UTC time | New |
| First correct, including after failure | 1 | 1 | accepted time + 24 hours | Learning |
| Correct at count 1 | 2 | 3 | accepted time + 72 hours | Learning |
| Correct at count 2 | 3 | 7 | accepted time + 168 hours | Reviewing |
| Correct at count 3 | 4 | 14 | accepted time + 336 hours | Reviewing |
| Correct at count 4 | 5 | 30 | accepted time + 720 hours | Reviewing |
| Correct at count 5 | 6 | 60 | accepted time + 1440 hours | Reviewing |
| Correct at count 6 or 7 | 7 | 90 | accepted time + 2160 hours | Reviewing |
| Any incorrect | 0 | 1 | accepted time + 24 hours | Learning |

**P1:** Reviewing starts at count 3. Do not claim permanent mastery; the 90-day cap is still Reviewing. Status values are New=0, Learning=1, Reviewing=2; no unused Suspended value in F6. Failure increments lifetime LapseCount; resets preserve lifetime lapse count. Reset clears current LastReviewedAt, count and interval, but retains EnrollmentAt and historical events. Enrollment initializes lapse=0. New rows can be unusable; usability is derived separately from schedule status.

Due means `NextReviewAt <= asOfUtc`; overdue means `< asOfUtc`. New is never excluded by a daily limit. A due word stays due until a review commits; start, abandonment, invalid answers, expired requests and errors do not create attempts. No overdue bonus. Exactly one UTC day is 24 hours across DST and leap dates. Reject invalid scheduler states or overflow before mutation. SQL datetime2(7)/DateTime preserves tick precision; mark UTC on materialization and serialize `Z`, including SQLite values with unspecified Kind.

AlgorithmVersion remains `interval-v1` for all F6 transitions. ScheduleVersion is a positive checked Int64 optimistic concurrency token, initially 1, incremented for every accepted answer and genuine reset; never increment for start, replay or same-definition update. Historical previous/next versions remain immutable. A future algorithm requires a separately approved migration and distinct version.

Acceptance: every table row is independently testable; failures then correct answers schedule one day again; day 0/1/4 successes are due at day 11; equality is due; cap successes remain 90 days.

## 6. Database Schema Design

All new names/types below are exact proposed model specifications. `int` means Int32, `long` Int64, `Guid` uniqueidentifier, `bool` bit; SQL strings use Unicode. SQLite provider mappings remain EF-native. Every new instant uses UTC DateTime/datetime2(7), with nullable variants where shown. All properties are required unless `?` appears. No provider-specific UTC defaults.

### UserWordReviewState

| Property | Type/rule |
|---|---|
| UserWordId | int PK, one-to-one dependent |
| UserId | int, ownership FK component |
| ScheduledWordDefinitionId | int?; null only when no usable selected meaning |
| ScheduledDefinitionRevision | long?; paired nullability with definition ID |
| EnrollmentAt, NextReviewAt | DateTime |
| LastReviewedAt | DateTime?; dedicated reviews in current meaning epoch |
| ConsecutiveCorrectCount, ReviewIntervalDays, LapseCount | int; 0..7, ladder/0, >=0 respectively |
| ReviewStatus | int stable enum above |
| AlgorithmVersion | nvarchar(32), `interval-v1` |
| ScheduleVersion | long >=1, EF IsConcurrencyToken, no rowversion |

FK `(UserId,UserWordId)` → UserWords `(UserId,Id)`, **Cascade**; add that alternate key while preserving the existing identity index. FK ScheduledWordDefinitionId → WordDefinitions.Id **NoAction**. The definition must belong to UserWord.WordId in service checks; cross-word FK enforcement would require extra canonical-key schema and is not silently assumed. Index `(UserId,NextReviewAt,UserWordId)` supports owner due ordering; status is not a leading filter because all statuses are eligible.

### ReviewSession

Id Guid PK; UserId int; CreatedAt/ExpiresAt DateTime; RequestedQuestionCount int 1..20; ItemCount int 1..20; CompletedAt DateTime?; Version long >=1 concurrency token. Alternate key `(UserId,Id)`; UserId → Users.Id **NoAction**. Index `(UserId,ExpiresAt,Id)` for recovery/cleanup. ExpiresAt > CreatedAt. Completion means every item accepted, not abandonment. Do not persist redundant correct/pending counters: derive inside the serialized session transaction.

### ReviewSessionItem

Id Guid PK; SessionId Guid; UserId/UserWordId/Ordinal int; CapturedScheduleVersion long; WordDefinitionId int (historical identifier, **no FK**); DefinitionRevision long; WordText nvarchar(100); DefinitionText nvarchar(1000); PartOfSpeech nvarchar(100); Prompt nvarchar(1500); QuestionType nvarchar(32) fixed `word-to-definition`; OptionsJson nvarchar(max); CorrectOptionId int 0..3; AcceptedSubmissionId Guid?; AcceptedAt DateTime?; SelectedOptionId int?; IsCorrect bool?; ReceiptJson nvarchar(max)?; ReceiptContractVersion int? (1 when accepted).

OptionsJson contains exactly four immutable records `{optionId,text,sourceUserWordId,sourceDefinitionId,sourceDefinitionRevision}`. Text supports 1000 characters. Only `{optionId,text}` projects publicly. No runtime mutable canonical reference is needed to render a session. Nullable accepted fields are all null for pending and all non-null for accepted. Unique `(SessionId,Ordinal)`, unique `(SessionId,UserWordId)`, alternate key `(UserId,Id,UserWordId)`; unique filtered `(UserId,AcceptedSubmissionId)` where non-null (provider-compatible filter must be verified in both providers). Session owner FK `(UserId,SessionId)` → ReviewSessions `(UserId,Id)` **NoAction**; saved-word owner FK `(UserId,UserWordId)` → UserWords alternate key **NoAction**. Index `(UserId,UserWordId,AcceptedAt)` for affected-session lookup.

### ReviewHistory

Id Guid PK; UserId/UserWordId int; SessionItemId/SubmissionId Guid; ReviewedAt DateTime; WordDefinitionId int historical ID (no canonical FK); DefinitionRevision long; WordText nvarchar(100); DefinitionText nvarchar(1000); QuestionType nvarchar(32); OutcomeSource nvarchar(32) fixed `server-multiple-choice`; IsCorrect bool; SelectedOptionId int; SelectedAnswer/CorrectAnswer nvarchar(1000); AlgorithmVersion nvarchar(32); PreviousScheduleVersion/NextScheduleVersion long; PreviousNextReviewAt/NextReviewAt DateTime; PreviousLastReviewedAt DateTime?; PreviousIntervalDays/NextIntervalDays/PreviousCorrectCount/NextCorrectCount/PreviousLapseCount/NextLapseCount/PreviousStatus/NextStatus int; ElapsedSincePreviousReviewTicks long?; ReceiptJson nvarchar(max); ReceiptContractVersion int fixed 1.

Unique SessionItemId and `(UserId,SubmissionId)`; index `(UserId,UserWordId,ReviewedAt,Id)`. FK `(UserId,SessionItemId,UserWordId)` → item alternate key **NoAction**; FK `(UserId,UserWordId)` → saved-word alternate key **NoAction**. This enforces event/session/target ownership. Receipt duplicated on item/history permits owner-scoped global key lookup and durable replay; write both in one transaction. History uses item snapshots for full prompt/options, so retain its accepted item and session. No fabricated recall rating or response-time field is needed; future rating fields can be nullable additions.

### Existing model additions and invariants

Add WordDefinition.ContentRevision long >=1, default 1 for existing/old-writer inserts; EF concurrency token. Add navigation collections only as needed. Do not expose revision through existing DTOs. No additional tables beyond D5. Configure new entities in `VocabularyApp.Data/Configurations/*Configuration.cs`, invoked by ApplicationDbContext.

CHECK constraints cover count/version/lapse bounds, allowed intervals/statuses, state consistency (`New/count0/interval0/lastnull`, `Learning/count0 interval1 or count1 interval1 or count2 interval3`, `Reviewing` count3..7 mapped intervals), session timing/count, option ranges, accepted-field coherence and history next-version=previous+1. JSON structure, definition membership and duplicated receipt equality are service invariants tested under both providers. Check expressions must use the shared SQL subset; provider-specific filtered-index SQL is isolated in mapping. No SQL Server multiple cascade path is introduced: only state cascades through UserWord; sessions/items/history use NoAction.

Append-only means application services cannot update/delete normal review events. A separate privileged retention/account-deletion operation may delete them under P4. No database trigger claiming absolute immutability is proposed; database administrator permissions must remain restricted operationally.

## 7. EF Core Migration and Backfill Strategy

One additive schema migration, proposed name `AddSpacedRepetition`, follows the actual verified latest migration at implementation time. Add UserWord alternate key and ContentRevision; create state/session tables, then items, then history; add constraints/indexes; update designer and model snapshot together. **Do not generate it during this assignment.** Review generated SQL for lock duration, NoAction edges and compatibility defaults. ContentRevision default 1 supports old lookup inserts; state enrollment is deliberately not a migration-time unbounded insert.

Use a separately reviewed operator backfill script `scripts/database/Enroll-F6ReviewStates.sql` and verification script `Verify-F6Enrollment.sql`, created only during later implementation. Backfill keyset batches of 1000 UserWord IDs, each short transaction; a single captured rollout UTC parameter supplies EnrollmentAt/NextReviewAt for legacy rows. Select preferred meaning if it belongs to the saved word/POS and is usable; otherwise use usable POS definitions ordered DisplayOrder then Id. Explicit blank preferred meaning is blocked, not silently substituted. Set count/interval/lapse=0, LastReviewedAt=null, version=1 and algorithm=interval-v1. Enroll unusable records too, with nullable scheduled meaning. No events or changes to UserWord fields.

SQL Server insert-if-missing must serialize competing enrollment via NOT EXISTS with appropriate UPDLOCK/HOLDLOCK against the PK inside each batch transaction; catch only recognized PK races and reread. Application enrollment has equivalent transaction/unique-key winner recovery. Re-running skips enrolled states and never resets progress. Capture enrolled IDs/range/row counts and elapsed time without logging definitions or credentials.

Schema-first rollout permits old binaries to continue saving words without states. New binaries, with EnrollmentEnabled=true after schema readiness, create UserWord/state atomically (including two SaveChanges inside one transaction if necessary for generated IDs). Duplicate add retains the existing schedule; if a rollout-created row lacks state, enrollment-only catch-up may add it without changing existing identity/data. No review GET performs enrollment.

Keep Reviews.Enabled=false while binaries overlap and catch-up runs. During old-writer overlap, preferred changes are not transactionally review-aware; therefore no accepted reviews are allowed. Final controlled recycle/write pause retires old instances, reruns enrollment and meaning reconciliation, verifies zero missing states, then resumes new-version writes and activates reviews. Enrollment reconciliation may reset unreviewed states to the actual selected meaning, with version increment; it must not blanket-reset reviewed states on reruns. Any need to reintroduce an old binary first disables reviews and later requires reconciliation before reactivation.

Verification is a left anti-join across all UserWords, not equality of table counts alone. Compare IDs, ownership, selected meaning/revision and baseline checksums/counts for notes, favorite, quiz counters and relationships. Unknown volume makes batch size a rehearsal tuning parameter, not permission for a production long transaction.

## 8. Backend Service Architecture

Create `IReviewScheduler`, `IntervalV1ReviewScheduler`, `IReviewService`, `ReviewService`, `IReviewEnrollmentService`, `ReviewEnrollmentService`, `ReviewDefinitionResolver`, `ReviewQuestionFactory`, and `ReviewsController`. Use scoped EF services, singleton pure scheduler and TimeProvider.System registered in Program.cs. Review options/configuration bind to `ReviewOptions`.

IReviewService operations: GetDueAsync(owner, query), GetSummaryAsync(owner), StartAsync(owner, request), SubmitAsync(owner, request), each cancellation-aware and returning typed ServiceResult. Enrollment/reset orchestration joins the caller's transaction, never starts an unrelated transaction. WordService uses it for new saves and genuine preference changes while retaining existing responses and error codes. A preference race is retried internally with a fresh context/state; exhausted unexpected failures stay safe internal_error rather than adding a new legacy wire error.

Due selection uses indexed owner queries, captured asOf and keyset ordering by due time/ID. Summary uses SQL aggregates independent of the loaded vocabulary page. Candidate questions are selected oldest due first; seek through blocked targets to obtain up to the requested count. Distractors use owner vocabulary, including non-due words; scan indexed keyset batches rather than random sorting/full materialization. Start scans to exhaustion when necessary to distinguish unavailable from an arbitrary candidate cap. Persist only the bounded result; start does not reserve or advance states. Expose performance budgets measured in rehearsal; do not promise constant-cost semantic validation over an arbitrarily large vocabulary.

P2 recommends word-to-definition only in F6. Resolve explicit meaning, else usable saved-POS fallback ordered DisplayOrder/Id. Require nonblank word and definition, distinct WordIds and four options. Normalize comparison keys with Unicode FormKC, trimmed/collapsed whitespace and ordinal case-insensitive comparison, preserving original display text. Exclude distractors equal to the target or to any usable canonical definition for the target word; exclude duplicate option keys. This prevents known alternate meanings from being scored wrong. Synonym/paraphrase ambiguity cannot be proved absent by normalization: apply curated ambiguous-definition fixtures and conservative rejection; never invent definitions, fuzzy-grade or claim semantic equivalence detection. If credible ambiguity remains, exclude the candidate and report unavailable.

Submit checks ownership, receipt, expiry, option membership, pending item, captured state version and current selected target content/revision; then server scores the stored option key. Persist state, item acceptance, full receipt, history and session completion in one transaction. No UserWord quiz field or QuizResult changes. Pending answers count neither correct nor incorrect. Cancellation before commit rolls back; cancellation/network loss after commit is recovered by receipt.

## 9. API Contracts and Error Codes

These are **approved planned** contracts under P2/P3/P5, not implemented APIs. Phase 1 supplies DTOs only. All four planned routes use [Authorize], JWT NameIdentifier owner, camelCase, HTTP 200 `SuccessResponse<T>` with exactly `{success:true,data:...}`. Explicit nullable fields remain present. No client owner, correctness, interval or schedule version input. Framework 401 may be bodyless; controller claim failure uses existing 401 invalid_token. All routes may return safe 500 internal_error and 503 reviews_disabled under P5. No new application 403/201/410.

Types: UUID=nonempty Guid JSON string; Instant=ISO UTC `Z`; count/ID=JSON integer; scheduleVersion is not public. Response DTO strings below have exact enum literals. C# DTOs reside in `DTOs/ReviewDTOs.cs`.

| DTO | Exact properties |
|---|---|
| ReviewDueQueryDto | take:int=20 (1..100), cursor:string? (maximum 2048) |
| ReviewDueItemDto | userWordId:int, word:string, nextReviewAt:Instant, reviewStatus:`new\|learning\|reviewing`, isUsable:bool |
| ReviewDueResponseDto | items:ReviewDueItemDto[], nextCursor:string?, asOfUtc:Instant |
| ReviewSummaryResponseDto | dueCount:int, newCount:int, overdueCount:int, blockedCount:int, usableDueCount:int, totalEnrolledCount:int, nextReviewAt:Instant?, asOfUtc:Instant |
| ReviewStartRequestDto | questionCount:int=10 (1..20), resumeSessionId:UUID?=null |
| ReviewOptionDto | optionId:int 0..3, text:string |
| ReviewItemDto | itemId:UUID, userWordId:int, questionType:`word-to-definition`, prompt:string, options:ReviewOptionDto[], acceptedReceipt:ReviewSubmitResponseDto? |
| ReviewStartResponseDto | availability:`ready\|empty\|unavailable`, reason:`insufficient_usable_vocabulary\|ambiguous_options\|no_usable_due_words` or null, sessionId:UUID?, expiresAtUtc:Instant?, items:ReviewItemDto[], sessionSummary:ReviewSessionSummaryDto? |
| ReviewSubmitRequestDto | sessionId:UUID, itemId:UUID, submissionId:UUID, selectedOptionId:int 0..3; all explicitly required |
| ReviewSessionSummaryDto | totalItems:int, acceptedCount:int, correctCount:int, incorrectCount:int, pendingCount:int, isComplete:bool |
| ReviewSubmitResponseDto | sessionId:UUID, itemId:UUID, submissionId:UUID, reviewedAtUtc:Instant, isCorrect:bool, selectedOptionId:int, correctOptionId:int, selectedAnswer:string, correctAnswer:string, nextReviewAt:Instant, reviewIntervalDays:int, reviewStatus:`learning\|reviewing`, sessionSummary:ReviewSessionSummaryDto |

GET due paginates the **full scheduled backlog**, including unusable entries with isUsable=false. This avoids hiding New vocabulary; blocked entries are visibly explainable. Counts overlap: dueCount includes blocked due entries; newCount counts all New; overdueCount is the strictly earlier subset of due; blockedCount counts due entries missing usable meanings; usableDueCount=dueCount-blockedCount, **not a guarantee of question generation**. nextReviewAt is the earliest future scheduled instant, or null. Small vocabulary/ambiguous options availability is evaluated by start. This deliberately refines the analysis's actionable-only due interpretation while fulfilling D7's full-backlog requirement.

Cursor is an opaque versioned encoding of owner, initial asOf UTC and last `(NextReviewAt,UserWordId)`, protected with ASP.NET Data Protection for integrity. Reuse the same asOf across pages; reject invalid/tampered/other-owner/older-than-30-minute cursors as validation_failed. Shared durable Data Protection key availability must be verified before release; loss of keys safely forces a first-page refresh. Pagination is a live view, not snapshot isolation across requests; concurrent rescheduling can remove/move rows. Start always rechecks due eligibility.

Start without resume creates up to questionCount items. No due rows → empty, null reason/session/expiry/summary, items=[]. Due rows but none usable → unavailable/no_usable_due_words. Fewer than four usable words → unavailable/insufficient_usable_vocabulary. Otherwise no valid questions → unavailable/ambiguous_options. Availability is HTTP 200 data, **not** an R7 error envelope; no session is created for empty/unavailable. Partial valid sets are ready and ItemCount records actual size.

P3 recovery uses the same POST start route with resumeSessionId, avoiding a fifth endpoint. questionCount defaults but is ignored for resume. Owner session loads immutable items and accepted receipts; pending items have acceptedReceipt=null. Accepted receipts expose keys only for already accepted items. Resume does not extend expiry or create events. If any pending target's version/content is stale, reject resume with review_state_changed; accepted history remains committed. A completed session may be resumed until expiry. At expiry a new session is explicit; pending words remain due.

Binding failures (missing/null body, malformed JSON, empty UUID, absent/null/type-invalid selectedOptionId, out-of-range count/option/query) use HTTP 400 extended ValidationProblemDetails, code validation_failed, application/problem+json. Use JsonRequired for submit presence and custom nonempty-Guid validation; zero is valid. Business item-option mismatch uses invalid_review_answer. A null selection is never interpreted as an incorrect/skipped answer. Extra JSON members follow existing framework behavior and cannot alter server-owned fields.

| Status/category | Code | Safe exact message; routes |
|---|---|---|
| 400 Validation | invalid_review_answer | The selected answer is not valid for this review item. Submit |
| 404 NotFound | review_session_unavailable | This review session is unavailable. Please start a new review. Submit/resume; absent, nonowned, wrong item/session combination |
| 409 Conflict | review_session_expired | This review session has expired. Please start a new review. Submit/resume, after owner check |
| 409 Conflict | review_state_changed | Your review content or schedule changed. Please start a new review. Submit/resume |
| 409 Conflict | review_submission_conflict | This submission key or review item has already been used for a different answer. Submit |
| 503 ServiceUnavailable | reviews_disabled | Reviews are temporarily unavailable. Please try again later. All routes |
| 401 Unauthorized | invalid_token | Invalid token. All routes, retain existing helper text |
| 500 InternalError | internal_error | An internal error occurred. Please try again. All routes |

Add only the new allowlisted pairs to ApiErrorResults. Extend query validation field names for take/cursor and review DTO fields, and request classification/operation metadata in Swagger filters. Advertise both 400 media types where relevant, required fields/nullability and bodyless Bearer challenge. Preserve all existing action mappings. No correct key or unaccepted receipt on start/due/summary. Exact replay concerns the typed success body; server Date/trace/transport headers may differ.

## 10. Concurrency and Idempotency Design

Do not depend on process locks. Use optimistic ScheduleVersion plus session Version, database uniqueness and transactional reads. Use Serializable for answer/preference/content coordination: SQL Server retains relevant UserWord/definition reads through commit, preventing a canonical/preference write from slipping between validation and update. SQLite serializes writes with busy/locked retry handling; tests use independent connections. All mutation paths use a consistent order: canonical definition rows by ID, UserWord rows by ID, review states by ID, session, item/history. Initial owner/receipt lookups are nontracking; authoritative locked reads occur in the transaction. Rehearse deadlocks and keep transactions short. Content maintenance acquires affected rows in the same order.

Submission protocol:

1. Require owner and immutable `(submissionId,sessionId,itemId,selectedOptionId)`; compute typed payload equality without timestamps or caller headers.
2. Find owner-scoped history receipt by SubmissionId. Identical payload returns original 200 **before expiry/staleness checks**; changed payload returns conflict. A key belongs to `(UserId,SubmissionId)`, not just a session.
3. Begin execution-strategy-managed transaction; reread receipt, then validate owned session/item. Use one attempt's TimeProvider instant after authoritative validation. `now >= ExpiresAt` rejects new acceptance. Accepted time is the successful attempt's server validation instant, not original due date or client clock.
4. Validate option, selected target identity/revision/text, due eligibility and CapturedScheduleVersion. Conditional state UPDATE uses old ScheduleVersion; affected row count must be one. Increment session Version to serialize different item answers in the same session; derive sessionSummary from transactional accepted items including this answer.
5. Insert one history event, store identical ReceiptJson/contract version on history/item, mark acceptance and session completion if appropriate; commit all together. No partial receipts/history/scheduling.
6. On state concurrency loss, rollback, clear tracking and reread receipt/item/state with a fresh context. Same request winner → replay; completed item with different key → submission conflict; different-session target winner → state changed. On session-version loss for independent targets, bounded retry from fresh state is allowed; it is not automatically a stale-target error.
7. Recognize only named unique constraints/provider codes. SQL Server deadlock 1205 and transient execution-strategy errors, or SQLite BUSY/LOCKED, receive bounded fresh-transaction retries (maximum three service attempts with jitter), without changing logical key. Never retry business validation. Unknown failures → safe internal_error.
8. Commit ambiguity triggers a fresh independent receipt lookup before deciding whether to retry. If found return the committed receipt; if absent and commit outcome resolved, retry safely. If database unavailable, return safe failure and preserve client request for later replay. Do not assume a thrown commit implies rollback.

Rolled-back attempts may choose a later server time on retry; the one committed history event determines the accepted instant. Once a receipt exists no time or summary is recomputed. Two concurrent independent targets in one session both eventually succeed without lost summary updates; two sessions for the same target yield one schedule transition. Same-definition preference writes do not need state-version advancement. Canonical snapshots protect answer auditing; locks/revisions protect acceptance against changing live content.

**P4 retention:** accepted history, receipts, items and their parent sessions remain for the life of the saved word/account; retry keys are never reused while retained. Bounded cleanup (100 sessions/request) deletes only expired sessions with zero accepted items, items first then sessions. Partially accepted sessions retain pending snapshots too for audit simplicity. Cleanup is a separate short transaction after core work; cleanup failure must not invalidate a committed answer. No cleanup mutation on GET. Account/word deletion, when separately introduced or performed by approved maintenance, deletes history → items → orphan sessions → state → UserWord in a transaction, including cross-session references, and coordinates existing dependent deletions. No public delete endpoint exists now. NoAction prevents an old unmanaged delete from silently violating retention/integrity; old delete maintenance must be updated before release. Deletion revokes replay rights and removes snapshots/receipts, never orphaning private learning data. Backup retention is governed separately by the production owner; no unapproved promise of permanent retention or timed purge.

## 11. Preferred-Definition and Content-Change Rules

**D2 implementation:** Compare the requested effective meaning ID with the currently effective meaning before writing. Same effective ID (including explicit selection of the current fallback) preserves scheduling; synchronization of derived POS or explicit preference metadata alone is not a learning reset. Different effective ID resets to New immediately, clears current-epoch review time/progression, increments ScheduleVersion and records the current definition revision in the same transaction as UserWord preference/POS changes. Preserve ID, added date, all quiz fields, favorite, notes, relationships, history and lifetime lapses. No synthetic ReviewHistory reset event: history contains genuine answers only, and version gaps can represent administrative resets.

**P2 canonical changes:** Add ContentRevision and route every later text/POS/word-membership change through a controlled maintenance service `ReviewContentChangeService` (no public editing API). Normalize text with FormKC and collapsed whitespace; normalized equality preserves revision/schedules for cosmetic whitespace changes, but case/punctuation/word changes reset conservatively. Text displayed on an existing item remains its immutable snapshot. Example-only changes do not reset. DisplayOrder changes or definition additions/removals require checking fallback selection: reset only owners whose effective meaning actually changes.

Content mutation, ContentRevision increment and reset of affected enrolled states occur atomically under serializable coordination. Definition removal first repoints/clears current UserWord preferences and review-state definition FKs within that transaction, preserving historical identifier snapshots. Revision is checked for target **and distractor** sources at submit/resume, so edited/deleted distractors also stale the item without resetting unrelated target schedules. Source UserWord deletion makes its item stale unless an accepted receipt is being replayed. A target whose definition was edited resets even if the canonical ID is unchanged. Same text with a different effective definition ID is a genuine meaning selection change and resets under D2.

There is no current canonical-edit API; direct SQL editing while reviews are active is not a supported writer. Emergency/operator imports must disable reviews, change content, reconcile/reset affected states, invalidate pending snapshots, verify coverage and reactivate. Exact normalized snapshot comparisons at submit provide an extra stale guard for out-of-band edits, but do not by themselves reset the due queue; operator reconciliation remains required. Large fan-out changes may use a controlled maintenance window instead of a long live transaction. This is an explicit policy approval, not authority to edit production content now.

## 12. Angular Architecture and User Experience

Add `models/review.model.ts`, `services/review.service.ts` and standalone `components/review-session/review-session.component.{ts,html,scss}`. ReviewService wraps existing ApiService, validates success discriminants/required shape, URL-encodes cursor and manages a pending immutable answer. Guard `/reviews` with authGuard. Dashboard calls summary after authentication and on return/navigation, shows the full due count with blocked explanation and a review action; review errors are never displayed as zero. Query counts independently of the vocabulary's pageSize=1000 request.

Flow: loading → launch/empty/unavailable → question → submitting → accepted feedback → next → completion, with explicit retry, expired and stale branches. Selecting a radio option does not submit; a labelled submit button commits it. Immediate feedback uses server isCorrect/correctAnswer and next due time. Accepted answers cannot be edited or regraded. Completion distinguishes accepted correct/incorrect and pending; leaving early retains accepted progress and leaves pending due. Score uses acceptedCount as denominator; zero accepted has no percentage.

P3 recommends sessionStorage scoped by authenticated user for active session ID and the exact pending submit payload. Persist pending request **before** sending; on reload replay uncertainty first, then resume via POST start. Storage is a convenience, never answer authority; malformed storage is ignored safely. Clear on explicit logout/user change and completion, with no tokens/answer keys in custom storage. Without client recovery metadata, abandoned words remain due and a new session is safe. If expiry/staleness is returned, retain visible accepted feedback, refresh summary and offer explicit new session. Network/500/malformed-200 failures preserve the same pending key and selection; do not unlock a different answer until resolved. A stale recovery does not invent failure events. Dashboard hides review action on reviews_disabled; direct guarded review route still explains unavailable.

Reuse existing quiz layout colors/spacing and ApiService/error normalization patterns. Do not extract a shared quiz component or change QuizService/question behavior merely for reuse. Extend api-error operation union/message/validation allowlists for review calls, preserving legacy rules. No vocabulary DTO or quiz refactor is needed.

Accessibility: native fieldset/legend radio group, arrow-key selection and Tab/Space/Enter controls, visible focus, prompt focus after moving, role=status/aria-live polite for accepted feedback and loading, textual correctness alongside color, labelled progress and errors, at least 44px action targets, wrapping definitions and stacked controls at 320px viewport, no horizontal overflow. Browser-local date/time rendering includes a local-time label; server intervals remain elapsed UTC. Verify zoom at 200%, keyboard-only flow and screen-reader announcements manually as well as DOM tests.

## 13. Testing and Verification Matrix

No tests/builds are executed by this plan. Author meaningful tests with the implementation phases, then run full regression in Phase 5. Avoid tests that merely repeat field declarations without behavior.

| Area/phase | Required scenarios and evidence |
|---|---|
| Scheduler / 1,3 | All correct ladder steps and cap, every failure, correct after failure, New/reset, invalid state/overflow; due one tick before/equal/after; leap/DST elapsed intervals; ScheduleVersion and lapse rules. |
| Schema/enrollment / 2,3 | FK ownership rejection across sessions/words, required/check/unique constraints, repeated backfill, empty/invalid preferred meaning, same-ID duplicate add, save race winner, new inserts during rollout; all existing personal data preserved; no generated history. |
| API/authority / 3 | Anonymous/bodyless 401, invalid principal, nonowner/missing concealed 404, empty UUID and malformed input, selected zero accepted/omitted/null rejected, foreign options, disabled flag no review DB access. No unaccepted key leaks. All R7 error/media/Swagger shapes. |
| Atomicity / 3 | Inject failure after state update, history insert, item update and before commit; state/item/history/session all roll back. Preference/reset rollback preserves old meaning/schedule. Replay after committed response loss. |
| Concurrency / 3,5 | Separate connections: identical keys, differing keys/same item, same key/different payload, different sessions/same target, different items/same session, preference vs answer, content/distractor edit vs answer, deadlock/BUSY retries, unknown commit result. Exactly one event per accepted item. |
| Content / 3 | Same preference/fallback ID preserves progress, real meaning resets, normalized whitespace no-op, genuine case/punctuation edit reset, example no-op, fallback reorder/removal reset, distractor revision stale without target reset; old receipts survive later edits. |
| Durable recovery / 3,4 | Replace host/context while keeping database, resume pending/accepted items, replay original summary after later answers, expiry equality, partial abandonment, cleanup leaves accepted receipts, deletion revokes replay. |
| Quiz/R7 regression / 3,5 | Quiz still writes only legacy fields/results and retains unanswered scoring, history/navigation/error semantics; quiz never advances review state, reviews never write quiz fields; preserve all original 14 endpoint payloads/statuses and intentionally add four OpenAPI paths. |
| Angular / 4 | Due summary/launch/bearer requests, empty vs unavailable vs failure, option zero, double-click guard, accepted correct/incorrect feedback, pending counts, immutable retry after network/500/malformed 200, reload replay/resume, stale/expired/401 recovery, logout storage cleanup, safe messages. |
| Accessibility / 4,5 | Radio/labels/live announcements/focus progression, keyboard submit/retry, no color-only correctness, 320px/200% zoom long-definition layout and screen-reader manual checks. |
| SQL Server / 2,5,6 | Upgrade actual pre-F6 migration history, datetime2 UTC translation/precision, filtered uniqueness, multiple cascade-path absence, independent concurrent connections/processes, serializable content races, index plans/lock duration, restored backup, idempotent enrollment and old-binary overlap. |

Create `Services/ReviewSchedulerTests.cs`, integration `ReviewApiTests.cs`, `ReviewConcurrencyTests.cs`, `ReviewEnrollmentTests.cs`, `ReviewContentChangeTests.cs`, `ReviewContractTests.cs` and infrastructure `ReviewPersistenceFailureInterceptor.cs`, `ReviewSynchronizationInterceptor.cs`, `IndependentConnectionDatabaseFixture.cs`, `ControllableTimeProvider.cs` under VocabularyApp.WebApi.Tests. Fixture connections share an isolated SQLite file or named memory database with a keeper connection, not a single shared connection. Use barriers around reads/writes; never rely on sleeps. Add independently configured SQL Server tests `Integration/SqlServerReviewTests.cs` using an explicitly disposable database; external test credentials are supplied securely, never production defaults. SQL Server tests remain a separately invoked gate unless later CI changes are approved.

Extend existing API metadata/OpenAPI/validation/shape/error tests and QuizApiTests, and factory/seeder hooks. Angular new `review.service.spec.ts`, `review-session.component.spec.ts`, `dashboard.component.spec.ts`; extend api-error.spec.ts and api-contract.fixtures.ts. Existing quiz.spec.ts remains regression evidence and needs changes only for a concrete new assertion that quiz remains independent.

## 14. Implementation Phases and File-Level Changes

All paths below are planned edits, not edits performed here. Phase dependency chain: **1 → 2 → 3 → 4 → 5 → 6**. Test authoring accompanies phases 1–4; Phase 5 integrates and verifies. SQL rehearsal starts on schema availability and completes before Phase 6. Frontend can prototype approved fixtures after Phase 1, but end-to-end acceptance depends on Phase 3.

### Phase 1 — Contracts and Scheduling Rules

- Objective: approve P1–P5 specifications and encode pure transitions and DTO contracts.
- Prerequisites: developer approves this plan for implementation; baseline branch/status reverified in a trusted checkout without persistent Git bypass.
- Files: create `VocabularyApp.WebApi/Services/IReviewScheduler.cs`, `IntervalV1ReviewScheduler.cs`, `Models/ReviewTransition.cs`, `DTOs/ReviewDTOs.cs`, `Configuration/ReviewOptions.cs`; create scheduler tests listed above. Update this plan only to record approved resolutions; add review contract reference to `Docs/Updates/F6-review-api-contract-reference.md` later.
- Database impact: none applied; finalize enum/version/date/retention invariants for Phase 2.
- API impact: DTO specification only; no live routes until Phase 3.
- Frontend impact: agreed fixtures/types, no route yet.
- Tests: transition matrix, presence/bounds and serialization of contract fixtures.
- Later commands: `dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --filter FullyQualifiedName~ReviewSchedulerTests`.
- Acceptance: every transition and public DTO/error is unambiguous; all policy decisions recorded approved or explicitly deferred with dependent work blocked.
- Risks: conflicting status/meaning semantics and accidental inferred grades.
- Stop/go: stop dependent implementation if P1–P5 unresolved; go once signed off and fixtures agree.

### Phase 2 — Database Schema and Migration Design

- Objective: additive mappings/migration and idempotent enrollment scripts compatible with production and tests.
- Prerequisites: Phase 1 schema/retention/content decisions approved; disposable SQL Server environment provided.
- Files: create `VocabularyApp.Data/Models/UserWordReviewState.cs`, `ReviewHistory.cs`, `ReviewSession.cs`, `ReviewSessionItem.cs`; create corresponding `Configurations/*Configuration.cs`; modify ApplicationDbContext.cs, Models/WordDefinition.cs and UserWord.cs; generate `Migrations/<timestamp>_AddSpacedRepetition.cs`, its Designer.cs and update ApplicationDbContextModelSnapshot.cs only when later authorized. Create `scripts/database/Enroll-F6ReviewStates.sql`, `Verify-F6Enrollment.sql`; new enrollment/schema tests and independent-connection fixture.
- Database impact: four tables, content revision, alternate keys, constraints/indexes; enrollment separate from DDL; no UserWord data rewrite.
- API impact: none to existing contracts; revision remains internal.
- Frontend impact: none.
- Tests: final-model SQLite integrity, migration definition review, SQL Server upgrade/backfill/old-insert compatibility.
- Later commands: `dotnet ef migrations add AddSpacedRepetition --project VocabularyApp.Data --startup-project VocabularyApp.WebApi`; `dotnet ef migrations script <verified-pre-F6-id> <generated-F6-id> --idempotent --project VocabularyApp.Data --startup-project VocabularyApp.WebApi --output <disposable-review-output.sql>`; `dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --filter FullyQualifiedName~ReviewEnrollmentTests`. Placeholder paths/IDs must be explicitly replaced; generation does not authorize applying to production.
- Acceptance: reviewed SQL succeeds on disposable SQL Server and rerun enrollment preserves existing states; baseline identity/counters unchanged; no cascade-path errors.
- Risks: unknown production EF history, large index locks, provider-specific filters and missing delete maintenance.
- Stop/go: no migration application to production; stop if SQL Server rehearsal or ownership constraints fail.

### Phase 3 — Backend Scheduling and Review Services

- Objective: durable review lifecycle, vocabulary hooks and concurrency/recovery.
- Prerequisites: Phase 2 model/schema validated; approved API/content policy.
- Files: create backend services named in section 8 and `Services/ReviewContentChangeService.cs`, `Controllers/ReviewsController.cs`; modify Program.cs, Services/WordService.cs, Helpers/ApiErrorResults.cs, ApiValidationResponses.cs, Swagger/ApiContractOperationFilter.cs, ApiContractSchemaFilter.cs. ReviewOptions initializes both feature switches to false; use runtime overrides and test configuration rather than editing the digest-pinned appsettings.json or deployment scripts. Add new tests/interceptors/time provider; extend existing factory/seeder and API metadata/OpenAPI/shape/validation/error tests. No QuizService/IQuizService/controller/legacy DTO edits planned.
- Database impact: transactional enrollment, accepted review writes and resets using Phase 2 schema; no new migration expected.
- API impact: four new routes, allowlisted errors, resume within start; existing 14 unchanged.
- Frontend impact: actual contract available for Phase 4.
- Tests: authorization, answer authority, availability, atomicity, expiry, replay, independent-connection races, content edits, legacy regressions.
- Later commands: `dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~Review|FullyQualifiedName~QuizApiTests|FullyQualifiedName~ApiContract|FullyQualifiedName~OpenApi"`.
- Acceptance: one accepted event per item, no double schedule advancement, durable replay/resume, real preference reset/no-op, no legacy quiz writes from reviews, disabled feature does not query absent review schema.
- Risks: deadlocks, retrying with tracked stale state, leaking keys on resumed pending items.
- Stop/go: block UI release if atomicity, content race or R7 regression fails.

### Phase 4 — Angular Review Experience

- Objective: accessible per-answer reviews and truthful backlog/dashboard.
- Prerequisites: approved DTOs and Phase 3 lifecycle; may use approved fixtures earlier.
- Files: create review model/service/component and specs from section 12/13; modify `VocabularyApp.UI/src/app/app.routes.ts`, `components/dashboard/dashboard.component.ts`, `.html`, `.scss`, add its `.spec.ts`; modify services/api-error.ts/.spec.ts and testing/api-contract.fixtures.ts. Existing ApiService and quiz presentation can be referenced without modification. No new package or unnecessary component extraction.
- Database impact: none beyond API calls.
- API impact: consume four routes; no added route or legacy DTO dependency.
- Frontend impact: guarded reviews, dashboard due indicator, immediate feedback, resumable immutable pending submit, empty/unavailable/error branches.
- Tests: section 13 frontend matrix, manual mobile/keyboard/screen reader.
- Later commands, from VocabularyApp.UI: `npm test -- --watch=false --browsers=ChromeHeadless`; `npm run build -- --configuration production`.
- Acceptance: pending key survives uncertainty/reload; stale/expired restart explicit; accepted/pending counts truthful; accessible mobile controls.
- Risks: storage outliving authentication, retry key regeneration, interpreting unavailable as zero backlog.
- Stop/go: no release with answer editing after acceptance, lost pending request or inaccessible core flow.

### Phase 5 — Automated Testing and Integrated Verification

- Objective: complete lifecycle/regression and SQL Server evidence, not a second implementation of the scheduler.
- Prerequisites: phases 1–4 complete; disposable SQL Server and independent connections configured.
- Files: finish tests enumerated in section 13; create SqlServerReviewTests.cs and `Docs/Updates/F6-verification-results.md`; update review contract reference. Existing test changes limited to F6 behavior and deliberate 18-action expectations; retain legacy assertions. No workflow changes assumed.
- Database impact: disposable schema/migration/backfill/concurrency exercises only.
- API impact: exact new contracts and all legacy contracts verified.
- Frontend impact: full unit/build gates plus manual accessibility evidence.
- Tests: all section 13 rows, migration rehearsal section 15, host recycle and commit ambiguity.
- Later commands: `dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release`; `dotnet build VocabularyApp.sln --configuration Release`; Angular commands in Phase 4; separately `dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release --filter FullyQualifiedName~SqlServerReviewTests` with explicit disposable connection configuration. Default tests must not silently contact production; SQL gate must report skipped/unconfigured as missing evidence, never a pass.
- Acceptance: required suites/builds pass, repeat races demonstrate invariants, SQL rehearsal documented, no untriaged material failure; accessibility manual checks recorded.
- Risks: SQLite-only false confidence, tests sharing one connection, historical results reported as new.
- Stop/go: no production candidate until SQL Server migration/concurrency gate passes and exact commit is known.

### Phase 6 — Production Release Preparation

- Objective: reviewable, authorized schema/application/activation sequence and recovery artifacts.
- Prerequisites: Phase 5 passes; P6/P7 owners/environments/configuration approved; production action separately authorized.
- Files: create `Docs/Deployment/F6-Spaced-Repetition-Release-Runbook.md` from section 16; finalize scripts/database files and verification results. Existing workflow/deploy/publish scripts are inspected and retained; changes require separate approval if hosting cannot support the proposed activation mechanism.
- Database impact: separately approved production migration/enrollment/verification only, never triggered by this plan.
- API impact: activate already deployed review routes after readiness; existing actions smoke-tested.
- Frontend impact: dashboard activation/guarded reviews, no new artifact at activation if runtime readiness governs visibility.
- Tests: approved authenticated smoke, restoration drill and feature-disable compatibility.
- Later commands: production owner selects approved SQL client to run reviewed DDL/backfill/postchecks; existing GitHub workflow executes on a later authorized master merge. Do not propose `dotnet ef database update` against production or bundle SQL into Web Deploy.
- Acceptance: backup restorable, schema/enrollment verified, correct application SHA deployed disabled, old writers retired, activation approved, smoke/replay/quiz checks pass, recovery owner available.
- Risks: automatic deploy triggered before schema ready, unverified backup/permissions, overlapping old writers, rollback restoring stale data.
- Stop/go: stop activation on any failed readiness check; deployment authorization is not implied by implementation approval.

## 15. SQL Server Migration Rehearsal

Use a disposable SQL Server instance/version compatible with the hosting database and a sanitized representative pre-F6 restore. Owner supplies actual migration history/compatibility level and volume later. Rehearsal sequence:

1. Record EF history, existing `(UserId,WordId)` and quiz unique indexes, FK validity, definition selections, row counts/data fingerprints. Restore backup to a separate disposable database and prove application connectivity before DDL.
2. Review exact generated SQL; apply only the intended migration range. Verify migration ledger, tables/columns/types/defaults, alternate keys, checks, filters and NoAction edges. Run old binary reads/quiz/new vocabulary insertion with ContentRevision default and no state requirement.
3. Execute batched enrollment twice, inject batch interruption and resume, interleave old/new saves, reconcile meaning changes; verify anti-join missing count zero after old writers stop. Preserve saved IDs/data; history remains zero before test reviews.
4. Run real separate-connection races and transactional failures, including SQL Server deadlocks, definition edits, same-key replay and lost-response recovery. Verify versions/history/receipts with SQL after outcomes.
5. Inspect due/summary/start/receipt query plans and elapsed time/locks at representative backlog sizes. Verify index usage, no randomized full-table sort and bounded session payload. Establish acceptable hosting latency/storage thresholds with owner.
6. Disable reviews and run prior binary against additive schema; confirm old writes need enrollment/reconciliation before reactivation. Rehearse restore to a separate database, measure RTO/RPO and document post-backup write loss. Do not treat a destructive Down migration as the normal rollback.

Keep migration IDs, artifact SHA, sanitized row metrics, constraint/plan output, timings, race outcomes and restore evidence in verification results. SQLite EnsureCreated is final-model evidence only and cannot substitute for this upgrade rehearsal.

## 16. Production Deployment and Recovery Plan

**Separate production checklist; no step is executed or authorized by this document.** Proposed activation P5 uses independent server configuration `Reviews:EnrollmentEnabled` and `Reviews:Enabled`, both false by default. Never infer schema readiness from a DLL version. Review routes short-circuit when disabled; WordService review hooks short-circuit when enrollment is disabled. Tests prove legacy actions still work against a pre-F6 schema with both false. Canonical lookup can rely on the SQL default for ContentRevision; feature binary still requires schema before rollout.

| Step | Owner/execution | Required evidence/stop condition |
|---|---|---|
| 1. Pre-deployment verification | Release owner, manual | Approved implementation/release, exact candidate SHA, full gates/rehearsal, production EF history matches intended migration base, compatible old binary and trusted checkout. No master merge yet. |
| 2. Database backup | Database owner, manual host-supported mechanism | Timestamped recoverable backup, restore drill and access verified, accepted RPO/RTO; stop if unavailable. Record application settings and prior artifact. |
| 3. Approved migration execution | Named database owner, manual reviewed SQL outside Web Deploy | Separate authorization, maintenance/lock plan, apply additive DDL only; record migration ledger and result. No runtime migrations. |
| 4. Schema verification | Database owner, manual postchecks | All new tables/keys/defaults/checks/indexes; old application smoke passes; stop on partial/mismatched schema. |
| 5. Legacy enrollment | Database owner, manual batched script | Stable captured UTC time, progress and resumable batches; Reviews.Enabled remains false. |
| 6. Enrollment coverage | Database/release owners, manual | Anti-join, selected-meaning/owner checks, data preservation; rerun catch-up for old-writer additions. Repeat final checks after step 7 retires old writers. |
| 7. Application release coordination | Release owner authorizes merge; existing GitHub Actions automated | Set/verify disabled runtime review config; separately approved master merge runs tests/build/package/Web Deploy/HTTPS checks. Confirm artifact SHA and retire old workers with controlled recycle/write pause. Enable enrollment hooks, catch up/reconcile again, verify zero missing states. |
| 8. Feature activation | Release owner, manual runtime configuration/recycle | Named approval after schema, coverage, new-writer-only and kill-switch checks. Set Reviews.Enabled=true; configuration persisted outside artifact overwrite and effective across all instances. |
| 9. Authenticated smoke | Release/test owner, manual approved test accounts | Due/summary, small-vocabulary availability, start/answer/replay, refresh/resume, expiry/stale recovery, ownership concealment, preference no-op/reset and ordinary quiz. Inspect SQL UTC/history/quiz isolation; no testing with unsuspecting users' vocabulary. |
| 10. Recovery/monitoring | Release/database owners, manual; existing safe logs | Monitor conflict/error/latency/coverage/storage. Disable reviews on review faults, retain data and forward-fix. Follow recovery cases below. |

Production config storage mechanism is unverified. P7 must confirm host environment variables or another protected runtime source persists over Web Deploy and is consistently applied. If unsupported, approve a narrow configuration/deployment change before release; do not silently edit the existing pipeline. EnrollmentEnabled stays true after readiness; disabling Reviews.Enabled stops review routes while allowing legacy quiz and atomic new-word enrollment.

Recovery cases: DDL failure before new code → stop release, inspect transaction/ledger, repair additively or restore only under an explicitly accepted write-loss window. Enrollment interruption → keep reviews off, rerun idempotent batches. Review application defect → disable reviews, keep additive schema/history, prefer a forward fix. Prior binary redeploy → keep both review flags off and check operational delete/content tooling; old saves/preferences require catch-up/reconciliation before next activation. Existing safeguards select release candidates from master, so emergency artifact/SHA eligibility and the authorized recovery mechanism must be rehearsed rather than assuming any old artifact can be uploaded. No automatic downgrade/Down/restore after new learning writes. Database restore is a last-resort separately approved disaster operation with write freeze and reconciliation of all post-backup application activity.

## 17. Risks, Dependencies and Technical Debt

| Risk/dependency | Mitigation and limit |
|---|---|
| Binary recognition and guessing | Preserve true source/outcome; no retention guarantee or invented FSRS grades. |
| Semantic distractor ambiguity | Known-definition exclusion, normalization, curated tests and safe unavailable; paraphrase equivalence remains a limitation. |
| Canonical maintenance fan-out | Revision/reset transaction or disabled maintenance window; all writers must conform. |
| Cross-connection races/deadlocks | Portable tokens, unique receipts and short serializable transactions; SQL Server evidence required. |
| Lifetime snapshots/storage | P4 explicit retention; session bounds/cleanup; owner measures quota/growth. No background worker requirement. |
| Full due queue and summary cost | Owner indexes, keyset pages, server aggregates; backlog stays visible, no concealed daily caps. |
| Automatic master deploy/no migrations | Manual schema/enrollment first, explicit server activation and merge coordination. |
| Data Protection keys lost on recycle | P7 durable key storage or approved cursor alternative; safely invalidate cursor without affecting review schedules. |
| New NoAction edges | Review-aware privileged deletion ordering must be documented; no existing delete API assumed. |
| Git ownership limitation | Exact commit/tree must be reverified in authorized trusted environment before implementation/release. |
| Existing quiz limitations | Process-local sessions, cross-session aggregate concurrency and 500-vs-1000 answer length remain separately scoped debt. |
| Dependency vulnerabilities | Broad assessment remains deferred; no audit/packages performed here. Specific credible issues would be handled within explicit scope if found. |

## 18. Remaining Decisions Requiring Approval

Do not reopen D1–D9 or P1–P5: **formally approved by the developer on 2026-10-12**, preserving the original specifications. The current implementation authorization covers **Phase 1 only**. P6–P7 remain **UNRESOLVED / NOT APPROVED**, and remain production-release verification gates.

| ID | Recommended resolution | Gate |
|---|---|---|
| P1 | Reviewing threshold count>=3; no mastery/suspension status; resets preserve lifetime lapse count and clear current-epoch LastReviewedAt. | APPROVED; Phase 1 |
| P2 | Word-to-definition only, normalized distinct/known-alternate option exclusion, canonical ContentRevision with conservative genuine-text resets and controlled maintenance policy; same effective preferred ID does not reset. | APPROVED design; implementation phases 1–3 |
| P3 | 30-minute session expiry at equality; POST start resumeSessionId recovery; scoped sessionStorage pending payload; abandonment has no outcome. | APPROVED design; implementation phases 1,3,4 |
| P4 | History/accepted receipts/session snapshots retained for saved-word/account lifetime, no timed accepted receipt purge; explicit privileged deletion ordering and backup retention handled by owner. | APPROVED design; schema/service work remains later scope |
| P5 | Exact section 9 DTO/status/error mapping; 200 availability result; full-backlog pagination/count semantics; server EnrollmentEnabled/Enabled flags. | APPROVED design; production activation remains separately gated |
| P6 | Name database migration executor, release approver, restore/recovery owner; select SQL Server rehearsal environment, maintenance window and acceptable RPO/RTO/performance/storage limits. | Phase 5 and release |
| P7 | Confirm SmarterASP runtime configuration persistence, feature-disable/recycle behavior and durable Data Protection key storage; approve an alternative only if unavailable. | Before release |

The original planning assignment requested no implementation or production action. The subsequent developer assignment authorizes Phase 1 only; this approval record does not authorize later phases or production action. If approved specifications are later amended, update their dependent schema/API/test specifications before that implementation.

## 19. Acceptance Criteria and Definition of Done

Implementation is complete only when approved decisions are recorded and the following are demonstrated:

- All interval/reset/time transitions and schedule versions match section 5; all historical answers retain binary provenance and immutable before/after state.
- Every saved word enrolls as New without inferred mastery or data loss; duplicate/catch-up enrollment never resets reviewed progress.
- Preferred/content changes invalidate pending old snapshots and reset only affected learning meaning; no-op selection preserves progress and all history/quiz data.
- Durable per-answer state/history/item/receipt commits are atomic; duplicate/concurrent requests cannot double advance; replay is original success even after session expiry or later reviews while retention applies.
- Ownership constraints and authentication conceal nonowned resources; correct keys are hidden until acceptance; invalid/expired/stale requests do not produce review events.
- Full backlog and unavailable reasons are visible; sessions are bounded, non-due distractors allowed and ambiguity is safely rejected.
- The original 14 contracts/quiz behaviors remain unchanged; four new contracts, safe R7 errors/validation and accurate OpenAPI are verified.
- Angular supports truthful immediate feedback/completion, immutable retry/reload recovery, keyboard/mobile/screen-reader interaction and error states.
- Backend/Angular gates pass for the actual implementation commit; disposable SQL Server upgrade/concurrency/restore rehearsal passes; all checks have recorded evidence.
- Production readiness includes named owners, verified backup/configuration/schema/enrollment and a reviewed recovery runbook. Production release/activation is separately authorized and smoke-verified before claiming production completion.

Planning assignment definition of done: this document contains all 20 requested sections, exact proposed schemas/contracts, six dependency-ordered phases with required fields, verification/release gates and clearly marked remaining approvals. Only this document is changed; stop after delivery.

## 20. Final Implementation Readiness Assessment

**CONDITIONAL GO for the complete F6 release.** D1–D9 and P1–P5 are approved, and Phase 1 implementation is authorized. Approval for Phase 2 and later work must be explicit and separate. P6/P7 operational evidence and successful SQL Server rehearsal remain mandatory release gates; production migration/merge/deployment remain unauthorized.

Exact Git commit/working-tree cleanliness could not be verified during planning because of ownership restrictions. Production readiness is not established by local source inspection or historical tests. The original planning pass changed no application/test/workflow code, migration or production resource and ran no builds/tests. Subsequent Phase 1 code and focused verification are documented separately in [F6-phase-1-implementation-results.md](F6-phase-1-implementation-results.md); they do not imply later-phase or production approval.
