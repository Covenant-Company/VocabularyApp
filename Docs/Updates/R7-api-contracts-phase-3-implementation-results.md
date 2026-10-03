# R7 Phase 3 — Backend Safe Errors and Service Classifications

Date: 2026-10-01

Status: **IMPLEMENTED — EXECUTION VERIFICATION PENDING**

This records Phase 3 only under D1–D4 in the [approved implementation plan](R7-api-contracts-remediation-implementation-plan.md), the [analysis](R7-api-contracts-remediation-analysis.md), and the supplied continuation requirements. The [Phase 1](R7-api-contracts-phase-1-implementation-results.md) and [Phase 2](R7-api-contracts-phase-2-implementation-results.md) work remains intact. No runtime verification or overall R7 completion is claimed. Phase 4 has not begun.

## 1. Recovered state and completed scope

The continuation inspected the existing working tree, tracked diff, untracked Phase 3 files, source consumers and authoritative documents before editing. The interruption checkpoint was substantially implemented: all three controllers already used the shared error helper and guarded required success data; typed auth/password outcomes, service classifications, validation configuration and initial fault/contract tests were present. Provider parsing and quiz session algorithms had not been redesigned. No Phase 3 results document existed.

The unfinished portions were explicit failure-category/code assertions in the adapted credential service tests, broader real-service fault coverage, complete nested-auth invariant coverage, additional validation boundaries, and this record. These were completed without restarting or replacing the valid production implementation. Existing Phase 1/2 files and the planning/backlog documents were preserved. There were no remaining signature inconsistencies identified by source inspection; compilation is still unverified.

Phase 3 addresses F01 and the Phase 3 portions of F02/F06/F12, with B02/B03/B04/B09/B12 coverage and the retained Phase 2 U06 compatibility fixtures. Later phases still own provider structural validation, full vocabulary contracts, quiz session classifications, metadata/cleanup and execution verification.

## 2. Backend production files

Paths below are relative to `VocabularyApp.WebApi/`. This inventory includes the valid work recovered from the interrupted run, not just the continuation edits.

| File | Phase 3 change |
|---|---|
| `DTOs/ApiErrorResponse.cs` (new) | Named six-field application error response with safe aliases and explicit null data. |
| `Helpers/ApiErrorResults.cs` (new) | Shared category/code-to-status/message catalog; fixed internal fallback; server trace identifier. |
| `Helpers/ApiValidationResponses.cs` (new) | Sanitized validation ProblemDetails with compatible extensions and safe field paths/messages. |
| `Models/ServiceResult.cs` | Added InternalError, Unauthorized and Conflict classifications and an optional stable code. |
| `Services/IUserService.cs` | Typed registration/login and password-change result signatures. |
| `Services/UserService.cs` | Safe registration exception handling; explicit auth/password failure classifications; preserved credential and timestamp-write semantics. |
| `Services/WordService.cs` | Explicit expected failure codes, concealed saved-row 404 classification and internal-failure groundwork; existing provider parsing and R5 behavior preserved. |
| `Services/QuizService.cs` | Internal-error classification for unexpected start/submit/history failures and safe codes for current expected failures; existing session/scoring/persistence behavior preserved. |
| `Controllers/UsersController.cs` | Shared safe errors, typed-result unwrapping, auth success invariants and removal of duplicate manual ModelState branches. |
| `Controllers/WordsController.cs` | Shared safe errors, required top-level success data checks and removal of add's null-data success fallback. |
| `Controllers/QuizController.cs` | Shared safe errors and required success data checks. |
| `Program.cs` | Registers `InvalidModelStateResponseFactory`; no other startup or middleware change. |

Twelve production files: nine modified and three new. The continuation retained their existing Phase 3 implementation without further production edits. `IWordService`, `IQuizService`, active successful DTOs, password/JWT implementations and middleware ordering remain unchanged.

## 3. Application errors and validation

Application-generated action failures use `application/json` with exactly these six keys:

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

`error` and `message` are identical safe strings. The trace is `HttpContext.TraceIdentifier`, not an echoed request header. Service messages, exception messages and unknown codes do not become public text. Only recognized category/code pairs select expected 400/401/404/409/503 responses; InternalError, Failure.None, unknown categories, missing codes and mismatched pairs fall back to fixed `500 internal_error`. A nominal success passed to the failure helper also falls back to 500.

Automatic validation remains `application/problem+json` with `type`, `title`, `status`, `errors` and `traceId`, plus `success:false`, `data:null`, `error`, `message` and `code:"validation_failed"`. The common summary is `One or more request fields are invalid.` Known annotation messages remain available. Binding/conversion exceptions and unrecognized messages become `Invalid value.` Known contract field paths, including bounded numeric collection indexes, are retained; unknown paths collapse to `$`. ModelState exceptions and attempted values are not serialized.

The factory uses MVC's ProblemDetailsFactory and returns a JsonResult with the validation content type explicitly set, including for controllers declaring `Produces(application/json)`. No global validation suppression, new request-presence requirement, middleware replacement or framework challenge wrapper was introduced. Unsupported-media 415 remains framework behavior. Framework Bearer challenges retain 401, their challenge header and empty body; action-level invalid claims use the application envelope. Routing and other framework failures are outside the application-error helper's scope.

## 4. Authentication and persistence distinctions

Registration/login now return `ServiceResult<AuthResponse>` internally. Controllers unwrap the existing successful AuthResponse, preserving `success,data,error` outside and `success,errorMessage,user,token` inside. They require a successful nested result, non-null user and nonblank token. Password change returns `ServiceResult<bool>` with true success data; the HTTP success remains the existing `success,message,data,error` acknowledgement. UserDto lookup signatures remain unchanged.

| Condition | Phase 3 outcome |
|---|---|
| Duplicate username / email | Existing 400, now `username_taken` / `email_taken`; no duplicate account. |
| Registration query/hash/save failure before a successful save | Fixed 500 `internal_error`; injected pre-save cases assert no persisted account. |
| Registration token generation failure after a successful save | Fixed 500 `internal_error`, no token in the error response; account remains persisted in the authored fixture. No rollback or new transaction is claimed. |
| Missing user, wrong password, malformed/unknown stored credential at login | Identical 401 `invalid_credentials` and `Invalid username or password`; no account-existence distinction or token. |
| Required legacy migration/modern rehash unavailable or persistence failure | Fixed 500 `internal_error`; login fails before token generation. Stored credentials/timestamp remain unchanged in the controlled failed-save fixtures. |
| Ordinary LastLogin timestamp-only save failure | Nonfatal: login still succeeds and may return a token; safely logged. The persisted timestamp remains unchanged in the fixture. This does not apply to mandatory credential writes or concurrency. |
| Credential concurrency during login or password change | 409 `credentials_changed`; login issues no token. Existing real stale-write tests preserve the newer credential hash. |
| Unexpected login query/verification/token-generation failure | Fixed 500 `internal_error`. Token-generation failure may follow an already-persisted LastLogin update; no rollback is claimed. |
| Wrong current password / unavailable account on password change | 401 `current_password_incorrect` / `user_unavailable`. |
| Unexpected password-change query/verification/hash/save failure | Fixed 500 `internal_error`; failed-save/hash fixtures retain the stored credential. |
| Missing authenticated account on profile / validate-token | 404 `user_not_found` / 401 `user_unavailable`, respectively. |
| Missing or malformed identifier claim reaching an action | 401 `invalid_token`; framework authentication remains separate. |
| Unexpected profile / validate-token lookup exception | Shared fixed 500 `internal_error`. |

Password bytes, username/email comparison behavior, password verification/hashing, credential concurrency tokens, JWT claims/signing/lifetime and token storage policy were not changed. The internal unused token-lookup method was not redesigned; the validate-token action continues to use the authenticated identifier and existing user lookup.

## 5. WordService, QuizService and controller boundaries

All six WordService outer unexpected catches now classify InternalError/internal_error. Existing recognized provider misses and transport failures retain 404 `word_not_found` and 503 `dictionary_unavailable`. Canonical-word and preferred-definition preconditions remain 400; missing/nonowned saved-row updates share 404 `vocabulary_not_found`. Existing duplicate-add winner recovery, `(UserId, WordId)` identity, preference precedence, POS fallback, text handling and query limits remain unchanged.

Provider structure parsing and mapping were not changed. Explicit-null provider collections/elements can still fall through to the generic internal boundary; Phase 4 must implement their intended 503 classification before R7 is complete. The Phase 3 valid-provider/local-save fault case distinguishes internal storage failure from provider unavailability without introducing new provider validation.

Quiz start/submit/history outer unexpected catches now classify InternalError/internal_error. Transaction rollback/disposal, change-tracker clearing, submission lock release, sessions, expiry, scoring and counters are unchanged. Current expected quiz failures have safe catalog codes; session missing/ownership/expiry/in-progress/repeat/vocabulary-change branches retain their existing 400 behavior with generic `invalid_request` at this phase. Their specific 404/409 outcomes and null-answer/selected-option presence remediation remain Phase 6. They are not claimed complete by this record.

All three controllers use the common safe 500 response in existing unexpected catches. A successful result lacking required top-level data is an internal invariant failure; password change also requires true and auth requires its nested user/token. Word add no longer invents `{message}` success data. All current successful response structures and all 14 routes remain intact. Deeper vocabulary typing and named acknowledgements remain Phase 5.

## 6. Backend tests and infrastructure

Paths are relative to `VocabularyApp.WebApi.Tests/`. These are authored source assertions, not executed results.

| File | Phase 3 coverage/change |
|---|---|
| `Infrastructure/ApiErrorContractAssert.cs` (new) | Exact application-error keys/status/content type, safe message aliases/code/nonempty trace and absence of secret sentinels/exception details. |
| `Infrastructure/VocabularyAppWebApplicationFactory.cs` | Optional test-only EF interceptors and DI customization on the existing SQLite host; default behavior retained. |
| `Integration/ApiSafeErrorTests.cs` (new) | Real services with controlled query/save/hash/token faults; registration persistence boundary; duplicate registration; invalid credentials; mandatory legacy/rehash versus timestamp-only failure; credential concurrency; password change; profile/claims; word/quiz fault groundwork and recognized provider status catalog. |
| `Integration/ApiErrorBoundaryTests.cs` (new) | Controller exceptions/missing success data, nested auth invariant failures and unknown/mismatched failure classifications through test-only service substitutes. |
| `Integration/ApiValidationContractTests.cs` (new) | Required/invalid bodies, malformed JSON and untrusted paths, query/body conversions, registration annotation bounds, validation extensions/safe fields, framework 415 and empty Bearer challenge. |
| `Integration/VocabularyOwnershipApiTests.cs` | Approved internal-failure and concealed missing/nonowned-row expectations; exact error envelopes; original no-mutation/R5 checks retained. |
| `Integration/QuizApiTests.cs` | Persistence rollback failure expects safe 500; original rollback, counter preservation and successful retry assertions retained. Other session-status assertions stay for Phase 6. |
| `Services/UserServiceAuthenticationTests.cs` | Typed auth/password result access; duplicate and incorrect-password categories/codes; successful password data true; original credential assertions retained. |
| `Services/LoginMigrationTests.cs` | Typed auth results; explicit invalid-credential, mandatory-save and real stale-migration categories/codes; original stored-state/no-token assertions retained. |
| `Services/CredentialConcurrencyTests.cs` | Typed password outcome, explicit conflict and false data; newer-hash preservation retained. Renamed the old bool-result test to describe conflict. |
| `Services/AuthenticationLoggingTests.cs` | Typed outcomes and explicit classifications; existing password/hash/replacement secrecy assertions retained. |

Static authored count for the three new suites:

| Suite | Test methods | Facts + InlineData cases |
|---|---:|---:|
| ApiSafeErrorTests | 14 | 52 |
| ApiErrorBoundaryTests | 3 | 23 |
| ApiValidationContractTests | 4 | 23 |
| **Total** | **21** | **98** |

The 98 count treats each InlineData row as one case, without expanding loops inside test methods. Boundary cases additionally loop over missing-data/thrown exceptions and four incomplete nested-auth variants. This is a source count, not a discovered test count or pass result. Existing tests gained assertions but no new methods. Phase 1's backend success suite/helper and all Phase 2 UI code/tests remain unchanged by this continuation.

The real-service cases reuse private SQLite, deterministic seeding, real JWT middleware and the controllable dictionary handler. ApiSafeErrorTests uses the existing QuizApiCollection/base cleanup. Faults are armed after setup and disarmed before verification reads. Test-only fake services cover impossible controller invariants; they do not replace real service/persistence coverage. No new dependency or host architecture was introduced. SQLite fixtures do not establish SQL Server/IIS behavior.

## 7. Intentional existing expectation changes

- Vocabulary unrelated persistence failure: 400 → 500, with no saved row and no duplicate-success shortcut.
- Vocabulary favorite/preferred-definition missing or nonowned saved row: 400 → concealed 404 with the same code/message; invalid definition for an owned row remains 400.
- Quiz persistence rollback failure: 400 → 500; the original session retry and exactly-once learning-state assertions remain.
- Credential service tests now inspect `IsSuccess`, typed `Data`, `FailureType` and `Code`. Invalid credentials stay Unauthorized; mandatory replacement failures are InternalError; credential races are Conflict. No credential verification, no-token, logging secrecy or newer-hash assertion was removed.

Existing auth duplicate-registration 400, invalid-login 401, successful HTTP/JSON contracts, framework challenge tests and R3/R4/R5 protections remain applicable. Phase 2's adapter already recognizes the emitted codes, validation summary and fixed 500/503 messages; no UI edit was needed here.

## 8. Static completion review and deferred work

| Requirement | Static review result |
|---|---|
| Approved application envelope and helper | Implemented; expected pairs allowlisted, unknown/internal failures return fixed 500. |
| Validation ProblemDetails and safe field handling | Implemented through the registered factory; no raw ModelState exception serialization. |
| Registration exception safety and persistence distinction | Implemented and separately covered before/after successful persistence; no transaction redesign. |
| Typed auth/password outcomes and security-sensitive distinctions | Implemented; mandatory replacement failure prevents token generation; ordinary timestamp-only failure stays nonfatal; concurrency remains fatal/conflict. |
| Profile/validate-token and controller error handling | Existing success/lookup flow preserved; safe endpoint-specific failures and unexpected-error catches. |
| Word/quiz internal-failure groundwork | Implemented and backed by real-service fault assertions, including existing quiz rollback/retry checks. |
| Required success payloads | Controller guards present; null-data add fallback removed; authored boundary cases cover missing/invalid required auth data. |
| Successful contracts and framework Bearer challenges | Preserved in source; Phase 1 successes and existing security tests retained. |
| Provider structural validation and quiz session remediation | Not pulled forward; remain Phases 4 and 6. |
| Database/migrations, CI/CD, dependencies | No changed entity/context/migration/snapshot, workflow/release/deployment or project/package dependency files. |
| Phase 1/2 and authoritative documents | Preserved; continuation source hashes match for those files. |

Phase 5 still owns full typed vocabulary contracts, required favorite presence, full empty-search responses and page-local UI semantics. Phase 6 owns selected-option presence, null-answer handling, session/status/expiry coverage and remaining quiz contracts. Phase 7 owns OpenAPI/legacy cleanup and reference documentation. Phase 8 owns execution verification. No later phase was begun.

No static implementation blocker remains. Compiler errors, framework binding/serialization differences and runtime failures cannot be ruled out without the separately authorized execution phase. This record does not claim full R7 remediation or production readiness.

## 9. Verification restrictions and working-tree disposition

**NO TESTS WERE RUN. NO BUILDS WERE RUN. NO RESTORES WERE RUN.**

No test discovery, compiler, package installation, application startup, HTTP smoke test, live or local API/provider call, database/EF command, application/API/provider/database verification, CI script or deployment script was executed. HTTP/database actions in test source are authored future checks only. No prior run's results are used as current evidence.

Review used source reads/searches, file hashes and read-only Git status/diff, including `git diff --check`. Git's ordinary sandbox invocation encountered its existing ownership mismatch; explicit `--git-dir=.git --work-tree=.` paths supported the subsequent read-only checks without changing persistent Git configuration. The new files also received whitespace inspection because ordinary diff checks do not include untracked files.

All accumulated Phase 1–3 changes remain in the working tree, uncommitted and unstaged. No commits, pushes, merges, pull requests, rebases, resets, reverts, cleaning, file restoration, deployments or GitHub settings changes occurred. No pre-existing work was discarded. Work stops at Phase 3: **IMPLEMENTED — EXECUTION VERIFICATION PENDING**.

## Later verification — R7 Phase 8 (October 2, 2026)

The pending/no-execution statements above describe this phase at its original completion and remain historical evidence. The cumulative R7 implementation was subsequently execution-verified in Phase 8: final full backend suite **454 passed, 0 failed, 0 skipped**; final full Angular suite **193 passed, 0 failed, 0 skipped**; Release solution and Angular production builds passed. Phase 8 corrected anonymous Swagger serialization, stale owned-definition and SQLite timestamp assertions, and malformed XML documentation; see the complete failure history and warnings in [R7 Phase 8 verification results](R7-api-contracts-phase-8-verification-results.md).

Current cumulative status: **R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED**, within the local verification limits recorded there. Existing dependency vulnerabilities remain a separate release-review concern. No production access, live dictionary-provider calls, commit, push or deployment occurred. This later note does not claim execution took place during the original phase.
