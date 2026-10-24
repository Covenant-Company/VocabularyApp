# R7 Phase 7 implementation results

Status: **IMPLEMENTED — EXECUTION VERIFICATION PENDING**.

Phase 7 is statically implemented. R7 is not execution-verified or complete. **Execution verification is deferred to R7 Phase 8.**

## 1. Objective

Complete approved schema metadata, legacy contract cleanup and durable contract documentation after Phases 1–6, preserving established runtime semantics. Authority: [approved plan](R7-api-contracts-remediation-implementation-plan.md), especially sections 14–19 and 27; decisions D1–D4 remain approved.

## 2. Scope and reconciliation checklist

Before editing, inspected current working-tree status and complete cumulative diff; analysis/plan; results Phases 1–6; all controllers, active/dormant DTOs, ServiceResult, errors, validation, provider integration, Angular models/ApiService/consumers, Swagger setup and authored tests. Current source controlled the review, rather than old endpoint examples.

The exact section 19 Phase 7 assignment was reconciled as follows:

- [x] F06/F08/F14/F15/F16 cleanup: reference-check section 15 declarations; remove only proven unused types and the throwing overload.
- [x] Relocate active generic ApiResult to DTOs, align non-generic namespace, update TF (`ApiTestClientHelper`) import; preserve JSON/factories.
- [x] Annotate all 14 actions with current typed 200 schemas and only reachable application/validation statuses.
- [x] BOOT Swagger correction: explicitly override global security on AllowAnonymous; retain authentication metadata on all 12 protected actions, including fallback lookup. Runtime policy untouched.
- [x] Describe actual error variants, codes, required/nullability semantics and defaults using the existing Swagger stack.
- [x] Correct DOC (`Docs/README.md`, root `test-api.http`, project HTTP file) current routes/examples; no nonexistent notes/sample/delete/profile-write routes.
- [x] Durable final R7 reference and Phase 7 results; preserve historical analysis/roadmap/results.
- [x] B01 success coverage retained; B11 metadata/Swagger coverage authored. Execution/reference/build checks deferred to Phase 8.
- [x] Cross-layer/source review: no canonical-write route added; no wire redesign; active Angular DTOs preserved.

Earlier phases already resolved typed auth, safe error classification, binding sanitization, provider 404/503 boundaries, typed vocabulary/quiz responses and presence/mutation safeguards. These were reviewed, not reimplemented. Ignored add fields are intentionally retained by plan sections 14/27, despite a Phase 5 note suggesting a Phase 7 cleanup point. Broader changes remain beyond R7.

## 3. Backend production files changed in Phase 7

Modified (paths relative to repository):

- `VocabularyApp.WebApi/Controllers/UsersController.cs`: response metadata, XML remarks, remove relocated generic wrapper declaration.
- `VocabularyApp.WebApi/Controllers/WordsController.cs`: typed response/status metadata and current XML remarks/comment cleanup.
- `VocabularyApp.WebApi/Controllers/QuizController.cs`: typed response/status metadata and current XML remarks.
- `VocabularyApp.WebApi/DTOs/ApiResult.cs`: DTO namespace, moved generic wrapper, remove unused throwing overload.
- `VocabularyApp.WebApi/DTOs/WordDTOs.cs`: remove dormant WordLookupRequest only.
- `VocabularyApp.WebApi/Program.cs`: Swagger-only registrations, nullable reference support, reference schema extension and XML inclusion.
- `VocabularyApp.WebApi/VocabularyApp.WebApi.csproj`: emit XML documentation; suppress missing-public-comment warning 1591. Package/project references unchanged.

Added:

- `VocabularyApp.WebApi/Swagger/ApiContractOperationFilter.cs`.
- `VocabularyApp.WebApi/Swagger/ApiContractSchemaFilter.cs`.

Deleted:

- `VocabularyApp.WebApi/DTOs/UserWordDTOs.cs`.
- `VocabularyApp.WebApi/DTOs/WordRequest.cs`.
- `VocabularyApp.WebApi/DTOs/ApiResponse.cs`.

Also updated developer documentation `Docs/README.md`, `test-api.http`, `VocabularyApp.WebApi/VocabularyApp.WebApi.http`; added this results document and the contract reference. These inventories describe Phase 7 changes only: the cumulative working tree also contains preserved Phase 1–6 and unrelated work.

## 4. Angular production files changed

**None in Phase 7.** Current named auth/word/vocabulary/quiz transport types and calls already align. All existing Angular changes from earlier phases remain intact.

## 5. DTO inventory and removals

Static C# reference searches covered production/tests, excluding generated bin/obj. Removed only declaration/intra-file-only types authorized by section 15:

| Removed artifact | Evidence / disposition |
|---|---|
| UserWordDto | Referenced only by dormant collection DTO; pre-R5 identity artifact |
| AddWordToCollectionRequest | No action/service binder/caller |
| UpdateUserWordRequest | No action/service caller; no feature introduced |
| UserWordCollectionResponse | Dormant wrapper; removed with UserWordDTOs.cs |
| WordLookupRequest | No caller; remove class without deleting active WordDTOs.cs |
| WordRequest, DefinitionDto | Dormant owner/nested type only; remove file |
| DTOs/ApiResponse.cs | Entirely commented, no compiled type; Angular ApiResponse retained |
| ApiResult.ErrorResult(object) | Unused internal throwing overload; no callers |

Active generic/non-generic ApiResult retained in DTOs with unchanged wire properties and string factories. Active CreateUserRequest/LoginRequest/UserDto/AuthResponse, controller-local ChangePasswordRequest, AddWordRequest, WordDto/WordDefinitionDto/WordLookupResponse, all UserVocabularyDTOs and QuizDTOs, SuccessResponse and ApiErrorResponse retained. Provider DTOs remain internal active integration contracts. ServiceResult remains internal classification data, not a public HTTP envelope. No new public validation DTO was introduced; the extended schema is metadata-only.

## 6. Legacy AddWordRequest cleanup

No fields removed. Approved plan explicitly retains definition/example/pronunciation as ignored compatibility inputs and POS as a used selection fallback. The first three are marked deprecated in Swagger and documented as unable to author canonical content. Angular payloads remain accepted. Preferred definition semantics are unchanged; duplicate rows are returned before selection validation. Future field removal requires separate approval beyond R7.

## 7. R5 identity artifact cleanup

Removed the dormant pre-R5 DTO family and corrected current API documentation. Identity remains `(UserId, WordId)`. POS remains valid synchronized derived state. No entity, unique index, context mapping or R5 implementation was edited. Historical migration/analysis documents remain historical.

## 8. OpenAPI metadata changes

All 14 action successes are named typed schemas. Application-classified errors use ApiErrorResponse. 400 binding errors use extended ValidationProblemDetails with problem+json; actions with business 400 advertise both media types. Validation-only 400 annotations use ValidationProblemDetails rather than stale success wrappers. No generic 400 was invented for the optional-string search action.

Lookup advertises genuine miss 404, provider unavailable 503, safe 500 and bodyless Bearer 401. Auth includes credential conflicts 409 where reachable; quiz submit includes its 404/409 distinctions. All controller error branches remain unchanged. OpenAPI security is explicitly empty for register/login, and Bearer for the other 12 actions. The existing security definition/global requirement is retained. XML inclusion and nullable support use installed Swashbuckle 6.8.1 APIs; no dependency added. The generated Swagger document was not produced or inspected.

## 9. XML/comment cleanup

Every action has current contract remarks; stale response comments and the outdated Angular-example add comment were removed/corrected. XML is configured for future builds and loaded by Swagger. Current README no longer claims obsolete profile-write/search/definitions routes or a free-provider public contract. Both HTTP files contain all 14 current actions, safe local placeholders, explicit false/zero examples and distinctions between AE, VE and empty challenges. No examples were executed. Existing security guidance/evidence was preserved.

## 10. Auth documentation

Reference documents nested register/login success, nullable reusable DTO declarations versus stronger successful guards, UserDto fields, null lastLoginAt on registration, profile/validate-token return shape, non-generic password-change success, required input annotations, invalid credentials/account/password errors, safe 500 and credential-race 409. No token expiry field invented and no JWT/runtime auth changes.

## 11. Word/provider documentation

Canonical DB-first lookup, typed public data, 404 genuine miss versus 503 unavailable/untrustworthy provider and 500 persistence failure are documented. Upstream typed transport remains an internal boundary; no provider payload/diagnostic is public schema data.

## 12. Vocabulary documentation

Reference covers typed acknowledgements, duplicate 200, same-row identity/derived POS, canonical add prerequisite, ignored legacy inputs, explicit favorite presence/false, concealed missing/nonowned 404, preferred-selection validation, full typed list/search response, relevance ordering, bounded search count and absent/empty/whitespace full empty 200.

## 13. Quiz documentation

Reference covers defaults/count normalization, answer-key-free start, 30-minute process-local sessions, explicit selectedOptionId/valid zero, omitted/empty/null answers, null elements and duplicate/foreign questions, scoring/result nullable selectedAnswer, history shape without sessionId, unavailable 404 and precise conflict 409 meanings. Completed removal can make later replay 404; permanent 409 is not promised. Existing transaction, recheck, rollback, tracker cleanup, locks/counters/expiry remain intact. No automatic retry introduced.

## 14. Nullability/required metadata

Response properties are marked present independently of nullable values, including nullable dates/selectedAnswer. Existing `[JsonRequired]` input properties are explicitly required and non-nullable in schemas. Answers remains optional with default [], but non-null when supplied. Start defaults are described without turning CLR defaults into required JSON input. Reusable nested nullable auth/lookup fields remain declared nullable; successful guard invariants are documented. Date/time transport stays strings; no global UTC rewrite.

## 15. Angular alignment review

Traced methods/routes, auth headers, encoded lookup/search queries, request bodies, typed acknowledgements, list/search and quiz responses, nullable fields, string dates and shared error handling. AuthService retains its guarded storage behavior; ApiService consumers remain generic and typed. No scoped active any replacement was needed; unrelated UI types were left alone. No nonexistent API inferred from the generic DELETE helper.

## 16. Tests changed/added (not executed)

- Modified `VocabularyApp.WebApi.Tests/Infrastructure/ApiTestClientHelper.cs`: remove controller import after wrapper relocation; existing DTO import resolves it. No behavioral assertion changed.
- Added `VocabularyApp.WebApi.Tests/Integration/ApiContractMetadataTests.cs`: reflection/filter checks for all 14 typed successes, 500 AE metadata, explicit anonymous/protected security, error media distinctions, bodyless lookup challenge, required value fields and nullable response presence.
- Added `VocabularyApp.WebApi.Tests/Integration/OpenApiContractTests.cs`: B11 authored future host/Swagger tests for exact 14 paths/methods, auth security overrides including fallback lookup, 200/500 schemas, absence of provider schemas, validation variants and required/nullable/deprecated properties. This test source contains future HTTP execution; it was not run during Phase 7.

Existing ApiContractShapeTests (B01), security action inventory, safe errors, validation/provider/vocabulary contracts and Phase 6 quiz tests retain behavior expectations. No test framework or packages changed.

## 17. Final reference

[R7-api-contract-reference.md](R7-api-contract-reference.md) is the durable reference, with all 24 required topic sections, exact envelope/extensions, consumers, limitations and pending verification statement.

## 18. Endpoint matrix

Complete source-reviewed E01–E14 matrix is in reference section 9. Each row records method, route, auth, request, 200 type, validation/application statuses/codes, Angular consumer and compatibility notes. Source route tokens can capitalize controller names in generated Swagger; public route matching remains case-insensitive. Framework routing/media-type responses are not falsely described as AE.

## 19. Error-code catalog

Reference section 14 lists all **20 application codes** from ApiErrorResults, plus separate `validation_failed` VE code, with HTTP status/meaning/endpoint categories. No invented or leaked provider codes. All application errors preserve six fields and server trace ID; safe messages remain centralized.

## 20. Expectations intentionally changed

Metadata/docs now describe actual R7 runtime: anonymous auth security override, authenticated lookup, typed successes, application/validation/bodyless challenge variants, provider 404/503 and quiz 404/409, retained deprecated add fields and no obsolete routes. DTO source namespaces move to DTOs. No established runtime payload, status/code, validation/scoring/mutation or existing behavioral test expectation was changed by Phase 7.

## 21. Deferred work

Phase 8: execute all approved restore/build/backend/Angular checks, actual Swagger/schema verification, XML output and applicable regression/publish review. Beyond R7: ignored add-field removal, universal envelope redesign, generated clients, broader UI typing, global UTC normalization, durable quiz coordination/replay, provider/cache architecture and SQL Server infrastructure verification. No Phase 8 work begun.

## 22. Blockers/discrepancies

No static implementation blocker. Phase 5 wording suggested ignored add-field cleanup in Phase 7, but approved plan sections 14/27 explicitly retain/defer it; fields retained and discrepancy documented. Some old analysis/results intentionally describe pre-remediation behavior; they were not rewritten. Generated OpenAPI/build/runtime evidence remains unavailable by explicit execution restriction, not claimed as passed. Reusable nullable DTO declarations and non-null successful guards are intentionally distinguished.

## 23. Test execution status

**NO TESTS WERE RUN**, including filtered/single tests, backend, Angular, PowerShell or CI equivalents. Authored source was inspected only.

## 24. Build/restore/install status

**NO BUILDS WERE RUN. NO RESTORES OR NPM INSTALLS WERE RUN.** No compilation, TypeScript execution, publish, app startup, local/production HTTP/API request, provider/DB call, Swagger generation or equivalent runtime verification occurred.

## 25. Database/migrations

No Phase 7 database/entity/index/context/migration/snapshot changes; no EF/database commands. R5/R6 schema and persistence logic preserved.

## 26. CI/CD/dependencies/security

No Phase 7 CI/CD/workflow/deployment script/release guard changes; no NuGet/npm dependency or lockfile changes. No environment URLs, secrets/configuration, HTTPS/HSTS/CORS/forwarded headers/JWT policy or middleware-order changes. Program edits are limited to Swagger metadata. Project XML output is a metadata setting, not a package change.

## 27. Git/static operations

Only read-only status/diff/source inspection and static whitespace checking; no staging, commits, pushes, merges, rebases, resets, reverts, cleaning or deployments. Entry hashes distinguish Phase 7 changes from the inherited cumulative working tree; existing services, active request DTOs, error/validation helpers, Angular, data, security, dependency and CI/CD files were preserved. `git diff --check` passed with no output. No branch/history changes or test artifacts created by execution.

## 28. Phase 8 handoff

Future verification must execute authored B01/B11 and all Phases 1–6 behavioral/regression suites, confirm emitted XML availability, inspect actual Swagger exactly 14 actions with explicit anonymous overrides and fallback lookup security, typed success schemas, correct error statuses/media, nullable/present fields and required false/zero semantics. Confirm no canonical-write/dormant routes, wrapper namespace compilation, generated nullable schemas, preserved public JSON and clean package/migration/security/CI scope. Record real outcomes separately; SQLite/test-host results cannot establish SQL Server/IIS production behavior. No deployment is authorized by this handoff.

## 29. Readiness assessment

**IMPLEMENTED — EXECUTION VERIFICATION PENDING**. Static Phase 7 cleanup, metadata and documentation are complete and consistent with the approved bounded scope. Phase 1–6 runtime semantics and pre-existing work remain preserved. R7 is ready for separately authorized Phase 8 verification, with no claim of test/build/runtime success or R7 completion.

## Later verification — R7 Phase 8 (October 2, 2026)

The pending/no-execution statements above describe this phase at its original completion and remain historical evidence. The cumulative R7 implementation was subsequently execution-verified in Phase 8: final full backend suite **454 passed, 0 failed, 0 skipped**; final full Angular suite **193 passed, 0 failed, 0 skipped**; Release solution and Angular production builds passed. Phase 8 corrected anonymous Swagger serialization, stale owned-definition and SQLite timestamp assertions, and malformed XML documentation; see the complete failure history and warnings in [R7 Phase 8 verification results](R7-api-contracts-phase-8-verification-results.md).

Current cumulative status: **R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED**, within the local verification limits recorded there. Existing dependency vulnerabilities remain a separate release-review concern. No production access, live dictionary-provider calls, commit, push or deployment occurred. This later note does not claim execution took place during the original phase.
