# R7 — API Contracts Remediation Analysis

Date: 2026-09-30

Status: **Analysis only. No remediation implemented.**

## 1. Executive Summary

The current application implements **14 controller endpoints**: five user/authentication endpoints, six dictionary/personal-vocabulary endpoints, and three quiz endpoints. Twelve require authentication, including dictionary lookup through the fallback policy. Registration and login are anonymous.

The active API largely uses successful JSON envelopes, but there is no single authoritative typed contract. Angular has concrete response and error-field mismatches. Service failures are often flattened into HTTP 400 or 401, including infrastructure failures. Dictionary lookup correctly distinguishes provider 404 and many provider failures (503), but malformed provider collections can escape that classification. Registration can expose exception messages.

R5's `(UserId, WordId)` identity is preserved in active save/update code and regression tests. Part of speech remains selection/derived state, not an additional identity key. The UI already handles duplicate adds and cross-part-of-speech preferred-definition changes correctly. Remaining ambiguity lies in legacy DTOs, ignored add fields, and fallback selection semantics.

There are **16 findings: 0 Critical, 4 High, 10 Medium, and 2 Low**. No demonstrated data-corruption or authorization bypass is identified by this review. The most urgent work is safe error classification, provider-response validation, and matching Angular error handling to the actual API. Contract tests should precede changes; existing tests intentionally assert some current 400 responses that will need coordinated updates.

## 2. Objective

Establish the actual API and consumer contracts, document disagreement and missing protection, and propose bounded R7 remediation. This is not an implementation plan approval, an API redesign, or a change to existing roadmap priorities.

## 3. Scope

Inspected controllers, DTOs, result wrappers, services, startup/authentication/serialization configuration, relevant persistence mappings, Angular models/services/components/guards, backend and frontend tests, deployment checks, and historical API documentation. Sample sentences, learning statistics, and nonexistent operations are explicitly accounted for below.

No application, tests, entities, migrations, settings, workflows, or scripts were changed. No tests/builds, production calls, database access, or dictionary-provider calls were performed for this analysis.

## 4. Repository State Examined

- HEAD: `5d63e068fbb8c62c8b24ece8d671c82f904f9a74`.
- Working tree at entry contained the pre-existing untracked future-feature backlog and `TestResults/ApiCoverage/b7115791-833e-4241-883b-f5b05c05d171/coverage.cobertura.xml`. Neither is an R7 change.
- Backend targets .NET 8 with nullable reference types enabled. [Program.cs](../../VocabularyApp.WebApi/Program.cs) calls `AddControllers()` without custom JSON options or an invalid-model-state response factory.
- Evidence is static source inspection. Framework-default behavior is distinguished from behavior asserted by existing tests; no new runtime verification is claimed.
- Primary controller evidence: [UsersController](../../VocabularyApp.WebApi/Controllers/UsersController.cs), [WordsController](../../VocabularyApp.WebApi/Controllers/WordsController.cs), [QuizController](../../VocabularyApp.WebApi/Controllers/QuizController.cs).
- Service evidence: [UserService](../../VocabularyApp.WebApi/Services/UserService.cs), [WordService](../../VocabularyApp.WebApi/Services/WordService.cs), [QuizService](../../VocabularyApp.WebApi/Services/QuizService.cs).
- Consumer evidence: [AuthService](../../VocabularyApp.UI/src/app/services/auth.service.ts), [ApiService](../../VocabularyApp.UI/src/app/services/api.service.ts), [WordLookupComponent](../../VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts), [QuizComponent](../../VocabularyApp.UI/src/app/components/quiz/quiz.component.ts).

Historical documents are context, not authority. [Docs/README.md](../README.md) still names Free Dictionary API and lists absent word search/definitions/by-ID routes and profile PUT. [test-api.http](../../test-api.http) repeats obsolete routes and anonymous lookup advice; [VocabularyApp.WebApi.http](../../VocabularyApp.WebApi/VocabularyApp.WebApi.http) calls an absent weather endpoint. The current provider is WordsAPI through RapidAPI. The [original R7 entry](../Vocabulary%20Builder%20%E2%80%94%20Plan%20of%20Action.md) describes `ApiResponse` among API wrappers, but the C# file is now entirely commented out. The [R6 completion report](R6-backend-integration-test-foundation-completion.md) correctly describes the relational test foundation; its historical counts are not substituted for current contract coverage.

## 5. Current API Inventory

### 5.1 Notation and common behavior

All routes below start with `/api`. `U`, `W`, and `Q` identify UsersController, WordsController, and QuizController. `Bearer` means a valid JWT; a missing/noninteger user-ID claim can also produce an action-level 401. Lookup is protected by `Program.cs`'s authenticated fallback policy despite lacking an action `[Authorize]`.

Wire-shape notation:

- `G<T>`: controller-local `ApiResult<T>`: `{success,data,error}`; unused nullable fields serialize as null under current defaults.
- `N`: non-generic `Models.ApiResult`: `{success,message,data,error}`.
- `A<T>`: anonymous success `{success:true,data:T}`; action errors `E = {success:false,error:string}`.
- `V`: automatic ASP.NET Core validation problem response, normally `{type,title,status,errors,traceId}`. It does not carry the application `success` field.
- `B`: framework Bearer challenge, normally empty 401 with `WWW-Authenticate: Bearer`; it is not a `G<T>` error body.

For body endpoints, invalid/missing JSON and model binding can return 400 `V` before the action; unsupported media types can return 415. Invalid typed query values can return 400 `V`. An unmatched integer route does not enter its action. These common framework cases are additional to the business failures in the inventory; exact headers/bodies need characterization tests. No endpoint explicitly returns application 403 or 409 today. Transport-level IIS HTTP rejection is a separate 403 policy.

### 5.2 Endpoint table

Request-field details and all response data fields are expanded in sections 6–7. Test abbreviations refer to the named suites in section 14.

| ID | Method and route | Action / authentication | Request type and fields | Successful response | Failure behavior and shape | Angular consumer | Existing tests |
|---|---|---|---|---|---|---|---|
| E01 | POST `/users/register` | U.Register; anonymous | `CreateUserRequest`: username, email, password | 200 `G<AuthResponse>` | 400 `V` or `G` for invalid/duplicate registration; caught service exceptions also 400 `G`; controller exception 500 `G` | AuthService.register; SignupComponent | Auth: valid registration, invalid model, duplicate username/email; R3 anonymous access |
| E02 | POST `/users/login` | U.Login; anonymous | `LoginRequest`: username, password | 200 `G<AuthResponse>` | 400 `V`; 401 `G` for invalid credentials **or service failure**; controller exception 500 `G` | AuthService.login; LoginComponent | Auth valid/invalid credentials; R3 anonymous access |
| E03 | GET `/users/profile` | U.GetProfile; Bearer | None | 200 `G<UserDto>` | 401 `B` or `G`; missing account 404 `G`; 500 `G` | No direct Angular call found | Auth profile isolation, missing user, token rejection; host/HTTPS tests |
| E04 | POST `/users/change-password` | U.ChangePassword; Bearer | Controller-local `ChangePasswordRequest`: currentPassword, newPassword | 200 `N` with success message and null data/error | 400 `V`; 401 `B` or `N` for bad current password, missing account, concurrency or service failure; controller exception 500 `N` | No direct Angular call found | Auth success, wrong password, anonymous; relational service tests |
| E05 | GET `/users/validate-token` | U.ValidateToken; Bearer | None | 200 `G<UserDto>` | 401 `B` or `G` for claim/account failure; 500 `G` | None; Angular decodes JWT locally | R3 anonymous challenge only; no dedicated valid/missing-user shape test |
| E06 | GET `/words/lookup/{word}` | W.LookupWord; Bearer via fallback | Route string `word`; no request DTO bound | 200 `A<WordLookupResponse>` | Blank 400 `E`; genuine provider 404 `E`; classified provider failure 503 `E`; generic service failure 400 `E`; controller exception 500 `E`; 401 `B` | WordLookupComponent.searchWord and definition editor | Dictionary suite; R3 anonymous/provider isolation |
| E07 | POST `/words/vocabulary/add` | W.AddToVocabulary; Bearer | `AddWordRequest`: word, definition?, example?, partOfSpeech?, pronunciation?, preferredWordDefinitionId? | 200 `A<AddToVocabularyResultDto>` for new **and existing** entry | 400 `V`/`E` for input, unavailable canonical word, invalid selection or service failure; 401 `B`/`E`; controller exception 500 `E` | WordLookupComponent.addToVocabulary | Vocabulary ownership, duplicate/concurrent add, invalid definition, persistence failure; R3 |
| E08 | GET `/words/vocabulary` | W.GetUserVocabulary; Bearer | Query: page=1, pageSize=20, term?, startsWithLetter? | 200 `A<UserVocabularyResponseDto>` | 400 `V` for binding or `E` for service failure; 401 `B`/`E`; 500 `E` for controller exception | WordLookupComponent.loadVocabularyPage (`pageSize=1000`) | Vocabulary isolation/filter tests; quiz accuracy projection; R3 |
| E09 | GET `/words/vocabulary/search` | W.SearchUserVocabulary; Bearer | Required nonnullable query string `term`; service maxResults=5 | 200 `A<UserVocabularyResponseDto>` normally; action blank branch returns only `A<{words:[]}>` | Missing/empty term can be rejected as 400 `V` before blank branch; service 400 `E`; 401 `B`/`E`; controller exception 500 `E` | WordLookupComponent suggestions and saved-word lookup | Vocabulary search isolation/filter tests; R3 |
| E10 | PUT `/words/vocabulary/{userWordId:int}/favorite` | W.SetFavorite; Bearer | Integer route; `UpdateFavoriteRequestDto`: isFavorite | 200 `A<{message,userWordId,isFavorite}>` | 400 `V`/`E`; absent or another user's ID both 400 `E`; service failures 400 `E`; 401 `B`/`E`; controller exception 500 `E` | WordLookupComponent.toggleFavorite | Vocabulary own/cross-user/missing ID; R3 |
| E11 | PUT `/words/vocabulary/{userWordId:int}/preferred-definition` | W.SetPreferredDefinition; Bearer | Integer route; `UpdatePreferredDefinitionRequestDto`: preferredWordDefinitionId | 200 `A<{message,userWordId,preferredWordDefinitionId}>` | 400 `V`/`E` for invalid selection, absent/nonowned entry, service failure; 401 `B`/`E`; controller exception 500 `E` | WordLookupComponent.savePreferredDefinition | Vocabulary same-row cross-POS update, ownership, missing/cross-word IDs; R3 |
| E12 | POST `/quiz/start` | Q.StartQuiz; Bearer | `StartQuizRequestDto`: questionCount=10, mode="mixed" | 200 `A<QuizStartResponseDto>` | 400 `V`/`E`; fewer than four usable words or service failure 400 `E`; 401 `B`/`E`; controller exception 500 `E` | QuizComponent.startQuiz | Quiz creation/no answer-key test; R3 anonymous |
| E13 | POST `/quiz/submit` | Q.SubmitQuiz; Bearer | `QuizSubmitRequestDto`: sessionId GUID, answers array of questionId GUID and selectedOptionId integer | 200 `A<QuizSubmitResponseDto>` | 400 `V`/`E` for absent/expired/nonowned session, invalid answers, repeated/in-progress submit or persistence failure; 401 `B`/`E`; controller exception 500 `E` | QuizComponent.submitQuiz | Quiz scoring, tampering, ownership, repeat/concurrency, rollback, missing session; R3 |
| E14 | GET `/quiz/history` | Q.GetQuizHistory; Bearer | Query integer take=5 | 200 `A<QuizHistoryResponseDto>` | 400 `V` for binding or `E` for service failure; 401 `B`/`E`; controller exception 500 `E` | QuizComponent.loadRecentQuizHistory | Quiz history user isolation; R3 anonymous |

`AddToVocabulary` contains a fallback `{message}` success data object if a successful service result has null data; current service success branches return `AddToVocabularyResultDto`. This fallback is not a separate implemented business outcome.

### 5.3 Surface boundaries

No controller routes implement vocabulary deletion/archive, general notes updates, sample-sentence CRUD, parts-of-speech enumeration, canonical-word creation, profile updates, or dedicated analytics. `ApiService.delete` is a generic unused capability, not evidence of a delete endpoint. Definitions expose one `example`; `SampleSentence` entities are not exposed as collections. Learning information is limited to vocabulary correct/attempt counters and computed accuracy, quiz scoring, and recent history. Dashboard cards are not analytics endpoints.

Swagger JSON/UI, static assets, and the anonymous SPA fallback are hosting surfaces, not additional application API actions. Unknown `/api` requests should be characterized: the broad SPA fallback is not explicitly excluded from that prefix. Do not infer an implemented route from a fallback HTML response. The removed canonical add route is separately protected by `DirectCanonicalWordAddIsUnavailableToAnonymousAndAuthenticatedUsers`.

## 6. Request Contract Analysis

### 6.1 Active request contracts

| Contract | Actual requirements, defaults, normalization | Contract issue |
|---|---|---|
| `CreateUserRequest` in [UserDTOs.cs](../../VocabularyApp.WebApi/DTOs/UserDTOs.cs) | Required username length 3–100; required email format/max 200; required password length 6–100. UserService compares username/email case-insensitively via `ToLower`, stores supplied text, and does not trim it. | Signup limits username to 50. Whitespace/normalization expectations are not explicit. Passwords must not be normalized as a cleanup side effect. |
| `LoginRequest` | Required username/password; no explicit maximum length. Username comparison is case-insensitive, untrimmed. | No shared documented username normalization policy; client max-length rules are not API rules. |
| `ChangePasswordRequest` in UsersController | Required currentPassword; required newPassword length 6–100. | Local DTO placement and distinct result type complicate discovery; false service result conflates failure causes. |
| `AddWordRequest` in [Models/AddWordRequest.cs](../../VocabularyApp.WebApi/Models/AddWordRequest.cs) | All nullable; word checked manually. Canonical text queried without trim. Preferred ID, if present on a new save, must belong to the word. Otherwise POS name/abbreviation is resolved case-insensitively; missing/unknown POS defaults to Noun. | Definition, example, pronunciation are accepted but ignored. No DTO validation or positive-ID annotation. Existing entry returns before preferred-ID validation intentionally. |
| Favorite update | Nonnullable bool, default false, no presence annotation. | `{}` can clear favorite rather than reject missing intent. Explicit null/wrong type is a binding failure. |
| Preferred-definition update | Nonnullable int, default 0; controller/service require >0, service verifies canonical ownership. | Validation lives in actions/services, unlike registration annotations. Route ID has an `int` constraint but no positive range. |
| Lookup | Route string checked for whitespace and trimmed in WordService. | No explicit length/character bound; exact DB equality/collation differs from the add path's untrimmed word. Empty route is routing, not necessarily input-validation 400. |
| Vocabulary list/search | Page <1 becomes 1; pageSize outside 1–10000 becomes 20. `term` trimmed/lowercased in service. `startsWithLetter` uses first trimmed character. Search returns at most five rows. | Silent normalization and limits are not typed query contracts. Search totalCount is returned-match count, not all matching rows. Missing-term model validation competes with intended empty-search branch. |
| Start quiz in [QuizDTOs.cs](../../VocabularyApp.WebApi/DTOs/QuizDTOs.cs) | questionCount <=0 becomes 10, otherwise capped at 20; actual count also limited by usable words. Mode is trimmed/lowercased; unknown values become mixed. At least four usable saved words required. | No annotations/string enum definition. Nonnullable mode can make explicit null invalid at model validation despite service fallback. Empty body can be rejected before controller's null fallback. |
| Submit quiz | Missing sessionId becomes empty GUID and fails service validation; malformed GUID fails binding. Missing answers defaults to empty list; omitted questions count incorrect. SelectedOptionId defaults to 0, which is a legitimate option. | Omitted option ID can silently mean option zero. Null answer elements are not a clear DTO contract and can hit the service catch. Duplicate questions/foreign questions/options checked in service. |
| History | take <=0 becomes 5, capped at 20. | Defaults and clamping should be documented/tested, not silently changed to rejection. |

IDs are C# `int`/TypeScript `number` for persisted objects and C# `Guid`/JSON strings for quiz sessions/questions. A vocabulary item `id` is **UserWord.Id**, while lookup word/definition IDs are canonical IDs. Requests never accept an authoritative userId: ownership derives from claims. These are needed resource references, not evidence of old composite identity.

### 6.2 Dormant and duplicate DTOs

[UserWordDTOs.cs](../../VocabularyApp.WebApi/DTOs/UserWordDTOs.cs) defines `UserWordDto`, `AddWordToCollectionRequest`, `UpdateUserWordRequest`, and `UserWordCollectionResponse`, but no active controller/service references these types. They imply writable custom definition/difficulty/notes capabilities that are not current routes. Its comment says favorite/custom-definition/difficulty are removed while properties remain; current UserWord maps favorite but not custom definition/difficulty.

`WordLookupRequest`, [WordRequest/DefinitionDto](../../VocabularyApp.WebApi/DTOs/WordRequest.cs) are also unbound to current endpoints. Do not retrofit these as active contracts or accidentally restore canonical writes. Remove/deprecate only after reference checks in implementation. The active add DTO must explicitly document ignored legacy fields before any removal.

## 7. Response Contract Analysis

### 7.1 Active data payloads

| Data type | Fields on the wire |
|---|---|
| `AuthResponse` | success, errorMessage (nullable), user (`UserDto`, nullable), token (nullable). Outer `G` duplicates success/error state. No expiresAt property. |
| `UserDto` | id, username, email, createdAt, lastLoginAt (nullable). No password/hash. |
| `WordLookupResponse` | success, errorMessage (nullable), word (`WordDto`, nullable), wasFoundInCache, isInUserVocabulary. Nested success is redundant with outer `A`. |
| `WordDto` | id, text, pronunciation (nullable), audioUrl (nullable), createdAt, definitions[]. |
| `WordDefinitionDto` | id, definition, example (nullable), partOfSpeech, partOfSpeechAbbreviation, displayOrder. No synonyms/antonyms. |
| `UserVocabularyItemDto` | id (saved-row ID), word, definition, preferredWordDefinitionId (nullable), example (nullable), partOfSpeech, pronunciation/audioUrl (nullable), addedAt, isFavorite, personalNotes (nullable), correctAnswers, totalAttempts, accuracyRate (nullable; percentage 0–100 for ordinary valid counters). |
| `UserVocabularyResponseDto` | words[], totalCount, page, pageSize, computed totalPages. |
| `AddToVocabularyResultDto` | userWordId, wordId, alreadyExisted, message. |
| Favorite/preferred acknowledgements | Anonymous payloads listed in E10/E11; no complete refreshed vocabulary row. UI updates its local row using its selected definition. |
| `QuizStartResponseDto` | sessionId, mode, questionCount, expiresAtUtc, questions[]: questionId, questionType, prompt, options[] {optionId,text}. No correct answer before submission. |
| `QuizSubmitResponseDto` | totalQuestions, correctAnswers, scorePercentage (rounded to two decimals), questionResults[] {questionId,questionType,prompt,correctAnswer,selectedAnswer (nullable),isCorrect}. |
| `QuizHistoryResponseDto` | items[] {attemptedAtUtc,totalQuestions,correctAnswers,scorePercentage}. No session ID or paging metadata. |

Sources: [WordDTOs](../../VocabularyApp.WebApi/DTOs/WordDTOs.cs), [UserVocabularyDTOs](../../VocabularyApp.WebApi/DTOs/UserVocabularyDTOs.cs), UserDTOs, QuizDTOs, and the service projections.

### 7.2 Typing and serialization

C# [ApiResponse.cs](../../VocabularyApp.WebApi/DTOs/ApiResponse.cs) is commented out, not a runtime wrapper. `ApiResult<T>` actually lives in the Controllers namespace in UsersController; the non-generic [DTOs/ApiResult.cs](../../VocabularyApp.WebApi/DTOs/ApiResult.cs) declares the Models namespace. Its unused `ErrorResult(object)` throws `NotImplementedException`. `ServiceResult<T>` is internal service output, not directly serialized; many WordService methods return `ServiceResult<object>`, hiding otherwise known payloads.

There are no ordinary successful raw DTO/array/string/204 actions in the inventory: successful data is enveloped. The important differences are anonymous versus declared envelopes, omitted versus null properties, redundant nested flags, and the special password-change shape. No active controller deliberately returns 200 with a failure result; some Angular mutation handlers nevertheless treat any 2xx body as success without inspecting it.

With current MVC defaults, properties are camelCase, nullable DTO properties are not globally omitted, and dates serialize as strings rather than JavaScript Date objects. UTC-created values and reloaded SQL Server `DateTime` values need explicit timezone-contract testing; the `Utc` suffix alone does not enforce a JSON offset. No custom enum-string converter is installed. Active mode/question-type/POS fields are already strings, not serialized C# enums; internal `ServiceFailureType` and persisted `QuizType` are not public response fields.

## 8. HTTP Semantics

- **Lookup:** 400 invalid input, 404 provider not-found, 503 classified provider failures is the intended and mostly implemented distinction. Explicit provider null collections and internal DB/mapping exceptions break it (F02/F03). Route misses are not genuine dictionary misses.
- **Creates:** registration, saved-word creation and quiz start return 200. 201 may be appropriate for some newly created resources, but changing status/Location is not required simply for consistency; registration has no public by-ID resource route. Keep current status until consumers and semantics are agreed.
- **Duplicates:** repeated saved-word POST intentionally returns the same ID, 200 and alreadyExisted=true without changing preference/state. Registration duplicates return 400; 409 is a possible deliberate migration, not current behavior.
- **Ownership/not-found:** favorite/preferred missing and nonowned rows both return 400, preventing mutation but obscuring meaning. A consistent 404 concealment policy is reasonable; quiz nonownership currently uses a revealing error string with 400. Choose 403 versus concealed 404 deliberately, not by blanket remapping.
- **Exceptions:** many service catches return normal failure values, so controller 500 catches do not classify them. Login/password failures can actually be storage failures expressed as 401.
- **Idempotency:** favorite and preferred PUT set state; same request should leave the same selection. Quiz submission is not repeatable-success idempotency: one success removes its in-memory session and repeats fail 400. Concurrency and persisted uniqueness guard counters. Preserve these invariants separately from status improvements.
- **Delete:** no delete route, hence no active delete-status inconsistency to fix.
- **GET lookup:** may populate canonical cache. Preserve this existing provider/cache boundary rather than expanding R7 into a caching redesign.

## 9. Error Contract Analysis

Current errors comprise `G<T>`, `N`, anonymous `E`, automatic validation ProblemDetails, and framework authentication challenges. Normal controller error paths do not return plain strings. No application-wide exception/ProblemDetails handler is configured in Program; exceptions outside action catches are not guaranteed the controller envelope.

`[ApiController]` automatically handles invalid models before the manual `ModelState.IsValid` blocks in UsersController under current configuration. Consequently those manual blocks do not establish a uniform validation format. Null/missing values and malformed JSON need HTTP-level tests rather than direct action tests.

Clients can distinguish success and transport status, but cannot reliably distinguish invalid input from internal failure, invalid credentials from login persistence failure, or unavailable/nonowned resources from bad requests. Registration's `CreateUserAsync` catch returns `ex.Message`, then Register places it in the public error field. This exposes implementation details depending on the exception; no actual secret disclosure was demonstrated.

Recommended concept: retain current typed success envelopes initially, establish one Angular error adapter supporting current `error`, `message`, validation `errors` and ProblemDetails, then migrate server errors to a documented ProblemDetails contract with stable machine-readable codes and a correlation identifier. Preserve Bearer challenge headers and statuses; do not wrap errors in HTTP 200. Whether to add bodies to challenges is a separate compatibility decision. A single typed error envelope is also viable if validation/auth failures are covered consistently. No target is implemented or implicitly approved here.

## 10. Frontend/Backend Contract Alignment

| Area | Backend evidence | Frontend evidence | Actual drift / impact |
|---|---|---|---|
| Registration payload | UsersController.Register returns `G<AuthResponse>` | [user.model.ts](../../VocabularyApp.UI/src/app/models/user.model.ts) `RegisterResponse.data?: User` | Wrong nesting/type; signup currently checks only success, so this does not by itself prove broken registration. |
| Login expiry/user dates | AuthResponse has token/user but no expiresAt; UserDto dates serialize as strings/null | `LoginResponse.data.expiresAt: Date`; User.createdAt/lastLoginAt use Date, optional without null | expiresAt is undefined, but setSession currently ignores its argument and authentication reads JWT exp. Latent mismatch, not demonstrated forced logout. |
| Error fields | Generic/anonymous errors expose error, not message | [LoginComponent](../../VocabularyApp.UI/src/app/components/login/login.component.ts), [SignupComponent](../../VocabularyApp.UI/src/app/components/signup/signup.component.ts), addToVocabulary error handler read message/errorMessage | Server explanations are lost for duplicate registration, bad credentials, invalid canonical selection. Common ApiResponse<T> does not declare error. |
| Validation | ASP.NET validation uses errors/title/status | No shared error adapter; quiz/vocabulary parse error/errorMessage | Field-validation details are lost; endpoint-specific parsing required. |
| Lookup models | `WordLookupResponse`/WordDto/WordDefinitionDto | WordLookupComponent `get<any>`, casts; [word-lookup.model.ts](../../VocabularyApp.UI/src/app/models/word-lookup.model.ts) is grouped UI view model | Mapping is intentional, but no transport model guards it. Reads nonexistent synonyms/antonyms, tolerates missing data, and duplicates lookup path in editor. |
| Nullability | Nullable preferred ID/example/audio/notes/accuracy and selectedAnswer | VocabularyItem/Definition fields and [quiz.model.ts](../../VocabularyApp.UI/src/app/models/quiz.model.ts) selectedAnswer are optional but exclude null | JSON null is not undefined; TypeScript promise does not reflect runtime data. |
| Requests | Add accepts ignored definition/example/pronunciation; typed DTO selection fields | Component sends definition/example and nullable preferred ID; ApiService post/put use any | Compiler cannot enforce request shape. IDs and ignored fields need documentation, not additional canonical writes. |
| Pagination | Server paginates and reports totalCount/totalPages | loadVocabularyPage requests 1000, comments call it all words; filtering/counts operate on current words[] | For larger collections, search and letter availability are page-local, potentially hiding saved words. This is an inferred boundary defect, not a production-size claim. |
| Registration limits | Username max 100 | Signup max 50 | UI rejects backend-valid usernames; choose one supported contract. |
| Mutation acknowledgements | Named add payload; anonymous favorite/preferred payloads | put<any>; preferred and favorite ignore body on success | Shape regressions can pass UI code/tests. Add assumes successful HTTP implies successful operation. |

All active Angular API paths match implemented endpoints; no active call to an absent delete/profile-update route was found. Generic transport methods take free-form path strings; lookup/search strings appear in multiple component paths. Auth paths are constructed independently. Central typed operation methods are a bounded improvement; broad component decomposition belongs to follow-up work. TypeScript auth response interfaces repeat envelope definitions, but grouped lookup view models are purposeful, not automatically duplicates to delete.

## 11. R5 UserWord Compatibility

[ApplicationDbContext](../../VocabularyApp.Data/ApplicationDbContext.cs) has the unique `(UserId,WordId)` index. WordService.AddToVocabularyAsync looks up existing rows using that pair **before** validating a new preferred selection. Existing rows return the same userWordId and unchanged preference/counters/notes. New preferred definitions must belong to the canonical word. SetPreferredDefinitionAsync scopes by row ID and authenticated owner, changes preferred ID/POS on the same row, and preserves dependent data.

Tests `DuplicateCanonicalSaveIsIdempotentAcrossPartsOfSpeech`, `ConcurrentDuplicateSavesReturnOneStableEntry`, `CrossPartOfSpeechPreferredDefinitionUpdatePreservesStateAndDependents`, and `RuntimeModelUsesTwoColumnUserWordIdentity` explicitly protect this. Angular's corresponding duplicate-add and same-item-definition tests are consistent with R5.

No active triple-key route, request identity, UI key, or service uniqueness check remains. POS filtering still exists in vocabulary/quiz projection because it is retained synchronized derived state. Legacy `AddWordToCollectionRequest` requires POS and can misleadingly suggest the old model, but is unused. Without a preferred ID, the active POS fallback can select Noun even when unavailable for a word, producing no selected definition; this is a selection-contract concern, not renewed composite identity. R7 must neither remove the derived column nor change existing-row short-circuit behavior casually.

## 12. External Provider Boundary

[WordsApiDtos.cs](../../VocabularyApp.WebApi/DTOs/External/WordsApiDtos.cs) is internal transport data: word, results, pronunciation dictionary, definition/POS/examples. WordService maps this into local Word/WordDefinition and application DTOs. RapidAPI host/key stay in outbound headers; provider HTTP authentication/rate limits map to 503, not client 401/429. `ApiKeyIsSentToProviderAndNeverReturnedToClient` protects the key boundary.

Public payloads expose local IDs, application word/definition fields, and cache/membership flags, not provider IDs/raw results/headers. `wasFoundInCache` exposes application cache state; it does not name a provider. Pronunciation text originates upstream but uses the application's field. AudioUrl is nullable and null for new WordsAPI words; Angular uses browser speech synthesis, with a null-audio regression test. Do not promise upstream audio or introduce a provider replacement.

Only the eight seeded POS names are mapped; unknown POS definitions are skipped, and all-unusable results return 503 rather than being fabricated as Noun. Angular priorities include determiner/exclamation, which are not seeded backend categories. This is vocabulary-taxonomy drift, not a new provider enum contract.

Important gap: JSON initializers do not prevent explicit `results:null`, `pronunciation:null`, `examples:null`, or null elements. Dereferencing these can throw outside the provider parse catch, become generic ServiceResult failure, and surface as 400. Structurally invalid but syntactically valid JSON therefore does **not** uniformly preserve the required malformed-response 503 rule. Test these cases and validate mapping input. Concurrent cache inserts/internal DB errors similarly become 400 today; repairing cache concurrency itself is follow-up, while classifying its failure correctly is R7.

## 13. Authentication Contracts

Registration/login return the same nested `AuthResponse`; registration issues a token even though signup intentionally navigates to login without storing it. Successful login stores `vocab_app_token` and serialized `vocab_app_user`. AuthService/guard check JWT exp locally using `atob`/JSON and do not call validate-token or profile. ApiService adds `Authorization: Bearer <token>` when available; AuthService uses HttpClient directly for anonymous operations. No interceptor refresh/revocation flow exists in inspected code.

[JwtHelper](../../VocabularyApp.WebApi/Helpers/JwtHelper.cs) signs HS256 tokens using configured issuer/audience/expiration, includes identifier/name/email/jti, and does not provide separate expiresAt in AuthResponse. JWT validation is configured in [JwtSettings](../../VocabularyApp.WebApi/Configuration/JwtSettings.cs). Preserve claims, signing configuration, token lifetime, password migration, and stored-session compatibility.

The backend enforces authentication independently of Angular guards. Profile returns 404 for a valid token whose user is missing; validate-token returns 401 for that same account condition. No role policy exists; resource ownership is checked by services. A server 401 from password-change concurrency/internal failure currently looks like invalid credentials, and the action says current password is incorrect regardless of cause. Token-expiry/base64url edge cases merit focused frontend tests; this review does not establish a token-decoding exploit or redesign authentication.

## 14. Backend Contract Test Coverage

Evidence suites in [VocabularyApp.WebApi.Tests/Integration](../../VocabularyApp.WebApi.Tests/Integration): `AuthenticationApiTests` (Auth), `DictionaryLookupApiTests` (Dictionary), `VocabularyOwnershipApiTests` (Vocabulary), `QuizApiTests` (Quiz), `R3SecurityContractApiTests` (R3), plus host/isolation/HTTPS suites. R3 lists all 14 actions, tests anonymous access to all 12 protected routes, and checks the controller action inventory.

### 14.1 Contract matrix

`Yes` means targeted behavior assertions exist, not exhaustive coverage. `Partial` identifies selected scenarios or data fields only. `Gap` means no meaningful targeted assertion located. `—` is not applicable. Not-found includes current missing-resource behavior even when status is 400.

| Endpoint | Success | Validation | Authentication | Authorization/ownership | Not-found | Conflict/duplicate | Provider failure | Response shape |
|---|---|---|---|---|---|---|---|---|
| E01 register | Yes | Partial: invalid fields/status | Anonymous success | — | — | Yes: username/email | — | Partial: user/token; not exact null/error shape |
| E02 login | Yes | Gap: invalid model/JSON | Yes: bad credentials | — | Unknown username gives 401 | — | — | Partial: token/profile; no complete wire schema |
| E03 profile | Yes | — | Yes: malformed/tampered/expired token | Yes: per-user claims | Yes: missing user 404 | — | — | Partial: user fields, not date/null contract |
| E04 password | Yes | Gap: boundary/missing fields | Yes: anonymous/wrong current | Yes: other user unchanged | Gap: missing account | Service concurrency tests, HTTP status classification gap | — | Gap: success/error envelope properties |
| E05 validate-token | Gap | — | R3 anonymous only | Gap: claim/account branches | Gap: missing user 401 | — | — | Gap |
| E06 lookup | Yes: cache/provider | Gap: invalid input/bounds | Yes: R3 fallback/provider untouched | Partial: no membership-flag isolation assertion | Yes: provider 404 | Gap: concurrent cache behavior | Yes for known cases; null collections gap | Partial: word/cache/mapping/key safety; null/casing schema gap |
| E07 add | Yes | Partial: absent canonical/cross-word definition | Yes: R3 | Yes: own rows/different users | Yes: absent canonical 400 | Yes: repeated and concurrent stable ID | — (does not call provider) | Partial: stable IDs/alreadyExisted; error contract incomplete |
| E08 list | Yes | Gap: query boundaries/invalid types | Yes | Yes: user isolation | Empty-list metadata gap | — | — | Partial: rows and accuracy; full paging/null schema gap |
| E09 search | Yes | Gap: absent/empty/binding | Yes | Yes: filtering/isolation | No-match metadata gap | — | — | Partial: rows; count/limit/empty shape gap |
| E10 favorite | Yes | Gap: missing bool/null/type | Yes | Yes: cross-user rejected | Yes: missing ID 400 | Gap: repeated PUT contract | — | Gap: acknowledgement/error wire fields |
| E11 preferred | Yes | Partial: invalid/missing/cross-word IDs | Yes | Yes: cross-user rejected | Yes: missing entry/definition 400 | Gap: repeated PUT contract | — | Partial: state preservation; acknowledgement shape gap |
| E12 quiz start | Yes | Gap: min vocabulary, count/mode/null/body | Yes | Partial: user-seeded creation, no dedicated disjoint question-pool test | — | — | — | Partial: no answer key/options; expiry/default semantics gap |
| E13 quiz submit | Yes | Yes for tampering; missing property/null element gaps | Yes | Yes: another user's session | Yes: unknown session; explicit timed expiry gap | Yes: sequential/concurrent submission | — | Partial: results/counters; exact null/error schema gap |
| E14 history | Yes | Gap: take boundaries/binding | Yes | Yes: user histories isolated | Empty history shape gap | — | — | Partial: results; ordering/time format gaps |

### 14.2 Strong existing guarantees and important limitations

Quiz tests protect correct/incorrect/unanswered scoring, persisted accuracy, rollback, retry, forged/foreign/duplicate questions, options, deleted vocabulary, and one logical submission. Vocabulary tests protect user ownership, canonical-write removal, R5 duplicate handling and same-row preference changes. Dictionary tests exercise 404, provider 401/403/429/500, network/timeout, malformed JSON/null root/empty object, and unsupported POS.

Many helpers deserialize into C# DTOs with web defaults. This protects behavior but can tolerate missing fields via defaults, extra fields, and case-insensitive property matching. It does not prove exact Angular-compatible key names, required-field presence, nullability, content types, or consistent error envelopes. `InvalidRegistrationIsRejectedByModelValidation` checks 400 and no persistence, not validation response shape.

`UnrelatedVocabularyPersistenceFailureIsNotDuplicateSuccess` and `PersistenceFailureRollsBackResultsAndLearningStateAndSessionRemainsRetryable` explicitly expect current **400** for server failure. Preserve their state/rollback assertions while intentionally revising status expectations if R7 changes semantics.

[VocabularyAppWebApplicationFactory](../../VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs) uses the real pipeline, isolated in-memory SQLite with `EnsureCreated`, deterministic JWT settings, and [ControllableDictionaryHandler](../../VocabularyApp.WebApi.Tests/Infrastructure/ControllableDictionaryHandler.cs). No live provider is needed. SQLite does **not** validate SQL Server migrations, collation, native error numbers, SQL Server-specific transaction behavior, or IIS rewrite rules. Migration-definition tests inspect operations without executing SQL Server migrations.

Highest-value missing tests: exact auth/error wire fixtures; explicit-null provider structures returning 503; internal service exceptions returning approved server errors without details; missing bool/option IDs; valid/missing-account validate-token; paging/search empty cases; expired sessions; full nullable/date contracts; and OpenAPI response/auth metadata. Use controlled faults and raw JSON assertions rather than live calls.

## 15. Angular Contract Test Coverage

| Test file / area | Actual protection | Missing contract protection |
|---|---|---|
| [auth.service.spec.ts](../../VocabularyApp.UI/src/app/services/auth.service.spec.ts) | Construction, null stored user, unauthenticated state | No login/register requests, payload/method assertions, actual nested responses, token storage, expiry, or error cases |
| [api.service.spec.ts](../../VocabularyApp.UI/src/app/services/api.service.spec.ts) | Construction, HttpTestingController.verify with no operation | No Authorization headers, base URL composition, verb/body assertions, envelope/error forwarding |
| Login/signup specs | Component creation with mocked AuthService | No submit payload, server error/validation rendering, auth response contract, navigation/session behavior |
| [word-lookup.component.spec.ts](../../VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts) | Mock lookup with null audio; search interaction; local filtering; duplicate add; same-item preferred change; speech behavior | Most HTTP matches use URL suffix/contains, not full method/body/header assertions. No 400/404/503 matrix, favorite rollback contract, full paging metadata, or provider nullability fixtures |
| Quiz | No quiz component spec found | Start/submit/history routes, GUID/option payloads, expiry/errors and response parsing unprotected |
| App/dashboard/toast | Construction and UI behavior | Not substitutes for API contract tests |

HttpTestingController fixtures are hand-authored, not derived from backend JSON contracts. Tests can remain green while server envelopes change. Add shared reviewed wire examples or equivalent schema assertions, explicit `request.method`, `request.body` and Bearer header checks, plus status-specific error fixtures. Include null fields and omission separately. Do not count speech/local-filter tests as broad API coverage.

## 16. Backward Compatibility

| Potential change | Classification | Consumers and migration implications |
|---|---|---|
| Add exact contract tests/docs and internal named response types preserving JSON | Non-breaking | Verify byte-level field/status compatibility where relevant; avoids unnecessary production changes |
| Correct TS interfaces to actual payloads, add error adapter | Non-breaking on wire; coordinated internal code change | UI compilation may expose existing assumptions; old localStorage user records remain compatible if wire fields remain |
| Replace envelopes/remove nested success/rename fields/change null omission | Internally breaking but coordinated; externally breaking/unknown | Current Angular expects data.word, data.token/user and success. Cached old bundles can outlive a combined deployment; use additive transition or explicit version strategy |
| Map 400/401 failures to 404/409/500; use ProblemDetails | Internally breaking but coordinated; external risk unknown | Update Angular parser and backend tests intentionally; preserve missing/invalid-token challenge and transport checks |
| Require previously defaulted fields/reject unknown modes or POS | Behavior-breaking for existing callers | Characterize omitted fields, preserve duplicate-add semantics, decide deprecation before stricter validation |
| Change canonical identity/IDs or stored vocabulary | Out of R7 scope | Would endanger persisted relationships, saved preferences and learning history; no schema change needed for R7 |
| Change token fields/lifetime/storage expectations | Potentially breaking authenticated users | Prefer correcting model to current JWT behavior; auth-system redesign is excluded |

No maintained third-party client was identified. Repository `.http` examples are real consumer evidence but stale; absence of other clients in source is not proof no external consumers exist. Treat wire changes as unknown external compatibility until ownership is established.

## 17. Production/Deployment Considerations

Read-only evidence: [.github/workflows/backend-tests.yml](../../.github/workflows/backend-tests.yml), [Deploy-SmarterAsp.ps1](../../scripts/ci/Deploy-SmarterAsp.ps1), [Assert-ProductionReleaseCandidate.ps1](../../scripts/ci/Assert-ProductionReleaseCandidate.ps1), [Test-ProductionHttps.ps1](../../scripts/ci/Test-ProductionHttps.ps1), Program.cs and the API project's [web.config](../../VocabularyApp.WebApi/web.config), which supplies the published root IIS configuration.

The push-to-master workflow runs backend and frontend test jobs before the build/publish artifact. Deployment consumes the tested artifact, runs automatically for the master push, and uses production release serialization (`cancel-in-progress:false`, `queue:max`) plus release-candidate freshness checks. R7 requires no gate, trigger, artifact, concurrency, or stale-release-protection change.

`Test-ProductionHttps.ps1` requires anonymous HTTPS GET `/api/users/profile` to return 401, a Bearer challenge, no redirect/HTML, no production CORS grant, and HSTS `max-age=300` without includeSubDomains/preload. It also expects insecure API/unsafe HTTP requests to be rejected with 403 and safe navigation redirects. Changing error JSON alone need not break those assertions; converting challenges to redirects, HTML, or 200 would. The script does not validate an authenticated API envelope and explicitly leaves authenticated manual smoke outstanding. Earlier documented manual login/lookup/vocabulary flows will need contract-compatible acceptance in eventual implementation.

Preserve application HSTS on the canonical Production HTTPS host, root IIS HTTPS enforcement, development-only configured CORS, same-origin production `/api`, and JWT middleware ordering. The test host cannot prove deployed IIS behavior. Do not run production acceptance scripts during this analysis or use production to discover contracts.

## 18. Findings

Counts: **0 Critical / 4 High / 10 Medium / 2 Low**. Severity reflects observed code and likely impact, not hypothetical exploits.

### R7-F01 — High — Registration exposes exception-derived errors

- **Files:** UserService.cs `CreateUserAsync`; UsersController.cs `Register`.
- **Current behavior:** catch sets `AuthResponse.ErrorMessage = ex.Message`; controller returns that string in a 400 error.
- **Risk:** implementation details may reach anonymous clients; server failure looks like invalid input. No specific secret leak was demonstrated.
- **Direction:** log the exception internally; return a safe classified failure with correlation information.
- **Breaking risk:** error text/status changes affect clients and tests; success payload need not change.
- **Tests:** injected hashing/persistence exception; assert safe body, approved server status, no exception detail and no unintended persisted user.

### R7-F02 — High — Failure categories collapse into 400/401

- **Files:** Models/ServiceResult.cs; WordService.cs; QuizService.cs; UserService.cs; all controllers.
- **Current behavior:** default failure is Validation; most service catches return it. Auth service false/failure results map to 401, including infrastructure/concurrency cases.
- **Risk:** UI and monitoring cannot distinguish bad input, credential rejection, unavailable resources and server failure; retry/session decisions become unreliable.
- **Direction:** explicitly classify failures and agree endpoint status mappings; preserve ownership checks and rollback.
- **Breaking risk:** coordinated error/status change; existing fault-injection tests assert 400.
- **Tests:** faults in each service family; approved not-found/ownership/conflict mapping; unchanged persistence safety and Bearer challenges.

### R7-F03 — High — Structurally malformed provider payloads can return 400

- **Files:** DTOs/External/WordsApiDtos.cs; WordService.cs `LookupWordAsync`; WordsController.cs `LookupWord`; DictionaryLookupApiTests.cs.
- **Current behavior:** explicit null collections/elements can pass deserialization, throw during mapping, and become generic 400 rather than required 503.
- **Risk:** a provider schema anomaly is reported as user error and violates the established 400/404/503 contract.
- **Direction:** validate provider structures at the boundary; classify unusable responses as ServiceUnavailable and avoid persistence.
- **Breaking risk:** corrective status change for malformed responses, no normal payload change.
- **Tests:** results/pronunciation/examples null, null result element, missing/malformed required data, mixed valid/unsupported POS; assert 503 and no partial records.

### R7-F04 — High — Angular loses actionable API errors

- **Files:** user.model.ts; login.component.ts; signup.component.ts; word-lookup.component.ts `addToVocabulary`; server controllers.
- **Current behavior:** consumers read message/errorMessage while server frequently returns error; validation errors use a third shape.
- **Risk:** actual duplicate/input/credential failures display generic messages instead of actionable explanations.
- **Direction:** shared typed error adaptation before server error migration; retain appropriate generic messages for unknown/internal failures.
- **Breaking risk:** non-breaking wire change if only client parsing is corrected.
- **Tests:** duplicate registration, invalid login/add, validation field map, empty 401 body, 404 and 503 rendering.

### R7-F05 — Medium — Authentication success models disagree

- **Files:** UserDTOs.cs; UsersController.cs; user.model.ts; auth.service.ts.
- **Current behavior:** registration data typed as User instead of AuthResponse; login requires nonexistent expiresAt; Date types promise objects where JSON supplies strings.
- **Risk:** future consumers use nonexistent fields; current expiry mismatch is masked because setSession ignores it.
- **Direction:** model actual nested authentication response and date/null types; decide separately whether explicit expiry should ever be added.
- **Breaking risk:** TS internal corrections; avoid renaming/removing existing wire fields.
- **Tests:** actual registration/login JSON fixtures, storage, JWT expiry/invalid encoding, nullable lastLoginAt.

### R7-F06 — Medium — Multiple weakly typed envelopes and missing schema metadata

- **Files:** UsersController.cs `ApiResult<T>`; DTOs/ApiResult.cs; DTOs/ApiResponse.cs; IWordService.cs/WordService.cs; WordsController.cs; QuizController.cs; Program.cs.
- **Current behavior:** object service payloads and anonymous action responses; duplicate nested success; non-generic wrapper adds message; Word/Quiz actions lack explicit response schema metadata. Swagger security requirement is global, including anonymous auth actions.
- **Risk:** source/OpenAPI consumers cannot rely on one schema; typed client generation would inherit incomplete/misleading metadata.
- **Direction:** name and type actual payloads, consolidate runtime wrappers without wire break first, document endpoint-specific responses and auth.
- **Breaking risk:** internal changes can be non-breaking; flattening envelopes or changing null omission is breaking.
- **Tests:** raw JSON property/null assertions and OpenAPI schemas/status/auth metadata for all actions.

### R7-F07 — Medium — Defaulted value types obscure missing request intent

- **Files:** UserVocabularyDTOs.cs; QuizDTOs.cs; WordsController.cs; QuizService.cs.
- **Current behavior:** `{}` favorite means false; omitted selectedOptionId means option zero; query/quiz values are silently normalized; validation is split across layers.
- **Risk:** incomplete requests can mutate state or score an unintended option; undocumented defaults surprise clients.
- **Direction:** specify required presence versus legitimate default for each active field; preserve omitted-question scoring intentionally; use appropriate presence/range checks after compatibility review.
- **Breaking risk:** stricter validation rejects previously accepted requests.
- **Tests:** omission/null/wrong type/zero/negative/max values; unknown mode; empty answer list versus missing selected option.

### R7-F08 — Medium — Add selection and normalization are ambiguous

- **Files:** Models/AddWordRequest.cs; WordService.cs `AddToVocabularyAsync`, `ResolvePartOfSpeechAsync`; word-lookup.component.ts; UserDTOs.cs/signup.component.ts.
- **Current behavior:** add ignores submitted definition/example/pronunciation; lookup trims word but add does not; absent/unknown POS falls back to Noun without guaranteeing a matching definition; username maximum differs in UI/backend.
- **Risk:** callers infer writes that do not occur; an added entry can lack a usable definition; equivalent-looking inputs behave differently.
- **Direction:** define canonical-reference/selection precedence, ignored-field compatibility and normalization; align username bounds. Preserve repeated-add short-circuit and canonical write prohibition.
- **Breaking risk:** removing fields/rejecting POS/changing normalization needs deliberate migration; no DB redesign needed.
- **Tests:** whitespace, POS name/abbreviation/unknown/missing, absent noun definition, preferred-ID precedence, repeated add with different selection, username limits.

### R7-F09 — Medium — Vocabulary search has inconsistent empty-response intent

- **Files:** WordsController.cs `SearchUserVocabulary`; UserVocabularyDTOs.cs; WordService.cs `SearchUserVocabularyAsync`.
- **Current behavior:** blank controller branch returns only words[], normal service response has paging fields; nonnullable required query may cause 400 before blank branch.
- **Risk:** response shape or empty-input behavior depends on binding rather than a documented contract.
- **Direction:** decide empty search behavior and return one typed shape; document five-result limit and count meaning.
- **Breaking risk:** standardizing empty/missing query behavior can change 400 versus 200.
- **Tests:** omitted, empty, whitespace, encoded term, no matches and >5 matches; exact metadata.

### R7-F10 — Medium — Vocabulary UI treats one page as complete collection

- **Files:** WordsController.cs `GetUserVocabulary`; WordService.cs; word-lookup.component.ts `loadVocabularyPage`, `filteredVocabularyWords`, letter-count helpers.
- **Current behavior:** fetches 1000 rows and locally filters/counts current page as a catalog.
- **Risk:** words on later pages appear absent from local search/letter availability; status/error can also collapse to an empty list.
- **Direction:** establish explicit page-local semantics or consume backend paging/filtering correctly; keep UX redesign separate.
- **Breaking risk:** coordinated frontend behavior; do not silently reduce existing page-size allowance.
- **Tests:** collection >1000, page boundaries, query encoding, empty versus failed list, totalCount/totalPages.

### R7-F11 — Medium — Transport typing hides response drift

- **Files:** api.service.ts; word-lookup.component.ts; word-lookup.model.ts; quiz.model.ts.
- **Current behavior:** any request bodies and lookup/search/list/update responses; casts bypass checking; optional fields omit actual null possibility; mutation handlers ignore response shape.
- **Risk:** incompatible payload changes compile and pass local rendering tests.
- **Direction:** separate typed transport DTOs from UI grouping models; type operation inputs/outputs and intentional null mappings; remove unsupported synonym/antonym assumptions from transport expectations.
- **Breaking risk:** internal TS errors may surface; preserve public payload initially.
- **Tests:** exact payload mapping, nullable fields, acknowledgement parsing, malformed success bodies.

### R7-F12 — Medium — Backend tests protect behavior more than wire contracts

- **Files:** Integration suites and Infrastructure/ApiTestClientHelper.cs.
- **Current behavior:** strong relational/security checks, partial deserialized shape checks; validate-token success and many validation/status boundaries untested.
- **Risk:** key-name/null/content-type/error changes can evade tests; inherited DTO defaults can mask missing fields.
- **Direction:** add raw JSON/header/schema assertions and targeted fault/boundary tests from section 14.
- **Breaking risk:** none to production; tests must distinguish current characterization from approved future semantics.
- **Tests:** all 14 endpoints represented, approved error classes, challenge header, null/date formats, OpenAPI and missing/invalid inputs.

### R7-F13 — Medium — Frontend contract test gaps

- **Files:** auth.service.spec.ts; api.service.spec.ts; login/signup specs; word-lookup.component.spec.ts; missing quiz spec.
- **Current behavior:** service/auth tests largely construction-only; useful word/R5 cases but few method/body/header assertions and no quiz tests.
- **Risk:** tests remain green after backend/client drift.
- **Direction:** cover each consumed operation with reviewed server-shaped fixtures and HttpTestingController request assertions.
- **Breaking risk:** none to wire; fixture updates must follow explicit contract decisions.
- **Tests:** auth session/error handling, all word operations, quiz start/submit/history, Bearer headers, status-specific failures.

### R7-F14 — Medium — Quiz temporal and retry contracts lack explicit definition

- **Files:** QuizService.cs; QuizDTOs.cs; quiz.model.ts; quiz.component.ts.
- **Current behavior:** 30-minute in-memory sessions, response expiry date, repeat submissions rejected, missing answers scored incorrect; history groups persisted results without exposing session IDs. Deployment/restart can lose active sessions before advertised expiry.
- **Risk:** clients infer retryability or expiry guarantees that process-local storage cannot provide; UTC wire interpretation is not pinned.
- **Direction:** document best-effort session lifetime/restart behavior and status/error codes; define date serialization and repeat-submit semantics. Persistent-session architecture is follow-up, not R7.
- **Breaking risk:** status/error/date changes need coordinated review; no scoring or counter redesign.
- **Tests:** expired/unknown/repeated/in-progress session, null selectedAnswer, history timestamps/order/take, state preserved after failed submit.

### R7-F15 — Low — Dormant DTOs and wrapper placeholders misrepresent capabilities

- **Files:** UserWordDTOs.cs; WordDTOs.cs `WordLookupRequest`; WordRequest.cs; ApiResponse.cs; ApiResult.cs unused throwing overload.
- **Current behavior:** unbound request/response classes imply unavailable operations; obsolete comments conflict with current entities.
- **Risk:** later developers select the wrong DTO or reintroduce old assumptions.
- **Direction:** mark/remove unused declarations after reference verification; keep live schema/entity cleanup outside R7.
- **Breaking risk:** normally none on wire; external code consumers unknown.
- **Tests:** reference/build checks and unchanged endpoint/schema inventory.

### R7-F16 — Low — API examples/documentation are stale

- **Files:** Docs/README.md; test-api.http; VocabularyApp.WebApi.http; historical Plan of Action.
- **Current behavior:** nonexistent routes, old provider and anonymous lookup claims persist.
- **Risk:** manual callers test the wrong endpoint or misunderstand authentication.
- **Direction:** future R7 documentation/example updates based on current inventory; historical documents should be labeled rather than rewriting roadmap priorities.
- **Breaking risk:** none; do not execute examples against production/provider during analysis.
- **Tests:** route/example cross-check against controller/OpenAPI inventory.

## 19. Risk Assessment

Highest priority is to prevent exception-detail disclosure and preserve the intended distinction between invalid requests and server/provider faults. Client error handling should be made tolerant before migrating server formats. No Critical finding is supported by the inspected evidence. Existing R3/R4/R5 guards remain valuable and must not be weakened to simplify contracts.

The main implementation risk is an apparently harmless wrapper or status cleanup breaking an older cached Angular bundle, a smoke check, or a regression test encoding current behavior. Secondary risks are changing optional/defaulted input semantics and confusing canonical IDs with saved-row IDs. Use additive wire changes and explicit compatibility decisions. Code-coverage percentage is not a measure of this matrix's completeness.

## 20. Required R7 Scope

- Establish reviewed request/success/error contracts for all 14 actions and exact tests for high-risk paths.
- Address F01–F04: safe error details, classified failures, provider malformed-response handling, compatible Angular parsing.
- Correct auth and active transport models/nullability; replace object/anonymous ambiguity with named types while initially preserving JSON structure.
- Resolve required-field/default/empty-search/normalization decisions for current operations and document R5 selection/duplicate behavior.
- Make pagination limits explicit and prevent collection-wide UI claims from silently representing only one page; keep broader search UX work separate.
- Add missing backend/frontend contract protection and accurate API metadata/examples as part of eventual implementation.
- Define quiz retry/expiry/date guarantees without implementing persistent sessions.

## 21. Recommended Follow-Up

Generated or schema-checked TypeScript clients; broader service/component decomposition; persistent quiz sessions; dedicated external-provider abstraction and concurrent cache-miss repair; SQL Server-specific verification in its own controlled test effort; richer pagination/filter UX; removal of dormant DTO/entity placeholders beyond active-contract needs. These should not be prerequisites for correcting error and DTO mismatches unless implementation discovers a concrete dependency.

## 22. Out-of-Scope Items

New vocabulary/study/AI features; delete/archive/notes/sample-sentence endpoints; major UI redesign; unrelated database/entity/migration changes; WordsAPI replacement; authentication-system replacement or token refresh architecture; infrastructure/CI/CD redesign; production probing; automatic deployment; changes to F1–F5 or future backlog priorities. R7 analysis authorizes none of these.

## 23. Proposed Implementation Sequence

1. **Approve contract decisions and compatibility scope.** Resolve section 25, publish endpoint schemas/error mappings, retain current route and R5 semantics. No coding is part of this analysis.
2. **Add focused characterization tests.** Record current raw auth/lookup/vocabulary/quiz JSON and challenge behavior, including faults currently classified incorrectly. Identify intentional future expectation changes rather than freezing defects permanently.
3. **Prepare Angular compatibility.** Add typed error adapter accepting existing formats and the agreed future format; correct auth/nullable transport models and cover actual request bodies/headers. Account for cached bundles.
4. **Correct failure semantics and provider validation.** Remove public exception text, distinguish internal failures, validate provider structures, preserve lookup 400/404/503 and resource-ownership safeguards. Update fault tests without dropping rollback assertions.
5. **Consolidate active typed contracts.** Name acknowledgement/lookup/service results, align request validation/defaults and search shape, annotate API schemas and authorization metadata. Avoid unnecessary success-envelope flattening or route changes.
6. **Complete consumer and boundary tests.** Cover paging scope, required properties, validate-token, quiz temporal/error behavior and all Angular operations. Confirm POS is selection, not identity.
7. **Run future verification.** Backend suite, frontend suite and production Angular build; API/OpenAPI contract checks; controlled review of unchanged deployment checks. SQL Server/IIS limitations remain explicit. Any production acceptance belongs to a separately authorized release.
8. **Record final contracts and compatibility evidence.** Update API documentation/examples, list deliberate status/shape changes, deferred items, and release acceptance. Do not change existing roadmap ordering.

## 24. Future Implementation Acceptance Criteria

- All backend tests pass, including preserved R3 authentication/canonical-write, R4 scoring/rollback and R5 identity/concurrency assertions; changed status expectations are explicitly reviewed.
- All Angular tests pass and production Angular build succeeds; each consumed route has request method/path/body/header and response/error fixtures.
- Inventory remains 14 actions with unchanged routes unless a separate change is explicitly approved; dormant DTOs do not become accidental endpoints.
- Every active response has a declared payload schema; raw JSON tests pin camelCase names, required presence, nullability and date format. OpenAPI describes actual success/error/auth behavior.
- Lookup invalid input yields 400, genuine provider not-found 404, and provider auth/rate-limit/network/timeout/malformed-response failures 503, including explicit null collections; no bad provider data is persisted.
- Unexpected internal failures produce the agreed server status and safe error body, never exception text or credential/provider secrets. Missing/invalid JWT remains 401 with Bearer challenge; ownership remains enforced with the approved concealment/status policy.
- Missing mutation fields, empty queries, paging bounds, quiz modes/counts and duplicate/retry behavior match documented decisions and have targeted tests.
- Angular auth models match actual token/user payloads; error explanations render correctly; nullable fields are handled; search/count scope is clear at pagination boundaries.
- One UserWord per `(UserId,WordId)` remains; repeated add returns stable identity without preference mutation; cross-POS preference updates preserve the row, counters, notes and dependents.
- No unintended schema/migration, provider replacement, password/JWT policy, IIS/HSTS/CORS, CI gate, deployment trigger, artifact, serialization or stale-release protection changes.
- Existing anonymous HTTPS profile acceptance remains compatible. Release-specific authenticated checks, if later authorized, use the final contracts without redefining CI policy.
- Documentation distinguishes measured tests from static review, SQLite from SQL Server, and test-host behavior from deployed IIS behavior.

## 25. Open Questions / Decisions Required

1. Use typed success envelopes plus ProblemDetails, or one consistent typed envelope? What migration window covers cached Angular and unknown external clients?
2. Which errors get stable machine codes, and should framework 401 bodies remain empty? Bearer headers/status must remain intact.
3. Use concealed 404 for absent/nonowned vocabulary/session IDs, explicit 403 for known ownership failures, or another documented policy? Decide duplicate-registration and quiz conflict statuses separately.
4. Which defaulted fields require explicit presence (favorite, selectedOptionId)? Which current defaults/clamps remain compatible? Preserve intentional unanswered-question scoring.
5. For legacy add without preferred ID, reject unsupported/missing POS, choose a deterministic available definition, or preserve fallback? What ignored-field deprecation is acceptable? Existing saved rows must remain idempotent.
6. What is the supported username/word normalization and length policy, without changing password bytes or database identity?
7. Should empty vocabulary search return a full empty envelope or 400, and what does count mean for autocomplete versus paging?
8. What exact timestamp representation is guaranteed for stored dates and quiz expiry/history? Can all current stores supply it without schema changes?
9. Are there external clients beyond the Angular app and checked-in manual examples? Unknown usage blocks assuming wire changes are private.
10. What minimal pagination correction belongs in R7 versus the existing later search/UI work? Avoid expanding into a full component rewrite.

These are future implementation decisions, not a request to begin remediation now.

## 26. Final Assessment

R7 is justified by concrete contract drift and classification defects, not a lack of working endpoints. The relational integration foundation and preserved R5 behavior make a staged correction feasible. Prioritize safe classified errors, Angular compatibility, provider payload validation and exact contract tests; preserve routes, authentication, persistence invariants and deployment controls.

This task produces only `Docs/Updates/R7-api-contracts-remediation-analysis.md`. No production code or tests were modified, no tests/builds were run for R7, and no commit, push, merge or deployment was performed. The earlier backlog remains a pre-existing untracked item. The coverage artifact recorded at entry was absent at final verification; this task did not edit or remove it. Final Git status shows only the backlog and this analysis as untracked, with no tracked-file changes.
