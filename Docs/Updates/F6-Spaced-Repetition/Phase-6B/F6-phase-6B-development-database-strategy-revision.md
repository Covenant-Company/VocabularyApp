# F6 Phase 6B development database strategy revision

Date: 2026-10-23 (America/New_York). **Documentation complete; plan execution not performed. Production: NO GO.**

## Authority and documents reviewed

The available authoritative Phase 6B execution sequence was B1–B13 in `../Phase-6A/F6-phase-6A-release-plan.md`. The pre-F6 baseline document is a preparation proposal, not the remaining behavioral plan. The specifically requested remaining-test plan, step-139M alternative-environment selection and step-139N infrastructure review were absent from this checkout. A new, explicitly authoritative [remaining development validation plan](F6-phase-6B-remaining-sqlserver-behavioral-test-plan.md) now replaces future development execution while preserving the old sequence and its historical context. Exact findings from the missing reports cannot be independently reconciled here.

Reviewed available documentation: the overall F6 implementation plan; Phase 1–5 implementation results; Phase 2 SQL Server rehearsal procedure; Phase 5 verification matrix and accessibility acceptance; Phase 6A readiness analysis, release plan, go/no-go checklist and risk/decision register; Phase 6B pre-F6 baseline preparation; and review API contract reference. Read-only source inspection covered the F6 migration, design factory, development target setting, ReviewTestHost/WAF provider and seed/lifecycle behavior, test inventory and Angular package scripts. No current runtime state was inspected.

## Files created and modified

Created:

- [F6-phase-6B-remaining-sqlserver-behavioral-test-plan.md](F6-phase-6B-remaining-sqlserver-behavioral-test-plan.md): authoritative revised implementation plan.
- [F6-phase-6B-development-database-strategy-revision.md](F6-phase-6B-development-database-strategy-revision.md): this completion report.

Modified only by adding a dated strategy/supersession pointer below the title:

- [F6-spaced-repetition-implementation-plan.md](../F6-spaced-repetition-implementation-plan.md).
- [F6-phase-6A-release-plan.md](../Phase-6A/F6-phase-6A-release-plan.md).
- [F6-phase-6A-production-readiness-analysis.md](../Phase-6A/F6-phase-6A-production-readiness-analysis.md).
- [F6-phase-6A-go-no-go-checklist.md](../Phase-6A/F6-phase-6A-go-no-go-checklist.md).
- [F6-phase-6A-risk-and-decision-register.md](../Phase-6A/F6-phase-6A-risk-and-decision-register.md).
- [F6-phase-6B-pre-F6-baseline-preparation.md](F6-phase-6B-pre-F6-baseline-preparation.md).

Completed implementation/test reports were not edited. Existing procedures, evidence tables and historical conclusions remain intact. The new pointer makes clear that the old rehearsal prerequisites no longer dictate the next development action; it does not clear production gates.

## Decisions and evidence limits

The owner selected existing `VocabularyAppDb_Dev` for remaining validation. Separate rehearsal database creation, VM/container/alternative SQL Server selection, connectivity/command-capture investigations and baseline rebuilding are SUPERSEDED for future execution. Preserve all databases, backups, scripts, projects, branches and historical migration/restore/E1/S1/S2/environment records. The owner reports successful historical migration rehearsal and E1; absent reports prevent independent review of their details, not recognition that the work occurred.

`20260829155134_CorrectUserWordIdentity` and `20261013014406_AddSpacedRepetition`, and their successful database update/build, are **REPORTED / NOT YET INDEPENDENTLY VERIFIED** for this revision. The earlier conversation contains execution success, but this documentation task performs no new ledger, metadata or behavioral verification. Neither migration success nor historical E1 means the remaining development scenarios have passed.

## Revised sequence and acceptance

1. **A — PENDING:** separately authorized read-only target/history/schema/data inspection. Repository configuration nominates `(localdb)\mssqllocaldb`; live identity remains to be verified. Check all expected migrations, four review tables, revision column and exact keys/FKs/indexes/checks against repository definitions.
2. **B — PENDING:** owner decides valuable-data preservation, backup/recovery procedure and test isolation before writes. Backup/restore execution is separately authorized. Protect existing users, canonical definitions, vocabulary, quiz and review history; no reset or automatic cleanup.
3. **C — PENDING:** enroll, start, resume, answer correctly/incorrectly, inspect history/schedule, replay/conflict, revision reset, expiry/ownership and quiz independence on SQL Server through existing application paths. E1-Dev/S1/S2 are retained as useful labels with historical mapping limits explicit. Review any harness's guards and writes first; no automatic retargeting.
4. **D — PENDING:** separately authorized backend/Angular regressions plus actual local browser workflow and manual acceptance. Record automated SQLite/Karma evidence separately from SQL Server/browser results.
5. **E — BLOCKED for production execution:** update readiness evidence after validation; retain production backup/recovery, schema-before-binary, pipeline coordination, default-disabled flags, smoke, rollback limits, hosting and explicit release/activation decisions.

Acceptance requires verified target/schema/history, approved protection/isolation, observed results for every required behavioral scenario, passing relevant regressions/browser acceptance and preservation comparisons. Unexecuted/blocked scenarios remain explicit and prevent an unqualified completion claim. Production remains NO GO regardless of local success until a separately reviewed production decision.

## Risks, unresolved decisions and smallest next step

The database may contain valuable data; its schema and effective application target are not freshly verified. Canonical definitions may be shared across users. Existing WAF tests use in-memory SQLite/EnsureCreated and cannot simply be redirected at this database. Controlled content/time access may need a small separately reviewed solution. LocalDB cannot prove production locking/load/commit ambiguity or hosting/key persistence; manual accessibility and production operational evidence remain gaps. Exact earlier S1/S2 definitions and detailed later rehearsal reports are unavailable here.

Outstanding owner decisions are the live target confirmation, preservation/recovery owner and objectives, backup destination/method, isolated test accounts/words, safe content/time testing and later execution scopes. Missing infrastructure reports do not revive the abandoned investigation path.

**Smallest next step requiring approval:** Stage A only—read-only connection and bounded identity, migration, metadata and data-inventory queries against `VocabularyAppDb_Dev`, returning sanitized findings. No builds, startup, tests, backups, migrations or writes in that scope. Decide protection and any write authorization after reviewing those findings.

## Task boundary and verification

This documentation revision made no SQL connection/query, database modification, migration/rollback, backup/restore, application startup, build/test/NuGet restore, source/settings/flag change, CI/CD modification, installation/provisioning, production access, Git mutation or branch deletion. Earlier authorized migration operations in the conversation precede this documentation task and are not included in this statement.

A read-only `git status --short` attempt was rejected by Git's ownership safeguard; no safety override or configuration change was made, and working-tree cleanliness is not claimed. File inventory, document text and local link checks are the verification basis. Only the eight documentation files listed above were written by this task. The revised plan was not executed.
