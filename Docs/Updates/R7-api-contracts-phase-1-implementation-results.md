# R7 Phase 1 — Contract Protection and Approved Baseline

Date: 2026-10-01

Status: **IMPLEMENTED — EXECUTION VERIFICATION PENDING**

This records Phase 1 only, under the approved D1–D4 decisions in the [implementation plan](R7-api-contracts-remediation-implementation-plan.md) and the [analysis](R7-api-contracts-remediation-analysis.md). It is not final R7 completion or runtime verification. Phase 2 has not begun.

## 1. Objective and implemented scope

Establish raw JSON protection for existing successful API contracts and preserved behavior before production remediation. Implement baseline B01/B02/B04/B06/B07 and U01/U06 through new backend integration tests, expanded Angular specs, and reviewed test-only fixtures. No production behavior was changed.

All 14 active routes have a deterministic successful fixture in the new backend suite. Backend setup uses the existing VocabularyAppWebApplicationFactory, private SQLite relational database, IntegrationTestSeeder, real registration/login/JWT helpers, and QuizApiCollection/QuizApiTestBase cleanup. Cached lookup asserts zero requests to the existing ControllableDictionaryHandler; existing provider-mapping coverage remains authoritative. There is no second host, database architecture, or external provider access.

## 2. Files changed

| File | Change |
|---|---|
| `VocabularyApp.WebApi.Tests/Infrastructure/JsonContractAssert.cs` | Added small raw JSON helper for 200/content type, exact unordered property sets, value kinds and success envelopes. |
| `VocabularyApp.WebApi.Tests/Integration/ApiContractShapeTests.cs` | Added successful contracts and preserved auth/vocabulary/quiz behavior. |
| `VocabularyApp.UI/src/app/services/api.service.spec.ts` | Expanded base URL, method/path/body/header, caller-encoded query, success-fixture and error-channel coverage. |
| `VocabularyApp.UI/src/app/services/auth.service.spec.ts` | Added current nested registration/login fixture characterization. |
| `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts` | Corrected lookup, nonempty-query search and preferred-definition response fixtures; isolated browser storage. |
| `VocabularyApp.UI/src/app/testing/api-contract.fixtures.ts` | Added test-only auth, lookup, vocabulary and quiz fixture factories; imported only by specs. |
| `Docs/Updates/R7-api-contracts-phase-1-implementation-results.md` | Added this Phase 1 record. |

Seven task files total: two backend test files, four Angular test/support files, and one document. Existing authentication, dictionary, ownership, quiz, security and infrastructure suites were not edited. Existing analysis, approved plan and backlog were not edited.

## 3. Backend tests added

Static count: **11 test methods, 23 cases** (5 Facts plus 18 InlineData cases across 6 Theories). These are authored counts, not discovery or execution results.

| Category | Cases | Protection |
|---|---:|---|
| Registration, login, profile, validate-token, password change | 5 | Nested AuthResponse; no expiresAt; required token/user; null LastLoginAt before login and date string after login; acknowledgement fields. Registration also exercises profile and validate-token with its real token before login. |
| Cached lookup | 2 | Exact outer/nested keys, numeric IDs, definition fields, nullable audio/pronunciation/example, cache flag, saved/unsaved membership flag, zero provider requests. |
| Vocabulary add/list/search/favorite/preferred lifecycle | 1 | New and repeated add, numeric WordId/UserWordId, alreadyExisted, stable identity, preference precedence, complete page/item fields, acknowledgements and persisted same-row cross-POS update. |
| Preserved legacy POS resolution | 5 | Missing/unknown POS falls back to Noun; case-insensitive POS names and abbreviation resolve existing definitions. |
| Preserved legacy fallback without a matching definition | 2 | Verb-only canonical word can be saved with Noun fallback, null preferred definition and current list placeholder; no invented canonical definition. |
| Quiz count/default/limit baseline | 4 | Nonpositive count uses 10, explicit 1 is preserved, large request caps at 20, eligible vocabulary limits actual count; omitted mode is mixed. |
| Quiz four-word minimum | 1 | Three usable words remain insufficient, with approved 400 and no results/counter mutation; error-body fields are not frozen. |
| Quiz start/submit/history | 3 | Word-to-definition, definition-to-word and mixed modes; GUID strings, option IDs 0–3, no answer-key fields, one correct plus one unanswered result, 50% score, null selectedAnswer, persisted results/counters and history. |

Existing `AuthenticationApiTests` already protects duplicate username/email registration as 400 and invalid/unknown credentials as 401 without a token. Those approved outcomes are reused unchanged. Existing malformed/expired/tampered/absent-token tests remain authoritative; new tests do not duplicate that matrix.

## 4. Angular tests and fixtures

Static count: **24 added cases**: 22 in ApiService and 2 in AuthService. Three existing WordLookup HTTP response fixtures were corrected without adding new component cases. Combined new backend/Angular count: **47 cases**, not executed.

- Twelve protected-route transport cases assert the environment base URL, exact method/path/query/body, JSON content header, Bearer header and full response pass-through. Profile, validate-token and password change are transport fixtures, not new production AuthService calls.
- Three token-absent GET/POST/PUT cases assert no Authorization header and consume a mocked empty 401 challenge.
- One query case confirms caller-encoded reserved characters are preserved without double encoding. ApiService itself does not construct or encode search terms; this does not claim component encoding coverage.
- Six supplied HTTP status cases (400/401/404/409/500/503) remain on the error channel. These do not assert that any particular backend operation should produce those statuses.
- Registration passes through the real nested AuthResponse shape and does not establish a session. Login receives the current response without expiresAt and stores its token/user using the existing keys. Storage is spied; no real browser session is inherited or modified by those cases.
- Test fixtures retain explicit nulls, nested flags, string dates/GUIDs, numeric IDs, vocabulary paging and quiz unanswered-result/history shapes. Fixed fixture tokens and IDs are test data, not assertions against generated backend values.

The new fixture file is test infrastructure, not a production transport model. Static import inspection found references only in the three modified specs; tsconfig.app.json starts from main.ts. No production TypeScript, template, tsconfig or package file was edited.

## 5. Successful shapes protected

| Routes | Exact successful structure |
|---|---|
| POST register/login | `success,data,error`; nested `success,errorMessage,user,token`; UserDto `id,username,email,createdAt,lastLoginAt`. No expiresAt. |
| GET profile/validate-token | `success,data,error`; UserDto with required null/date fields. |
| POST change-password | `success,message,data,error`, including null data/error and current message. |
| GET lookup | `success,data`; nested `success,errorMessage,word,wasFoundInCache,isInUserVocabulary`; WordDto and each WordDefinitionDto exact keys. |
| POST vocabulary/add | `success,data`; `userWordId,wordId,alreadyExisted,message`, for new and repeated adds. |
| GET vocabulary and normal nonempty search | `success,data`; `words,totalCount,page,pageSize,totalPages`; all 14 UserVocabularyItemDto fields, including explicit nulls and zero counters. |
| PUT favorite | `success,data`; `message,userWordId,isFavorite`, for true and false. |
| PUT preferred-definition | `success,data`; `message,userWordId,preferredWordDefinitionId`. |
| POST quiz/start | `success,data`; session/mode/count/expiry/questions, each question's four fields and each option's two fields; no answer key. |
| POST quiz/submit | `success,data`; total/correct/percentage/questionResults; six result fields including selectedAnswer:null when unanswered. |
| GET quiz/history | `success,data`; `items`; timestamp and three score fields, without session IDs. |

Property order, literal generated GUIDs/JWTs, trace values and unstable timestamp values are not asserted. Dates are required parseable JSON strings, without inventing a uniform stored UTC suffix. Null-versus-omitted fields are asserted explicitly.

## 6. R3/R4/R5/R6 invariants

- **R3:** Real JWT-authenticated requests; exact UserDto keys exclude credential/hash fields; canonical dictionary data is not authored by ignored legacy add fields. Existing security, credential-migration, challenge and ownership suites remain untouched.
- **R4:** No answer leakage before submission; current modes/count/minimum rules; unanswered questions count incorrect; one correct/one unanswered produces two persisted results and aggregate attempts=2/correct=1. Existing rollback, retry, concurrency, counter/date and user-isolation tests remain authoritative and unchanged.
- **R5:** `(UserId, WordId)` is the saved identity. Repeated add returns the same row before applying either a different valid preference or an invalid new preference. Explicit preferred ID takes precedence over submitted POS. Cross-POS preference update retains that UserWord ID; no duplicate row, canonical overwrite or counter reset is introduced. Existing dependent-record and concurrency protections remain unchanged.
- **R6:** Private SQLite host, deterministic seeding, real relational constraints, scoped reads and disposal; quiz static sessions use existing collection cleanup. No infrastructure replacement or mock-only persistence architecture.

These are assertions authored/preserved in source, not claims that they passed during this task. SQLite tests cannot validate SQL Server migrations, collation, SQL Server-specific error codes, or deployed IIS behavior.

## 7. Legacy behavior and later status changes

The new POS tests explicitly name current legacy behavior preserved by D3. Duplicate registration 400, invalid credentials 401, duplicate add 200 and insufficient quiz vocabulary 400 are approved preserved behaviors.

No new backend test establishes an obsolete internal-failure, provider-malformation, nonowned-resource or unavailable-session status as final R7 behavior. Existing legacy assertions remain intact for their later planned updates:

- `VocabularyOwnershipApiTests`: unrelated persistence failure 400 becomes 500; missing/nonowned saved-row mutation 400 becomes concealed 404; invalid definition remains 400.
- `QuizApiTests`: rollback failure 400 becomes 500 while rollback/retry checks remain; unknown/nonowned/removed session 400 becomes 404; detectable conflicts become 409, subject to the documented concurrent removal race.
- Auth fatal persistence/concurrency classification is Phase 3; malformed provider structures are Phase 4.

Empty/omitted/whitespace search binding is deliberately not frozen. D2's full typed empty 200 target and runtime characterization remain Phase 5/future execution work. The corrected Angular empty-result fixture is for a **nonempty** search with no matches, whose current response already has complete metadata.

## 8. Findings and plan items addressed

Phase 1 delivers the baseline part of **F12/F13**, protecting successful shapes and transport fixtures through **B01/B02/B04/B06/B07, U01/U06**. It supplies protection for **F05** auth alignment, **F06** successful envelopes, **F08** preserved add semantics, and **F14** quiz result/session wire representation and scoring. No finding is claimed fully remediated across all R7 phases.

## 9. Discrepancies and work deferred to Phase 2+

No Phase 1 portion required production changes; no implementation blocker remains. Static inspection reconfirmed these mismatches:

1. Frontend RegisterResponse incorrectly declares data as User. The server returns nested AuthResponse. The baseline uses the real fixture through HttpTestingController and observes it as unknown; production model correction remains Phase 2.
2. LoginResponse requires expiresAt, which the backend does not serialize. The setSession parameter is unused, so the honest fixture can characterize current runtime handling without a production edit. Removing the assumption and adding malformed-success guards remains Phase 2.
3. User dates are declared Date/optional on the frontend but arrive as strings and explicit null; word/vocabulary models also omit actual null possibilities. Fixtures do not cast away those mismatches. Transport model alignment remains Phase 2/5.
4. The prior lookup fixture omitted required WordDto/definition/errorMessage fields; normal-search fixture omitted paging; preferred-definition acknowledgement omitted message. Those test fixtures were corrected. Directly constructed UI view-model samples remain view-model tests, not purported HTTP DTOs.

The shared error adapter, new error/validation envelopes, safe server failure classifications, provider validation, mutation-field presence, empty-search behavior, page-local UI changes, quiz session-status/expiry changes, full component request/error coverage, OpenAPI/cleanup and execution verification remain their approved Phase 2–8 work. No api-error.ts or quiz component spec was introduced. No expired-session seam was added to production.

## 10. Test, build and restore execution status

**NO TESTS WERE RUN. NO BUILDS WERE RUN. NO RESTORES WERE RUN.**

No test host discovery, compiler, package install, application startup, HTTP smoke test, provider call, database command/query, EF command, application/API/provider/database verification, CI script or deployment script was executed. Database reads/writes and HTTP requests visible in the new tests are authored test code only. No historical test/coverage result is used as R7 evidence.

Review consisted only of source/file inspection, text searches, and read-only Git status/diff. Compilation and runtime results remain unknown until separately authorized execution.

## 11. Git state and operations

Entry status had no tracked modifications and these pre-existing untracked items: `Docs/Backlog/`, `Docs/Updates/R7-api-contracts-remediation-analysis.md`, and `Docs/Updates/R7-api-contracts-remediation-implementation-plan.md`. They were preserved.

Ordinary Git status reported an ownership/safe-directory error; command-scoped safe.directory attempts did not resolve it. Read-only `git --git-dir=.git --work-tree=. status --short` and corresponding diff inspection succeeded. No persistent Git configuration was changed. The task adds the seven files/changes listed above; three are modified tracked specs and four are new files.

No staging, commits, pushes, merges, rebases, deployments or history edits occurred. No pre-existing user changes were discarded.

## 12. Final static scope review and readiness

Source review against the approved plan confirms Phase 1 only; production C#/TypeScript/templates/configuration/middleware/auth/provider code untouched; no entity/context/database/migration/snapshot changes; no CI/CD/release/deployment changes; no new permanent obsolete error-status expectation; R5 identity remains `(UserId, WordId)`; existing security/rollback/concurrency infrastructure remains intact.

**IMPLEMENTED — EXECUTION VERIFICATION PENDING.** The Phase 1 test-code baseline and documentation are ready for owner review, but are not compiled, tested, or runtime-verified. Work stops here; Phase 2 has not begun.

## Later verification — R7 Phase 8 (October 2, 2026)

The pending/no-execution statements above describe this phase at its original completion and remain historical evidence. The cumulative R7 implementation was subsequently execution-verified in Phase 8: final full backend suite **454 passed, 0 failed, 0 skipped**; final full Angular suite **193 passed, 0 failed, 0 skipped**; Release solution and Angular production builds passed. Phase 8 corrected anonymous Swagger serialization, stale owned-definition and SQLite timestamp assertions, and malformed XML documentation; see the complete failure history and warnings in [R7 Phase 8 verification results](R7-api-contracts-phase-8-verification-results.md).

Current cumulative status: **R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED**, within the local verification limits recorded there. Existing dependency vulnerabilities remain a separate release-review concern. No production access, live dictionary-provider calls, commit, push or deployment occurred. This later note does not claim execution took place during the original phase.
