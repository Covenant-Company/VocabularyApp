# Documentation reorganization completion

Date: 2026-10-23 (America/New_York). Scope: documentation organization and reference corrections only.

## Inventory and final structure

Original direct-root inventory: **80 files: 77 Markdown documents and 3 SQL scripts**. All 77 Markdown documents were moved exactly once with their original filenames. No destination collisions were found; every resolved move stayed within `Docs/Updates`. No empty group was created. The new completion report is additional to the original inventory.

```text
Docs/Updates/
  CI-CD/                         14 documents
  F6-Spaced-Repetition/           3 general documents
    Phase-1/                     1
    Phase-2/                     2
    Phase-3/                     1
    Phase-4/                     1
    Phase-5/                     3
    Phase-6A/                    4
    Phase-6B/                    4
  Pronunciation-Audio/           5
  R4-Remediation/                5
  R5-Remediation/                4
  R6-Remediation/                4
  R7-Remediation/               12
  Security/                      3 PSH-1 documents
    R1-JWT-Secret/               4
    R2-Password-Hashing/         4
    R3-Canonical-Word/           3
  documentation-reorganization-completion.md
  R4-Phase-5-Quiz-Result-Duplicate-Audit.sql
  R4-Phase-6-Historical-Quiz-Audit.sql
  R4-Phase-6-Historical-Quiz-Reconciliation.sql
```

F6 totals 19 documents; Security totals 14. The dashboard review-count investigation is in F6 `Phase-6B`. The historical SQL Server rehearsal and baseline records remain available in their respective phase folders. The additional filename groups warranted descriptive folders; `Other` was unnecessary.

The three SQL scripts intentionally remain at the root: authorization covered moving Markdown documents, not relocating executable SQL. Their contents are unchanged; document links were rebased to continue pointing to them. This report is the only Markdown file intentionally at the root.

## References corrected

**163 Markdown link destinations/syntax corrections**, plus **44 other documentation-path occurrences**, for **207 reference corrections total**:

- 162 local Markdown destination corrections rebased document-to-document and source/migration links for their new depth, including preserving intended targets for three already-missing source files.
- One existing unescaped link containing spaces in the R7 analysis was made valid with angle brackets; it now resolves to `Docs/Vocabulary Builder — Plan of Action.md`.
- 40 explicit repository-path occurrences and 2 inline filename-path references were updated.
- Two non-executable `# Contracts:` comments were updated in `test-api.http` and `VocabularyApp.WebApi/VocabularyApp.WebApi.http`. HTTP request definitions and executable behavior were unchanged.

Reference corrections changed 34 moved Markdown files; 43 moved Markdown files retained identical content. Three Markdown files outside Updates also needed reference corrections: `Docs/README.md`, `Docs/Deployment/SmarterASP-Manual-Deployment.md`, and `Docs/TechDebt/Technical-Debt-Register.md`. Historical wording, conclusions, filenames and report evidence were preserved; only paths/link syntax changed.

## Unresolved historical references

Three source links in `R7-Remediation/R7-api-contracts-remediation-analysis.md` already targeted absent files before the move:

- `VocabularyApp.WebApi/DTOs/UserWordDTOs.cs`
- `VocabularyApp.WebApi/DTOs/WordRequest.cs`
- `VocabularyApp.WebApi/DTOs/ApiResponse.cs`

Their relative depths were corrected while preserving the historical intended targets. Replacement source files were not guessed; these remain unresolved. The links' historical discussion was retained.

Three plain-text root paths name proposed reports that were never present in the original inventory: `CI-CD-remove-production-manual-gate-implementation-results.md` (CI/CD implementation plan), `F6-verification-results.md` (overall F6 plan), and `R4-Historical-Quiz-Data-Audit-Results.md` (R4 implementation plan). These future-output references were left unchanged rather than inventing files or moving nonexistent documents. They are not stale references to any moved original.

Repository-wide text inspection also found two `.gitignore` entries for old `Docs/R2-...` locations. They do not point to the moved `Docs/Updates` files and were left unchanged: changing ignore behavior was outside scope. No other unresolved references to the old moved-document locations were identified in the inspected repository files. Generated/vendor/Git internals were excluded from the text-reference inventory.

## Verification and boundaries

Preflight inventoried destinations, checked collisions and resolved affected references before moving. After the moves, read-only byte comparisons verified that every inventoried original file existed at its expected path with the expected content; original moved paths were absent, destination filenames matched, and no document was lost. Expected changes were generated only by path substitutions. Subsequent edits were limited to the two contract comments and the single Markdown link syntax correction described above.

Read-only local-link verification across all moved Markdown files and the three other updated Markdown documents found **259 resolvable local links and the 3 pre-existing missing-source links above**. Path existence was checked; anchor semantics and external websites were not validated. No remaining old root-level path to a moved document was found in the affected documentation or HTTP comments. The original 77 Markdown documents remain, plus this report, for **78 Markdown files total** under Updates; all three original SQL files remain at the root.

No application source logic, configuration, migration, SQL script content or CI/CD behavior was changed. No SQL connection/query, database modification, application/build/backend/Angular test, dependency installation, deployment or production operation was performed. No Git command, ownership-safeguard bypass, Git configuration change, commit, merge, push or branch deletion was performed. All development/rehearsal databases and local/remote branches were preserved. Temporary organization/verification tooling and its inventory output were kept outside the repository.
