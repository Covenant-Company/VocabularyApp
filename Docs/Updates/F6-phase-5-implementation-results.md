# F6 — Phase 5 integrated verification and acceptance results

Date: 2026-10-13 (America/New_York). Assessment: **CONDITIONAL COMPLETE** for authorized local verification. Live SQL Server rehearsal, browser-to-backend E2E and manual assistive-technology/mobile/zoom acceptance remain unverified. No production-readiness claim is made.

## 1. Executive summary

The actual ASP.NET Core application passed the authenticated review lifecycle through WebApplicationFactory HTTP with isolated SQLite, including enrollment, correct/incorrect grading, persistence, replay, resume, completion and progression after a lapse. New tests also covered invalid/expired JWTs, owner-scoped keys, runtime disable/re-enable, a 103-word full backlog and restart recovery with isolated persisted Data Protection keys.

Full backend regression: **682 passed, 0 failed, 0 skipped** in Debug and independently **682/0/0** in Release. Angular focused acceptance: **44/0/0**; full Angular regression: **265/0/0**. Release solution build and Angular production build passed. Counts overlap and are not summed. No application defect requiring remediation was reproduced; only a new test-code compilation issue was corrected.

## 2. Authorization and scope

Work was limited to repository inspection, isolated local automated verification, test fixtures/tests and documentation. No production database/API, external dictionary provider, SQL Server connection, persistent/shared/hosted database migration or enrollment operation was used. No dependency, public contract, scheduler, schema/migration, application feature flag, production configuration/secret or CI/CD workflow changed. No Git write, branch change/deletion, commit, push, merge or deployment occurred. Phase 6 was not begun.

## 3. Repository baseline

Normal branch/status/log commands failed Git's ownership safety check; no safety override/configuration change was made. Read-only HEAD/ref/loose commit inspection verified `feature/f6-spaced-repetition` at `7e387827907cff603b8e673d1ceeedd85c9bf73d` (“Complete F6 Phase 4 Angular review experience”), descending from `bf91b033…` and Phase 3 `be793575…`.

Before edits, a read-only Git index comparison found no tracked differences with CRLF normalization permitted. A SHA-256 baseline captured 315 source/configuration/documentation files, excluding generated/dependency/Git directories. The final preservation check permits only the expected test/documentation changes, with no deletion. Git objects/index/configuration were not written. This is safe alternate evidence, not a claim that normal Git status succeeded.

All eight requested documents and the actual scheduler/model/mappings/review services/controllers/DTOs, Angular service/validators/component and existing infrastructure were reviewed. Phase 1–4 files are present. D1–D9 and P1–P5 remain unchanged.

## 4. Verification environment

Windows; .NET SDK **10.0.401**, projects targeting **net8.0**, executed .NET/ASP.NET Core runtime **8.0.25**. EF SQL Server/design **8.0.10** and SQLite test provider **8.0.31**; Microsoft.AspNetCore.Mvc.Testing **8.0.10**; xUnit **2.9.3**. Node **18.20.8**, npm **10.8.2**, Angular core **18.2.14**, CLI **18.2.21**, existing Jasmine/Karma and Chrome Headless **155.0.0.0** (installed Chrome product 155.0.8059.79).

WAF replaces the application DbContext with isolated SQLite and uses fake test JWT settings plus a rejecting/controlled dictionary HttpMessageHandler. New review tests use ControllableTimeProvider, synthetic vocabulary/accounts and independent contexts/connections where relevant. Restart testing used a unique temporary XML key directory and deleted only its verified files after both hosts were disposed. No production Data Protection storage was modified.

The existing restored dependencies were sufficient; no restore/install/upgrade was needed. Angular executions used the approval-reviewed local route because the previously documented sandbox cannot resolve the workspace's parent path. This did not change filesystem/Git safety settings or authorize publication.

## 5. Requirements traceability matrix

[F6-phase-5-verification-matrix.md](F6-phase-5-verification-matrix.md) was created **before** new tests and updated from execution results. It maps each decision/requirement to implementation, existing/new tests, environment/layer, expected/actual outcome, evidence and remaining risk. It distinguishes units, Angular mocks, WAF HTTP, relational/concurrency, recorded HTTP/OpenAPI contracts, live browser E2E, manual accessibility and SQL Server rehearsal.

## 6. Tests reused

The full reruns include ReviewSchedulerTests/ReviewDtoContractTests, ReviewPersistenceModelTests and offline migration/model checks, ReviewApiTests, ReviewSessionTests, ReviewEnrollmentTests, ReviewContentChangeTests, ReviewConcurrencyTests, ReviewContractTests, and all existing R7/authentication/vocabulary/dictionary/quiz/error/OpenAPI/regression tests. Angular reruns include review service/component, dashboard, auth/login/signup, API/error, dictionary/My Words, quiz, shell and toast tests.

Earlier phase results were treated as inventory, not as new execution evidence. All current full-suite counts come from this Phase 5 turn.

## 7. New tests, fixtures and files

Created:

- `VocabularyApp.WebApi.Tests/Integration/ReviewPhase5AcceptanceTests.cs` — 13 acceptance cases, including 8 invalid/expired JWT cases.
- `VocabularyApp.WebApi.Tests/Integration/ReviewPhase5HttpEvidenceTests.cs` — one actual HTTP/OpenAPI capture/acceptance case.
- `VocabularyApp.WebApi.Tests/Infrastructure/MutableReviewOptionsMonitor.cs` — test-only runtime flag control.
- `VocabularyApp.UI/src/app/services/review-http-contract.spec.ts` — 16 recorded-wire/OpenAPI compatibility cases using real Angular parsers/transport.
- `VocabularyApp.UI/src/app/testing/phase5-http.evidence.ts` — generated, test-only actual HTTP responses/OpenAPI subset, no JWT or production data.
- `Docs/Updates/F6-phase-5-verification-matrix.md`, `F6-phase-5-accessibility-acceptance.md`, and this report.

Modified only `VocabularyApp.UI/src/app/components/review-session/review-session.component.spec.ts`: one browser layout/focus/target-size acceptance case. No production application code changed. Generated bin/obj/cache/dist/TRX outputs are verification artifacts, not deployment actions.

Captured-wire evidence SHA-256: `d5741a727bd1e47e0bc7498c183b8d9dd293c21b7ee80c94fea19d6d2c729b22`. Regeneration produces fresh synthetic GUIDs/cursors/traces; its hash therefore changes on an explicit export. Normal test runs validate new HTTP behavior without rewriting this fixture unless the export environment variable is supplied.

## 8. Complete authenticated lifecycle

New WAF HTTP acceptance registers/authenticates a synthetic user, inserts canonical test definitions and saves words through the real vocabulary endpoint/enrollment hook. It retrieves summary/due, starts two questions with four distinct options, submits a stored-key correct answer, replays the same request and resumes identical options/receipt. A separate item is deliberately answered incorrectly; history, lapse, schedule, completion and receipt equality are verified in the database.

Controlled time reaches the exact next-due boundary and a new eligible session verifies correct-after-lapse progression and preserved lifetime lapses. All pending questions are completed; every completion item has a receipt. Six genuine review events persist, without synthetic enrollment/abandonment outcomes. An ordinary quiz then runs through its actual endpoints and changes only quiz results/counters, leaving review scheduling/history unchanged. No outbound dictionary request occurs in this lifecycle.

## 9. Scheduling verification

Fresh full-suite executions cover every approved 1/3/7/14/30/60/90-day success step, repeated 90-day cap, Reviewing after three successes, failures resetting Learning/+1 UTC day, lifetime lapse increments/preservation, reset rules and correct-after-lapse restart. The Phase 5 HTTP lifecycle additionally checks equality vs one tick before due. Scheduler unit tests cover UTC/DST/leap/overflow boundaries without changing interval-v1.

This establishes the tested .NET/SQLite behavior, not SQL Server datetime2 precision or provider execution results.

## 10. Authentication and ownership

Existing missing-JWT checks and new valid-signature expired JWT/invalid JWT checks cover all four review routes; framework challenges are 401 with no leaked body. Existing tests cover owner/nonowner/wrong item/session/cursor isolation. New tests show that two owners can independently accept the same SubmissionId, while neither can use it to retrieve the other's receipt. New-key access to a nonowned session is concealed as 404; owned-key payload conflict returns the approved safe 409. Relational ownership FKs and state/history isolation tests passed.

## 11. Pagination and availability

New testing enrolls 103 saved words with distinct monotonically increasing UTC instants, verifies 103 due/102 overdue, default take=20, maximum take=100, rejection of take=101 and full ordered keyset traversal with no duplicate/omitted record in the static backlog. Existing tests cover tampering, cross-user/expired cursor rejection, blocked visibility and all summary/count semantics.

Actual ready/empty/insufficient/no-usable-due/ambiguous/blocked-due HTTP bodies were captured and accepted by Angular validators. Existing tests verify non-due distractors and distinct options. Pagination remains a live view: concurrent rescheduling may legitimately move/remove rows, so the static traversal result is not a snapshot-reservation guarantee.

## 12. Durable session resume

Two recreated WAF hosts share only an isolated named SQLite database and a unique test key directory. The second host decrypts the first host's cursor, resumes identical option order and accepted receipt, rejects resume at the 30-minute expiry boundary and replays the original accepted answer after expiry. Database keeper lifetime spans recreation; each context owns an independent connection. This proves local fixture durability/key reuse, not an IIS recycle, real browser restart, hosted SQL connection or production key-storage policy.

## 13. Idempotency and concurrency

Original accepted response bodies replay identically, with no second state advancement/history event. Existing barrier-based independent-connection cases were freshly rerun in **both Debug and Release**: identical submissions, different keys/same item, same key/different payload, competing sessions/same target, independent items/same session, concurrent adds and preference/content changes during an in-flight request. This repeats the SQLite invariants without timing sleeps as primary synchronization.

Actual-write failure tests cover state/history/item rollback and precommit failure. A postcommit injected exception recovers the persisted receipt. Controlled SQLITE_BUSY tests succeed after two retries and exhaust safely after three failed attempts. Failure injection is synthetic; SQL Server deadlock 1205, filtered uniqueness/isolation/locking, network-level ambiguous commits and host-specific recovery still need real SQL Server evidence.

## 14. Definition changes and revisions

Existing preference no-op/reset, effective fallback selection, lifetime lapse/history/quiz/metadata preservation, rollback, normalized whitespace/example edits, genuine revision changes, fallback addition/reorder/removal, distractor staleness and out-of-band text guards all passed in current full runs. Controlled maintenance remains DI-only, fails closed when coordination is disabled and creates no public editing endpoint. Direct production SQL editing/reconciliation was not tested or authorized.

## 15. Feature flags

Default-off options tests and all independent flag combinations passed. New test-only mutable options disable both flags after an accepted answer; review routes return approved 503 while profile, cached dictionary lookup, vocabulary and quiz history remain operational. Adding a word while enrollment is disabled creates no state. Re-enabling in that isolated host replays the original receipt and resumes the same session without deleting/resetting data.

Existing unenrolled words are not silently backfilled by re-enable; legacy catch-up remains a separately authorized operation. No appsettings/runtime production flag changed.

## 16. Angular/backend contract compatibility

The capture test records **actual** WAF serialized responses and Swagger metadata: summary/due, protected cursor, option-zero submit/receipt/replay/resume/completion, all availability outcomes, validation problem details, unavailable/stale/conflict/expired/disabled/internal errors and bodyless 401. Registration tokens/passwords are not exported.

Angular's real ReviewService and validators consume these captured bytes, validate receipt identity/options/nullable fields, encode the actual opaque cursor and compare submit keys/required fields/start optionality/due defaults/public fields to actual OpenAPI. All 16 cases passed. This is stronger than manually authored mock-only DTO fixtures and verifies serialized wire compatibility. **HttpTestingController still supplies transport in Angular; it is not proof of simultaneous live browser-to-backend HTTP, cookie/CORS/hosting or real network-loss behavior.**

## 17. Browser E2E result/limitations

Repository package/configuration/script inspection found no configured Playwright/Cypress/Selenium/Protractor/Puppeteer E2E framework. None was added. ChromeHeadless/Karma exercised component/service/browser DOM flows using existing tooling; WAF independently exercised the actual backend HTTP/persistence lifecycle. Full login → routed browser UI → live isolated backend → browser refresh/network interruption/completion/disablement remains **pending**, not passed. No browser automation targeted production.

## 18. Accessibility acceptance

Automated checks passed for semantic radio grouping/labels, option zero, effective disabled state, prompt focus, status/live markup, long content at a 320px container, no container horizontal overflow, minimum 44px controls and focus-within outline. New data uses a 100-character word and 1,000-character definition. Existing frontend tests continue to verify textual correct/incorrect feedback and recovery branches.

[F6-phase-5-accessibility-acceptance.md](F6-phase-5-accessibility-acceptance.md) gives precise isolated-environment manual steps for physical keyboard/arrow/Tab/Enter, focus/traps, named screen readers, loading/error/feedback announcements, true 320px viewport, **actual 200% browser zoom**, real touch devices and all unavailable/recovery states. All manual rows are **PENDING**. No NVDA, JAWS, VoiceOver, physical device, manual keyboard session or real browser-zoom acceptance was performed. Container/DOM tests are not reported as those manual results.

## 19. SQL Server rehearsal status

**NOT EXECUTED.** No separately authorized disposable SQL Server target/connection details were supplied, and none were sought from production configuration. The existing Phase 2 rehearsal plan remains applicable. Needed evidence includes explicit disposable identity/version/compatibility/collation, authorized owner and known pre-F6 history, recoverable backup/restore, additive migration/default/check/index/FK verification, idempotent enrollment/catch-up/reconciliation, preserved personal/quiz data, independent-connection SQL races/isolation/deadlocks/recovery, query plans/locks/volume limits, persistence/restart and rollback/forward-recovery evidence.

Schema-before-binary remains mandatory: the new backend needs the additive F6 schema and WordDefinition.ContentRevision even with reviews disabled. Old/new binaries must be checked against that additive schema; no pre-F6-schema compatibility is inferred from flags. Existing offline migration/SQLite checks passed but are not SQL Server execution proof. Zero skipped tests in the local suites does not imply that an absent SQL Server gate ran.

## 20. Exact regression commands and results

From repository root, using existing restored assets:

```powershell
$env:F6_PHASE5_HTTP_EVIDENCE = 'C:\Users\Owner\Documents\SourceCode\VocabularyApp\VocabularyApp.UI\src\app\testing\phase5-http.evidence.ts'
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewPhase5 --logger 'trx;LogFileName=f6-phase5-acceptance.trx'
```

Final targeted/export run: exit **0**, **14 passed / 0 failed / 0 skipped**, reported duration **10s**. Export path is verified by the test against the exact repository test-fixture path; the variable is set only in that command's process. Initial narrower acceptance was 13/0/0; subsequent 14-case runs added HTTP evidence and actual safe 500 capture. Repeated use of the same TRX name emitted an overwrite warning.

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --no-restore --logger 'trx;LogFileName=f6-phase5-backend-regression.trx'
dotnet build VocabularyApp.sln --configuration Release --no-restore
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release --no-build --no-restore --logger 'trx;LogFileName=f6-phase5-backend-release.trx'
```

Debug: exit **0**, **682/0/0**, **1m10s**. Release build: exit **0**, **0 errors, 6 existing analyzer warnings**, **6.33s**. Release test: exit **0**, **682/0/0**, **1m14s**. TRX counts were independently read after execution. Six unchanged warnings are xUnit2031 (five existing assertions) and xUnit1026 (one existing unused theory parameter) in ApiContractMetadataTests/ApiContractShapeTests.

From `VocabularyApp.UI`, using approval-reviewed local execution for Angular's workspace-path access:

```powershell
$env:CHROME_BIN = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
npm test -- --watch=false --browsers=ChromeHeadless --reporters=dots --include=src/app/services/review-http-contract.spec.ts --include=src/app/components/review-session/review-session.component.spec.ts
npm test -- --watch=false --browsers=ChromeHeadless --reporters=dots
npm run build -- --configuration production
```

Focused: exit **0**, **44/0/0**, browser execution **0.420s**, test time **0.381s**. Full Angular: exit **0**, **265/0/0**, reporter execution **0.979s**, test time **2.368s**, excluding compilation/launch. Production build: exit **0**, bundle generation **7.459s**; initial **482.30kB** / estimated transfer **120.26kB**, lazy review **25.94kB** / **6.36kB**. Output stays in local dist; no publication occurred.

Angular build warnings remain the unchanged WordLookupComponent NG8107 optional chain at template line 273 and its 2.80kB stylesheet exceeding the 2.05kB warning budget by 751 bytes. Budgets were not relaxed; the review test-only evidence does not enter the production bundle. Overlapping focused/full/Debug/Release counts are deliberately not added together.

## 21. Defects and corrections

**No verified production implementation defect** emerged from these checks, so no application fix, contract/algorithm/schema change or broad refactor was made. The new Angular captured-evidence test initially failed compilation because its body was declared unknown while HttpTestingController.flush requires a JSON-compatible object/null. Only that test declaration was narrowed; real application validators remained unchanged, and focused/full tests then passed. Assertions were not removed or weakened to hide a behavioral failure.

## 22. Remaining risks and documentation differences

SQL Server behavior, live browser/backend networking, actual hosting recycle/config/key persistence and manual accessibility remain missing evidence. Recovery is intentionally per-tab sessionStorage; actual physical network interruption/browser shutdown/device transfer is not established by synthetic failure tests. Representative production query/locking/storage performance is unmeasured.

The historical plan lists SQL Server tests/rehearsal among Phase 5 deliverables, but this assignment explicitly disallows inferring a disposable target authorization. Those requirements are preserved as pending rather than adding empty SQL tests or claiming completion. Its generic suggested results-document names are superseded by the user's explicit Phase 5 report/matrix/checklist deliverables. Historical Phase 1–4 authorization statements describe their original phases, not this current authorization. No actual public HTTP contract discrepancy was found.

## 23. Phase 6 prerequisites

Developer review and separate Phase 6 authorization are required. A production candidate additionally needs separately authorized SQL Server rehearsal and its recorded evidence, P6 named migration/release/recovery owners and limits, P7 persistent runtime flags/disable-recycle/Data Protection verification, live browser/backend acceptance and completed manual accessibility checks. Local build success does not grant release approval or remove schema-first requirements.

## 24. Final readiness assessment and stop point

**CONDITIONAL COMPLETE — Phase 5 local verification.** Available automated acceptance/regression/build gates passed with precise missing evidence documented. Live SQL Server, browser E2E and manual acceptance are not passed; production readiness is not established. Development stops here for developer review. No Phase 6, Git write/deletion, deployment, production database operation, migration/enrollment execution or production flag activation was performed.
