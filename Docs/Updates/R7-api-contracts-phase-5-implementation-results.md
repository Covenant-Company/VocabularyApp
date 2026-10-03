# R7 Phase 5 — Typed Vocabulary Contracts and Bounded Client Alignment

Date: 2026-10-02

Status: **IMPLEMENTED — EXECUTION VERIFICATION PENDING**

This records Phase 5 under approved D1–D4 in the [implementation plan](R7-api-contracts-remediation-implementation-plan.md), the [analysis](R7-api-contracts-remediation-analysis.md), and the supplied interrupted-task continuation. The [Phase 1](R7-api-contracts-phase-1-implementation-results.md), [Phase 2](R7-api-contracts-phase-2-implementation-results.md), [Phase 3](R7-api-contracts-phase-3-implementation-results.md), and [Phase 4](R7-api-contracts-phase-4-implementation-results.md) records remain historical checkpoints. No overall R7 completion, test pass, compilation, or runtime verification is claimed. Phase 6 has not begun.

## 1. Objective

Complete named vocabulary results and the existing Angular transport alignment while preserving successful JSON fields, R5 identity, legacy add selection, paging, safe errors, and the Phase 4 provider boundary. Address Phase 5 portions of F06/F07/F08/F09/F10/F11/F12/F13 through B01/B06/U04.

## 2. Scope implemented and recovered checkpoint

The continuation inspected repository status, the cumulative tracked diff, untracked vocabulary contract files, authoritative documents, and relevant consumers before repository edits. The checkpoint was accurate: named backend results, favorite JSON presence, and complete empty-search responses were already implemented. Angular page wording and initial Phase 5 tests also existed. Valid implementation was preserved rather than replaced.

Completed production work at resumption: typed service/controller results; favorite presence; full typed search; compatible legacy add comments; transport DTOs/generics/null mapping; acknowledgement guards; page-local labels; load errors/stale data; favorite rollback; editor preservation. Partially completed tests: favorite invalid-input and search matrix, UI new-add/boolean/empty/error/page-navigation cases, and typed search transport. Missing work: paging/default/empty-list and >1000-row/search-cap coverage, add text boundaries, stronger component header/editor assertions, failed removal and malformed-refresh cases, and this document. Static signature inspection found the test service substitute already aligned with IWordService. No unfinished syntax or scope expansion was identified.

This continuation edits only `VocabularyContractTests.cs`, `word-lookup.component.spec.ts`, and this new record. Earlier production changes and Phase 1–4 tests remain intact. The separate historical original Phase 5 message is not available in this resumed context; requirements were reconciled against the supplied comprehensive continuation and approved plan's Phase 5/B01/B06/U04 scope. No requirement from those available authorities remains unaccounted for.

## 3. Backend production-file inventory

Paths relative to `VocabularyApp.WebApi/`; includes recovered Phase 5 work.

| File | Phase 5 contribution |
|---|---|
| `DTOs/SuccessResponse.cs` (new) | Named `SuccessResponse<T>` retaining exactly `success,data`, with required data initialization. |
| `DTOs/UserVocabularyDTOs.cs` | JsonRequired favorite presence; named favorite/preferred acknowledgements; existing add/list/item fields retained. |
| `Models/AddWordRequest.cs` | Documents accepted ignored legacy fields and selection-only POS fallback; no field removal or new prevalidation. |
| `Services/IWordService.cs` | Named lookup/add/favorite/preferred result signatures and nullable search term. |
| `Services/WordService.cs` | Matching typed results, named acknowledgement construction and existing-entry helper; full empty-search result retained. |
| `Controllers/WordsController.cs` | Named successful wrappers for all six word actions; nullable optional search routed through the service; required success data checked. |

Six production files. Shared Phase 3 errors and Phase 4 provider validation are reused without new changes to their behavior. No unrelated success envelope was changed.

## 4. Angular production-file inventory

Paths relative to `VocabularyApp.UI/src/app/`; distinguishes recovered Phase 5 edits from Phase 2 groundwork reused in this phase.

| File | Contribution |
|---|---|
| `models/word-api.model.ts` | Backend-shaped transport DTOs and typed acknowledgements introduced in Phase 2; Phase 5 extends search path construction to represent an absent term. |
| `models/word-lookup.model.ts` | Reexports the authoritative add acknowledgement instead of repeating its declaration; presentation types remain separate. |
| `components/word-lookup/word-lookup.component.ts` | Typed vocabulary calls and guards/null mapping from Phase 2 retained; letter tooltips now explicitly say “on this page.” |
| `components/word-lookup/word-lookup.component.html` | Page-local search/letter/count/no-match wording; existing failure/stale-data display and pagination retained. |
| `services/api.service.ts` (reused Phase 2 groundwork) | Typed response and POST/PUT request generics, defaulting to unknown, already satisfy vocabulary transport typing; no additional Phase 5 service redesign. |

Four files have Phase 5 contributions; the fifth is an explicitly reused production dependency. `services/api-error.ts` and the compatible `ApiResponse<T>` are likewise reused unchanged. No active vocabulary transport uses `any`; old speech-test casts and intentionally optional presentation fields are not transport declarations.

## 5. Test-file inventory and static counts

| File | Phase 5 change/coverage |
|---|---|
| `VocabularyApp.WebApi.Tests/Integration/VocabularyContractTests.cs` (new) | Favorite omission/null/string/number/object/array validation with no mutation; absent/empty/whitespace/normal owned search; paging defaults/bounds/second page; successful empty list; 1001 owned rows across two pages plus private row/search cap; missing/null/empty/whitespace/padded add text. |
| `VocabularyApp.WebApi.Tests/Integration/ApiErrorBoundaryTests.cs` (existing untracked Phase 3 suite) | Recovered IWordService substitute signatures aligned with named types and nullable search. Existing thrown/missing-success-data cases remain. No test methods removed. |
| `VocabularyApp.UI/src/app/services/api.service.spec.ts` | Four typed absent/empty/whitespace/encoded search transport cases and one typed search error-channel case; prior typed add/favorite and complete fixture transport tests retained. |
| `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts` | Page-local tooltip expectations; new add, both favorite booleans, genuine empty versus initial failure, 1001-total two-page navigation; additional failed removal/malformed refresh cases; Bearer/method/body checks; editor options/selection and stale filter assertions strengthened. |

Static authored Phase 5 count: **25 backend cases across 6 methods** (2 Facts plus 23 InlineData rows across 4 Theories); **13 new Angular cases** (8 component cases, 5 ApiService cases). Total **38 Phase 5 cases**, including recovered interrupted edits. This excludes pre-existing cases merely strengthened or adapted, and is not a discovery or execution count. Normal search also covers an encoded padded term and a complete zero-match response.

Existing coverage is reused rather than duplicated: `ApiContractShapeTests` pins add/new/duplicate IDs, true/false acknowledgements, full normal list/search items, and same-row preferred update; `VocabularyOwnershipApiTests` protects concurrent duplicate recovery, dependents/counters, ownership/missing targets and invalid definitions; `ApiSafeErrorTests` protects real query/save failures and no mutation; `ApiErrorBoundaryTests` protects required top-level data. Phase 2 UI cases already cover normalized errors, malformed acknowledgements, nullable mapping and editor/rollback behavior. Those suites and their assertions remain intact.

## 6. Typed vocabulary acknowledgement contracts

All successful vocabulary actions use `SuccessResponse<T>`: exactly `{success:true,data:...}`. Required service data is checked before returning 200; absent data yields Phase 3 safe `500 internal_error`, without anonymous fallback payloads.

| Operation/data type | Preserved data fields |
|---|---|
| Add / `AddToVocabularyResultDto` | `userWordId,wordId,alreadyExisted,message` |
| Favorite / `FavoriteUpdateResponseDto` | `message,userWordId,isFavorite` |
| Preferred / `PreferredDefinitionUpdateResponseDto` | `message,userWordId,preferredWordDefinitionId` |
| List/search / `UserVocabularyResponseDto` | `words,totalCount,page,pageSize,totalPages` |

Lookup also uses the named wrapper and `WordLookupResponse`; its nested flags and word/null fields remain unchanged. Angular requires complete mutation acknowledgement fields before confirming success; mismatched IDs/booleans/preference or missing message/payload cannot produce success feedback.

## 7. Add-word behavior

Canonical text lookup remains exact and untrimmed; empty/missing/null/whitespace words fail safely, and padded text does not silently match the unpadded canonical word. Lookup's existing trim behavior is unchanged. Canonical words must already exist; add does not call the provider or create canonical content.

Existing `(UserId,WordId)` rows return 200 with the same UserWordId and `alreadyExisted:true` before validating a newly supplied preference. New rows prefer a valid explicit definition ID over submitted POS. Otherwise case-insensitive POS name/abbreviation matching and the Noun fallback remain, including the legacy null-preference case when no matching definition exists. No new automatic normalization or fallback was introduced.

## 8. Favorite JSON presence and privacy

`[JsonRequired]` on the non-nullable request bool distinguishes absence from its CLR default. `{}` returns automatic validation **400**, `application/problem+json`, code `validation_failed`, sanitized `errors`, compatible safe aliases, null data and trace ID. Null and malformed scalar/collection types likewise fail validation before persistence. Explicit `false` and `true` remain valid and update the boolean field; the database field remains non-nullable.

Nonexistent and nonowned saved-row IDs both remain concealed **404 `vocabulary_not_found`**, with identical safe text, revealing no ownership/existence detail. Unexpected query/save failure remains **500 `internal_error`** through Phase 3 infrastructure.

## 9. Preferred-definition behavior

Preferred definition is changed on the existing owner-scoped UserWord. The selected canonical definition must belong to that row's WordId; invalid/nonpositive/cross-word selections remain 400. Missing/nonowned saved rows remain concealed 404. Successful data preserves message, saved-row ID and preferred-definition ID. POS is synchronized from the selection; counters, notes, timestamps and dependents remain protected by existing R5 tests.

## 10. Absent, empty, whitespace and normal search

The optional nullable controller term is passed to the typed service. Absent, `term=`, and whitespace-only values return **200** with `words:[],totalCount:0,page:1,pageSize:5,totalPages:0`. No abbreviated anonymous branch or required-term 400 remains. Normal search uses the same complete typed structure and owner-scoped results.

Existing trim/case matching, relevance ordering, and five-result cap remain. Search `totalCount` is the number of returned matches (at most five), not a collection-wide match total. List `totalCount` remains the server's total owned filtered rows. List defaults remain page 1/size 20; page below 1 normalizes to 1; size outside 1–10000 normalizes to 20. These semantics are explicitly protected by authored tests.

## 11. Vocabulary transport alignment

Wire DTOs retain required numeric persisted IDs/counters/metadata and booleans, required date strings, and explicit nullable preferred ID/example/pronunciation/audio/notes/accuracy. Add optional fields accept null consistently with the backend; legacy fields remain nonauthoritative. No Date object, invented synonym/antonym field, optional wire field replacing an explicit null, or fabricated success payload is assumed.

Presentation models map null to undefined at the component boundary; zero counters/accuracy remain values. Generic ApiService returns the compatible envelope with typed data and keeps HTTP failures on the error channel. No dependency, generated client, runtime schema library, new transport layer or whole-vocabulary endpoint was introduced.

## 12. Page-local UI semantics

Search input says “Search words on this page.” Scope notice says search and letter counts apply to the current page and reports loaded rows. Letter browse/tooltips and no-search-match wording explicitly state page scope. Empty-letter guidance suggests changing pages. The server-supplied total remains accurately labeled “words total,” distinct from loaded-page counts.

Existing Next/Previous controls and page numbering remain. Authored Angular fixture has 1000 rows on page 1 and a later matching word on page 2, total 1001; local availability/count/search update only after navigation. Backend authored fixture separately protects distinct pages, ownership, totals and capped search. Collection-wide UI search/count APIs remain deferred.

## 13. Load failure versus genuine empty

A complete successful zero-total response shows the ordinary “No words in your vocabulary yet” state. HTTP failure or incomplete success payload sets a normalized safe load error and does not synthesize an empty result. Before any successful response, no zero-total claim is displayed. Empty UI requires a successful response, zero total, no load error and no active loading.

## 14. Stale-data behavior

Failed refresh retains the last valid response object, selected letter and local query; marks refresh required and displays the safe error plus “Showing the previously loaded vocabulary.” Failed navigation retains the previously loaded page number/data. Malformed refresh payloads also retain data. Existing view toggling/paging supports deliberate reload; no automatic retry was added.

## 15. Favorite rollback

Optimistic favorite updates are preserved. HTTP failure or malformed/mismatched acknowledgement restores the previous boolean and shows a normalized safe error, without success feedback. Tests cover adding and removing favorites, both explicit successful boolean payloads, and no automatic retry after failure.

## 16. Preferred-editor failure behavior

Failed save clears only the saving flag and shows a normalized safe error. Editor visibility, row reference, options and selected ID remain available; saved preference remains unchanged. Malformed success acknowledgement also preserves editor state. Successful save updates the same presentation row and closes the editor only after a matching typed acknowledgement. No add operation/new UserWord is involved.

## 17. R5 identity preservation

Identity remains **`(UserId,WordId)`**, with one saved row per pair. Existing-row lookup, duplicate concurrency winner recovery and two-column unique-index safeguards remain intact. PartOfSpeechId is synchronized derived state, never a third identity key. No index, model, entity, migration or snapshot was changed. Existing same-row/counter/dependent/concurrency assertions are retained.

## 18. Legacy add fields retained

Definition, example and pronunciation fields remain accepted and ignored for canonical persistence. POS remains selection fallback input when an explicit preferred ID is absent. They were documented, not removed or made authoritative. No canonical dictionary overwrite is permitted. Cleanup/removal remains deferred.

## 19. Existing expectations intentionally changed

Favorite omission changes from an accidental default-false mutation to validation 400, while explicit false remains valid. Absent/empty/whitespace search changes from binding-dependent 400 or shortened success to full typed empty 200. Tooltip text and local filtering test wording now say “on this page.” No successful mutation fields, routes, normal paging/search behavior or business identity changed.

Phase 3's prior intentional missing/nonowned 400→404 and internal-failure 400→500 expectations are retained; they were already implemented and were not changed again. Prior Phase 1–4 test coverage was not weakened.

## 20. Phase 6+ work deferred

Quiz selectedOptionId presence, null answer elements, session statuses/expiry/repeat-submit and full quiz client contracts remain Phase 6. Existing Phase 2 quiz groundwork and Phase 3 safe errors remain, without advancing those boundaries. Phase 7 OpenAPI/XML/reference cleanup, dormant DTO deletion and legacy-field removal were not performed. Phase 8 tests/builds/execution remain pending. Broader collection-wide filtering/counts, provider extraction/cache concurrency redesign, new normalization/POS policies and durable quiz sessions remain separate work.

## 21. Blockers and discrepancies

No static implementation blocker was identified. Historical original-message availability is limited as recorded in section 2; the supplied continuation and approved plan provide the actionable scope. Source inspections found no unmatched service/stub signatures, active word transport `any`, missing required controller data guards, or new Phase 6 behavior. Static review cannot establish compilation, actual MVC serializer/binding behavior, Angular template compilation, EF fixture insertion, relational/runtime results, SQL Server specifics or deployed IIS behavior.

## 22. Test execution status

**NO TESTS WERE RUN.** No discovery, test host, Jasmine/Karma, PowerShell test, CI test or equivalent command was executed. HTTP/database operations shown in test source are authored future assertions only. Counts are static source counts, with no pass claim or historical results reused.

## 23. Build, restore and install status

**NO BUILDS WERE RUN. NO RESTORES OR NPM INSTALLS WERE RUN.** No standalone TypeScript compilation, dotnet publish/run, package install/update, application startup, local/live HTTP request, provider call, EF/database command, or application/API/provider/database verification occurred.

## 24. Database and migration status

No Data/entity/context/schema/index/migration/snapshot files changed in the cumulative working-tree status. No database was accessed or migration created/executed. Private SQLite operations in authored tests remain unexecuted.

## 25. CI/CD and dependency status

No CI/CD/workflow/deployment/release/configuration/dependency manifest or lockfile changed. No dependency was added. No CI/CD script, production request or deployment occurred. Existing security/hosting/release safeguards remain intact.

## 26. Git operations and static checks

Read-only status/diff and `git diff --check` were used. The tracked whitespace check passed; new Phase 5 files were also inspected separately. Git initially rejected repository ownership; command-scoped safe.directory did not resolve that older Git behavior. A temporary Git global-config file under the system temp directory enabled status/diff, and subsequent inspections used explicit `--git-dir=.git --work-tree=.`. No repository or permanent user Git configuration was edited.

No staging, commit, push, merge, rebase, reset, revert, clean, discarded file, deployment or history change occurred. All interrupted valid changes were preserved. Only the three continuation files in section 2 were written in the repository during resumption.

## 27. Phase 5 readiness assessment

**IMPLEMENTED — EXECUTION VERIFICATION PENDING.** Available Phase 5 requirements are statically accounted for by recovered production implementation, focused new/retained tests and this record. Safe Phase 3 errors and Phase 4 provider validation remain intact; R5 identity and paging are preserved. No execution claim is made. Work stops after Phase 5; Phase 6 has not begun.

## Later verification — R7 Phase 8 (October 2, 2026)

The pending/no-execution statements above describe this phase at its original completion and remain historical evidence. The cumulative R7 implementation was subsequently execution-verified in Phase 8: final full backend suite **454 passed, 0 failed, 0 skipped**; final full Angular suite **193 passed, 0 failed, 0 skipped**; Release solution and Angular production builds passed. Phase 8 corrected anonymous Swagger serialization, stale owned-definition and SQLite timestamp assertions, and malformed XML documentation; see the complete failure history and warnings in [R7 Phase 8 verification results](R7-api-contracts-phase-8-verification-results.md).

Current cumulative status: **R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED**, within the local verification limits recorded there. Existing dependency vulnerabilities remain a separate release-review concern. No production access, live dictionary-provider calls, commit, push or deployment occurred. This later note does not claim execution took place during the original phase.
