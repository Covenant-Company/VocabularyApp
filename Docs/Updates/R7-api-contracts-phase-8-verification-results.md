# R7 Phase 8 verification results

**R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED**

Local verification completed October 2, 2026 (America/New_York). Final required gates passed: **454 backend tests, 193 Angular tests, Release solution build and Angular production build**. No deployment was performed. Production/IIS/SQL Server acceptance and dependency-security approval are separate release concerns.

## 1. Objective

Execute and remediate the cumulative, uncommitted R7 Phases 1–7 implementation against approved decisions D1–D4 and the [implementation plan](R7-api-contracts-remediation-implementation-plan.md). Preserve established contracts, security, vocabulary identity and quiz transaction/concurrency guarantees. No new features or redesign.

## 2. Environment and tool versions

| Tool | Actual local version / use |
|---|---|
| `dotnet --version` | **10.0.401**, default installed SDK; no global.json or machine configuration added |
| Target/test framework | **net8.0**; VSTest reports `.NETCoreApp,Version=v8.0` |
| `node --version` | **v18.20.8** |
| `npm --version` | **10.8.2** |
| Chrome | Installed product version **154.0.8037.93**; Karma reports Chrome Headless 154.0.0.0 (Windows 10) |
| Karma | **6.4.4** |

CI uses .NET SDK 8 and Node 22. Local default SDK 10 built the unchanged .NET 8 targets, and Node 18.20.8 meets the installed Angular CLI engine range (`^18.19.1 || ^20.11.1 || >=22.0.0`). These results establish local verification with the versions above, not exact CI toolchain parity. No SDK/runtime/npm upgrade or global configuration change was made.

## 3. Baseline and safety/isolation checks

Before verification, inspected status/diff, authoritative analysis/plan, Phases 1–7 records and contract reference. Captured **256 entry-file hashes** to distinguish remediation from the existing cumulative changes. Preserved uncommitted R7 and unrelated pre-existing work.

`VocabularyAppWebApplicationFactory` and `RelationalDatabaseFixture` replace SQL Server with private in-memory SQLite connections; test infrastructure creates/disposes only its own databases. The factory replaces the dictionary primary handler with `ControllableDictionaryHandler`; registered responses/exceptions are deterministic, and unmatched requests fail within the handler without network access. Test HttpClients are TestServer clients. Production-shaped hostnames in HTTPS assertions therefore do not contact production. Frontend tests use Angular HTTP testing/mocks.

No production access, live dictionary-provider request, production credential, EF migration operation or manual database script was used. Package-registry access was limited to the authorized dependency installation. No CI/deployment script or workflow was executed.

## 4. Backend restore

Executed from repository root:

```powershell
dotnet restore VocabularyApp.sln
```

**Passed**, exit 0: all projects up to date. No NuGet vulnerability-metadata or restore warning/error was reported. No package/version/lockfile changes.

## 5. Initial backend build

```powershell
dotnet build VocabularyApp.sln --configuration Release --no-restore
```

**Passed**, exit 0; **0 errors, 9 warnings**, elapsed **13.69 seconds**. Data, API and test projects compiled. Warnings: three CS1570 malformed XML entity warnings in the vocabulary query example, five xUnit2031 assertion-style warnings and one xUnit1026 unused theory parameter warning. DTO relocation/removal, filters and registrations compiled; XML documentation was emitted.

## 6. Initial full backend test result

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release --no-build --no-restore --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=r7-phase8-initial.trx' --results-directory artifacts/r7-phase8
```

**454 total/executed: 449 passed, 5 failed, 0 skipped**. Exit 1; runner duration **50 seconds**. TRX start/finish: 21:15:33–21:16:26 local. Initial failures were retained in the verification history below; passing targeted tests were not treated as final acceptance.

## 7. Backend failures discovered

| Failure / instances | Classification | Root cause |
|---|---|---|
| `OpenApiContractTests.GeneratedDocumentContainsExactlyTheFourteenCurrentActionsAndSecurityOverrides` (1) | R7 metadata defect | Microsoft.OpenApi serialization omitted the empty operation security list. With a global Bearer requirement, omission failed to override it for anonymous register/login. Generated test failed reading the absent security property. |
| `VocabularyOwnershipApiTests.MissingVocabularyAndDefinitionIdsFailWithoutMutation` (1) | Stale R7 expectation | An invalid definition ID on an owned vocabulary row was incorrectly expected to become concealed 404. Approved plan retains 400 `invalid_preferred_definition`; concealed 404 applies to missing/nonowned vocabulary rows. |
| `DictionaryProviderContractTests.ValidMixedContentPreservesPublicShapePersistenceAndCache`, optionalMode 0/1/2 (3) | R7 test-fixture/assertion defect | Whole-object raw JSON equality assumed the freshly created UTC DateTime's `Z` suffix would survive SQLite materialization. SQLite preserves timestamp value while returning unspecified DateTimeKind. Global UTC normalization is explicitly deferred. |
| Build CS1570 warnings (3 diagnostics) | R7 XML metadata correctness issue | Enabling XML documentation exposed unescaped `&` query separators. This prevented a useful generated member comment. |

No R7 compile error, provider-network escape, credential-security failure, quiz mutation regression or database-isolation failure was discovered.

## 8. Backend remediation and targeted rerun

Narrow corrections:

- Anonymous operations now contain one empty `OpenApiSecurityRequirement`, serialized as **`security: [{}]`**. This standard anonymous alternative overrides global Bearer metadata and survives the existing serializer. Protected operations retain Bearer; runtime authentication is unchanged. Reflection/generated-document expectations now assert the empty requirement object.
- Owned-row invalid definition expectation now asserts HTTP 400, `invalid_preferred_definition` and its safe public message. Missing favorite/vocabulary-row 404 and all no-mutation assertions remain.
- Cache test checks the complete property set and every non-date field exactly; createdAt remains a JSON string and its parsed timestamp ticks must match. It no longer invents a UTC-suffix guarantee. Provider-call count and persistence/cache assertions remain.
- Escaped query-example separators as `&amp;`; the generated XML now includes `GetUserVocabulary` documentation. No controller execution code changed.

Executed:

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~OpenApiContractTests|FullyQualifiedName~ApiContractMetadataTests|FullyQualifiedName~MissingVocabularyAndDefinitionIdsFailWithoutMutation|FullyQualifiedName~ValidMixedContentPreservesPublicShapePersistenceAndCache' --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=r7-phase8-targeted.trx' --results-directory artifacts/r7-phase8
```

Recompiled affected projects, then **39 passed, 0 failed, 0 skipped**, runner duration **3 seconds**, exit 0. XML warnings disappeared; six informational xUnit warnings remained. No existing runtime contract was undone to obtain green results.

## 9. Final full backend test result

Repeated the full initial command with `LogFileName=r7-phase8-final.trx` after remediation and initial frontend verification.

**454 total/executed: 454 passed, 0 failed, 0 skipped**, exit 0, runner duration **49 seconds**. TRX start/finish: **21:24:36–21:25:27** local. This is the authoritative backend result.

Final TRX groups include 33 metadata tests, 2 generated OpenAPI tests, 23 successful wire-contract tests, 23 error-boundary tests, 52 safe-error tests, 23 validation tests, 14 dictionary lookup tests, 46 provider-contract tests, 25 vocabulary-contract tests, 17 vocabulary-ownership tests, 53 quiz tests and 24 HTTPS-hardening tests. These are runner counts, including parameterized cases, not static estimates. The complete suite also executed credential/password, R3 security, R5 definition and R6 infrastructure/safety tests.

## 10. Frontend clean install

Executed `npm ci` from `VocabularyApp.UI`, honoring the existing lockfile.

Initial sandboxed attempt failed with registry-connect **EACCES** downloading a locked package; npm also reported cleanup EPERM and denied cache-log writes. Classified as environment permission failure, not an R7 dependency defect. The same command was retried with tool-approved elevated permissions and **passed**, exit 0: **967 packages added, 968 audited**. No dependency or lockfile changed; no npm update/audit-fix/global install was run.

Audit reported **91 vulnerabilities: 8 low, 24 moderate, 55 high, 4 critical** in the existing dependency tree. Recorded for separate security/release review; not suppressed or upgraded within R7.

## 11. Initial Angular test result

Executed from `VocabularyApp.UI`:

```powershell
npm test -- --watch=false --browsers=ChromeHeadless
```

First sandboxed launch failed before test discovery with Node filesystem `EPERM` at the user-profile ancestor. Retried unchanged with tool-approved elevated permissions to run the local Karma server/Chrome.

First actual complete suite: **193 executed, 193 passed, 0 failed, 0 skipped**, exit 0. Karma reported **TOTAL: 193 SUCCESS**, browser time **1.515 seconds** (test time 1.246 seconds; initial clean bundle compilation took additional time).

## 12. Angular failures discovered

No Angular test, TypeScript, template or production-build failure. The initial launch permission failure was environmental and executed zero tests. No stale fixture/transport/component-state/error-normalizer defect was revealed.

## 13. Angular remediation

**No frontend production, template, style or test file changed during Phase 8.** Elevated tool execution resolved the environment restriction. Existing typed transport, storage guards, page-local wording/error states and quiz recovery/no-retry behavior remain intact.

## 14. Final full Angular test result

Repeated the same complete ChromeHeadless command with elevated tool permissions; redirected its output to ignored `artifacts/r7-phase8/angular-final.log`, preserving native exit status.

**193 executed, 193 passed, 0 failed, 0 skipped**, exit 0; **TOTAL: 193 SUCCESS**, browser time **0.91 seconds** (test time 0.772 seconds). This is the authoritative frontend result. No focused/skipped suite substituted for it.

## 15. Angular production builds

After the initial complete Angular pass, executed:

```powershell
npm run build -- --configuration production
```

Initial production build **passed**, exit 0, generation **8.748 seconds**. After final Angular tests, repeated the same command: final production build **passed**, exit 0, generation **6.290 seconds**. Final output: `VocabularyApp.UI/dist/vocabulary-app.ui`, including browser index/assets. Initial raw total **444.78 kB**, estimated transfer **110.11 kB**; no initial-bundle budget error.

Both builds reported NG8107 for a redundant optional chain and the existing word-lookup component-style warning (2.80 kB exceeds 2.05 kB warning budget by 751 bytes). No template/compiler error or suppressed budget. PowerShell's redirected log wraps stderr warnings as NativeCommandError entries; Angular/native exit remained 0 and generation completed successfully.

## 16. Final backend / integration build

After final suites and production build, repeated the full solution Release build with `--no-restore`: **passed**, exit 0, **0 errors, 0 warnings reported by the incremental build**, elapsed **3.15 seconds**. Earlier six xUnit analyzer warnings remain informational source warnings, not fixed or suppressed.

No combined Angular-to-wwwroot staging was necessary to verify the R7 contract-only changes. Built frontend output and API XML were inspected; source contracts and in-process integration tests cover their boundary. No combined packaged artifact or IIS compatibility result is claimed.

## 17. Publish verification applicability

**Not required and not run.** Approved plan section 25 defers package/publish compatibility to the existing later authorized release gate and states local publication is separately requested. No separate local package publication was requested here. No new publish process, CI publish/deployment script, MSDeploy, upload or production credential was used.

## 18. OpenAPI verification

Final suite passed **33 ApiContractMetadataTests + 2 OpenApiContractTests**. Generated `/swagger/v1/swagger.json` was retrieved solely through the isolated TestServer. Verification covers exact 14 paths/actions, typed success metadata/data, 500 application schemas, explicit anonymous register/login overrides, all 12 protected actions including fallback lookup, bodyless lookup 401, validation-only versus business/validation media types, required false/zero fields, nullable response/date properties and retained deprecated add fields. Provider transport schemas are absent. XML output is emitted and consumed successfully.

Source/status cross-check remains consistent for all 14 matrix rows: auth 409 where reachable, lookup 404/503, mutation concealed 404 and quiz unavailable/conflict 404/409. Empty framework Bearer challenges remain distinct from application errors. The serialization correction affects metadata only.

## 19. Auth contract verification

Backend/auth/credential tests and Angular auth/login/signup/API/error tests passed. Nested auth/user/token responses, profile/validate-token, password change, safe internal failures, validation, invalid credentials and credential races retain approved behavior. Mandatory legacy migration/rehash/replacement failure issues no token; ordinary LastLogin-only persistence failure can still authenticate successfully. Base64url expiry parsing, malformed-success storage guards and absence of a server expiresAt dependency are protected.

## 20. Provider contract verification

All 14 lookup and 46 provider-contract cases passed, alongside safe-error persistence cases. Deterministic handlers exercised valid/mixed data, genuine 404, 401/403/429/5xx/other failures, network/timeout/cancellation/stream faults, malformed JSON, explicit-null collections/items, unsupported POS, unusable definitions, no partial persistence and DB-first cache. Date-string/timestamp preservation is tested without imposing a new UTC normalization rule. **No live provider calls.**

## 21. Vocabulary contract verification

All 25 vocabulary-contract and 17 ownership cases passed, with successful-wire and fault/concurrency coverage in other groups. New/duplicate/concurrent add preserves the same pair/ID and state; favorite true/false/omission/malformed input; same-row preferred selection; missing/nonowned concealment; full absent/empty/whitespace search; pagination/large vocabulary/counts; persistence failures remain protected. Angular tests passed typed acknowledgements, optimistic rollback, editor preservation, genuine empty versus load-error/stale state and page-local wording/navigation.

## 22. Quiz contract verification

All **53 QuizApiTests** passed, alongside successful-wire/safe-error and Angular quiz cases. Executed coverage includes start/submit/history, explicit option zero, omitted/null/malformed selectedOptionId, null elements, omitted/null/empty answers, unavailable/expired/nonowned sessions, repeat/concurrent submission, recognized persisted duplicate, changed vocabulary before/at transactional recheck, rollback/no double mutation and lock release. Angular guards in-flight/duplicate clicks, retains nullable answers/history, handles 400/404/409/500, allows explicit restart and makes no automatic retry.

## 23. R3 preservation

Credential/password/concurrency/security tests passed. Authentication/credential infrastructure source hashes are unchanged by Phase 8. No token-on-failed-replacement compromise, credential overwrite, weakened validation or runtime JWT policy change.

## 24. R4 preservation

Quiz service and transaction/interceptor infrastructure hashes are unchanged by Phase 8. Executed rollback, transactional recheck, scoring/counter/history/concurrent/replay/lock tests passed. No locking, retry, transaction or scoring mechanism bypassed.

## 25. R5 preservation

Identity remains **(UserId, WordId)**, with same-row preferred/POS changes and successful duplicates. Relevant relational and migration-definition tests passed. No entity/index/mapping/migration change or database update operation.

## 26. R6 preservation

Private relational infrastructure, fixture and host-safety tests passed in the complete suite; no in-memory nonrelational substitute or isolation bypass introduced. Tests create/dispose only their designed private SQLite databases. These results do not establish SQL Server-specific execution behavior.

## 27. PSH-1 preservation

All **24 HttpsHardeningApiTests** passed, including framework Bearer challenge, environment-specific CORS/HSTS and host/static-file behavior, with TestServer isolation. HTTPS/HSTS/CORS/forwarded-header/JWT/middleware/production configuration hashes are unchanged. No production smoke, IIS request or CI/offline deployment-script execution; no claim of fresh production acceptance.

## 28. Warnings and environment issues

| Item | Classification / disposition |
|---|---|
| CS1570 XML warnings (3 initial diagnostics) | R7 metadata correctness issue; escaped query separators; resolved on recompilation |
| xUnit2031 (5), xUnit1026 (1) | R7-authored informational test-style warnings; remain, with no suppressed correctness errors |
| NG8107 optional chain | Informational R7 transport/template narrowing warning; no runtime defect shown; no cosmetic frontend change |
| Word-lookup SCSS budget warning | Pre-existing unchanged style/budget; build passes, no budget increased |
| npm deprecations (inflight, rimraf, glob, critters) | Existing locked dependency warnings; no upgrades |
| npm audit 91 vulnerabilities, including 4 critical | Existing dependency-security concern; separate review required before a release decision; no audit fix/update authorized or run |
| npm cache/registry/Node sandbox permissions | Environment issue; original failures recorded, same authorized commands passed with approved elevated tool execution |
| SDK/Node differ from CI | Local toolchain limitation documented; .NET 8 targets and Angular-supported Node used; no machine configuration change |

No warning was treated as a code/test failure or hidden to manufacture green output. No NuGet vulnerability-metadata warning was observed.

## 29. Phase 8 changed files

Production metadata/comments only:

- `VocabularyApp.WebApi/Swagger/ApiContractOperationFilter.cs`: serializable anonymous security override.
- `VocabularyApp.WebApi/Controllers/WordsController.cs`: escape XML query example.

Tests:

- `VocabularyApp.WebApi.Tests/Integration/ApiContractMetadataTests.cs`: anonymous empty-requirement assertion.
- `VocabularyApp.WebApi.Tests/Integration/OpenApiContractTests.cs`: generated anonymous security assertion.
- `VocabularyApp.WebApi.Tests/Integration/VocabularyOwnershipApiTests.cs`: owned-row invalid-definition 400 expectation.
- `VocabularyApp.WebApi.Tests/Integration/DictionaryProviderContractTests.cs`: exact cache properties with timestamp-value comparison.

Documentation: new Phase 8 results; later-verification notes in the seven prior phase records; current reference/README verification status and security serialization detail updated. No Angular/source-service/request DTO/error-helper/persistence/security configuration changes in Phase 8. Cumulative earlier-phase changes remain in the working tree.

## 30. Contract-reference changes and final cross-layer review

[R7 API contract reference](R7-api-contract-reference.md) now records local execution verification and links here. Anonymous Swagger metadata explicitly documents `security: [{}]`. Runtime endpoints/payloads/statuses/codes/nullability/defaults are unchanged.

Repeated the 14-row controller → request → service → success/error → OpenAPI → Angular → tests → reference review after remediation. Only the metadata serialization needed production-source correction; owned-definition and SQLite date expectations now match the approved existing contract. No contract backsliding or documentation drift introduced. Historical Phase 1–7 pending/no-execution statements remain, with later verification notes.

F01–F16 disposition reconciliation: F01/F02 safe/classified failures verified; F03 provider structure verified; F04 shared error handling verified; F05 auth/date types verified; F06 typed wrappers/OpenAPI verified; F07 required false/zero presence verified; F08 canonical/legacy/POS preservation and signup bounds verified; F09 complete empty search verified; F10 page-local/error-state behavior verified; F11 transport types compile; F12/F13 backend/frontend contract coverage executed; F14 temporal/mutation semantics verified within process-local limits; F15 dormant cleanup compiles; F16 current 14-route documentation is source/schema-consistent. Broader deferred remedies remain deferred.

## 31. Database/migration status

No entities, indexes, ApplicationDbContext mappings, migrations or snapshots changed. No EF command, manual SQL, LocalDB mutation outside tests or production schema access. Test-only private database creation/disposal is the authorized R6 workflow.

## 32. CI/CD status

No GitHub Actions, deployment scripts, release guards, environment protections or production concurrency changed or executed. Workflow source was read only to identify supported local commands. No workflow triggered.

## 33. Dependency status

Package versions, .csproj/package references, package.json and all lockfiles unchanged from Phase 8 entry. Restore/npm ci installed existing dependencies only. No package upgrade, audit fix, security-error suppression or global machine change.

## 34. Production/live-provider access status

**No production access and no live dictionary-provider calls occurred.** No production website/API/database/files/environment/deployment endpoint was accessed. TestServer production-shaped hostnames and stubbed provider URIs are test data. No real provider/deployment credentials were inserted.

## 35. Git, artifacts and secret-safety review

Read-only status/diff/diff --check and entry-hash comparisons preserved the cumulative working tree. Final diff/source review found no new production secrets or credentials, unintended files or forbidden configuration changes. Test values remain synthetic/placeholders. No tracked generated output was added; backend TRX and final frontend logs are under ignored `artifacts/r7-phase8`, and normal bin/obj/node_modules/dist/cache outputs are ignored.

`git diff --check` passed. No staging, commit, push, merge, rebase, reset, revert, cleaning or deployment. No pre-existing files discarded.

## 36. Remaining blockers and limitations

**No remaining R7 local compile/test/production-build gate blocker.** Existing npm vulnerabilities require separate dependency-security/release review. Production/IIS/SQL Server acceptance, exact CI toolchain parity, combined artifact publication and deployment were not performed or claimed. Durable quiz sessions/replay, broad UTC normalization, ignored legacy-field removal and other approved beyond-R7 work remain deferred.

## 37. Completion/readiness assessment

**R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED.** All required local verification gates passed after narrowly correcting the recorded defects. The cumulative changes are ready for user source review and a separately authorized commit/release decision, with dependency-security warnings and infrastructure limitations visible. Release approval must assess those concerns; this result does not authorize deployment.

Historical phase records keep their implementation-time pending status and now link to this later verification evidence. **No commit, push or deployment was performed.** Work stops after Phase 8.
