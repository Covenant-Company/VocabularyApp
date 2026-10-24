# F6 Spaced Repetition Analysis

Date: 2026-10-10. Repository: Covenant-Company/VocabularyApp. Status: **ANALYSIS ONLY — decisions pending approval**.

Scope: repository inspection and this document only. No implementation, migrations, package installation, builds, tests, commits, pushes, branch deletion, application execution or production access. Section 17 supplies the requested high-level implementation phases; it is not an implementation plan or authorization to begin one.

Evidence notation: **Verified** means observed in local source; **Recorded** means a historical document reports it; **Proposed** means an unapproved design; **Unknown** means not established by this analysis. Paths and line numbers below refer to the inspected files. Git commands were blocked by Git's ownership safety check, including command-local safe-directory attempts; no persistent Git configuration was changed. `.git/HEAD:1` identifies `feature/f6-spaced-repetition`. A clean working tree and exact commit could not be verified. No AGENTS.md was found in the repository file listing or checked ancestor directories.

## 1. Executive Summary

**Recommended:** a deterministic custom interval ladder, initially `1, 3, 7, 14, 30, 60, 90` days, driven by server-evaluated binary answers in dedicated review sessions. These intervals are product defaults proposed for approval, not scientifically validated retention targets.

The application already has canonical words, owner-scoped saved-word identities, definition selection, server-held quiz answer keys, transactional result persistence and relational API tests. It does **not** have a due queue, interval state or spaced-repetition history. Existing quiz counters and review timestamps are actively written, but are insufficient to reconstruct a schedule.

Use separate scheduling state associated one-to-one with UserWord, append-only review events, and durable bounded review sessions/items. A separate review service should share pure answer evaluation and definition resolution with quizzes, while retaining ordinary quiz behavior and existing wire contracts. Due status is calculated at request time; no worker, external scheduler or paid service is required.

The main approval decisions are algorithm/default intervals, review interaction for small vocabularies, preferred-definition reset behavior, legacy counter semantics, durable session scope, backfill eligibility, new API contracts and migration ownership. The greatest engineering risks are stale submissions, overlapping sessions, accidental quiz-statistics changes and relying on SQLite schema creation as migration verification.

## 2. Current Architecture Findings

| Verified finding | Evidence and implication |
|---|---|
| Word is shared canonical dictionary content; UserWord is personal state. | `VocabularyApp.Data/Models/Word.cs:6`; `ApplicationDbContext.cs:35–40,70–100`. Word.Text has a unique index. Do not place user-specific scheduling on Word. |
| A user saves a canonical word once. | `ApplicationDbContext.cs:98–99`; `WordService.cs:222–312`. Repeated add returns the existing ID and does not overwrite selection or counters; recognized unique-key races recover the winner. |
| WordDefinition is one meaning with POS and display order. | `Models/WordDefinition.cs:8–22`; `ApplicationDbContext.cs:49–67`. Multiple definitions exist per word; `(WordId, PartOfSpeechId, DisplayOrder)` is unique. |
| Preferred definition changes mutate the same owner-scoped row. | `WordService.cs:318–364`. The service checks definition.WordId and synchronizes PartOfSpeechId; identity is unchanged. |
| Quiz generation is not due-aware. | `QuizService.cs:23–144`. It materializes the user's eligible vocabulary, filters blank content, groups by WordId, shuffles, and chooses at most 20 targets. It needs at least four eligible saved words for three distractors. |
| Quiz evaluation compares option IDs, not natural-language meaning. | `QuizService.cs:147–278`. Selected option must belong to the server session; missing answers count as incorrect. No fuzzy text scoring or recall quality rating exists. |
| Quiz questions hide answer keys until submission. | `QuizDTOs.cs:15–31`; `QuizService.cs:96–141,492–500`. CorrectOptionId stays in server state. |
| Quiz results and counters are committed together. | `QuizService.cs:286–365`. Execution strategy wraps a transaction, owner-scoped vocabulary is rechecked, counters/timestamps and QuizResults are saved, and failed writes clear tracking. |
| Quiz sessions are process-local and expire after 30 minutes. | `QuizService.cs:13,111–120,467–515`. In-process submission locking prevents simultaneous processing of one session; recycling loses outstanding sessions. |
| Persistence protects one result per user/session/saved word. | `ApplicationDbContext.cs:119–139`; `Migrations/20260819000000_AddQuizResultSubmissionUniqueness.cs`. This is not a general concurrency token for UserWord. |
| Quiz history is grouped persisted results. | `QuizService.cs:369–394`; `QuizDTOs.cs:63–74`. History exposes score/time summaries, not session IDs or scheduling events. |
| Angular uses typed HTTP calls and shared error normalization. | `UI/src/app/services/api.service.ts:13–54`; `components/quiz/quiz.component.ts:93–257`; `services/api-error.ts`. Bearer headers come from AuthService. |
| Vocabulary transport and display already use preferred definitions and statistics. | `WordService.cs:417–487,601–623`; `DTOs/UserVocabularyDTOs.cs:3–38`; `word-lookup.component.ts:357,414,566,618`. UI requests pageSize=1000, so do not derive due counts from its loaded page. |

Quiz options can contain duplicate definition text: distractors are chosen by different words, not distinct meanings (`QuizService.cs:70–88`). The first option with matching correct text becomes authoritative. This is a credible learning-quality ambiguity to address in shared question generation, not evidence of robust recall grading.

**R7 conventions verified in controllers/helpers and recorded in** [the contract reference](../R7-Remediation/R7-api-contract-reference.md), sections 3–8 and endpoint matrix: camelCase JSON; word/quiz HTTP 200 successes use `{success:true,data:...}`; application failures have exactly `success,data,error,message,code,traceId`; validation uses extended ValidationProblemDetails with `validation_failed`; Bearer challenges can be bodyless. `Helpers/ApiErrorResults.cs:23–50` allowlists category/code pairs; an invented service code currently becomes 500. Existing routes, fields, nullable answers, zero-valued options and retry semantics must remain protected.

R7's [completion record](../R7-Remediation/R7-api-contracts-remediation-completion.md), sections 7,10,20, records preservation of R5 identity and R4 transaction behavior, with no R7 schema change. Historical test/release outcomes are not new verification performed here.

## 3. Existing Data Model Assessment

`UserWord.cs:8–45` has Id, UserId, WordId, AddedAt, nullable LastReviewedAt/LastCorrectAt, cumulative CorrectAnswers/TotalAttempts, preferred definition, favorite and notes. `CreatedAt`, `CustomDefinition` and `DifficultyLevel` are `[NotMapped]`; DifficultyLevel's spaced-repetition comment does not make it persistent scheduling state. The obsolete comment in `WordService.cs:279` also calls IsFavorite unmapped, but the actual model maps it and a favorite migration exists. Prefer mappings over comments.

**Actively maintained:** `QuizService.cs:307–313` increments attempts and updates LastReviewedAt for every generated question, including unanswered ones; correct answers also increment CorrectAnswers and update LastCorrectAt. Repository search found these service writes and no interval/due scheduling. Vocabulary responses expose counters and computed nullable accuracy (`UserVocabularyDTOs.cs:16–18`), but not review dates. AddedAt is written on add (`WordService.cs:281`); duplicate add does not refresh it.

QuizResult (`Models/QuizResult.cs:7–25`) retains user/saved word/session/type, binary correctness, answer text and AttemptedAt. All current generated modes persist QuizType.Definition, and ResponseTimeSeconds is always zero (`QuizService.cs:262–277`). Result text is limited to 500 characters while definitions allow 1000. Do not infer quality ratings, real timing, exact tested definition identity or scheduler state from these rows.

**Alternatives:** scheduling columns directly on UserWord are simpler and preserve identity, but make legacy LastReviewedAt semantics and generic tracked counter updates harder to isolate. A one-to-one UserWordReviewState has one extra join yet cleanly separates quiz statistics, scheduling concurrency and future algorithm state. A per-definition card table would support multiple independently scheduled meanings, but changes product scope and is not recommended for F6.

**Proposed unit of learning:** one selected meaning per UserWord, not all definitions independently. Keep `(UserId,WordId)` unique and all dependent saved-word IDs intact.

## 4. Algorithm Comparison

| Dimension | Leitner system | SM-2 | Custom interval ladder |
|---|---|---|---|
| Complexity | Low: promote/reset a box and assign its interval. | Moderate: quality grade, ease update, successful repetition sequence, rounding and relearning policy. | Low: success advances a step; failure resets it. |
| Database | Box, last review, due timestamp; history recommended. | Ease factor, repetition count, interval, last review/due, quality history. | Step or consecutive-correct count, interval, last review/due, versioned events. |
| Accuracy/limits | Coarse groups; no measured forgetting prediction; box intervals are configuration. | Per-item ease adapts to graded performance, but remains heuristic and depends on meaningful ratings. | Transparent deterministic behavior; cannot estimate individual retention or distinguish hesitant recall from guessing. |
| Quiz compatibility | Binary scoring maps naturally to promotion/reset. | Current correctness alone loses the 0–5 quality information; binary mapping would be an adaptation. | Exact fit for server-evaluated correct/incorrect answers. |
| UX | Explainable boxes, but unnecessary vocabulary for this UI. | Can add nuanced difficulty feedback; more interaction and ambiguous rating choices. | Simple feedback: correct/incorrect and next review time. |
| Testing | Box transitions, boundaries, reset rules. | Formula precision, grade mapping, ease floor, interval rounding, failure/relearning behavior. | Small transition matrix plus scheduling/concurrency tests. |
| Extensibility | Later migration needs algorithm/version history. | More adaptive state today; not directly equivalent to FSRS. | Versioned scheduler boundary and history enable future migration; adaptive state can be added later. |

SM-2 uses a 0–5 grade, initial ease 2.5, ease floor 1.3, first intervals of 1 and 6 days and subsequent ease-based multiplication rounded upward. It resets the repetition sequence after failure and also specifies same-day repetition for low grades. Those semantics exceed current binary quiz evidence. See [the original SM-2 specification](https://www.super-memory.com/english/ol/sm2.htm). These rules are a comparison, not the proposed F6 algorithm.

The [Anki manual background](https://docs.ankiweb.net/manual/background) explains active recall and spacing. Neither it nor this analysis establishes that any proposed ladder achieves a particular retention percentage. Leitner here means the general promote/reset model; the proposed interval values are not claimed as a canonical Leitner standard.

## 5. Recommended Algorithm

**Proposed `interval-v1`:** use the ladder `1,3,7,14,30,60,90` elapsed UTC days. Store ConsecutiveCorrectCount, capped at seven, and ReviewIntervalDays. Initially count=0, interval=0, last scheduled review=null and due=enrollment time.

- Correct: increment the capped count, select ladder[count-1], and set due=accepted server review time + interval.
- Incorrect: count=0, interval=1 day, due=accepted server time + one day; increment cumulative lapse count. Show the correct meaning immediately.
- The next correct answer after a failure schedules one day again. Repeated failures stay at one day; they do not create negative intervals or repeated same-session promotions.
- Missed reviews remain due. Anchor the next interval to actual accepted review time, not the old due date; do not grant an untested overdue bonus or manufacture missed attempts.
- At the last step, correct answers remain on the 90-day interval. Limits and date overflow must be validated; no random interval fuzz in v1.

Example: a new word passes on days 0,1,4 and is next due on day 11. Failure on day 11 resets count to zero and schedules day 12; passing on day 12 schedules day 13. This example defines the proposed behavior, not validated learning efficacy.

This resembles a fixed-box progression but avoids an additional box concept or redundant BoxNumber field. It is preferable now because the observed answer signal is binary, the UI can explain it, and deterministic tests are straightforward. SM-2 is a reasonable later choice if graded recall becomes an approved UX. No claim is made that the custom ladder is scientifically superior.

**FSRS influence:** preserve precise timestamps, elapsed intervals, meaning identity, review format, outcome/rating source and scheduler version in history. FSRS models stability/difficulty and uses four ratings; current binary multiple-choice outcomes are not equivalent to measured graded recall. A later migration must define and label any rating mapping and select a version/parameters; do not invent missing historical ratings. See [the maintained FSRS algorithm documentation](https://github.com/open-spaced-repetition/awesome-fsrs/wiki/The-Algorithm). No FSRS package, optimizer, training service or stability field is needed for F6.

## 6. Proposed Database Design

**Proposed UserWordReviewState**: primary key and FK UserWordId (one-to-one). Include UserId for efficient owner-scoped queries, protected by a composite relationship to UserWord's `(UserId,Id)` alternate key if duplicated; alternatively join UserWord and omit redundant ownership. Preserve the existing `(UserId,WordId)` unique index. Do not store an independently assignable owner without database enforcement.

| Field under evaluation | Recommendation |
|---|---|
| NextReviewAt | Nonnullable UTC instant for enrolled active rows; due uses `<= now`. Initial value is explicit enrollment/backfill time. |
| LastReviewedAt | Nullable on the **review state**, meaning last accepted dedicated scheduled review. Keep UserWord.LastReviewedAt's legacy quiz meaning. |
| ReviewIntervalDays | Nonnegative integer; zero only for new state, otherwise ladder value. |
| EaseFactor | Omit for interval-v1. Add later only when the selected scheduler requires it; speculative 2.5 defaults would imply nonexistent evidence. |
| RepetitionCount | Omit redundant success-sequence state; if added later define whether total events or successful repetitions. Use history for total review counts. |
| ConsecutiveCorrectCount | Integer 0–7, reset on failure or meaning change. Drives progression. |
| ReviewStatus | Explicit stable integer/string mapping for New, Learning, Reviewing, Suspended. New has never been reviewed; Learning includes failures and early steps; Reviewing begins at count>=3. Suspension mutation UI is outside initial scope. Due is derived, not a status needing a nightly update. |
| Additional state | AlgorithmVersion=`interval-v1`, ScheduleVersion (application-managed integer concurrency token), ScheduledWordDefinitionId, cumulative LapseCount and EnrollmentAt. |

**Proposed ReviewHistory**: Id, UserWordId, durable SessionItemId, client SubmissionId, ReviewedAt UTC, target definition ID/text snapshot, prompt/options/selected answer or references to immutable snapshots, IsCorrect, response format, outcome source, nullable explicit recall rating and rating scale, algorithm version, previous/next due timestamps, previous/next intervals and count, previous/next ScheduleVersion, and elapsed time since the last scheduled review (null on first review). Store only the answer/prompt data needed for audit/replay; allow definition-length content rather than copying QuizResult's 500-character cap. Avoid logging raw user responses globally.

History is append-only for normal application operations, indexed by `(UserWordId,ReviewedAt,Id)`; enforce a unique SessionItemId and owner-scoped SubmissionId for exactly one accepted event. Define retention and deletion policy before promising permanent FSRS training data. FK deletion through UserWord may cascade like QuizResult; additional User links should use NoAction to avoid SQL Server multiple cascade paths. Historical snapshots must survive preferred-definition changes; do not cascade history when a definition is removed.

**Proposed ReviewSession/ReviewSessionItem**: durable session ID, owner, CreatedAt/ExpiresAt, item IDs, saved-word/definition snapshot, opaque option IDs and correct option, captured schedule version, completion state and immutable accepted response. Keep sessions bounded (default 10, max 20; proposed expiry 30 minutes). Session persistence adds schema but preserves answer keys and retry receipts across app-pool recycling. Remove expired unanswered sessions opportunistically on later requests in bounded batches; retain accepted receipts/history per approved retention policy. GET requests do not create sessions.

Use SQL Server datetime2 with an explicit application UTC convention and UTC-kind materialization/serialization for new dates; SQLite uses comparable DateTime storage, not provider-dependent DateTimeOffset comparisons. Capture one TimeProvider instant per operation. Return ISO timestamps with `Z`; don't assume SQLite returns DateTime.Kind=Utc. Existing R7 timestamp-kind behavior is recorded in its completion section 14; do not redesign all legacy dates under F6.

Use portable Guid/int concurrency values instead of SQL Server-only rowversion. Validate bounds, status/interval consistency and history uniqueness in services and provider-compatible constraints. Index due selection on `(UserId,ReviewStatus,NextReviewAt,UserWordId)` if ownership is enforced in state; inspect the actual SQL Server plan before finalizing status/index ordering. Explicit defaults/backfill are required: avoid provider-specific UTC default functions in a common model or silently setting nullable due dates to DateTime.MinValue.

## 7. Proposed Backend Architecture

Add IReviewService/ReviewService and ReviewsController, typed ReviewDTOs, an IReviewScheduler pure calculation boundary and injected TimeProvider. Register through `Program.cs` alongside current services (`Program.cs:26,66–68`). Change ApplicationDbContext for proposed entities and relationships. WordService's add/enrollment and preferred-definition methods would require transactional coordination; their existing response DTOs need not change.

Extract narrowly scoped definition resolution and question/option evaluation from `QuizService.cs:30–102,244–248` and `WordService.cs:601–623`. Use stable DisplayOrder then definition ID tie-breaking. Share pure code, not quiz session storage or SubmitQuizAsync. QuizService and IQuizService can retain their public interfaces and HTTP behavior; extraction must be covered by regression tests.

**Answer authority:** authenticated identity comes from JWT claims. Session ownership, target word, answer key, due eligibility and algorithm state are server-owned. Clients send item/option/submission identifiers, never correctness, next due, UserId, interval or grade inferred from timing. Recheck selected meaning and schedule version during submission.

**Transaction per answer:** load/check durable receipt first, then owner-scoped item/state; verify expiry, pending status, meaning/version and valid option; evaluate; conditionally update state at captured version; insert history/receipt and mark item complete; save/commit together through EF execution strategy. Request cancellation before commit rolls back. Network loss after commit is recovered through the same receipt, not a second review. Compute times/versioned responses once and keep them stable across execution-strategy retries.

**Concurrency:** atomic conditional update or an EF concurrency token on review state ensures overlapping sessions cannot advance the same schedule twice. One request succeeds; another stale item returns proposed 409 `review_state_changed` and reload guidance. Same-key identical retries return the committed 200 response; same key with different item/answer returns proposed 409 `review_submission_conflict`. A unique item event also rejects a different SubmissionId attempting a second answer. Process locks alone are insufficient. On commit ambiguity, reread the receipt before concluding failure. Recognize provider-specific unique errors narrowly; do not classify every DbUpdateException as conflict.

Preferred-definition change and schedule reset must occur in the same transaction and advance ScheduleVersion. Otherwise an answer for an old meaning can be accepted while a new meaning is selected. Same-definition updates should be no-ops for scheduling. Any missing/nonowned word/item/session is concealed with equivalent safe 404s. Validate empty GUIDs, null/malformed collections and option presence before mutation. Zero is a valid option ID. New code/message pairs must be explicitly added to ApiErrorResults and safe validation/Swagger metadata; current helpers do not automatically understand new codes.

## 8. Proposed API Contracts

**All routes, fields and codes in this section require approval.** Preserve the current 14 endpoints; add a review surface rather than overloading existing quiz payloads.

| Endpoint | Proposed request and HTTP 200 data | Behavior/errors |
|---|---|---|
| GET `/api/reviews/due?take=20&cursor=...` | `{items:[{userWordId,word,nextReviewAt,reviewStatus}],nextCursor,asOfUtc}`; no answer keys or definitions used as quiz answers. | Owner-scoped eligible active words, oldest due then saved ID; bounded take, stable cursor anchored to asOf. Empty success is items=[]/nextCursor=null. Invalid query: validation 400. |
| GET `/api/reviews/summary` | `{dueCount,newCount,overdueCount,blockedCount,nextReviewAt,asOfUtc}`. nextReviewAt nullable and denotes earliest future eligible due instant. | Global owner counts, independent of vocabulary UI page. New is a subset of due; define overdue as due strictly before asOf, not a separate bucket to sum. Blocked means no usable meaning/review format. |
| POST `/api/reviews/start` | `{questionCount:10,mode:"mixed"}` → `{sessionId,expiresAtUtc,items:[{itemId,userWordId,questionType,prompt,options}]}`. | Snapshot targets from due words; distractors may come from eligible **not-due** vocabulary. Empty due queue: 200 empty session result with sessionId/expiry=null. Unsupported format/small vocabulary: proposed 400 `review_unavailable`. |
| POST `/api/reviews/submit` | `{sessionId,itemId,submissionId,selectedOptionId}` → `{itemId,isCorrect,selectedAnswer,correctAnswer,nextReviewAt,reviewIntervalDays,reviewStatus,sessionSummary}`. | Exactly one item, immediate feedback; null selected answer is invalid. 400 `invalid_review_answer`; 404 `review_session_unavailable`; 409 `review_state_changed`/`review_submission_conflict`; safe 500. |

Use numeric saved-word/option IDs, GUID session/item/submission strings and JSON UTC date strings. SubmissionId is client-created once per logical answer and reused only with that immutable answer. Save the full accepted response so replayed schedules/sessionSummary are the original receipt, not recomputed from later state. Session summary distinguishes accepted correct/incorrect, skipped/pending and total; abandoning pending items does not count as failure. A later GET session recovery endpoint is optional scope requiring approval; identical answer replay suffices for v1 transient uncertainty.

Every successful response uses `SuccessResponse<T>` with exactly success/data at the envelope level. Application errors retain R7's six fields and equal safe error/message; validation failures retain application/problem+json and safe errors; invalid JWT may yield a bodyless framework 401. All review routes require Bearer. Do not introduce a 201 success convention, bare entities, arbitrary service text or an unapproved 403 contract. Document required option presence with JsonRequired so explicit zero remains valid. Add all approved routes to operation/schema filters and the reference matrix without weakening existing contract assertions.

GET due/summary use request-time eligibility and never enroll, reserve, or reschedule words. A cursor is an ordering aid, not a durable reservation: concurrent updates can remove items from subsequent pages. Start rechecks authoritative state and snapshots up to the approved cap. Review UX must tolerate counts changing between dashboard and start.

## 9. Proposed Angular Architecture

Verified routes in `UI/src/app/app.routes.ts:10–21` are dashboard, vocabulary and quiz under authGuard, with standalone components. Dashboard (`components/dashboard/dashboard.component.ts:14–76`) currently consumes AuthService, not a review API or actual analytics. QuizComponent stores a session, selectedAnswers Map, progress and results; submits once after the final question, with failures preserving state (`quiz.component.ts:31–257`). Its template provides option buttons and completion results, but no live answer feedback or scheduling information.

Add typed `review.model.ts`, ReviewService wrapping existing ApiService, and a guarded `/reviews` route. Proposed components: ReviewSessionComponent, ReviewSummaryComponent (or local completion template), and a dashboard due indicator. Reuse layout/question presentation selectively via a presentational component if justified; do not parameterize the entire existing QuizComponent around a different persistence lifecycle. Immediate accepted review feedback prevents editing previous accepted answers, unlike quiz navigation.

Dashboard retrieves summary after authentication and refreshes on return from reviews; display actual due count and Start Review action. Vocabulary can link to reviews and show upcoming information from a separate review state lookup only if approved; avoid adding fields to existing exact-shape DTOs by default. Completion displays accepted total/correct/incorrect, pending/skipped and upcoming dates without suggesting every pending word was reviewed.

Review state flows idle → loading → question → submitting → feedback → next → completion, with explicit empty, failed and stale states. Preserve the active answer and SubmissionId while a submission outcome is uncertain; retries reuse the immutable request. Reconcile a committed receipt before unlocking a different answer. Expired/stale sessions offer explicit restart; never silently requeue answered items. Normalize bodyless 401 and R7 application/validation failures through the existing error module with approved review contexts. Distinguish failed retrieval from a successful zero queue.

Use responsive stacked controls and wrapping action rows, readable long definitions, no fixed-height viewport assumptions, sufficient touch targets, keyboard-accessible options (radio group semantics or aria-pressed), visible focus, labelled progress, focus movement to new prompts, aria-live feedback/loading, and textual correct/incorrect labels in addition to color. Upcoming times are formatted in the user's browser zone with a clear local-date label. No timezone preferences page is needed for elapsed-time scheduling.

## 10. Review Scheduling Lifecycle

1. **Save:** WordService creates the canonical owner-scoped UserWord and new review state in one transaction. A duplicate save returns existing identity and preserves its schedule.
2. **Eligibility:** new usable meanings are due immediately. Missing/blank definitions are blocked from question generation and surfaced as blockedCount, not counted as actionable due. No worker marks records due.
3. **Start:** select oldest eligible due targets, bounded per session; snapshot effective definition, options and ScheduleVersion. Starting alone does not advance a schedule or mark a review attempted.
4. **Submit:** validate owner/session/item/option and evaluate the server-held key. Explicit unanswered/skipped items create no event in dedicated reviews. Existing quiz unanswered behavior stays unchanged.
5. **Persist:** write exactly one review event and its state transition atomically, returning correct-answer feedback and next review time.
6. **Complete/abandon:** summarize accepted events. Pending items remain due; completed items keep their committed schedule even if the user leaves early.
7. **Become due again:** subsequent reads compare stored due instants with server UTC now, inclusive at equality.

**Meaning policy proposed:** review the explicit preferred definition, otherwise the first usable definition in saved POS ordered by DisplayOrder/Id. A preferred-definition change resets count/interval, schedules immediately and increments ScheduleVersion while preserving review history and legacy statistics. If a canonical definition's text changes without ID changing, target snapshot/version checks must detect it or an approved content revision must invalidate the session; a FK alone cannot detect semantic edits.

Multiple definitions do not each earn independent schedules in F6. Distractors need distinct unambiguous answer text; equivalent definitions should not be marked wrong solely due to option IDs. If valid options cannot be generated, classify the item/session as unavailable rather than fabricating content. One due word can be reviewed using three non-due distractors, but users with fewer than four usable saved words still require an approved alternative format or an explicit availability message. Recommended initial decision: preserve multiple choice and clearly communicate the small-vocabulary limit; typed recall or self-rating requires separate approval.

Repeated failures produce corrective feedback and one-day retries; an optional failure count can suggest revisiting the chosen definition. Automatic suspension or an intensive same-day learning loop is not proposed. Large backlogs remain visible; cap session size without hiding true counts. A daily new-word cap/backfill staggering is an optional product decision, not silently applied scheduling.

UTC elapsed days avoid server/browser timezone disagreement and DST-length changes. A one-day interval means 24 hours, not next local midnight. Local display may show a different calendar date; the server remains the authority. Future local-day preferences would require a stored timezone and a separately approved boundary policy.

## 11. Quiz Integration Strategy

**Recommend dedicated reviews only change schedules.** Ordinary quizzes retain cumulative counters, timestamps, results, history, unanswered scoring and submission recovery. Automatically scheduling all quiz questions would allow practice or guessed answers to defer due reviews and require new meaning/session-version semantics on the old contract.

Initial dedicated reviews should write review state/history **without incrementing UserWord's legacy quiz counters or inserting QuizResults**. This preserves current vocabulary accuracy and quiz-history meaning; label them as quiz statistics where needed. Combining all learning attempts is a possible alternative requiring explicit product/contract approval and safe atomic counter increments across independent quiz/review sessions. Existing read-modify-write quiz aggregates and per-session locks do not prove serialization across different sessions reviewing the same word (`QuizService.cs:294–315,511–514`).

Pure server option evaluation, selected-meaning resolution, no-key DTO projection and safe error helpers are reusable. Quiz-specific process-local sessions and end-of-session submission are not appropriate foundations for per-answer durable scheduling. No optional review flag should be silently introduced into `/api/quiz/submit`.

## 12. Data Migration Considerations

Migrations live in `VocabularyApp.Data/Migrations`, with a model snapshot and generally generated designer files. The R4 uniqueness migration is hand-written without the same companion pattern. R5 (`20260829155134_CorrectUserWordIdentity.cs:12–32`) runs SQL Server-specific IF/THROW preflight and swaps the identity index; the full historical migration chain is not provider-neutral. Do not assume SQLite can run it.

**Proposed rollout:** additive review tables/indexes, deterministic owner-preserving state backfill, then compatible service/UI enablement. Backfill one state per existing UserWord, no identity rewrite, no replacement saved IDs and no synthetic review history. Existing quiz attempts/review dates are retained but do not seed successful ladder steps; meaning/quality/order evidence is incomplete. Use a captured UTC enrollment instant for initial due, count=0 and LastReviewedAt=null on review state. Block unusable meanings at query time. Repeated backfill must skip already-enrolled rows rather than reset schedules.

New saves during rollout require coverage: use a brief controlled write window or a backfill catch-up after dual creation is enabled. Old application code cannot create review state, so feature reads must not assume enrollment coverage until catch-up/verification completes. A missing review row means not enrolled, not fully learned; expose/monitor enrollment coverage separately.

Do not use historical AddedAt to schedule years-old missed reviews, or default every existing row to an inferred advanced interval. Immediate enrollment of all legacy words can create a backlog; confirm whether to apply an explicit onboarding/new-word limit, while keeping the full backlog count visible. Null/default decisions must be documented before generating migration SQL.

SQL Server rehearsal must check production EF migration history, existing identity/quiz indexes, orphan/invalid definition selections, row counts, migration duration and locks, duplicate backfill prevention, index plans and backup restoration. Current production counts/state are unknown; no database was accessed. SQLite EnsureCreated only checks the final relational model, not upgrades or SQL Server datetime/default/cascade/index behavior.

## 13. Testing Strategy

This is a proposed strategy only; **no tests or builds were run**.

**Existing foundation:** .NET 8/xUnit API tests use WebApplicationFactory, isolated in-memory SQLite with FK enforcement and injected faults (`WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs:20–115`; `RelationalDatabaseFixture.cs:12–23`). QuizApiCollection disables parallel execution due to static quiz sessions (`QuizApiCollection.cs:5`). API contract helpers and metadata/OpenAPI tests are available. Angular uses Jasmine/Karma (`UI/package.json`; `angular.json` test builder) and HttpClient testing with raw R7 fixtures (`components/quiz/quiz.component.spec.ts:12–261`; `testing/api-contract.fixtures.ts`).

| Layer | Proposed meaningful verification |
|---|---|
| Pure scheduler | Every ladder transition; capped success; failure/reset/first success after failure; lapse count; exact UTC due instant; invalid state and overflow; version stability. Inject fake TimeProvider rather than sleeping. |
| Authorization/validation | Anonymous/expired tokens; other owner's word/session/item concealed as 404; fabricated options; zero option accepted; omission/null malformed requests rejected; no mutation on invalid input. |
| Persistence | State/history/item commit atomically; injected SaveChanges/transaction failure leaves all unchanged; definition change reset and identity/dependent preservation; replay after host recycle. |
| Idempotency | Identical receipt replay returns exact original response; changed payload with same key conflicts; different key for completed item cannot reschedule; commit/network ambiguity reconciles receipt. |
| Concurrency | Same item parallel submissions; different sessions same word; meaning change racing answer; version-based single winner; rollback and retry behavior. Use separate contexts/connections for realistic races and SQL Server confirmation, not only a shared SQLite connection. |
| Time boundaries | One tick before/at/after due and expiry; explicit expiry equality policy; leap day/DST browser displays; UTC-kind serialization; overdue behavior and no artificial missed attempts. |
| Existing data | Backfill preserves IDs/counters/notes/favorites/definitions; empty/unusable definitions; reentrant backfill; new saves during catch-up; no fabricated history. |
| Contract regression | Exact raw success/error/validation shapes, nullable fields, stable codes, required option zero, no answer keys, operation security/nullability, all original 14 endpoint contracts. Update exact endpoint-count assertions intentionally for added routes. |
| Angular retrieval/session | Typed due/summary requests and Bearer headers; empty vs error; loading guard; state transitions; pending items; approved score/schedule feedback; dashboard refresh and actual global counts. |
| Angular submission/recovery | Double clicks; option zero; immutable retry ID; timeout after server commit; stale/expired restart; 401/400/409/500 and malformed success; preserve selections; keyboard/focus/live feedback and mobile layouts. |
| Full lifecycle | Save → immediate due → start → correct/incorrect → persisted schedule/history → no longer due → fake-clock due again; definition reset; abandon partial session; other-user isolation. |
| SQL Server rollout | Disposable SQL Server upgrade from pre-F6 snapshot with realistic data, constraints/indexes/cascade paths/defaults/UTC query translation, old binary compatibility, migration rollback/restore rehearsal. SQLite FK tests remain complementary. |

Retain `QuizApiTests.cs:70–171,401–564,703–1013` coverage for scoring, timestamps, duplicate/concurrent submissions, rollback, raw input and vocabulary recheck; ordinary quizzes must not write review state. Existing frontend quiz navigation/history/error tests remain relevant. Future CI should use the existing full backend and Angular gates, with a separately approved SQL Server migration verification route; do not add a live production test database dependency.

## 14. Production Deployment Considerations

Verified workflow `.github/workflows/backend-tests.yml:12–132` runs backend tests, frontend ChromeHeadless tests, then builds/publishes and automatically deploys master pushes. `Program.cs` registers SQL Server but contains no startup Migrate/EnsureCreated call. Inspected publish/deploy scripts and workflow show no migration-application step; package scripts reject `.sql` payloads (`scripts/ci/Publish-VocabularyApp.ps1:88`; `Deploy-SmarterAsp.ps1:53`). A deployed DLL therefore must not be assumed to upgrade production schema.

Before a later release, designate migration ownership and approved execution mechanism. Prefer reviewed SQL applied separately to a backed-up SQL Server database before enabling code that requires it, with enrollment catch-up and postchecks. Adding an automated migration pipeline is a decision requiring approval, not an existing capability. Do not casually enable runtime migrations on shared hosting or bundle SQL into Web Deploy artifacts that currently reject it.

Use additive tables so the prior binary can run after disabling reviews; retain original endpoints and statistics. A Down migration deleting history/state destroys learning data, and restoring the database backup after new production writes also loses subsequent activity. Prefer feature disable and forward fixes; validate restore procedures before treating rollback as available.

SmarterASP.NET shared-hosting/app-pool recycling makes process-local pending sessions fragile; proposed durable items/receipts address this without a worker. Actual account-specific SQL permissions, table/index capacity, backup controls and connection/resource limits remain unknown and must be verified by the developer before rollout. Keep sessions/page queries bounded and cleanup opportunistic. No hosted service or external queue is justified by request-time due comparisons.

Proposed production verification after authorized deployment: identify applied migration/version; compare enrollment coverage and identity counts; smoke-test one owner's due/start/answer/replay flow; inspect saved UTC due timestamps and ownership isolation; verify preference-reset behavior and ordinary quiz regression; review safe conflict/error metrics. Do not claim production verification from historical R7 results. Because master deploys automatically, merge timing must be coordinated with schema readiness; no commit/merge is authorized by this analysis.

## 15. Risks and Technical Debt

| Risk | Consequence and proposed response |
|---|---|
| Binary recognition/guessing vs recall | Intervals may overestimate learning; identify review format and avoid retention guarantees. Graded or typed recall is later approved scope. |
| Duplicate meanings in options | An apparently valid answer may be marked wrong; distinct/validated options and explicit unavailable behavior. |
| Overlapping/old-meaning answers | Double advancement or wrong semantic evidence; state concurrency version, immutable snapshots and transactional preference resets. |
| Existing aggregate updates | Different quiz sessions may contend on read-modify-write counters; F6 isolates scheduling/counters rather than claiming current locks solve all races. Track broader quiz aggregate concurrency separately. |
| Legacy timestamps/counters | False historical mastery if backfilled as schedule; enroll as new without fabricating events. |
| Process-local quiz sessions | Ordinary quizzes remain recycle-sensitive; durable F6 session design does not silently migrate the quiz architecture. |
| SQL Server migration gap | Passing SQLite tests can conceal upgrade/default/cascade errors; separate disposable SQL Server rehearsal and explicit rollout owner. |
| Large vocabularies/backlogs | Full materialization/shuffling becomes costly; indexed bounded due targets, bounded distractor query and actual summary aggregates. Avoid ORDER BY random over entire large tables; inspect plans. |
| Definition/answer size mismatch | Existing QuizResult text cap is shorter than definitions; new history must support actual snapshot lengths. Quiz cap remediation is a separately scoped follow-up. |
| Durable snapshot/history growth | More storage and private learning data; bounded sessions, indexed event queries, approved retention/deletion policy. |
| Scheduler evolution | Unversioned changes make past transitions irreproducible; version policy and event state snapshots. |

Dependency vulnerability assessment is explicitly deferred. No broad vulnerability audit, package installation or remediation occurred. R7 historical dependency findings remain separate technical debt; they are not a general F6 blocker. No incidental concrete credential exposure or exploitable security flaw is asserted by this inspection; ownership, answer authority and safe error handling remain concrete F6 design requirements.

## 16. Open Questions and Decisions Required

All following choices require developer approval **before implementation**:

1. Approve interval-v1/default ladder, one-day failure delay, capped progression, status thresholds and elapsed-UTC scheduling, or choose SM-2 with an explicit graded-recall UX.
2. Approve one selected meaning per UserWord and immediate schedule reset on genuine preferred-definition changes; specify canonical definition-text edit behavior.
3. Approve dedicated review scheduling only and independent review statistics; decide whether legacy quiz counter labels should be clarified.
4. Approve multiple-choice v1 and the fewer-than-four-usable-words limitation, or explicitly add a different recall format. Define handling of ambiguous distractors.
5. Approve one-to-one review state plus event history and durable session/items; confirm ownership constraints, receipt retention, deletion behavior and session expiry boundary (`now >= expiresAt` proposed).
6. Approve new endpoints/DTOs/codes, per-answer persistence, exact-response idempotency and stale-session recovery. Optional session recovery/state lookup endpoints remain separate choices.
7. Approve backfill enrollment time and immediate new status, with no inferred mastery; decide whether initial backlog/new-word limits are necessary and how users see them.
8. Identify production schema execution owner, SQL Server rehearsal environment, backup/restore permissions, release coordination and feature-enable mechanism. Production data volumes/quality are unknown.
9. Confirm mobile/accessibility acceptance criteria, history retention and future FSRS expectations. Binary events should retain their true provenance rather than promise direct optimized FSRS conversion.

These are decision inputs, not a permission request to run deployment or an implementation plan. The analysis is complete without choosing them on the developer's behalf.

## 17. Recommended Implementation Phases

High-level dependency ordering requested for this analysis; detailed implementation planning waits for explicit approval:

1. Resolve algorithm, meaning, quiz/statistics, small-vocabulary and API decisions; define acceptance behavior.
2. Validate additive model, ownership/concurrency rules and migration/backfill design against disposable SQL Server and SQLite.
3. Establish pure scheduler/definition/evaluation boundaries and backend lifecycle with durable session/receipt behavior, preserving quiz contracts.
4. Introduce guarded review UX, dashboard summary and approved upcoming information with accessible feedback/recovery.
5. Complete proposed lifecycle, concurrency, raw-contract, frontend and existing-quiz regression verification; rehearse schema upgrade and rollback limits.
6. Coordinate approved production schema rollout, enrollment coverage, compatible release and smoke verification; evaluate observed learning outcomes before algorithm expansion.

No phase has begun. These phases confer no authorization to create migrations or change application/tests/workflows.

## 18. Final Recommendation

Proceed, after explicit approval and a separate implementation plan, with a versioned binary interval ladder and dedicated review service. Reuse owner identity, typed transport, safe errors, preferred-meaning resolution and pure quiz evaluation; preserve the existing quiz lifecycle and statistics.

Use separate one-to-one scheduling state, append-only events and durable bounded sessions/receipts to make reviews auditable and safe under retries, overlapping sessions and app-pool recycling. Compute due eligibility during requests. Address preferred-definition invalidation, small-vocabulary limitations and SQL Server migration verification before implementation.

**Execution outcome:** this analysis document is the sole intended edit. No application code, tests, migrations, packages or workflow were changed; no tests/builds or production operations were performed. Stop here and wait for explicit developer approval before producing an implementation plan or implementing F6.
