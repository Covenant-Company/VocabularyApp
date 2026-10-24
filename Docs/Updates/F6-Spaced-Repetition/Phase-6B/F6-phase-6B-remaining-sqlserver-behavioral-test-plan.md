# F6 Phase 6B remaining development database validation plan

Date: 2026-10-23 (America/New_York). **Authoritative revised Phase 6B implementation plan. Documentation only; execution is not authorized. Production: NO GO.**

## Authority and strategy change

The owner has discontinued the separate rehearsal-database approach for remaining F6 development validation and selected `VocabularyAppDb_Dev`. This plan supersedes the future Phase 6B execution sequence B1–B13 in the [Phase 6A release plan](../Phase-6A/F6-phase-6A-release-plan.md), the [pre-F6 baseline preparation](F6-phase-6B-pre-F6-baseline-preparation.md), and infrastructure prerequisites in older F6 plans **for remaining development execution only**. Production requirements remain separate.

The requested pre-existing `F6-phase-6B-remaining-sqlserver-behavioral-test-plan.md`, step-139M environment-selection document and step-139N infrastructure review were not present in this checkout. Before this revision, the available operative Phase 6B sequence was the Phase 6A release plan; the baseline document was a prerequisite proposal, not the remaining behavioral implementation plan. This clearly identified replacement preserves those historical documents rather than rewriting their procedures. Missing reports limit reconciliation of their exact scenario identifiers and findings; they do not require more infrastructure investigation.

VM/container provisioning, alternative SQL Server selection, connection/command-capture investigations, separate baseline construction and repeated migration rehearsal are **SUPERSEDED for future execution**. Preserve all prior databases, backups, scripts, test projects, branches and reports. Historical migration rehearsal and E1 enrollment testing produced successful results according to the owner's supplied history; do not relabel them failures or erase backup/restore, S1/S2 planning, connectivity or environment-assessment evidence.

## Evidence and status

| Item | Status | Evidence and limit |
|---|---|---|
| Phases 1–5 implementation and recorded local verification | COMPLETE within each report's scope | [Phase 1](../Phase-1/F6-phase-1-implementation-results.md), [2](../Phase-2/F6-phase-2-implementation-results.md), [3](../Phase-3/F6-phase-3-implementation-results.md), [4](../Phase-4/F6-phase-4-implementation-results.md), [5](../Phase-5/F6-phase-5-implementation-results.md). Historical results, not rerun here; conditional release gaps remain. |
| Earlier migration rehearsal, backup/restore and E1 | Historical successful migration rehearsal/E1 retained; detailed evidence unavailable here | Owner-supplied history. Later execution reports are missing from this checkout; older Phase 2/6A documents describe their own earlier pending state. Do not invent backup/restore outcomes or transfer E1 success to this database. |
| `20260829155134_CorrectUserWordIdentity` on `VocabularyAppDb_Dev` | REPORTED / NOT YET INDEPENDENTLY VERIFIED | Owner-supplied migration/build success, also reported earlier in this conversation; no fresh ledger/schema verification in this task. |
| `20261013014406_AddSpacedRepetition` on `VocabularyAppDb_Dev` | REPORTED / NOT YET INDEPENDENTLY VERIFIED | Same evidence boundary; migration success proves no behavioral scenario by itself. |
| Separate rehearsal infrastructure path | SUPERSEDED | Owner-approved development strategy change. |
| Stages A–D below | PENDING | Target verification, protection and new execution evidence required. |
| Writes before protection/authorization; production deployment | BLOCKED | No write authorization in this task; production remains NO GO pending applicable gates and explicit approval. |

## Stage A — read-only development database verification

Smallest next execution scope: separately authorize a read-only inspection of `VocabularyAppDb_Dev`, with no application startup, build, migration, backup or test execution. Repository development settings nominate `(localdb)\mssqllocaldb`; this is configuration evidence, not verified live instance identity. Confirm resolved instance/server, database name, version, edition, compatibility and collation. Check effective application target without exposing credentials; the design factory instead selects `VocabularyApp_F6_DesignOnly` and must not be used as the runtime-target authority.

Compare the full EF history with repository migration attributes, including both reported IDs and all earlier entries; do not assume a fixed count from an older plan. Verify `ReviewSessions`, `ReviewSessionItems`, `ReviewHistories`, `UserWordReviewStates`, and `WordDefinitions.ContentRevision` (bigint, required, default 1, positive check). Compare metadata with the migration/designer and current mappings: ownership alternate keys/composite FKs, delete actions, trusted/enabled checks and FKs, unique and filtered submission/history/session indexes, due/history indexes, types and nullability. Check for missing/disabled/untrusted objects, duplicates/orphans and obvious ledger/schema inconsistencies through bounded read-only queries.

Inventory application row counts and existing users/vocabulary/review/quiz data without publishing private contents. Ask the owner which data is valuable and must be preserved; until resolved, treat all existing data as valuable. Record sanitized expected/observed results and discrepancies. Wrong identity, unexpected schema/history or excessive read load means stop and report; no automatic repair, migration rerun or ledger edits.

## Stage B — protect existing development data

Before any write test, identify the backup/recovery owner, supported backup method for the verified engine, protected destination, retention, acceptable recovery time/data loss and how recovery will be verified. Prefer a native recoverable backup where supported; do not copy live database files as an assumed backup. Any backup or restore execution needs separate authorization; a recovery-check destination, if needed, is solely for protection and is not a revived behavioral rehearsal environment.

Approve a unique run identifier, two dedicated synthetic accounts, their saved-word IDs and exclusively test-owned canonical words/definitions. Use at least four distinct usable meanings, plus isolated blocked/revision cases. Never reuse an existing canonical definition for mutation: it may affect other users. Capture protected pre-test comparisons for existing vocabulary, preferences, notes/favorites, quiz counters/history and review state/history/receipts. Do not bulk-enroll real users, reset the database, run destructive Down, delete records or perform automatic cleanup. Retain test artifacts unless narrowly scoped cleanup is separately approved.

Stop procedure: stop issuing test writes on unexpected identity, ownership, count or data changes; pause the dedicated test client and isolate the test process, preserve sanitized requests/receipts and outcome evidence, and notify the owner. Do not retry with a new submission ID, repair data, toggle shared settings or restore automatically. Assess committed outcomes read-only under the approved scope; any repair/restore needs a separate recovery decision accounting for unrelated writes since backup.

## Stage C — focused application behavior on SQL Server

After A/B acceptance and explicit write authorization, use existing application services/API and Angular workflow with dedicated accounts. Local flag changes, app startup, controlled time/fault hooks or test-data writes must be included explicitly in that later scope. Keep production untouched. Record per scenario target, source revision, operator/time, isolated IDs, requests/status, expected/observed result and SQL state/history/receipt comparison. Each scenario starts PENDING.

| Scenario | Action and acceptance evidence |
|---|---|
| E1-Dev enrollment | Enroll an eligible test-owned saved word through supported coordination. Exactly one state with selected meaning/revision and initial due schedule; repeat preserves state and creates no answer history. Earlier E1 remains historical evidence, not a pass here. |
| S1 start/resume | Start a ready session and resume its ID; immutable item/option order, accepted receipts and expiry preserved, no extra state advancement. This retains S1 as a useful label; exact mapping to unavailable earlier S1 planning needs reconciliation. |
| S2 correct/history | Submit a correct answer; one accepted receipt/history event and one version advance, next review +1 UTC day for a new state. Verify due boundary and later 1/3/7/14/30/60/90-day progression using existing controlled-time support only if safely scoped; otherwise remaining intervals stay pending. S2 label mapping to missing reports is provisional. |
| C4 incorrect | A separate eligible test item answered incorrectly resets count to 0, Learning/+1 UTC day, increments lifetime lapse once and preserves prior history. |
| C5 replay/conflict | Exact same owner/submission/session/item/option returns original receipt without another history/version advance; changed payload conflicts. Include same-key concurrent requests using existing practical support; unexecuted races/ambiguous commit cases stay pending. |
| C6 revision reset | Change only an exclusively test-owned definition via the controlled content service; genuine edit increments revision, resets affected schedule, invalidates stale pending items and preserves history/lapses/quiz data. Cosmetic/no-op changes preserve schedule. No direct SQL bypass or new public editor. |
| C7 expiry/ownership | Resume/new answer fails at expiry boundary; accepted exact replay retains original result. Second account cannot read/answer the first account's session/items/receipts; same UUID across owners remains isolated. Use an existing safe clock hook or wait for real expiry; do not alter system time/shared rows. |
| C8 quiz independence | Ordinary quiz changes only expected quiz data/counters; review submissions change only expected review data. Compare protected existing-user data and isolated test data before/after. |

Review any previous rehearsal harness before reuse: exact target guards, provider substitution, seed/reset/delete/EnsureCreated/EnsureDeleted paths, transactions, disposal and cleanup. **Do not point it at `VocabularyAppDb_Dev` automatically or weaken guards to force it to run.** Current WAF infrastructure uses SQLite in-memory, calls EnsureCreated and seeds users/words; it is automated regression support, not an approved development-database runner. Prefer normal application requests and narrowly scoped verification over a new test platform. If no safe controlled content/time path exists, record the case as blocked pending a small reviewed solution; no source change is authorized here.

## Stage D — regression and browser acceptance

Separately authorize backend and Angular test execution after reviewing target isolation and dependency availability. Reuse authentication, word lookup/add/preferred-definition, quiz and review contract/session/enrollment/content/concurrency tests, and Angular review/service/error/idempotency tests. Suggested existing entry points are the WebApi.Tests project and UI `npm test` headless configuration; no install/restore/build/test is authorized by this plan revision. Record actual commands, provider, source revision and results; failures/skips remain explicit.

Maintain two evidence columns: automated .NET/SQLite/Karma results and actual SQL Server/application/browser observations. Historical Phase 5 reports 682 backend tests in each Debug/Release run and 265 Angular tests; those are not fresh results or SQL Server proof. Exercise real browser login, lookup, add/preferences, review start/resume/submit/recovery and ordinary quiz against the approved local backend. Complete the [manual accessibility checklist](../Phase-5/F6-phase-5-accessibility-acceptance.md) or leave its gaps explicit. No extra framework or infrastructure is required by default.

## Stage E — production readiness review, not deployment

After development results, update the [6A checklist](../Phase-6A/F6-phase-6A-go-no-go-checklist.md) with actual evidence and unresolved limits. LocalDB behavior does not establish production engine/load/locking/failure behavior. Retain applicable SQL concurrency/commit ambiguity, performance, old-binary compatibility, hosting/key persistence, browser/accessibility and monitoring gaps as production dependencies; missing tests are not waived by this shorter development path.

Production requires verified target-specific backup/recovery, migration order/base/schema compatibility and preserved existing data. **Apply and verify the F6 migration before deploying the F6 binary, even with flags disabled.** Existing master-push deployment applies application files and does not apply migrations; no master merge/push is authorized. Start with `Reviews:EnrollmentEnabled=false` and `Reviews:Enabled=false`; authorize enrollment/reconciliation separately, then activation only after coverage/writer coordination and signoff. Plan authenticated post-deployment legacy/review/replay/ownership/quiz smoke, effective persistent flag controls, monitoring and a recovery owner. Feature disable retains schema/history; prior-binary recovery requires compatibility and an approved artifact route. Down destroys review data; restore can lose unrelated post-backup writes. No CI/CD or configuration change is authorized. Production remains **NO GO** until separately reviewed and explicitly approved.

## Acceptance, approvals and unresolved decisions

Development validation is accepted only when A identity/history/schema checks pass; B data-value/recovery/isolation decisions are approved; every C scenario has observed SQL Server evidence or an explicit unresolved block; D regressions and actual browser/manual acceptance have recorded outcomes; and existing data is unchanged except approved isolated actions. A blocked or unexecuted required scenario prevents claiming full completion. Stage E must record a separate production decision; local completion never implies production GO.

Separate approvals are required for: (1) A read-only connection/queries; (2) backup/recovery execution; (3) synthetic data, application startup/local flags and bounded C writes; (4) builds/restores/backend/Angular tests and browser execution; (5) any cleanup, repair, restore or code/tooling remedy; and (6) production inspection, backup/schema/deployment/enrollment/activation, each explicitly scoped. The owner's strategy choice and earlier migration authorization do not authorize these new actions.

Outstanding decisions: live target/effective configuration, valuable-data inventory and preservation owner, backup method/destination/recovery objectives, isolated accounts/words, safe content/time access, exact historical S1/S2 mapping, and production-specific evidence gaps. None requires resuming VM/container/command-capture investigation. **Next step: approve Stage A only, then review its sanitized findings before approving protection or writes.**
