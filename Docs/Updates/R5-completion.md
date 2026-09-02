# R5 — UserWord Identity Remediation Completion

## 1. Executive Summary

R5 corrected saved-word identity so each user can own at most one `UserWord` for a canonical `Word`, regardless of part of speech. Production now enforces unique `(UserId, WordId)` through `IX_UserWords_UserId_WordId`.

Duplicate saves are idempotent, preferred-definition changes update the existing saved row, and `PartOfSpeechId` remains required synchronized compatibility state rather than identity. Existing notes, favorites, counters, timestamps, quiz history, and sample-sentence relationships were preserved.

R5 was implemented, tested, migrated, deployed, and verified in production. All production audits, preservation checks, referential-integrity checks, and application smoke tests passed. No rollback was required.

## 2. Original Problem

Before R5, the database and application treated part of speech as part of saved-word identity:

| State | Enforced identity | Consequence |
|---|---|---|
| Before R5 | Unique `(UserId, WordId, PartOfSpeechId)` | One user could save the same canonical word more than once under different parts of speech, splitting user-owned state and dependent history across multiple `UserWord` rows. |
| After R5 | Unique `(UserId, WordId)` | One user has one stable saved row per canonical word; part of speech can change with the selected definition without changing identity. |

The earlier rule conflicted with the product's canonical-word behavior. It also left duplicate prevention dependent on the submitted part of speech and allowed concurrent check-then-insert requests to race.

Production contained no duplicate `(UserId, WordId)` groups before migration, so R5 did not introduce merge or deletion machinery. The migration instead fails safely if unexpected duplicate pairs exist.

## 3. Final Design

- **Canonical identity:** `(UserId, WordId)` is the complete saved-word identity and is protected by a database unique index.
- **`PartOfSpeechId`:** remains on `UserWord`, required and populated. It is synchronized from the preferred definition when one is selected, but it is not an identity component.
- **Duplicate saves:** a repeated save returns the existing stable `UserWord` as successful idempotent behavior; no second row is inserted and existing user state is not reset.
- **Preferred definitions:** selecting another definition updates the existing `UserWord` in place and synchronizes `PartOfSpeechId`. It does not replace the row or create another saved word.
- **Concurrency:** the database constraint is the final guard. Expected unique-key races are classified narrowly, the losing pending insert is detached, and the winning row is reloaded and returned. Unrelated database failures remain failures.
- **Data preservation:** R5 never merges, deletes, replaces, or reparents existing saved words or their dependents.

## 4. Implementation Summary

R5 made narrowly scoped schema, backend, and Angular changes:

- changed EF uniqueness from `(UserId, WordId, PartOfSpeechId)` to `(UserId, WordId)`;
- added a fail-fast migration precondition for unexpected duplicate pairs;
- changed add-to-vocabulary lookup and responses to the canonical two-column identity;
- made sequential and concurrent duplicate adds deterministic and idempotent;
- validated a supplied preferred definition against the canonical word;
- updated preferred definitions on the existing row while synchronizing POS;
- retained the API route and request compatibility while adding typed result metadata; and
- updated Angular handling to report repeat saves accurately and preserve the same saved-word identity.

R5 did **not**:

- remove or make nullable `PartOfSpeechId`;
- merge or delete duplicate data;
- change `UserWord` primary keys or dependent foreign keys;
- alter favorite, note, learning-counter, review-timestamp, quiz, or sample-sentence semantics;
- redesign quiz sessions, dictionary providers, or unrelated vocabulary functionality; or
- include the separately identified pronunciation-audio concern.

## 5. Database Migration

Migration: `20260829155134_CorrectUserWordIdentity`

The migration performs three operations in `Up`:

1. Execute a SQL Server duplicate-pair precondition and `THROW 51000` if any `(UserId, WordId)` group has more than one row.
2. Drop `IX_UserWords_UserId_WordId_PartOfSpeechId`.
3. Create unique `IX_UserWords_UserId_WordId` on `(UserId, WordId)`.

`Down` drops the two-column index and restores the prior composite index. The migration changes no columns, foreign keys, or row data.

Final production database identity:

```text
IX_UserWords_UserId_WordId
UNIQUE (UserId, WordId)
```

## 6. Automated Verification

| Verification area | Result |
|---|---|
| Backend test suite | **166 / 166 passed** |
| .NET build | **Succeeded** |
| R5-focused Angular tests | **13 / 13 passed** |
| Angular production build | **Succeeded** |
| Full Angular suite | 19 / 26 passed; seven unrelated legacy failures were documented outside R5 scope |
| Clean full migration chain plus R5 | **Passed** on isolated SQL Server LocalDB |
| Representative data upgrade | **Passed** with stable `UserWord.Id` and preserved notes, favorite, counters, `QuizResult`, and `SampleSentence` |
| Down migration | **Passed**; prior composite index restored |
| Deliberate duplicate-pair precondition | **Passed** by failing with SQL error 51000 as designed |
| Fail-fast transaction behavior | **Passed**; no partial schema/history change and both deliberate duplicate rows remained untouched |

The migration verification covered the real migration chain, final index metadata, required/non-null POS, nullable preferred definition, preservation of user-owned state and dependents, successful rollback, and safe rejection of incompatible data.

## 7. Production Deployment

Production deployment followed the controlled R5 checklist:

1. A SmarterASP MSSQL backup was completed successfully before migration.
2. The pre-migration audit passed with zero duplicate pairs, zero composite duplicates, zero null POS values, and zero preferred-definition consistency errors.
3. Writes were quiesced with `/vocabularyapp/app_offline.htm`.
4. A final duplicate-pair check returned zero rows.
5. Migration `20260829155134_CorrectUserWordIdentity` applied successfully and its fail-fast precondition passed.
6. The old composite index was removed and the unique two-column index was created.
7. Clean backend publish and Angular production artifacts were deployed to the SmarterASP application.
8. Post-migration SQL, migration-history, preservation, orphan, and application smoke verification passed.
9. The maintenance file was removed only after deployment verification.

Application smoke tests passed for application load, login, word lookup, new-word save, duplicate-save idempotency, preferred-definition update in place, favorites, notes, and quiz/history behavior.

## 8. Production Incident and Resolution

After application deployment, login initially returned HTTP 401. Temporary stdout logging was enabled in `web.config` and showed the application attempting to connect to `(localdb)\mssqllocaldb`.

The root cause was a missing `ConnectionStrings__DefaultConnection` variable in the SmarterASP application-pool environment. Without the production override, the deployed application fell back to the LocalDB value in `appsettings.json`.

Resolution:

- `ConnectionStrings__DefaultConnection` was added securely in SmarterASP Pool Manager;
- the existing `JwtSettings__SecretKey` and `WordsApi__ApiKey` variables were confirmed without exposing their values;
- the application/site was restarted;
- login was retested successfully; and
- `web.config` was returned to `stdoutLogEnabled="false"`.

No secret values were recorded. No rollback was required, and the successfully verified R5 database migration remained valid throughout the incident.

## 9. Production Data Verification

The pre- and post-migration preservation values matched exactly:

| Measure | Before migration | After migration | Result |
|---|---:|---:|---|
| `UserWords` | 35 | 35 | Preserved |
| `QuizResults` | 18 | 18 | Preserved |
| `SampleSentences` | 0 | 0 | Preserved |
| Favorite `UserWords` | 4 | 4 | Preserved |
| `UserWords` with notes | 0 | 0 | Preserved |
| Sum `TotalAttempts` | 18 | 18 | Preserved |
| Sum `CorrectAnswers` | 15 | 15 | Preserved |

Final production checks also confirmed:

- unique `IX_UserWords_UserId_WordId` exists with exactly `UserId`, `WordId`;
- the old composite unique index is absent;
- `PartOfSpeechId` remains populated;
- duplicate `(UserId, WordId)` groups: **0**;
- preferred-definition/word inconsistencies: **0**;
- preferred-definition/POS inconsistencies: **0**;
- `OrphanQuizResults = 0`; and
- `OrphanSampleSentences = 0`.

Production referential-integrity verification passed.

## 10. Repository / Git Closeout

Git verification at final closeout confirmed:

| Commit | Purpose | On `master` and `origin/master` |
|---|---|---|
| `3b89cb2330df28d3f7bbc1305e92f1a18f2b6190` | Complete R5 UserWord identity remediation | Yes |
| `3a198ff25b49f278e7bac0fc66570e00ba59fe05` | Complete R5 production deployment verification | Yes |
| `f35278a12fcf72e070e166cdfb8e77ba68f8b861` | Close R5 and document pronunciation audio analysis | Yes |
| `744cac6b6758c5d9b8c7d16d41ba41d982e73b44` | Remove generated deployment artifacts | Yes |
| `6205c3251f4b251b5dc50e97723250308d6c8147` | Ignore generated deployment artifacts | Yes |

Local `master` and `origin/master` both pointed to `6205c3251f4b251b5dc50e97723250308d6c8147`. The working tree was clean before creation of this completion document.

Temporary generated deployment artifacts had been accidentally committed as repository housekeeping. Commit `744cac6` removed these tracked outputs:

- `VocabularyApp.WebApi/R5-Deploy/`
- `VocabularyApp.WebApi/R5-Deploy.zip`
- `VocabularyApp.WebApi/R5-wwwroot.zip`
- generated `VocabularyApp.WebApi/wwwroot/`

Commit `6205c32` added matching `.gitignore` protection to prevent recurrence. This cleanup was not an R5 functional defect and did not alter the verified production deployment.

## 11. Out-of-Scope Follow-Up

A separate pronunciation-audio concern was identified after deployment: new WordsAPI-backed words lack audio URLs, while older cached words may retain stale historical URLs. This is not part of R5 and does not affect the completed UserWord identity remediation.

It is tracked independently in `Docs/Updates/Audio-analysis.md` under the working title **Restore Pronunciation Audio**.

## 12. Definition of Done

- [x] Database and EF model enforce one `UserWord` per `(UserId, WordId)`.
- [x] Old unique `(UserId, WordId, PartOfSpeechId)` identity is absent.
- [x] `PartOfSpeechId` remains required, populated, and synchronized but is not identity.
- [x] Sequential duplicate saves return the existing stable row without mutation.
- [x] Concurrent duplicate saves produce exactly one row and deterministic idempotent outcomes.
- [x] Expected unique-key races are classified narrowly; unrelated persistence failures remain failures.
- [x] Preferred definitions are validated against the canonical word and updated in place.
- [x] Notes, favorites, counters, timestamps, quiz results, sample sentences, and saved-row identity are preserved.
- [x] Migration Up, fail-fast, no-partial-change, and Down behavior are verified.
- [x] Backend tests and build passed.
- [x] R5-focused Angular tests and production build passed.
- [x] Production backup and pre-migration audits passed.
- [x] Production writes were quiesced before migration and deployment.
- [x] Production migration and final index verification passed.
- [x] Production preservation counts matched exactly.
- [x] Production orphan checks returned zero.
- [x] Production application and quiz/history smoke tests passed.
- [x] Deployment incident was resolved without rollback or migration invalidation.
- [x] Temporary stdout logging was disabled after diagnosis.
- [x] R5 and closeout commits are merged to `master` and represented by `origin/master`.
- [x] Generated deployment artifacts were removed and ignored.
- [x] Pronunciation audio remains a separate follow-up.

## 13. Final Status

> **R5 STATUS: FULLY VERIFIED / COMPLETE**

**Remaining R5 work: NONE**
