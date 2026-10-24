# F6 — Phase 2 implementation results

Date: 2026-10-12 (America/New_York). Scope: **Phase 2 — Database Schema and Migration Design only**. Assessment: **CONDITIONAL COMPLETE**. Local implementation and safe verification are complete; live SQL Server rehearsal remains pending. No Phase 3 work or production operation was performed.

## 1. Executive summary

Implemented the four durable review persistence models, EF mappings, ownership keys/constraints/indexes, UTC date mapping, application-managed concurrency versions and ContentRevision. Generated the additive `20261013014406_AddSpacedRepetition` migration with designer/snapshot. Prepared separate insert-only initial/catch-up enrollment and read-only verification SQL scripts plus a SQL Server rehearsal procedure.

Final focused checks: **157 passed, 0 failed, 0 skipped** (59 Phase 2 checks plus the existing 98 Phase 1 checks). Directly affected legacy vocabulary/quiz/identity/fixture regression checks: **98 passed, 0 failed, 0 skipped**. Both offline SQL Server forward/rollback script commands succeeded. No SQL Server database was connected to or changed; migration/enrollment/recovery rehearsal is not claimed.

## 2. Approved decisions and repository baseline

D1–D9 and P1–P5 remain approved. P6–P7 are unresolved production-release gates. The [implementation plan](../F6-spaced-repetition-implementation-plan.md), particularly sections 6/7/13/14/15, supplies the schema and rollout specification. The analysis, Phase 1 results and API reference were read and retained unchanged as historical records. This later assignment explicitly authorizes local migration generation, superseding the planning pass's prohibition; it does not authorize applying migrations to production or starting Phase 3.

Git branch/status/log/ancestor commands were rejected by ownership safety checks; no persistent/global security settings were changed. Direct `.git/HEAD` identifies `feature/f6-spaced-repetition`; its branch ref contains `99fdbc54e46ece48bb7be1d36ac9e07cebb556c6`, and its reflog records the Phase 1 commit description. This establishes the intended branch and supplied Phase 1 commit through read-only files, but Git working-tree status/pre-existing uncommitted changes remain unverified. No branch switch, reset, merge, commit or push occurred.

A SHA-256 baseline captured 270 existing files excluding `.git`, node_modules, bin, obj and dist. Final comparison identified only the four expected existing Data files modified, authorized new Phase 2 files, and no baseline deletions. This proves task-local preservation of unrelated source/configuration/documentation; it does not establish a clean tree before the task. Normal builds/tests updated generated bin/obj outputs.

## 3. Files created and modified

Created:

- `VocabularyApp.Data/Models/UserWordReviewState.cs`, `ReviewHistory.cs`, `ReviewSession.cs`, `ReviewSessionItem.cs`.
- `VocabularyApp.Data/Configurations/UserWordReviewStateConfiguration.cs`, `ReviewHistoryConfiguration.cs`, `ReviewSessionConfiguration.cs`, `ReviewSessionItemConfiguration.cs`, `ReviewMapping.cs`.
- `VocabularyApp.Data/ApplicationDbContextDesignFactory.cs` for offline EF tooling without API startup/production configuration access.
- `VocabularyApp.Data/Migrations/20261013014406_AddSpacedRepetition.cs` and `.Designer.cs`.
- `scripts/database/Enroll-F6ReviewStates.sql`, `Verify-F6Enrollment.sql`.
- `VocabularyApp.WebApi.Tests/Infrastructure/IndependentConnectionDatabaseFixture.cs`, `ReviewMigrationDefinitionTests.cs`.
- `VocabularyApp.WebApi.Tests/Integration/ReviewPersistenceModelTests.cs`.
- `Docs/Updates/F6-Spaced-Repetition/Phase-2/F6-phase-2-sql-server-rehearsal.md` and this results document.

Modified only:

- `VocabularyApp.Data/ApplicationDbContext.cs`: four DbSets, explicit configuration application, UserWord ownership alternate key, revision default/token/check.
- `VocabularyApp.Data/Models/UserWord.cs`: optional one-to-one ReviewState navigation.
- `VocabularyApp.Data/Models/WordDefinition.cs`: ContentRevision initialized to 1.
- `VocabularyApp.Data/Migrations/ApplicationDbContextModelSnapshot.cs`: generated current relational model.

No historical migration, WebApi application source, Phase 1 source/tests, existing tests, API helper/controller/service, UI, settings, deployment script or workflow was modified. Temporary offline SQL scripts were generated under `$env:TEMP`; they are not application/deployment payloads.

## 4. Entities and relationships

Persistence status fields are the approved int values 0/1/2; the Data assembly does not depend on WebApi's domain enum. All column names/types/nullability follow section 6. Table names are explicit: UserWordReviewStates, ReviewSessions, ReviewSessionItems and ReviewHistories.

State uses UserWordId PK, owner composite FK to UserWords `(UserId,Id)`, nullable selected definition/revision pair, enrollment/last/due timestamps, count/interval/lapse/status, algorithm and ScheduleVersion. The state FK cascades with its saved word; current selected definition FK is NoAction. The unchanged `(UserId,WordId)` unique identity remains authoritative.

Session has Guid Id and owner, creation/expiration, requested/actual item counts, nullable CompletedAt and Version; owner FK is NoAction. Pending/completed status comes from CompletedAt and expiration at query time, as specified; no unapproved SessionStatus column or redundant progress counters were added. A 30-minute policy is supported but belongs to later service orchestration, not a database clock/default.

Item captures session/owner/target/ordinal/version, definition identity/revision, word/definition/POS/prompt/question type, full option JSON and correct option, with nullable coherent accepted submission/time/selection/outcome/receipt/version. Composite session and saved-word FKs enforce ownership with NoAction. History retains its item/owner/target, genuine binary outcome/provenance/answer and definition snapshots, timestamps, before/after progression/version/lapse state, elapsed ticks and receipt. Its composite item and saved-word FKs are NoAction. Session reference is available through SessionItem.Session; review state through UserWord.ReviewState, without adding unspecified SessionId or direct state-FK columns. Historical definition IDs are snapshots without canonical FKs.

## 5. Migration details

Generated `AddSpacedRepetition` after the verified current final migration `20260829155134_CorrectUserWordIdentity`. Scaffold ID `20261013014406` is the UTC generation timestamp, corresponding to the user's 2026-10-12 evening. Designer and snapshot retain EF ProductVersion 8.0.10.

Up operations are limited to one ContentRevision column/default, one UserWord alternate key, four CreateTable operations, new checks and indexes. No old table/index is dropped/recreated, no legacy data is replaced, and no seed/enrollment/history SQL is embedded. Foreign-key dependency ordering creates sessions/state before items and history. Existing seed data remains unchanged.

Down drops history, state, items and sessions, then removes the new revision check, ownership alternate key and ContentRevision. It preserves existing identity/quiz indexes and legacy tables, but destroys F6 state/history/receipts. Down is a disposable rehearsal operation, not an approved production rollback. Forward recovery/feature disable remains preferred after real learning writes.

Offline migration tests verify additive operations/ordering, constraints/types/ownership, no enrollment, generated forward/down SQL, and absence of relational differences among current model, snapshot and migration target. They do not prove SQL Server execution success.

## 6. ContentRevision and UTC mapping

ContentRevision is nonnullable Int64, CLR default 1, database default 1 and EF concurrency token with CHECK >=1. Existing definitions acquire revision 1 during the additive SQL Server upgrade; old inserts omitting it receive default 1. SQLite raw-insert/default and stale-revision update tests passed. No content-edit service or automatic revision advancement/reset hook was implemented; future genuine changes must increment/reconcile transactionally in Phase 3.

New review dates are explicitly mapped datetime2(7). A narrowly scoped value converter requires UTC Kind when EF writes review timestamps and restores UTC Kind when SQL Server/SQLite materializes them, including nullable properties. It leaves all legacy dates untouched. SQL storage has no timezone marker; raw SQL/operator input must use UTC. Date arithmetic correctness is still enforced by the Phase 1 scheduler/later services, rather than provider-specific SQL DATEADD checks.

## 7. Enrollment and catch-up scripts

`Enroll-F6ReviewStates.sql` supports initial/catch-up modes using the same insert-only operation. Parameters: explicit captured EnrollmentAtUtc (defaults NULL to fail safely), BatchSize=1000, AfterUserWordId=0, optional captured/explicit StopAtUserWordId and MaximumBatches=100. Null/range/schema/enclosing-transaction checks reject invalid invocation. Each ascending keyset batch runs in its own transaction and emits committed range/count information. PK NOT EXISTS uses UPDLOCK/HOLDLOCK; arbitrary errors/deadlocks roll back/rethrow and can be safely rerun by the operator. No global long-running enrollment transaction.

Every saved word is enrolled as New with count/interval/lapse=0, last review NULL, version=1 and interval-v1. EnrollmentAt=NextReviewAt uses the fixed supplied UTC reference. Valid explicit blank preference remains blocked; otherwise fallback is usable saved-POS definition ordered DisplayOrder/Id. A Unicode-whitespace predicate identifies unusable words/definitions without rewriting text. Existing states are skipped, never reset, and no quiz/preference/note/favorite/history/session rows are mutated.

Catch-up starts from 0 for coverage, capturing a fresh upper ID bound; new legacy inserts after that bound need another run. Resuming from the last committed key is suitable after interruption. Final activation requires retiring/quiescing old writers and zero missing states. The Phase 3 dual-enrollment hook does not exist yet. Preference/content changes to already enrolled records are detected by verification, not silently repaired by insert-only enrollment; controlled reconciliation remains later rollout/service work.

`Verify-F6Enrollment.sql` is read-only: EF ledger, global coverage and missing IDs, duplicate state/identity, wrong-owner/orphan checks, invalid preferred memberships, expected selected meaning/revision mismatches, clean-enrollment invariant, revision checks, baseline counts/sums and FK/index metadata. Queries are not a single consistent snapshot; final gate needs a controlled write pause. Counts alone do not prove preservation: secure pre/post row comparisons remain required. Neither script was executed on SQL Server; rerun/race/completeness behavior remains part of pending rehearsal.

## 8. Indexes, checks and ownership protections

Owner-key joins use UserWords `(UserId,Id)`, ReviewSessions `(UserId,Id)` and ReviewSessionItems `(UserId,Id,UserWordId)` alternate keys. Composite FK tests reject cross-user state, item/session/word and event/target relationships. EF adds necessary supporting FK indexes in addition to the plan's explicitly required indexes; none replaces the original saved-word identity.

Required indexes include owner/due/time/ID, owner/session-expiry/ID, session ordinal and target uniqueness, owner accepted-submission uniqueness filtered to non-null, owner/target/acceptance lookup, unique event item, unique owner/submission history and owner/target/review-time/ID history lookup. SQLite tests demonstrate pending NULL submissions coexist while accepted duplicate keys fail; SQL Server DDL generation includes the filter but live enforcement is still pending.

Shared SQL-subset checks enforce allowed progression/status/interval pairs, nonnegative lapses and positive versions/revisions, paired-null current meaning, session timing/counts, item option/ordinal/version bounds and all-null/all-present accepted fields, history next-version=previous+1 and permitted progression/provenance/timestamp bounds. Metadata tests verify no shadow FK columns or rowversion, explicit date/string sizes and required/optional receipt storage. SQL Server string lengths are column bounds; SQLite MaxLength metadata alone is not a demonstration of SQL Server length enforcement.

Only the state FK cascades through UserWord. Accepted audit/session references use NoAction so unmanaged deletion is rejected. SQLite tests prove explicit privileged ordering is required; SQL Server cascade-path/DDL/deletion behavior still needs live verification.

## 9. Concurrency, idempotency and immutability limits

ScheduleVersion, Session.Version and ContentRevision are portable application-managed EF tokens. Separate SQLite context connections demonstrate stale updates fail after another context persists a newer version; this is structural optimistic-concurrency evidence, not a simultaneous SQL Server answer race.

Schema uniqueness prevents duplicate state, accepted item/submission and history events, while persistent receipt columns enable later exact replay. Normal history operations must be append-only in Phase 3. No unapproved immutability trigger or global SaveChanges interception was introduced; EF/administrator writes can still alter rows if a caller is improperly authorized. P4 privileged retention/deletion remains explicit.

Phase 3 must enforce current definition belonging to the target word, option JSON validity/source revisions/unambiguous options, accepted submission matching event/receipt, receipt copies equality, genuine binary outcome matching exact before/after scheduler values, actual item/progress/completion consistency and schedule due arithmetic. It must atomically coordinate state/session/item/history/receipts and supply retries/commit-ambiguity recovery. Schema constraints alone do **not** guarantee end-to-end atomic submission or history truthfulness.

## 10. Tests added

`ReviewPersistenceModelTests` supplies 51 SQLite relational cases using the new isolated named-memory fixture with a keeper connection and a separately owned connection per context. Coverage includes creation/enrollment scheduler compatibility, personal-data preservation, state identity/ownership/FKs/nullable meanings, invalid checks/all valid intervals, tokens/revision defaults, session/item relationships/uniqueness/required snapshots/receipt coherence, owner-scoped submission keys, history/snapshot persistence/ownership/version checks, deletion behavior, current definition removal, non-UTC write rejection and unchanged canonical saved-word identity.

`ReviewMigrationDefinitionTests` supplies eight offline SQL Server provider checks for additive operations/no enrollment, composite FK/delete behavior, unique/filtered indexes, Down order, target/snapshot/model equivalence, generated SQL, column/token/type/nullability metadata and the new definition query's schema prerequisite. These assert connections remain closed.

Unmodified Phase 1 scheduler/DTO tests were rerun, as were targeted existing vocabulary contract/ownership, QuizApiTests, R5 migration definition and relational fixture tests. No whole-solution unrelated test suite, Angular tests or live provider calls were run.

## 11. Exact verification commands and results

Commands ran from repository root unless stated otherwise:

```powershell
dotnet ef --version
dotnet build VocabularyApp.Data/VocabularyApp.Data.csproj --no-restore
dotnet build VocabularyApp.WebApi/VocabularyApp.WebApi.csproj --no-restore
dotnet ef migrations add AddSpacedRepetition --project VocabularyApp.Data --startup-project VocabularyApp.WebApi --no-build
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewPersistenceModelTests|FullyQualifiedName~ReviewMigrationDefinitionTests|FullyQualifiedName~ReviewSchedulerTests|FullyQualifiedName~ReviewDtoContractTests"
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~VocabularyContractTests|FullyQualifiedName~VocabularyOwnershipApiTests|FullyQualifiedName~QuizApiTests|FullyQualifiedName~R5MigrationDefinitionTests|FullyQualifiedName~RelationalDatabaseFixtureTests"
dotnet ef migrations script 20260829155134_CorrectUserWordIdentity 20261013014406_AddSpacedRepetition --idempotent --project VocabularyApp.Data --startup-project VocabularyApp.WebApi --no-build --output "$env:TEMP\VocabularyApp-F6-Phase2-Up.sql"
dotnet ef migrations script 20261013014406_AddSpacedRepetition 20260829155134_CorrectUserWordIdentity --project VocabularyApp.Data --startup-project VocabularyApp.WebApi --no-build --output "$env:TEMP\VocabularyApp-F6-Phase2-Down.sql"
```

- CLI reported **9.0.9**; no tool/package installed. Runtime model/designer/snapshot remain EF **8.0.10**.
- Both targeted Debug builds: exit 0, **0 warnings, 0 errors**. No Release/production build.
- Migration scaffold and both offline script commands: exit **0**. Nothing was applied to a database.
- Final Phase 1/2 run: exit **0**, **157 passed, 0 failed, 0 skipped**, duration 2 seconds.
- Targeted legacy regression: exit **0**, **98 passed, 0 failed, 0 skipped**, duration 47 seconds.
- Total distinct tests across final selected runs: **255 passed, 0 failed, 0 skipped**. No claim that unselected tests passed.

Intermediate attempts are recorded honestly: the first focused command failed to compile because the generated snapshot type is internal (0 tests executed); the test was corrected to obtain the snapshot through IMigrationsAssembly without modifying generated visibility. The next run had **155 passed, 1 failed, 0 skipped**, because a test expected an unwrapped UTC conversion exception. It was corrected to assert DbUpdateException/inner InvalidOperationException and no saved state; the 156-test rerun passed in 5 seconds. A subsequently discovered plan verification inconsistency justified adding the schema-prerequisite query check and rerunning the focused selection. No failing production logic was masked.

Test compilation reported six pre-existing warnings in unchanged tests: xUnit2031 twice in ApiContractMetadataTests and three times in ApiContractShapeTests, plus xUnit1026 in ApiContractMetadataTests. No warning from new Phase 2 files was reported. No broad warning/dependency remediation was performed; all builds/tests used existing restore assets.

Temporary SQL artifact hashes (SHA-256): forward `DC9FA0E9D94C2E06139981F5EC17803462CEC0EDEB355AA8D4FA3DE0C3BBBA88`; rollback `3721D4E9CD242936872A72D45626AA32C91DD6FAF7AD559B806A5D070DE9B7FC`. These identify local generated files, not approved production release artifacts.

## 12. SQL Server rehearsal status

**PENDING / NOT EXECUTED.** No explicitly authorized disposable SQL Server database was available in the assignment. No production database was accessed to close the gap. The [rehearsal procedure](F6-phase-2-sql-server-rehearsal.md) defines required version/target ownership, pre-F6 restore/backup, explicit application of reviewed scripts, schema/default/FK/index/check inspection, repeated enrollment/interruption/catch-up, SQL Server integrity/concurrency/deletion checks, preserved-data evidence, query plans/locks and Down/restore rehearsal.

SQLite EnsureCreated checks the final relational model only; it does not execute the historical SQL Server migration chain or either enrollment script. Offline SQL Server script generation/model comparisons do not verify SQL Server DDL acceptance, lock/permission/cascade behavior, concurrent enrollment, old binary compatibility or restore readiness.

## 13. Deviations and unresolved questions

No approved schema/property/algorithm change. Minimal supporting mapping helper and design-time factory were added to keep UTC handling scoped and offline tooling independent of application authentication/production settings. A dedicated independent-connection fixture uses existing SQLite libraries. No new dependency or product architecture was introduced.

The prompt's session status/progress and history session/state references are implemented through the exact approved section 6 fields/navigation rather than inventing extra columns: CompletedAt/item counts and SessionItem.Session/UserWord.ReviewState. Append-only history remains a normal-operation service responsibility as approved. Live SQL Server rehearsal and P6–P7 remain pending; insert-only catch-up cannot repair later meaning mismatches and must not be represented as doing so.

**Finding recorded at Phase 2 completion, corrected below:** the original plan section 16 incorrectly claimed disabled flags permitted the new binary's legacy actions on a pre-F6 schema, while also requiring schema before rollout. A mapped WordDefinition query selects ContentRevision irrespective of future review flags, as the offline query test demonstrates. The new binary therefore needs the additive schema first. Phase 2 recommended old-binary/additive-schema and new-binary/additive-schema/reviews-disabled verification; it left the original plan unchanged pending review and did not alter approved fields or runtime behavior to satisfy the contradictory expectation.

**Documentation correction after Phase 2 review:** the implementation plan's sections 15–16 now explicitly require the additive F6 schema, including ContentRevision, before deploying the new binary, even with both flags disabled, and remove the pre-F6-query compatibility claim. The corrected sequence is recovery-ready backup → migration rehearsal/approval → additive schema under the compatible old application → existing-functionality checks → new binary with both flags disabled → existing-functionality checks again → enrollment/catch-up/reconciliation → separate activation approval. Section 17's deployment risk entry now matches that sequence. D1–D9/P1–P5 remain unchanged; P6–P7 and live SQL Server rehearsal remain unresolved release gates. This correction changed only the plan and this results document; no code, migration, script, test execution, deployment, Git write or Phase 3 work occurred.

## 14. Risks and technical debt

Production EF history/volume/version/compatibility/collation/permissions and backup/runtime activation remain unknown. Adding the alternate key/nonnullable revision/default/check can take locks; rehearsal must measure them. SQL Server constraint/filter/default/Unicode and migration behavior are generated but not execution-verified. Large initial backlog remains visible and sessions bounded by later services.

New NoAction links require review-aware privileged deletion/retention maintenance. Raw SQL dates require UTC discipline. ContentRevision does not automatically detect a writer that edits content without incrementing it; future coordinated writers/reconciliation must implement P2. Lifetime snapshots/receipts imply storage growth under P4. Git cleanliness/exact object history checks are limited by ownership restrictions. Deferred dependency auditing and existing quiz aggregate/process-local-session limitations remain separate debt.

## 15. Phase 2 acceptance checklist

- [x] Branch/ref/Phase 1 commit established through safe read-only files; Git inspection limitation and task-local preservation recorded.
- [x] Four exact approved review entities/mappings/DbSets added; existing saved-word identity preserved.
- [x] ContentRevision default/check/token implemented with no content service hook.
- [x] Additive migration/designer/snapshot generated and inspected; schema enrollment separated; no historical migration rewrite.
- [x] Required composite ownership/FK/delete/unique/index/check/string/date/token safeguards implemented.
- [x] Initial/catch-up insert-only script and separate read-only verification prepared with bounded transactions and parameters.
- [x] Applicable SQLite relational and offline SQL Server migration checks passed; directly affected Phase 1 and legacy tests passed.
- [x] Rehearsal preparation, commands/results/limits and Phase 3 service responsibilities documented.
- [ ] Live SQL Server upgrade/enrollment/concurrency/rollback/restore rehearsal executed and verified — pending authorized disposable environment.
- [x] No Phase 3/API/UI hooks, feature activation, production migration/enrollment/deployment or Git writes performed.

## 16. Phase 3 prerequisites

Stop for developer review and **separate explicit Phase 3 authorization**. Reinspect branch/source, account for any unrelated new changes, and review the model/migration/scripts plus pending SQL Server evidence. Follow the corrected section 16 compatibility expectation: old and new binaries are verified against the additive schema, with both review flags false for the new-binary existing-functionality check. Phase 3 must implement transactional enrollment/reset/content coordination, dedicated review service/controller, immutable per-answer receipt/history lifecycle, ownership/availability/error/Swagger integration and safe concurrency recovery while preserving quizzes/R7. No feature activation is warranted by this schema assignment.

Live SQL Server rehearsal must be completed before release/production migration readiness, with P6–P7 resolved. It remains an explicit outstanding requirement, even if the developer later authorizes Phase 3 before providing that environment.

## 17. Final readiness assessment

**CONDITIONAL COMPLETE — Phase 2.** All authorized local persistence implementation, additive migration generation, enrollment/rehearsal preparation and safe focused verification are complete. The pre-schema verification inconsistency has been corrected in documentation as noted in section 13; live SQL Server rehearsal and associated P6–P7 release gates remain outstanding. Local service/UI work is deliberately out of scope. Production migration readiness is not established. Stop here; no commit/push or Phase 3 work is authorized by completion.
