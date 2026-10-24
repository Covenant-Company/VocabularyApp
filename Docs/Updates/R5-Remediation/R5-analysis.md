# R5 Analysis — Correct UserWord Identity

## 1. Executive Summary

The original R5 product finding is still valid. The approved rule is one saved canonical word per `(UserId, WordId)`, but the current write path treats `(UserId, WordId, PartOfSpeechId)` as the logical identity and can create a second `UserWord` for another part of speech. The current EF runtime model and model snapshot also declare that three-column combination unique and contain no unique constraint on `(UserId, WordId)`.

R5 remains critical because a second row would split user-owned state across two identities: favorite and notes, learning counters and review timestamps, `QuizResult` history, and `SampleSentence` rows. The supplied live-data check found **zero existing duplicate `(UserId, WordId)` groups**, so the verified issue is presently structural, behavioral, and concurrency-related rather than a known cleanup incident.

The likely R5 scope is smaller than the original worst-case plan: add database uniqueness for `(UserId, WordId)`, make add behavior idempotent at that identity, make a preferred-definition change update the existing row, update projections/quiz selection to consistently derive the selected part of speech, and add targeted integration/concurrency/migration tests. A production merge should not be designed or run unless deployment-time evidence finds duplicates. The migration should fail safely on unexpected duplicates rather than silently deleting or merging user data.

`PartOfSpeechId` is redundant in the desired model when `PreferredWordDefinitionId` is present, but current list, search, quiz, add, and update code actively depends on it. Removing it in the same migration is technically coherent but larger and riskier. The final recommendation is to stop it participating in identity, retain it during R5 as synchronized compatibility/derived state, and consider removal in a tightly scoped follow-up after all reads derive through the preferred definition.

No application code, migration, schema, test, configuration, or database change was made by this analysis. Only this report was added.

## 2. Current Architecture

### UserWord columns and key

`UserWord` has the following EF-mapped columns (`VocabularyApp.Data/Models/UserWord.cs:6-43`):

| Column | CLR/schema model | Purpose |
|---|---|---|
| `Id` | non-null `int` | Surrogate primary key |
| `UserId` | non-null `int` | Owning user FK |
| `WordId` | non-null `int` | Canonical word FK |
| `PersonalNotes` | nullable string, max 500 | User notes |
| `AddedAt` | non-null `DateTime` | Saved timestamp |
| `LastReviewedAt` | nullable `DateTime` | Learning state |
| `LastCorrectAt` | nullable `DateTime` | Learning state |
| `CorrectAnswers` | non-null `int` | R4 counter |
| `TotalAttempts` | non-null `int` | R4 counter |
| `PartOfSpeechId` | non-null `int` in current entity/runtime model | Selected/legacy POS FK |
| `PreferredWordDefinitionId` | nullable `int` | Selected definition FK |
| `IsFavorite` | non-null `bool` | User state |

`CreatedAt`, `CustomDefinition`, and `DifficultyLevel` are `[NotMapped]` and do not exist in the current EF schema (`UserWord.cs:12-13,33-39`). Comments and legacy DTOs still refer to removed fields, but they are not persisted.

The primary key is `UserWord.Id` (`ApplicationDbContext.cs:71-74`; snapshot `ApplicationDbContextModelSnapshot.cs:269-322`).

### Relationships and delete behavior

| Principal relationship | FK nullability in current model | Delete behavior | Evidence |
|---|---:|---|---|
| `UserWord -> User` | required | Cascade | `ApplicationDbContext.cs:77-80` |
| `UserWord -> Word` | required | Cascade | `ApplicationDbContext.cs:82-85` |
| `UserWord -> PartOfSpeech` | required | Restrict | `ApplicationDbContext.cs:88-91` |
| `UserWord -> PreferredWordDefinition` | optional | NoAction | `ApplicationDbContext.cs:93-96` |
| `SampleSentence -> UserWord` | required | Cascade | `ApplicationDbContext.cs:113-116`; `SampleSentence.cs:9-23` |
| `SampleSentence -> User` | required | NoAction | `ApplicationDbContext.cs:108-111` |
| `QuizResult -> UserWord` | required | Cascade | `ApplicationDbContext.cs:130-133`; `QuizResult.cs:9-29` |
| `QuizResult -> User` | required | NoAction | `ApplicationDbContext.cs:125-128` |
| `WordDefinition -> Word` | required | Cascade | `ApplicationDbContext.cs:56-59` |
| `WordDefinition -> PartOfSpeech` | required | Restrict | `ApplicationDbContext.cs:61-64` |

`UserWord` exposes a collection navigation only for `SampleSentences` (`UserWord.cs:43`). `QuizResult` has a required navigation to `UserWord`, but `UserWord` has no inverse quiz-results collection (`QuizResult.cs:27-29`; `ApplicationDbContext.cs:130-133`). `WordDefinition` likewise has no inverse collection for preferred uses. These are valid unidirectional relationships, not missing FKs, but the absent quiz navigation makes dependent-history discovery less obvious.

Direct database dependents of `UserWords` are exactly `QuizResults.UserWordId` and `SampleSentences.UserWordId`. Both FKs are non-nullable and cascade on `UserWord` deletion. `ChatHistory` does not reference `UserWord`.

## 3. Current Add-to-Vocabulary Behavior

The full request path is:

1. Angular `WordLookupComponent.addToVocabulary()` selects the first definition of the first POS group and posts word text, POS, and preferred-definition ID (`VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts:328-347`).
2. `POST /api/words/vocabulary/add` requires authentication, obtains `NameIdentifier`, and calls `IWordService.AddToVocabularyAsync` (`VocabularyApp.WebApi/Controllers/WordsController.cs:71-103`).
3. `WordService.AddToVocabularyAsync` requires an existing canonical `Word` matched by exact text (`VocabularyApp.WebApi/Services/WordService.cs:211-224`).
4. The service resolves the submitted POS, defaulting missing or unknown values to Noun (`WordService.cs:226,359-371`).
5. It checks existence by **`UserId + WordId + PartOfSpeechId`** (`WordService.cs:228-234`). Same-POS duplicate adds return HTTP 200 with an “already” message; they do not update the existing preference or any other state.
6. A different-POS request creates another `UserWord`, resolving a preferred definition only within the requested word/POS (`WordService.cs:236-252,577-599`). An invalid requested definition silently falls back to the first definition in that POS.

Therefore current service logic can create two rows for the same user and canonical `WordId` when different parts of speech are supplied. The response does not return `UserWord.Id`; its `wordId` is the canonical `Word.Id` (`WordService.cs:252`), which is potentially ambiguous for callers.

Duplicate prevention is only logically applied to the three-column identity. In the current EF runtime model it is also backed by a unique three-column index (`ApplicationDbContext.cs:98-99`), but there is no database-backed protection for the approved two-column identity.

There is also a check-then-insert race. Two concurrent same-POS requests can both pass `AnyAsync`; the current model-created database lets one fail on the composite unique index, and the catch converts it to a generic failure (`WordService.cs:254-258`) rather than deterministic idempotent success. If the deployed database follows the executable migration history discussed in section 5 and lacks that index, even same-POS duplicates can be inserted. Different-POS concurrent inserts are allowed by the present runtime constraint in all cases.

The UI usually suppresses the risk: lookup marks a word saved using only `UserId + WordId` (`WordService.cs:46-51,183-188`), and Angular disables the single Add button whenever that flag is true (`word-lookup.component.ts:199-213`; `word-lookup.component.html:139-145`). It also submits only the first POS group. This explains why normal UI use is unlikely to create cross-POS duplicates, but direct/replayed/concurrent API calls still can; UI suppression is not an invariant.

## 4. Current Preferred-Definition Behavior

### Assignment on add

On add, `ResolvePreferredDefinitionIdAsync` accepts the requested ID only if it belongs to both the canonical `WordId` and selected `PartOfSpeechId`; otherwise it chooses the first definition for that word/POS, or null if none exists (`WordService.cs:577-599`). Thus add does validate both word and POS, but it treats a bad preferred ID as a fallback rather than a validation error.

### Assignment after save

`PUT /api/words/vocabulary/{userWordId}/preferred-definition` validates a positive ID in the controller and service (`WordsController.cs:206-240`; `WordService.cs:261-268`). The service loads the row by both `Id` and owning `UserId`, then requires the definition's `WordId` to equal the saved `UserWord.WordId` (`WordService.cs:270-292`). This correctly rejects a definition from another canonical word.

If the selected definition has a different POS, the service:

- checks whether another row already occupies `(UserId, WordId, selected PartOfSpeechId)`;
- rejects the update if one exists; otherwise
- mutates `UserWord.PartOfSpeechId` to the selected definition's POS and then updates `PreferredWordDefinitionId` (`WordService.cs:294-314`).

Changing the preferred definition therefore currently changes a component of the logical/database identity. It does not create or replace the row, so its `Id`, favorite, notes, counters, timestamps, `QuizResult` FKs, and `SampleSentence` FKs remain intact. It can, however, be rejected because of another sense-row or encounter a race/unique-index violation if a conflicting row is inserted after its pre-check. The catch again returns only a generic failure (`WordService.cs:323-327`).

The update changes neither `AddedAt` nor learning fields. It also does not validate or repair pre-existing inconsistency between `PreferredWordDefinitionId` and `PartOfSpeechId` except when this endpoint is called.

The Angular editor deliberately offers definitions across all parts of speech: it fetches all canonical definitions and does not filter `buildDefinitionOptions` by current POS (`word-lookup.component.ts:497-525,528-562`). It updates the same `VocabularyItem.id` in place after a successful PUT (`word-lookup.component.ts:574-617`). The frontend therefore already models preferred-definition change as mutation of one saved entry, even though the backend may reject it due to the old POS identity.

## 5. Current Database Constraints

### Current entity/runtime model and snapshot

The runtime model and snapshot agree on:

- primary key `PK_UserWords (Id)`;
- non-unique indexes on `PartOfSpeechId`, `PreferredWordDefinitionId`, and `WordId`;
- unique index `IX_UserWords_UserId_WordId_PartOfSpeechId`;
- no index or unique constraint on `(UserId, WordId)`;
- required `PartOfSpeechId` and optional `PreferredWordDefinitionId`;
- relationships/delete behaviors listed in section 2.

Evidence: `ApplicationDbContext.cs:70-100`; `ApplicationDbContextModelSnapshot.cs:269-322,448-480`.

### Executable migration-chain audit

Every executable migration that changes `UserWords` is listed below. Designer files are excluded from executable effects.

| Migration | UserWords change | Resulting schema state |
|---|---|---|
| `20251004202304_InitialCreate` | Creates `UserWords`; required `UserId`, `WordId`, and `PartOfSpeechId`; cascade FKs to User/Word; Restrict FK to POS; indexes on POS and Word; unique `(UserId, WordId, PartOfSpeechId)` (`20251004202304_InitialCreate.cs:87-127,276-290`). | Required POS; three-column identity enforced. |
| `20251009004514_RemoveUserWordFields` | Drops POS FK and composite unique index; removes `CustomDefinition`, `DifficultyLevel`, and `IsFavorite`; makes POS nullable; adds non-unique `IX_UserWords_UserId`; re-adds POS FK with provider default/no cascade (`20251009004514_RemoveUserWordFields.cs:13-51`). | Nullable POS; no UserWord uniqueness; UserId/WordId/POS individual supporting indexes. |
| `20251009004653_DropUserWordPartOfSpeechId` | `Up` and `Down` are empty (`20251009004653_DropUserWordPartOfSpeechId.cs:8-20`). | No executable change, regardless of its designer target model. |
| `20251030143529_AddAudioUrlToWords` | Despite its name, drops the temporary UserId index/POS FK; changes null or invalid POS values to ID 1; makes POS required; recreates unique `(UserId, WordId, PartOfSpeechId)`; restores Restrict POS FK (`20251030143529_AddAudioUrlToWords.cs:11-46`). It does not add `AudioUrl`. | Required POS; three-column identity restored; final POS FK/index state now matches runtime model. |
| `20260505194632_AddFavoriteToUserWords` | Adds required `IsFavorite` with default false (`20260505194632_AddFavoriteToUserWords.cs:11-18`). | Adds current favorite column; identity unchanged. |
| `20260725020317_AddPreferredDefinitionToUserWords` | Adds nullable `PreferredWordDefinitionId`, its non-unique index, and FK to `WordDefinitions` without cascade (`20260725020317_AddPreferredDefinitionToUserWords.cs:11-30`). | Optional preference added; identity unchanged. |

The other migrations do not execute changes against `UserWords`; some designer files repeat its model because designers contain the entire target model. The final executable chain therefore expects the same UserWord identity and POS nullability as the current entity, fluent configuration, and snapshot: required `PartOfSpeechId` and unique `(UserId, WordId, PartOfSpeechId)`. It does **not** enforce unique `(UserId, WordId)`.

The earlier version of this report incorrectly stopped the audit before `20251030143529_AddAudioUrlToWords`; that conclusion is corrected here. There is no final repository-level mismatch on UserWord POS nullability, its FK/delete behavior, or its three-column unique index. The unusual empty migration and misleading audio migration name are historical hazards, but their final executable effects are determinable.

Manual verification was subsequently completed. The reported deployed schema, constraints, data consistency, and populated migration history agree with the final executable chain and current runtime/snapshot model. No deployed drift relevant to R5 was identified.

## 6. Current Data Findings

The developer executed the read-only verification queries against the deployed SQL Server database and supplied the results. The duplicate `(UserId, WordId)` query returned zero rows. No existing duplicate cleanup is justified by current evidence. This report did not connect to or alter the database.

Repository evidence explains why zero rows are plausible but cannot prove a single exclusive cause:

- normal UI lookup disables Add once any row for the canonical word exists;
- the UI posts only one chosen default POS;
- same-POS sequential API adds are rejected by service logic;
- the current runtime model prevents same-POS duplicates when its schema is actually present;
- no application/database rule prevents different-POS duplicates at the approved identity.

Thus zero observed duplicates are most likely the result of UI/service usage patterns plus current data history, not enforcement of the desired invariant.

## Deployed Schema Verification Required

### Verification status: completed

The developer reported these deployed results:

| Check | Deployed result | Assessment |
|---|---|---|
| A. Columns/nullability | Expected `UserWords` structure; `PartOfSpeechId` NOT NULL; `PreferredWordDefinitionId` nullable | Matches runtime/snapshot/final migration chain |
| B. Indexes | Unique `(UserId, WordId, PartOfSpeechId)`; no unique `(UserId, WordId)` | Confirms the R5 structural weakness |
| C. Foreign keys | User, Word, POS, and preferred-definition FKs present and consistent | Matches expected relationships |
| D. Duplicate `(UserId, WordId)` | Zero rows | No known merge candidates; new two-column index is data-compatible |
| E. Duplicate `(UserId, WordId, PartOfSpeechId)` | Zero rows | Consistent with current unique constraint |
| F. Cross-word preferred definitions | Zero rows | Existing preference-to-word data is valid |
| G. POS/preference mismatches | Zero rows | Retained compatibility POS is currently synchronized |
| H. Null `PartOfSpeechId` | 0 | Existing data satisfies required POS |
| `__EFMigrationsHistory` | Populated through later preferred-definition and quiz-result migrations | No evidence that deployment is behind the relevant chain |

These results resolve the deployed-schema blocker. They confirm the exact current identity while showing none of the duplicate or consistency defects R5 must defensively guard against. The SQL below remains the required pre-deployment rerun/audit set because data can change between analysis and release.

The following small, read-only SQL Server checks were used. No query modifies data or schema.

### A. Columns and nullability

```sql
SELECT c.column_id, c.name, t.name AS data_type, c.max_length, c.is_nullable
FROM sys.columns AS c
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.UserWords')
ORDER BY c.column_id;
```

This verifies deployed columns and nullability. Expect required `Id`, `UserId`, `WordId`, and `PartOfSpeechId`; nullable `PreferredWordDefinitionId`. Missing columns or different nullability indicate migration/manual drift.

### B. Indexes and uniqueness

```sql
SELECT i.name, i.is_unique, i.is_primary_key, ic.key_ordinal, c.name AS column_name
FROM sys.indexes AS i
JOIN sys.index_columns AS ic
  ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns AS c
  ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID(N'dbo.UserWords')
ORDER BY i.name, ic.key_ordinal;
```

Expect PK `Id`, non-unique POS/Word/preference indexes, and unique `(UserId, WordId, PartOfSpeechId)`. There should not yet be unique `(UserId, WordId)`. Any other result changes the eventual migration DDL.

### C. Foreign keys and delete actions

```sql
SELECT fk.name,
       pc.name AS parent_column,
       OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS referenced_schema,
       OBJECT_NAME(fk.referenced_object_id) AS referenced_table,
       rc.name AS referenced_column,
       fk.delete_referential_action_desc
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
WHERE fk.parent_object_id = OBJECT_ID(N'dbo.UserWords')
ORDER BY fk.name;
```

Expect User and Word FKs with `CASCADE`, POS with `NO_ACTION` (SQL Server representation of Restrict), and preferred definition with `NO_ACTION`. Missing/different FKs indicate drift and may change migration ordering.

### D and E. Duplicate identities

```sql
SELECT UserId, WordId, COUNT(*) AS nums
FROM dbo.UserWords
GROUP BY UserId, WordId
HAVING COUNT(*) > 1
ORDER BY nums DESC;

SELECT UserId, WordId, PartOfSpeechId, COUNT(*) AS nums
FROM dbo.UserWords
GROUP BY UserId, WordId, PartOfSpeechId
HAVING COUNT(*) > 1
ORDER BY nums DESC;
```

Expect zero rows from both. Rows from the first query block the new two-column index and require an exception plan; rows from the second also prove the current three-column constraint is absent or was bypassed.

### F and G. Preferred-definition consistency

```sql
SELECT uw.Id, uw.UserId, uw.WordId, uw.PreferredWordDefinitionId,
       wd.WordId AS DefinitionWordId
FROM dbo.UserWords AS uw
LEFT JOIN dbo.WordDefinitions AS wd ON wd.Id = uw.PreferredWordDefinitionId
WHERE uw.PreferredWordDefinitionId IS NOT NULL
  AND (wd.Id IS NULL OR wd.WordId <> uw.WordId);

SELECT uw.Id, uw.UserId, uw.WordId, uw.PartOfSpeechId,
       uw.PreferredWordDefinitionId, wd.PartOfSpeechId AS DefinitionPartOfSpeechId
FROM dbo.UserWords AS uw
JOIN dbo.WordDefinitions AS wd ON wd.Id = uw.PreferredWordDefinitionId
WHERE uw.PartOfSpeechId <> wd.PartOfSpeechId OR uw.PartOfSpeechId IS NULL;
```

Expect zero rows from both. The first finds dangling/cross-word preferences; the second finds disagreement between retained derived POS and the selected definition. Any row requires an explicit correction/rejection decision before migration.

### H. Null PartOfSpeechId values

```sql
SELECT COUNT_BIG(*) AS NullPartOfSpeechCount
FROM dbo.UserWords
WHERE PartOfSpeechId IS NULL;
```

Expect `0`. A positive count contradicts the current required runtime/final-migration model and blocks a straightforward migration. Also provide the rows from `SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;` so the applied chain can be compared with checked-in migrations.

## PartOfSpeechId Final Recommendation

### Can it be derived?

When `PreferredWordDefinitionId` is non-null and valid for the same word, the selected POS is fully derivable as `UserWord -> PreferredWordDefinition -> PartOfSpeech`. The FK alone guarantees that the definition exists, but the database has no composite constraint guaranteeing `PreferredWordDefinition.WordId == UserWord.WordId`; that invariant is currently application-validated only. When the preference is null, derivation is impossible, and current code uses stored `PartOfSpeechId` plus the first definition in that POS as fallback.

### Complete current usage classification

| Usage | Classification | Finding |
|---|---|---|
| Entity property/navigation and required FK (`UserWord.cs:30-42`; `ApplicationDbContext.cs:88-91`) | Legacy/required by current model | Persists the selected POS independently of the preferred definition. |
| Three-column unique index (`ApplicationDbContext.cs:98-99`) | Potentially incorrect | Directly conflicts with approved identity. |
| Add duplicate check and assignment (`WordService.cs:226-241`) | Potentially incorrect identity; currently required by flow | Allows another sense-row. |
| Preferred-ID resolution constrained by POS (`WordService.cs:577-599`) | Redundant/derived | Preference itself already identifies POS after validation. |
| Preferred-definition update conflict check and POS mutation (`WordService.cs:278-313`) | Derived plus potentially incorrect | Keeps duplicate columns synchronized but treats POS as identity. |
| Vocabulary list definition filtering/display (`WordService.cs:377-430`) | Currently required; derivable | Filters definitions and displays POS from stored FK. |
| Vocabulary search projection (`WordService.cs:467-512`) | Currently required; derivable | Same pattern as list. Search predicate itself searches all definitions. |
| Quiz definition selection (`QuizService.cs:29-47`) | Currently required; derivable | Requires preference and stored POS to agree; otherwise falls back within stored POS. |
| Quiz grouping by canonical WordId (`QuizService.cs:50-59`) | Defensive/legacy | Masks duplicate UserWords by arbitrarily taking the first row. |
| Tests and seed helpers (`VocabularyOwnershipApiTests.cs:15-37,95-117`; `IntegrationTestSeeder.cs:83-111`) | Current-contract coverage needing modification | Encode POS as persisted UserWord state and same-POS identity. |
| DTO/UI POS string (`UserVocabularyDTOs.cs:3-18`; `word-lookup.model.ts:47-62`) | Required API display, not required storage | Can be projected from preferred definition. |

`UserWord.PartOfSpeechId` is not used for quiz scoring, counter updates, history grouping, favorite operations, notes, timestamps, alphabet filtering, or ownership checks. It is used for add identity, projection/display, selected-definition fallback, quiz generation, validation/synchronization, and the FK/index.

### Remove during R5 versus retain without identity

| Concern | Option A: remove during R5 | Option B: retain during R5, remove from identity |
|---|---|---|
| Migration complexity | Must drop FK/index/column, backfill every null preference, decide rows with no valid definition, and rewrite the model/projections atomically. | Drop old identity index and add two-column unique index; existing POS column/FK remain. |
| Data-loss risk | No direct dependent FK targets POS, but an incorrect backfill can change selected meaning; a failed broad migration is harder to recover. | No row/dependent transformation is needed when duplicate audit remains empty; preference/POS mismatches can be rejected before deployment. |
| QuizResult impact | `QuizResult` FKs do not reference POS, but quiz generation must be rewritten at the same time. Existing history remains safe only if `UserWord.Id` is preserved. | Quiz generation can be changed incrementally while all `QuizResult.UserWordId` values remain untouched. |
| SampleSentence impact | No direct POS dependency, but the larger migration offers no preservation benefit. `UserWord.Id` must still remain stable. | No direct impact; existing FKs remain attached to the same row. |
| API impact | List/search/quiz projections must derive through preference; legacy null fallback disappears. | Existing DTO shape and display POS remain usable while source-of-truth semantics change. |
| Angular impact | DTO shape can remain, but backend projection changes and null cases require broader coordinated tests. | Minimal: POS remains display data and `VocabularyItem.id` remains stable. |
| Testing burden | Higher: backfill, null/no-definition rows, every rewritten projection, schema removal, and rollback. | Lower: identity, synchronization, concurrency, preservation, and compatibility projection tests. |
| Rollback complexity | High: reconstruct POS from preference before re-adding a required column/FK/index; post-change null/invalid states can block rollback. | Moderate: restore the old index only if rollback is deliberately supported; no column reconstruction. |
| Future cleanup cost | None for this column, but paid during the critical identity change. | A later migration/query rewrite is required; synchronization risk persists until then. |

Removal is not necessary to make one `(UserId, WordId)` row correct. Current code has five active dependencies on stored POS: add persistence, preferred-definition synchronization, vocabulary projection, search projection, and quiz definition selection. Retention isolates the critical identity/concurrency change and preserves a fallback for nullable preferences. The compatibility rule must be exact: whenever `PreferredWordDefinitionId` is non-null, `PartOfSpeechId` equals that definition's `PartOfSpeechId`; create and preference-update paths assign both from the validated definition in the same `SaveChanges`; callers cannot independently choose POS identity; legacy null preferences keep their existing POS until separately backfilled.

RECOMMENDATION:
RETAIN PARTOFSPEECHID DURING R5

It must stop participating in identity. `PreferredWordDefinitionId` becomes authoritative when present, while POS remains temporary derived compatibility state. Physical removal should be a later cleanup after null-preference and projection behavior is eliminated and tested.

## 8. Quiz and Dependent-Data Impact

`QuizResult.UserWordId` is a required direct FK and delete of a `UserWord` cascades quiz history (`QuizResult.cs:9-29`; `ApplicationDbContext.cs:119-139`). `SampleSentence.UserWordId` is likewise required and cascades (`SampleSentence.cs:9-23`; `ApplicationDbContext.cs:102-117`). A naïve duplicate cleanup that deletes a losing `UserWord` would therefore delete both its quiz results and sample sentences before any aggregate merge is visible.

Quiz generation depends on `UserWord.PartOfSpeechId`: it selects the preferred definition only when both IDs agree, otherwise it uses the first definition in stored POS (`QuizService.cs:29-47`). It groups duplicate saved rows by `WordId` and picks `group.First()` (`QuizService.cs:50-59`), which suppresses duplicate quiz questions but makes the chosen `UserWordId`, definition, and learning state order-dependent.

Quiz scoring does not inspect POS. Session state stores the selected `UserWordId`; submit reloads that exact owned row, writes `QuizResult.UserWordId`, and increments `TotalAttempts`, `CorrectAnswers`, `LastReviewedAt`, and `LastCorrectAt` transactionally (`QuizService.cs:205-219,257-329`). R4's counter invariant therefore attaches to the surviving stable `UserWord.Id`.

Changing a preferred definition in place is safe for R4: no FK or counter changes. Replacing or merging IDs is hazardous:

- reparent all `QuizResults` and `SampleSentences` before deletion or cascade will lose them;
- aggregate counters require a proven policy and reconciliation against history, including R4's definition of attempted questions;
- `LastReviewedAt`/`LastCorrectAt` require deterministic max/non-null handling;
- `IsFavorite`, `PersonalNotes`, and `AddedAt` require conflict rules;
- reparenting `QuizResult` can collide with unique `(UserId, QuizSessionId, UserWordId)` if both duplicate rows participated in one session (`ApplicationDbContext.cs:138-139`);
- current in-memory quiz sessions hold `UserWordId`; deleting/replacing a row during deployment makes submission fail as “vocabulary no longer available” (`QuizService.cs:205-220`). A deployment should drain/restart instances or accept expiry of active sessions.

Given the verified zero-duplicate result, none of those destructive merge operations should run. Preserve all existing `UserWord.Id` values and dependent rows; install the new unique index after a defensive precondition.

There is no implemented create/update API for `SampleSentence` in the current repository, but its model/FK is active. This is an incomplete product surface relevant to preservation, not grounds to ignore its data.

## 9. API and Angular Impact

### Backend contracts and endpoints

- `AddWordRequest` exposes word, definition/example/POS/pronunciation, and optional preferred ID (`VocabularyApp.WebApi/Models/AddWordRequest.cs:3-11`). Only word, POS, and preferred ID affect `UserWord`; caller definition/example/pronunciation are ignored by save.
- `WordsController.AddToVocabulary` and `IWordService.AddToVocabularyAsync` are the only personal-vocabulary add path (`WordsController.cs:71-103`; `IWordService.cs:9`).
- `UpdatePreferredDefinitionRequestDto` and the preferred-definition PUT already address a `UserWord.Id`, which suits the target model (`UserVocabularyDTOs.cs:26-29`; `WordsController.cs:206-240`).
- `UserVocabularyItemDto` returns one row per `UserWord`, the derived display definition, preferred ID, POS string, favorite/notes/counters/timestamp (`UserVocabularyDTOs.cs:3-18`). Its shape need not change if POS continues as a display projection.
- `WordLookupResponse.IsInUserVocabulary` already means any row with `(UserId, WordId)`, matching target semantics (`WordDTOs.cs:28-35`; `WordService.cs:46-51`).
- `UserWordDto`, `AddWordToCollectionRequest`, and `UpdateUserWordRequest` appear unused by controllers/services. They retain stale `CustomDefinition`/`DifficultyLevel` contract ideas and require audit if activated, but broad cleanup is outside R5 (`UserWordDTOs.cs:5-58`).

Recommended add semantics: identify by canonical `(UserId, WordId)`. If absent, create once. If present, return deterministic idempotent success with the existing `UserWord.Id`; do not silently overwrite preference or user state merely because add was repeated. If product wants repeat-add to select the newly supplied definition, that should be explicit, validated update semantics—not insertion—and is a product decision. Returning both `userWordId` and canonical `wordId`, plus an `alreadyExisted` indicator, would remove current ambiguity.

### Angular assumptions

- `VocabularyItem.id` is treated as stable `UserWord.Id`; favorite and preference PUTs use it (`word-lookup.model.ts:47-62`; `word-lookup.component.ts:574-627`). This already matches target identity.
- `VocabularyItem.partOfSpeech` is required for display/search suggestions but is not used as the entry key (`word-lookup.model.ts:47-62`; `word-lookup.component.ts:85-100`). It can remain a required response string derived from the preference.
- Lookup groups all definitions by POS for dictionary display (`word-lookup.component.ts:216-245`). Multiple parts of speech are definitions of one canonical word in that UI.
- The Add button is word-level, disabled by `IsInUserVocabulary`; it sends the first definition of the first sorted group (`word-lookup.component.ts:199-213,328-365`; template lines 139-145). The UI does not expose “save each POS” behavior.
- The preference editor offers all definition/POS options and updates one item in place (`word-lookup.component.ts:497-617`). That matches the approved rule.
- List rendering is one item per returned row and would visibly duplicate a word if backend rows existed (`word-lookup.component.html:329-347`). Alphabet counts and total count would also count duplicate rows (`word-lookup.component.ts:642-657`).

Frontend changes should therefore be small: consume deterministic add metadata/messages, continue treating POS as display data, and test cross-POS preferred-definition selection. No frontend redesign or multi-sense entry key is needed.

## 10. Original R5 Assumption Audit

| Original R5 assumption/task | Current finding | Status | Evidence |
|---|---|---|---|
| 1. Confirm one saved word / one selected meaning | Product rule is approved; UI preference editor already mutates one entry. Backend identity still disagrees. | Confirmed | Brief; `word-lookup.component.ts:574-617`; `WordService.cs:228-241` |
| 2. Find duplicate `(UserId, WordId)` records | Supplied live query found zero. Recheck at deployment boundary. | Already resolved for inspected data | User-provided query; section 6 |
| 3. Define deterministic merge policy | No known rows need merging. A full product merge policy is unnecessary unless a defensive check fails. | Partially required | Required only as an exception/runbook path; dependents in `ApplicationDbContext.cs:102-139` |
| 4. Clean existing duplicate data | No evidence supports cleanup; silent cleanup would risk cascades/data loss. | Not applicable currently | Zero-row finding; cascade FKs |
| 5. Add uniqueness on `(UserId, WordId)` | Neither runtime model nor snapshot has it. | Still required | `ApplicationDbContext.cs:98-99`; snapshot lines 313-320 |
| 6. Decide whether to remove `PartOfSpeechId` | Retain during R5 as synchronized compatibility state; remove it from identity. Later physical removal is optional cleanup. | Confirmed | PartOfSpeechId Final Recommendation |
| 7. Change add behavior so another sense cannot create another row | Current add checks three columns and can insert another POS row. | Still required | `WordService.cs:226-252` |
| 8. Simplify preferred-definition updates | Current update mutates POS identity and performs a conflict check. | Still required | `WordService.cs:278-314` |
| 9. Update quiz and vocabulary projections | Both filter definitions through stored POS; quiz groups duplicates arbitrarily. | Still required | `WordService.cs:407-430,489-512`; `QuizService.cs:29-59` |
| 10. Update Angular assumptions | Core UI already uses one stable item, but contracts/add messaging and cross-POS tests need alignment. | Partially required | `word-lookup.component.ts:328-365,574-617` |
| 11. Validate migration against production-like data | Final executable chain now aligns with runtime model, but applied deployment state and dependent preservation still require validation. | Still required | Section 5; test setup uses `EnsureCreated` |

Steps 3 and 4 should be reduced to a defensive precondition and documented exception path, not an automatic merge, as long as the duplicate query remains zero immediately before index creation.

## Duplicate-Merge Decision

Choose approach **B: a defensive migration precondition that refuses to continue if duplicates are unexpectedly found**. The inspected database returned zero duplicate `(UserId, WordId)` groups, so full merge code would introduce unneeded and dangerous policies for notes, favorite state, counters, timestamps, quiz-result uniqueness, and sample-sentence ownership. The precondition and unique-index creation must occur in the same migration/transactional deployment boundary where supported, eliminating a check-to-index race. If the precondition fails, stop without mutation, inventory the rows and dependents, and create a separately approved exception plan. Do not silently select a winner.

## R5 Identity Invariants

| Invariant | Current status | Evidence/current gap |
|---|---|---|
| 1. At most one `UserWord` per user and `WordId` | Not guaranteed | Add and unique index include POS (`WordService.cs:228-241`; `ApplicationDbContext.cs:98-99`). |
| 2. Different users may save the same `WordId` | Already guaranteed | User is part of existing uniqueness and all add checks; no global WordId uniqueness. |
| 3. Saving the same `WordId` again creates no row | Partially guaranteed | Sequential same-POS add is idempotent; another POS can insert (`WordService.cs:228-252`). |
| 4. Non-null preference belongs to `UserWord.WordId` | Partially guaranteed | Add/update validate it (`WordService.cs:278-292,577-599`), but FK alone does not enforce cross-column membership. |
| 5. Preference change updates the existing row | Already guaranteed | Endpoint loads by stable `UserWord.Id` and mutates it (`WordService.cs:270-314`). It may currently reject a cross-POS conflict. |
| 6. Preference change preserves PK, user state, timestamps, quiz/sample relationships, and history | Already guaranteed for successful current update | Only POS and preference are assigned; the row is not replaced (`WordService.cs:294-314`). Explicit regression coverage is incomplete. |
| 7. Concurrent duplicate adds cannot persist duplicates | Partially guaranteed only for same POS in a correctly migrated DB | Current three-column unique index protects same POS; different POS remains allowed, and API race outcome is generic failure. |
| 8. Database enforces unique `(UserId, WordId)` | Not guaranteed | No such runtime/snapshot/executable-chain constraint. |
| 9. Retained POS does not participate in identity | Not guaranteed | It is part of service check and unique index. |
| 10. Retained POS stays synchronized with preference | Partially guaranteed | Current add validates both and update assigns POS from definition (`WordService.cs:236-241,278-313`); direct writes/legacy mismatches and null preferences remain possible. |

The eventual synchronization rule is: for every non-null `PreferredWordDefinitionId`, load/validate its `WordId` equals `UserWord.WordId`, then assign both `PreferredWordDefinitionId` and `PartOfSpeechId = WordDefinition.PartOfSpeechId` within the same tracked row and `SaveChanges`. No endpoint may independently use POS to select identity. A null preference may temporarily retain its existing POS solely for legacy projection fallback.

## 11. Recommended Target State

### Recommended smallest safe design

1. Keep `UserWord.Id` as the stable surrogate PK and define the business identity with a unique `(UserId, WordId)` index.
2. Keep one `UserWord` row and all existing user/dependent state attached to that ID.
3. Make `PreferredWordDefinitionId` the authoritative selected meaning. Validate that it references a definition for the same `WordId` on add and update.
4. For R5, retain required `PartOfSpeechId` as a synchronized derived compatibility column: set it from the preferred definition, never use it for identity, and avoid independent caller authority. This minimizes query and migration risk.
5. Make add check by `(UserId, WordId)`. A repeated save returns the existing entry deterministically without resetting favorite, notes, counters, dates, or preference. Catch the database unique violation caused by racing requests and reload/return the winner.
6. Preferred-definition PUT updates the existing row only. Cross-POS selection is allowed; update the compatibility POS from the selected definition and remove the old conflicting-entry logic because the new invariant ensures only one row exists.
7. Vocabulary and quiz projections should select the explicit preferred definition first. During compatibility, fallback can use stored POS only for legacy null preferences; after backfill/non-null enforcement, derive POS directly from the selected definition.
8. Keep DTO/UI `partOfSpeech` as display data and `VocabularyItem.id` as `UserWord.Id`. Return unambiguous add metadata.
9. Do not delete, recreate, or change IDs of existing `UserWord` rows. No `QuizResult`/`SampleSentence` reparenting is needed when the defensive duplicate check passes.

### Alternative: remove PartOfSpeechId in R5

This is the cleaner normalized endpoint: backfill every preference, make it required, remove the POS FK/index/column/navigation, and rewrite list/search/quiz projections to join through the preferred definition. It removes synchronization risk permanently. However, it requires verified handling of null preferences and invalid/mismatched rows, changes more queries and tests, and complicates rollback. It is not necessary for R5 correctness and is not the recommended R5 scope.

### Integrity limitation

A simple FK on `PreferredWordDefinitionId` does not enforce “definition belongs to the same WordId.” Application validation is required. A database-level composite FK would require an alternate unique key on `WordDefinitions(Id, WordId)` and a composite FK from `UserWords(PreferredWordDefinitionId, WordId)`, which is stronger but adds migration/index complexity. It is a valid hardening option, not required for the smallest R5 if service validation is comprehensively tested.

## 12. Migration Assessment

R5 requires a migration because database-enforced uniqueness must change.

### Known required changes

1. Use a fail-fast duplicate `(UserId, WordId)` precondition; do not auto-delete or merge.
2. Drop unique `IX_UserWords_UserId_WordId_PartOfSpeechId` and create unique `IX_UserWords_UserId_WordId`.
3. Retain `PartOfSpeechId`, its supporting index and Restrict FK, but remove it from business identity.
4. Preserve every row, `UserWord.Id`, FK, state column, and dependent record.
5. Update the EF model and snapshot to the two-column unique index.

### Changes dependent on deployed-schema verification (resolved)

- Verification confirms the expected old identity index is present, POS is required, relevant FKs exist, and relevant migrations are applied; the normal migration path can be planned against that state.
- Zero duplicates and zero consistency mismatches mean no data repair or merge belongs in the normal R5 migration.
- Null `PreferredWordDefinitionId` remains schema-valid and does not block the retained-POS design; forced preference backfill is not required to establish two-column identity.
- The same checks must be rerun immediately before deployment. Any newly found null POS, cross-word preference, POS/preference mismatch, or duplicate blocks migration rather than activating implicit cleanup.

### Changes that should not be part of R5

- Full duplicate merge machinery while audits show zero duplicates.
- Physical removal of `PartOfSpeechId` or forced non-null preference.
- Reparenting/deleting `QuizResult` or `SampleSentence` records.
- Unrelated DTO/model cleanup, counter reconciliation, or architecture changes.

There is no verified need for data transformation to merge rows. Optional preference backfill is a data correction, not duplicate cleanup. A safe deterministic backfill is: preserve every valid existing preference; where null, select the lowest `DisplayOrder` (then lowest `Id`) definition matching the row's `WordId` and stored `PartOfSpeechId`. Rows with no matching definition must abort/report rather than receive an unrelated definition.

If duplicates unexpectedly appear between audit and migration, index creation must fail. The operator can then collect state/dependent counts and approve a separate merge plan. A defensive precondition is safer than embedding a generic merge whose notes/counters/history rules have not been approved.

`Down` can drop the two-column index and recreate the old three-column index, but rollback reopens the product defect and may fail if post-R5 data contains a null POS (if allowed). If R5 makes the preference non-null or removes POS, rollback would also require reconstructing POS from the preferred definition before recreating its FK/index. Rollback must never delete `UserWord`, `QuizResult`, or `SampleSentence` rows.

Production-like migration testing must use SQL Server migrations, not only SQLite `EnsureCreated`; the current integration infrastructure (`VocabularyAppWebApplicationFactory.cs:76-103`; `RelationalDatabaseFixture.cs:13-24`) bypasses migration history and cannot verify the executable upgrade path or applied deployment state.

## 13. Testing Gap Analysis

### Existing tests

- canonical-backed save persists expected User/Word/POS/preferred relationships (`VocabularyOwnershipApiTests.cs:14-37`);
- missing canonical word cannot be created through save (`VocabularyOwnershipApiTests.cs:39-70`);
- vocabulary ownership isolation (`VocabularyOwnershipApiTests.cs:72-93,248-277`);
- same user/same word/same POS sequential duplicate add is idempotent (`VocabularyOwnershipApiTests.cs:95-117`);
- different users can own the same canonical word indirectly through favorite/preference tests (`VocabularyOwnershipApiTests.cs:119-185`);
- preferred-definition ownership isolation and same-POS change (`VocabularyOwnershipApiTests.cs:146-185`);
- definition from another word, missing ID, and invalid ID are rejected without mutation (`VocabularyOwnershipApiTests.cs:187-246`);
- extensive R4 quiz tests verify `UserWordId` persistence, per-row counters/timestamps, atomic failure, retry, duplicate submission, ownership, and deleted-session-row behavior (`QuizApiTests.cs`, notably lines 84-211, 353-378, 431-578, 777-888).

### Tests needing modification

- Rename/rewrite `DuplicateCurrentLogicalSaveIsIdempotentForSamePartOfSpeech` to assert the canonical two-column identity and response metadata, independent of submitted POS (`VocabularyOwnershipApiTests.cs:95-117`).
- The persisted-relationship save test should assert preference-to-word validity and, if POS is retained, synchronization rather than treating POS as identity (`VocabularyOwnershipApiTests.cs:14-37`).
- Seed helpers should support a canonical word with multiple POS definitions without directly encoding a second `UserWord` identity (`IntegrationTestSeeder.cs:31-112`).
- Projection and quiz expectations should derive the selected definition/POS from preference and cover cross-POS selection.
- Angular specs for add/preference behavior need updated response/message and cross-POS assertions; current component spec has no visible R5-specific contract coverage.

### Tests to add before/with implementation

- same user cannot persist the same `WordId` twice when requests use different POS values;
- different users can save the same `WordId` once each (direct assertion of the new index scope);
- repeat add is deterministic, returns the existing `UserWord.Id`, and does not change preference or state unless explicitly specified;
- two concurrent duplicate requests result in one row and deterministic successful/idempotent API outcomes;
- preferred definition must belong to the same canonical word;
- preferred definition can switch to a different POS without a new row;
- cross-POS change preserves `Id`, `IsFavorite`, `PersonalNotes`, `AddedAt`, counters, `LastReviewedAt`, and `LastCorrectAt`;
- existing `QuizResult` rows and their FKs/counts remain intact after preference change;
- existing `SampleSentence` rows and their FKs/counts remain intact after preference change;
- quiz generation uses the newly preferred cross-POS definition and R4 scoring continues updating the same row;
- legacy null preference and invalid/mismatched data behavior (fallback or migration rejection) is explicit;
- migration from the real prior SQL Server schema succeeds with zero duplicates, creates exactly the desired index, and preserves all row/dependent counts and IDs;
- migration aborts safely with injected duplicate groups and leaves data unchanged;
- migration handles/aborts null POS, null preference, invalid preference-word, and POS/preference mismatches as designed;
- rollback on a representative post-R5 database is documented/tested, with no dependent deletion;
- model-vs-migration schema comparison catches future drift and confirms the final executable chain.

No current test covers different-POS duplicate add, add concurrency, cross-POS preference preservation, `SampleSentence` preservation, or migration execution. SQLite `EnsureCreated` tests validate the current model but not checked-in SQL Server migrations.

## 14. Risks and Edge Cases

- **Deployed drift/applied-state uncertainty:** the final repository chain aligns with runtime/snapshot, but production may have missing migrations or manual changes. Incorrect index names/nullability assumptions can break deployment.
- **Race conditions:** application pre-check alone cannot enforce identity. Unique-index exception handling must distinguish the target index and reload the winner.
- **Case-sensitive canonical lookup:** add uses exact `Word.Text == request.Word` while canonical uniqueness/collation may be case-insensitive (`WordService.cs:218-220`). This is adjacent but can affect deterministic repeat requests; avoid broad normalization refactoring unless a failing R5 test establishes need.
- **Unknown/missing POS becomes Noun:** current fallback can select the wrong meaning (`WordService.cs:359-371`). Once preference is authoritative, a valid preferred ID should drive POS; invalid payload should not silently change identity.
- **Null/invalid preferences:** nullable schema requires fallback. Removal of POS without a complete backfill would make some rows unprojectable/unquizzable.
- **Cross-column mismatch:** the FK does not ensure preferred definition belongs to `UserWord.WordId`; direct SQL or old code can create mismatches.
- **Arbitrary duplicate masking:** quiz groups by `WordId` and takes the first row, hiding split counters/history (`QuizService.cs:50-59`).
- **Cascade data loss:** deleting a duplicate row deletes its quiz and sample data.
- **Quiz uniqueness collision during merge:** reparenting two rows can conflict on `(UserId, QuizSessionId, UserWordId)`.
- **Active in-memory sessions:** replacing a `UserWord.Id` invalidates outstanding sessions.
- **Ambiguous add response:** `wordId` currently means canonical ID, not saved-entry ID.
- **Stale DTOs/comments:** unused DTOs and comments claim removed/retained fields inconsistently. Limit cleanup to contracts touched by R5.
- **No concurrency token on UserWord:** simultaneous preference/favorite/quiz updates can overwrite only modified EF properties in normal cases, but there is no optimistic row-version protection. Introducing one is outside the minimum R5 scope.

## 15. Recommended R5 Scope

### Required for R5

- Confirm live indexes, nullability, FKs, migration history, and repeat the zero-duplicate/data-consistency audits.
- Add unique database enforcement on `(UserId, WordId)` and remove old POS-based identity enforcement where present.
- Change add existence logic to two-column identity and handle unique races idempotently.
- Preserve the existing `UserWord.Id` and every user/dependent field.
- Validate preference belongs to the same canonical word.
- Allow cross-POS preference updates on the existing row without conflict-row logic.
- If retaining POS, synchronize it from preference and stop treating request POS as independent identity.
- Align vocabulary/quiz projections so selected definition/POS are consistent; remove arbitrary duplicate grouping once invariant is enforced or retain only as transitional defense.
- Update backend and Angular contracts/messages only where needed for deterministic one-word behavior.
- Add the R5 integration, concurrency, preservation, and real-migration tests from section 13.
- Confirm deployed schema/applied migration history before finalizing exact DDL.

### Optional follow-up

- Backfill and make `PreferredWordDefinitionId` non-null for all saveable rows.
- Remove `UserWord.PartOfSpeechId`, its navigation/FK/index, and derive POS exclusively through the preference.
- Add database-level composite integrity for preferred-definition/word membership.
- Retire stale unused `UserWordDTOs` fields/comments.
- Add a `UserWord.QuizResults` inverse navigation for discoverability.

### Explicitly out of scope

- Automatic duplicate merging without evidence and approved field/dependent conflict rules.
- Redesigning canonical word lookup/import.
- Repository pattern, CQRS, microservices, or broad service refactoring.
- R4 counter/history redesign or reconciliation unrelated to an actual duplicate exception.
- Sample-sentence feature implementation.
- General DTO, naming, formatting, or legacy-comment cleanup.
- Database execution or migration generation during this analysis.

## R5 Implementation-Planning Gate

READY FOR IMPLEMENTATION PLANNING

Both blockers are resolved:

1. The deployed database matches the expected final migration/runtime model: required POS, unique `(UserId, WordId, PartOfSpeechId)`, expected FKs, and relevant migrations applied.
2. `PartOfSpeechId` will be retained during R5 as synchronized derived compatibility state but removed from identity.

The clean deployed data supports a narrowly scoped plan: replace three-column uniqueness with unique `(UserId, WordId)`, change add/concurrency behavior to that identity, update preference behavior in place, preserve all row IDs and dependents, and add a fail-fast duplicate precondition. Full merge logic is unnecessary. The verification queries must be rerun at implementation/deployment time; a newly non-empty duplicate or consistency result would stop deployment and reopen only the affected data exception.
