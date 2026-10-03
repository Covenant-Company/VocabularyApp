# R7 Phase 2 — Compatible Angular Auth, Error, and Transport Groundwork

Date: 2026-10-01

Status: **IMPLEMENTED — EXECUTION VERIFICATION PENDING**

This is a Phase 2 implementation record under approved D1–D4 in the [implementation plan](R7-api-contracts-remediation-implementation-plan.md), informed by the [analysis](R7-api-contracts-remediation-analysis.md) and [Phase 1 record](R7-api-contracts-phase-1-implementation-results.md). Phase 3 has not begun. Neither Phase 1 nor Phase 2 is runtime-verified by this task.

## 1. Objective and implemented scope

Prepare Angular to consume the current backend's successful wire contracts and both current and future R7 errors without requiring Phase 3. This addresses Phase 2 portions of F04/F05/F08/F11/F13 through U01/U02/U03/U06 and bounded U04/U05 groundwork.

Production changes are entirely under VocabularyApp.UI. No backend source or test file was changed in Phase 2. No dependencies, infrastructure or deployment changes were needed.

## 2. Files changed in this phase

All UI paths below are relative to `VocabularyApp.UI/src/app/`.

| Production file | Change |
|---|---|
| `models/user.model.ts` | Actual nested AuthResponseData; nullable compatible envelope; required numeric user ID and string/null dates. |
| `models/word-api.model.ts` (new) | Backend-shaped lookup/word/definition/vocabulary DTOs, mutation request/acknowledgement types and encoded lookup/search path helpers. |
| `models/quiz.model.ts` | Required selectedAnswer as string or null. |
| `services/api-error.ts` (new) | Pure status/context-aware error normalization and controlled auth contract-error class. |
| `services/api.service.ts` | POST/PUT request generic parameters, defaulting to unknown; existing transport preserved. |
| `services/auth.service.ts` | Auth success guards, exact request projection, session-write guard, removal of expiresAt, base64url/expiry parsing and stored-user shape check. |
| `components/login/login.component.ts` | Safe normalized errors/validation text and nested successful navigation check. |
| `components/signup/signup.component.ts` | Same auth/error alignment; username maximum 100; existing login-after-registration flow. |
| `components/signup/signup.component.html` | Username maximum message updated to 100. |
| `components/word-lookup/word-lookup.component.ts` | Typed calls, explicit nullable mapping, shared error handling, acknowledgement guards, preserved favorite rollback/editor state, list failure state. |
| `components/word-lookup/word-lookup.component.html` | Bounded list-error/stale-data notice; no fabricated zero count or empty-list message after failure. |
| `components/quiz/quiz.component.ts` | Typed request generics, nullable result transport, shared errors and basic success-body guards; no scoring/session/retry changes. |

| Test file | Change |
|---|---|
| `services/api-error.spec.ts` (new) | Current/future envelopes, status/context precedence, validation sanitization, unsafe inputs and fallbacks. |
| `services/api.service.spec.ts` | Preserve Phase 1 cases; add explicitly typed POST/PUT request/response cases. |
| `services/auth.service.spec.ts` | Preserve Phase 1 cases; malformed success/session protection, payload projection, HTTP errors and JWT parsing. |
| `components/login/login.component.spec.ts` | Success navigation, invalid credentials, safe 500, validation and controlled contract failure. |
| `components/signup/signup.component.spec.ts` | 100/101 boundary and template, existing minimum, payload/navigation, current/future errors and malformed success. |
| `components/word-lookup/word-lookup.component.spec.ts` | Lookup/add/favorite/preferred/list/search errors, rollback, malformed acknowledgements and explicit nullable mapping. |
| `components/quiz/quiz.component.spec.ts` (new) | Minimal start/submit/history error groundwork, option zero, retained state and no automatic submit retry. |
| `components/dashboard/dashboard.component.spec.ts` | Existing User fixture gains required createdAt and lastLoginAt fields; assertions retained. |

Documentation added: `Docs/Updates/R7-api-contracts-phase-2-implementation-results.md`.

Total Phase 2 scope: **12 production UI files, 8 test files, 1 document**. Phase 1's shared fixture file remains unchanged and is reused. The Git diff against HEAD also includes uncommitted Phase 1 work; that must not be mistaken for new Phase 2 backend changes.

## 3. Authentication transport and session corrections

LoginResponse and RegisterResponse now share the real `data:{success,errorMessage,user,token}` payload. The compatible generic envelope permits nullable data/error/message and optional code/traceId without injecting fields into successful responses. User has required id:number, createdAt:string and lastLoginAt:string|null.

AuthService validates outer/nested success, null errorMessage, nonblank token and the required User fields before session writes. Missing/null/malformed payloads produce a controlled ApiContractError through RxJS map's error channel. Components show an operation fallback and do not navigate as authenticated. Registration is also checked but never establishes a session.

The `expiresAt` server assumption and unused setSession parameter were removed. Existing quiz `expiresAtUtc` is unrelated and retained. JWT expiry checking converts base64url to padded base64, parses header/payload safely, rejects missing/nonfinite/nonnumeric exp and malformed structures, and uses existing exp semantics. This is a UI expiry hint, not signature verification; server JWT configuration/validation is untouched.

Storage keys remain `vocab_app_token` and `vocab_app_user`. Malformed responses and HTTP failures do not clear or overwrite an existing session. Tests seed an existing unexpired session before malformed login attempts and assert unchanged storage/current user. User JSON loaded from storage must match the current wire shape; no migration or forced logout was added. Explicit existing logout behavior remains unchanged.

Login/register requests project only their declared fields; confirmPassword cannot leak into registration even if an extended object is passed. Username/password bytes are not trimmed or normalized.

## 4. Shared error normalization

`normalizeApiError(unknown, operation, safeFallback)` returns message and optional recognized code/fieldErrors. It supports HttpErrorResponse, structural HTTP fixtures and direct compatible body fixtures. It performs no I/O or session mutation.

- Status 0 produces a fixed connection message.
- HTTP 500 and other server failures produce `An internal error occurred. Please try again.` regardless of arbitrary body text, codes or validation arrays.
- Lookup 503 produces `Dictionary service is temporarily unavailable. Please try again.`
- Recognized stable codes take precedence for expected failures. Otherwise 401 context distinguishes login, password change and protected operations; lookup 404 differs from favorite/preferred-row 404.
- Compatible 4xx bodies accept nonempty error, message, then errorMessage. Text is bounded and obvious HTML/exception/stack/provider diagnostics are rejected. Unknown code values do not displace usable legacy text.
- Current and extended validation ProblemDetails use a safe summary. Only known field names and exact allowlisted annotation messages are forwarded; other messages become `Invalid value.` and unknown fields are omitted. Each field is limited to three messages. Arbitrary ProblemDetails title/detail and binding values are not reflected.
- Raw strings/HTML, exception objects, nested arbitrary objects and HttpErrorResponse.message are never used as display messages. Unrecognized bodies use the operation fallback. Malformed HTTP 200 handling never trusts its message text.

Login/signup render bounded field messages through existing text interpolation; no error innerHTML was introduced. Existing dictionary highlighting HTML is unrelated and unchanged.

The helper does not repair server misclassification: current internal errors incorrectly labeled 400/401 still require Phase 3. Client text screening is not a substitute for safe server error construction. Fixed HTTP 500/lookup 503 suppression is unconditional.

## 5. Transport typing and nullability

ApiService POST/PUT now accept `<TResponse, TRequest = unknown>`. All active production word/quiz mutation calls supply concrete request types; GET responses use current DTOs. Base URL, HTTP verbs/routes, Authorization construction and the HttpClient non-2xx channel are unchanged. No interceptor, retry or success-shaped error conversion was introduced.

Word transport DTOs retain actual required null keys: pronunciation/audioUrl/example/preferredWordDefinitionId/personalNotes/accuracyRate. Presentation models remain separate, with explicit null-to-undefined mapping. Zero counters/accuracy values are not lost through truthiness conversion. Lookup mapping no longer assumes backend synonyms/antonyms properties that do not exist. Current path/query encoding is retained in small shared helpers.

Quiz selectedAnswer is required string|null. Dates remain serialized strings; there is no global date conversion or claim of a uniform stored UTC suffix. Runtime guards are intentionally narrow and do not constitute a new schema-validation framework.

## 6. Login/signup behavior

Successful login still navigates to dashboard. Successful registration still navigates to login with the existing success message and does not log the user in. Signup accepts username lengths 3–100; validator/template agree. Email/password validation, matching confirmation and backend request exclusion remain.

Current duplicate-registration error text, future code-based errors, login 401, validation responses and safe server failures all pass through the shared normalizer. No global logout, redirect, refresh token or automatic retry was added.

## 7. Word/vocabulary groundwork

Lookup, add, list, autocomplete/search, saved-word detail lookup, favorite, preferred-definition save and editor-definition lookup now use the shared error helper.

New guards prevent success:false, missing mutation data, or mismatched/incomplete acknowledgement fields from marking a word saved, confirming a favorite, or closing the preferred editor. Favorite errors roll back the optimistic state. Preferred save failures retain editor selection and the original saved preference.

Failed list loads preserve previous data/selection, set vocabularyError and leave refresh required. The template displays the failure, labels stale data when present, suppresses empty-state claims and does not show zero total before a response exists. Existing toggling/paging can retry. Autocomplete failure displays an error while retaining the dictionary-search option. No collection-wide filtering, page-local wording redesign or backend query/selection behavior changed; those remain Phase 5.

## 8. Quiz groundwork

Start/submit/history failures use the shared normalizer. Submission failure leaves session and selected answers available for deliberate recovery; option ID zero remains explicit. History failure retains previous results. No auto-submit retry was added. Basic required success-data checks prevent accepting absent/malformed top-level quiz data.

The new minimal component spec covers legacy start error, future-shaped 500 submission error, and history error. Full request-presence/session-status/expiry/conflict/temporal coverage remains Phase 6. No backend session or scoring logic changed.

## 9. Test work and Phase 1 preservation

Static added-case count, expanding explicit loop inputs by source inspection only:

| File | New Phase 2 cases |
|---|---:|
| api-error.spec.ts | 32 |
| auth.service.spec.ts | 21 |
| api.service.spec.ts | 2 |
| login.component.spec.ts | 5 |
| signup.component.spec.ts | 7 |
| word-lookup.component.spec.ts | 21 |
| quiz.component.spec.ts | 3 |
| **Total** | **91** |

No discovery/execution result is implied. The dashboard fixture correction adds no case. Phase 1's 47 cases remain: backend files were untouched, Angular assertions retained, and its login current-user assertion was strengthened from serialized JSON comparison to direct typed equality because User dates now match the wire contract. No Phase 1 status expectation was changed or weakened.

Tests use local fixtures, HttpTestingController or service spies; localStorage is isolated. Existing successful Phase 1 backend-shaped fixtures are reused without adding a fictional expiresAt. Some older component-only presentation fixtures remain intentionally partial view models rather than HTTP DTOs.

## 10. Compatibility and resolved Phase 1 mismatches

New Angular accepts current nested success envelopes, legacy error/message/errorMessage bodies and current validation ProblemDetails. Static future fixtures cover error/message aliases, code, opaque traceId, data:null and extended validation responses. No new server field is required for successful operation.

Resolved mismatches: RegisterResponse is nested rather than User; login has no expiresAt dependency; user dates/ID/nullability reflect JSON; word transport nulls are explicit; quiz unanswered selectedAnswer is nullable; typed acknowledgements include current message/IDs/flags. The previously corrected Phase 1 fixtures remain accurate. This is source-level compatibility evidence, not proof of a deployed pairing.

## 11. Deferred work and discrepancies

No blocking dependency or production-backend change was needed. Phase 3 owns safe backend errors, service classification, compatible validation extensions and old 400/401 internal failures. Phase 4 owns provider structural validation. Phase 5 owns required favorite presence, full empty-search contract, vocabulary paging-scope wording and complete vocabulary boundaries. Phase 6 owns quiz required option presence/session status/expiry/full contract coverage. Phase 7 owns backend cleanup/OpenAPI/reference work; Phase 8 owns execution verification.

No full deep runtime DTO schema checker, new session persistence, backend normalization, database migration or package dependency was introduced. This phase makes no claim that future error formats already exist on the server or that later findings are fully remediated.

## 12. Execution status

**NO TESTS WERE RUN. NO BUILDS WERE RUN. NO RESTORES OR NPM INSTALLS WERE RUN.**

No TypeScript compiler, test discovery/host, package update, application startup, local/live HTTP request, WordsAPI/RapidAPI request, database/EF command, CI script, deployment script, or application/API/provider/database verification was executed. Test HTTP/storage actions shown in source are authored test code only.

Review consisted of source reads, searches and read-only Git status/diff. Compilation, Angular template checking and runtime results are unknown. No historic test result is reused as evidence.

## 13. Git state and final static review

Entry state included Phase 1's three modified specs, untracked backend tests/helper, shared UI fixtures and Phase 1 report, plus the pre-existing backlog/analysis/plan. Those were preserved. Phase 2 expanded the three specs as authorized; shared fixtures, Phase 1 report, backend tests, backlog and authoritative planning documents were not changed.

Read-only Git inspection used explicit `--git-dir=.git --work-tree=.` paths, as in Phase 1. No Git configuration or history was changed. No staging, commits, pushes, merges, rebases or deployments occurred.

Static scope review confirms all new production changes are under VocabularyApp.UI; no backend production/test, database/entity/context/migration/snapshot, package/dependency, configuration, CI/CD, release guard or deployment script changes were made in Phase 2. R3/R4/R5/R6, PSH-1, production serialization and stale-release protection remain untouched. R5 identity remains `(UserId, WordId)`.

**IMPLEMENTED — EXECUTION VERIFICATION PENDING.** Phase 2 implementation and documentation are ready for owner review. No pass/build claim is made. Work stops at Phase 2.

## Later verification — R7 Phase 8 (October 2, 2026)

The pending/no-execution statements above describe this phase at its original completion and remain historical evidence. The cumulative R7 implementation was subsequently execution-verified in Phase 8: final full backend suite **454 passed, 0 failed, 0 skipped**; final full Angular suite **193 passed, 0 failed, 0 skipped**; Release solution and Angular production builds passed. Phase 8 corrected anonymous Swagger serialization, stale owned-definition and SQLite timestamp assertions, and malformed XML documentation; see the complete failure history and warnings in [R7 Phase 8 verification results](R7-api-contracts-phase-8-verification-results.md).

Current cumulative status: **R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED**, within the local verification limits recorded there. Existing dependency vulnerabilities remain a separate release-review concern. No production access, live dictionary-provider calls, commit, push or deployment occurred. This later note does not claim execution took place during the original phase.
