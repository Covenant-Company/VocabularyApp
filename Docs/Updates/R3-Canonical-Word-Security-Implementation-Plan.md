# R3 Canonical Word Security Implementation Plan

## 1. R3 Executive Summary

R3 closes two related security exposures in Vocabulary Builder:

1. application/business API endpoints are not secure by default, leaving dictionary lookup anonymously reachable; and
2. `POST /api/words/add` permits callers to submit words and definitions directly to the shared canonical dictionary.

The authoritative product policy is:

> Registration and login are the only anonymous API endpoints. Every other application/business API endpoint must require authentication.

and:

> Users must never directly write canonical words or canonical definitions. Canonical dictionary data may only be created from trusted external dictionary API responses.

R3 will implement this policy incrementally. Phase 1 is already complete: integration tests define the intended authorization and canonical-write contract and establish an intentional RED baseline of **131 passed and 3 failed**. Phase 2 will make authentication secure by default while explicitly allowing anonymous registration and login. Phase 3 will remove the direct canonical-write route and its dedicated service operation. Phase 4 will finalize documentation and developer-run regression validation.

R3 builds on the existing JWT authentication established and hardened by R1. It does not redesign JWT configuration. It also preserves the R2 password hashing, legacy migration, persistence, concurrency, and logging behavior. No database, Angular, secret, environment-variable, or hosting change is expected.

## 2. Original Finding

The original Plan of Action defined R3 as **Secure or remove the public canonical word-write endpoint**. Repository analysis confirmed that:

- `POST /api/words/add` is routed by `WordsController.AddWord` without authorization;
- the action accepts caller-controlled `AddWordRequest` word and definition content;
- `WordService.AddWordAsync` creates shared `Word` and `WordDefinition` records from that content;
- the controller ignores `ServiceResult.IsSuccess` and reports HTTP 200 success after service failure;
- the operation can commit a word before a later definition write fails;
- the application has no administrator role or claim model; and
- an integration test characterized anonymous canonical mutation as current behavior.

The analysis also identified that `GET /api/words/lookup/{word}` was public and that a provider-backed cache miss creates canonical words and definitions. The initial analysis treated the lookup behavior as a scope question because its canonical definition content comes from the external dictionary provider, not the caller.

The product owner subsequently resolved that question and broadened R3. The original R3 analysis remains useful as the technical discovery record, but the later product-owner security decision supersedes any conflicting scope conclusions. In particular, the earlier suggestion that public dictionary lookup might remain outside R3 is no longer valid. Dictionary lookup must require authentication.

## 3. Authoritative Security Policy

The following rules govern implementation and take precedence over the narrower original interpretation:

1. `POST /api/users/register` remains anonymously accessible.
2. `POST /api/users/login` remains anonymously accessible.
3. Every other application/business API endpoint requires authentication.
4. Authentication must be secure by default for present and future endpoints.
5. Anonymous access must be an explicit exception, not an accidental result of omitted metadata.
6. `GET /api/words/lookup/{word}` requires authentication.
7. `POST /api/words/add` is removed entirely rather than protected for administrators.
8. Authentication does not grant direct canonical-authoring permission.
9. Authenticated lookup may populate the canonical cache only from a trusted external dictionary response.
10. The authenticated caller may choose a lookup term but may not supply canonical definition content.

No password-reset initiation/completion, email-verification, account-confirmation, or similar authentication-bootstrap endpoint exists in the current controllers. Therefore, R3 has no additional anonymous exception requiring a product decision. If such an endpoint is added before Phase 2 is implemented, it must be explicitly reviewed and added to the approved exception list before receiving anonymous access.

## 4. Scope

R3 includes:

- preserving anonymous registration and login;
- requiring authentication for all other API controller actions;
- selecting a secure-by-default ASP.NET Core authorization mechanism;
- explicitly marking the two approved anonymous actions;
- protecting dictionary lookup from anonymous requests;
- preserving authenticated cache-hit and provider-backed cache-miss lookup behavior;
- removing `POST /api/words/add`;
- removing `WordsController.AddWord`;
- removing `IWordService.AddWordAsync`;
- removing `WordService.AddWordAsync`;
- retaining shared request models and helpers used by personal vocabulary operations;
- inventorying every remaining `Word` and `WordDefinition` creation path;
- proving that remaining canonical definition writes use provider response data rather than caller-authored canonical content;
- retaining and satisfying the Phase 1 security-contract tests; and
- documenting and manually validating the final policy.

## 5. Out of Scope

R3 does not include:

- an administrator role, claim, policy, user flag, or administration UI;
- a replacement canonical-entry endpoint;
- redesigning the external dictionary provider or cache architecture;
- changing dictionary provider error mapping;
- general API response-contract standardization assigned to R7;
- centralized validation/exception handling assigned to R8;
- concurrent dictionary cache-miss redesign assigned to R13;
- rate limiting;
- refresh tokens, revocation, or cookie authentication;
- Angular authentication refactoring;
- `UserWord` identity changes;
- quiz behavior changes;
- database cleanup for any historically polluted canonical data;
- JWT secret rotation or token-validation redesign;
- password hashing or legacy migration changes; or
- unrelated source cleanup.

## 6. Current Architecture

ASP.NET Core startup in `VocabularyApp.WebApi/Program.cs` currently:

- binds and validates `JwtSettings`;
- registers JWT bearer authentication;
- calls `AddAuthorization()` without a fallback policy;
- applies `UseAuthentication()` and `UseAuthorization()` before `MapControllers()`; and
- relies on controller/action `[Authorize]` attributes to require authentication.

This produces an opt-in authorization model: an endpoint is anonymous when a developer omits `[Authorize]`. Most existing user-owned vocabulary actions use action-level `[Authorize]`, and `QuizController` uses controller-level `[Authorize]`. `UsersController` protects profile, password change, and token validation individually. However, `WordsController.LookupWord` and `WordsController.AddWord` have no authorization metadata.

Current canonical lookup flow:

```text
Anonymous or authenticated caller
        |
        v
GET /api/words/lookup/{word}
        |
        v
WordsController.LookupWord
        |
        v
WordService.LookupWordAsync
        |
        +--> Cache hit: return existing Word/WordDefinition data
        |
        +--> Cache miss: call dictionaryapi.dev
                         |
                         v
                  map provider response
                         |
                         v
                  persist Word/WordDefinition
```

Current prohibited direct-write flow:

```text
Caller-authored AddWordRequest
        |
        v
POST /api/words/add
        |
        v
WordsController.AddWord
        |
        v
WordService.AddWordAsync
        |
        v
canonical Word/WordDefinition rows
```

## 7. Target Architecture

Authorization will become opt-out rather than opt-in:

```text
New API endpoint
      |
      v
ASP.NET Core authorization fallback policy
      |
      v
Authenticated user required automatically
```

Only explicitly approved bootstrap actions bypass that default:

```text
POST /api/users/register -- explicit anonymous exception
POST /api/users/login    -- explicit anonymous exception
```

The preferred implementation is an ASP.NET Core authorization fallback policy requiring an authenticated user, configured through `AddAuthorization`. Registration and login receive `[AllowAnonymous]`. Existing `[Authorize]` attributes may remain for clarity; removing them has no R3 value and would create unnecessary diff surface.

This architecture is preferred over adding `[Authorize]` only to `LookupWord` because it:

- satisfies the product policy for all current endpoints;
- protects future endpoints automatically;
- makes anonymous access visible and reviewable;
- works with the existing JWT bearer middleware; and
- avoids controller-by-controller dependence on developer memory.

No authentication handler, JWT validation parameter, token claim, or credential behavior needs to change.

## 8. Canonical Dictionary Trust Boundary

### Prohibited direct canonical mutation

No caller, including an authenticated caller, may provide canonical word or definition content for direct persistence:

```text
Caller
   |
   v
caller-authored word/definition
   |
   X
canonical repository
```

Removing the route and dedicated service method is stronger and narrower than adding authorization. It eliminates the capability instead of granting it to every authenticated user or inventing an administrator system.

### Permitted provider-backed canonical population

The existing lookup/cache design remains intentional:

```text
Authenticated user
       |
       v
dictionary lookup term
       |
       v
canonical cache hit?
    /             \
  yes              no
   |                |
return cached   external dictionary API
data               |
                   v
             trusted provider response
                   |
                   v
             map and persist canonical
               Word/WordDefinition
                   |
                   v
                response
```

The trust distinction is content provenance:

- the user supplies only the lookup term;
- provider response fields supply canonical word metadata and definition content;
- authenticated lookup is the trigger;
- `WordService.LookupWordAsync` is the current persistence boundary; and
- R3 must not introduce another caller-authored canonical path.

Phase 3 must perform a repository-wide write-path inspection after removal. Any remaining arbitrary caller-authored canonical path is a blocker and must be reported rather than accepted silently.

## 9. Endpoint Security Classification

The Phase 1 inventory found three controllers and fifteen actions.

| Controller | Method and route | R3 classification | Current production state |
| --- | --- | --- | --- |
| `UsersController` | `Register` - `POST /api/users/register` | Anonymous by design | Anonymous |
| `UsersController` | `Login` - `POST /api/users/login` | Anonymous by design | Anonymous |
| `UsersController` | `GetProfile` - `GET /api/users/profile` | Authentication required | `[Authorize]` |
| `UsersController` | `ChangePassword` - `POST /api/users/change-password` | Authentication required | `[Authorize]` |
| `UsersController` | `ValidateToken` - `GET /api/users/validate-token` | Authentication required | `[Authorize]` |
| `WordsController` | `LookupWord` - `GET /api/words/lookup/{word}` | Authentication required | Currently anonymous |
| `WordsController` | `AddWord` - `POST /api/words/add` | Must be removed | Currently anonymous |
| `WordsController` | `AddToVocabulary` - `POST /api/words/vocabulary/add` | Authentication required | `[Authorize]` |
| `WordsController` | `GetUserVocabulary` - `GET /api/words/vocabulary` | Authentication required | `[Authorize]` |
| `WordsController` | `SearchUserVocabulary` - `GET /api/words/vocabulary/search` | Authentication required | `[Authorize]` |
| `WordsController` | `SetFavorite` - `PUT /api/words/vocabulary/{userWordId}/favorite` | Authentication required | `[Authorize]` |
| `WordsController` | `SetPreferredDefinition` - `PUT /api/words/vocabulary/{userWordId}/preferred-definition` | Authentication required | `[Authorize]` |
| `QuizController` | `StartQuiz` - `POST /api/quiz/start` | Authentication required | Controller `[Authorize]` |
| `QuizController` | `SubmitQuiz` - `POST /api/quiz/submit` | Authentication required | Controller `[Authorize]` |
| `QuizController` | `GetQuizHistory` - `GET /api/quiz/history` | Authentication required | Controller `[Authorize]` |

There are twelve authentication-required actions, two anonymous-by-design actions, and one action that must be removed.

## 10. Phase 1 - Establish Security Contract

**Status: COMPLETE**

### Purpose

Define the future R3 policy in integration tests before changing production behavior. The tests are intentionally stronger than current production code and provide a RED baseline for Phases 2 and 3.

### Added

`VocabularyApp.WebApi.Tests/Integration/R3SecurityContractApiTests.cs`:

- classifies every discovered controller action as `AnonymousByDesign`, `AuthenticationRequired`, or `MustBeRemoved`;
- asserts all twelve application/business routes reject anonymous requests with HTTP 401;
- proves anonymous registration and login remain reachable;
- asserts anonymous dictionary lookup cannot contact the provider or persist canonical data; and
- fails the classification inventory if a new controller action is added without an explicit R3 classification.

### Updated

`VocabularyApp.WebApi.Tests/Integration/DictionaryLookupApiTests.cs`:

- cache-hit, provider-success, provider-not-found, provider-failure, and unknown-part-of-speech tests now use authenticated API clients;
- provider-backed canonical population remains covered through the real HTTP pipeline and controlled provider boundary.

`VocabularyApp.WebApi.Tests/Integration/VocabularyOwnershipApiTests.cs`:

- the former `CanonicalWordAddIsCurrentlyAnonymousAndWritesSharedData` characterization was converted rather than deleted;
- `DirectCanonicalWordAddIsUnavailableToAnonymousAndAuthenticatedUsers` requires `404 NotFound` for both caller types; and
- the test asserts no matching `Word` or caller-authored `WordDefinition` is persisted.

### Completion criteria already satisfied

- All controllers and actions were inventoried.
- Registration and login were confirmed as the only bootstrap endpoints.
- No additional bootstrap exception was found.
- Every action received an explicit security classification.
- Authentication-required route coverage was established.
- Authenticated dictionary lookup tests were retained and adapted.
- The vulnerable direct-write characterization became a security regression test.
- No production code was changed during Phase 1.

Phase 1 must not be repeated or weakened. Production code must satisfy these tests.

## 11. Phase 1 RED Baseline

The developer manually ran the backend suite after Phase 1:

```text
Failed: 3
Passed: 131
Skipped: 0
Total: 134
```

The three failures are intentional:

1. `R3SecurityContractApiTests.AnonymousLookupCannotContactProviderOrPersistCanonicalData`
   - Expected: `401 Unauthorized`
   - Actual: `404 NotFound`
   - Meaning: anonymous lookup passed the authentication boundary and reached lookup/provider behavior.

2. `R3SecurityContractApiTests.AnonymousApplicationRoutesReturnUnauthorized`
   - Route: `GET /api/words/lookup/security-contract`
   - Expected: `401 Unauthorized`
   - Actual: `404 NotFound`
   - Meaning: the route-wide contract independently confirms dictionary lookup is anonymous.

3. `VocabularyOwnershipApiTests.DirectCanonicalWordAddIsUnavailableToAnonymousAndAuthenticatedUsers`
   - Expected: `404 NotFound`
   - Actual: `200 OK`
   - Meaning: `/api/words/add` still exists and still permits direct canonical mutation.

The remaining 131 tests passed. This is the formal RED baseline. These tests must not be deleted, relaxed to current behavior, or bypassed. Phase 2 should resolve the first two failures; Phase 3 should resolve the third.

## 12. Phase 2 - Authentication Enforcement

**Status: NOT STARTED**  
**Next implementation action**

### Objective

Make authenticated access the default for every application/business API endpoint while preserving explicit anonymous registration and login.

### Expected production changes

1. In `VocabularyApp.WebApi/Program.cs`, configure authorization with a fallback policy that requires an authenticated user. Use the standard ASP.NET Core `AuthorizationPolicyBuilder`/fallback-policy mechanism appropriate for the existing JWT bearer setup.
2. Add explicit `[AllowAnonymous]` metadata to `UsersController.Register`.
3. Add explicit `[AllowAnonymous]` metadata to `UsersController.Login`.
4. Do not add an anonymous exception to dictionary lookup.
5. Preserve existing `[Authorize]` attributes unless a concrete problem requires changing them.
6. Do not remove `/api/words/add` in this phase; route removal is isolated to Phase 3.

### Expected behavior

- Registration remains reachable without a bearer token.
- Login remains reachable without a bearer token.
- Anonymous dictionary lookup returns HTTP 401 before controller, provider, or persistence behavior executes.
- All existing protected routes continue returning HTTP 401 anonymously.
- Authenticated dictionary lookup continues reaching cache/provider behavior.
- Authenticated personal vocabulary, profile, password, token-validation, and quiz behavior remains reachable.
- `/api/words/add` becomes authentication-required because of the fallback policy, but still exists temporarily until Phase 3.

The temporary Phase 2 state is acceptable only between reviewed phases. The direct-write route must not be considered remediated merely because it requires authentication; ordinary authenticated users must never receive canonical-authoring capability.

### Expected test transition

- Both anonymous dictionary lookup failures become GREEN.
- Registration/login anonymous tests remain GREEN.
- Authenticated dictionary lookup tests remain GREEN.
- Existing protected-route tests remain GREEN.
- The `/api/words/add` removal regression remains RED because it expects route absence, not merely HTTP 401.

### Completion criteria

- Secure-by-default fallback authorization is active.
- Only registration and login have approved anonymous metadata.
- Anonymous lookup cannot contact the provider or persist data.
- Authenticated lookup remains functional.
- The developer confirms only the direct-write removal test remains RED.
- No R1/R2 behavior is changed.

No database migration, Angular change, JWT redesign, password change, or deployment configuration change is required.

## 13. Phase 3 - Canonical Write Removal

**Status: NOT STARTED**

### Objective

Eliminate the ability for any caller to submit canonical word or definition content directly.

### Required removals

- `POST /api/words/add`
- `WordsController.AddWord`
- `IWordService.AddWordAsync`
- `WordService.AddWordAsync`

### Implementation sequence

1. Remove the controller action and route.
2. Remove the service-interface member.
3. Remove the dedicated service implementation.
4. Search the repository for all references to `AddWordAsync`, `/api/words/add`, and the removed action.
5. Retain `AddWordRequest` because `AddToVocabularyAsync` and the Angular personal-vocabulary workflow still use it.
6. Retain `ResolvePartOfSpeechAsync` and other shared helpers if remaining functionality uses them.
7. Do not redesign `LookupWordAsync` or its provider/cache behavior.
8. Inventory all remaining construction/addition of `Word` and `WordDefinition` entities.

### Required remaining-write-path record

For every remaining canonical creation path, the implementation report must state:

- file;
- method;
- trigger;
- word-data source;
- definition-data source; and
- why the path is legitimate under the R3 trust policy.

Expected legitimate path:

| File | Method | Trigger | Word source | Definition source | R3 legitimacy |
| --- | --- | --- | --- | --- | --- |
| `VocabularyApp.WebApi/Services/WordService.cs` | `LookupWordAsync` | Authenticated cache-miss lookup | Mapped trusted provider response, with lookup term used for request/fallback | Trusted external dictionary response | User cannot author canonical definition content; fallback authorization prevents anonymous triggering |

`AddToVocabularyAsync` currently may create a minimal `Word` when none exists. This path must be inspected carefully in Phase 3 because its `Word.Text` comes from the authenticated caller, although it does not create a canonical definition. The authoritative rule says canonical words and definitions may only be created from trusted external dictionary responses. If this fallback remains reachable, it conflicts with the revised policy and is a Phase 3 blocker. The implementation must not silently leave it in place or broaden scope without reporting it. The likely narrow resolution is to require the canonical word to exist from authenticated lookup before personal saving, but the exact behavior must be confirmed against current tests and client flow before changing it.

### Expected test transition

- `DirectCanonicalWordAddIsUnavailableToAnonymousAndAuthenticatedUsers` becomes GREEN.
- Both callers receive route-not-found.
- No caller-authored canonical word or definition is persisted through the removed route.
- The complete R3 security-contract suite is GREEN.
- Authenticated provider/cache and personal-vocabulary regression tests remain GREEN.

### Completion criteria

- No `/api/words/add` route exists.
- No dedicated `AddWordAsync` contract or implementation remains.
- Swagger no longer advertises the operation.
- All remaining canonical writes are documented and policy-compliant.
- Any caller-authored canonical creation fallback has been resolved or explicitly reported as blocking completion.

## 14. Phase 4 - Documentation and Regression Validation

**Status: NOT STARTED**

### Objective

Record the final implementation and complete developer-run validation before declaring R3 done.

### Documentation work

Create or update R3 documentation under `Docs/Updates` with:

- final authorization fallback-policy design;
- explicit anonymous registration/login exceptions;
- final endpoint inventory;
- canonical dictionary trust policy;
- all remaining legitimate canonical creation paths;
- exact production and test files changed;
- confirmation that no administrator system was introduced;
- database, configuration, frontend, and deployment impact;
- test commands and developer-supplied results;
- manual smoke-test results;
- R1/R2 regression review; and
- residual risks or follow-up work.

The existing `R3-Canonical-Word-Write-Analysis.md` should be annotated, updated, or formally superseded so readers do not treat its narrower public-lookup conclusion as current policy. Preserve its useful technical discovery evidence.

### Final regression validation

The developer, not Codex, runs:

```powershell
dotnet test VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj
```

If focused diagnosis is needed first:

```powershell
dotnet test VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter FullyQualifiedName~R3SecurityContractApiTests
dotnet test VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter FullyQualifiedName~DictionaryLookupApiTests
dotnet test VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter FullyQualifiedName~VocabularyOwnershipApiTests
dotnet test VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter FullyQualifiedName~AuthenticationApiTests
dotnet test VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj --filter "FullyQualifiedName~PasswordServiceTests|FullyQualifiedName~LoginMigrationTests|FullyQualifiedName~CredentialConcurrencyTests|FullyQualifiedName~AuthenticationLoggingTests"
```

The completion gate remains the full backend suite, not only focused tests.

### Completion criteria

- All R3 tests pass.
- Dictionary lookup/cache tests pass authenticated.
- Personal vocabulary and ownership tests pass.
- Authentication/JWT tests pass.
- R1 and R2 regression coverage passes.
- The full backend suite passes.
- Manual smoke tests pass.
- Final documentation accurately describes deployed behavior.

## 15. R1/R2 Regression Boundaries

### R1 - JWT configuration and validation

R3 must not unnecessarily modify:

- `JwtSettings` binding or validation;
- signing-secret location or production environment-variable handling;
- issuer or audience values/validation;
- signing algorithm;
- token lifetime or clock-skew behavior;
- signature/lifetime validation semantics;
- `JwtHelper` token generation; or
- production secret rotation procedures.

The fallback authorization policy consumes the already validated authenticated principal. It does not require changes to authentication itself.

### R2 - password security and migration

R3 must not unnecessarily modify:

- `IPasswordService` or `PasswordService`;
- `ILegacyPasswordVerifier` or `LegacyPasswordVerifier`;
- modern registration hashing;
- login recognition, verification, rehash, or legacy migration;
- required hash-upgrade persistence ordering;
- password-change behavior;
- `PasswordHash` concurrency-token configuration;
- credential concurrency handling;
- authentication logging protections; or
- the documented legacy-removal conditions.

Registration and login receive explicit anonymous authorization exceptions, but their credential processing remains unchanged. R3 builds on those endpoints; it does not redesign them.

## 16. Database Impact

Expected database impact:

- **Schema change:** None.
- **EF Core migration:** None.
- **Data migration:** None.
- **Model change:** None expected.
- **Seed change:** None.

Fallback authorization is middleware configuration, and route/service removal does not require a schema change. `Word`, `WordDefinition`, and `PartOfSpeech` remain required for provider-backed lookup, personal vocabulary, and quiz behavior.

Historical canonical data quality is an operational consideration but is not required to close the active exposure. If evidence of pollution is discovered, audit/cleanup should be separately scoped and performed with backup and provenance criteria.

## 17. Configuration/Deployment Impact

Expected impact:

- **New secrets:** None.
- **New environment variables:** None.
- **JWT configuration:** Unchanged.
- **`appsettings` files:** No change expected.
- **CORS:** Unchanged.
- **Angular application:** No change expected.
- **SmarterASP.NET/IIS configuration:** No change expected.
- **`web.config`:** No change expected.
- **Deployment topology:** Unchanged.

Deploy the updated API through the normal process. Authentication enforcement changes runtime access behavior, so release notes should call out that dictionary lookup now requires a bearer token and that `/api/words/add` has been removed. Repository evidence shows the Angular lookup client already uses `ApiService`, which attaches a bearer token when one is present, and the lookup page belongs to the authenticated application flow; no Angular code change is currently indicated.

Post-deployment validation must confirm the fallback policy is active, registration/login remain reachable, authenticated lookup works, and the removed route is absent. R1's `JwtSettings__SecretKey` deployment requirement remains unchanged.

## 18. Testing Strategy

R3 uses Red-Green-Refactor sequencing without weakening established tests:

### Phase 1 - RED, complete

- Security policy encoded before production changes.
- Manual baseline: 131 passed, 3 failed.
- Failures map exactly to public lookup and direct canonical mutation.

### Phase 2 - partial GREEN

- Anonymous route theory proves HTTP 401 for all business routes.
- Dedicated anonymous lookup test proves the provider is not contacted and canonical data is not persisted.
- Anonymous registration/login tests prove approved exceptions.
- Authenticated dictionary tests prove cache/provider behavior still works.
- Direct-write removal test remains RED by design.

### Phase 3 - complete GREEN for R3

- Removed route returns 404 for anonymous and authenticated callers.
- No caller-authored canonical word/definition is persisted.
- All controller actions remain explicitly classified.
- Remaining canonical creation is provider-backed and documented.

### Phase 4 - full regression

Coverage must include:

- `R3SecurityContractApiTests`;
- `DictionaryLookupApiTests`;
- `VocabularyOwnershipApiTests`;
- `AuthenticationApiTests` and API host/authentication infrastructure tests;
- JWT-protected endpoint behavior;
- R2 password component and service tests;
- login migration and persistence tests;
- credential concurrency tests;
- authentication logging tests;
- quiz integration tests; and
- the entire backend suite.

Codex must not run tests during implementation phases when the phase request retains that constraint. The developer records actual results in final R3 documentation.

## 19. Manual Smoke-Test Strategy

Use a nonproduction environment with a valid externally configured JWT signing key and a controlled database. Do not use production credentials in request files or logs.

1. **Registration**
   - Send valid anonymous `POST /api/users/register`.
   - Confirm success and no authorization challenge.
2. **Login**
   - Send valid anonymous `POST /api/users/login`.
   - Confirm a usable JWT is returned.
3. **Anonymous dictionary lookup**
   - Send `GET /api/words/lookup/{unique-word}` without a bearer token.
   - Confirm HTTP 401.
   - Where observable in a controlled environment, confirm no provider request and no canonical insertion occurred.
4. **Authenticated dictionary lookup**
   - Repeat with the login JWT.
   - Confirm cache hit or provider-backed result succeeds as appropriate.
   - On a controlled cache miss, confirm canonical definition content matches provider data.
5. **Personal vocabulary save**
   - Save the looked-up word through `POST /api/words/vocabulary/add` with the JWT.
   - Confirm it appears in the authenticated user's vocabulary.
6. **Removed canonical route**
   - Send `POST /api/words/add` both without and with a JWT.
   - Confirm route-not-found for both.
   - Confirm no submitted canonical content was persisted.
7. **Swagger/API surface**
   - Inspect Swagger UI or `/swagger/v1/swagger.json`.
   - Confirm `/api/words/add` is absent.
   - Confirm registration/login remain documented and other routes support the bearer security scheme.
8. **Representative protected routes**
   - Check profile, vocabulary, and quiz without a token and confirm HTTP 401.
   - Repeat representative allowed operations with the JWT and confirm they reach normal behavior.

Record environment, date, build identifier, tester, expected/actual result, and evidence for each step in the final review.

## 20. Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Fallback policy blocks registration/login | Users cannot acquire accounts or tokens | Add explicit `[AllowAnonymous]` to exactly those actions; retain anonymous integration tests |
| Lookup remains anonymously accessible | Anonymous callers can invoke provider/cache writes | Use fallback authorization and dedicated 401/no-provider/no-persistence test |
| `[Authorize]` is added only to current routes | Future endpoints may accidentally be public | Prefer fallback policy; retain action-classification inventory |
| `/api/words/add` is merely protected, not removed | Any authenticated user can still author shared data | Keep Phase 3 separate; removal regression requires 404 for both caller types |
| Shared `AddWordRequest` is removed | Personal vocabulary save breaks | Remove only the dedicated action/service operation; retain shared model while referenced |
| Provider-backed lookup is over-refactored | Cache/provider regressions expand R3 | Preserve `LookupWordAsync` architecture and authenticated dictionary tests |
| `AddToVocabularyAsync` creates a minimal caller-supplied canonical word | Revised canonical-source policy remains violated | Treat as a Phase 3 inspection blocker; resolve deliberately before declaring completion |
| Existing external client depends on anonymous lookup | Client receives new 401 responses | Product policy intentionally requires authentication; document release behavior and smoke-test Angular flow |
| Existing external client calls `/api/words/add` | Route removal breaks an undocumented client | Repository has no caller; confirm operational usage if logs are available, but do not retain insecure capability |
| R1 is disturbed while changing authorization | Token issuance/validation or secret handling regresses | Limit Phase 2 to authorization policy and anonymous metadata; run authentication/JWT regressions |
| R2 is disturbed through registration/login edits | Password migration or concurrency behavior regresses | Change only authorization metadata on actions; run full R2 suite |
| Tests are weakened to achieve GREEN | Vulnerability can persist behind passing tests | Preserve Phase 1 expectations and RED baseline; production must conform |
| Swagger security presentation is misleading | Operators misunderstand public/protected surface | Inspect generated Swagger in Phase 4 and document fallback-policy behavior |

## 21. Definition of Done

R3 is complete only when all of the following are true:

- Registration is explicitly and anonymously accessible.
- Login is explicitly and anonymously accessible.
- No other current application/business endpoint is anonymously accessible.
- Future API endpoints require authentication by default.
- Anonymous business requests receive HTTP 401 before controller/business/provider execution.
- Anonymous users cannot trigger dictionary lookup or canonical cache population.
- Authenticated dictionary lookup works for cache hits and provider-backed misses.
- `/api/words/add` no longer exists.
- Anonymous and authenticated callers both receive route-not-found for the removed operation.
- No caller can directly submit a canonical definition for persistence.
- Every remaining canonical `Word` and `WordDefinition` creation path is documented and policy-compliant.
- Canonical definition content populated on lookup originates from the trusted external provider.
- Swagger no longer advertises `/api/words/add`.
- Personal vocabulary and quiz behavior remain functional.
- R1 JWT configuration, secret handling, and validation semantics remain intact.
- R2 password hashing, migration, concurrency, and logging behavior remain intact.
- No database migration, new secret, new environment variable, Angular change, or hosting configuration change was introduced unless a documented repository discrepancy requires reconsideration.
- Developer-run focused R3 tests pass.
- Developer-run full backend suite passes.
- Manual smoke tests pass and are recorded.
- Final R3 documentation reflects actual implementation and validation results.

## 22. Current R3 Status / Next Action

| Stage | Status | Evidence/next step |
| --- | --- | --- |
| Analysis | **COMPLETE** | Technical discovery recorded in `R3-Canonical-Word-Write-Analysis.md`; revised product policy supersedes conflicting scope conclusions |
| Phase 1 - Security contract | **COMPLETE** | Tests added/updated; manual RED baseline is 131 passed and 3 failed |
| Phase 2 - Authentication enforcement | **NOT STARTED - NEXT ACTION** | Configure secure-by-default fallback authorization and explicit registration/login exceptions |
| Phase 3 - Canonical write removal | **NOT STARTED** | Remove route/action/service operation and audit remaining canonical write paths |
| Phase 4 - Documentation and validation | **NOT STARTED** | Finalize R3 records and developer-run regression/smoke validation |

The next implementation request should authorize **Phase 2 only**. Phase 2 must stop after enforcing the authentication policy and reporting the expected transition from three RED failures to the single Phase 3 `/api/words/add` failure. It must not bundle canonical-route removal, R1/R2 changes, database work, frontend changes, or Phase 4 validation.

