# R5 Implementation Plan — Correct UserWord Identity

## 1. Objective

Change the saved-vocabulary business identity from `(UserId, WordId, PartOfSpeechId)` to `(UserId, WordId)` while preserving the existing `UserWord.Id` and all user-owned/dependent state.

A canonical `Word` may have many definitions and parts of speech, but each user may save it once. `PreferredWordDefinitionId` represents the selected meaning. `PartOfSpeechId` remains during R5 as required synchronized compatibility state, never as identity.

This document is implementation planning only. It does not create code, tests, migrations, configuration, or database changes.

## 2. Confirmed Starting State

- `UserWord.Id` is the surrogate primary key; `UserId`, `WordId`, and `PartOfSpeechId` are required; `PreferredWordDefinitionId` is nullable (`VocabularyApp.Data/Models/UserWord.cs:6-43`).
- EF and the deployed SQL Server enforce unique `IX_UserWords_UserId_WordId_PartOfSpeechId`; neither enforces unique `(UserId, WordId)` (`VocabularyApp.Data/ApplicationDbContext.cs:70-100`; `VocabularyApp.Data/Migrations/ApplicationDbContextModelSnapshot.cs:269-322`).
- The executable migration chain restores required POS and the composite unique index in `20251030143529_AddAudioUrlToWords.cs:11-46`, then adds nullable preference/FK/index in `20260725020317_AddPreferredDefinitionToUserWords.cs:11-30`.
- Deployed read-only verification found no duplicate two-column or three-column groups, no null POS values, no cross-word preferences, and no preference/POS mismatches. Relevant migrations are applied.
- Add currently checks `(UserId, WordId, PartOfSpeechId)`, returns an anonymous success for a sequential same-POS repeat, and inserts another row for another POS (`VocabularyApp.WebApi/Services/WordService.cs:211-258`).
- Preferred-definition update already validates ownership and same canonical word, but it checks for another POS-based entry and can reject a cross-POS change before synchronizing POS (`WordService.cs:261-327`).
- Vocabulary list/search and quiz generation read stored POS to select/project definitions (`WordService.cs:373-430,450-512`; `VocabularyApp.WebApi/Services/QuizService.cs:29-59`).
- `QuizResult.UserWordId` and `SampleSentence.UserWordId` are required cascade FKs to `UserWord`; neither needs reassignment because no merge is planned (`ApplicationDbContext.cs:102-139`; `VocabularyApp.Data/Models/QuizResult.cs:5-29`; `VocabularyApp.Data/Models/SampleSentence.cs:5-23`).
- Angular already presents a word-level Add button, keys saved entries by `UserWord.Id`, and updates preferred definition on the same entry (`VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts:328-365,485-617`; `VocabularyApp.UI/src/app/models/word-lookup.model.ts:47-62`).
- Integration tests use SQLite `EnsureCreated`, not the SQL Server migration chain (`VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs:76-103`; `RelationalDatabaseFixture.cs:13-24`).

## 3. Target Invariants

1. At most one `UserWord` exists for each `(UserId, WordId)`.
2. Different users may save the same `WordId`.
3. SQL Server and the EF runtime model enforce unique `(UserId, WordId)`.
4. No uniqueness rule includes `PartOfSpeechId`.
5. `PartOfSpeechId` remains required during R5.
6. A non-null preference belongs to `UserWord.WordId`.
7. For a non-null preference, `UserWord.PartOfSpeechId` equals the selected definition's `PartOfSpeechId`.
8. A repeat add is idempotent: it returns the existing row and does not modify preference, POS, notes, favorite, counters, or timestamps.
9. Preferred-definition change updates the same row and preserves its ID and all unrelated state/dependents.
10. Concurrent adds produce exactly one row and equivalent successful/idempotent API outcomes.
11. Unexpected pre-existing duplicates stop migration without automatic mutation.

## 4. Files and Components Affected

| Area | File / Component | Planned Change | Reason |
|---|---|---|---|
| Entity documentation | `VocabularyApp.Data/Models/UserWord.cs` | Keep properties/navigation; narrowly update comments to describe preference authority and derived POS. | Prevent future use of POS as identity. |
| EF model | `VocabularyApp.Data/ApplicationDbContext.cs` | Replace unique three-column index with unique two-column index; keep POS column/FK/nullability. | Enforce approved identity. |
| Migration | New R5 migration under `VocabularyApp.Data/Migrations/` | Fail-fast duplicate check, drop old index, add new index; generated snapshot update. | Deploy constraint safely without merging. |
| Snapshot | `VocabularyApp.Data/Migrations/ApplicationDbContextModelSnapshot.cs` | Generated change from unique triple to unique pair. | Keep EF model history consistent. |
| Service interface | `VocabularyApp.WebApi/Services/IWordService.cs` | Prefer a typed add result if introduced; otherwise signature may remain. | Make idempotent result unambiguous/testable. |
| Add request/result | `VocabularyApp.WebApi/Models/AddWordRequest.cs` and a narrowly scoped DTO/result file | Keep request fields for compatibility; add/standardize response metadata (`userWordId`, `wordId`, `alreadyExisted`, message). | Distinguish canonical and saved IDs and report repeat outcome. |
| Word service | `VocabularyApp.WebApi/Services/WordService.cs` | Two-column lookup, normalized definition/POS selection, idempotent return, expected unique-race recovery, simplified preference update. | Correct behavior and concurrency. |
| Controller | `VocabularyApp.WebApi/Controllers/WordsController.cs` | Preserve route/envelope/status; return standardized add result. | Avoid Angular break while exposing deterministic semantics. |
| Vocabulary DTO | `VocabularyApp.WebApi/DTOs/UserVocabularyDTOs.cs` | No breaking shape change expected; keep POS display and preferred ID. | Existing frontend contract remains valid. |
| Quiz | `VocabularyApp.WebApi/Services/QuizService.cs` | Prefer explicit preference while retained synchronized POS remains compatible; remove/reassess duplicate-group masking. | One row per word makes arbitrary `GroupBy(...).First()` unnecessary. |
| Dependents | `QuizResult`, `SampleSentence`, EF relationships | No schema/relationship change. | Stable `UserWord.Id` preserves all FKs. |
| Angular model | `VocabularyApp.UI/src/app/models/word-lookup.model.ts` | Add a typed add-result interface only if component consumes metadata. | Avoid `any` for the changed response. |
| Angular component | `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` | Keep request compatible; optionally use `alreadyExisted` for truthful toast/state. No entry replacement. | Minimal UI alignment. |
| Backend tests | `VocabularyApp.WebApi.Tests/Integration/VocabularyOwnershipApiTests.cs`, infrastructure/seeder, new focused migration/concurrency tests as needed | Modify identity test and add preservation/race/schema tests. | Prove R5 invariants. |
| Angular tests | `word-lookup.component.spec.ts` | Verify idempotent response handling and in-place preference UI behavior if component changes. | Contract regression coverage. |
| Deployment docs | Existing R5 completion/deployment note or `docs/Updates/` artifact during implementation | Record backup, precondition, verification, downgrade hazard. | Manual deployment safety. |

## 5. Database Migration Plan

### Up migration

1. Execute a SQL Server fail-fast precondition before dropping/creating indexes:

   ```sql
   IF EXISTS (
       SELECT 1
       FROM dbo.UserWords
       GROUP BY UserId, WordId
       HAVING COUNT_BIG(*) > 1
   )
       THROW 51000, 'R5 cannot enforce unique UserWords(UserId, WordId): duplicate rows exist.', 1;
   ```

   Use `migrationBuilder.Sql(...)`. `THROW` makes the migration fail explicitly; no winner selection, delete, update, or merge is permitted. The migration transaction should leave the old schema intact on failure.
2. Drop exact current index `IX_UserWords_UserId_WordId_PartOfSpeechId`.
3. Create unique `IX_UserWords_UserId_WordId` on `UserWords(UserId, WordId)`.
4. Leave `PartOfSpeechId` non-null, its non-unique index and Restrict FK intact.
5. Leave `PreferredWordDefinitionId` nullable and keep its FK/index unchanged. R5 does not need a data backfill.
6. Preserve all rows and IDs. The clean path contains no `UPDATE`, `DELETE`, or FK reparenting.

The same-word preference invariant remains service validation. A database composite FK would require adding a candidate key such as `WordDefinitions(Id, WordId)` and a composite FK from `UserWords`, adding schema complexity unrelated to the identity correction. Do not add it in R5.

### Down migration

1. Drop unique `IX_UserWords_UserId_WordId`.
2. Recreate unique `IX_UserWords_UserId_WordId_PartOfSpeechId`.
3. Leave columns/FKs unchanged.

Because R5's two-column constraint is stricter than the old triple, data valid under R5 is also valid under the old triple; a transactional Down should recreate it successfully while the R5 constraint is still the starting state. The operational downgrade hazard occurs **after** downgrade: the old schema/application can again create POS variants. Re-upgrading would then fail the R5 precondition. Rollback notes must call this out and require a duplicate audit before re-deployment.

### Migration validation

- Generate and review the migration to ensure it contains only the precondition/index changes and snapshot update.
- Script it for SQL Server and confirm ordering and transaction behavior.
- Test against a representative clean database upgraded through real migrations.
- Test a disposable database with deliberately seeded two-column duplicates and verify fail-fast/no partial schema change.
- Never test the failure path against deployed data.

## 6. Backend Service Changes

### Add behavior decision

Choose **idempotent success returning the existing saved entry**, not HTTP 409. Current sequential same-POS behavior already returns 200, Angular treats Add as word-level, and retries/concurrent requests should not become user-visible failures. Preserve the existing controller envelope and 200 status while standardizing response data.

Recommended typed result fields:

- `UserWordId`: stable saved-entry ID;
- `WordId`: canonical ID;
- `AlreadyExisted`: false for creator, true for a pre-existing/race-winning row returned to the loser;
- `Message`: truthful “added” or “already in vocabulary”.

### `AddToVocabularyAsync` target flow

1. Validate request and resolve the existing canonical word as today (`WordService.cs:211-224`).
2. Query by `UserId` and `WordId` only. If found, return it immediately with `AlreadyExisted = true`. Do **not** resolve/mutate a new preference/POS first and do not alter any existing state.
3. For a new row, resolve the requested preferred definition and POS consistently:
   - retain the request contract for compatibility;
   - if a valid `PreferredWordDefinitionId` is supplied, require it to belong to the canonical word and derive POS from that definition;
   - if preference is absent, retain current POS-name resolution/fallback and select the first definition in that POS;
   - if a supplied preferred ID is invalid/cross-word, return validation failure rather than silently selecting another meaning. This aligns add with the preferred-definition endpoint invariant.
4. Create exactly one `UserWord`, assigning preference and derived POS together, then save.
5. Return the standardized result with `AlreadyExisted = false`.
6. Handle the expected unique race as section 11 specifies.

Repeat requests with different POS or preferred ID still return the existing row unchanged. “Add” is not an implicit preference update. The explicit preferred-definition endpoint is the only operation that changes selected meaning, preventing retries/stale clients from overwriting learning context.

No repository pattern, new service layer, or WordService decomposition is needed.

## 7. Preferred Definition Changes

Update `SetPreferredDefinitionAsync` (`WordService.cs:261-327`) as follows:

1. Validate the positive definition ID.
2. Load `UserWord` by `Id` and authenticated `UserId`; failure preserves current ownership-safe behavior.
3. Load the selected `WordDefinition` and require `selectedDefinition.WordId == userWord.WordId`.
4. Remove `conflictingEntryExists` and its rejection (`WordService.cs:294-308`). With unique `(UserId, WordId)`, another POS row is not a valid state.
5. Assign on the tracked existing row:
   - `PreferredWordDefinitionId = selectedDefinition.Id`;
   - `PartOfSpeechId = selectedDefinition.PartOfSpeechId`.
6. Call `SaveChangesAsync` once and return the same `userWordId`.

Do not instantiate/reassign a `UserWord`, change its `Id`, or touch `PersonalNotes`, `IsFavorite`, `AddedAt`, learning counters/timestamps, navigations, or dependents. EF's property-level update on the existing entity preserves those values and all `QuizResult`/`SampleSentence` FKs.

The service should treat POS as derived whenever preference is non-null. Vocabulary/quiz projections may keep POS fallback for legacy null preferences, but preference must win when present.

## 8. API / DTO Changes

- Keep `POST /api/words/vocabulary/add`, authentication, request envelope, and HTTP 200 idempotent behavior (`WordsController.cs:75-103`).
- Keep accepting `AddWordRequest.PartOfSpeech` temporarily for clients that omit preference. It is compatibility input, not identity.
- When `PreferredWordDefinitionId` is supplied, derive POS from the validated definition; do not trust an inconsistent POS string. This is option C—temporarily accept but normalize server-side.
- Stop silently falling back when a supplied preferred ID is invalid; return the existing BadRequest envelope with a clear validation message.
- Standardize/add an additive typed response as described in section 6. Existing clients that ignore fields remain compatible.
- Keep `UpdatePreferredDefinitionRequestDto` unchanged (`UserVocabularyDTOs.cs:26-29`).
- Keep `UserVocabularyItemDto.PartOfSpeech` and `.PreferredWordDefinitionId` unchanged (`UserVocabularyDTOs.cs:3-18`). POS remains useful display data.
- No broad cleanup of unused `UserWordDTOs.cs` is part of R5.

## 9. Angular Changes

Current Angular behavior already matches one-word identity:

- lookup source is word-level through backend `IsInUserVocabulary`;
- one Add button is disabled when the word is saved (`word-lookup.component.ts:199-213,328-365` and template lines 139-145);
- saved list items use `VocabularyItem.id` (`UserWord.Id`), not POS, for favorite/preference endpoints;
- preferred definition is changed in place (`word-lookup.component.ts:574-617`);
- POS is displayed/search-suggestion metadata, not a list key (`word-lookup.model.ts:47-62`; component lines 85-100).

Required/minimal work:

1. Keep the existing add payload for compatibility; it may continue sending POS and preferred ID.
2. If the backend introduces the typed result, add the small Angular result interface and use `alreadyExisted/message` for accurate toast text. Continue setting the word source to `user` and refreshing the list.
3. Ensure successful preferred update mutates the same `VocabularyItem`; optionally update its displayed `partOfSpeech` from the selected option if the list stays visible/cached. The current local update changes definition/example but not POS (`word-lookup.component.ts:587-605`), so this is the one concrete UI consistency adjustment.
4. Do not add multiple cards per POS, change list identity, or refactor `WordLookupComponent`.

Backend/API deployment does not require a simultaneous Angular deployment because request and envelope remain backward-compatible. The Angular metadata/display adjustment may follow immediately, but should ship in the same release for truthful UX.

## 10. Quiz and Dependent Data Preservation

### Quiz

- Keep every `UserWord.Id`; no `QuizResult.UserWordId` reassignment is allowed.
- Keep `CorrectAnswers`, `TotalAttempts`, `LastReviewedAt`, and `LastCorrectAt` untouched by add-repeat/preference update.
- Retained synchronized POS preserves current quiz-generation filters (`QuizService.cs:29-47`). Prefer the explicit definition when present; POS fallback remains only for null preference.
- Once the new identity is guaranteed, `GroupBy(WordId).First()` (`QuizService.cs:50-59`) is redundant. Remove it only if doing so simplifies the query safely and tests show no behavior change; otherwise retain it as defensive projection behavior for this release. It must not choose between valid identities anymore.
- R4 submit/scoring logic (`QuizService.cs:205-329`) requires no change.

### Direct dependents

| Dependent | R5 change | Verification |
|---|---|---|
| `QuizResult` | No model, FK, query-history, or data change. | Test existing rows keep the same `UserWordId`; scoring still updates the same counters. |
| `SampleSentence` | No model, FK, DTO, or data change. | Test existing rows keep the same `UserWordId` after preference change. |
| `User`, `Word`, `PartOfSpeech`, `WordDefinition` navigations | No relationship/delete change. | Model/migration review confirms only identity index changes. |

No other entity directly references `UserWord`. Cascades are not invoked because no row is deleted or replaced.

## 11. Concurrency Strategy

The database unique `(UserId, WordId)` index is the authoritative final guard. The service pre-query is an optimization/idempotent fast path, not the concurrency guarantee.

For the losing insert:

1. Catch `DbUpdateException` only around the insert `SaveChangesAsync`.
2. Inspect the full inner-exception chain.
3. For SQL Server, require duplicate-key error number 2601 or 2627 **and** evidence naming `IX_UserWords_UserId_WordId` (or the exact UserWords/columns). For SQLite integration tests, recognize extended code 2067 plus the expected columns, following the narrow approach already used by `QuizService.IsQuizSubmissionDuplicate` (`QuizService.cs:422-457`).
4. Do not classify an unrelated unique constraint or arbitrary `DbUpdateException` as an idempotent race.
5. Detach the failed added `UserWord` or clear the change tracker so it cannot be reinserted.
6. Re-query `(UserId, WordId)` from the database, preferably no-tracking.
7. If found, return the same typed success with its stable ID and `AlreadyExisted = true`.
8. If not found, log and return the normal add failure; the presumed race was not recoverable and must not be hidden.
9. All other database errors follow existing logging/failure behavior.

Avoid a broad reusable exception framework for R5. A private narrowly named helper in `WordService` is consistent with current architecture.

## 12. Test Plan

### Existing tests expected to pass unchanged

Authentication/ownership isolation, missing canonical-word rejection, favorite ownership, foreign-word preferred-definition rejection, invalid IDs, search/list ownership, and the R4 `QuizApiTests` should remain green (`VocabularyOwnershipApiTests.cs:39-93,119-323`; `QuizApiTests.cs`).

### Existing tests to modify

- Replace `DuplicateCurrentLogicalSaveIsIdempotentForSamePartOfSpeech` with two-column identity assertions and typed response checks (`VocabularyOwnershipApiTests.cs:95-117`).
- Extend `AuthenticatedUserSavesVocabularyWithExpectedRelationships` to verify preference/POS derivation and new add response (`VocabularyOwnershipApiTests.cs:14-37`).
- Extend preferred-definition coverage from same-POS only to cross-POS synchronization and state preservation (`VocabularyOwnershipApiTests.cs:146-213`).
- Enhance `IntegrationTestSeeder` to seed multiple definitions/POS and dependent data without creating an invalid duplicate identity (`IntegrationTestSeeder.cs:31-112`).

### Detailed matrix

| Test | Type | Expected Result | Protects Against |
|---|---|---|---|
| Save a new canonical word | Backend integration | 200; one row; `AlreadyExisted=false`; valid preference/POS | Broken create path |
| Repeat same word/same POS | Backend integration | 200; existing ID; one row; no state mutation | Sequential duplicate |
| Repeat same word/different POS | Backend integration | 200; existing ID; one row; existing preference/state unchanged | Old three-column identity |
| Repeat same word/different preferred ID | Backend integration | Existing row returned unchanged | Add becoming implicit preference update |
| Different users save same WordId | Backend integration | One row per user; both succeed | Over-broad uniqueness |
| Supplied add preference belongs to another word | Backend integration | 400; no row | Cross-word preference |
| Preferred endpoint rejects another word | Existing/extended integration | 400; row unchanged | Preference integrity |
| Cross-POS preferred change | Backend integration | 200; same row/ID; preference and POS synchronized | Replacement/conflict logic |
| Preserve `IsFavorite` and `PersonalNotes` | Backend integration | Values unchanged after preference update | User-state loss |
| Preserve counters | Backend integration | Correct/attempt totals unchanged | R4 regression |
| Preserve review timestamps and `AddedAt` | Backend integration | Exact values unchanged | History reset |
| Preserve existing QuizResult | Backend integration | Same row count and `UserWordId` | Cascade/reparent/history loss |
| Preserve existing SampleSentence | Backend integration | Same row count and `UserWordId` | Cascade/reparent loss |
| Quiz uses new preferred definition/POS | Backend integration | Question projection uses new meaning; same saved ID | Stale POS quiz behavior |
| Quiz scoring after preference update | Backend integration | Same UserWord counters/history updated correctly | R4 interaction |
| Two concurrent adds | Backend integration with synchronization interceptor/barrier | Both deterministic successes; exactly one row | Check-then-insert race |
| Losing concurrent response | Backend integration | Same `UserWordId`; loser `AlreadyExisted=true` | Avoidable 500/generic failure |
| Unrelated `DbUpdateException` | Service/integration with interceptor | Normal failure; not reported as duplicate success | Over-broad exception swallowing |
| SQL Server migration on clean representative data | Migration integration/manual disposable SQL Server | Migration succeeds; all IDs/counts/state preserved | Upgrade/data loss |
| Migration with seeded two-column duplicates | Migration integration on disposable SQL Server | `THROW`; old index/schema remains; no rows mutated | Unsafe automatic merge/partial DDL |
| Post-migration index metadata | Migration integration | Unique pair exists; unique triple absent | Wrong generated migration |
| Required POS remains | Migration integration | Column NOT NULL; FK/index remain; no nulls | Scope creep/schema regression |
| Down migration | Migration integration | Pair removed; triple restored; data preserved | Broken rollback |
| Add response toast | Angular unit, if UI consumes typed result | Added vs already-existing message correct | Misleading UX |
| Preferred UI update | Angular unit | Same item ID; definition/example/POS update; favorite unchanged | Client-side replacement/stale POS |

SQLite `EnsureCreated` integration tests prove runtime configuration and behavior. They do not replace real SQL Server migration tests, particularly for `THROW`, index metadata, and error numbers.

## 13. Manual Verification Plan

### SSMS after local/test migration and after deployment

- Query duplicate `(UserId, WordId)` groups; expect zero.
- Inspect `sys.indexes/sys.index_columns`; expect unique `IX_UserWords_UserId_WordId`.
- Confirm `IX_UserWords_UserId_WordId_PartOfSpeechId` is absent.
- Confirm `PartOfSpeechId` remains NOT NULL, populated, indexed, and FK-backed.
- Query non-null preferences joined to definitions; expect no `WordId` mismatch.
- Query retained POS against preferred-definition POS; expect no mismatch.
- Compare `UserWords`, `QuizResults`, and `SampleSentences` counts and FK-orphan checks with pre-deployment baseline.
- Confirm the R5 migration appears once in `__EFMigrationsHistory`.

### User flows

1. Save a canonical word; verify one list entry.
2. Repeat the add request, including a different POS/preference payload; verify no second entry and no existing state changes.
3. Favorite the word and add/seed notes/history as available.
4. Change to a definition with another POS; verify the same card/ID displays the new definition/POS.
5. Verify favorite, notes, counters, timestamps, quiz history, and sample data remain.
6. Run and submit a quiz containing the word; verify the preferred meaning is used and the same row's counters advance.
7. Test another user can save the same canonical word.

## 14. Deployment Plan

1. Take and verify a recoverable SQL Server backup per the existing manual deployment process (`docs/Deployment/SmarterASP-Manual-Deployment.md`).
2. Record pre-deployment row counts and run the R5 read-only audits from `R5-analysis.md` immediately before change.
3. If any duplicate pair or consistency mismatch exists, stop. Do not deploy code/migration and do not auto-merge.
4. Use a short maintenance window or otherwise quiesce vocabulary writes. This closes the interval between final audit, index migration, and backend deployment.
5. Apply the reviewed R5 migration first. It is the final invariant guard. If its precondition throws, the migration fails and old application/schema remain in service after rollback of the deployment attempt.
6. Deploy the backend immediately after successful migration. The old backend is read-compatible with the new schema but a different-POS add could receive a generic unique failure; minimizing this window avoids that UX.
7. Deploy the compatible Angular update if included. It need not be atomic with the backend because request and envelope remain compatible.
8. Restart backend instances so no stale process state remains; active in-memory quiz sessions may expire across normal deployment restart, consistent with current architecture.
9. Run SSMS and application-flow verification from section 13.
10. Monitor backend logs for expected-index violations, add failures, preference validation failures, and quiz regressions.
11. Record migration ID, verification results, deployed versions, and backup reference in the R5 completion note.

## 15. Rollback Plan

1. Prefer forward correction for application-only defects when the database invariant is sound.
2. If full rollback is necessary, quiesce writes and back up the post-deployment database first.
3. Roll back the backend before reopening traffic only in coordination with schema downgrade; avoid running new code against old identity semantics or old code against the new unique index for an extended period.
4. Apply the migration `Down`: drop pair uniqueness and restore the exact triple index. No row/column/FK mutation is expected.
5. Redeploy prior backend/UI artifacts and perform smoke tests.
6. Query duplicate pairs immediately after rollback and again before any future R5 attempt. The downgraded system can recreate POS variants.
7. If the Up precondition failed initially, no schema rollback should be necessary; verify transaction/migration history, return the old app to service, and investigate duplicates separately.
8. Never restore a backup or delete/merge rows casually: restoration discards post-backup user activity and requires an explicit incident decision.

## 16. Implementation Sequence

1. Create/adjust behavioral integration tests for two-column identity, repeat payload variants, cross-POS preference update, and state/dependent preservation; confirm target tests fail for the expected current reasons.
2. Add narrowly typed add-result contract and update controller/service signatures if chosen; keep route/envelope/status compatible.
3. Change `AddToVocabularyAsync` pre-check to `(UserId, WordId)`, make existing-row response idempotent, and make new-row preference/POS resolution validate and normalize consistently.
4. Simplify `SetPreferredDefinitionAsync`: remove POS-entry conflict logic and assign preference/POS together on the same row.
5. Add narrow unique-violation classification and losing-request recovery; add synchronized concurrency and unrelated-failure tests.
6. Update vocabulary/quiz projections only as needed to make preference authoritative while retaining null-preference POS fallback; run all R4 quiz tests.
7. Apply minimal Angular typed-result/toast and displayed-POS update with unit tests, if backend result metadata is consumed.
8. Change EF configuration to unique `(UserId, WordId)` and update scoped entity/configuration comments.
9. Generate the R5 migration; add the fail-fast SQL before index operations; review generated `Up`, `Down`, and snapshot. Do not accept unrelated generated changes.
10. Run formatting/build plus full backend and Angular automated suites.
11. Validate Up/failure/Down paths on disposable SQL Server databases created through the real migration chain; verify IDs, counts, indexes, FKs, and data.
12. Prepare backup, maintenance-window, deployment, rollback, and verification notes.
13. Re-run deployed read-only audits. Abort if results differ from the clean baseline.
14. Apply migration and deploy backend/UI in the order described in section 14.
15. Complete post-deployment SQL, user-flow, quiz, and log verification; publish the R5 completion record.

## 17. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| New duplicates appear before migration | Quiesce writes; duplicate precondition inside migration; unique pair is final guard. |
| Automatic merge loses notes/history | No merge logic; fail and investigate. |
| Concurrent loser surfaces generic failure | Narrow expected-index detection, tracker cleanup, winner re-query, idempotent response. |
| Unrelated DB error becomes false success | Require provider error code plus expected index/columns; test unrelated failures. |
| Repeat add changes selected meaning/state | Existing-row fast path returns without mutation; explicit preference endpoint owns changes. |
| Preference and retained POS diverge | Validate same word and assign both from one loaded definition in one save; audit/test mismatches. |
| Quiz uses stale/wrong definition | Preference-first projection, synchronized POS, cross-POS quiz regression tests. |
| Dependent data is deleted/reparented | Never delete/replace UserWord; assert stable IDs and dependent FKs/counts. |
| Migration generated from snapshot contains noise | Review exact operations/script; reject unrelated changes. |
| SQLite tests mask SQL Server behavior | Disposable SQL Server migration/error-number/index tests. |
| Mixed-version deployment causes transient add errors | Maintenance/quiesce writes; migration then immediate backend deployment; compatible API/UI. |
| Rollback reopens duplicate risk | Document downgrade hazard; audit after rollback and before re-upgrade. |

The largest implementation risk is race-safe, provider-specific duplicate handling combined with deployment ordering: the system must never mistake an unrelated database failure for idempotent success, and the unique index must be installed before relying on application recovery.

## 18. Scope Boundaries

### Required for R5

- Unique `(UserId, WordId)` EF/database identity and removal of the triple identity index.
- Fail-fast migration precondition without merge.
- Two-column idempotent add semantics and deterministic concurrency recovery.
- Same-word preference validation and synchronized retained POS.
- Stable in-place updates preserving all state/dependents.
- Minimal additive API/Angular alignment.
- Backend, Angular-if-changed, concurrency, dependent-preservation, and SQL Server migration tests.
- Manual backup/deployment/rollback/verification notes.

### Deferred

- Physical removal of `PartOfSpeechId` after preferences/projections no longer require fallback.
- Optional database-level composite enforcement of preference-to-word membership.
- Making `PreferredWordDefinitionId` required/backfilling legacy nulls.
- Cleanup of unused/stale UserWord DTOs/comments outside touched lines.
- Removing quiz duplicate grouping if not necessary for correctness in this release.

### Out of Scope

- Duplicate merge implementation while data is clean.
- WordService decomposition, repository pattern, CQRS, or API-wide refactoring.
- Angular `WordLookupComponent` decomposition or unrelated UI/accessibility work.
- Server-side paging, new learning fields, spaced repetition, or analytics.
- Quiz-session architecture redesign or R4 counter reconciliation.
- SampleSentence feature expansion.
- Removing/redesigning `PartOfSpeechId` in R5.
- CI/CD or infrastructure redesign.

## 19. Definition of Done

- Database and EF model enforce one `UserWord` per `(UserId, WordId)`.
- Old unique `(UserId, WordId, PartOfSpeechId)` identity is absent.
- Same user cannot create another saved row by changing POS or preferred-definition payload.
- Different users can save the same canonical word.
- Repeat adds return the existing stable `UserWord.Id` without state mutation.
- Concurrent duplicate adds yield exactly one row and deterministic idempotent outcomes.
- Only the expected unique-index violation is recovered; unrelated failures remain failures.
- Preferred definition is validated against the same `WordId` and updated in place.
- `PartOfSpeechId` remains required/synchronized but is not identity.
- `PersonalNotes`, favorite, counters, timestamps, `QuizResult`, `SampleSentence`, and all existing history survive preference changes.
- No existing row is merged, deleted, replaced, or orphaned.
- Vocabulary/list/search/quiz projections use a consistent selected meaning; R4 behavior remains correct.
- Required backend and Angular tests pass.
- Migration Up and Down are verified against representative clean SQL Server data.
- Fail-fast behavior is verified on a disposable database containing deliberate duplicates, with no partial mutation.
- Migration contains no unrelated schema changes.
- Backup and rollback instructions are reviewed.
- Pre/post-deployment manual schema, consistency, dependent-count, user-flow, and quiz checks pass.
- R5 completion evidence records migration, test, and deployed verification results.

## 20. Implementation Readiness

READY TO IMPLEMENT

## 21. Implementation Results — 2026-08-29

### Status

**IMPLEMENTATION COMPLETE — MANUAL DEPLOYMENT PENDING**

R5 was implemented without changing the approved design. Production/deployed SQL Server was not modified.

### Files changed

- `VocabularyApp.Data/ApplicationDbContext.cs`
- `VocabularyApp.Data/Models/UserWord.cs`
- `VocabularyApp.Data/Migrations/20260829155134_CorrectUserWordIdentity.cs`
- `VocabularyApp.Data/Migrations/20260829155134_CorrectUserWordIdentity.Designer.cs`
- `VocabularyApp.Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `VocabularyApp.WebApi/Services/WordService.cs`
- `VocabularyApp.WebApi/DTOs/UserVocabularyDTOs.cs`
- `VocabularyApp.UI/src/app/models/word-lookup.model.ts`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts`
- `VocabularyApp.WebApi.Tests/Integration/VocabularyOwnershipApiTests.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularySaveSynchronizationInterceptor.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyPersistenceFailureInterceptor.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/R5MigrationDefinitionTests.cs`

### Implemented behavior

- EF/database identity is unique `(UserId, WordId)` via `IX_UserWords_UserId_WordId`; POS remains required compatibility state.
- Add checks the pair only. A repeat request returns HTTP 200 with the existing stable `UserWordId`, canonical `WordId`, `AlreadyExisted=true`, and does not mutate the entry.
- A new supplied preference must belong to the canonical word; its POS is derived server-side. Requests without preference retain the existing compatible POS/fallback flow.
- Preferred-definition update removes POS identity conflict behavior, updates the same row, and synchronizes POS from the selected definition.
- Concurrent losing inserts classify only SQL Server 2601/2627 or SQLite 2067 when the expected index/columns are identified, detach the failed insert, reload the winner, and return idempotent success. Unrelated failures retain failure behavior.
- Angular preserves the request route/shape, consumes additive result metadata, reports repeat success truthfully, and updates displayed definition/POS on the same item.

### Migration

Migration: `20260829155134_CorrectUserWordIdentity`.

`Up` executes a `THROW 51000` duplicate-pair precondition, drops `IX_UserWords_UserId_WordId_PartOfSpeechId`, and creates unique `IX_UserWords_UserId_WordId`. `Down` drops the pair index and restores the old composite index. No columns, FKs, or data are changed.

### Automated verification

- Baseline backend: 158/158 passed.
- Final backend: 166/166 passed.
- Focused Angular baseline: 11/11 passed.
- Focused Angular final: 13/13 passed.
- .NET build: succeeded, 0 errors. Final run emitted two `NU1900` warnings because NuGet vulnerability metadata was unreachable; compilation was unaffected.
- Angular production build: succeeded. It retains the existing word-lookup SCSS budget warning (2.80 kB versus 2.05 kB budget).
- Full Angular run: 19/26 passed; seven unrelated pre-existing test failures remain in `ApiService`, `AuthService`, dashboard/login/signup test providers and stale `AppComponent` title expectations. The R5-focused suite is fully green; those failures were not changed under R5 scope.

### SQL Server migration verification

Used isolated LocalDB databases only:

- Full real migration chain plus R5 applied successfully to `VocabularyAppR5Verification_20260829`.
- Post-migration metadata showed unique `IX_UserWords_UserId_WordId`, no old composite identity, required/non-null POS, and nullable preference.
- Read-only checks returned zero duplicate pairs, cross-word preferences, POS mismatches, and null POS rows.
- A representative pre-R5 database upgraded with the same `UserWord.Id`, notes, favorite, counters, one quiz result, and one sample sentence preserved.
- `Down` successfully restored `IX_UserWords_UserId_WordId_PartOfSpeechId`.
- After deliberately inserting two POS variants for one pair in the isolated downgraded database, re-Up failed with SQL error 51000. Both rows and the old index remained; the R5 migration-history row remained absent. This verifies fail-fast/no-partial-migration behavior.

### Deviations and remaining work

No design deviation was required. `QuizService` was left unchanged because retained synchronized POS preserves its current projection and all R4 tests pass. No manual browser user flow was run because no development API/UI server was launched; automated API/UI coverage exercises the R5 flows.

Remaining manual deployment steps are the approved backup, immediate pre-deployment read-only audits, a short vocabulary-write maintenance window, production migration, backend/UI deployment, post-deployment SQL checks, and smoke/quiz verification. If the duplicate precondition fails, stop without merging and investigate separately.
