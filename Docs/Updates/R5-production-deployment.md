# R5 Production Deployment Checklist

> Authoritative checklist for the manual R5 production deployment. Production-only items remain unchecked until the developer confirms them. Never record credentials, passwords, JWT secrets, API keys, or full connection strings here.

## 1. Deployment Objective

Deploy the tested R5 application and migration so production enforces exactly one `UserWord` per `(UserId, WordId)`, while retaining synchronized `PartOfSpeechId`, stable `UserWord.Id`, and all dependent/user state.

- [x] Deployment was performed from reviewed R5 commit `3b89cb2330df28d3f7bbc1305e92f1a18f2b6190` on branch `r5/correct-userword-identity`.
- [x] Deployment checklist and rollback procedure were reviewed for final production sign-off.
- [x] R5 deployment completed without unrelated remediation being recorded.

## 2. Production Change Summary

- Migration: `20260829155134_CorrectUserWordIdentity`.
- Drop unique index: `IX_UserWords_UserId_WordId_PartOfSpeechId`.
- Create unique index: `IX_UserWords_UserId_WordId` on `(UserId, WordId)`.
- Migration aborts with SQL error 51000 if duplicate pairs exist; it never merges or deletes.
- `PartOfSpeechId` remains NOT NULL and FK-backed but is no longer identity.
- Repeat saves return HTTP 200 with the unchanged existing entry.
- Preferred-definition changes update the same entry and synchronize POS.
- Backend and Angular UI are deployed together as one IIS site: Angular browser assets inside backend `wwwroot`.

## 3. Preconditions

Repository readiness was initially blocked while the R5 worktree was uncommitted. That blocker was resolved: production was deployed from commit `3b89cb2330df28d3f7bbc1305e92f1a18f2b6190` on branch `r5/correct-userword-identity` using clean deployment artifacts.

- [x] All intended R5 files were committed in the deployment commit.
- [x] Branch and commit were reviewed and recorded.
- [x] Migration, snapshot, backend, UI, tests, and R5 documents were included.
- [x] Backend R5 tests passed before deployment.
- [x] Focused Angular R5 tests passed before deployment.
- [x] .NET Release build succeeded before deployment.
- [x] Angular production build succeeded; the known SCSS budget warning was understood.
- [x] Production SQL/hosting access was used without recording secrets.
- [x] The database principal successfully applied the migration DDL.
- [x] IIS application root, upload method, and external configuration mechanism were confirmed in SmarterASP.NET.
- [x] A production account was used for the confirmed login smoke test.

Repository verification commands:

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
Select-String .\VocabularyApp.Data\ApplicationDbContext.cs -Pattern 'e.UserId, e.WordId'
Get-Item .\VocabularyApp.Data\Migrations\20260829155134_CorrectUserWordIdentity.cs
```

Stop if the worktree is dirty, commit differs from the approved deployment commit, or artifacts cannot be traced to it.

## 4. Backup

Production migration must not begin without a recoverable database backup.

- [x] Backup completed successfully before migration.
- [x] Correct production database was backed up.
- [x] SmarterASP MSSQL backup was used.
- [x] Backup file was created in the hosting `/db` folder; no credentials are recorded.
- [x] Backup completion was confirmed.
- [x] Backup and recovery procedures were completed as documented for the production deployment.

Record in Deployment Record: UTC/local time and timezone, database identifier, backup method, non-secret reference, operator, and restoration confirmation. Do not assume provider backups exist.

## 5. Pre-Migration Database Audit

Run in SSMS against the confirmed production database using a read-only session before quiescing, then repeat duplicate/history checks after writes are quiesced. Save results securely with the deployment record; do not capture note contents.

### A. Current UserWords indexes

```sql
SELECT
    i.name AS IndexName,
    i.is_unique AS IsUnique,
    ic.key_ordinal AS KeyOrdinal,
    c.name AS ColumnName
FROM sys.indexes AS i
JOIN sys.index_columns AS ic
  ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns AS c
  ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID(N'dbo.UserWords')
  AND ic.is_included_column = 0
ORDER BY i.name, ic.key_ordinal;
```

Expected pre-R5: unique `IX_UserWords_UserId_WordId_PartOfSpeechId` in key order UserId, WordId, PartOfSpeechId; no `IX_UserWords_UserId_WordId`.

### B. Duplicate `(UserId, WordId)` groups — hard stop

```sql
SELECT UserId, WordId, COUNT(*) AS NumRows
FROM dbo.UserWords
GROUP BY UserId, WordId
HAVING COUNT(*) > 1
ORDER BY NumRows DESC;
```

Expected: **zero rows**. Any row means STOP; do not migrate, delete, or merge.

### C. Current composite duplicates

```sql
SELECT UserId, WordId, PartOfSpeechId, COUNT(*) AS NumRows
FROM dbo.UserWords
GROUP BY UserId, WordId, PartOfSpeechId
HAVING COUNT(*) > 1
ORDER BY NumRows DESC;
```

Expected: zero rows.

### D. Required POS population

```sql
SELECT COUNT_BIG(*) AS NullPartOfSpeechCount
FROM dbo.UserWords
WHERE PartOfSpeechId IS NULL;
```

Expected: `0`.

### E. Preference belongs to the same word

```sql
SELECT uw.Id, uw.UserId, uw.WordId, uw.PreferredWordDefinitionId,
       wd.WordId AS DefinitionWordId
FROM dbo.UserWords AS uw
LEFT JOIN dbo.WordDefinitions AS wd
  ON wd.Id = uw.PreferredWordDefinitionId
WHERE uw.PreferredWordDefinitionId IS NOT NULL
  AND (wd.Id IS NULL OR wd.WordId <> uw.WordId);
```

Expected: zero rows.

### F. Preference/POS consistency

```sql
SELECT uw.Id, uw.UserId, uw.WordId, uw.PartOfSpeechId,
       uw.PreferredWordDefinitionId,
       wd.PartOfSpeechId AS DefinitionPartOfSpeechId
FROM dbo.UserWords AS uw
JOIN dbo.WordDefinitions AS wd
  ON wd.Id = uw.PreferredWordDefinitionId
WHERE uw.PartOfSpeechId <> wd.PartOfSpeechId;
```

Expected: zero rows.

### G. Preservation baseline

```sql
SELECT
    (SELECT COUNT_BIG(*) FROM dbo.UserWords) AS UserWords,
    (SELECT COUNT_BIG(*) FROM dbo.QuizResults) AS QuizResults,
    (SELECT COUNT_BIG(*) FROM dbo.SampleSentences) AS SampleSentences,
    (SELECT COUNT_BIG(*) FROM dbo.UserWords WHERE IsFavorite = 1) AS FavoriteUserWords,
    (SELECT COUNT_BIG(*) FROM dbo.UserWords
      WHERE PersonalNotes IS NOT NULL AND LEN(LTRIM(RTRIM(PersonalNotes))) > 0) AS UserWordsWithNotes,
    (SELECT COALESCE(SUM(CONVERT(bigint, TotalAttempts)), 0) FROM dbo.UserWords) AS TotalAttempts,
    (SELECT COALESCE(SUM(CONVERT(bigint, CorrectAnswers)), 0) FROM dbo.UserWords) AS CorrectAnswers;
```

Record numbers only. Expected post-migration: unchanged, because R5 performs no data DML.

### H. Migration history

```sql
SELECT MigrationId, ProductVersion
FROM dbo.__EFMigrationsHistory
ORDER BY MigrationId;
```

- [x] `20260819000000_AddQuizResultSubmissionUniqueness` was present.
- [x] `20260829155134_CorrectUserWordIdentity` was absent before migration.

Pre-migration audit result: **PASS**. Both duplicate queries returned zero rows; `PartOfSpeechId` had zero NULL values; and both preferred-definition consistency queries returned zero rows.

Recorded preservation baseline: `UserWords` 35; `QuizResults` 18; `SampleSentences` 0; favorites 4; words with notes 0; `TotalAttempts` sum 18; `CorrectAnswers` sum 15.

If R5 is already present, stop and investigate schema/application state; do not apply it again.

## 6. Quiesce Vocabulary Writes

Selected practical method for this repository's single ASP.NET Core IIS site: use ASP.NET Core Module's site-root `app_offline.htm` during a short maintenance window. This quiesces all requests, including vocabulary writes, without inventing application maintenance infrastructure.

- [x] Confirmed production application root `/vocabularyapp` in SmarterASP.NET.
- [x] Prepared the non-sensitive maintenance page.
- [x] Deployed it as `/vocabularyapp/app_offline.htm`.
- [x] Application writes were quiesced.
- [x] Final pre-migration duplicate check returned zero `(UserId, WordId)` groups.
- [x] `app_offline.htm` remained in place through migration and artifact replacement.

If SmarterASP does not honor `app_offline.htm`, stop and select a provider-supported site-stop/app-pool-stop mechanism in the control panel. Do not rely on an unverified UI banner or operator timing alone.

## 7. Apply R5 Migration

The repository's supported method is EF Core CLI from the reviewed deployment checkout (`docs/Deployment/SmarterASP-Manual-Deployment.md:408-450`). Target R5 explicitly. Supply the production connection string only in the current process; do not edit source-controlled appsettings.

Recommended PowerShell procedure from repository root:

```powershell
$secureR5Connection = Read-Host 'Production SQL connection string' -AsSecureString
$r5Connection = [System.Net.NetworkCredential]::new('', $secureR5Connection).Password
$env:ConnectionStrings__DefaultConnection = $r5Connection

dotnet ef database update 20260829155134_CorrectUserWordIdentity `
  --project .\VocabularyApp.Data\VocabularyApp.Data.csproj `
  --startup-project .\VocabularyApp.WebApi\VocabularyApp.WebApi.csproj `
  --configuration Release `
  --no-build

Remove-Item Env:\ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
$r5Connection = $null
$secureR5Connection = $null
```

Preconditions for `--no-build`: Release build from the exact approved commit exists and includes the migration. Otherwise omit `--no-build` only after confirming a clean checkout and successful Release build.

- [x] Migration targeted approved commit `3b89cb2330df28d3f7bbc1305e92f1a18f2b6190` and the production database.
- [x] Migration succeeded; its fail-fast duplicate precondition executed successfully.
- [x] `20260829155134_CorrectUserWordIdentity` appears in migration history with ProductVersion `8.0.10`.

On error 51000 or any unexpected failure: keep the app offline, capture the non-secret error, verify transaction/schema/history, and STOP. Do not retry blindly or alter data.

## 8. Deploy Backend

Build artifacts only after the R5 commit is clean and approved. There is no publish profile; use the documented CLI process.

```powershell
dotnet restore .\VocabularyApp.sln

Set-Location .\VocabularyApp.UI
npm install
npm run build
Set-Location ..

New-Item -ItemType Directory -Force -Path .\VocabularyApp.WebApi\wwwroot | Out-Null
Remove-Item .\VocabularyApp.WebApi\wwwroot\* -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item .\VocabularyApp.UI\dist\vocabulary-app.ui\browser\* `
  .\VocabularyApp.WebApi\wwwroot\ -Recurse -Force

dotnet publish .\VocabularyApp.WebApi\VocabularyApp.WebApi.csproj `
  -c Release -o .\VocabularyApp.WebApi\publish
```

Upload **the contents** of `VocabularyApp.WebApi/publish` to the confirmed IIS application root while `app_offline.htm` remains in place. The folder must contain the backend executable/DLLs, root ASP.NET Core `web.config`, and `wwwroot/index.html` plus hashed Angular assets.

- [x] Publish was built successfully from the recorded clean commit.
- [x] Production database configuration is supplied by the secure `ConnectionStrings__DefaultConnection` application-pool variable; the LocalDB fallback is not active.
- [x] `ConnectionStrings__DefaultConnection`, `JwtSettings__SecretKey`, and `WordsApi__ApiKey` were confirmed as external application-pool variables after incident correction; no values are recorded.
- [x] Existing production external variables were preserved and confirmed without recording their values.
- [x] Clean publish contents were manually deployed to `/vocabularyapp`; old deployment files were removed while `logs` and `app_offline.htm` were preserved.

## 9. Deploy Angular UI

Angular is not a separate production site in this repository. `npm run build` uses `environment.prod.ts`, where `apiUrl` is `/api`, and outputs `VocabularyApp.UI/dist/vocabulary-app.ui/browser`. Those browser files are copied into backend `wwwroot` **before** `dotnet publish`; deployment is the single publish-folder upload in section 8.

- [x] Production build used `npm run build` successfully.
- [x] Production Angular configuration targets relative `/api`.
- [x] Angular assets were deployed under `/vocabularyapp/wwwroot`: `main-C24LQOWV.js`, `polyfills-FFHMD2TL.js`, and `styles-CCGXTJ5Y.css`.
- [x] ASP.NET Core deployment and temporary stdout diagnostic logging operated through the root `web.config`.

After upload completes, remove only the site-root `app_offline.htm` (not anything under `wwwroot`) to restart service.

- [x] Upload completed.
- [x] Site-root `app_offline.htm` was removed only after deployment verification.
- [x] Application pool/site was restarted after completing external database configuration; login then succeeded.

## 10. Post-Migration SQL Verification

Run immediately after migration before acceptance. Reuse queries D–G and H, plus:

```sql
SELECT
    i.name AS IndexName,
    i.is_unique AS IsUnique,
    ic.key_ordinal AS KeyOrdinal,
    c.name AS ColumnName
FROM sys.indexes AS i
JOIN sys.index_columns AS ic
  ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns AS c
  ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID(N'dbo.UserWords')
  AND i.name IN (
      N'IX_UserWords_UserId_WordId',
      N'IX_UserWords_UserId_WordId_PartOfSpeechId')
  AND ic.is_included_column = 0
ORDER BY i.name, ic.key_ordinal;

SELECT COUNT_BIG(*) AS OrphanQuizResults
FROM dbo.QuizResults AS qr
LEFT JOIN dbo.UserWords AS uw ON uw.Id = qr.UserWordId
WHERE uw.Id IS NULL;

SELECT COUNT_BIG(*) AS OrphanSampleSentences
FROM dbo.SampleSentences AS ss
LEFT JOIN dbo.UserWords AS uw ON uw.Id = ss.UserWordId
WHERE uw.Id IS NULL;
```

Expected:

- [x] Unique `IX_UserWords_UserId_WordId`, key order UserId then WordId.
- [x] Old composite index absent.
- [x] `PartOfSpeechId` remains populated; null count is 0.
- [x] Duplicate pair query returned zero rows.
- [x] Preference/word and preference/POS queries returned zero rows.
- [x] Baseline counts/sums were unchanged: 35 UserWords, 18 QuizResults, 0 SampleSentences, 4 favorites, 0 words with notes, 18 total attempts, and 15 correct answers.
- [x] Both orphan counts are 0: `OrphanQuizResults = 0`; `OrphanSampleSentences = 0`. Detailed orphan queries returned no rows.
- [x] Migration history contains R5 with ProductVersion `8.0.10`.

Any unexplained discrepancy is a rollback-evaluation trigger; do not continue automatically.

## 11. Application Smoke Tests

Use an appropriate production test account and a word selected to avoid unrelated user data.

- [x] Site and static assets load.
- [x] Login succeeds after correcting the missing database environment variable and restarting the site.
- [x] Word lookup succeeds.
- [x] Saving a word succeeds.
- [x] Duplicate-save/idempotent behavior succeeds and remains one `UserWord`.
- [x] Preferred-definition update behavior succeeds in place.
- [x] Favorite behavior remains functional.
- [x] Notes behavior remains functional.
- [x] Quiz/history behavior remains functional.

## 12. Quiz / Data-Preservation Smoke Tests

Use a preselected existing saved word with modest history; record values without exposing note content.

- [x] Quiz/history production smoke testing completed successfully.
- [x] Production preservation/count verification completed successfully and matched the recorded baseline.
- [x] Referential-integrity verification passed: no `QuizResults` or `SampleSentences` reference missing `UserWords`.

## 13. Production Acceptance Criteria

- [x] Production backup confirmed.
- [x] Pre-migration pair-duplicate audit returned zero rows.
- [x] Pre-migration consistency checks passed.
- [x] Baseline counts recorded.
- [x] Vocabulary writes safely quiesced.
- [x] R5 migration applied successfully.
- [x] Migration history contains R5.
- [x] New unique pair index exists.
- [x] Old composite unique index is gone.
- [x] `PartOfSpeechId` remains intact/populated.
- [x] Post-migration consistency and preservation counts passed.
- [x] Backend and embedded Angular UI deployed successfully.
- [x] Application, login, lookup, save, and repeat-save checks passed.
- [x] Preferred definition updates in place.
- [x] Favorite/notes behavior remains intact.
- [x] Quiz/history/sample data remains intact.
- [x] Quiz smoke test succeeds.
- [x] No R5 rollback was required; the migration remained valid and verified during the deployment incident.

Do not mark production R5 complete until every applicable item is confirmed.

## 14. Rollback Triggers

### Stop before migration

- No verified backup/restoration path.
- Dirty/unreviewed deployment checkout or untraceable artifacts.
- Wrong/uncertain database or missing preceding migration.
- Any duplicate pair, composite duplicate, null POS, or consistency mismatch.
- R5 already appears in migration history unexpectedly.
- Writes cannot be reliably quiesced.

### Stop/assess after migration begins

- Error 51000 or any migration failure.
- Partial/unexpected schema or migration-history state.
- Baseline count/sum discrepancy or orphaned dependents.
- Application cannot access `UserWords`.
- Widespread save/preference failures or unexpected R5-related 500 errors.
- Preference/POS corruption or quiz/history relationship failure.

Error 51000 normally means stop, not Down: the tested transactional migration leaves the old index/history intact. Verify before taking action.

## 15. Rollback Procedure

Do not execute automatically. Keep/restore `app_offline.htm`, preserve logs/evidence, and take a post-failure backup before destructive recovery.

### If migration did not commit

1. Verify R5 is absent from `__EFMigrationsHistory` and old composite index remains.
2. Do not run Down unnecessarily.
3. Restore prior application artifacts/configuration if they were changed.
4. Remove `app_offline.htm` only after the old application/schema is confirmed healthy.

### If migration committed and schema rollback is appropriate

1. Quiesce writes and audit duplicate pairs/current state.
2. Confirm all data created under R5 remains valid for required non-null POS. Pair uniqueness is stricter than old composite uniqueness, so a direct immediate Down is structurally valid unless other changes/data corruption occurred.
3. From the exact R5 commit, securely configure the connection as in section 7 and target the preceding migration:

   ```powershell
   dotnet ef database update 20260819000000_AddQuizResultSubmissionUniqueness `
     --project .\VocabularyApp.Data\VocabularyApp.Data.csproj `
     --startup-project .\VocabularyApp.WebApi\VocabularyApp.WebApi.csproj `
     --configuration Release `
     --no-build
   ```

4. Verify unique `IX_UserWords_UserId_WordId_PartOfSpeechId` restored and R5 history row removed.
5. Restore the exact previous backend/UI publish artifacts and external configuration.
6. Run old-version schema/application smoke tests before reopening traffic.
7. Audit duplicate pairs after reopening: old code/schema can create POS variants, which would block a future R5 reapply.

### When backup restoration is safer

Use the verified backup instead of Down if unexpected data mutation, corruption, multiple uncoordinated schema/application changes, or uncertain partial deployment makes schema-only rollback insufficient. Restoration loses activity after the backup and requires an explicit operator/business decision. Follow provider restoration procedures; never improvise SQL deletes/merges.

## 16. Deployment Record

### Production Incident During Deployment

1. Login returned HTTP 401 after deployment.
2. Temporary stdout logging was enabled in `web.config`; server logs showed the API attempting to connect to `(localdb)\mssqllocaldb`.
3. SmarterASP application-pool environment variables were inspected.
4. `ConnectionStrings__DefaultConnection` was missing.
5. It was added securely in Pool Manager without recording its value. Existing `JwtSettings__SecretKey` and `WordsApi__ApiKey` variables were also confirmed without exposing their values.
6. The application pool/site was restarted.
7. Login was retested and succeeded.
8. No R5 rollback was required.
9. The database migration remained valid and fully verified throughout.

- [x] Production `web.config` was returned to `stdoutLogEnabled="false"` after diagnostics; temporary stdout logging is disabled.

### Final Record

Deployment date: Not provided

Deployment start time: Not provided

Deployment end time: Not provided

Branch: `r5/correct-userword-identity`

Git commit: `3b89cb2330df28d3f7bbc1305e92f1a18f2b6190`

Migration: `20260829155134_CorrectUserWordIdentity`

Database: Production VocabularyApp SQL Server database; specific database identifier not provided

Production application root: `/vocabularyapp`

Production Angular static root: `/vocabularyapp/wwwroot`

Backup confirmed: Yes — SmarterASP MSSQL backup completed successfully before migration; backup file created in hosting `/db` folder

Pre-migration audit: PASS — zero pair or composite duplicates, zero NULL POS values, and zero preferred-definition consistency errors

Pre-migration baseline: UserWords 35; QuizResults 18; SampleSentences 0; Favorite UserWords 4; UserWords with notes 0; TotalAttempts 18; CorrectAnswers 15

Write quiescing: Completed using `/vocabularyapp/app_offline.htm`; final pair-duplicate check returned zero rows

Migration result: PASS — fail-fast precondition succeeded; old composite index removed; unique `IX_UserWords_UserId_WordId (UserId, WordId)` created; migration history records ProductVersion `8.0.10`

Backend deployment: PASS — publish succeeded and clean deployment artifacts were manually deployed through SmarterASP

Frontend deployment: PASS — production build succeeded; deployed bundles `main-C24LQOWV.js`, `polyfills-FFHMD2TL.js`, and `styles-CCGXTJ5Y.css`

Post-migration SQL verification: PASS — index, duplicate, POS, preference consistency, migration history, and preservation-count checks passed

Post-migration baseline: UserWords 35; QuizResults 18; SampleSentences 0; Favorite UserWords 4; UserWords with notes 0; TotalAttempts 18; CorrectAnswers 15

Application smoke test: PASS — application load, login, word lookup, save, duplicate-save/idempotent behavior, preferred-definition updates, favorites, and notes verified successfully

Quiz smoke test: PASS — quiz/history behavior verified successfully

Final orphan verification: PASS — detailed queries returned no rows; `OrphanQuizResults = 0`; `OrphanSampleSentences = 0`

Rollback required: No

Final status: **FULLY VERIFIED / COMPLETE**

Final production sign-off: R5 production deployment and verification complete.

Notes: Login initially returned HTTP 401 because `ConnectionStrings__DefaultConnection` was absent from the application-pool environment. The variable was added securely and the site restarted, after which login passed. Temporary stdout logging was enabled for diagnosis and was subsequently returned to `stdoutLogEnabled="false"`. The deployment incident required no rollback and did not invalidate the successfully verified R5 migration.

### Final Repository Closeout — 2026-09-01

R5 remains **FULLY VERIFIED / COMPLETE** in production. Production enforces unique `(UserId, WordId)` identity; `PartOfSpeechId` remains synchronized compatibility state rather than identity. Duplicate saves are idempotent, preferred-definition updates mutate the existing entry in place, the final orphan counts remain `OrphanQuizResults = 0` and `OrphanSampleSentences = 0`, stdout diagnostic logging is disabled, and no rollback was required.

Git verification found implementation commit `3b89cb2330df28d3f7bbc1305e92f1a18f2b6190` and deployment-verification commit `3a198ff25b49f278e7bac0fc66570e00ba59fe05` on `r5/correct-userword-identity`. At verification time, both local `master` and the read-only queried remote `origin/master` pointed to `a243ba577ec6e72f05f0e6d3b07c6ffed952a0ec`; neither R5 commit was an ancestor of that `master` ref. Therefore the supplied statement that R5 had been merged to master was **not confirmed by Git evidence** and requires the branch to be merged or the intended master remote/ref to be clarified. This repository-state discrepancy does not change the completed production verification.

Post-R5 audio behavior was identified as a separate dictionary-provider/audio concern and is tracked independently from R5 in `Docs/Updates/Audio-analysis.md`.
