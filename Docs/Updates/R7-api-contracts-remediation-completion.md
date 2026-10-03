# R7 — API Contracts Remediation: Completion Record

Record date: October 2, 2026.

## 1. Final status

**R7 API Contracts Remediation — COMPLETE**

R7 is implemented, locally execution verified, merged to master, CI verified, automatically deployed to production, and production smoke tested successfully. **No R7 release blocker remains.** The existing dependency-security findings are a separate follow-up and do not make R7 incomplete.

## 2. Executive summary

R7 aligned the maintained Angular client with the backend's actual transport contracts, standardized safe application failures while preserving automatic validation, and completed bounded provider, vocabulary and quiz contract remediation. Successful response shapes, the existing 14 routes and prior security/persistence protections were preserved. Typed responses, request-presence checks, stable error codes, accurate Swagger metadata and a durable contract reference now describe the same application behavior.

Phase 8 supplied local execution evidence and corrected the failures discovered during that execution. The implementation was subsequently committed and merged. GitHub Actions **CI / Publish Artifact #65** succeeded for master SHA **edeb68c**, including automatic production deployment to SmarterASP.NET. A subsequent manual production smoke test passed.

Evidence provenance: local implementation and verification results come from the linked R7 records; implementation and merge identities were checked through local read-only Git history. CI/CD, deployment and manual smoke-test evidence was supplied by the project owner for this completion record. No workflow logs or production endpoints were queried during document authoring. Local test counts are not presented as CI test counts.

## 3. R7 objective

Resolve the API-contract findings in the R7 analysis through the approved D1–D4 implementation decisions: compatible safe errors, appropriate status classifications, honest client transport types and bounded coordinated backend/frontend changes. Make requests, responses, validation, documentation and client behavior agree without redesigning the application or weakening previous remediation.

## 4. Scope completed

Completed scope includes successful-contract characterization; Angular authentication, error and transport alignment; backend safe failure classifications; provider structural validation; typed vocabulary acknowledgements and search responses; quiz presence and session classifications; dormant contract cleanup; OpenAPI/XML documentation; the 14-endpoint matrix and stable error-code catalog; and local execution verification with targeted corrections.

The maintained Angular application and backend were released together under the approved consumer assumptions. No new routes, API-versioning program, distributed client SDK, durable quiz-session architecture or broader normalization policy was required.

## 5. Phase-by-phase completion summary

| Phase | Completed contribution |
|---|---|
| 1 — Contract protection | Characterized successful raw JSON and Angular fixtures; protected existing wire shapes and prior invariants. |
| 2 — Client groundwork | Corrected nested auth responses, nullable fields and JSON date strings; removed the fictional `expiresAt` dependency; added typed API calls, success guards and shared safe error normalization. Signup's username maximum aligned to 100. |
| 3 — Safe backend errors | Introduced named application errors, sanitized validation extensions and typed failure classifications; preserved successful auth payloads and distinguished credential-write failures, concurrency and internal errors. |
| 4 — Provider boundary | Validated complete typed provider structures before tracking/persistence; separated genuine misses, unavailable/untrustworthy upstream responses and local failures. |
| 5 — Vocabulary contracts | Completed typed acknowledgements/list/search, explicit favorite presence, complete empty search, R5 identity protection and page-local UI/error behavior. |
| 6 — Quiz contracts | Completed option presence and answer validation, session 404/conflict 409 classifications and client recovery behavior while preserving R4 transaction/scoring protections. |
| 7 — Metadata and cleanup | Removed dormant DTO artifacts, retained approved legacy add fields, aligned response/security/nullability metadata and published the final contract reference and examples. |
| 8 — Execution verification | Executed local restore/build/test gates, corrected discovered Swagger/test/XML issues and passed the final backend and Angular suites and production build. |

Phases 1–7 originally completed as **IMPLEMENTED — EXECUTION VERIFICATION PENDING**. Their tests were not executed at those original checkpoints. Phase 8 subsequently verified the cumulative implementation. Phase 8's statements that no commit, push or deployment occurred describe that verification task; the later release evidence below records the subsequent commit, merge, CI/CD deployment and production smoke test. Existing historical records remain unchanged by this document.

## 6. Final API contract outcome

The final reference covers **14 endpoints**: five user/authentication operations, six word/vocabulary operations and three quiz operations. Successful auth responses retain the existing generic wrapper and nested `AuthResponse`; password change retains its existing acknowledgement. Word and quiz successes use named `SuccessResponse<T>` with `success` and typed `data`, preserving established JSON field names and null behavior rather than imposing a new universal success envelope.

Angular now models numeric IDs, JSON date strings and explicit nullable transport fields accurately. Login/register guards verify usable user/token data before establishing a session, and JWT expiry handling uses the token rather than a nonexistent server `expiresAt`. Request projection and typed generics constrain outgoing payloads. Shared normalization supports application errors, validation details, legacy failures and bodyless authentication challenges without exposing raw internal text.

The [contract reference](R7-api-contract-reference.md) is the authoritative endpoint matrix and catalog: **20 stable application error codes**, with `validation_failed` documented separately for automatic validation.

## 7. R5 identity preservation

Canonical UserWord identity remains **`(UserId, WordId)`**. POS is derived selection state, not an additional identity dimension. Duplicate add returns 200 with the same saved-row ID and `alreadyExisted:true`; existing duplicate-winner recovery remains intact. Preferred-definition changes update the same owner-scoped row, synchronize derived POS and preserve counters, notes, timestamps and dependents. Missing/nonowned saved rows remain concealed behind identical 404 responses.

## 8. Provider boundary outcome

Lookup remains DB-first. A genuine provider miss returns **404 `word_not_found`**. Transport failure, unsuccessful non-404 upstream status, malformed JSON or unusable/structurally invalid content returns **503 `dictionary_unavailable`** with safe public text. Valid upstream data followed by local persistence failure returns **500 `internal_error`**.

The entire provider structure is checked before canonical records are tracked, preventing partial persistence from malformed mixed content. Supported usable entries can still coexist with structurally valid unsupported content; optional omissions retain their existing nullable behavior. Deterministic provider-handler tests establish these local boundaries and cache behavior. They do not claim live-provider availability testing.

## 9. Vocabulary contract outcome

Lookup/add/list/search/favorite/preferred-definition successes have named types. Mutation acknowledgements carry the actual message, saved-row identifiers and relevant state, with client guards before success feedback.

Favorite requests require the JSON field: explicit `false` remains valid, while omission, null or malformed values fail automatic validation before mutation. Invalid definitions on an owned row return **400 `invalid_preferred_definition`**; missing/nonowned vocabulary rows return **404 `vocabulary_not_found`**.

Absent, empty or whitespace search terms return a complete typed 200 result: `words:[]`, `totalCount:0`, `page:1`, `pageSize:5`, `totalPages:0`. Normal search preserves the five-result cap and returned-match count. List pagination and defaults remain unchanged. Client labels make page-local filtering/counts explicit, and failed loads display an error/stale state rather than claiming a successfully empty collection.

## 10. Quiz contract outcome

`selectedOptionId` requires JSON presence; explicit zero remains valid, while omission/null/malformed values produce validation 400. Null answer elements are rejected safely before mutation. Omitted answers or an empty array remain valid unanswered submissions, scored under existing rules; an explicitly null answer collection fails validation.

Unknown, nonowned, expired or removed sessions return **404 `quiz_session_unavailable`**. In-progress/concurrent submission and recognized persistence duplicates return **409 `quiz_submission_conflict`**. Required vocabulary removed before or during submission returns **409 `quiz_vocabulary_changed`**. Unexpected failures return safe 500.

R4 transaction/execution-strategy rechecks, rollback, tracker cleanup, scoring, counters and session-lock release remain intact. Angular retains failed submission state, gives explicit recovery/restart guidance and introduces no automatic submission retry. Sessions remain process-local with the existing 30-minute lifetime; durable cross-instance sessions and new expiry-refresh behavior were outside R7.

## 11. Error and validation outcome

Application-generated failures use `application/json` with six fields: `success:false`, `data:null`, `error`, `message`, `code`, `traceId`. `error` and `message` contain identical safe catalog text. The server trace identifier is not a reflected caller header. Unexpected or unrecognized failures resolve to fixed **500 `internal_error`**, without exception/provider details.

Automatic validation remains **400 `application/problem+json`**, preserving ProblemDetails and field `errors` while adding compatible safe envelope extensions and `code:"validation_failed"`. Binding errors and unknown field paths are sanitized; attempted values and internal type/exception text are not exposed. Framework Bearer 401 challenges can remain bodyless, and routing/unsupported-media behavior remains framework-owned.

Authentication retains duplicate registration 400 and invalid credentials 401, adds explicit credential-concurrency 409 and safe internal 500 classifications, and preserves mandatory credential-write safety. Ordinary timestamp-only login persistence failure retains its approved nonfatal distinction. Successful auth payloads, credential security and token policy remain unchanged.

## 12. OpenAPI and documentation outcome

All 14 operations have aligned typed success/error metadata, required-field and nullability descriptions, and correct authentication requirements. Register/login are anonymous; the remaining operations, including fallback-authorized lookup, require Bearer authentication. Application JSON, validation ProblemDetails and possible bodyless Bearer challenges are distinguished.

Dormant DTOs and unused error artifacts were removed; active `ApiResult` types were relocated to the DTO namespace without changing factory serialization. Existing Swashbuckle was reused. XML output and safe HTTP examples support the durable contract reference, endpoint matrix and error catalog. Historical analysis and phase records were not rewritten as if earlier implementation had already executed.

## 13. Local execution-verification evidence

Phase 8 used **.NET SDK 10.0.401** targeting **.NET 8**, **Node 18.20.8** and **npm 10.8.2**. CI uses .NET SDK 8 and Node 22; local verification therefore does not claim identical toolchain execution. Subsequent workflow #65 supplies separate CI evidence.

| Final local gate | Recorded result |
|---|---|
| Backend restore | Passed. |
| Release solution build | Passed; 0 errors. Final incremental build reported 0 warnings. |
| Full backend suite | **454 passed, 0 failed, 0 skipped.** |
| OpenAPI coverage within backend suite | **33 metadata cases and 2 generated-document tests passed.** These are included in 454, not additional tests. |
| Targeted remediation rerun | **39 cases passed.** |
| `npm ci` | Passed after an authorized retry following an initial sandbox permission failure; lockfile/dependencies unchanged. |
| Full Angular suite | **193 passed, 0 failed, 0 skipped.** |
| Angular production build | Passed. |

The initial backend run had **449 passed and 5 failed**. Final passes followed the corrections below. Initial sandbox restrictions also prevented Angular test startup; the authorized retry succeeded. **No Angular remediation was required.**

Six existing xUnit analyzer warnings remain on full compilation; the final incremental build's zero-warning output does not imply their removal. Angular production-build warnings for redundant optional chaining and a component stylesheet budget remain recorded. These did not fail the completed gates. Local relational/provider tests use controlled infrastructure, not production SQL Server or live-provider validation. Full failure history and execution limits are in the [Phase 8 record](R7-api-contracts-phase-8-verification-results.md).

## 14. Phase 8 remediation discovered during execution

| Discovery | Correction |
|---|---|
| Anonymous Swagger serialization | An empty operation security list was omitted from generated JSON, leaving global Bearer security effective. The filter now emits `security:[{}]`, an anonymous alternative; metadata/generated-document assertions were aligned. Runtime authorization was unchanged. |
| Stale owned-definition 404 expectation | The owned-row invalid-definition test now expects the approved **400 `invalid_preferred_definition`**, preserving integrity assertions. Missing/nonowned rows remain 404. |
| SQLite timestamp comparison | Three valid-provider cases compared UTC `Z` text with SQLite's unspecified-kind serialization. Assertions retain complete property checks and exact non-date values while comparing parsed timestamp ticks, without changing runtime timestamp policy. |
| Malformed XML documentation | Query-string ampersands in the WordsController XML comment were escaped as `&amp;`, restoring valid generated XML documentation. |

These bounded corrections were followed by the targeted 39-case pass, full 454-case backend pass and final frontend/build passes. No test weakening or broader date-policy migration is claimed.

## 15. Git and merge evidence

| Release fact | Evidence |
|---|---|
| R7 branch | `remediation/r7-api-contracts`; pushed to origin before merge. |
| Implementation commit | **`8ca44f7`** — `Complete R7 API contracts remediation` (full SHA `8ca44f78d231e91623d84579f1607aaae7907ea4`). |
| Master merge commit | **`edeb68c`** — `Merge R7 API contracts remediation` (full SHA `edeb68cc0497571ab465253a520d063d37d5ff86`). |
| Merge/release sequence | Local `--no-ff` merge into master, then master pushed to origin. |

Local Git history confirms the implementation commit and the merge's two parents, `5d63e06` and `8ca44f7`. Push/release sequence is owner-supplied evidence. This documentation task performed no Git history or remote mutation.

## 16. CI/CD release evidence

Workflow **CI / Publish Artifact**, run **#65**, was triggered by **push to master** for **edeb68c**. Overall result: **SUCCESS**. Total duration shown: **5m 39s**.

| Job | Result |
|---|---|
| Backend / Integration Tests | Succeeded. |
| Frontend Tests | Succeeded. |
| Build / Publish Artifact | Succeeded. |
| Deploy to SmarterASP.NET | Succeeded. |

These results establish the automated CI/CD gates and artifact/deployment outcome. Individual CI test counts were not supplied and are not invented or inferred from local suite counts.

## 17. Production deployment evidence

The successful **Deploy to SmarterASP.NET** job deployed master SHA **edeb68c** to production automatically under the existing CI/CD configuration. **No manual production approval was required.** R7 required no workflow/deployment implementation change. Deployment-job success is recorded separately from the subsequent manual smoke test.

## 18. Production smoke-test result

**PASSED.** After deployment, a manual production smoke test successfully exercised authenticated production functionality and the primary R7 application path. The supplied evidence does not enumerate individual user actions, endpoint results or live-provider checks; none are asserted here.

## 19. Preserved R3/R4/R5/R6/PSH-1 protections

| Prior remediation | Preserved protection |
|---|---|
| R3 | Authentication/security protections, credential-write safety and concurrency safeguards. |
| R4 | Quiz transaction, rollback, scoring, counters, locking and concurrency protections. |
| R5 | Canonical `(UserId, WordId)` identity and same-row vocabulary selection semantics. |
| R6 | Relational integration-test infrastructure and isolated test database/provider fixtures. |
| PSH-1 | HTTPS/SSL/HSTS/CORS production hardening, with existing security configuration and middleware protections retained. |

## 20. Database and migration status

R7 required **no database migration, schema change or index change**. Existing entities, mappings and persistence invariants were preserved. Relational test execution is verification evidence, not a production migration or database deployment.

## 21. CI/CD implementation-change status

R7 required **no CI/CD implementation change**. Existing test, artifact publication and automatic production deployment configuration carried the release. No workflow, release guard or deployment script was changed by this documentation task.

## 22. Dependency status

R7 required **no dependency change**. Existing NuGet/npm versions and lockfiles were retained; Phase 8 installation used the existing dependency graph. XML documentation output and Swagger metadata changes did not add a dependency.

## 23. Known follow-up

Phase 8 reported **91 existing dependency vulnerabilities: 8 low, 24 moderate, 55 high and 4 critical**. These were **not introduced or remediated by R7** and require a separate dependency/security review. No dependency remediation is attempted or claimed here. This follow-up does **not** make R7 incomplete or constitute an outstanding R7 release blocker.

Previously deferred broader work remains outside this bounded remediation, including removal of retained deprecated add fields, global UTC timestamp redesign and durable cross-instance quiz sessions. This completion record does not imply those separate initiatives were delivered.

## 24. Legacy and deprecation notes

Legacy add fields were **retained according to the approved plan**, not removed. `definition`, `example` and `pronunciation` are accepted but documented as **ignored/deprecated**; clients do not author canonical dictionary content through them. **POS (`partOfSpeech`) remains active** as the existing selection fallback. For new adds, a valid explicit preferred-definition selection remains authoritative. Dormant DTO cleanup must not be confused with removal of these active accepted request fields.

## 25. Final acceptance assessment

The approved R7 contract remediation is complete across implementation, local execution verification, master integration, successful CI/CD, automatic production deployment and successful manual production smoke testing. All final local gates passed, all four run #65 jobs succeeded, and no R7 release blocker remains. Existing dependency-security findings retain a separate follow-up disposition.

This completion task created **only this document**, left untracked for review. Existing R7 records and the contract reference were not edited. No tests, builds, restores, npm commands, application execution, HTTP/provider calls, database commands, CI/CD execution or deployment commands were run. No code, configuration, database, migration, CI/CD or dependency files were changed. No staging, commits, pushes, merges, rebases, resets, reverts, cleaning or deployments occurred during document authoring.

## 26. References

- [R7 analysis](R7-api-contracts-remediation-analysis.md)
- [Approved R7 implementation plan](R7-api-contracts-remediation-implementation-plan.md)
- [Phase 1 — Contract protection](R7-api-contracts-phase-1-implementation-results.md)
- [Phase 2 — Angular groundwork](R7-api-contracts-phase-2-implementation-results.md)
- [Phase 3 — Safe backend errors](R7-api-contracts-phase-3-implementation-results.md)
- [Phase 4 — Provider boundary](R7-api-contracts-phase-4-implementation-results.md)
- [Phase 5 — Vocabulary contracts](R7-api-contracts-phase-5-implementation-results.md)
- [Phase 6 — Quiz contracts](R7-api-contracts-phase-6-implementation-results.md)
- [Phase 7 — OpenAPI and cleanup](R7-api-contracts-phase-7-implementation-results.md)
- [Phase 8 — Execution verification and remediation](R7-api-contracts-phase-8-verification-results.md)
- [Final API contract reference](R7-api-contract-reference.md)
