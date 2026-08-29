# R5 Production Deployment Checklist

> Authoritative checklist for the manual R5 production deployment. Production-only items remain unchecked until the developer confirms them. Never record credentials, passwords, JWT secrets, API keys, or full connection strings here.

## 1. Deployment Objective

Deploy the tested R5 application and migration so production enforces exactly one `UserWord` per `(UserId, WordId)`, while retaining synchronized `PartOfSpeechId`, stable `UserWord.Id`, and all dependent/user state.

- [ ] Deployment is being performed from the reviewed, committed R5 deployment commit.
- [ ] The operator has read this checklist and the rollback procedure.
- [ ] No unrelated remediation is included.

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

Repository readiness status when this checklist was created: **NOT READY TO DEPLOY** because the R5 working tree was uncommitted. Branch `r5/correct-userword-identity`; current pre-deployment-check commit `a243ba577ec6e72f05f0e6d3b07c6ffed952a0ec`. Record the eventual reviewed R5 commit in the Deployment Record; do not deploy the commit above unless it is subsequently proven to contain R5.

- [ ] All intended R5 files are committed; `git status --short` is empty in the deployment checkout.
- [ ] Branch and commit have been reviewed and recorded.
- [ ] Migration, snapshot, backend, UI, tests, and R5 documents are in that commit.
- [ ] `dotnet test .\VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --no-restore` passes.
- [ ] Focused Angular R5 tests pass.
- [ ] `dotnet build .\VocabularyApp.sln -c Release` succeeds.
- [ ] `npm run build` succeeds; the known SCSS budget warning is understood.
- [ ] Production SQL/hosting access is available without placing secrets in source or documentation.
- [ ] Database principal has migration DDL permissions.
- [ ] IIS application root, upload method, and external configuration mechanism are confirmed in SmarterASP.NET.
- [ ] A suitable production test account is identified.

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

- [ ] Backup completed immediately before the maintenance window.
- [ ] Correct production database was backed up.
- [ ] Backup method is provider-supported.
- [ ] Backup location/reference is recorded without credentials.
- [ ] Operator confirmed the backup is complete and restoration is possible.
- [ ] Relevant site/application artifacts and external configuration are also recoverable.

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

- [ ] `20260819000000_AddQuizResultSubmissionUniqueness` is present.
- [ ] `20260829155134_CorrectUserWordIdentity` is absent before migration.

If R5 is already present, stop and investigate schema/application state; do not apply it again.

## 6. Quiesce Vocabulary Writes

Selected practical method for this repository's single ASP.NET Core IIS site: use ASP.NET Core Module's site-root `app_offline.htm` during a short maintenance window. This quiesces all requests, including vocabulary writes, without inventing application maintenance infrastructure.

- [ ] Confirm the exact IIS application root in SmarterASP.NET.
- [ ] Prepare a non-sensitive maintenance page named `app_offline.htm` locally.
- [ ] Upload `app_offline.htm` to the IIS application root using the established FTP/File Manager method.
- [ ] Verify the public application returns the maintenance page and API writes are unavailable.
- [ ] Repeat audit B and H after quiescing; expect zero duplicates and R5 absent.
- [ ] Keep `app_offline.htm` present through migration and artifact replacement.

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

- [ ] Command targets the approved commit and correct production database.
- [ ] Migration succeeds without error 51000.
- [ ] R5 appears exactly once in migration history.

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

- [ ] Publish was built from recorded clean commit.
- [ ] LocalDB/development values were not promoted as production configuration.
- [ ] Production `ConnectionStrings:DefaultConnection`, JWT, WordsAPI, and any required CORS settings remain externally configured and available; values were not printed.
- [ ] Existing production external configuration was backed up/preserved.
- [ ] Complete publish contents uploaded; no source, `.git`, `node_modules`, local DB, or test artifacts uploaded.

## 9. Deploy Angular UI

Angular is not a separate production site in this repository. `npm run build` uses `environment.prod.ts`, where `apiUrl` is `/api`, and outputs `VocabularyApp.UI/dist/vocabulary-app.ui/browser`. Those browser files are copied into backend `wwwroot` **before** `dotnet publish`; deployment is the single publish-folder upload in section 8.

- [ ] Production build used `npm run build`.
- [ ] Built JS targets relative `/api`, not localhost.
- [ ] `wwwroot/index.html`, JS/CSS bundles, favicon/assets are present in publish output.
- [ ] Root `web.config` is the ASP.NET Core publish file, not the Angular-only static rewrite file.

After upload completes, remove only the site-root `app_offline.htm` (not anything under `wwwroot`) to restart service.

- [ ] Upload complete.
- [ ] Site-root `app_offline.htm` removed.
- [ ] IIS application starts with external production configuration.

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

- [ ] Unique `IX_UserWords_UserId_WordId`, key order UserId then WordId.
- [ ] Old composite index absent.
- [ ] `PartOfSpeechId` column remains NOT NULL and null count is 0.
- [ ] Duplicate pair query returns zero rows.
- [ ] Preference/word and preference/POS queries return zero rows.
- [ ] Baseline counts/sums are unchanged.
- [ ] Both orphan counts are 0.
- [ ] Migration history contains R5 exactly once.

Any unexplained discrepancy is a rollback-evaluation trigger; do not continue automatically.

## 11. Application Smoke Tests

Use an appropriate production test account and a word selected to avoid unrelated user data.

- [ ] Site and static assets load without R5-related console/network errors.
- [ ] Login succeeds.
- [ ] Existing vocabulary loads.
- [ ] Dictionary lookup succeeds.
- [ ] Save a previously unsaved canonical word; one `UserWord` appears.
- [ ] Repeat save; UI/API reports success and row count remains one.
- [ ] If practical, repeat with another definition/POS; row count remains one.
- [ ] Change preferred definition, ideally cross-POS; same `UserWord.Id` remains and POS synchronizes.
- [ ] Favorite behavior remains functional.
- [ ] Notes remain functional if exposed by the deployed UI.
- [ ] No unexpected R5-related 500 errors appear in provider/application logs.

## 12. Quiz / Data-Preservation Smoke Tests

Use a preselected existing saved word with modest history; record values without exposing note content.

- [ ] Existing `UserWord.Id`, favorite, note-presence, counters, and timestamps match baseline before intentional quiz activity.
- [ ] Existing quiz history remains accessible/associated.
- [ ] Existing sample-sentence relationship remains present where applicable.
- [ ] Small quiz starts and submits successfully.
- [ ] `TotalAttempts`, correctness counter, and review timestamps change only as expected from that quiz.
- [ ] The same `UserWord.Id` owns the new result/history.

## 13. Production Acceptance Criteria

- [ ] Production backup confirmed.
- [ ] Pre-migration pair-duplicate audit returned zero rows.
- [ ] Pre-migration consistency checks passed.
- [ ] Baseline counts recorded.
- [ ] Vocabulary writes safely quiesced.
- [ ] R5 migration applied successfully.
- [ ] Migration history contains R5.
- [ ] New unique pair index exists.
- [ ] Old composite unique index is gone.
- [ ] `PartOfSpeechId` remains intact/populated.
- [ ] Post-migration consistency and preservation checks passed.
- [ ] Backend and embedded Angular UI deployed successfully.
- [ ] Application, login, lookup, save, and repeat-save checks passed.
- [ ] Preferred definition updates in place.
- [ ] Favorite/notes behavior remains intact.
- [ ] Quiz/history/sample data remains intact.
- [ ] Quiz smoke test succeeds.
- [ ] No new production errors attributable to R5 are observed.

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

Deployment date:

Deployment start time:

Deployment end time:

Git commit:

Database:

Backup confirmed:

Pre-migration audit:

Migration result:

Backend deployment:

Frontend deployment:

Post-migration SQL verification:

Application smoke test:

Quiz smoke test:

Rollback required:

Final status:

Notes:
