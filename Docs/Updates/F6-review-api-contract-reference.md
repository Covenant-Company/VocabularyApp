# F6 review API contract reference — Phase 1

Date: 2026-10-12. Decisions D1–D9 and P1–P5 are approved in the [implementation plan](F6-spaced-repetition-implementation-plan.md). **All endpoints below are planned, not yet implemented.** Phase 1 adds a pure scheduler, DTOs, inactive configuration options and focused unit tests. No review database schema, durable session service/controller, Angular interface or production feature exists yet.

## 1. Scheduling domain contract

`Models/ReviewTransition.cs` contains immutable `ReviewScheduleState`, `ReviewTransition` and `ReviewStatus`. These are domain values, not EF entities or public wire payloads.

State properties: EnrollmentAt/NextReviewAt UTC DateTime; LastReviewedAt nullable UTC DateTime; ReviewIntervalDays/ConsecutiveCorrectCount/LapseCount Int32; ReviewStatus enum; ScheduleVersion positive Int64; AlgorithmVersion string `interval-v1`. Transition contains PreviousState, genuine binary IsCorrect, AcceptedReviewAtUtc and NextState. No inferred recall grade or mastery field.

`IReviewScheduler` exposes Enroll(UTC time), Reset(state, UTC time), Review(state, bool outcome, UTC accepted time), and IsDue(state, UTC asOf). `IntervalV1ReviewScheduler` accesses no clock/database/network/randomness and does not mutate inputs. Caller supplies trusted server time; API evaluation and TimeProvider orchestration are later work.

| Operation/outcome | Count | Interval days | Status | NextReviewAt |
|---|---:|---:|---|---|
| Enrollment/genuine reset | 0 | 0 | New=0 | Enrollment/reset instant |
| Correct, count becomes 1 | 1 | 1 | Learning=1 | Accepted time + 24 hours |
| Correct, count becomes 2 | 2 | 3 | Learning=1 | Accepted time + 72 hours |
| Correct, count becomes 3 | 3 | 7 | Reviewing=2 | Accepted time + 168 hours |
| Correct, count becomes 4 | 4 | 14 | Reviewing=2 | Accepted time + 336 hours |
| Correct, count becomes 5 | 5 | 30 | Reviewing=2 | Accepted time + 720 hours |
| Correct, count becomes 6 | 6 | 60 | Reviewing=2 | Accepted time + 1440 hours |
| Correct, count becomes/stays 7 | 7 | 90 | Reviewing=2 | Accepted time + 2160 hours |
| Any incorrect | 0 | 1 | Learning=1 | Accepted time + 24 hours |

Enrollment has version=1, lapse=0, last review=null. Every accepted answer and genuine reset increments the checked version once. Failure increments lifetime lapse count; correct/reset preserve it. Reset clears current-epoch last review/count/interval and preserves EnrollmentAt and lifetime lapses. Reset is called only for a genuine meaning/content change; same-effective-definition updates leave the state untouched. Reset produces no fabricated answer event.

Due is inclusive `NextReviewAt <= asOf`; overdue is strictly earlier. Early acceptance is rejected. An overdue answer uses the actual accepted instant with no bonus. UTC days remain 24 hours across DST/leap days. Inputs must have UTC Kind, supported algorithm, valid enum/count/interval/status/lapse/version and coherent enrollment/last/due times. Reviewed due time must exactly equal last accepted time plus its interval. Null/invalid state or non-UTC input throws an ArgumentException subtype; early/backwards times and date overflow throw ArgumentOutOfRangeException; checked version/lapse overflow throws OverflowException. Failed calculation returns no transition and mutates nothing. These are domain exceptions, not new live API error responses.

## 2. Planned endpoint surface and envelopes

| Planned method/route | Request | HTTP 200 data |
|---|---|---|
| GET `/api/reviews/due` | ReviewDueQueryDto query | ReviewDueResponseDto |
| GET `/api/reviews/summary` | None | ReviewSummaryResponseDto |
| POST `/api/reviews/start` | ReviewStartRequestDto | ReviewStartResponseDto |
| POST `/api/reviews/submit` | ReviewSubmitRequestDto | ReviewSubmitResponseDto |

All require Bearer authentication and JWT owner scope. Success follows existing word/quiz R7 conventions: exactly `{ "success": true, "data": <DTO> }` using SuccessResponse<T>, camelCase JSON, explicit nulls. No success-level message/error field. Existing 14 endpoint contracts are unchanged. No fifth session recovery endpoint: start carries optional resumeSessionId.

No owner, correctness, algorithm, interval, grade or schedule version is accepted from clients. Opaque option IDs are integers 0..3; session/item/submission IDs are nonempty UUID strings. Dates are ISO UTC strings ending in `Z`; the DTO-scoped ReviewUtcDateTimeConverter rejects non-UTC DateTime values rather than relabelling local/unspecified time. This converter does not change legacy DTOs. Later persistence mapping must explicitly materialize SQL/SQLite dates as UTC.

## 3. Exact DTO properties

All names below are serialized camelCase. `?` means present but nullable on response DTOs; request optionality is stated separately. Lists are JSON arrays. C# definitions are in `VocabularyApp.WebApi/DTOs/ReviewDTOs.cs`.

| DTO | Properties |
|---|---|
| ReviewDueQueryDto | take:int default 20, range 1..100; cursor:string? optional, max 2048 |
| ReviewDueItemDto | userWordId:int; word:string; nextReviewAt:UTC instant; reviewStatus:string (`new`, `learning`, `reviewing`); isUsable:bool |
| ReviewDueResponseDto | items:ReviewDueItemDto[]; nextCursor:string?; asOfUtc:UTC instant |
| ReviewSummaryResponseDto | dueCount:int; newCount:int; overdueCount:int; blockedCount:int; usableDueCount:int; totalEnrolledCount:int; nextReviewAt:UTC instant?; asOfUtc:UTC instant |
| ReviewStartRequestDto | questionCount:int optional/default 10, range 1..20; resumeSessionId:UUID? optional/default null, nonempty if supplied |
| ReviewOptionDto | optionId:int 0..3; text:string |
| ReviewItemDto | itemId:UUID; userWordId:int; questionType:string fixed `word-to-definition`; prompt:string; options:ReviewOptionDto[]; acceptedReceipt:ReviewSubmitResponseDto? |
| ReviewStartResponseDto | availability:string (`ready`, `empty`, `unavailable`); reason:string? (`insufficient_usable_vocabulary`, `ambiguous_options`, `no_usable_due_words`); sessionId:UUID?; expiresAtUtc:UTC instant?; items:ReviewItemDto[]; sessionSummary:ReviewSessionSummaryDto? |
| ReviewSubmitRequestDto | sessionId:UUID; itemId:UUID; submissionId:UUID; selectedOptionId:int 0..3; all four explicitly required |
| ReviewSessionSummaryDto | totalItems:int; acceptedCount:int; correctCount:int; incorrectCount:int; pendingCount:int; isComplete:bool |
| ReviewSubmitResponseDto | sessionId:UUID; itemId:UUID; submissionId:UUID; reviewedAtUtc:UTC instant; isCorrect:bool; selectedOptionId:int; correctOptionId:int; selectedAnswer:string; correctAnswer:string; nextReviewAt:UTC instant; reviewIntervalDays:int; reviewStatus:string (`learning`, `reviewing`); sessionSummary:ReviewSessionSummaryDto |

JsonRequired on every submit member distinguishes omission from valid selectedOptionId=0. Range validates numeric bounds; NonEmptyReviewGuid validates supplied UUIDs, allowing null only for optional resume. Null/type-invalid nonnullable UUID/option JSON fails deserialization. GUID parsing and annotations operate separately; no default Guid.Empty is accepted by validation. String literal annotations document permitted response values; cross-field availability/count consistency and server projection belong to later service tests. Phase 1 DTO tests do not claim MVC routes or error mappings are implemented.

## 4. Backlog, availability and recovery semantics

Due pages expose the full scheduled backlog, including unusable meanings marked isUsable=false, oldest due then UserWordId. Cursor preserves owner/asOf/last ordering and has a planned 30-minute validity with Data Protection integrity; production key persistence is P7, still unresolved. Live pagination does not reserve words and can reflect concurrent rescheduling.

Counts overlap: due includes blocked due rows; new counts all New; overdue is the strict-earlier subset of due; blocked counts due meanings that are unusable; usableDue=due-blocked, without guaranteeing valid distractors. totalEnrolled counts all enrolled saved words. nextReviewAt is the earliest future scheduled time or null.

No due rows: availability=empty, reason/session/expiry/summary=null, items=[]. Due but no usable target: unavailable/no_usable_due_words. Fewer than four usable words: unavailable/insufficient_usable_vocabulary. Otherwise no valid questions: unavailable/ambiguous_options. Empty/unavailable are successful availability data, not error envelopes, and create no session. Ready contains 1..questionCount items (max 20), non-null session/expiry/summary and null reason. Three non-due words may supply distractors. Option text must be distinct and exclude known alternate meanings; do not add recall/self-rating.

Pending items expose no correct key, and acceptedReceipt=null. Resume returns immutable snapshots and receipts for accepted items only. It does not extend the 30-minute expiry; `now >= expiresAt` rejects new answers. Stale pending target/distractor content or scheduling requires an explicit new session. Abandonment leaves pending words due and records no incorrect attempt. Completion counts accepted correct/incorrect separately from pending.

Submission keys are scoped to `(UserId,SubmissionId)` and the immutable session/item/option payload. Exact replay returns the original saved success body before expiry/staleness validation, while changed payload/key reuse conflicts. History/receipts/accepted session snapshots are retained for saved-word/account lifetime under approved P4; authorized deletion revokes replay. Transaction/concurrency persistence is later-phase work.

## 5. Planned R7 error contracts

Application failures use exactly success=false, data=null, error, message (same safe text), code and server traceId, application/json. Binding/annotation validation uses HTTP 400 extended ValidationProblemDetails, application/problem+json, code validation_failed and existing safe extensions/messages. Bearer challenges may be bodyless 401. Framework routing/media errors retain framework behavior.

| Status | Code | Planned safe message |
|---|---|---|
| 400 | invalid_review_answer | The selected answer is not valid for this review item. |
| 404 | review_session_unavailable | This review session is unavailable. Please start a new review. |
| 409 | review_session_expired | This review session has expired. Please start a new review. |
| 409 | review_state_changed | Your review content or schedule changed. Please start a new review. |
| 409 | review_submission_conflict | This submission key or review item has already been used for a different answer. |
| 503 | reviews_disabled | Reviews are temporarily unavailable. Please try again later. |
| 401 | invalid_token | Invalid token |
| 500 | internal_error | An internal error occurred. Please try again. |

Absent/nonowned/wrong-session items are concealed with the same 404. Required-field/type/UUID/range validation produces validation_failed, not a silently incorrect answer. Business option-membership rejection is invalid_review_answer. New helper allowlist pairs and Swagger request/action classification must be added in Phase 3, not Phase 1. Existing shared helpers, schema filters, authentication and controllers are untouched. No new 201/403/410 convention.

## 6. Configuration and later-phase prerequisites

ReviewOptions.SectionName=`Reviews`; EnrollmentEnabled=false and Enabled=false. No runtime binding/DI registration or appsettings changes occur in Phase 1. Later release must verify schema before enrollment hooks, and enrollment/new-writer coverage before review activation. SQL Server rehearsal, production owners/backups/runtime configuration and Data Protection key storage remain release gates P6–P7.

Phase 2 requires separate authorization for the four persistence entities, EF mappings, additive migration and enrollment scripts. Phase 3 implements services/routes/persistence/error/Swagger integration; Phase 4 implements Angular. None is authorized by Phase 1 completion.
