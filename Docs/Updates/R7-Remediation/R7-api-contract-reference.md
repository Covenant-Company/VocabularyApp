# R7 API contract reference

Status: **R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED**. Local verification completed in [Phase 8](R7-api-contracts-phase-8-verification-results.md): 454 backend tests, 193 Angular tests and both Release/production builds passed. Production release and deployment remain separately authorized.

## 1. Purpose

Developer reference for the public contracts remediated by R7 Phases 1–7. The approved [implementation plan](R7-api-contracts-remediation-implementation-plan.md), current controllers, DTOs and services define the scope. Historical analysis/results remain historical evidence.

## 2. Scope

The surface contains exactly 14 actions: five users/auth, six words/vocabulary and three quiz actions. There is no profile-update, word-delete, weatherforecast, `/words/search/{term}` or `/words/definitions/{word}` action. No new endpoints or wire-field renames were introduced in Phase 7.

## 3. Contract conventions

Routes are under `/api`. JSON uses camelCase. Successful actions return HTTP 200, including registration, duplicate add and updates. DTO names below are C# names, not extra JSON nesting. Request binding/validation and application failures are intentionally different contracts. Route-constraint misses, unsupported content types and other framework responses are outside the application error envelope; do not assume every non-200 body is JSON.

## 4. Authentication convention

Register and login are anonymous. All other 12 actions require `Authorization: Bearer <token>`, including lookup, which is secured by the existing fallback authorization policy. OpenAPI keeps the existing Bearer definition and gives anonymous operations an explicit empty security requirement override (`security: [{}]`) that survives serialization. Protected operations declare Bearer security.

Unauthenticated/expired/invalid-token requests may be challenged before the controller: framework HTTP 401 with an empty body and Bearer challenge header. A controller rejecting an authenticated principal uses the application envelope and its stable code. These two responses are not interchangeable. No application-defined 403 response was added.

## 5. Successful response convention

User register/login/profile/validate-token preserve `ApiResult<T>`: `{ "success": true, "data": <typed value>, "error": null }`. Register/login data is `AuthResponse`: `{ "success": true, "errorMessage": null, "user": <UserDto>, "token": "<token>" }`. There is no `expiresAt` member. Controllers guard against missing successful data/user/token.

Password change preserves the separate non-generic `ApiResult`: `{ "success": true, "message": "Request succeeded.", "data": null, "error": null }`.

Word/vocabulary/quiz successes use `SuccessResponse<T>`: `{ "success": true, "data": <typed value> }`, with no success-level error/message fields. Existing inner lookup flags remain; successful envelopes have not been unified.

## 6. Application-error envelope

`ApiErrorResponse`, content type `application/json`, has exactly six present fields:

```json
{
  "success": false,
  "data": null,
  "error": "An internal error occurred. Please try again.",
  "message": "An internal error occurred. Please try again.",
  "code": "internal_error",
  "traceId": "<server-generated-trace-id>"
}
```

`message` equals `error`. Only allowlisted status/code/message combinations are public; unknown or inconsistent service failures become safe HTTP 500 `internal_error`. Exception, database, provider and arbitrary service text are not returned.

## 7. Validation-error convention

MVC binding and validation return HTTP 400, `application/problem+json`, extended `ValidationProblemDetails`. Standard fields include `type`, `title`, `status` and `errors`; optional standard `detail`/`instance` members may occur. R7 extensions are exactly `traceId`, `success: false`, `data: null`, `error`, `message` and `code: "validation_failed"`. Both text extensions are `One or more request fields are invalid.`

`errors` maps safe field names to arrays of safe messages. Known request-property/query names and bounded array indices are preserved; unknown paths become `$`. Binding/deserialization text that might expose submitted values or internal type information is replaced with safe text. Required-property failures follow this same format. This is not the six-field application envelope; OpenAPI advertises both media types where the action can also business-reject with 400. Unsupported media type 415 remains framework behavior.

## 8. Trace ID behavior

Application errors and validation failures use server `HttpContext.TraceIdentifier`. Caller-provided trace/correlation headers are not echoed into the contract. Treat the identifier as an opaque string for correlation, not as input validation or a retry token.

## 9. 14-endpoint contract matrix

Abbreviations: **AE** = six-field application/json error; **VE** = extended application/problem+json validation error (`validation_failed`); **BC** = empty framework Bearer 401 challenge; **S<T>** = `SuccessResponse<T>`; **G<T>** = `ApiResult<T>`. Every row's success status is **200**. Protected 401 entries include BC; lookup has BC only. All application 500 entries are `internal_error`.

| ID / method / route | Auth | Request | 200 response | Validation / application errors and codes | Angular consumer | Compatibility |
|---|---|---|---|---|---|---|
| E01 POST `/api/users/register` | Anonymous | `CreateUserRequest` body | `G<AuthResponse>` | VE 400; AE 400 `username_taken`, `email_taken`; AE 500 | `AuthService.register` / registration UI | Nested auth flags; nullable registration lastLoginAt; no expiresAt |
| E02 POST `/api/users/login` | Anonymous | `LoginRequest` body | `G<AuthResponse>` | VE 400; AE 401 `invalid_credentials`; AE 409 `credentials_changed`; AE 500 | `AuthService.login` / login UI | Nested auth; token/storage only after guards |
| E03 GET `/api/users/profile` | Bearer | None | `G<UserDto>` | BC; AE 401 `invalid_token`; AE 404 `user_not_found`; AE 500 | No active production caller | Read-only profile; no PUT profile |
| E04 POST `/api/users/change-password` | Bearer | `ChangePasswordRequest` body | Non-generic `ApiResult` | VE 400; BC; AE 401 `invalid_token`, `user_unavailable`, `current_password_incorrect`; AE 409 `credentials_changed`; AE 500 | No active production caller | Legacy success has null data/error, message present |
| E05 GET `/api/users/validate-token` | Bearer | None | `G<UserDto>` | BC; AE 401 `invalid_token`, `user_unavailable`; AE 500 | No active production caller | Does not issue another token |
| E06 GET `/api/words/lookup/{word}` | Bearer fallback | String path word | `S<WordLookupResponse>` | VE 400; AE 400 `invalid_request`; BC; AE 404 `word_not_found`; AE 503 `dictionary_unavailable`; AE 500 | `WordLookupComponent` via typed `ApiService.get` | Inner success/errorMessage flags and cache/membership remain |
| E07 POST `/api/words/vocabulary/add` | Bearer | `AddWordRequest` body | `S<AddToVocabularyResultDto>` | VE 400; AE 400 `invalid_request`, `canonical_word_required`, `invalid_preferred_definition`; BC; AE 401 `invalid_token`; AE 500 | `WordLookupComponent` via typed `ApiService.post` | Existing canonical word; duplicate success; legacy fields retained |
| E08 GET `/api/words/vocabulary` | Bearer | Queries page=1, pageSize=20, term?, startsWithLetter? | `S<UserVocabularyResponseDto>` | Integer-binding VE 400; BC; AE 401 `invalid_token`; AE 500 | `WordLookupComponent` via typed `ApiService.get` | Bounds normalize rather than reject |
| E09 GET `/api/words/vocabulary/search` | Bearer | Optional query term | `S<UserVocabularyResponseDto>` | BC; AE 401 `invalid_token`; AE 500 | `WordLookupComponent` via typed `ApiService.get` | Full empty response for absent/blank term; at most five items |
| E10 PUT `/api/words/vocabulary/{userWordId:int}/favorite` | Bearer | Int route, `UpdateFavoriteRequestDto` body | `S<FavoriteUpdateResponseDto>` | VE 400 including omitted isFavorite; BC; AE 401 `invalid_token`; AE 404 `vocabulary_not_found`; AE 500 | `WordLookupComponent` via typed `ApiService.put` | false valid; missing/nonowned indistinguishable |
| E11 PUT `/api/words/vocabulary/{userWordId:int}/preferred-definition` | Bearer | Int route, `UpdatePreferredDefinitionRequestDto` body | `S<PreferredDefinitionUpdateResponseDto>` | VE 400; AE 400 `invalid_preferred_definition`; BC; AE 401 `invalid_token`; AE 404 `vocabulary_not_found`; AE 500 | `WordLookupComponent` via typed `ApiService.put` | Same row, synchronized derived POS; integer route miss is routing behavior |
| E12 POST `/api/quiz/start` | Bearer | `StartQuizRequestDto` body | `S<QuizStartResponseDto>` | VE 400; AE 400 `quiz_unavailable`; BC; AE 401 `invalid_token`; AE 500 | `QuizComponent` via typed `ApiService.post` | Defaults count 10/mode mixed; no answer key |
| E13 POST `/api/quiz/submit` | Bearer | `QuizSubmitRequestDto` body | `S<QuizSubmitResponseDto>` | VE 400; AE 400 `invalid_request`, `invalid_quiz_answers`; BC; AE 401 `invalid_token`; AE 404 `quiz_session_unavailable`; AE 409 `quiz_submission_conflict`, `quiz_vocabulary_changed`; AE 500 | `QuizComponent` via typed `ApiService.post` | Explicit selectedOptionId; omitted/empty answers permitted; no auto retry |
| E14 GET `/api/quiz/history` | Bearer | Query int take=5 | `S<QuizHistoryResponseDto>` | Integer-binding VE 400; BC; AE 401 `invalid_token`; AE 500 | `QuizComponent` via typed `ApiService.get` | At most 20 sessions; items have no sessionId |

E09 has only an optional string query, so it does not advertise an invented integer-binding/business 400. Framework routing, media-type and authorization responses are not mechanically listed on every operation.

## 10. User/auth contracts

Registration requires username (length 3–100), valid email (maximum 200) and password (length 6–100). Login requires username/password, with no registration-length restriction added to login. Password change requires currentPassword and newPassword (6–100). Missing/null body, invalid JSON and annotation failures use VE.

`UserDto` fields: `id` integer, `username` string, `email` string, `createdAt` date string and `lastLoginAt` nullable date string. Register/login's successful inner `user` and `token` are non-null by controller guards even though the reusable `AuthResponse` DTO permits null values for unsuccessful construction. Profile/validate-token return UserDto directly inside the generic outer data field. Preserve established nested success flags and null error fields.

## 11. Word lookup contract

Lookup trims its lookup term and consults the canonical database first. Data is `WordLookupResponse`: `success`, `errorMessage` (nullable), `word` (nullable DTO declaration, non-null on 200), `wasFoundInCache` and `isInUserVocabulary`. A word includes `id`, `text`, nullable `pronunciation`/`audioUrl`, `createdAt` and `definitions` array. Definitions include `id`, `definition`, nullable `example`, `partOfSpeech`, `partOfSpeechAbbreviation` and `displayOrder`.

Public consumers receive canonical data, not a provider transport object or provider diagnostics. Genuine missing definitions produce 404; unavailable/untrustworthy provider responses produce 503. Local persistence faults remain safe 500.

## 12. Vocabulary contracts

Add accepts `word`, nullable optional `preferredWordDefinitionId`, and retained nullable compatibility fields `definition`, `example`, `pronunciation`, `partOfSpeech`. Nonblank word is a business requirement, not newly enforced with binding annotations. Add uses an exact existing canonical database word; it neither calls the provider nor accepts client-authored definitions. If no canonical word exists, lookup first; `canonical_word_required` is returned.

Definition/example/pronunciation are accepted and ignored, deprecated in OpenAPI. POS remains an active selection fallback when preferred ID is absent. Explicit preferred ID must belong to that word on a new row; duplicate lookup occurs before selection validation. Add returns `userWordId`, `wordId`, `alreadyExisted`, `message`. Duplicate success leaves counters, favorite, preferred definition and identity unchanged.

Favorite request: `{ "isFavorite": false }`; presence is enforced by `[JsonRequired]`. Omission/null/type mismatch is VE 400, not an implicit false update. Acknowledgement: `message`, `userWordId`, `isFavorite`.

Preferred request: `{ "preferredWordDefinitionId": <integer> }`. Omission defaults to zero and is business-rejected, as are nonpositive/wrong-word selections. Invalid integer binding is VE. Acknowledgement: `message`, `userWordId`, `preferredWordDefinitionId`. Missing/nonowned rows are concealed with the same 404 code. A valid cross-POS selection updates the same UserWord row and its synchronized derived POS.

List/search data always has `words`, `totalCount`, `page`, `pageSize`, `totalPages`. Each item has `id`, `word`, `definition`, nullable `preferredWordDefinitionId`, nullable `example`, `partOfSpeech`, nullable `pronunciation`/`audioUrl`, `addedAt`, `isFavorite`, nullable `personalNotes`, `correctAnswers`, `totalAttempts`, nullable `accuracyRate` (null when no attempts).

List page below 1 becomes 1; pageSize outside 1–10000 becomes 20. Optional term is trimmed and matches word/definitions/examples; startsWithLetter uses the trimmed first character. List ordering is word order and totalCount counts all filtered rows. Search ranks exact/prefix/contains word, definition and example matches, then word; returns at most five rows, with totalCount equal to returned rows, page=1/pageSize=5. Absent/empty/whitespace search returns `words: []`, totalCount=0, page=1, pageSize=5, totalPages=0.

## 13. Quiz contracts

Start defaults omitted questionCount to 10 and mode to mixed. Nonpositive count normalizes to 10, then clamps to 1–20; actual count may be lower than requested. Mode trims/case-normalizes: `word-to-definition`, `definition-to-word`, otherwise `mixed`. Explicit null mode fails MVC non-nullable validation. Insufficient usable distinct vocabulary is `quiz_unavailable` 400.

Start data has `sessionId` GUID string, `mode`, `questionCount`, `expiresAtUtc` date string and `questions`. Questions have `questionId` GUID string, `questionType`, `prompt`, `options`; options have `optionId` integer and `text`. Correct answers are not included.

Submit accepts `sessionId` and `answers`. Supplied answers have `questionId` and explicitly required `selectedOptionId` integer 0–3: zero is valid, omission is VE, explicit null/type mismatch is VE. Omitted answers initializes to []; [] is valid and all questions are unanswered/incorrect. Explicit null collection is rejected by MVC; null elements, duplicate/foreign question IDs and out-of-range options are safely rejected with `invalid_quiz_answers` before mutation. Empty sessionId is business `invalid_request`.

Submit data has `totalQuestions`, `correctAnswers`, `scorePercentage` (rounded to two decimals), `questionResults`. Each result has `questionId`, `questionType`, `prompt`, `correctAnswer`, nullable `selectedAnswer` and `isCorrect`. Unanswered selectedAnswer is null. Submit response does not include sessionId.

History data has `items`. Each item has `attemptedAtUtc`, `totalQuestions`, `correctAnswers`, `scorePercentage`; no sessionId. Sessions are ordered most recent first. Omitted/nonpositive take uses 5; maximum is 20. Numeric bounds normalize; malformed integers are VE.

## 14. Stable error-code catalog

The complete active application allowlist is below. All entries use AE; validation_failed is separately listed as VE. The endpoint matrix narrows each entry's use. Public messages are centralized in `ApiErrorResults` and never copied from arbitrary exceptions.

| Code | HTTP | Meaning | Endpoints/categories |
|---|---|---|---|
| username_taken | 400 | Username already registered | E01 |
| email_taken | 400 | Email already registered | E01 |
| invalid_request | 400 | Invalid business request, such as blank word or empty quiz session ID | E06, E07, E13 |
| canonical_word_required | 400 | Canonical word must exist before add | E07 |
| invalid_preferred_definition | 400 | Invalid definition selection for the canonical word | E07, E11 |
| quiz_unavailable | 400 | Not enough usable vocabulary to create a quiz | E12 |
| invalid_quiz_answers | 400 | Invalid answer set/question/option | E13 |
| invalid_credentials | 401 | Username/password authentication failed | E02 |
| invalid_token | 401 | Authenticated principal lacks usable user ID | E03–E05, E07–E14 |
| user_unavailable | 401 | Authenticated account no longer available | E04, E05 |
| current_password_incorrect | 401 | Current password check failed | E04 |
| user_not_found | 404 | Profile account not found | E03 |
| word_not_found | 404 | Genuine dictionary miss/no definitions | E06 |
| vocabulary_not_found | 404 | Missing or nonowned vocabulary entry | E10, E11 |
| quiz_session_unavailable | 404 | Unknown, expired, removed or nonowned session | E13 |
| credentials_changed | 409 | Credentials changed during guarded credential operation | E02, E04 |
| quiz_submission_conflict | 409 | Concurrent/in-progress submission or recognized already-persisted submission | E13 |
| quiz_vocabulary_changed | 409 | Quiz vocabulary disappeared before/during scoring | E13 |
| dictionary_unavailable | 503 | Provider unavailable or response untrustworthy | E06 |
| internal_error | 500 | Safe fallback/internal failure | All 14 actions |
| validation_failed | 400 (VE) | Sanitized binding/annotation/required-property errors | Actions with body or integer binding; see matrix |

## 15. Nullability and required-property conventions

OpenAPI required property means the key is present, independently of nullable values. Response DTOs serialize their declared properties, including explicit nulls; schemas mark their keys present. `SupportNonNullableReferenceTypes` and reference extension schemas preserve declared nullability. Reusable nullable AuthResponse/WordLookupResponse properties remain nullable in their component schemas; successful controllers impose the stronger non-null guard described above.

Input absence is not universally equivalent to null: start properties and answer collection have defaults; optional add fields are nullable; favorite and selectedOptionId have explicit `[JsonRequired]` presence semantics. OpenAPI schema metadata mirrors those attributes without adding validation annotations. Valid preferred-definition IDs are a business constraint, with omission defaulting to zero before rejection. No global rule makes every non-nullable CLR input property wire-required.

## 16. Date/time wire conventions

C# DateTime values serialize as JSON date/time strings; Angular transport types use strings. `lastLoginAt` is present and nullable. Quiz fields retain UTC-oriented names, but no global date conversion, DateTimeKind correction or promise that every database timestamp carries a normalized UTC suffix was introduced. Consumers must preserve current JSON values; Date objects belong in an explicit mapping layer if later needed.

## 17. R5 UserWord identity rule

Identity is **(UserId, WordId)**. PartOfSpeechId remains synchronized derived state, not a third identity component. Multiple selected definitions/POS do not create separate UserWord entries. Duplicate add and concurrent recognized unique-row races return the existing pair. Dormant pre-R5 DTOs were removed; database mappings/indexes were not modified.

## 18. Provider boundary behavior

Provider transport DTOs are internal integration types, not public endpoint schemas. Typed decoding validates collections and items before tracking canonical entities. Genuine provider 404 is a dictionary miss. Non-success responses other than a genuine miss, transport failures/timeouts, malformed JSON, structurally invalid collections and untrustworthy data produce safe 503. Unsupported POS/blank definitions are filtered; structural corruption is not silently accepted. Nullable audio remains supported. Valid provider data followed by local database failure is safe 500. No upstream response body, credential, header or raw diagnostic becomes public error text.

## 19. Quiz session/error semantics

Sessions live in process-local memory for 30 minutes; they are not durable across process restarts or shared across instances. Unknown/nonowned/expired/removed sessions share 404 `quiz_session_unavailable`.

An in-progress/concurrent claim or a recognized persisted duplicate gives 409 `quiz_submission_conflict`. Successfully completed sessions are removed; a later replay after removal therefore yields 404, not a guarantee of permanent 409. Missing vocabulary before scoring or the guarded transactional recheck gives 409 `quiz_vocabulary_changed`. Invalid answers are rejected before mutation.

Existing lock, relational transaction, execution-strategy, recheck, rollback and tracker cleanup protect counters/results from duplicate or partial mutation. A successful submission increments attempts once for each question, including unanswered questions. Failed submissions do not authorize automatic retry; Angular does not automatically retry. Durable cross-instance coordination and response replay are deferred.

## 20. Angular consumer compatibility

`AuthService` uses typed HttpClient auth calls and projects guarded auth fields before storage. `ApiService` uses typed GET/POST/PUT response/request parameters and adds the existing Bearer header when available. `WordLookupComponent` and `QuizComponent` consume the named transport models; lookup/search URL parameters are encoded. The DELETE helper does not imply an active DELETE API endpoint.

Generic Angular ApiResponse models tolerate nullable data/error/message for the intentionally different envelopes. Error handling reads stable codes/safe messages through the shared error channel and falls back for empty framework bodies. Dates remain string transport values. No active `any` remained at these remediated boundaries requiring Phase 7 replacement; unrelated UI typing is outside scope.

## 21. Compatibility/deprecation notes

The approved plan sections 14 and 27 explicitly retain ignored add fields and defer removal beyond R7. Phase 5 results suggested later cleanup in Phase 7; the approved plan controls and these fields remain. Definition/example/pronunciation are documented as ignored/deprecated; POS is still active and is not deprecated or removed. Angular's retained payload fields remain accepted. Removal needs a separately approved compatibility change.

`ApiResult<T>` moved from the controller namespace into DTOs, and non-generic ApiResult now uses the DTO namespace. JSON/factories remain unchanged. Source-level imports must use DTOs. Dormant `UserWordDto`, `AddWordToCollectionRequest`, `UpdateUserWordRequest`, `UserWordCollectionResponse`, `WordLookupRequest`, `WordRequest`, `DefinitionDto`, the commented ApiResponse artifact and the unimplemented ErrorResult(object) overload are removed. None had active production/test callers after static reference review.

## 22. Known intentional limitations

No universal success envelope, universal ProblemDetails conversion, framework challenge JSON normalization, generated client or automatic retry protocol. Search count describes the bounded returned set. Query bounds often normalize rather than reject. Quiz sessions are process-local. Date/time transport remains as implemented. Generated Swagger metadata was verified in Phase 8 through the isolated TestServer, including all 14 actions and anonymous/protected security overrides.

## 23. Deferred improvements

Beyond R7: removal of retained ignored add fields, broader request normalization, durable quiz/session coordination, global UTC normalization, generated clients, broad UI typing/refactoring, provider architecture/cache concurrency changes and SQL Server infrastructure verification. Phase 8 completed the prescribed local restore/build/test/Angular verification. Local publish was not required by the approved plan; packaging and deployment remain separate release work.

## 24. Verification status and handoff

At Phase 7 publication, static inspection traced all 14 actions through requests, services, response DTOs, error mapping, Swagger source metadata, Angular consumers and authored tests. Execution verification was then deferred to Phase 8. No tests, builds, restores, installs, startup, requests, provider/database calls, Swagger generation or deployment occurred during Phase 7.

[Phase 8](R7-api-contracts-phase-8-verification-results.md) subsequently completed execution verification after recorded narrow fixes. Final results: **454 backend tests passed, 0 failed/skipped; 193 Angular tests passed, 0 failed/skipped; Release solution build and Angular production build passed**. The full suite includes 33 metadata cases and 2 generated OpenAPI tests. Anonymous metadata uses `security: [{}]` because the existing serializer omits an empty security list; runtime authentication is unchanged.

Existing npm dependencies report 91 vulnerabilities (including 4 critical); informational analyzer/template/style warnings remain documented for review. R7 is ready for source review and a separately authorized commit/release decision. Production/IIS/SQL Server acceptance, exact CI toolchain parity, artifact publication and deployment are not established by these local results. No production access, live provider calls, commit, push or deployment occurred.
