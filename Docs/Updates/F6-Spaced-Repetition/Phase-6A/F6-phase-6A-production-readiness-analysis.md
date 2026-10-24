# F6 Phase 6A production readiness analysis

> **Development strategy update, 2026-10-23:** The [revised authoritative Phase 6B plan](../Phase-6B/F6-phase-6B-remaining-sqlserver-behavioral-test-plan.md) supersedes the separate rehearsal/baseline/infrastructure path for future development validation. The owner selected `VocabularyAppDb_Dev`; the two migration successes remain reported pending independent verification. Earlier procedures, status tables and authorization statements below are historical, not the current development next step. Preserve prior successful migration/E1 and backup/restore evidence; no environment or artifact deletion is authorized. Production requirements remain applicable, production is NO GO, and execution requires separately scoped approval. See the [revision report](../Phase-6B/F6-phase-6B-development-database-strategy-revision.md).

Date: 2026-10-13. Assessment: **CONDITIONAL — REHEARSAL PREREQUISITES OUTSTANDING**. This assessment concerns permission to begin an isolated rehearsal, not production readiness. Phase 6A authorizes repository inspection and these four planning documents only. No tests, builds, SQL connections, scripts, migrations, enrollment, hosting probes, configuration changes or Git writes were performed.

## Baseline and evidence limits

The inspected branch is `feature/f6-spaced-repetition`, HEAD `e019fda780c60e7472dcb55058c884c018c7365e` (Complete F6 Phase 5 integrated verification), with Phase 4 commit `7e387827907cff603b8e673d1ceeedd85c9bf73d` immediately preceding it. Normal read-only Git commands were rejected by Git's ownership safeguard. No safeguard was overridden or configuration changed. HEAD, refs and commit objects were read directly; an index comparison allowing checkout newline normalization found no initial tracked differences. Source/configuration/document hashes were captured for preservation verification. This is a qualified repository baseline, not a claim that ordinary `git status` succeeded.

Evidence classes used here: **repository fact** means inspected source or a committed execution record; **historical execution** means an earlier phase's reported result, not rerun in Phase 6A; **manual outstanding** means target-specific evidence unavailable in the repository. No production state or provider capability is inferred from a script or an old successful deployment.

## Reviewed F6 records

| Record | Interpretation against current source |
|---|---|
| [Original analysis](../F6-spaced-repetition-analysis.md) | Design rationale; proposed transaction/execution-strategy language is not proof of current SQL Server behavior. |
| [Implementation plan](../F6-spaced-repetition-implementation-plan.md) | D1–D9/P1–P5 approved; historical phase authorization text is not current authorization. P6/P7 remain unresolved. |
| [Phase 1](../Phase-1/F6-phase-1-implementation-results.md) | Scheduler, DTOs and false flag defaults implemented; later binding is now present in Program.cs. |
| [Phase 2](../Phase-2/F6-phase-2-implementation-results.md) | Mappings, migration, insert-only enrollment and verification scripts implemented; no live SQL Server execution. |
| [SQL rehearsal](../Phase-2/F6-phase-2-sql-server-rehearsal.md) | Useful baseline procedure; its statements that Phase 3 hooks/services are future work are superseded by current source. Expanded rehearsal appears in the Phase 6A release plan. |
| [Phase 3](../Phase-3/F6-phase-3-implementation-results.md) | Four authenticated routes, receipt recovery and coordinated writers implemented; provider behavior still requires SQL Server rehearsal. |
| [Phase 4](../Phase-4/F6-phase-4-implementation-results.md) | Angular review experience implemented; mocked HTTP is not a full browser/backend exercise. |
| [Phase 5 results](../Phase-5/F6-phase-5-implementation-results.md) | Local regression evidence; conditional completion preserves operational gaps. |
| [Verification matrix](../Phase-5/F6-phase-5-verification-matrix.md) | Distinguishes SQLite, wire captures, browser mocks and unexecuted acceptance. |
| [Accessibility acceptance](../Phase-5/F6-phase-5-accessibility-acceptance.md) | Automated DOM/layout evidence exists; all manual rows remain pending. |
| [Review contract](../F6-review-api-contract-reference.md) | Current endpoint behavior must be read with Phase 3 implementation update; old planned wording is not a missing implementation. |

Hosting/release review included [manual deployment guide](../../../Deployment/SmarterASP-Manual-Deployment.md), [R5 production deployment](../../R5-Remediation/R5-production-deployment.md), PSH-1 analysis/plan/completion, manual-gate-removal analysis/plan/completion and Web Deploy remediation records, cross-checked against the actual [workflow](../../../../.github/workflows/backend-tests.yml), [publisher](../../../../scripts/ci/Publish-VocabularyApp.ps1), [deployer](../../../../scripts/ci/Deploy-SmarterAsp.ps1), [release guard](../../../../scripts/ci/Assert-ProductionReleaseCandidate.ps1), [HTTPS acceptance](../../../../scripts/ci/Test-ProductionHttps.ps1) and [root web.config](../../../../VocabularyApp.WebApi/web.config). Historical manual Node/runtime/publication guidance does not override the current CI artifact format. Gate-removal completion records reviewers removed and an automatic deployment in September; current remote settings remain unverified.

## Architecture and release dependencies

Current [Program.cs](../../../../VocabularyApp.WebApi/Program.cs) binds `Reviews`, registers the scheduler/coordinators/review service and Data Protection, uses SQL Server, JWT authentication and authenticated fallback authorization, and does not run migrations at startup. [ReviewOptions](../../../../VocabularyApp.WebApi/Configuration/ReviewOptions.cs) defaults `Enabled` and `EnrollmentEnabled` to false; neither inspected appsettings file supplies a Reviews override. Effective hosting overrides are unknown.

`interval-v1` is deterministic server scheduling: correct answers advance the 1/3/7/14/30/60/90 day ladder, capped at 90; incorrect answers reset progression to Learning/one day and increment lapses. Genuine effective meaning/content changes reset progression and increment scheduling versions without erasing enrollment or accepted history. Review state and quiz counters remain independent. UTC instants are enforced in application conversion; SQL `datetime2(7)` carries no timezone by itself.

The four review tables separate current schedule, durable sessions, immutable item snapshots/accepted receipts and append-only history. Submission recovery uses the original owner-scoped submission ID and immutable answer tuple. Identical replay returns the stored receipt rather than advancing a schedule again. State/session/content versions and ownership constraints supplement short serializable transactions. Bounded recognized-error retries and receipt rereads handle known conflicts and commit ambiguity; an unresolved error is not proof that a commit failed. No SQL Server `EnableRetryOnFailure` configuration is present; provider retry/locking outcomes must be tested rather than inferred from SQLite.

Enrollment-enabled new vocabulary/preference writers coordinate state. Duplicate add does not silently enroll every legacy row. DI-only canonical content maintenance protects revision/reset invariants; there is no public editor or general operator reconciliation endpoint. Old binaries/direct SQL writers bypass these hooks. Insert-only catch-up cannot repair existing meaning/revision mismatches. A reviewed reconciliation method and writer retirement/quiescence are activation prerequisites.

Due cursors are Data Protection tokens scoped to owner, as-of and ordering with a 30-minute validity. Session snapshots, receipts and history live in SQL and are not encrypted cursor payloads. Losing a cursor key invalidates pagination tokens; it does not itself delete durable sessions or receipts. `AddDataProtection()` has no explicit persistence, encryption-at-rest or stable application-name configuration in Program.cs. Default host behavior must be verified, not assumed durable.

Angular uses the existing JWT API service and protected lazy review route/R7 envelopes. Owner-scoped sessionStorage retains session ID and the exact pending answer/submission UUID before sending. Retry preserves that tuple through uncertain failures, and replay precedes resume; accepted data comes from server receipts. It stores no JWT or answer-key snapshots in that review recovery record. Owner changes clear recovery state. A browser's closed-tab storage lifetime differs from database receipt retention.

## Verified migration identity and shape

Identity is **`20261013014406_AddSpacedRepetition`**, established by `[Migration(...)]` and `[DbContext(typeof(ApplicationDbContext))]` in [designer](../../../../VocabularyApp.Data/Migrations/20261013014406_AddSpacedRepetition.Designer.cs), namespace `VocabularyApp.Data.Migrations`, partial migration class and Data project/assembly. It follows repository migration `20260829155134_CorrectUserWordIdentity`. Actual target ledger/order is manual outstanding. [Design factory](../../../../VocabularyApp.Data/ApplicationDbContextDesignFactory.cs) uses a fixed design-only LocalDB connection; it neither proves a live target nor authorizes an update.

[Up/Down](../../../../VocabularyApp.Data/Migrations/20261013014406_AddSpacedRepetition.cs) and model metadata show:

- `WordDefinitions.ContentRevision`: bigint, required, default 1, positive check and application concurrency token. New ownership alternate key `(UserId,Id)` on UserWords; existing vocabulary identities retained.
- `UserWordReviewStates`: saved-word PK, paired meaning/revision, UTC timestamps, interval/status/count/version/algorithm checks; owner/saved-word composite FK cascades state only; current definition FK NoAction. Due index `(UserId,NextReviewAt,UserWordId)` plus ownership/definition indexes.
- `ReviewSessions`: Guid identity and owner alternate key; expiry/count/version/time checks, owner FK NoAction, owner/expiry index.
- `ReviewSessionItems`: owner/session and owner/saved-word NoAction FKs, immutable Unicode snapshots/options, captured versions, option/ordinal/time and all-null-or-complete receipt checks. Unique session/ordinal and session/saved-word, unique filtered `(UserId,AcceptedSubmissionId)` excluding NULL; owner/item/saved-word alternate key.
- `ReviewHistories`: owner/item/saved-word and owner/saved-word NoAction FKs; unique session item and owner/submission; owner/saved-word/time/ID history index. Historical definition IDs deliberately have no canonical-definition FK so snapshots survive content removal.
- Versions are bigint concurrency values, not SQL rowversion. Checks constrain stored fields but do not prove JSON structure, receipt agreement, scoring or exact due arithmetic; those depend on transactional services.
- Up adds schema without enrolling rows. Down drops history, state, items, sessions, then the revision check, ownership key and revision column. It destroys F6 learning/recovery data and is not the ordinary production rollback.

## Critical schema-before-binary constraint

The new EF model selects mapped `ContentRevision` during ordinary definition queries even when both flags are false. **Feature flags do not make the new binary compatible with the old schema.** Publishing/deploying first can break vocabulary and quiz behavior before reviews activate.

Required order: verified backup/restoration capability → approved schema → schema/integrity verification → prove previous binary on additive schema → deploy new binary with both flags false → legacy smoke → separately authorized enrollment coordination/catch-up/reconciliation → separately authorized review activation → monitoring. Each transition has stops/recovery in the [release plan](F6-phase-6A-release-plan.md).

## Pipeline and host findings

The actual workflow runs on a push to `master`: backend tests, Angular ChromeHeadless tests, production builds, static staging, offline packaging/security/release-guard checks, exact artifact upload/download, serialized production deploy, then HTTPS acceptance. It uses Windows 2022, .NET 8.x and Node 22.x. The package is framework-dependent portable net8.0, without apphost; ANCM V2 launches `dotnet .\VocabularyApp.WebApi.dll` in process. Publishing verifies Angular hashes and rejects SQL/scripts, development config, key/certificate files and nested web.config. No migration or database-readiness gate exists.

The guard checks candidate SHA against live master immediately before msdeploy; it prevents stale candidate deployment, not a schema-incompatible current master. Concurrency and AppOffline are not migration gates. DoNotDelete retains destination-only files but does not protect overwritten same-name config/files or establish persistent key storage. Failed synchronization can leave partial content/offline state; HTTPS failure occurs after deployment and requires manual recovery. No automatic rollback exists.

Existing workflow can support a manually coordinated schema-first release **only if owners prevent the master push until schema and all prerequisites are proven**, account for queued unrelated releases, and authorize that push as production deployment. A separate explicit schema-readiness/manual gate change could be proposed and reviewed if that coordination cannot be assured; none is implemented here. A named `production` environment must not be presented as proof of an approval gate. Old artifact redeployment can conflict with the stale-SHA guard; recovery route must be approved beforehand, without casually bypassing it.

Manual outstanding: host .NET/ANCM support and patch level, SQL engine/compatibility/collation, effective Production environment, actual process topology/site paths and identity permissions, recycle behavior, override persistence across content sync/restart, key directory/application discriminator/encryption and key sharing, backup/log access, monitoring and remote environment protection. In-process hosting is a package fact, not a verified provider configuration.

## Transport, acceptance and remaining evidence

IIS rules reject HTTP API requests and non-GET/HEAD methods with 403/no redirect; navigation uses a fixed canonical HTTPS destination preserving the original target. Program.cs leaves HTTP authority to IIS, enables development-only CORS and applies Production HSTS only to exact host `myvocabularybuilder.org`, max-age 300, without includeSubDomains/preload. No custom broad forwarded-header trust is configured; actual IIS/proxy HTTPS/Host interpretation needs acceptance. Existing HTTPS script checks transport, anonymous Bearer 401, absent production CORS and same-origin assets; it does not test authenticated reviews or database readiness. Real authenticated smoke must later use securely supplied synthetic/test identities and never send credentials over HTTP.

Historical Phase 5 evidence reports 682 backend tests passing separately in Debug and Release, 265 Angular tests, successful Release/Angular production builds, six existing xUnit warnings and two existing WordLookup Angular warnings. SQLite/WAF, captured real backend wire/OpenAPI fixtures and Angular parsers/template tests are valuable but do not prove SQL Server races, hosting or a live browser connected to the backend. Phase 6A reran none of them. Local toolchain differed from CI; the eventual release SHA needs its own CI artifact evidence.

No E2E framework is configured. Phase 6B can perform manual real browser-to-isolated-backend testing without adding dependencies; automated framework installation would require separately scoped approval. All physical keyboard/touch, actual 320px viewport, 200% browser zoom and named screen-reader acceptance remains pending. Existing DOM/container tests are not replacements.

No rehearsal target, owner, restore proof, performance thresholds, effective host settings or manual acceptance evidence is supplied. No confirmed source defect currently requires the BLOCKED classification, but these are enforceable stop gates. See [checklist](F6-phase-6A-go-no-go-checklist.md) and [risk/decisions](F6-phase-6A-risk-and-decision-register.md). Production go/no-go remains **NO GO pending evidence and separate authorization**.
