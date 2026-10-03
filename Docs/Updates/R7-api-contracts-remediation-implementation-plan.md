# R7 — API Contracts Remediation Implementation Plan

Date: 2026-09-30

Status: **GO — READY FOR IMPLEMENTATION**. The project owner has approved D1–D4. The approved plan authorizes a separate future R7 implementation task; implementation has not begun. Deployment, release, production access, database access and live provider calls remain separately controlled and are not authorized by this GO.

## 1. Executive Summary

This plan converts the [R7 analysis](R7-api-contracts-remediation-analysis.md) into eight implementation phases covering all **16 findings** across **14 API endpoints**. Severity remains **0 Critical, 4 High, 10 Medium, 2 Low**. All 16 have an R7 disposition; **zero findings are deferred in their entirety**. Some larger remedies are explicitly deferred while their current contracts are documented and protected.

The approved strategy is **incremental error standardization**, preserving successful JSON structures, routes, authentication, and R5 identity. Use a named application error envelope retaining `error`, add a compatible `message` alias and stable error code, preserve ASP.NET validation ProblemDetails with compatible extensions, and leave framework Bearer challenges unchanged. Do not replace every response with ProblemDetails in R7.

The principal changes are safe exception handling, meaningful failure classification, structurally malformed dictionary responses returning 503, corrected Angular auth/nullable models and error extraction, explicit mutation-field presence, and exact contract tests. Backend/frontend changes must be coordinated; Angular compatibility work precedes server changes. No database migration or CI/CD change is expected.

All four decisions are **APPROVED**: the incremental error strategy; the status and request-presence changes; preservation of legacy/default behavior and bounded page-local vocabulary scope; and the coordinated frontend/backend compatibility policy. The project owner confirms that `VocabularyApp.UI` is the only maintained production API consumer, with no maintained external API or distributed SDK/binary DTO consumers. Manual/test examples are supporting development/test consumers only. Section 30 records the approved choices and consequences. All 16 finding dispositions and eight phases remain unchanged; implementation is ready to begin under a separate implementation task.

**Documentation only:** this approval-record update changes only this implementation plan. No production code or test code changed. No tests, builds, restores, application execution, application verification, migrations, database queries, provider requests, production requests, or CI/deployment scripts were executed. Phase 1 has not begun.

## 2. Objective

Define a bounded, reversible R7 implementation that a later task can follow under the approved decisions in section 30. Establish exact intended behavior, change locations, dependencies, tests and completion criteria. Preserve existing security, credential migration, quiz persistence/counters, R5 saved-word identity, and deployment hardening.

Implementation and release remain separate future actions. Proposed files and tests below do not yet exist unless identified as existing. Approval resolves the earlier decision-based NO-GO; it does not expand technical scope or authorize implementation during this documentation update.

## 3. Inputs and Repository State

During original plan preparation, the complete R7 analysis was read, followed by source and test inspection. Its source baseline is commit `5d63e068fbb8c62c8b24ece8d671c82f904f9a74`; this plan relies on that inspection rather than assuming that historical hash is a deployed release. At that planning task's entry, the future-feature backlog and R7 analysis were untracked existing documentation. Neither is changed by this approval-record update. The project owner's subsequent D1–D4 approvals are recorded in section 30. No new discrepancy was discovered during this document inspection; no fresh application verification is claimed.

No remediation of F01–F16 was found in the inspected active sources. There are important refinements to the analysis, not claims that implementation has changed:

- `word-lookup.component.html` already contains Next/Previous controls and page numbering. F10 is a page-local search/count interpretation and error-state problem; later pages are reachable.
- `UserService.LoginAsync` deliberately tolerates a failed last-login timestamp save when no password replacement is required. Preserve that successful-login path. Mandatory credential replacement failure must still return no token, with a corrected server-error classification.
- `ApiTestClientHelper` references the controller-local `ApiResult<T>` directly. Moving that type requires updating this test helper as well as controller code.
- The coverage artifact mentioned at the beginning of the analysis is not in the planning task's entry status. No historical test result is treated as verification of this plan.

### 3.1 Source and test aliases used in the matrices

Paths below are authoritative. Aliases keep the disposition matrix readable.

| Alias | Files |
|---|---|
| UC / WC / QC | [UsersController.cs](../../VocabularyApp.WebApi/Controllers/UsersController.cs), [WordsController.cs](../../VocabularyApp.WebApi/Controllers/WordsController.cs), [QuizController.cs](../../VocabularyApp.WebApi/Controllers/QuizController.cs) |
| US / WS / QS | [UserService.cs](../../VocabularyApp.WebApi/Services/UserService.cs), [WordService.cs](../../VocabularyApp.WebApi/Services/WordService.cs), [QuizService.cs](../../VocabularyApp.WebApi/Services/QuizService.cs), and their `IUserService`, `IWordService`, `IQuizService` interfaces in the same Services directory |
| SR | [Models/ServiceResult.cs](../../VocabularyApp.WebApi/Models/ServiceResult.cs) |
| UD / WD / VD / QD | [UserDTOs.cs](../../VocabularyApp.WebApi/DTOs/UserDTOs.cs), [WordDTOs.cs](../../VocabularyApp.WebApi/DTOs/WordDTOs.cs), [UserVocabularyDTOs.cs](../../VocabularyApp.WebApi/DTOs/UserVocabularyDTOs.cs), [QuizDTOs.cs](../../VocabularyApp.WebApi/DTOs/QuizDTOs.cs) |
| AD / PD | [Models/AddWordRequest.cs](../../VocabularyApp.WebApi/Models/AddWordRequest.cs), [DTOs/External/WordsApiDtos.cs](../../VocabularyApp.WebApi/DTOs/External/WordsApiDtos.cs) |
| BOOT | [Program.cs](../../VocabularyApp.WebApi/Program.cs) |
| AU / API | [auth.service.ts](../../VocabularyApp.UI/src/app/services/auth.service.ts), [api.service.ts](../../VocabularyApp.UI/src/app/services/api.service.ts) |
| UM / WM / QM | [user.model.ts](../../VocabularyApp.UI/src/app/models/user.model.ts), [word-lookup.model.ts](../../VocabularyApp.UI/src/app/models/word-lookup.model.ts), [quiz.model.ts](../../VocabularyApp.UI/src/app/models/quiz.model.ts) |
| LOGIN / SIGNUP / WORD / QUIZ | Respective `login`, `signup`, `word-lookup`, `quiz` component `.ts`/`.html` files under `VocabularyApp.UI/src/app/components/` |
| TA / TD / TV / TQ | Existing `AuthenticationApiTests.cs`, `DictionaryLookupApiTests.cs`, `VocabularyOwnershipApiTests.cs`, `QuizApiTests.cs` under `VocabularyApp.WebApi.Tests/Integration/` |
| TR / TH | Existing `R3SecurityContractApiTests.cs`, `HttpsHardeningApiTests.cs` in the same Integration directory |
| TS | Existing `UserServiceAuthenticationTests.cs`, `LoginMigrationTests.cs`, `CredentialConcurrencyTests.cs`, `AuthenticationLoggingTests.cs` under `VocabularyApp.WebApi.Tests/Services/` |
| TF | Existing [VocabularyAppWebApplicationFactory.cs](../../VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs), `ApiTestClientHelper.cs`, `ControllableDictionaryHandler.cs`, and fault/concurrency interceptors in the same Infrastructure directory |
| UA / UP / UL / USU / UW | Existing Angular `auth.service.spec.ts`, `api.service.spec.ts`, `login.component.spec.ts`, `signup.component.spec.ts`, `word-lookup.component.spec.ts`, adjacent to their production files |
| DOC | `Docs/README.md`, root `test-api.http`, `VocabularyApp.WebApi/VocabularyApp.WebApi.http`; analysis retained as historical evidence |

### 3.2 Stable endpoint identifiers

All paths are prefixed with `/api`. No routes are added, removed or renamed.

| ID | Endpoint | Authentication / principal consumer |
|---|---|---|
| E01 | POST `/users/register` | Anonymous; AU/SIGNUP |
| E02 | POST `/users/login` | Anonymous; AU/LOGIN |
| E03 | GET `/users/profile` | Bearer; no direct Angular caller; deployment challenge check |
| E04 | POST `/users/change-password` | Bearer; no direct Angular caller |
| E05 | GET `/users/validate-token` | Bearer; no direct Angular caller |
| E06 | GET `/words/lookup/{word}` | Bearer via fallback policy; WORD lookup/editor |
| E07 | POST `/words/vocabulary/add` | Bearer; WORD |
| E08 | GET `/words/vocabulary` | Bearer; WORD |
| E09 | GET `/words/vocabulary/search` | Bearer; WORD suggestions/saved-word lookup |
| E10 | PUT `/words/vocabulary/{userWordId:int}/favorite` | Bearer; WORD |
| E11 | PUT `/words/vocabulary/{userWordId:int}/preferred-definition` | Bearer; WORD |
| E12 | POST `/quiz/start` | Bearer; QUIZ |
| E13 | POST `/quiz/submit` | Bearer; QUIZ |
| E14 | GET `/quiz/history` | Bearer; QUIZ |

## 4. R7 Findings Summary

Source inspection still shows the unsafe registration `ex.Message`, service failure flattening, unchecked provider collections, Angular `message`/`error` drift, nonexistent login `expiresAt`, default-valued mutation fields, unused DTOs, and gaps in raw JSON/frontend request tests. The full disposition matrix below covers F01–F16, including DTO and endpoint dependencies.

Accounting by primary disposition: **11 Fix in R7** (F01–F11), **3 Test/protect in R7** (F12–F14), **2 Documentation/legacy cleanup in R7** (F15–F16). F08 includes intentional preservation of legacy selection/normalization. F10 is a small scope-label/error-state fix, not server-wide search redesign. F14 documents/tests existing session behavior; durable quiz sessions remain deferred. These limited dispositions do not claim that the broader follow-up problems have been eliminated.

## 5. Finding Disposition Matrix

The matrix is split into evidence and execution tables, joined by finding ID. Together each row identifies severity, endpoints, backend/frontend/DTO files, current test evidence, disposition, remediation, compatibility, phase, verification and follow-up. `Bxx` and `Uxx` refer to the future test groups in sections 17–18. All verification entries are **future work**, not commands executed now.

### 5.1 Current evidence and affected contracts

| Finding | Severity / description and source confirmation | Endpoints | Backend / DTOs | Frontend | Existing test files |
|---|---|---|---|---|---|
| F01 | High: US registration catch still returns ex.Message | E01 | US.CreateUserAsync, UC.Register, UD.AuthResponse | AU/SIGNUP | TA, TS logging/authentication |
| F02 | High: caught internal failures become Validation/false; controllers send 400/401 | E01–E14 failure paths; E03/E05 already catch to 500 | SR, US/WS/QS and interfaces, UC/WC/QC | AU/API, LOGIN/SIGNUP/WORD/QUIZ | TA/TV/TQ/TS; TF fault interceptors |
| F03 | High: explicit null provider collections/elements remain unchecked | E06 | PD, WS.LookupWordAsync, WC.LookupWord | WORD | TD; TF dictionary handler |
| F04 | High: auth/add error handlers miss server error field | E01/E02/E07 directly; all consumers need consistent parsing | UC/WC/QC, existing wrappers | UM, LOGIN/SIGNUP/WORD/QUIZ | UA/UL/USU creation-only; UW limited request fixtures |
| F05 | Medium: registration data wrongly User; login expiresAt absent on server; Date mismatch | E01/E02; UserDto also E03/E05 | UD, UC, JwtHelper unchanged | UM/AU | TA/UA/UL/USU |
| F06 | Medium: anonymous/object payloads and wrapper duplication; sparse schemas/global Swagger security | E01–E14 | UC local ApiResult<T>, DTOs/ApiResult.cs, commented ApiResponse.cs, SR, WS/interface, WC/QC, BOOT | API/UM | TF helper directly uses ApiResult<T>; TA/TD/TV/TQ/TR |
| F07 | Medium: missing bool becomes false; missing option ID becomes zero | E10/E13; defaults in E08/E09/E12/E14 | VD.UpdateFavoriteRequestDto, QD.QuizAnswerSubmissionDto, WC/QC/QS | WORD/QUIZ already submit fields | TV/TQ cover related behavior, not omission matrix |
| F08 | Medium: ignored add fields, differing trim/POS defaults and username bound | E07/E06/E01 | AD, WS selection, UD.CreateUserRequest | WORD; SIGNUP .ts/.html | TV/TA/UW; no boundary/POS-fallback coverage |
| F09 | Medium: empty-search anonymous shape differs and binding may reject first | E09 | WC.SearchUserVocabulary, WS, VD.UserVocabularyResponseDto | WORD | TV search isolation; UW empty mock |
| F10 | Medium: local catalog/filter/counts use one page; errors turn into empty data | E08 | WC/WS/VD paging unchanged | WORD .ts/.html, WM; Next/Previous already implemented | TV filtering; UW local filter tests |
| F11 | Medium: any/casts hide wire drift and nullability | E01/E02/E06–E14 | UD/WD/VD/QD, AD, named acknowledgements needed | API, UM/WM/QM, WORD/QUIZ | UP creation-only; UW limited mocks; no quiz spec |
| F12 | Medium: behavior assertions lack exact JSON/status/nullable protection | E01–E14 | Controllers/DTOs and TF | No direct production change | TA/TD/TV/TQ/TR/TH, TS |
| F13 | Medium: missing frontend auth/API/quiz request and error contracts | E01/E02/E06–E14 | No extra backend redesign | All listed Angular consumers | UA/UP/UL/USU/UW; quiz spec absent |
| F14 | Medium: 30-minute static sessions, retry/expiry/date semantics unclear | E12–E14 | QS, QD | QM/QUIZ | TQ strong counters/rollback; expiry/raw-null gaps |
| F15 | Low: dormant DTOs and unused throwing overload remain | No bound route for dormant types | UserWordDTOs.cs, WordRequest.cs, WD.WordLookupRequest, ApiResponse.cs, ApiResult.cs | No reference to these C# classes | No direct dormant DTO tests; TF references active generic wrapper |
| F16 | Low: old provider, routes and anonymous lookup in examples | E01–E14 documentation; stale absent routes | DOC | No active stale Angular route found | TR inventory; no automated example coverage |

### 5.2 Disposition and execution

| Finding | R7 disposition and explicit remediation | Dependencies / phase | Breaking-change risk | Required future verification | Deferred follow-up |
|---|---|---|---|---|---|
| F01 | Fix: safe generic server message; InternalError classification; never publish ex.Message | Approved error policy; P3 | Error text/400→500 | B02/B03; TS secrecy assertions | None for unsafe response |
| F02 | Fix: typed failures and endpoint mapping in section 7, preserving tolerated timestamp-save behavior | P1/P2 then P3; quiz details P6 | 400/401→404/409/500; coordinated Angular/backend change approved | B02–B08, B12; existing rollback/concurrency assertions retained | Broad exception middleware/service rewrite |
| F03 | Fix: validate provider transport structure before tracking records; 503 for unusable payload | Typed failure map; P4 | Incorrect 400→503 | B05 and U04, no live provider | Provider extraction/cache race recovery |
| F04 | Fix: shared unknown-safe Angular error adapter and aliases on server | P2 before P3 | Additive shape; UI-only parsing compatible | U01/U02/U04/U05 plus B01 | All-error ProblemDetails migration |
| F05 | Fix: frontend models match existing nested AuthResponse; remove expiresAt assumption | P2; typed US results P3 | Internal TS changes; no success wire changes | B02/U02/U03 | Refresh/expiry response redesign |
| F06 | Fix: named typed payloads and accurate schema/auth annotations; preserve successful wire forms | P3/P5, metadata P7 | Non-breaking if property presence preserved; errors additive | B01/B09/B11 | One universal success shape/client generation |
| F07 | Fix: require JSON presence of isFavorite/selectedOptionId; explicit null element validation; preserve other defaults | Decision D2; P5/P6 | Newly rejected incomplete requests | B06/B07/U04/U05 | General validation policy overhaul |
| F08 | Fix supported alignment; preserve intentionally: frontend username max 100; document ignored fields, exact add text/POS fallback and repeated-add precedence | D3; P2/P5/P7 | UI accepts more backend-valid names; legacy wire unchanged | B02/B06/U03/U04 | New POS-selection rule and word/account normalization |
| F09 | Fix: nullable optional query; full typed empty envelope for absent/empty/whitespace term | D2; P5 | Some 400→200 cases; additive metadata | B06/U04 | Search architecture redesign |
| F10 | Fix narrowly: explicitly label current-page search/counts; retain existing paging; separate failed load from empty result | D3; P5 | UI clarification; no route/query change | B06/U04 with >1000 rows/two pages | Collection-wide server filtering/letter counts |
| F11 | Fix: transport DTOs, typed request generics and mapping; nullable wire types; named acknowledgements | P2/P5/P6 | Compile-time changes only when JSON preserved | B01/U01–U05 | General UI decomposition/runtime schema library |
| F12 | Test/protect: R6 raw JSON/status tests for 14 actions and fault boundaries | P1 throughout P8 | Tests intentionally update approved obsolete statuses | B01–B12 | SQL Server-specific test project |
| F13 | Test/protect: exact HTTP route/verb/body/header, fixtures, errors and quiz specs | P1/P2/P5/P6/P8 | No wire impact | U01–U06 | Broad UI/E2E suite |
| F14 | Test/protect/document: current temporal/scoring guarantees; approved session status map; no persistent session rewrite | P6/P7 | Session error statuses change; success dates retained | B07/B08/U05 | Durable sessions, guaranteed retry results, timestamp migration |
| F15 | Cleanup: remove verified unreferenced declarations and unused overload; relocate active generic type only with references updated | P7 after typing | No HTTP impact; owner confirms no distributed SDK/binary DTO consumers | B01/B11, future build/reference review | Entity placeholders/database cleanup |
| F16 | Documentation cleanup: current 14 routes/provider/auth and contracts in reference/examples | P7 | None; historical analysis/roadmap not rewritten | B11 and future manual source review | None necessary |

## 6. API Contract Principles

1. Preserve all 14 route/method pairs and successful 200 responses. Do not change to 201 or 204 merely for consistency.
2. Preserve existing success property names and presence: auth/profile `success,data,error`; password-change `success,message,data,error`; word/quiz `success,data`. Preserve nested auth/lookup flags, GUID strings, numeric IDs, counters and acknowledgement fields.
3. HTTP status identifies failure class; stable error codes refine it. Never send successful HTTP status for application failure. No exception-driven string matching for classification.
4. Anonymous registration/login and twelve protected routes remain unchanged. A framework Bearer challenge stays a 401 challenge, not a JSON redirect or login-page response.
5. Public errors contain only approved messages/codes and opaque correlation IDs. No exception, SQL, provider response body, credential, connection or infrastructure information is copied out.
6. `(UserId,WordId)` remains the unique saved-word identity. POS describes selected meaning/derived state, never a key. Canonical data cannot be authored through personal-vocabulary requests.
7. Provider 404 is a genuine dictionary miss; upstream authentication/rate-limit/network/timeout/unusable-data failures are application 503. Internal persistence failure is 500, not automatically 503.
8. Correct TypeScript to the backend wire format first. UI view models may still differ through explicit mapping. JSON date strings are not JavaScript Date instances.
9. Favor additive compatibility changes. Preserve default/clamping/legacy semantics explicitly where change is not essential. Distinguish omitted fields from null and zero where they express mutation intent.
10. No schema, migration, JWT infrastructure, middleware-order, hosting or CI redesign is required.

## 7. HTTP Status Strategy

This table is the approved target under D2; its status mappings are unchanged by the approval record. `AE` is the application error shape in section 8; `VE` is validation ProblemDetails with compatible extensions; `BC` is the unchanged framework Bearer challenge. Business error text is allowlisted. All action catches for unexpected exceptions produce 500 AE.

| Endpoint / condition | Current → target | Backend change | Angular impact / future test |
|---|---|---|---|
| E01 valid registration / existing username or email | 200 / 400 → unchanged | Preserve duplicate prechecks and response; codes username_taken/email_taken | Surface useful error; B02/U03 |
| E01 unexpected query/hash/save/token-generation exception | 400 with exception text → 500 AE internal_error | US returns explicit internal failure; UC maps it | Generic safe message; B03 |
| E02 invalid credentials or malformed stored credential | 401 → unchanged AE invalid_credentials | Preserve identical message for absent user/bad password | No account-existence disclosure; B02/U02 |
| E02 required replacement unavailable or save fails; other unexpected failure | 401 → 500 AE internal_error | Explicit failure from US; no token | Do not treat as wrong password; B03/TS |
| E02 concurrent credential change | 401 → 409 AE credentials_changed | Classify existing concurrency branch; no token | Ask user to retry login manually; B03 |
| E02 ordinary last-login save failure without required credential update | 200 → unchanged | Preserve intentional catch-and-log success; never weaken mandatory migration | B03/TS preserve authentication semantics |
| E03 missing authenticated account | 404 → unchanged AE user_not_found | Typed error only | No Angular caller; B04 |
| E04 wrong current password / missing account / bad claim | 401 → unchanged AE | Classify absent account separately internally; public missing/invalid-credential explanation stays safe | No direct Angular caller; B03/B04 |
| E04 credential concurrency / unexpected error | 401 → 409 credentials_changed / 500 internal_error | Replace bool-only service outcome with typed result | No false incorrect-password message; B03 |
| E05 valid account / missing account | 200 / 401 → unchanged | Use existing controller user lookup; typed error, no new endpoint call from Angular | B04 |
| E06 invalid word / provider 404 / known provider failure | 400 / 404 / 503 → unchanged | Preserve classification; codes invalid_request/word_not_found/dictionary_unavailable | Keep lookup-specific missing-word message; B05/U04 |
| E06 explicit-null/unusable provider structure | Often 400 → 503 AE dictionary_unavailable | Provider validation before mapping/tracking | Temporary provider failure, not spelling failure; B05 |
| E06 local DB/save/unexpected internal mapping failure | 400 → 500 AE internal_error | Outer catch is InternalError; not every exception is provider failure | Generic retry-later message; B05 |
| E07 valid new/repeated add | 200 → unchanged | Keep stable IDs and alreadyExisted; no 409 for ordinary duplicate add | Preserve R5 UI/tests; B06/U04 |
| E07 absent canonical word / invalid preferred definition | 400 → unchanged AE canonical_word_required/invalid_preferred_definition | Selection precondition is invalid request, not a by-ID resource GET miss | B06 |
| E07 unexpected persistence error, including unrecovered cache/identity race | 400 → 500 AE internal_error | Preserve recognized duplicate winner path; other failures are not duplicate success | TV fault assertion changes to 500; B06 |
| E08/E09 query binding / service exception | 400 VE unchanged / 400→500 AE | Classify list/search catches; no arbitrary new query bounds | B06 |
| E09 absent/empty/whitespace term | Binding-dependent 400 or shortened 200 → full typed 200 empty result | Optional nullable query and typed empty response | Existing consumer can read words; B06/U04 |
| E10/E11 missing or other user's saved row | 400 → 404 AE vocabulary_not_found | Same status/code/message for nonexistent and nonowned ID; scope by owner first | Generic unavailable-entry explanation; B06 |
| E10 missing isFavorite / E11 invalid definition selection | Default mutation →400 VE / 400 unchanged AE | Required presence for bool; positive/same-word definition rule remains | Current UI sends valid fields; B06 |
| E10/E11 internal read/save failure | 400 → 500 AE | InternalError classification | Restore optimistic favorite, keep editor open on failure; B06/U04 |
| E12 insufficient usable vocabulary / invalid body | 400 → unchanged AE quiz_unavailable / VE | Preserve four-word threshold, counts/modes | B07/U05 |
| E12/E13/E14 unexpected DB/service failure | 400 → 500 AE internal_error | Explicit failure; submission rollback/release remains | User may retry only under documented semantics; B07/B08 |
| E13 empty GUID / invalid question, option, duplicate question, null element | 400 → unchanged or corrected 400 AE/VE | Validate intent and null elements before LINQ; no answer leakage | B07 |
| E13 unknown, expired, removed-after-success or nonowned session | 400 → 404 AE quiz_session_unavailable | One public unavailable message; do not expose owner/existence | Start a new quiz only through user action; B07/U05 |
| E13 known submission already in progress / persisted duplicate caught during submission | 400 → 409 AE quiz_submission_conflict | Classify existing lock/unique-constraint branches | No automatic submit retry; B07 |
| E13 vocabulary removed since start | 400 →409 AE quiz_vocabulary_changed | Session is valid but required state changed | Ask user to start another quiz; preserve no mutation; B07 |

No new application 403 is introduced: nonowned vocabulary/session references deliberately use concealed 404. 403 remains appropriate for a future explicit policy denial, but no such role policy exists. Existing IIS insecure-transport 403 is untouched. Unknown resource route 404/405 and unsupported-media 415 remain framework/routing behavior, not remapped as dictionary-not-found.

A repeat after successful submit is generally **404**, because the process-local session has been removed; 409 is only for a conflict the service actually detects. Do not add a database query/tombstone system merely to force all repeats to 409. A concurrent loser may observe 409 or 404 depending on whether removal happened first; state/counter tests must still prove only one successful mutation.

## 8. Error Contract Strategy

### 8.1 Approved strategy: incremental compatibility, not universal replacement

The C# `ApiResponse<T>` file is commented out. Active errors are anonymous objects, generic/non-generic ApiResult, automatic validation ProblemDetails and Bearer challenges; plain strings are not normal action error responses. Replace **application-generated action errors** with a named `ApiErrorResponse`, preserving old fields needed by clients. Do not replace successful payloads or every framework error.

Proposed `application/json` AE shape for all application errors in E01–E14:

```json
{
  "success": false,
  "data": null,
  "error": "An internal error occurred. Please try again.",
  "message": "An internal error occurred. Please try again.",
  "code": "internal_error",
  "traceId": "opaque-server-request-identifier"
}
```

All six keys are present; error and message contain the same safe string. `message` is a compatibility alias for current auth/add handlers, not an independently meaningful field. `data` is always null on an error. Use server-generated `HttpContext.TraceIdentifier`; never an arbitrary echoed header. No errorMessage alias is added; Angular accepts it only for old payload compatibility. Do not change successful data.message values.

### 8.2 Validation and framework exceptions to uniformity

Keep validation responses as `application/problem+json` with their standard `type,title,status,errors,traceId` fields. Configure a narrow `InvalidModelStateResponseFactory` to add `success:false`, `data:null`, `error`, `message`, `code:"validation_failed"`. Use a safe summary such as `One or more request fields are invalid.` Keep safe per-field annotation messages; substitute `Invalid value.` for binding/conversion exceptions rather than exposing CLR type names, attempted values or exception messages. Keep field keys and nonempty string-array values. Do not serialize ModelState exceptions.

Use the same factory for normal automatic validation; remove/repoint unreachable manual ModelState envelope branches in UC so they cannot evolve separately. Leave unsupported-media/routing ProblemDetails behavior intact and document/test it. Leave middleware 401/403 challenge/forbid bodies and headers intact. Application-level invalid claim 401 may use AE, but must not replace the framework challenge path.

This deliberately leaves documented AE/VE/BC variants; it is not a claim that all HTTP failures have one body. A future all-ProblemDetails migration would need separate compatibility work. The analysis proposed ProblemDetails as one direction; this plan recommends the narrower compatible envelope because old Angular already reads error or message and successful wrappers need no redesign.

### 8.3 Implementation mechanics and codes

Proposed new files under `VocabularyApp.WebApi`: `DTOs/ApiErrorResponse.cs`, `Helpers/ApiErrorResults.cs`, and `Helpers/ApiValidationResponses.cs`. The helper maps an explicit service failure/code to status and the safe body; it does not inspect message text. Codes are the ones in section 7, plus `invalid_token`, `current_password_incorrect`, `user_unavailable`, `invalid_quiz_answers` and `validation_failed`. Codes are stable; safe display text can improve without changing status/meaning. Use the same unavailable code/message for absent and nonowned records.

Extend SR with explicit `InternalError`, `Unauthorized`, and `Conflict` failure kinds in addition to existing Validation, NotFound and ServiceUnavailable; add an application error code field. Failure.None or an unknown kind must fail safely as 500, never silently as 400. Expected failures provide allowlisted messages; InternalError always uses the fixed public message regardless of any internal message.

Introduce typed auth service outcomes rather than adding internal failure properties to serialized AuthResponse: `Task<ServiceResult<AuthResponse>>` for create/login and `Task<ServiceResult<bool>>` for password change (success data true). Leave UserDto lookup methods unchanged. Controller success unwrapping preserves the existing AuthResponse JSON. Update TS and TF service consumers without weakening their assertions.

Required tests assert both exact AE keys and safe values, standard-plus-extension VE shape, content types, opaque trace ID, and empty Bearer challenge behavior. JSON property order and a literal trace ID are not contracts. D4 resolves consumer ownership: Angular is the only maintained production consumer. Retain compatibility aliases and old/new-client fixtures for transition and rollback; no external-client versioning or deprecation program is required for R7.

## 9. Exception Safety Remediation

Prefer **service classification plus existing controller catches**, backed by shared response construction. A new global exception middleware is unnecessary to fix the identified paths and could interfere with HSTS/auth/fallback behavior. Do not reorder middleware or broaden this into exception-pipeline architecture work.

- **US.CreateUserAsync:** remove public ex.Message. Log exception in protected server logs with operation/user identifier where safe; return InternalError/internal_error. Token-generation failure after a successful user save is still 500; R7 does not promise transactionally undoing an already-created account. Tests must distinguish pre-save faults (no user) from post-save faults (possibly persisted user, no token in error).
- **US.LoginAsync:** missing replacement and mandatory credential-save faults are InternalError; stale credential concurrency is Conflict. Preserve ordinary last-login timestamp-save tolerance and mandatory-migration no-token behavior. Do not change password verification/hash generation.
- **US.ChangePasswordAsync:** replace indistinguishable false outcomes with Unauthorized for wrong password/missing account, Conflict for stale credentials, InternalError for unexpected failures. No token revocation or password policy changes.
- **WS:** every outer unexpected catch returns InternalError. Keep only the provider transport/schema branch as ServiceUnavailable. Keep known saved-word uniqueness handling returning the winner; do not generalize all DbUpdateException into duplicate success.
- **QS:** unexpected start/submit/history catches become InternalError. Keep transaction rollback, change-tracker clearing, session lock release and protected duplicate branch; only classification changes.
- **UC/WC/QC:** existing unexpected catches use shared 500 AE. Successful service result with missing required payload is an internal invariant failure (500), not a fabricated success. Remove add's `{message}` null-data success fallback.

Public internal message: `An internal error occurred. Please try again.` Provider message: `Dictionary service is temporarily unavailable. Please try again.` Expected invalid-credential message remains `Invalid username or password`. No automatic retry is implied by message text.

Logs retain exception type/stack and correlation for operators through existing ILogger, but never deliberately log request bodies, passwords, password hashes, bearer tokens, provider keys, connection strings or raw upstream bodies. Preserve AuthenticationLoggingTests and add sentinel leak assertions on public JSON. A test logger may be enabled only inside a specific factory fixture; do not enable sensitive EF logging.

## 10. Provider Failure Contract

Use the existing PD/WS transport boundary and `ControllableDictionaryHandler`. Do not add a new provider architecture or call RapidAPI.

| Provider/input case | Target E06 outcome / persistence |
|---|---|
| Local canonical hit | 200 current WordLookupResponse; zero provider calls |
| Invalid client word reaching action | 400 AE invalid_request; no provider/write |
| Provider HTTP 404 | 404 AE word_not_found; no record |
| Provider 401/403/429/5xx or other unsuccessful non-404 status | 503 AE dictionary_unavailable; never forwarded as client authentication/rate limit |
| HttpRequestException, TaskCanceledException/timeout | 503; no record |
| Invalid JSON, unsupported JSON type/root null, missing/blank canonical word | 503; no record |
| Results missing/empty/null, null result element, no usable definitions | 503; no record |
| Pronunciation or examples explicitly null, invalid structure/type | 503; no record; this is deliberate structural validation, not a requirement that optional audio exist |
| Optional pronunciation/examples omitted or empty | Allowed, preserving current initialized-empty behavior; output pronunciation/example may be null |
| Unknown POS only | 503; never invent Noun |
| Mix of supported/unsupported POS with valid structure and at least one usable definition | 200 mapped supported definitions; unsupported entries skipped |
| Valid provider data but local database fails | 500 AE internal_error; no claim that provider is unavailable |

Validate the complete transport structure before `_db.Words.Add` or adding definitions. Require correct non-null collection structure when explicitly supplied; distinguish malformed shape from unusable definition content. Skip blank definitions as today, but reject structurally null result elements. Empty/blank example strings can be ignored as today. Do not reject a missing audio URL: provider mapping intentionally sets AudioUrl null, and browser speech handles pronunciation.

No provider DTO, status body, header or credential reaches public JSON. Preserve local IDs, application field names and cache/member flags. DB constraints/transaction effects still require relational tests; a 503 contract is not a license to swallow programming/persistence errors as provider faults.

## 11. Authentication Contract Alignment

| Contract | Authoritative behavior / planned side to change |
|---|---|
| Register request | Backend username 3–100, email format/max 200, password 6–100 remains. Frontend validator **and signup template message** change 50→100. ConfirmPassword remains UI-only and excluded from payload. |
| Login request | Required username/password unchanged. Preserve case-insensitive comparison and existing untrimmed text; do not normalize password or accounts in R7. |
| Registration response | Backend nested `data:{success,errorMessage,user,token}` remains. Frontend RegisterResponse aligns with it. Signup still navigates to login, without automatically starting a session. |
| Login response | Same nested shape retained. Remove frontend-required `expiresAt` and unused setSession parameter. Token/user checked before writing localStorage or navigating as authenticated. |
| JWT | Existing claims/signing/issuer/audience/expiration/lifetime remain. No new expiry field or refresh token. UI derives expiration from JWT exp as today; parsing tests include invalid and base64url payloads. Any necessary decoder correction is client-only, with no auth policy change. |
| Profile/validate-token | Existing UserDto success shape. Profile missing account 404; validate-token missing account 401. Do not add unsolicited calls to these endpoints in AuthService. |
| Password change | Wire request/success remains; service result becomes typed to distinguish failures. Wrong password remains 401, concurrency 409, unexpected failure 500. |
| Session storage | Keep `vocab_app_token` / `vocab_app_user`, fields and logout behavior. Existing serialized date strings remain strings. No forced logout/data migration on deployment. |

Use a shared `AuthResponseData` TypeScript interface and aliases for LoginResponse/RegisterResponse rather than incompatible duplicated data types. Preserve nested success/errorMessage for compatibility even if redundant. Neither auth payload nor a 500 response should contain credential hashes. Failed/malformed success payloads must not write `undefined` into storage; report a controlled client error and leave the session unchanged.

## 12. Angular Error Handling

Add a pure helper at `VocabularyApp.UI/src/app/services/api-error.ts`, accepting `unknown`, an operation context and safe fallback. Return a normalized object `{message, code?, fieldErrors?}`. No global interceptor, router redirect, automatic logout or automatic retry is introduced.

Extraction order:

1. Inspect HttpErrorResponse status first. Status 0 gets a safe connectivity message; do not show browser transport internals. 500 uses the fixed generic message regardless of body. 503 lookup uses the fixed dictionary-unavailable message.
2. For expected 4xx, prefer recognized code-to-message mapping. Accept the known API body's nonempty string `error`, then `message`, then legacy `errorMessage`, then safe validation/ProblemDetails summary. Do not blindly stringify objects, HTML bodies, Exception objects or HttpErrorResponse.message.
3. Validation `errors` string arrays may be displayed as plain text for known fields; trim/bound their display and never use innerHTML. If the body is not a recognized API/ProblemDetails object, use the operation fallback. No raw standalone string/HTML response is trusted as an API error.
4. For an empty 401, use context: login credentials failure versus protected-operation sign-in required. Do not globally log the user out on a password-change 401. For lookup-only 404 use word-not-found; a 404 from a saved-row update is unavailable vocabulary, not dictionary miss.
5. Retain HttpClient's error channel for non-2xx. Do not convert errors into success-shaped observables. 200 `success:false`, missing data, or invalid required acknowledgement fields are controlled client contract errors, not successful mutations.

Use this helper in LOGIN/SIGNUP and WORD lookup, add, list/search, favorite and preferred-definition operations, plus QUIZ start/submit/history. Keep optimistic favorite rollback. Preferred editor stays open on failure. A list fetch failure must have a visible error state rather than presenting a fabricated zero-word collection. Preserve current stale data if any, label the failure, and allow existing navigation/reload actions to retry; no new page design is required.

Compatibility: new Angular works with old server `error`, password-change `message`, and VE responses; old Angular still sees `error` and gains `message` on new AE responses. D4 confirms no maintained external consumers; aliases remain required for Angular transition and rollback compatibility. Do not display arbitrary server 500 text even while old backend code is temporarily deployed.

## 13. Nullability and Type Alignment

Backend JSON is authoritative. No global omit-null setting or database nullability change. Use separate transport interfaces where UI view models intentionally allow omitted presentation fields.

| Backend property/type | Current TS mismatch | Intended TS transport contract / change | Compatibility / test |
|---|---|---|---|
| UserDto.Id, CreatedAt | User.id optional; createdAt optional Date | id:number; createdAt:string, required on API User DTO | Frontend only; B02/B04/U02 exact fields |
| UserDto.LastLoginAt | optional Date excludes null | lastLoginAt:string\|null, required key | Frontend; null registration date fixture |
| AuthResponse.User / Token / ErrorMessage | registration data is User; login assumes nonnull user/token and expiresAt | user:UserDto\|null; token:string\|null; errorMessage:string\|null; success:boolean; valid success requires nonnull user/token | Frontend guard before storage; B02/U02 |
| Generic envelope Data/Error | data?:T and no error | shared permissive wire envelope `data?:T\|null`, `error?:string\|null`, `message?:string\|null`; stronger endpoint success aliases require data | Preserves omitted word/quiz error vs null auth error; B01/U01 |
| WordLookupResponse.Word/ErrorMessage | no typed transport DTO | word:WordDto\|null; errorMessage:string\|null; flags required | Frontend mapping; reject missing word on success; B05/U04 |
| WordDto.Pronunciation/AudioUrl | optional phonetic/audio in UI | transport pronunciation/audioUrl:string\|null; explicitly map null to view-model undefined or support null consistently | Browser speech behavior retained; B05/U04 |
| WordDefinitionDto.Id/Example | optional ID/example in Definition view model | transport id:number; example:string\|null; definition/POS/abbreviation/order required | Definition is a view model, not transport; B05/U04 |
| UserVocabularyItemDto.PreferredWordDefinitionId | preferredWordDefinitionId?:number | required number\|null on transport/list model | Frontend; B06/U04 |
| UserVocabularyItemDto.Example/Pronunciation/AudioUrl/PersonalNotes | optional strings without null | required string\|null transport keys; map only at explicit UI boundary | B06/U04 nullable row fixture |
| UserVocabularyItemDto.AccuracyRate | accuracyRate?:number | accuracyRate:number\|null; null at zero attempts; otherwise existing percentage | B06/TQ/U04 |
| QuizQuestionResultDto.SelectedAnswer | selectedAnswer?:string | selectedAnswer:string\|null | B07/U05 unanswered question |
| Quiz dates / UserWord.AddedAt / Word.CreatedAt | quiz/list already strings, user model Date | strings throughout transport; retain existing serialized ISO representation | B01/B08; do not assert all persisted dates already have Z |
| AddWordRequest optional fields | inferred anonymous request can conceal null | typed optional string\|null and preferredWordDefinitionId?:number\|null; word:string in UI | Backend legacy acceptance retained; B06/U04 |

Keep data dates in their current System.Text.Json form: freshly UTC-created timestamps normally include Z; reloaded unspecified DateTime values may not. R7 must not invent timezone conversion or claim uniform UTC offset without evidence about stored values. Document the present limitation and protect string/null type and existing examples. Uniform timestamp normalization is deferred; no schema change follows from TS correcting Date to string.

For WordService replace object outcomes with `ServiceResult<WordLookupResponse>`, `ServiceResult<AddToVocabularyResultDto>`, `ServiceResult<FavoriteUpdateResponseDto>` and `ServiceResult<PreferredDefinitionUpdateResponseDto>`. New acknowledgement DTO fields exactly match current anonymous properties. Keep existing typed vocabulary/quiz data DTOs. Add a named `SuccessResponse<T>` with only success/data for word/quiz actions; preserve auth's generic wrapper and password-change's existing four-field success contract. Consolidation must not inject null fields into old successful word/quiz responses.

Frontend API post/put signatures gain a request type parameter instead of `any`; each active call supplies the concrete request DTO. Add transport `word-api.model.ts` and shared response/error types as needed, not a new API architecture. Centralize duplicated word lookup/search path construction in small existing-service methods or constants; do not move all component behavior into new services. Query/path values remain encoded.

## 14. R5 / UserWord Compatibility

The persisted index in ApplicationDbContext remains `(UserId,WordId)`. UserWord.PartOfSpeechId remains synchronized derived selection state. Do not edit entities, indexes, migrations or model snapshots.

Preserve these ordered add semantics:

1. Require a nonblank word and locate the existing canonical word by the current text comparison; absent canonical word stays invalid request 400, with no provider call or canonical insert.
2. Look for the user's existing saved canonical word **before** enforcing a new preferred selection. Return its stable ID/alreadyExisted=true without altering preference, notes, counters or dates. Do not add automatic DTO validation of optional preferred-ID range that would reject an existing-entry request before this step.
3. For a new save with preferred ID, ensure it belongs to that canonical word and derive POS from it.
4. For new save without preferred ID, retain the present case-insensitive POS name/abbreviation resolution and Noun fallback. Document that it may yield null preference when no matching definition exists; test/protect, do not silently select a different definition in R7.
5. Keep recognized uniqueness-race recovery returning the persisted winner. Unrelated failures become safe 500.

Keep favorite/preferred updates scoped to authenticated owner and UserWord ID. Preferred-definition changes across POS update the same row and preserve SampleSentences/QuizResults. Return the same 404 for missing/nonowned row under the proposed mapping; invalid canonical definition remains 400. Repeated valid PUT keeps the same final state.

Ignored add definition/example/pronunciation fields remain accepted and ignored, explicitly deprecated in API documentation. UI may retain its currently submitted fields for this release; do not suggest these author canonical data. Changes to fallback selection, trimming/casing or ignoring legacy fields need later policy/data review. No restoration of the old triple identity is permitted.

## 15. Legacy DTO Strategy

Repository-wide C# reference inspection (including tests, excluding generated bin/obj) found only declarations/intra-file references for the dormant types below. Active generic ApiResult **does** have controller/test-helper references. Reference searches are source evidence, not execution. The project owner separately confirms under D4 that no distributed C# SDK or binary DTO consumer requires backward compatibility.

| Declaration/file | Inspected references | R7 recommendation |
|---|---|---|
| UserWordDto / UserWordDTOs.cs | Definition and UserWordCollectionResponse.Words only | Remove together with unused collection wrapper; do not change UserWord entity |
| AddWordToCollectionRequest / same file | Definition only; no controller/service binding | Remove; do not add an endpoint or promote it over AddWordRequest |
| UpdateUserWordRequest / same file | Definition only | Remove; no notes/custom-definition/difficulty feature |
| UserWordCollectionResponse / same file | Definition only | Remove with dormant file |
| WordLookupRequest / WordDTOs.cs | Definition only | Remove that class; retain active WordDto/WordDefinitionDto/WordLookupResponse |
| WordRequest / WordRequest.cs | Definition only; owns DefinitionDto list | Remove dormant file |
| DefinitionDto / WordRequest.cs | Only dormant WordRequest relationship | Remove with owner; do not confuse with active WordDefinitionDto |
| DTOs/ApiResponse.cs | Entirely comments, no compiled C# type | Remove dead commented file; Angular ApiResponse is active and retained/corrected |
| ApiResult.ErrorResult(object) | Declaration only; active call arguments are strings | Remove throwing overload after new error helper replaces calls; retain required success shape |
| Controller-local ApiResult<T> | UsersController, ApiTestClientHelper use it | Retain contract; move to an explicit DTO file/namespace only with all references updated |
| Non-generic ApiResult | Password-change success/error currently active | Retain success JSON; errors migrate to AE; align file/namespace cautiously, not delete blindly |
| AddWordRequest fields | Active binder and WORD payload | Retain and document ignored fields; not dormant DTO removal candidates |

Reinspect references during later implementation before removal; stop and retain/deprecate any type that gains a live use. Correct misleading comments but do not rewrite old migration/history documents. D4 confirms there is no distributed SDK/binary DTO consumer; if later evidence contradicts that confirmation, document the discrepancy before changing the approved cleanup scope.

## 16. Quiz Contract Strategy

Preserve two question types and mixed mode, option IDs beginning at zero, GUID strings, four-word minimum, questionCount default 10/cap 20, unknown-mode normalization to mixed, and actual count limited by eligible vocabulary. Preserve the existing null-mode/body binding behavior unless a test reveals it differs from source expectations; do not globally suppress implicit required validation to simplify one DTO.

Add `[JsonRequired]` to `QuizAnswerSubmissionDto.SelectedOptionId` (and favorite IsFavorite in VD) so omitted properties fail JSON binding without changing their CLR value types or legitimate zero/false values. Explicit null remains invalid. Ensure null answer elements are rejected safely before GroupBy/iteration with 400 invalid_quiz_answers; empty list remains valid and omitted questions still count incorrect. Preserve missing Answers default empty list; do not add `[JsonRequired]` to the whole answers array. Explicit null answers remains invalid 400.

Preserve request sessionId and questionId validation: malformed GUID is VE 400, empty/missing sessionId is invalid request, foreign/fabricated/duplicate questions and invalid options remain 400. Keep correct answers absent before submission. Responses keep totalQuestions/correctAnswers/scorePercentage/questionResults and selectedAnswer:null for unanswered questions.

Use section 7 for session status changes. Expiry is 30 minutes at creation, subject to process restart/loss and cleanup; it is not a durable availability guarantee. No automatic POST retries. On failure, leave UI state available for deliberate recovery; 404 session-unavailable tells the user to start a new quiz. Preserve successful transactional mutation and one-result/counter update under repeats/concurrency.

History remains take default 5/cap 20, grouped by existing session ID internally, newest first, with existing items payload and no new session ID fields. A uniform UTC timestamp redesign is deferred. An expiry test should set an expired session deterministically through a narrowly scoped internal test helper next to existing `ClearQuizSessionsForTesting` (or a scoped clock only if genuinely required), never sleep 30 minutes or add a public test endpoint. Maintain QuizApiCollection isolation.

## 17. Backend Contract Test Plan

All tests below are for **later implementation**. Reuse R6's WebApplicationFactory, private SQLite connection, real middleware/JWT/services, deterministic dictionary handler and existing interceptors. Add focused fault injection through ConfigureTestServices or a test-only subclass/factory hook; no parallel application host architecture or production credentials.

Use raw JsonDocument assertions alongside existing typed helpers: exact contract field sets, casing, value kinds, null-versus-omission, no secrets, status/content type and required headers. Do not snapshot generated IDs/tokens/trace values or assume JSON property order. For VE, assert required standard/extension keys and safe field maps, allowing framework metadata that is documented rather than depending on localized exact text.

| Group | Endpoints / scenarios | Expected status and shape | Existing/new test work |
|---|---|---|---|
| B01 | Every successful E01–E14; exact outer/data DTO properties; positive and null examples; AE/VE/BC representatives | 200 current success shapes; non-2xx AE/VE/BC as specified | New Integration/ApiContractShapeTests.cs; extend TA/TD/TV/TQ; small raw-JSON helper in TF |
| B02 | Register valid/duplicate/length/email bounds; login good/bad/unknown; malformed JSON/required fields; no extra expiresAt | 200 nested auth; duplicates400 AE; credentials401 AE; validation400 VE | Expand TA; retain R3 anonymous access; update TS for typed results |
| B03 | Registration faults before save and after save; required legacy/rehash persistence failure; ordinary timestamp failure; stale credentials; password-change fault | 500 AE safe/no token for fatal faults; concurrency409 AE; ordinary timestamp failure still200; wrong password401 | Expand TA and TS; injected faults in test scope; no password/hash leak; distinguish post-save persisted user from rollback guarantee |
| B04 | Profile/validate-token valid JWT, missing account, malformed/missing identifier, missing/expired/tampered token; password fields | Profile/validate200 existing UserDto; profile404; validate401; malformed claims401 AE; middleware401 BC; invalid body400 VE | Expand TA/TR; TH remains unchanged; valid validate-token test added |
| B05 | Cached/provider lookup, genuine404, every provider failure in section10, member flag isolation, null audio, DB exception | 200 nested lookup; invalid400; provider404; unavailable503 AE; local fault500 AE; no provider/key leakage or partial canonical save | Expand TD and ControllableDictionaryHandler fixtures; keep real WordService for mapping tests |
| B06 | Add success/duplicate/concurrent/ignored fields/selection precedence/POS fallback; list paging/type bounds/empty/count; missing/empty search; favorite/preferred success/ownership/presence | Add200 stable IDs; invalid400; missing/nonowned mutations404; faults500; query validation400 VE; empty search200 full words/count/page metadata | Expand TV, preserve R5 relationships and counters; raw request bodies for `{}`/null; query fixtures >1000 rows where needed |
| B07 | Quiz start defaults/modes/limits/four-word minimum; submission omission/zero/nulls/foreign questions/options; session expiry/unknown/nonowned/conflict/repeat; no answer key | Start200 or400; malformed400 VE; invalid answers400; unavailable404; detectable conflicts409; internal fault500; score success200 | Expand TQ; internal deterministic expiry seam only; retain existing collection and concurrency interceptors |
| B08 | History empty/populated/order/take/clamp/wrong type/user isolation; quiz persistence rollback retry | History200 existing items; invalid query400 VE; service fault500 AE; retry after rolled-back failure200 | Expand TQ, including safe 500 replacement for existing rollback test's400 |
| B09 | Validation body/query conversion, missing body, null, unsupported media; no attempted secrets/type names; action errors have aliases/codes | 400 VE; framework415 remains documented; AE appropriate business statuses; BC unchanged | New Integration/ApiValidationContractTests.cs; expand TA/TV/TQ |
| B10 | Existing transport/security boundary tests remain applicable | HTTPS anonymous profile401+Bearer/HSTS; no redirects/production CORS; test-host cannot assert IIS403 | Retain TH/TR/ApiHostSmokeTests; offline existing script fixtures only at future verification if appropriate |
| B11 | All 14 OpenAPI actions/statuses/schemas; auth endpoints anonymous metadata; lookup protected by fallback; no dormant endpoints | In-process Swagger200 schema; paths/methods accurately represented, no raw provider DTO | New Integration/OpenApiContractTests.cs; retain R3 action inventory; review DOC examples against source |
| B12 | Every WS/QS outer catch and auth fatal branch maps to server failure, not client400/401 | 500 AE for unexpected failures; expected category cases remain per section7 | Table-driven contract cases in new Integration/ApiFailureContractTests.cs plus real-service interceptors; do not rely solely on fake service failures |

### 17.1 Existing assertions that intentionally change

- TV `UnrelatedVocabularyPersistenceFailureIsNotDuplicateSuccess`: 400→500; still no new saved row.
- TV `FavoriteStateIsOwnedPerUserForSameCanonicalWord`, `PreferredDefinitionsRemainIndependentAndCrossUserMutationIsRejected`, `MissingVocabularyAndDefinitionIdsFailWithoutMutation`: nonowned/missing UserWord response 400→404; invalid definition for owned row remains400. Preserve all no-mutation assertions.
- TQ `PersistenceFailureRollsBackResultsAndLearningStateAndSessionRemainsRetryable`: failure400→500; rollback and successful retry assertions remain.
- TQ unknown/other-user/expired/removed-after-success session cases: 400→404. Known in-progress or duplicate constraint cases:409. A concurrent loser may see404 after winner removal; exactly one success and unchanged aggregate invariants remain mandatory.
- TS typed auth/password returns require assertion access changes, not weaker credential, logging or concurrency behavior. Use explicit FailureType assertions on failed service outcomes.

### 17.2 Limitations

SQLite relational tests validate the production model with EnsureCreated, not SQL Server migrations/collation/error codes/IIS. No migration is planned. Do not substitute broad mock-only controller tests for real provider/persistence paths. Keep fault-injection tests deterministic and free of live provider/network dependencies. No test count or pass result is promised by this plan.

## 18. Angular Contract Test Plan

Use existing Jasmine/Karma/HttpTestingController patterns. Stub localStorage and speech where needed; tests must not inherit a real browser session. No broad UI redesign/E2E stack.

| Group | Endpoint/behavior and expected contract | File work |
|---|---|---|
| U01 | Generic API base URL, exact verb/path/query encoding/body; Authorization present only with token; error channel preserved; typed data/null | Expand UP; new `services/api-error.spec.ts` for AE/legacy/VE/ProblemDetails/empty401/network/HTML/unknown/malicious500 text |
| U02 | POST register/login bodies and nested AuthResponse; no confirmPassword; no expiresAt dependency; store valid token/user only; no storage on invalid response/error; JWT expiry/base64url/invalid token | Expand UA, assert method/body/headers, localStorage writes and unchanged keys |
| U03 | Signup limits3/100 and template text; duplicate400 message; login401 vs internal500 display; success navigation; validation field messages rendered safely | Expand UL/USU with reviewed backend-shaped fixtures |
| U04 | Lookup400/404/503/500/401, typed mapping/null audio; add request/duplicate success; favorite PUT true/false and rollback; preferred same-row selection and unavailable404; list/search full metadata; page-local scope/errors | Expand UW; assert request.method/body/header in existing duplicate/preferred tests; two-page (>1000 total) fixtures and existing navigation; no full-catalog claim on one page |
| U05 | Quiz start exact request/defaults, questions without answers, option zero sent explicitly, submit GUID/answers, result selectedAnswer:null, history/take,400/404/409/500 handling; no automatic retry | New `components/quiz/quiz.component.spec.ts`; use HttpTestingController, deterministic fixtures, no live backend |
| U06 | Old/new compatibility: legacy error-only and new AE aliases; VE before/after extensions; success envelope unchanged; malformed200 rejected safely | Reuse U01–U05 fixtures; paired old-server/new-client cases |

Tests must inspect method, route, request payload and Bearer headers where applicable, not just URL suffix or component construction. Fixture JSON must match reviewed backend examples in B01, including null keys, and must not be shaped independently around TypeScript assumptions. Compilation alone is not runtime validation.

## 19. Detailed Implementation Phases

These are **eight approved future phases** under the approved D1–D4 decisions. Phases are development sequencing, not instructions to deploy each phase. No phase starts in this documentation task, including Phase 1.

### Phase 1 — Contract protection and approved baseline

- **Findings:** F12/F13; protect F05/F06/F08/F14 and R3/R4/R5.
- **Files:** existing TA/TD/TV/TQ/TR/TF and Angular specs; proposed ApiContractShapeTests plus reviewed fixture files. No production change required for baseline.
- **Work:** capture successful wire shapes and known legacy errors, auth boundary and duplicates. Label tests whose expected error statuses will intentionally change. Add regression cases alongside each later fix rather than leaving the branch permanently failing between phases.
- **Tests:** baseline portions of B01/B02/B04/B06/B07 and U01/U06; retain current state assertions.
- **Dependency:** approved D1–D4; current source inspected again by implementer.
- **Risk:** freezing incorrect400 behavior as desired. Distinguish characterization from final acceptance explicitly.
- **Complete when:** old success contracts and security/persistence invariants are pinned; planned expectation changes are named; fixture shapes are reviewable.

### Phase 2 — Compatible Angular auth/error and transport groundwork

- **Findings:** F04/F05/F08/F11/F13.
- **Files:** UM/AU/API, new api-error helper/spec and transport types, LOGIN/SIGNUP .ts/.html, WORD/QUIZ error paths, UA/UP/UL/USU/UW.
- **Work:** tolerant safe error extraction; auth payload/date/null alignment; remove expiresAt assumption; username max100; guard session writes; type requests/responses without changing routes. Keep current success shapes and localStorage keys.
- **Tests:** U01–U03/U06; baseline error tests for U04/U05.
- **Dependency:** P1; works against current backend before server changes.
- **Risk:** unsafe legacy500 display or rejecting old valid success payload. Prefer status-safe fallback and actual wire fixture types.
- **Complete when:** new frontend handles both existing and planned error formats; no token field invented; current auth flows/storage retained.

### Phase 3 — Backend safe errors and service classifications

- **Findings:** F01/F02/F06/F12.
- **Files:** SR, proposed ApiErrorResponse/ApiErrorResults/ApiValidationResponses, US/IUserService, WS/QS, UC/WC/QC, BOOT validation-factory registration, TF/TS and contract tests.
- **Work:** explicit categories/codes, typed auth/password outcomes, safe application errors and validation extensions, controller status mapping. Preserve ordinary timestamp-write tolerance and all credential/persistence safety. No middleware reorder.
- **Tests:** B02/B03/B04/B09/B12; revise approved fault statuses; U06 compatibility.
- **Dependency:** P2 compatibility layer and D1/D2.
- **Risk:** accidental token issuance on failed mandatory save; R4 lock/rollback changes; global validation leaks. Keep change surface at result classification/response construction.
- **Complete when:** no identified exception text can reach action responses; unexpected faults yield500 AE; known ownership/conflict mappings follow section7; challenges unchanged.

### Phase 4 — Provider structural failure boundary

- **Findings:** F03; lookup part of F02/F12.
- **Files:** PD/WS, TD and controllable provider fixtures; WC only if needed for typed signature changes.
- **Work:** validate before persistence, preserve supported mapping and optional missing audio/pronunciation/examples, handle explicit null/bad structure as503. Keep real provider HTTP/non-404 failures abstracted.
- **Tests:** B05 and U04 provider display; assert no persistence/no secret leakage.
- **Dependency:** P3 categories.
- **Risk:** over-rejecting optional missing fields or treating internal DB errors as503. Follow the table exactly; no catch-all provider exception label.
- **Complete when:** malformed provider cases cannot fall through to400, genuine404 remains separate, cache success unchanged.

### Phase 5 — Typed vocabulary contracts and bounded client alignment

- **Findings:** F06/F07/F08/F09/F10/F11/F12/F13.
- **Files:** WD/VD/AD documentation, IWordService/WS/WC, named SuccessResponse/acknowledgement DTOs, API/WM/WORD .ts/.html, TV/UW.
- **Work:** eliminate object and anonymous payload ambiguity without success-field changes; required favorite presence; full typed empty search; preserve POS/text/duplicate semantics; make page-local filtering/count scope explicit and distinguish list failure from empty result; use existing paging controls.
- **Tests:** B01/B06 and U04, positive/negative/null boundaries, same-row R5 cases, >1000 totals/two-page UI.
- **Dependency:** P2/P3/P4; approved D2/D3.
- **Risk:** automatic DTO validation before existing-entry return; accidental WordId/UserWordId mixup; changing successful omission/null shape.
- **Complete when:** typed current operations, explicit scope/default documentation, no any at active word transport boundary, stable duplicate IDs, current-page wording and failure display are covered.

### Phase 6 — Quiz contract boundaries and missing frontend tests

- **Findings:** F07/F11/F12/F13/F14.
- **Files:** QD/QS/QC, QM/QUIZ, TQ, new quiz component spec; narrowly scoped internal expiry test helper if needed.
- **Work:** require selectedOptionId presence; reject null answer elements; preserve zero/empty-answer scoring and normalization. Complete proposed unavailable/conflict semantics and temporal documentation.
- **Tests:** B07/B08 and U05; retain rollback/retry/concurrency counter checks; use deterministic expiration.
- **Dependency:** P3 errors, P2 frontend helper; D2 status/presence approval.
- **Risk:** scoring/retry behavior changes disguised as validation cleanup. No session persistence rewrite or automatic submit retry.
- **Complete when:** exact request/result/null shapes, expiry/conflict and user isolation covered; no correct answers before submission; counters/dependents unchanged.

### Phase 7 — Schema metadata, legacy cleanup and reference documentation

- **Findings:** F06/F08/F14/F15/F16.
- **Files:** legacy files in section15, active ApiResult relocation and TF import, controller response annotations, BOOT Swagger registration plus new `Swagger/ApiContractOperationFilter.cs` if needed, DOC and a final R7 contract reference/completion document during implementation.
- **Work:** remove only proven unused declarations; correct active names/locations; document actual error variants/statuses and defaults. Add an operation filter clearing the global security requirement for AllowAnonymous actions while retaining required authentication metadata for all protected actions, including fallback-protected lookup. Do not change runtime auth policy.
- **Tests:** B01/B11; future source reference/build checks; preserve schemas and removed canonical-write route absence.
- **Dependency:** P3–P6 stabilized contracts.
- **Risk:** wrapper move breaks TF or Swagger types collide; documentation might imply new sample/notes/delete routes. Keep all14 actions explicit.
- **Complete when:** schema describes actual shapes/status/auth; dead classes are gone without active contract deletion; current examples accurate; analysis/roadmap history not rewritten.

### Phase 8 — Full future verification and release-review evidence

- **Findings:** all16; no new scope.
- **Files:** implementation completion/evidence documentation and only fixes justified by failures. Workflows/deployment scripts remain unchanged.
- **Work/tests:** future commands/checks in section25; all B/U groups, backend suite, Angular suite/build, schema/reference inspection and no-migration/CI compatibility review. No automatic release action.
- **Dependency:** P1–P7 complete; no unapproved deviations.
- **Risk:** using a successful SQLite run as SQL Server/IIS proof; bypassing release guard for rollback; claiming historic results as current.
- **Complete when:** fresh required checks pass, limitations recorded, all16 dispositions reconciled and release review receives explicit observable-change list. No unexplained skipped/failing tests or unreviewed contract decisions remain.

## 20. Database Impact

**No database migration is expected or authorized by this plan.** Request presence, response typing, safe errors, HTTP status, frontend interpretation and provider validation do not require schema changes. Preserve entities, ApplicationDbContext mappings, R5 uniqueness, quiz uniqueness, migrations and snapshots.

Timestamp string typing does not justify changing stored timestamp kinds. If implementation discovers a genuine data/schema requirement, stop that dependent portion and present a separate proposal for explicit approval; do not create an opportunistic migration within R7.

## 21. Production Compatibility

Planned production files intersect with the HTTP boundary: UC/WC/QC and BOOT. BOOT changes are limited to MVC validation response configuration and Swagger metadata/registration. US interface/result changes alter internal dependency contracts but not JWT cryptography/configuration. No changes to `JwtSettings`, `JwtHelper`, API/public `web.config`, environment URLs, middleware ordering, forwarded-header handling or hosting settings are planned.

Preserve HTTPS redirects/rejections, canonical-host Production HSTS `max-age=300` without subdomains/preload, development-only configured CORS, existing JWT validation, same-origin `/api`, and current forwarded-header behavior (do not introduce or alter middleware).

The existing production acceptance script checks anonymous HTTPS `/api/users/profile` for401 with Bearer challenge, no redirect/HTML/production CORS and required HSTS. It does not assert the full application error JSON or authenticated success payload. Proposed AE changes do not require that script to change; replacing401 with200/redirect would be a regression. Test-host HTTPS checks cannot prove IIS redirects. Production smoke execution is outside this planning task and requires a separately authorized release context later.

## 22. CI/CD Impact

**No CI/CD changes expected.** Existing [.github/workflows/backend-tests.yml](../../.github/workflows/backend-tests.yml) runs the backend suite and frontend ChromeHeadless tests, builds/publishes the combined artifact, and deploys master pushes through the `production` environment. New tests in existing projects/spec patterns are picked up by those gates.

Preserve automatic deployment, tested artifact selection/digest behavior, production concurrency group, `cancel-in-progress:false`, `queue:max`, and [Assert-ProductionReleaseCandidate.ps1](../../scripts/ci/Assert-ProductionReleaseCandidate.ps1)'s current-master SHA guard. Do not push to master as a test of the plan. No workflow, release script, gate, environment or serialization changes are needed for R7.

## 23. Backward Compatibility

| Observable change | Classification | Mitigation |
|---|---|---|
| Same200 success fields/routes; typed backend classes and TS models | Non-breaking HTTP; coordinated internal signature changes | Preserve nested flags/null omission; raw fixtures; update service tests/imports |
| Safe fixed500 error instead of raw exception/400 or auth401 | Coordinated frontend/backend; potentially breaking status | P2 parsing first; fixed server-safe text; explicit release notes; no auto logout/retry |
| AE adds message/code/traceId/data while retaining success/error | Non-breaking for current Angular; additive change approved | No removal of old fields; retain aliases and old/new client fixtures |
| VE retains ProblemDetails and adds compatibility fields | Additive change approved for the maintained Angular consumer | Preserve standard keys/content type; sanitize unsafe binding errors; fixtures |
| Missing/nonowned saved rows/sessions404; known conflicts409 | Potentially breaking status change; coordinated frontend/backend approval recorded | Single unavailable message for concealment; old UI still handles non-2xx; D2/D4 approved |
| Missing isFavorite/selectedOptionId rejected400 | Potentially breaking for incomplete callers | Current Angular already sends both; explicit approved tightening; zero/false remain valid |
| Empty search returns full200 empty envelope | Potentially breaking status; added metadata | Consumers already expect words[]; preserve normal-query shape/count limit |
| Signup max100 instead of50 | Non-breaking expansion of UI inputs | Backend already accepts100; update field-message/tests |
| Current-page labels and distinct vocabulary load error | Non-breaking UI clarification | Preserve Next/Previous and query cap; no whole-catalog promise |
| Remove inactive C# declarations | No HTTP change; no distributed SDK/binary DTO consumers per owner | Repository references checked; retain implementation-time reference inspection |
| Date strings typed correctly, ignored add fields/POS defaults preserved | Non-breaking; known limitations remain | No wire/date/account normalization in R7; document follow-up |

`VocabularyApp.UI` is the only maintained production API consumer. No maintained external consumers were identified, and the project owner confirms none exist: no external mobile application, second production website, external integration, distributed public API client, or distributed C# SDK/binary DTO consumer. Repository manual/test examples remain supporting development/test consumers only. External-client ownership is resolved, not unknown.

Coordinated Angular/backend remediation is approved for R7; a versioned API or external-client deprecation program is not required. This permits only the already-approved changes, not unnecessary breaking changes. Preserve successful response contracts and retain all planned compatibility aliases for transition and rollback. Implementation can proceed as one coordinated change set with P2 logically first. If separate releases are later authorized, ship client tolerance before server classification changes through the unchanged release process. Old browser tabs must continue ordinary successful flows against the new backend; new clients must accept old error bodies during rollback. Do not remove aliases or flatten nested responses during R7. Deployment/release authorization remains separate from future implementation authorization.

## 24. Rollback Strategy

Keep R7 code-only and reversible. Retain a reviewed pre-R7 code reference and produce matched frontend/backend artifacts through the existing pipeline; do not depend on database rollback. If regression occurs after a separately authorized release, create a new revert/fix commit on the normal branch flow, run normal gates, and deploy the newly tested artifact at current master.

**Do not rerun an old release or manually deploy an old artifact to bypass the stale-release guard.** It intentionally rejects a candidate that is no longer current master. A rollback is a new forward-moving source change through the same serialization/production environment controls.

Revert paired server/status and client-consumer changes together where practical. New Angular's tolerant parser must still accept old server responses, and retained successful JSON allows old tabs to work with reverted server. No token key/lifetime/localStorage migration means no planned user-session reset. Stored vocabulary/quiz results need no transformation. An app restart may lose in-memory quiz sessions exactly as before; surface unavailable session and allow the user to start another.

Prefer a targeted safe fix if a complete revert would reintroduce raw exception disclosure; release owner chooses under the incident process. Rollback/release commands are not authorized or executed by this planning document.

## 25. Future Verification — DO NOT EXECUTE DURING THIS TASK

The commands below are **examples for Phase8 of a later authorized implementation task**. None were run now. Do not execute production scripts, EF commands or live provider requests as substitutes for these checks.

From the repository root, using the repository's .NET8 toolchain:

```powershell
dotnet restore .\VocabularyApp.sln
dotnet build .\VocabularyApp.sln --configuration Release --no-restore
dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=normal"
```

During the relevant development phase, a focused contract run may use the same project with `--filter "FullyQualifiedName~Integration"`; targeted new contract classes may be selected while iterating. The full suite above remains required at final verification. Avoid rerunning identical full checks without new changes or unresolved failures.

From `VocabularyApp.UI`, using the workflow's Node22-compatible environment and Chrome available for headless tests:

```powershell
npm ci
npm test -- --watch=false --browsers=ChromeHeadless
npm run build -- --configuration production
```

Future non-execution reviews/checks:

- Review the diff for no changes to Data entities/context/migrations/snapshot, configuration files, workflows, deployment scripts, JWT policy or middleware order. No EF command is needed to prove a documentation/type-only intent; any unexpected model change blocks completion.
- Inspect contract fixtures/OpenAPI tests for all14 actions, no new route, correct auth metadata and response types. Reconcile all16 dispositions.
- Review built Angular output and API publish compatibility using the existing CI publish gate in a later authorized release; do not create a new publish/deploy mechanism. If local package publication is separately requested, follow the existing build/staging approach and keep it distinct from deployment.
- Retain existing offline script fixtures/TH assertions where applicable; production smoke remains a separately authorized release action, not automatic Phase8 permission.
- Record exact commands, actual results and SQLite/IIS limitations in implementation completion evidence. Do not recycle earlier196-test or coverage results as R7 acceptance.

## 26. Required R7 Work

All16 findings receive the dispositions in section5. Required work is: safe/classified errors, compatible typed error construction and validation handling, provider structural validation, auth/nullable transport alignment, mutation-presence validation, full empty-search contract, explicit page-local vocabulary semantics and failure display, exact backend/frontend contract tests, accurate OpenAPI/reference documentation, and verified dormant DTO cleanup.

Preservation is work too: retain R5 duplicate/selection semantics, R4 transactional counters, credential migration/no-token guarantees, current successful envelopes and current deployment/auth/transport controls. Do not interpret an internal result-type refactor as authorization to change business rules.

## 27. Deferred Follow-Up

Zero findings are deferred entirely, but these larger remedies are explicitly deferred:

- F06: universal ProblemDetails-only errors, a single redesigned success envelope, generated clients.
- F08: new word/account normalization and POS selection fallback rules, removal of ignored legacy add fields, data repair for saved rows lacking usable selection.
- F10: collection-wide server filtering, server-provided letter counts and broad vocabulary UI decomposition. R7 instead makes existing page scope explicit.
- F11/F13: runtime schema library, broad Angular service/component redesign and full browser E2E architecture.
- F14: persistent quiz sessions, durable repeat-submit results and uniform stored timestamp migration/normalization.
- Provider extraction/concurrent cache-miss recovery, SQL Server-specific test infrastructure, entity placeholder cleanup unrelated to active contracts.

These remain known limitations, not claimed resolved defects. Any expansion requires a separate scoped decision rather than silently adding phases.

## 28. Out-of-Scope Work

New features, delete/archive/notes/sample-sentence APIs, AI/study features, major UI redesign, database redesign or migration, WordsAPI replacement, JWT/password infrastructure replacement, hosting/forwarded-header redesign, broad service-layer rewrite, unrelated cleanup, CI/CD redesign, roadmap reprioritization, production access and release execution. Existing analysis/backlog documents are not modified by this task.

## 29. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Status/additive-field changes disrupt old Angular tabs or rollback | D4 confirms Angular is the only maintained production consumer; release notes, retained aliases and tolerant Angular before backend; no success redesign |
| A result refactor weakens credential migration or rollback | TS/TQ/TV invariants retained; distinguish fatal credential writes from tolerated timestamp writes |
| Required JSON fields accidentally reject false/zero or existing-entry add | JsonRequired presence only on bool/option ID; no blanket optional-preference annotation; raw omission/null/zero tests |
| Provider validation rejects legitimate absent optional data | Explicit absent-versus-null policy/table; keep missing audio supported; fixtures before production |
| Empty-query behavior differs from inferred framework default | Characterize through real HTTP in P1; target behavior fixed by D2, no global required-validation suppression |
| Page-scope fix grows into search feature | D3 restricts to label/error-state and existing controls; server-wide UX deferred |
| JSON DTO consolidation changes null-field presence or Swagger auth | Raw success fixtures and operation-level metadata tests; no runtime auth change |
| Rollback bypasses deployment safeguards | New tested revert/fix commit at master; no stale artifact replay |
| Static plan is mistaken for verified software | No execution in this task; Phase8 records actual later results; no fabricated pass status |

## 30. Approved R7 Decisions

The original plan recorded four unresolved decisions and a decision-based NO-GO. The project owner has now reviewed and approved **D1–D4 exactly as recorded below**. All four decisions are resolved; no project-owner approval is pending for these choices. This record supersedes the previous open-decision state without redesigning the technical plan.

| Decision | Status | Approved choice | Implementation consequence |
|---|---|---|---|
| D1 — Error strategy | **APPROVED** | Named AE retaining error, compatible message alias, stable code, opaque traceId and data:null; existing success structures preserved; ASP.NET validation ProblemDetails retained with the proposed compatible extensions; framework Bearer challenges unchanged; universal ProblemDetails-only migration deferred. | Implement section8 exactly as planned. Retain aliases and successful payloads; no broad error-contract redesign. |
| D2 — Observable behavior changes | **APPROVED** | Section7 matrix unchanged: appropriate400/401, concealed404, limited detectable409, unexpected internal500, provider/unusable-response503. Require JSON presence of isFavorite and selectedOptionId while accepting false and0. Return full typed200 empty search for absent/empty/whitespace terms. Preserve duplicate-registration400, duplicate-UserWord-add200, R5 duplicate behavior and quiz scoring. | Implement the existing mappings and presence rules with their planned tests. Do not mechanically remap statuses or alter scoring/duplicate semantics. |
| D3 — Preservation and scope | **APPROVED** | Preserve and document/test add-word text behavior, POS fallback, ignored legacy add fields, timestamp wire behavior, R5 (UserId,WordId) identity, quiz persistence/scoring and paging. Align frontend signup max50→100. Limit F10 to current-page labels and distinct vocabulary load-error state. | Keep all16 dispositions and eight phases. Defer new normalization/POS fallback, removal of ignored fields, collection-wide filtering/letter counts, broad UI redesign, persistent sessions, durable repeat results and uniform stored timestamp normalization. |
| D4 — Consumers and release policy | **APPROVED** | Owner confirms VocabularyApp.UI is the sole maintained production API consumer; manual/test examples support development/testing only. No external mobile app, second production website, external integration, distributed public API client or distributed C# SDK/binary DTO consumer exists. Coordinated frontend/backend remediation is approved; retain compatibility aliases; API versioning/external-client deprecation program is not required for R7. | External-consumer uncertainty is resolved. Preserve successful contracts and rollback compatibility; do not introduce extra breaking changes. Deployment/release remains separately authorized. |

No other architecture choice is intentionally left to the implementer: concrete defaults, statuses, response types, test groups and phase dependencies are specified above. Routine coding details may vary if they preserve these contracts.

## 31. GO / NO-GO Criteria

**Current decision gate: GO — READY FOR IMPLEMENTATION.** D1–D4 are approved and recorded, and the external-consumer question is resolved. No new discrepancy was discovered during this documentation inspection. The approved plan authorizes the future R7 implementation task, with all eight phases and no migration/CI change expected. This task only records readiness: no phase begins now. Future implementation should inspect its starting source state and reconcile any newly discovered discrepancy before dependent work.

**Return to NO-GO only if a new contradiction is discovered:** the work requires an unapproved schema/auth/hosting change, evidence contradicts the confirmed consumer boundary, or the implementer cannot preserve R3/R4/R5 guarantees. Document such a contradiction rather than silently redesigning approved decisions. No such blocker is currently recorded.

**Authorization boundary:** GO does not authorize deployment, production release, production access, database access or live provider calls. Those remain separately controlled. Tests/builds in section25 are future implementation work and were not executed during this documentation task.

**Before completion/release review:** all eight phases meet their criteria, new/current backend and frontend suites pass, production Angular build succeeds, contracts and OpenAPI agree, required security/ownership/rollback tests remain, no unintended production-control changes exist, and actual verification limitations/results are recorded. Release remains governed by the existing process and separate authorization.

## 32. Final Implementation Readiness Assessment

**GO — READY FOR IMPLEMENTATION.** D1 is **APPROVED**; D2 is **APPROVED**; D3 is **APPROVED**; D4 is **APPROVED**. All16 findings retain explicit R7 dispositions and all eight implementation phases remain approved, with no technical scope expansion. No database migration or CI/CD change is expected. `VocabularyApp.UI` is the only maintained production API consumer, as confirmed by the project owner; supporting manual/test examples are development/test consumers only.

Implementation is ready to begin under a separate future R7 implementation task and **has not yet begun**, including Phase1. Deployment/release remains separately authorized. GO does not authorize production access, database access or live provider calls.

This documentation task updated only `Docs/Updates/R7-api-contracts-remediation-implementation-plan.md`; the R7 analysis and all other documentation remain unchanged. No production or test code changed. **No tests, builds, restores or application verification commands were run.** No commit, push, merge, rebase, deployment, production/database access or live provider call occurred.
