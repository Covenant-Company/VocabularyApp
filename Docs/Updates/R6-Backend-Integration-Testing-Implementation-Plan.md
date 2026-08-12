# R6 Backend Integration-Testing Implementation Plan

## 1. Executive Summary

R6 is approximately 35% complete by capability. R2 already established a .NET 8 xUnit
project, relational SQLite service/database testing, and strong password-security coverage.
R6 must retain that work and add the missing API, ownership, vocabulary, quiz, lookup,
isolation, and CI layers needed for the original definition of done:

> Critical API journeys run automatically and reliably from a clean checkout.

This plan contains exactly **six implementation phases** in dependency order:

1. API integration host foundation.
2. Isolation, seeding, and authentication helpers.
3. Authentication and authorization API coverage.
4. Vocabulary and ownership coverage.
5. Quiz and lookup characterization coverage.
6. CI, reliability, and final review.

The selected architecture uses `WebApplicationFactory<Program>`, the real ASP.NET Core
middleware and JWT bearer handler, a fresh SQLite in-memory database and open connection per
API test instance, deterministic seed/auth helpers, and a fake dictionary
`HttpMessageHandler`. The only required production startup change is a public partial
`Program` marker. A narrow internal quiz-session cleanup seam is also planned solely because
the current process-wide static session dictionary otherwise prevents reliable isolation;
it must not redesign quiz behavior.

SQLite remains the default relational provider. Existing R2 tests remain at their current
unit and service/database layers. API tests will be deliberately smaller and will prove
routing, model binding, middleware, authorization, claims, controller mappings, and critical
database outcomes through HTTP.

Each phase is implemented and reviewed separately. Codex must not run tests during any
phase. The developer runs the documented commands manually. Codex may run
`dotnet build VocabularyApp.sln` when compilation verification is needed, but must never
commit or push automatically.

## 2. Confirmed Current Baseline

Current repository inspection confirms the R6 analysis remains accurate.

| Area | Confirmed state | R6 disposition |
| --- | --- | --- |
| Test project | `VocabularyApp.WebApi.Tests` targets `net8.0`, uses xUnit, and is included in `VocabularyApp.sln` | Retain and extend |
| Packages | Test SDK, xUnit, coverlet, and EF Core SQLite 8.0.10 | Add only .NET 8-compatible `Microsoft.AspNetCore.Mvc.Testing` |
| References | Test project references WebApi and Data | Retain |
| Relational fixture | `RelationalDatabaseFixture` keeps one SQLite in-memory connection per xUnit class and supports fresh contexts/interceptors | Retain unchanged for R2 service tests; do not force it into API hosting |
| Logger helper | `CapturingLogger<T>` captures formatted text and exceptions | Retain unchanged |
| JWT helper | `TestJwtSettingsFactory` supplies valid deterministic settings | Retain for service tests; align API host configuration with the same values |
| Controlled hasher | `ControlledPasswordHasher` controls framework outcomes | Retain for focused security/service tests only |
| Security tests | Strict legacy, modern password service, verification outcomes | Retain unchanged |
| Authentication service tests | Registration, login migration, password change, persistence failures, concurrency, logging | Retain unchanged |
| HTTP tests | None | Add through `WebApplicationFactory` |
| Ownership/vocabulary tests | None | Add before R5 |
| Quiz tests | None | Add before R4/R12 |
| Lookup tests | None | Add focused current-behavior coverage |
| Startup | Top-level `Program.cs`; SQL Server registration; startup-time JWT validation; typed dictionary `HttpClient` | Add a discoverability marker and override dependencies in the factory |
| Quiz state | Private static `ConcurrentDictionary` with wall-clock time and `Random.Shared` | Add narrow cleanup/targeted serialization; do not redesign |
| CI | `.github/workflows` exists but contains no workflow files | Add separate backend-test workflow in Phase 6 |

The existing R2 tests are not API tests: they instantiate security classes or services
directly. That is appropriate for their responsibilities. R6 must not replace them merely to
make the suite stylistically uniform.

## 3. R6 Completion Target

R6 is complete when the repository has a layered, deterministic safety net that proves:

- the real API boots without production secrets, production SQL Server, or live external
  network access;
- database state and process-static quiz state do not leak between tests;
- user registration/login and real JWT middleware operate through HTTP;
- anonymous and malformed-token requests cannot reach protected behavior;
- controller claims resolve the correct user;
- vocabulary reads and mutations are owner-scoped;
- duplicate, favorite, and preferred-definition behavior is characterized before R5;
- quiz authorization, ownership, answer secrecy, submission, persistence, history, and
  session reuse are characterized before R4/R12;
- required dictionary cache/provider paths are tested against a fake boundary;
- the complete backend suite restores, builds, and runs in CI from a clean checkout; and
- R6 introduces no database migration, production database dependency, or bundled R4/R5/R7/
  R8/R10/R12 feature change.

The completion target does not require every edge case at HTTP level. Detailed password
combinations remain protected by R2 units and service/database tests.

## 4. Layered Testing Strategy

### Unit/component tests

Use for pure or nearly pure rules with many input combinations and no need for ASP.NET or
database behavior. Existing examples are `LegacyPasswordVerifierTests`,
`PasswordServiceTests`, and `PasswordVerificationOutcomeTests`. Future extracted quiz
scoring rules may belong here, but extraction is not R6 work.

### Service/database integration tests

Use real EF Core relational behavior when tracking, persistence, concurrency, transactions,
or precise database state is the primary target. Keep all R2 service tests in this layer.
Add a focused `WordService` or `QuizService` integration test only when direct database
assertions or provider behavior cannot be expressed clearly through an API journey.

### API integration tests

Use `WebApplicationFactory` for behavior only the hosted pipeline proves:

- route matching and HTTP verbs;
- JSON serialization and deserialization;
- `[ApiController]` model binding/validation;
- JWT authentication and `[Authorize]` middleware;
- claim propagation into controllers;
- DI wiring and service lifetimes;
- controller status-code mappings; and
- end-to-end persistence through an HTTP request.

API assertions should prefer stable semantics—status, success indicator, identifiers, owner
isolation, and database outcomes—over incidental full message text. Exact messages should be
asserted only when they are an intentional current contract needed before R7/R8.

## 5. API Integration Architecture

The API integration layer will consist of small collaborators rather than one giant fixture:

```text
API test instance
  -> VocabularyAppWebApplicationFactory
       -> Testing environment/configuration
       -> one owned open SQLite connection
       -> replaced ApplicationDbContext registration
       -> controlled dictionary handler
  -> TestDataSeeder (scope-based database setup)
  -> AuthenticationApiClient extensions (register/login/token/header)
  -> HttpClient (real TestServer pipeline)
```

The factory owns host-wide infrastructure and exposes narrow methods to create service
scopes or reset/create the database. Seeders own domain setup. Authentication extensions own
HTTP registration/login and bearer headers. The fake handler owns external response/call
control. Tests own their assertions and do not access production configuration.

The default is a **fresh factory per API test instance**. xUnit creates a new test-class
instance per test method, so an `IAsyncLifetime` API-test base can create and dispose a new
factory, client, connection, and database for every test. This is more reliable and simpler
than sharing a factory and implementing table-by-table deletion for the current suite size.

## 6. WebApplicationFactory Design

Add `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs` as a
sealed `WebApplicationFactory<Program>` with these exact responsibilities:

1. Create and hold one open `SqliteConnection("Data Source=:memory:")` per factory instance.
2. Set the environment to `Testing`.
3. Add in-memory configuration before application services are built:
   - a non-production SQLite placeholder connection string;
   - `JwtSettings:SecretKey` equal to the test key;
   - fixed test issuer/audience;
   - positive short expiration;
   - harmless CORS test origin.
4. Remove every existing `DbContextOptions<ApplicationDbContext>` and relevant DbContext
   registration, then register `ApplicationDbContext` with `UseSqlite` against the owned open
   connection.
5. Override the effective typed `IWordService`/`WordService` HTTP-client registration so it
   uses `FakeDictionaryHttpMessageHandler`. Account for the current `AddScoped<IWordService,
   WordService>()` followed by `AddHttpClient<IWordService, WordService>()`; ensure the test
   resolves exactly the intended typed client and cannot use a live handler.
6. After the host is created, open a scope and call `Database.EnsureCreatedAsync()`.
7. Expose a narrow `CreateScope()` or `WithDbContextAsync` facility for seed/reset helpers.
8. Dispose the `HttpClient`, host, and SQLite connection asynchronously.

Use `WebApplicationFactory<Program>` directly. Append this conventional marker after
`app.Run()` in `Program.cs`:

```csharp
public partial class Program;
```

If the repository/compiler style does not accept the semicolon form, use an empty body. Do
not refactor startup into a separate class or change runtime registration behavior.

The factory must prove the production database is not used. Recommended evidence is a
factory-owned marker/connection identity plus successful test-only schema access. Do not
make a destructive probe against the configured SQL Server string.

## 7. Test Database and Isolation Strategy

Use a separate API database utility; do not reuse `RelationalDatabaseFixture` as the API
host's database owner. The R2 fixture has class lifetime and is working for its existing
tests. Coupling it to the host would complicate ownership and disposal.

Concrete API strategy:

- one `VocabularyAppWebApplicationFactory` per test method through a per-test xUnit class
  instance/base;
- one open SQLite in-memory connection owned by that factory;
- `EnsureCreatedAsync` once after host creation;
- seed only the records required by the test;
- dispose the factory/connection after the test, which drops the entire database;
- no shared global database, no identity reset assumptions, and no table truncation logic;
- use model-seeded `PartOfSpeech` rows, but query by name or known model seed only where the
  model explicitly guarantees IDs;
- return created entity IDs and DTOs from seed methods; never assume generated IDs/order;
- use unique usernames/emails even with isolation, improving failure diagnostics and making
  accidental sharing visible; and
- use fresh scopes/contexts for verification so tracked state cannot produce false positives.

Add an explicit isolation test that creates a marker user/word in one fully disposed factory,
creates a second factory, and asserts the marker is absent. This test proves database
isolation rather than merely relying on implementation intent.

Do not apply migrations in the default SQLite suite. `EnsureCreated` is appropriate for
fast model integration. SQL Server migration validation is optional and deferred unless R4/
R5 identifies provider-sensitive requirements.

## 8. Parallel Safety Strategy

Database isolation is achieved by ownership, not by disabling concurrency. Each ordinary
API test receives its own factory/connection/database, so different test classes may run in
parallel safely.

Specific rules:

- Do not add assembly-wide `CollectionBehavior(DisableTestParallelization = true)`.
- Do not share one mutable `WebApplicationFactory` across the whole test assembly.
- Do not share a static mutable fake dictionary response queue; each factory owns its fake
  handler.
- Use unique user credentials and return generated IDs from seeders.
- Keep the existing R2 fixtures unchanged; their per-class databases remain independent.
- Put quiz API tests in a named `[Collection("Quiz API integration")]` with
  `[CollectionDefinition(..., DisableParallelization = true)]` because
  `QuizService.QuizSessions` is process-wide static state.
- Add a narrow `internal static void ClearSessionsForTesting()` method on `QuizService` that
  only calls `QuizSessions.Clear()`. Expose internals to the test assembly through a focused
  `InternalsVisibleTo("VocabularyApp.WebApi.Tests")` declaration. Call cleanup before and
  after every quiz test.
- Do not expose session internals, correct answers, or mutation APIs through production HTTP.
- Do not replace static sessions, inject a clock, or inject randomness during R6; those are
  R12/R4 concerns. Use fixed quiz modes, invariant/set assertions, and bounded time checks.

The reported historical MSBuild/worker behavior in a Codex execution environment is not a
reason to serialize tests. The user has confirmed normal local `dotnet test` works. Only
actual shared quiz state is serialized.

## 9. Authentication Helper Strategy

Add focused HTTP helpers rather than duplicating password internals:

- `RegisterUserAsync`: generate or accept unique username/email/password, POST
  `/api/users/register`, require/return the current response DTO and JWT.
- `LoginAsync`: POST `/api/users/login` and return parsed response/token without assuming
  registration was used.
- `CreateAuthenticatedClientAsync`: create a factory client, register or login, then set
  `Authorization: Bearer <token>`.
- `AsBearerToken`: clone/set request headers without mutating a shared client in ways that
  leak identity between tests.
- `CreateTwoUsersAsync`: return independent User A/User B identities and authenticated
  clients/headers for ownership tests.
- `CreateToken`: use the same test `JwtSettings`/signing rules only for malformed/expired/
  nonexistent-user claim cases that cannot be produced through normal login.

Prefer actual API registration/login in end-to-end auth and ownership tests. Use direct
database seeding when a test requires legacy credentials, a deleted user, exact vocabulary
relationships, quiz history, or other controlled persisted state. Do not recreate legacy
hashing logic in API helpers; reuse the application verifier/test seed method already proven
by R2 only for the one representative legacy API journey.

## 10. External Dependency Strategy

`WordService` already accepts `HttpClient`, so R6 does not need R10's provider abstraction.
Add `FakeDictionaryHttpMessageHandler` with per-factory deterministic behavior:

- enqueue/configure a status code and JSON body;
- configure a thrown `HttpRequestException` or cancellation/timeout-like failure where
  current behavior needs characterization;
- record requested method/URI and call count; and
- fail the test immediately on an unexpected request, preventing accidental live traffic.

The factory must configure the typed `HttpClient` for `IWordService` to use this handler. Use
the real dictionaryapi.dev-shaped DTO JSON so `GetFromJsonAsync<DictionaryApiResponse[]>`
and mapping are exercised.

Required R6 lookup coverage in Phase 5:

- database cache hit and zero external calls;
- successful cache miss, persistence, and subsequent cache hit;
- empty/not-found-equivalent provider result;
- provider transport failure; and
- unknown part of speech falling back to seeded Noun.

Deferred to R10/R13:

- extracting a provider interface;
- retries, circuit breaker, or detailed timeout policy;
- changing error contracts; and
- robust concurrent cache-miss coordination. A small concurrent-miss characterization may
  be attempted only if deterministic with the existing seam; failure to make it reliable is
  documented as an R13 requirement, not solved with locks in R6.

## 11. Six-Phase Implementation Sequence

| Phase | Outcome | Depends on |
| --- | --- | --- |
| 1. API Integration Host Foundation | Real API boots with test JWT, isolated SQLite, fakeable dictionary boundary, and baseline HTTP smoke test | Existing R2 test project |
| 2. Isolation, Seeding, and Authentication Helpers | Per-test isolation is proven; deterministic domain/user/auth setup exists | Phase 1 |
| 3. Authentication and Authorization API Coverage | Public auth endpoints, JWT middleware, claims, validation, and representative password-change flow are protected | Phases 1-2 |
| 4. Vocabulary and Ownership Coverage | User A/B vocabulary boundaries, duplicates, favorite, preferred definition, list/search, and invalid resources are protected | Phases 1-3 |
| 5. Quiz and Lookup Characterization Coverage | Pre-R4 quiz safety net and required lookup/cache/provider behavior exist | Phases 1-4 |
| 6. CI, Reliability, and Final Review | Clean-checkout build/tests run automatically; scope and readiness gates are reviewed | Phases 1-5 |

Do not overlap phases merely because later files are known. Each checkpoint must be manually
verified before the next phase begins.

## 12. Phase 1 — API Integration Host Foundation

### Goal

Boot the real Web API through `WebApplicationFactory<Program>` using safe test configuration,
an owned SQLite database, and no production database or external network. Add one baseline
HTTP smoke test only.

### Current Prerequisites

- `VocabularyApp.WebApi.Tests` builds and remains in the solution.
- Existing R2 tests and `RelationalDatabaseFixture` are unchanged.
- .NET 8 package versions remain aligned at 8.0.x.

### Files to Add

- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs`
- `VocabularyApp.WebApi.Tests/Api/ApiHostSmokeTests.cs`

### Files to Modify

- `VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj`
- `VocabularyApp.WebApi/Program.cs`

### Implementation Steps

1. Add `Microsoft.AspNetCore.Mvc.Testing` at an 8.0.x version compatible with the repository's
   ASP.NET Core/EF 8.0.10 dependencies. Do not update unrelated packages.
2. Append a public partial `Program` declaration after `app.Run()`.
3. Create `VocabularyAppWebApplicationFactory` with an owned open SQLite in-memory
   connection and `Testing` environment.
4. Add test JWT values through `ConfigureAppConfiguration` early enough for
   `JwtSettings.BindAndValidate` during startup.
5. In test services, remove the SQL Server DbContext options/registration and register
   `ApplicationDbContext` against the owned connection.
6. Configure a fail-closed dictionary handler placeholder: any unexpected outbound request
   throws a test-specific exception. Full response configuration waits for Phase 2/5.
7. Create the schema through a service scope after host startup.
8. Add one smoke test that requests a known protected API route anonymously and expects 401.
   This proves host/routing/auth middleware without needing seed data.
9. Add a database assertion through a factory scope showing the SQLite provider is active.
   Do not contact or probe SQL Server.
10. Audit factory disposal and ensure the owned connection closes.

### Tests to Add

- Host boots with test-only JWT configuration.
- Anonymous `GET /api/users/profile` returns 401 through middleware.
- Resolved `ApplicationDbContext.Database.ProviderName` is SQLite and schema access succeeds.
- Unexpected dictionary outbound traffic fails closed if the smoke route ever triggers it.

### Important Constraints

- Codex must not run tests.
- Do not add endpoint breadth, seed builders, or auth helpers yet.
- Do not alter production database settings, JWT architecture, middleware order, controllers,
  or service behavior.
- Do not reuse or modify `RelationalDatabaseFixture`.
- Do not add migrations or test-only branches to runtime behavior.

### Manual Developer Verification

Codex must not execute these commands. The developer runs:

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
```

Then:

```powershell
dotnet test
```

Codex may run this compile-only command if needed:

```powershell
dotnet build VocabularyApp.sln
```

### Checkpoint

The real API starts in `Testing`, anonymous authorization is exercised over HTTP, the test
context is SQLite, no production SQL Server connection is attempted, and all existing R2
tests still pass when the developer runs them.

### Recommended Commit Message

`test: add API integration test host`

### Stop Condition

Stop after Phase 1 verification. Do not begin isolation helpers or broad API coverage.

## 13. Phase 2 — Isolation, Seeding, and Authentication Helpers

### Goal

Make each API test deterministic and independent, and provide small reusable helpers for
domain seeding, user registration/login, bearer tokens, and multiple-user scenarios.

### Current Prerequisites

- Phase 1 factory boots reliably and owns its SQLite connection.
- Existing R2 tests pass manually.
- No live dictionary call is possible.

### Files to Add

- `VocabularyApp.WebApi.Tests/Infrastructure/ApiIntegrationTestBase.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/TestDataSeeder.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/AuthenticationApiClientExtensions.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/TestUser.cs` (small record, if useful)
- `VocabularyApp.WebApi.Tests/Infrastructure/FakeDictionaryHttpMessageHandler.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/ApiIsolationTests.cs`
- `VocabularyApp.WebApi/Properties/AssemblyInfo.cs` for test-only internal visibility, only if
  no existing assembly metadata file is available
- `VocabularyApp.WebApi.Tests/Infrastructure/QuizApiCollection.cs`

### Files to Modify

- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs`
- `VocabularyApp.WebApi/Services/QuizService.cs` (only internal session clear method)

### Implementation Steps

1. Implement `ApiIntegrationTestBase : IAsyncLifetime` so every test instance creates its own
   factory/client and disposes them after the test.
2. Keep schema creation in the factory; do not add global reset or shared mutable storage.
3. Implement `TestDataSeeder` with explicit methods for:
   - modern user;
   - canonical word plus one/multiple definitions;
   - `UserWord` for a specified owner/part of speech/preferred definition;
   - four-word quiz-ready vocabulary; and
   - `QuizResult` history.
   Every method returns the created entities/IDs and verifies inputs.
4. Query seeded parts of speech deterministically; do not assume arbitrary generated IDs.
5. Implement API helpers for register, login, bearer attachment, two independent users, and
   raw test token generation using the same factory JWT settings.
6. Ensure client authorization is request/user-specific. Prefer separate clients or explicit
   request headers; do not leak User A's default header into User B tests.
7. Complete `FakeDictionaryHttpMessageHandler` with per-factory response/call recording.
8. Add `internal static ClearSessionsForTesting()` to `QuizService`, granting the test
   assembly internal access. The method may only clear the dictionary.
9. Define a quiz-only nonparallel collection. Do not annotate non-quiz tests.
10. Add isolation tests using two separately created/disposed factories.
11. Add a helper-validation test that creates and authenticates two users and proves tokens
    identify different profiles.

### Tests to Add

- Data created through Factory A is absent from a newly created Factory B.
- Two independently registered users receive nonempty different tokens and resolve distinct
  profile IDs.
- Fresh verification scopes observe seeded relationships correctly.
- The fake dictionary handler records configured requests and rejects unexpected requests.
- Quiz session cleanup can run before/after a quiz test without exposing session contents.

### Important Constraints

- Codex must not run tests.
- Do not globally disable xUnit parallelization.
- Do not implement table truncation, production reset endpoints, or shared global databases.
- Do not modify quiz scoring, expiration, ownership, persistence, counters, or API contracts.
- Do not add clock/random abstractions.
- Do not consolidate existing R2 helpers unless a direct conflict is proven.

### Manual Developer Verification

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
```

```powershell
dotnet test
```

Codex must not run these. A compile-only build remains permitted:

```powershell
dotnet build VocabularyApp.sln
```

### Checkpoint

Every API test instance owns a disposable database, cross-factory isolation is proven,
multiple authenticated users are reliable, fake HTTP cannot reach the network, and quiz
static state has narrowly scoped cleanup and serialization.

### Recommended Commit Message

`test: add integration isolation and authentication helpers`

### Stop Condition

Stop after Phase 2 verification. Do not add user endpoint coverage beyond helper proof.

## 14. Phase 3 — Authentication and Authorization API Coverage

### Goal

Protect HTTP behaviors that R2 service tests cannot prove: validation, routing, status-code
mapping, real JWT middleware, protected routes, and controller claim resolution.

### Current Prerequisites

- Phases 1-2 are manually green.
- Per-test databases and multi-user/token helpers are stable.
- R2 password tests remain unchanged.

### Files to Add

- `VocabularyApp.WebApi.Tests/Api/UsersApiTests.cs`
- `VocabularyApp.WebApi.Tests/Api/AuthorizationApiTests.cs`

### Files to Modify

- Only shared API helpers if a narrowly required capability is missing.
- No production file is expected to change.

### Implementation Steps

1. Add registration HTTP tests for valid input, automatic model validation, and duplicate
   username/email mapping.
2. Add modern login success and wrong-credential response tests.
3. Add one representative legacy seeded-user login that proves the HTTP response returns a
   token and a fresh context observes a modern replacement. Do not duplicate all R2 format
   cases.
4. Exercise `GET /api/users/profile` anonymously, with a valid token, malformed token,
   wrong-signature token, expired token, and a valid token whose user no longer exists where
   the endpoint contract applies.
5. Assert valid profile identity matches the token subject and never another seeded user.
6. Exercise `GET /api/users/validate-token` with valid/invalid cases.
7. Add a small password-change HTTP path: anonymous rejection, valid authenticated success,
   and incorrect-current mapping. Verify new login succeeds and old login fails once; leave
   hashing edge cases to R2.
8. Assert status codes and stable response shape. Avoid exact validation-framework text
   unless required as the current pre-R7 contract.
9. Confirm middleware returns 401 before controller/service mutation on anonymous requests.

### Tests to Add

- Valid registration: 200, success response, token, correct profile identity.
- Invalid registration fields: 400 through `[ApiController]` validation.
- Duplicate registration: current 400 mapping.
- Valid modern login: 200; bad credentials: 401.
- Representative strict legacy login migrates and returns a usable token.
- Anonymous protected route: 401.
- Valid JWT: accepted and correct claims/user resolved.
- Malformed, invalid-signature, wrong issuer/audience, and expired JWT: 401.
- Valid JWT for nonexistent user: characterize profile/validate-token current response.
- Password change: anonymous 401, owner success, incorrect current password current mapping.

### Important Constraints

- Codex must not run tests.
- Do not re-test every password-service outcome over HTTP.
- Do not change password implementation, JWT settings/claims/algorithm, controllers, DTOs,
  or response contracts.
- If tests reveal a current contract defect, document it for R7/R8 unless it prevents the
  harness from operating safely.
- Do not use a fake authentication handler for these tests; exercise real JWT bearer
  middleware.

### Manual Developer Verification

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
```

```powershell
dotnet test
```

Codex must not run tests. It may run:

```powershell
dotnet build VocabularyApp.sln
```

### Checkpoint

Registration/login/protected user routes are exercised through real HTTP, valid claims map
to the correct user, invalid authentication is rejected by middleware, and no R2 security
test was replaced or weakened.

### Recommended Commit Message

`test: cover authentication and authorization API flows`

### Stop Condition

Stop after Phase 3 verification. Do not begin vocabulary tests or fix API contracts.

## 15. Phase 4 — Vocabulary and Ownership Coverage

### Goal

Protect user-owned vocabulary behavior and EF relationships before R5 changes `UserWord`
identity/schema.

### Current Prerequisites

- Real JWT/multi-user API helpers are manually green.
- Deterministic user/word/definition/user-word seeding exists.
- Per-test database isolation is proven.

### Files to Add

- `VocabularyApp.WebApi.Tests/Api/VocabularyApiTests.cs`
- `VocabularyApp.WebApi.Tests/Services/WordServiceIntegrationTests.cs` only if a relational
  constraint/state assertion is substantially clearer outside HTTP

### Files to Modify

- `TestDataSeeder.cs` only for missing vocabulary relationship builders.
- No production file is expected to change.

### Implementation Steps

1. Seed or register User A and User B with independent authenticated clients.
2. Test User A adding a canonical/new word through
   `POST /api/words/vocabulary/add`; verify one owner-linked `UserWord` in a fresh context.
3. Repeat the same user/word/part-of-speech add. Characterize the current idempotent success
   and prove no duplicate row. Do not assert that the current composite key is the permanent
   post-R5 identity design.
4. Prove User B can independently save the same canonical word without seeing/mutating A's
   `UserWord`.
5. Test `GET /api/words/vocabulary` and `/search` for A/B isolation, pagination, term filter,
   starts-with filter, empty search, preferred definition, favorite, and counters mapped from
   seeded values.
6. Test favorite mutation as owner and cross-user attacker. Assert database state, not only
   response status.
7. Test preferred-definition mutation:
   - owner success for same word;
   - cross-user mutation failure;
   - definition from another word rejected;
   - invalid/nonexistent IDs fail without mutation; and
   - current part-of-speech move/conflict behavior is characterized.
8. Assert anonymous access to protected vocabulary endpoints returns 401.
9. Add a database-level test for the current `(UserId, WordId, PartOfSpeechId)` unique
   constraint if HTTP idempotence alone does not prove relational enforcement.
10. Add a characterization test for anonymous `POST /api/words/add`, because the code calls
    it an admin endpoint but has no authorization. Mark the result as a policy observation;
    do not fix it silently in R6.

### Tests to Add

- User A saves and retrieves own vocabulary.
- User B list/search excludes User A data.
- User B cannot favorite or change preferred definition on A's entry.
- Same canonical word can belong independently to A and B.
- Duplicate same-owner/current-key save is idempotent and produces one row.
- Favorite persists only on owner's row.
- Preferred definition belongs to the same word and owner; cross-word/invalid IDs fail.
- List/search filters and empty search are owner-scoped.
- Anonymous protected vocabulary endpoints return 401.
- Current anonymous canonical-add behavior is documented by characterization.

### Important Constraints

- Codex must not run tests.
- Do not alter `UserWord`, indexes, migrations, duplicate identity, DTOs, controllers, or
  WordService behavior.
- Do not lock the current composite key in as R5's desired design; assert current observable
  behavior and security invariants separately.
- Ownership failures may currently map to 400 rather than 404/403. Characterize status
  semantically without redesigning it; R7 can standardize later.
- Do not add delete/edit endpoints that do not exist.

### Manual Developer Verification

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
```

```powershell
dotnet test
```

Codex must not run them. A build is permitted:

```powershell
dotnet build VocabularyApp.sln
```

### Checkpoint

User A/B read and mutation isolation is proven through HTTP and fresh database contexts;
duplicate/favorite/preferred-definition/current relationship behavior is characterized;
tests do not depend on generated row order or fixed IDs.

### Recommended Commit Message

`test: cover vocabulary ownership API flows`

### Stop Condition

Stop after Phase 4 verification. Do not begin R5 or change vocabulary identity/contracts.

## 16. Phase 5 — Quiz and Lookup Characterization Coverage

### Goal

Create the safety net required before R4/R12 and cover the minimum current dictionary cache
and provider behavior without implementing R10/R13 architecture.

### Current Prerequisites

- Phases 1-4 are manually green.
- Quiz tests have targeted serialization and session cleanup.
- Four-word quiz-ready vocabulary seeding is deterministic.
- Fake dictionary handler is fail-closed and configurable per factory.

### Files to Add

- `VocabularyApp.WebApi.Tests/Api/QuizApiTests.cs`
- `VocabularyApp.WebApi.Tests/Api/DictionaryLookupApiTests.cs`
- `VocabularyApp.WebApi.Tests/Services/QuizServiceIntegrationTests.cs` only if exact persisted
  result checks cannot remain clear through API plus verification scope

### Files to Modify

- `TestDataSeeder.cs` for any missing quiz/lookup builders.
- `FakeDictionaryHttpMessageHandler.cs` for required response modes.
- `QuizApiCollection.cs` only if collection wiring needs refinement.
- No quiz or dictionary business logic should change.

### Implementation Steps

1. Ensure every quiz test clears static sessions before and after, even if an assertion
   fails (fixture disposal/finally path).
2. Add anonymous 401 tests for start, submit, and history.
3. Seed fewer than four eligible words and characterize start failure.
4. Seed four or more eligible caller-owned words and start a fixed-mode quiz.
5. Inspect raw JSON/DTO to prove the start response does not contain `CorrectOptionId`,
   `CorrectAnswer`, or another answer-key field. Do not infer secrecy merely because the DTO
   lacks a property.
6. Use User A's session with User B's token. Assert failure, no B results, and then prove A
   can still submit—foreign access must not consume the session.
7. Submit valid answers using response option IDs. Because the correct option is intentionally
   hidden, assertions should focus on successful scoring shape, result count/ownership,
   session ID, and history; do not require a known perfect score unless a controlled
   service-level seam safely supplies it.
8. Test empty/unknown session ID and invalid option ID. The current service treats invalid
   option IDs as unanswered/incorrect. Record this as current safe handling, not as the
   permanent desired validation contract. If the R4 target explicitly requires rejection,
   mark that assertion pending R4 rather than changing code.
9. Submit the same successfully consumed session again and characterize session-not-found.
10. Verify persisted `QuizResult` rows belong to A, reference A's `UserWord` rows, share the
    session ID, and appear only in A's history.
11. Do **not** assert that `UserWord.CorrectAnswers`, `TotalAttempts`, `LastReviewedAt`, or
    `LastCorrectAt` remaining unchanged is correct. Record absence of counter coverage as the
    R4-known gap.
12. Add dictionary cache-hit test with zero handler calls.
13. Add successful cache-miss response using realistic provider JSON; verify persisted word,
    definitions/audio, `WasFoundInCache = false`, and subsequent hit with no second call.
14. Add empty/not-found-equivalent and transport-failure provider tests using current API
    mapping.
15. Add unknown part-of-speech response and verify Noun fallback.
16. Attempt concurrent cache-miss characterization only if it can be deterministic without
    production locks/refactoring. Otherwise add it explicitly to R13 follow-up notes.

### Tests to Add

- Anonymous quiz start/submit/history rejected.
- Insufficient eligible vocabulary fails safely.
- Authenticated fixed-mode start succeeds.
- Start response does not expose answer keys.
- User B cannot submit User A's session and does not consume it.
- Valid submission persists owned `QuizResult` rows and owner-scoped history.
- Empty/unknown session and invalid option ID fail or score safely according to current
  behavior.
- Successful session cannot be submitted twice.
- Dictionary cache hit avoids HTTP.
- Successful cache miss persists provider data and becomes a hit.
- Provider empty/not-found and transport failure map safely.
- Unknown part of speech falls back to Noun.

### Important Constraints

- Codex must not run tests.
- Do not fix quiz counters/timestamps/transactions (R4).
- Do not persist quiz sessions or redesign static storage (R12).
- Do not expose or inject correct answers for API convenience.
- Do not require deterministic shuffle/order or exact expiration timestamps.
- Do not extract a dictionary provider, add retry/resilience, or coordinate cache misses
  (R10/R13).
- Do not make brittle assertions that freeze known defects.

### Manual Developer Verification

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
```

```powershell
dotnet test
```

Codex must not run tests. It may run:

```powershell
dotnet build VocabularyApp.sln
```

### Checkpoint

The R4 and R5 readiness gates are satisfied: quiz ownership/secrecy/result persistence and
vocabulary ownership are protected; static state is reliable; required lookup paths use no
network; known counter and concurrent-cache-miss gaps are documented without being fixed.

### Recommended Commit Message

`test: cover quiz ownership and dictionary lookup flows`

### Stop Condition

Stop after Phase 5 verification. Do not begin R4, R10, R12, or provider/session redesign.

## 17. Phase 6 — CI, Reliability, and Final Review

### Goal

Prove clean-checkout reliability, run backend tests automatically on GitHub events, and
perform the final R6 scope/readiness review.

### Current Prerequisites

- Phases 1-5 pass manually through both focused and solution commands.
- No test uses a production connection, production secret, or live dictionary service.
- Test parallel behavior is stable on the developer machine.

### Files to Add

- `.github/workflows/backend-tests.yml`
- `Docs/Updates/R6-Backend-Integration-Testing-Final-Review.md`

### Files to Modify

- Test infrastructure/tests only for reliability defects proven during manual repeated runs.
- No production file should change unless compilation/platform evidence identifies a narrow
  test-host issue requiring separate approval.

### Implementation Steps

1. Add a separate lightweight workflow rather than altering deployment behavior.
2. Trigger on pull requests and pushes to the repository's active integration branches;
   avoid deployment triggers.
3. Use a Windows runner initially because WebApi targets `win-x64`. Install/cache the .NET 8
   SDK as appropriate.
4. Run explicit commands in fail-fast order:
   - `dotnet restore VocabularyApp.sln`;
   - `dotnet build VocabularyApp.sln --configuration Release --no-restore`;
   - `dotnet test VocabularyApp.sln --configuration Release --no-build`.
5. Do not supply production secrets. Test configuration must be self-contained.
6. Do not permit live dictionary access or SQL Server dependency in CI.
7. Make test failure fail the workflow. Recommend configuring it as a required merge check,
   subject to repository owner approval.
8. Have the developer run the suite repeatedly and, where supported, with different xUnit
   scheduling/order to expose leakage. Do not hide failures by globally serializing tests.
9. Review test duration and remove only accidental duplication; retain R2 coverage.
10. Inspect status/diff for R4/R5/R7/R8/R10/R12 drift, migrations, connection strings,
    secrets, and production database changes.
11. Create the final R6 review under `Docs/Updates`, recording counts/coverage by capability,
    manual test results supplied by the developer, CI result, readiness gates, and deferred
    defects.

### Tests to Add

- No new business scenario is mandatory.
- Add a clean-host/repeated-run regression only if a concrete isolation defect was found.
- CI itself must execute the complete solution suite and fail on any failing test.

### Important Constraints

- Codex must not run tests.
- Do not modify deployment workflows or deploy.
- Do not add production secrets/connection strings to workflow or repository.
- Do not change `RuntimeIdentifier` merely to prefer a Linux runner; handle that as a
  separately reviewed platform decision if Windows CI is unacceptable.
- Do not globally disable parallelization to make CI green.
- Do not begin another remediation or fix deferred defects.

### Manual Developer Verification

Before pushing:

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
```

```powershell
dotnet test
```

Build verification:

```powershell
dotnet build VocabularyApp.sln --configuration Release
```

After push/PR, the developer reviews the `backend-tests` GitHub workflow and confirms restore,
build, and tests succeed. Codex must not run the local test commands or push changes.

### Checkpoint

The full suite runs reliably from a clean checkout, CI fails on backend regression, R4/R5/
R7/R8 gates are documented and satisfied at the appropriate level, and the final diff is
limited to R6 test foundation/coverage/CI/documentation.

### Recommended Commit Message

`ci: run backend integration tests`

### Stop Condition

Stop after Phase 6 and final review. Do not begin R4, R5, R7, R8, R10, R12, deployment, or
merge without explicit instruction.

## 18. R4 Readiness Gate

R4 may begin only after all are true:

- the real quiz API boots under `WebApplicationFactory`;
- anonymous start/submit/history requests are rejected;
- authenticated quiz creation works from caller-owned eligible vocabulary;
- the start response is proven not to expose correct option IDs/answers;
- User B cannot submit or consume User A's session;
- valid submission persists the expected number of caller-owned `QuizResult` rows;
- empty/unknown/invalid option/session behavior is characterized safely;
- duplicate submission behavior is understood;
- history is scoped to the authenticated user;
- static quiz session cleanup and targeted serialization are reliable; and
- tests intentionally do not require R4's future counter/timestamp behavior.

R4 should then add/adjust tests for its desired atomic `UserWord` counter/timestamp and
transaction semantics. R6 must not pre-implement those changes.

## 19. R5 Readiness Gate

R5 may begin only after all are true:

- vocabulary API ownership is covered through real JWT claims;
- User A cannot read/search User B vocabulary;
- User A cannot favorite or change the preferred definition of User B's entry;
- two users can independently save the same canonical word;
- current duplicate-save behavior is characterized without treating the existing composite
  identity as permanent;
- owner favorite state persists;
- preferred definition same-word validation and current part-of-speech conflict behavior are
  covered;
- EF user/word/definition/part-of-speech relationships can be seeded deterministically;
- each test receives an isolated database;
- generated IDs are returned from setup rather than assumed; and
- the suite passes without order dependence.

R5 may deliberately update tests when it changes `UserWord` identity/schema, but ownership
and user-visible intent must remain protected.

## 20. R7/R8 Readiness Gate

Before R7/R8 change response contracts or exception handling, representative API tests must
record current semantics for:

- 200 success for user, vocabulary, lookup, and quiz journeys;
- 400 model-validation and current business-failure mappings;
- 401 anonymous/malformed-token/invalid-credential behavior;
- 404 current lookup/profile/resource behavior where exposed;
- ownership failures (even if currently mapped to 400 rather than 403/404);
- duplicate/idempotent vocabulary behavior;
- service/provider failures and controller-local 500 mappings where deterministically
  injectable; and
- response envelopes and stable fields.

Prefer semantic JSON assertions and status codes. Avoid snapshots of full incidental error
messages or validation ordering. R7/R8 may intentionally update these tests in the same
change as the standardized contract/middleware behavior.

## 21. Files to Add

| Proposed File | Purpose | Phase |
| --- | --- | --- |
| `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs` | Real API test host, test configuration, SQLite/HTTP overrides | 1 |
| `VocabularyApp.WebApi.Tests/Api/ApiHostSmokeTests.cs` | Host, middleware, and SQLite smoke coverage | 1 |
| `VocabularyApp.WebApi.Tests/Infrastructure/ApiIntegrationTestBase.cs` | Per-test factory/client lifecycle | 2 |
| `VocabularyApp.WebApi.Tests/Infrastructure/TestDataSeeder.cs` | Deterministic domain relationship setup | 2 |
| `VocabularyApp.WebApi.Tests/Infrastructure/AuthenticationApiClientExtensions.cs` | Register/login/token/bearer helpers | 2 |
| `VocabularyApp.WebApi.Tests/Infrastructure/TestUser.cs` | Small user/token test result record if needed | 2 |
| `VocabularyApp.WebApi.Tests/Infrastructure/FakeDictionaryHttpMessageHandler.cs` | Fail-closed deterministic provider boundary | 2 |
| `VocabularyApp.WebApi.Tests/Infrastructure/ApiIsolationTests.cs` | Cross-factory isolation and multi-user proof | 2 |
| `VocabularyApp.WebApi.Tests/Infrastructure/QuizApiCollection.cs` | Quiz-only serialization definition | 2 |
| `VocabularyApp.WebApi/Properties/AssemblyInfo.cs` | Internals visibility for narrow session cleanup, if required | 2 |
| `VocabularyApp.WebApi.Tests/Api/UsersApiTests.cs` | Registration/login/profile/password-change API contracts | 3 |
| `VocabularyApp.WebApi.Tests/Api/AuthorizationApiTests.cs` | JWT middleware and protected-route behavior | 3 |
| `VocabularyApp.WebApi.Tests/Api/VocabularyApiTests.cs` | Vocabulary ownership, duplicates, favorites, preferred definitions | 4 |
| `VocabularyApp.WebApi.Tests/Services/WordServiceIntegrationTests.cs` | Optional precise relational WordService assertions | 4 |
| `VocabularyApp.WebApi.Tests/Api/QuizApiTests.cs` | Quiz auth, ownership, secrecy, submission, history | 5 |
| `VocabularyApp.WebApi.Tests/Api/DictionaryLookupApiTests.cs` | Cache/provider/mapping behavior | 5 |
| `VocabularyApp.WebApi.Tests/Services/QuizServiceIntegrationTests.cs` | Optional precise result persistence assertions | 5 |
| `.github/workflows/backend-tests.yml` | Clean restore/build/test workflow | 6 |
| `Docs/Updates/R6-Backend-Integration-Testing-Final-Review.md` | Final R6 coverage/scope/readiness record | 6 |

Optional files should be added only when they keep tests clearer than API plus verification
scope assertions. Avoid empty abstractions.

## 22. Files to Modify

| Existing File | Change | Why | Phase |
| --- | --- | --- | --- |
| `VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj` | Add .NET 8 `Microsoft.AspNetCore.Mvc.Testing` | Enable `WebApplicationFactory` | 1 |
| `VocabularyApp.WebApi/Program.cs` | Append public partial `Program` marker only | Make top-level host discoverable | 1 |
| `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs` | Extend handler/config/scope support | Complete isolation/external controls | 2, 5 |
| `VocabularyApp.WebApi/Services/QuizService.cs` | Add internal static clear-for-testing method only | Reliable cleanup of existing static state | 2 |
| `VocabularyApp.WebApi.Tests/Infrastructure/TestDataSeeder.cs` | Extend explicit vocabulary/quiz builders | Phase-specific deterministic setup | 4-5 |
| `VocabularyApp.WebApi.Tests/Infrastructure/FakeDictionaryHttpMessageHandler.cs` | Add provider response modes/call assertions | Lookup scenarios | 5 |

`VocabularyApp.sln` requires no change because the test project is already included.
Existing R2 fixture/security/service test files should remain unchanged unless compilation
reveals an unavoidable, narrowly scoped compatibility adjustment.

## 23. Files Explicitly Not to Modify

| Area/files | Why excluded from R6 |
| --- | --- |
| `VocabularyApp.Data/Models/UserWord.cs`, `ApplicationDbContext` relationship/index design, migrations | R5 owns `UserWord` identity/schema; R6 only characterizes it |
| `VocabularyApp.WebApi/Services/QuizService.cs` business/scoring/persistence logic | R4/R12 own counters, transactions, and session persistence; only cleanup seam permitted |
| `VocabularyApp.WebApi/Controllers/*.cs` | R7/R8 own API/error redesign; current behavior should be tested, not rewritten |
| `VocabularyApp.WebApi/DTOs/*.cs` | Contract redesign is out of scope |
| `VocabularyApp.WebApi/Services/WordService.cs` provider/cache logic | R10/R13 own provider architecture/resilience/concurrency |
| `VocabularyApp.WebApi/Security/**`, `UserService` password flows, `PasswordHelper` | R2 is complete; retain its implementation/tests |
| `VocabularyApp.WebApi/Helpers/JwtHelper.cs`, `Configuration/JwtSettings.cs` | JWT architecture is not R6; tests supply configuration only |
| `VocabularyApp.UI/**` | Frontend/Playwright testing is outside backend R6 |
| `VocabularyApp.Data/Migrations/**` | Test foundation requires no schema change |
| Deployment workflows/settings | Phase 6 adds a separate test workflow only; no deployment redesign |

If a test reveals a defect in one of these areas, stop, record it against the owning
remediation, and obtain approval before expanding scope.

## 24. Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Shared SQLite state creates order dependence | Fresh factory/open connection/database per test; disposal drops database; isolation test proves it |
| API factory accidentally retains SQL Server registration | Remove DbContext options/registration explicitly; assert SQLite provider; never probe production DB |
| Startup JWT binding happens before test settings | Add in-memory configuration in factory before host build; reuse validated test settings |
| JWT helper diverges from bearer middleware | Use the same issuer/audience/key/expiration values and real middleware; generate only exceptional tokens manually |
| Fake dictionary client leaks live HTTP | Factory-owned fail-closed primary handler; unexpected URI throws; assert call counts |
| Duplicate `IWordService` registrations defeat override | Remove/replace all relevant scoped/typed registrations and assert the fake handler receives cache misses |
| Static quiz sessions leak across tests | Internal clear-only seam, before/after cleanup, quiz-only nonparallel collection |
| Global parallelization is disabled to hide defects | Prohibit assembly-wide disable; isolate resources and serialize quiz collection only |
| Random/time behavior makes quiz tests flaky | Fixed modes, set/invariant assertions, bounded times; no exact ordering/timestamp assertions |
| Tests freeze R4 counter defects | Assert result persistence/ownership, explicitly omit unchanged-counter correctness |
| Tests freeze R5 composite identity | Separate current duplicate characterization from permanent ownership intent |
| API assertions become brittle before R7/R8 | Assert status/stable envelope/DB state; avoid incidental full-message snapshots |
| `EnsureCreated` hides migration defects | Accept for fast R6 suite; consider targeted SQL Server migration lane in R4/R5, not silently now |
| SQLite differs from SQL Server collation/transactions | Keep provider-sensitive cases identified; add a small optional SQL Server lane only when evidence requires |
| CI differs from developer environment | Start on Windows matching `win-x64`; run explicit restore/build/test; no secrets/network |
| Helper layer becomes a god fixture | Keep factory, seeder, auth extensions, and fake handler separate with narrow responsibilities |
| R2 tests are replaced or weakened | Treat them as retained baseline and run full solution at every phase checkpoint |

## 25. Manual Test Strategy

Codex must not run tests in any R6 implementation phase. The developer manually runs tests
after Codex completes each phase.

Default full command:

```powershell
dotnet test
```

Focused backend command:

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
```

Compile-only verification Codex may run when necessary:

```powershell
dotnet build VocabularyApp.sln
```

Do not add filters until stable traits or fully qualified class names exist in the
implementation. If focused debugging is needed later, the developer may use a fully
qualified-name filter documented by that phase, but every checkpoint still ends with the
full solution command.

During Phase 6 the developer should also run a clean Release build/test locally and review
the GitHub workflow after push/PR. CI running tests does not violate the instruction that
Codex must not execute them.

## 26. Git Commit Recommendations

After each phase is manually green and its diff is reviewed, recommend one commit:

1. `test: add API integration test host`
2. `test: add integration isolation and authentication helpers`
3. `test: cover authentication and authorization API flows`
4. `test: cover vocabulary ownership API flows`
5. `test: cover quiz ownership and dictionary lookup flows`
6. `ci: run backend integration tests`

Codex must not commit, push, open a PR, merge, or deploy unless the user explicitly asks.

## 27. Definition of Done

- [ ] Existing R2 unit, security, service/database, concurrency, and logging tests remain intact.
- [ ] A .NET 8-compatible `WebApplicationFactory<Program>` API harness exists.
- [ ] Production startup has no change beyond the minimal discoverability marker.
- [ ] The test host never uses or probes the production database.
- [ ] Test JWT settings are safe, deterministic, and accepted by real bearer middleware.
- [ ] The relational API test database is deterministic and isolated per test.
- [ ] An automated test proves data does not cross factory/test boundaries.
- [ ] Seed helpers return generated IDs and do not depend on row order.
- [ ] Multiple users can be created and authenticated reliably.
- [ ] Anonymous protected API requests are rejected.
- [ ] Valid authenticated requests resolve the correct user identity.
- [ ] Malformed/invalid/expired JWT behavior is covered.
- [ ] Registration/login/password-change representative HTTP paths are covered without duplicating all R2 cases.
- [ ] User A cannot read or search User B vocabulary.
- [ ] User A cannot mutate User B vocabulary.
- [ ] Favorites and preferred-definition ownership are covered.
- [ ] Preferred definitions are constrained to the correct word under current behavior.
- [ ] Current duplicate vocabulary behavior is characterized without freezing R5's defect/design.
- [ ] Vocabulary relationship setup is deterministic and verified relationally.
- [ ] Authenticated quiz creation and insufficient-vocabulary behavior are covered.
- [ ] Correct quiz answers/internal option IDs are not exposed by the start response.
- [ ] Quiz session ownership is covered.
- [ ] Invalid option/session submissions fail or score safely under characterized current behavior.
- [ ] Successful and duplicate submission behavior is covered.
- [ ] Quiz result persistence and owner-scoped history are covered.
- [ ] Tests do not declare stale `UserWord` counters/timestamps correct.
- [ ] Static quiz state is cleared between tests and quiz tests alone are serialized.
- [ ] Dictionary cache hit, successful miss, provider failure/not-found, and unknown part-of-speech behavior are covered.
- [ ] Dictionary tests cannot make live outbound requests.
- [ ] Concurrent cache-miss behavior is covered if deterministic or explicitly deferred to R13.
- [ ] Tests run reliably from a clean checkout on the developer machine.
- [ ] CI restores, builds, and runs backend tests automatically and fails on regression.
- [ ] No global parallelization disable hides isolation defects.
- [ ] No R4, R5, R7, R8, R10, R12, or unrelated remediation fix is bundled.
- [ ] No production database/schema change or migration is introduced for R6.
- [ ] Final R6 review records manual test and CI results plus readiness gates.

## 28. Implementation Readiness Assessment

**Ready with Conditions.** This plan is sufficiently specific for phase-by-phase execution.
The repository has the required starting test project and seams; ordinary implementation
work is not an unresolved condition.

The remaining policy conditions are:

1. The repository owner must choose whether `backend-tests` becomes a required merge check.
2. The initial plan assumes a Windows GitHub runner to match `win-x64`; choosing Linux would
   require a separately reviewed runtime-targeting decision if restore/build fails.
3. R4 must decide the desired invalid-option contract and counter transaction semantics; R6
   characterizes safety without preselecting them.
4. The team must confirm whether anonymous `POST /api/words/add` is intentional; R6 documents
   current behavior but does not change authorization.
5. A SQL Server container lane remains optional until R4/R5 demonstrates provider-sensitive
   behavior that must gate merges.

None of these conditions blocks starting Phase 1. Implement one phase, have the developer run
the documented tests, inspect the diff, and stop before requesting the next phase.
