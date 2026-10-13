# F6 — Phase 1 implementation results

Date: 2026-10-12. Scope: **Phase 1 — Contracts and Scheduling Rules only**. Assessment: **COMPLETE** for the authorized Phase 1 scope and focused verification. No Phase 2 implementation is authorized or started.

## 1. Executive summary

Implemented the deterministic `interval-v1` scheduler with immutable scheduling state and transitions, all eleven approved review DTOs, disabled feature options and a planned API contract reference. Focused scheduler/DTO tests passed: **98 passed, 0 failed, 0 skipped**. The selected test run compiled the new code; no separate production build was performed.

No review EF entities/DbSets/configurations, migrations, database changes, review service/controller/endpoints, WordService/QuizService changes, Angular work, runtime service registration, production settings, CI/CD changes, commits, pushes, merges or deployments were performed.

## 2. Approved decision references

The developer formally approved **D1–D9 and P1–P5** in the Phase 1 assignment. The [implementation plan](F6-spaced-repetition-implementation-plan.md) now records that approval, preserving the original technical specifications. The scope remains Phase 1 only. **P6–P7 remain unresolved production-release gates**, not approved or completed.

The [analysis](F6-spaced-repetition-analysis.md) remains historical and unchanged. The [Phase 1 contract reference](F6-review-api-contract-reference.md) explicitly labels every proposed route as planned and unimplemented.

## 3. Files created or modified

Created:

- `VocabularyApp.WebApi/Services/IReviewScheduler.cs`
- `VocabularyApp.WebApi/Services/IntervalV1ReviewScheduler.cs`
- `VocabularyApp.WebApi/Models/ReviewTransition.cs`
- `VocabularyApp.WebApi/DTOs/ReviewDTOs.cs`
- `VocabularyApp.WebApi/Configuration/ReviewOptions.cs`
- `VocabularyApp.WebApi.Tests/Services/ReviewSchedulerTests.cs`
- `VocabularyApp.WebApi.Tests/Services/ReviewDtoContractTests.cs`
- `Docs/Updates/F6-review-api-contract-reference.md`
- `Docs/Updates/F6-phase-1-implementation-results.md`

Modified only `Docs/Updates/F6-spaced-repetition-implementation-plan.md` to record approvals and distinguish original planning execution from the subsequent Phase 1 authorization/results. No existing application or test source file was modified.

Before edits, a SHA-256 baseline of 261 existing source/document/configuration files was captured using `rg --files --hidden`, excluding `.git`, node_modules, bin, obj and dist. Post-implementation comparison found only the authorized plan changed, the new Phase 1 files added and no baseline files deleted. This verifies preservation during this task; it does **not** establish pre-existing Git cleanliness. Normal test compilation updated generated bin/obj outputs, which are excluded from the source comparison.

## 4. Scheduler implementation details

`ReviewStatus` uses stable values New=0, Learning=1, Reviewing=2. `ReviewScheduleState` holds enrollment, next/last review UTC instants, interval, capped consecutive count, lifetime lapse count, status, positive Int64 schedule version and algorithm version. `ReviewTransition` records the previous state, genuine bool outcome, accepted UTC instant and next state. Immutable records are independent of EF/transport.

`IReviewScheduler` provides Enroll, Reset, Review and IsDue. Enrollment yields New, immediately due, no last review, count/interval/lapse=0 and version=1. Success increments the capped count and schedules 1/3/7/14/30/60/90 elapsed UTC days; Reviewing begins at count 3. Incorrect answers reset count to zero, schedule one day, increment lifetime lapses and become Learning. The correct answer after a failure schedules one day again. The cap remains 90 days with count 7.

Every accepted answer/genuine reset advances ScheduleVersion with checked arithmetic. Reset preserves EnrollmentAt/lifetime lapses/algorithm, clears current-epoch progression/LastReviewedAt and schedules immediately at the supplied reset time. Caller must invoke Reset only for a genuine meaning/content change; the scheduler does not inspect definitions or alter quiz/history data.

Validation rejects null/unsupported/inconsistent state, non-UTC timestamps, invalid enum/count/interval/lapse/version, mismatched due arithmetic, backwards enrollment/review/reset timestamps and early review acceptance. A reset may happen before the next scheduled review but cannot precede enrollment/last accepted review; an already-New epoch cannot reset backwards. Date/version/lapse overflow throws without mutating inputs. Due comparison is inclusive at exact tick equality. Overdue answers anchor to actual accepted server time, with no progression bonus. No clock, HTTP, database access or randomness exists in the scheduler.

## 5. DTO contract details

All eleven DTOs from section 9 are defined with the approved property names/types, nullable response members, list defaults, numeric bounds and exact wire literals. Defaults include due take=20, start questionCount=10, optional resume=null and word-to-definition questions. Due pagination bounds are 1..100 and cursor maximum length 2048; start count is 1..20.

Submit's sessionId/itemId/submissionId/selectedOptionId have JsonRequired. The valid option zero deserializes and validates; omission/null/type-invalid options fail binding, out-of-range integers fail Range, and Guid.Empty fails the new NonEmptyReviewGuid annotation. Optional resume may be null but not an empty GUID.

Review wire status/availability/reason/questionType remain strings with permitted literal annotations, separate from the scheduler enum. Public scheduling versions/algorithm/correctness inputs are absent. Pending items have no answer-key property and a nullable acceptedReceipt; accepted receipt supports the already-approved recovery contract. Service-level cross-field availability, ownership, option membership, count consistency and key projection enforcement are later work.

All review instant properties have a DTO-scoped JSON converter that requires UTC DateTime Kind on write and `Z` UTC input on read. Nullable timestamps retain explicit null serialization. Existing R7 DTO/date behavior is not changed. Tests use ASP.NET-style JsonSerializerDefaults.Web and SuccessResponse<T> to verify camelCase, exact success/data envelope, explicit nulls, receipt round trips and no pending key leakage.

No new error helper pairs or Swagger action/request classifications were added: those require the planned controller/service integration in Phase 3. No live MVC contract/HTTP error mapping is claimed by unit serialization/annotation tests.

## 6. Configuration defaults

`ReviewOptions.SectionName` is `Reviews`. EnrollmentEnabled and Enabled both default to false. Options are not bound or registered yet. No production/appsettings/deployment changes, algorithm configuration, alternative schedulers or new packages were introduced.

## 7. Tests added

ReviewSchedulerTests covers:

- New enrollment; every ladder transition, repeated capped success and stable algorithm/enrollment values.
- Incorrect answers from every step, repeated failures, correct after failure and lifetime lapse preservation.
- New/Learning/Reviewing transitions, checked version increments and genuine reset behavior.
- Due equality and adjacent ticks, early rejection, overdue anchoring without bonus.
- Twenty-four-hour UTC intervals across both New York DST offset changes and leap-day/tick preservation.
- Invalid algorithm/status/count/version/interval/timestamp combinations, null state and non-UTC instants.
- Date overflow/exact maximum-date boundary, version/lapse overflow and deterministic repeated execution without mutation.

ReviewDtoContractTests covers required submit-member omission, option zero/all option IDs, malformed/null/empty GUIDs and options, bounds/defaults/optional resume/cursor length, exact wire shapes, explicit nulls, pending answer-key absence and accepted receipt recovery, UTC serialization/rejection, allowed wire literals and disabled configuration defaults.

The tests are pure unit/contract tests. They do not start the application, initialize SQLite, migrate SQL Server, call production or use live dictionary APIs.

## 8. Exact verification commands and results

Executed from `C:\Users\Owner\Documents\SourceCode\VocabularyApp`:

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewSchedulerTests|FullyQualifiedName~ReviewDtoContractTests"
```

Exit code **0**. **Passed 98; failed 0; skipped 0; total 98**, reported test duration 414 ms, target net8.0. Data/WebApi/test projects compiled in Debug as part of this focused run. Existing restored assets were used; no package installation/restore, unrelated test suite, Angular tests, Release/production build, migrations or deployment ran.

Compilation emitted six warnings from existing unchanged tests: xUnit2031 in ApiContractMetadataTests.cs (two) and ApiContractShapeTests.cs (three), and xUnit1026 in ApiContractMetadataTests.cs (one). No warning from the new Phase 1 files was reported. Unrelated warning remediation was not attempted.

Read-only Git checks `git branch --show-current` and `git status --short` failed with the ownership safety check. A temporary process-only attempt also failed:

```powershell
git -c safe.directory=C:/Users/Owner/Documents/SourceCode/VocabularyApp branch --show-current
git -c safe.directory=C:/Users/Owner/Documents/SourceCode/VocabularyApp status --short
git -c safe.directory=C:/Users/Owner/Documents/SourceCode/VocabularyApp rev-parse HEAD
```

No global or persistent Git configuration was changed. `.git/HEAD` directly records `ref: refs/heads/feature/f6-spaced-repetition`; the existing solution/projects and unused Phase 1 target paths were inspected before edits. The explicit assignment permits continuing under this limitation when the intended branch/files can be established safely. Exact commit, Git working-tree status and pre-existing uncommitted changes remain unverified; file hashes establish only task-local preservation.

## 9. Deviations from the approved plan

No architectural or behavioral deviation. Supporting domain state/status types share ReviewTransition.cs, minimizing files and abstractions. The required nonempty-Guid annotation and UTC-only converter share ReviewDTOs.cs and affect only the new contracts. Focused DTO tests use an additional new file permitted by the assignment. A combined test filter verifies both new classes in one compilation; `--no-restore` uses existing dependencies without installing packages.

Git status/commit verification could not be completed; the safety restriction and permitted fallback are explicitly reported rather than represented as a clean tree. No later-phase dependency was implemented to bypass it.

## 10. Risks or blockers discovered

No Phase 1 implementation/test blocker remains. Git ownership prevents exact commit/dirty-tree identification and must be resolved in a trusted environment before release. Unit tests establish scheduling/DTO behavior only, not transaction concurrency, durable recovery, content invalidation, runtime activation, MVC/OpenAPI integration or SQL Server migration compatibility.

P6–P7 remain release gates. Later services must call Reset only for genuine changes, supply server UTC time, materialize persistence timestamps as UTC, validate server-owned options and atomically coordinate state/history/receipts. Shared R7 helpers/Swagger filters will require explicit new-code/request metadata in Phase 3; Phase 1 does not weaken or replace them. Deferred dependency assessment stays outside scope.

## 11. Phase 1 acceptance checklist

- [x] D1–D9/P1–P5 approvals recorded; P6–P7 remain unresolved.
- [x] Intended feature branch established by `.git/HEAD`; Git inspection limitation reported; unrelated existing files preserved by baseline comparison.
- [x] Pure deterministic versioned scheduler and immutable domain state/transitions implemented.
- [x] Approved enrollment/reset/lapse/status/version and elapsed UTC rules validated/tested.
- [x] All eleven DTOs defined, including required option-zero distinction, nullable fields and UTC wire behavior.
- [x] Both feature options default false with no runtime activation/registration.
- [x] Contract reference labels endpoints planned, not implemented.
- [x] Required focused scheduler/DTO verification passed: 98/0/0.
- [x] Results and actual command/output limits documented.
- [x] No Phase 2/later code, migrations, production changes or Git writes performed.

## 12. Phase 2 prerequisites

Wait for developer review and **explicit Phase 2 authorization**. Reinspect the current feature branch and relevant source state, preserve any new unrelated changes and use the approved mappings/schema/enrollment policy in plan sections 6–7. SQL Server rehearsal environment and approved verification path must be arranged; P6–P7 stay release gates, not completed tasks.

Phase 2 may then add the four persistence models/mappings, content revision, ownership keys, additive migration and idempotent enrollment/verification scripts under its own authorization. SQLite final-model checks cannot substitute for SQL Server migration rehearsal. Do not introduce review routes, WordService hooks, Angular or production deployment merely because Phase 1 passed.

## 13. Final readiness assessment

**COMPLETE — Phase 1 only.** The authorized scheduler/domain/DTO/options/documentation scope is implemented and all required focused verification passed. No claim is made that the full F6 feature or production rollout is ready. Stop here; Phase 2 requires developer review and separate explicit approval.
