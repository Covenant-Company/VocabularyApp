# R6 Backend Integration-Testing Analysis

## 1. Executive Summary

R6 is **partially complete, approximately 35% by capability**. That percentage is an
engineering estimate, not a test-count ratio. R2 materially changed the baseline assumed by
the original Plan of Action: the repository now has a .NET 8 xUnit project in the solution,
a relational SQLite fixture, reusable authentication test helpers, and strong unit and
service/database coverage for password security. Those assets complete most of the basic
test-project foundation and a substantial part of authentication testing.

The original R6 definition of done is not met. Critical API journeys do not yet run through
an application host, authentication/authorization middleware, routing, model binding, or
controller status-code mapping. There are no automated ownership, vocabulary, dictionary,
or quiz tests and no CI workflow that runs backend tests. The current database fixture is
well suited to focused R2 tests but does not provide per-test reset for a broader suite, and
the process-wide static quiz-session store creates cross-test and cross-host state risk.

The largest remaining gap is an **isolated `WebApplicationFactory` API test harness plus
ownership-focused HTTP coverage**. R6 should retain all useful R2 tests, add API integration
as a separate layer, continue using SQLite for the default fast suite, and add only targeted
SQL Server validation later where provider differences are material. Six implementation
phases are recommended. R6 is **Ready with Conditions** for implementation planning: the
repository supplies enough evidence, but the team must decide CI merge policy and whether a
small optional SQL Server lane is desired.

This analysis is source-only. No tests were run and no production or test source was
modified.

## 2. Original R6 Objective

The original R6 objective was to establish reliable backend integration testing before
major remediation, protecting authentication, authorization, ownership, EF relationships,
vocabulary behavior, dictionary lookup, and quiz behavior. Its preferred direction was a
`WebApplicationFactory` host, a relational test database with isolated setup/teardown,
reusable user/token helpers, authentication and ownership coverage first, and automatic CI
execution. Its definition of done was: **critical API journeys run automatically and
reliably from a clean checkout**.

Repository fact: that objective predated R2. Historical R2 analysis and planning explicitly
said no backend test project existed. Current repository evidence supersedes that statement:
`VocabularyApp.WebApi.Tests` now exists and is included in `VocabularyApp.sln`.

The target therefore should not be “create any backend tests.” It should be “extend the
existing layered test base into a reliable API and domain-integration safety net.”

## 3. Current Test Architecture

### Project inventory

| Item | Current state |
| --- | --- |
| Project | `VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj` |
| Target | `net8.0`, nullable and implicit usings enabled |
| Framework | xUnit 2.5.3 with `Microsoft.NET.Test.Sdk` 17.8.0 |
| Coverage collector | `coverlet.collector` 6.0.0 is referenced, but no CI/reporting configuration exists |
| Relational provider | `Microsoft.EntityFrameworkCore.Sqlite` 8.0.10 |
| Project references | `VocabularyApp.WebApi` and `VocabularyApp.Data` |
| Solution | Included in `VocabularyApp.sln` with normal build configurations |
| HTTP host package | None; `Microsoft.AspNetCore.Mvc.Testing` is not referenced |
| Organization | `Infrastructure`, `Security`, and `Services` folders |
| Current execution boundary | Pure security classes or services called directly; no controller is invoked over HTTP |

### Test-file inventory

| Test file | Layer | Dependencies | Protected behavior |
| --- | --- | --- | --- |
| `Infrastructure/RelationalDatabaseFixtureTests.cs` | Database integration | SQLite, EF model, fresh contexts | A `User` persists and reloads relationally |
| `Security/LegacyPasswordVerifierTests.cs` | Unit | Cryptography only | Exact historical algorithm, strict shape/canonical Base64, malformed input, fixed behavior |
| `Security/PasswordServiceTests.cs` | Unit/component | Real or controlled framework hasher, legacy verifier | Modern hashing/verification, legacy migration outcome, rehash, malformed/unknown mapping |
| `Security/PasswordVerificationOutcomeTests.cs` | Unit | Outcome type only | Replacement invariants and non-sensitive `ToString` |
| `Services/UserServiceAuthenticationTests.cs` | Service/database integration | SQLite, real/controlled password service, real JWT helper | Modern registration, duplicate handling, legacy/modern password change, nonmutation |
| `Services/LoginMigrationTests.cs` | Service/database integration | SQLite, password service, JWT helper, save interceptor | Legacy migration, modern login, rehash, malformed input, required-save failure, login conflict |
| `Services/CredentialConcurrencyTests.cs` | EF and service/database integration | Two SQLite contexts, controlled hasher | Stale writes conflict and newer credentials survive |
| `Services/AuthenticationLoggingTests.cs` | Service/database integration | SQLite, capturing logger, interceptors | Credential sentinels absent from formatted messages and exception rendering |

No test file currently exercises `UsersController`, `WordsController`, `QuizController`,
ASP.NET authentication/authorization middleware, HTTP serialization, model validation,
routing, CORS, or status-code contracts.

## 4. R2 Test Infrastructure Already Completed

R2 completed and should be credited for the following R6 foundation:

- A backend test project exists, targets the same .NET major version, references WebApi and
  Data, and participates in solution builds.
- `RelationalDatabaseFixture` holds an open SQLite in-memory connection, calls
  `EnsureCreatedAsync`, and creates multiple fresh `ApplicationDbContext` instances over the
  same relational database.
- Tests reload persisted state through fresh contexts, reducing false positives from EF
  tracking.
- `TestJwtSettingsFactory` supplies a deterministic, sufficiently long test signing key and
  fixed issuer/audience/expiration.
- `ControlledPasswordHasher` provides narrow deterministic control over framework hashing,
  verification, rehash, callbacks, and call counts.
- `CapturingLogger<T>` safely captures level, event ID, formatted text, and exception.
- Save interceptors are already used to force persistence failures without replacing EF.
- SQLite affected-row behavior is used to characterize password concurrency.
- Authentication coverage is unusually strong at the service/security layer.

These are durable assets. R6 should extend them rather than replace them with an all-HTTP
suite.

## 5. R6 Capability Status Matrix

| Capability | Status | Evidence and rationale |
| --- | --- | --- |
| Test Project Foundation | **Complete** | Test project exists, is in the solution, references WebApi/Data, and has standard xUnit tooling. Clean-checkout execution is plausible but not CI-proven. |
| Relational Database Testing | **Partial** | SQLite, schema creation, shared fresh contexts, and teardown-by-connection-disposal exist. There is no per-test reset, general seeder, migration-based schema boot, or API-host database lifecycle. |
| Authentication Coverage | **Partial** | Registration/login/password-change and modern/legacy credentials are strongly covered at service/database level. HTTP endpoints, middleware, token acceptance/rejection, validation, and status mapping are not. |
| Authorization Coverage | **Missing** | No anonymous-versus-authenticated HTTP tests, invalid/expired token tests, or claim extraction tests exist. There are no roles in current application code, so role testing is **Not Yet Appropriate**. |
| Ownership Coverage | **Missing** | User-scoped predicates exist in `WordService` and `QuizService`, but no test proves cross-user read/mutation/session isolation. |
| Vocabulary Integration Coverage | **Missing** | No automated tests cover add, duplicate behavior, retrieval, search/filter, favorite, preferred definition, invalid IDs, or ownership. |
| Dictionary Lookup Coverage | **Missing** | No cache-hit/cache-miss/provider response/failure/part-of-speech/concurrency tests exist. |
| Quiz Coverage | **Missing** | No tests cover start, response secrecy, ownership, answer handling, persistence, history, or session reuse. |
| HTTP/API Integration Harness | **Missing** | No `WebApplicationFactory`, test server, `HttpClient`, or HTTP-level tests exist. |
| CI Test Execution | **Missing** | `.github/workflows` exists but contains no workflow files; no other discovered script runs backend tests automatically. |

Overall completion is estimated at **35%**: foundational plumbing and the authentication
core are meaningful, but five of the seven original product-risk areas and the entire HTTP
and CI layers remain uncovered.

## 6. Test Infrastructure Quality Review

### Strengths

- `RelationalDatabaseFixture.InitializeAsync` opens one SQLite `Data Source=:memory:`
  connection and keeps it alive for the fixture lifetime. This correctly preserves the
  in-memory database across contexts.
- `CreateContext()` reuses immutable options; the interceptor overload rebuilds options
  against the same connection. Both support realistic EF tracking and affected-row behavior.
- `EnsureCreatedAsync` creates the complete current model and seeded parts of speech without
  external infrastructure.
- Fixture disposal closes the connection, reliably discarding that fixture database.
- Most service tests create unique usernames/emails with GUID suffixes.
- Controlled helpers are narrow and behavior-specific rather than broad application mocks.

### Limitations

- `IClassFixture<RelationalDatabaseFixture>` creates one database per test class, not per
  test. Test methods in the same class leave rows behind. Current GUID-based data avoids
  most collisions but is not a reset strategy and makes deterministic numeric IDs unsafe.
- `RelationalDatabaseFixtureTests` uses fixed `fixture-user` data; it is safe only because
  that class currently has one test.
- `EnsureCreated` validates the current EF model, not the migrations chain. That is useful
  for fast integration tests but cannot detect a broken migration sequence.
- There is no transaction-per-test, table reset, checkpoint mechanism, or deterministic
  domain seed builder for users/words/definitions/user words/quiz results.
- SQLite connections are not designed for concurrent commands from many contexts. Existing
  concurrency tests sequence operations; broader parallel tests must not share the same
  connection concurrently.
- xUnit classes can run in parallel, but each current class gets a separate fixture/database,
  which limits cross-class database collisions. This advantage will disappear if one API
  factory/database is shared globally without a reset design.
- `QuizService.QuizSessions` is a static `ConcurrentDictionary`. It survives service scopes
  and factory instances within the same test process. Session IDs are GUIDs, so collisions
  are unlikely, but leaked sessions and test-order coupling remain possible.
- Quiz time uses `DateTime.UtcNow` and selection uses `Random.Shared`; exact question order,
  mixed mode, and expiration are not deterministic.

Recommendation: retain the R2 fixture for its current tests. Add a separate API fixture
with a database-per-test or database-per-test-class strategy and explicit reset. Prefer
unique SQLite files or uniquely named shared in-memory databases per API test class over one
global mutable database. Add small seed builders returning created IDs. Do not disable all
xUnit parallelization globally; isolate resources first, and serialize only collections
that truly share a host/static quiz state.

## 7. WebApplicationFactory Assessment

`WebApplicationFactory` is still recommended and is the central missing R6 capability.

Current tests are service-level integration tests: they combine real services, EF, SQLite,
and selected real collaborators, but bypass the HTTP host. `WebApplicationFactory` would add
coverage for:

- routing and endpoint discovery;
- JSON input/output serialization;
- `[ApiController]` model binding and automatic validation behavior;
- `[Authorize]` enforcement before controller execution;
- JWT bearer validation and claim propagation;
- controller mapping to 200/400/401/404/500 responses;
- DI wiring and service lifetimes; and
- middleware order (`UseCors`, `UseAuthentication`, `UseAuthorization`, controllers, and
  fallback routing).

The top-level `Program.cs` does not declare a public `Program` type. The compiler-generated
type is not a stable public generic anchor for a separate test assembly. The conventional
minimal change is to append `public partial class Program { }` to `Program.cs`, or expose an
equivalent public marker in the WebApi assembly. That is a test-host discoverability seam,
not an authentication redesign.

R6 should include both layers:

- Keep unit and service/database tests for exhaustive password outcomes, forced failures,
  and precise EF state assertions.
- Add a smaller API suite for critical end-to-end contracts and boundaries.

Rewriting all R2 tests as HTTP tests would be slower, less diagnostic, and unnecessarily
duplicate detailed password-service coverage.

## 8. API Host/Testability Assessment

`VocabularyApp.WebApi/Program.cs` can be hosted safely after several test-only overrides:

| Startup concern | Current behavior | Test requirement |
| --- | --- | --- |
| DbContext | Registers SQL Server from `DefaultConnection` | Remove/replace the DbContext registration with isolated SQLite before service resolution |
| JWT configuration | `JwtSettings.BindAndValidate` runs during startup and rejects a missing/short external secret | Inject `JwtSettings:SecretKey`, issuer, audience, and expiration through test host configuration before `Program` binds settings |
| Authentication | Real JWT bearer middleware uses bound validation parameters | Prefer real signed test tokens from registration/login or a helper using the same test settings; use a fake auth scheme only for tests specifically outside JWT behavior |
| Authorization | Standard `AddAuthorization` and `[Authorize]` | No production change required |
| CORS | Configured from settings with defaults | Usually irrelevant to `HttpClient`; one small policy smoke test is optional, not core R6 |
| Swagger/static files/fallback | Always enabled | Should not block API routes; avoid treating fallback HTML as a successful missing API response |
| Dictionary HTTP | `AddHttpClient<IWordService, WordService>()`; `WordService` calls a fixed external URL | Replace the typed client's primary handler or registration with a deterministic fake handler; never call the public provider in tests |
| Database startup | No migration or `EnsureCreated` call in `Program` | The API fixture must create/reset schema explicitly after host services are built |
| Environment | No test-specific startup branch | Set environment to `Testing` for predictable configuration/logging, even if production code does not currently branch on it |
| Runtime target | WebApi specifies `RuntimeIdentifier` `win-x64` | Verify clean CI behavior, especially if CI is Linux; this may constrain host execution or restore and should not be silently ignored |

The double registration of `IWordService` (`AddScoped` followed by
`AddHttpClient<IWordService, WordService>`) should be treated carefully when overriding
tests; the later typed-client registration is the effective resolution path. The API factory
should remove all relevant `IWordService`/typed-client registrations or configure the
expected handler deliberately.

No startup migration automatically touches production, which is good for test safety. The
fixture, not production startup, should own test schema creation.

## 9. Authentication & Authorization Coverage

### Already protected

R2 source tests protect:

- strict legacy recognition and exact historical verification;
- modern framework hash creation and verification;
- legacy migration and modern `SuccessRehashNeeded` outcomes;
- registration persistence with a modern hash;
- duplicate username and email service behavior;
- legacy-to-modern and modern-to-modern password change;
- wrong/malformed/unknown credential nonmutation;
- login migration, rehash persistence, required-save failure, and concurrency failure;
- password-write optimistic concurrency; and
- credential logging safety.

### Missing at HTTP/API level

High-value API tests should cover:

1. `POST /api/users/register`: valid request returns 200 and token; invalid model returns
   400; duplicate username/email maps to 400.
2. `POST /api/users/login`: valid modern and one representative legacy login return 200;
   wrong/malformed credentials return 401; response contract and token are usable.
3. `GET /api/users/profile`: anonymous request is rejected by middleware; valid JWT returns
   only the token user's profile; invalid signature, wrong issuer/audience, and expired token
   return 401.
4. `POST /api/users/change-password`: anonymous request returns 401; authenticated request
   derives identity from the token rather than request data; success/incorrect-current
   mapping is characterized.
5. `GET /api/users/validate-token`: valid token returns current user; deleted/nonexistent
   user behavior maps to 401.
6. Model binding: missing/invalid registration fields and too-short new password receive the
   actual `[ApiController]` response contract. Direct controller invocation would not prove
   this behavior.

Do not duplicate every R2 password edge case over HTTP. One legacy migration journey, one
modern journey, representative failures, and middleware/token cases are enough.

There are no application roles or role policies. Role/claim-policy tests beyond the user ID
claim are not currently appropriate.

## 10. Ownership Coverage

Important current ownership boundaries are:

| Resource/action | Endpoint | Enforcement evidence | Required R6 scenario |
| --- | --- | --- | --- |
| Read vocabulary | `GET /api/words/vocabulary` | `WordService.GetUserVocabularyAsync` filters `uw.UserId == userId` (line 366) | User A response excludes User B entries |
| Search vocabulary | `GET /api/words/vocabulary/search` | `SearchUserVocabularyAsync` filters by user ID (line 456) | Search cannot reveal User B words/notes/stats |
| Add vocabulary | `POST /api/words/vocabulary/add` | Controller supplies claim user ID; duplicate check includes user ID (lines 213-218) | Addition belongs to token user; same canonical word may be independently saved by B |
| Favorite mutation | `PUT /api/words/vocabulary/{id}/favorite` | Query includes both `Id` and `UserId` (lines 319-320) | A cannot favorite B's `UserWord`; B row remains unchanged |
| Preferred definition | `PUT /api/words/vocabulary/{id}/preferred-definition` | Initial `UserWord` query includes owner (lines 255-256); selected definition is constrained to the same word | A cannot modify B; cross-word definition is rejected; conflict behavior is characterized |
| Start quiz | `POST /api/quiz/start` | Vocabulary query filters `UserId` (QuizService line 32) | Questions use only A's vocabulary |
| Submit quiz | `POST /api/quiz/submit` | Session stores owner and rejects mismatched user (lines 111-116, 157-160) | B cannot submit A's session and creates no results |
| Quiz history | `GET /api/quiz/history` | `QuizResults` query filters `UserId` (line 285) | A cannot read B's history |

There is no endpoint that directly reads a single `UserWord`; coverage should target the
actual exposed list/search/mutation endpoints rather than inventing one. `SampleSentence`
and `ChatHistory` are user-owned entities but have no current controller/service endpoints,
so API ownership tests for them are not yet appropriate.

## 11. Vocabulary Coverage

No current automated test protects vocabulary behavior. The highest-value API/database
tests are:

1. Authenticated add creates the canonical word if absent and one `UserWord` owned by the
   token user with the resolved part of speech and preferred definition.
2. Repeating the same user/word/part-of-speech add returns the current idempotent success
   message and does not create a duplicate. Also retain a database-level unique-index test
   for `(UserId, WordId, PartOfSpeechId)`.
3. Two users can save the same canonical word independently.
4. Vocabulary list returns only the caller's rows, with pagination, favorite, preferred
   definition, and counters mapped from persisted state.
5. Term and starts-with filters do not cross ownership boundaries; empty search returns an
   empty successful result.
6. Favorite mutation succeeds for the owner and cannot mutate another user's row.
7. Preferred-definition mutation succeeds for a definition belonging to the same word;
   rejects another word's definition; enforces ownership; and characterizes the
   part-of-speech conflict rule.
8. Invalid or missing `UserWord`/definition IDs return the current API error mapping and do
   not mutate data.

`POST /api/words/add` is described as an admin endpoint but has no `[Authorize]` or role
guard. R6 should add a characterization test documenting its current anonymous reachability
and flag the policy gap; changing authorization belongs in an explicitly approved security
change, not silently in test-foundation work.

R5 will change `UserWord` identity/database behavior. The ownership and duplicate tests
above are the minimum safety net before that migration.

## 12. Dictionary Lookup Coverage

`WordService.LookupWordAsync` first queries the local canonical dictionary. On a miss it
calls the fixed dictionaryapi.dev URL through injected `HttpClient`, persists a `Word`, then
persists mapped definitions. Unknown part-of-speech text falls back to seeded `Noun`.
External exceptions are logged and converted to “No definitions found.”

R6 should cover now:

- local cache hit makes no external HTTP request and reports `WasFoundInCache = true`;
- authenticated cache hit computes `IsInUserVocabulary` only for the caller;
- cache miss with a fake successful provider response persists the word/definitions/audio
  and reports `WasFoundInCache = false`;
- provider empty/404-equivalent response and transport exception return the current not-found
  API behavior without external network access;
- unknown provider part of speech uses the current Noun fallback;
- repeated lookup after a successful miss uses the database cache.

A concurrent cache-miss test is valuable because `Word.Text` is unique and the current
two-save flow can race. However, if it exposes the known lack of lookup coordination, R6
should document the result for R10/R13 rather than add locking or provider architecture.

R10 is the appropriate place for a dedicated dictionary-provider abstraction or richer
failure policy. R6 can use a fake `HttpMessageHandler` against the existing `HttpClient`
seam. Provider retry, circuit breaker, detailed timeout taxonomy, and broad concurrency
redesign should be deferred.

## 13. Quiz Coverage

Current quiz behavior is untested and is the most important prerequisite for R4.

### Current repository facts

- `QuizController` is class-level `[Authorize]` for start, submit, and history.
- `StartQuizAsync` requires at least four unique saved words with usable definitions,
  clamps question count to 1-20, normalizes mode, shuffles with `Random.Shared`, and creates
  a 30-minute process-memory session.
- `QuizStartResponseDto` exposes options but not `CorrectOptionId`; the internal state retains
  it.
- Sessions are stored in a static `ConcurrentDictionary<Guid, QuizSessionState>` and include
  `UserId`.
- Submit rejects empty/missing/foreign/expired sessions.
- Duplicate submitted answers for one question are grouped and the first is used.
- An invalid option ID is treated as an unanswered/incorrect selection rather than rejected.
- A successful submission persists one `QuizResult` per still-owned session question and
  removes the session. A second submission therefore returns session-not-found.
- Stale questions whose `UserWord` was removed are skipped during persistence.
- History groups results by `QuizSessionId` and filters by user.
- `UserWord.CorrectAnswers`, `TotalAttempts`, `LastReviewedAt`, and `LastCorrectAt` are not
  updated by submission. This is a known behavior area for R4, not a contract R6 should bless.

### R6 tests to add

Before R4, add characterization/integration coverage for:

1. Anonymous start/submit/history return 401.
2. Fewer than four eligible words fails; four eligible caller-owned words starts a quiz.
3. Start response contains no internal correct-option ID or other direct answer key.
4. Fixed mode produces the requested question type; avoid asserting randomized ordering or
   exact mixed-mode distribution.
5. User B cannot submit User A's session, and the failed attempt does not consume A's session
   or persist results.
6. Empty and unknown session IDs fail safely.
7. Submission persists `QuizResult` rows owned by the caller and history returns only that
   user's grouped session.
8. A successful session cannot be submitted twice.
9. Missing answers and invalid option IDs are characterized carefully. If R4 is intended to
   reject invalid options, mark the current behavior as a known defect rather than asserting
   it as permanent correctness.
10. Deleting a `UserWord` after start does not create a foreign/stale result.

Do not write an assertion that counters “correctly remain unchanged.” Instead, write result
persistence tests and separately document that R4 must introduce the desired atomic counter
updates. Randomness, clock control, session persistence, and transaction redesign belong to
R4/R12 unless a minimal test-only reset seam is required for reliable R6 execution.

## 14. Test Isolation & Parallel Safety

Current database tests are isolated by class, not by method. This works because:

- xUnit creates a separate `RelationalDatabaseFixture` for each class;
- each fixture creates a separate in-memory database connection; and
- most mutable records use GUID-suffixed natural keys.

Within a class, method order must not matter, but rows accumulate. Current tests generally
query by unique IDs/usernames, limiting interaction. Broad R6 seed data will use shared words,
parts of speech, and predictable IDs, so accumulation becomes fragile.

API integration introduces additional risks:

- sharing one `WebApplicationFactory` across classes may share one database;
- SQLite permits different threading patterns than SQL Server and one shared connection can
  become a bottleneck;
- static quiz sessions survive request scopes and can survive factory recreation in the
  same process;
- `Random.Shared` and wall-clock expiration make exact quiz assertions nondeterministic;
- fixed usernames/emails violate unique indexes when reset is incomplete.

Recommended design:

1. Use a unique database per API test class or per test. For this suite size, per-class host
   plus explicit reset before each test is a reasonable balance.
2. Seed by natural labels and return IDs; never assume identity starts at 1 except seeded
   parts-of-speech IDs already defined by the model.
3. Give every user unique credentials even when database reset is expected.
4. Add an explicit quiz-session reset seam accessible to tests, or place quiz tests in one
   serialized xUnit collection until R12 removes static process state. Do not globally
   disable parallelization.
5. Avoid assertions on question order and exact timestamps. Assert bounded expiration and
   stable mode/ownership/content invariants.
6. Distinguish test-runner worker limitations in a development environment from repository
   isolation defects; R6 must make ordinary `dotnet test` reliable independent of Codex's
   instruction not to execute it during remediation.

## 15. Test Database Strategy

SQLite should remain the default R6 relational provider.

### Strengths

- Fast, local, and dependency-free for clean-checkout execution.
- Exercises relational constraints, change tracking, includes, transactions, and affected
  row counts unlike EF InMemory.
- Already proven useful for R2 credential concurrency.
- Supports multiple fresh contexts and is suitable for API-host replacement.

### Compatibility risks

- Production is SQL Server; string equality/collation and case sensitivity differ.
- `ToLower`, `Contains`, and ordering translations can differ.
- SQL Server `nvarchar`, date/time, decimal/rounding, and generated SQL behavior are not
  identical to SQLite.
- Unique constraint exception details and concurrent-write locking differ.
- `EnsureCreated` does not validate SQL Server migrations.

Recommendation: use SQLite for the full default R6 suite. Add a very small optional SQL
Server integration lane only for behavior proven provider-sensitive: case-insensitive unique
username/email/search assumptions, migration application, and any transaction/concurrency
semantics introduced by R4/R5. A mandatory SQL Server container for every local test run is
disproportionate now and may reduce adoption. Container testing can be deferred until R4/R5
implementation planning determines that SQL Server-specific transactional behavior must be
gated.

## 16. External Dependency/Test Seam Analysis

| Dependency | Current seam | Smallest R6 control |
| --- | --- | --- |
| Database | DI `ApplicationDbContext` | Replace SQL Server registration in factory; create/reset isolated SQLite schema |
| JWT configuration | Configuration-bound immutable `JwtSettings` | Supply test configuration before host startup; sign real tokens with the same settings |
| Dictionary provider | Injected `HttpClient`, fixed URL | Fake `HttpMessageHandler` or test typed-client registration; no live network |
| Clock | Direct `DateTime.UtcNow` | Use range assertions for R6; defer a clock abstraction unless expiration tests cannot be reliable |
| Randomness | `Random.Shared` | Request fixed quiz mode and assert sets/invariants; defer random abstraction |
| Quiz sessions | Private static dictionary | Minimal internal reset/test seam or serialized collection; long-term replacement belongs to R12 |
| Password hashing | `IPasswordService`, controlled hasher | Reuse real application service for API journeys and controlled helper only for focused service cases |
| Logging | `CapturingLogger<T>` | Retain for service tests; API log capture is optional unless validating an HTTP-specific leak |

Prefer real controllers, middleware, services, EF, JWT signing, and serialization. Fake only
the external dictionary boundary and nondeterministic infrastructure that cannot otherwise
be isolated. Broad mocking would defeat R6's purpose.

## 17. CI Readiness

No workflow file exists under `.github/workflows`, and no discovered build script runs the
backend test project. Backend tests therefore do not currently run automatically on push or
pull request.

R6 should add a CI workflow that:

1. checks out the repository;
2. installs the .NET 8 SDK;
3. restores the solution;
4. builds in a clean configuration;
5. runs `dotnet test VocabularyApp.sln --no-build` (or the backend project explicitly);
6. uploads test results/coverage only if the team wants those artifacts; and
7. does not require production secrets or live dictionary/database access.

The WebApi project's `win-x64` runtime identifier is a potential Linux-runner issue. Confirm
the chosen runner/restore behavior during R6 implementation. A Windows runner is the lowest
risk initial option; changing production runtime targeting is outside test-foundation scope
unless clean CI proves it necessary.

The current remediation instruction that Codex must not run tests is a session safety rule,
not a reason to omit CI. Once R6 changes are committed/pushed under the team's normal
workflow, CI should run tests automatically and preferably block merge on failure. Whether
it is a required branch check is a team policy decision.

## 18. Layered Testing Strategy

Use three complementary layers:

1. **Pure unit/component tests** — strict parsing, password outcomes, DTO-independent pure
   rules, and any later deterministic quiz scoring rule. Fast and exhaustive.
2. **Service/database integration tests** — real EF relational behavior, persistence,
   transactions, concurrency, provider mapping, and forced failures. Retain R2 here and add
   focused WordService/QuizService tests where precise database assertions are clearer than
   HTTP.
3. **API integration tests** — a smaller critical-path suite through
   `WebApplicationFactory`, real middleware, JWT, controllers, serialization, and DI.

Avoid duplicating every assertion at every layer. API tests should prove wiring and public
contracts; service tests should prove detailed state transitions and edge cases; units should
prove combinatorial pure rules.

## 19. Tests to Retain

Retain all current R2 tests unless later behavior intentionally changes:

- `LegacyPasswordVerifierTests` and `PasswordVerificationOutcomeTests` are precise, fast,
  security-critical unit tests.
- `PasswordServiceTests` efficiently cover outcome combinations that would be cumbersome
  over HTTP.
- `UserServiceAuthenticationTests` protect database state and duplicate behavior.
- `LoginMigrationTests` protect token-before-save ordering and forced persistence failures.
- `CredentialConcurrencyTests` protect relational affected-row semantics and stale writes.
- `AuthenticationLoggingTests` protect formatted messages and exception text from secret
  leakage.
- `RelationalDatabaseFixtureTests` is a useful smoke test for the fixture, although it could
  later be folded into fixture self-validation if maintenance cost grows.

The test project currently has 54 `[Fact]` methods plus theory cases. Count alone should not
be used as R6 completion evidence; almost all cover one security domain.

## 20. Gaps to Address

Ranked by risk and value:

1. **API host and middleware coverage** — without it, no `[Authorize]`, token, validation,
   routing, or status-code behavior is protected.
2. **Ownership boundaries** — cross-user vocabulary mutations and quiz sessions are high
   security risks and prerequisites for later data refactoring.
3. **Quiz characterization and persistence** — essential before R4 changes transactions and
   counters.
4. **Vocabulary/EF relationship behavior** — essential before R5 changes `UserWord`
   identity/schema.
5. **Reliable database/session isolation** — necessary for a growing suite and CI.
6. **Dictionary fake and cache-path coverage** — protects current lookup behavior without
   binding R6 to R10's future architecture.
7. **CI execution** — required to satisfy “automatically and reliably from a clean checkout.”

### Redundant or weak-test observations

No current test is sufficiently harmful to delete during R6. Some helper creation logic is
duplicated across service test classes (`CreateUserService`, seed/reload helpers), but it is
clear and local. Consolidate only stable seed/factory behavior needed by new API tests; do
not create a large testing framework. The fixture smoke test asserts only one entity, but it
has value as a relational baseline. Logging tests intentionally overlap authentication paths
because their assertion target is different (absence of secrets).

## 21. Scope Boundaries

### In R6

- Public `Program` test-host discoverability seam.
- `Microsoft.AspNetCore.Mvc.Testing` test dependency and `WebApplicationFactory` harness.
- Isolated relational SQLite database lifecycle and deterministic domain seed builders.
- Real JWT test configuration/token helpers.
- Fake external dictionary HTTP handler.
- Auth/authz HTTP tests.
- Cross-user ownership tests for vocabulary and quiz.
- Critical vocabulary and dictionary cache-path tests.
- Quiz characterization tests that avoid locking known R4 defects.
- Narrow quiz-session cleanup/serialization needed for test reliability.
- CI build/test execution.

### Out of R6

- Fixing quiz counters/transactions (R4).
- `UserWord` schema/identity migration (R5).
- API response-contract redesign (R7).
- Global exception middleware redesign (R8).
- Dictionary-provider architecture, retry, or resilience redesign (R10/R13).
- Persistent quiz-session redesign (R12).
- Full SQL Server container requirement unless provider evidence makes it necessary.
- Frontend/Playwright testing.
- Fixing unrelated security or business defects discovered by characterization; record and
  route them to the owning remediation item.

## 22. Minimum Safe Completion Set

### Before R4

Must have:

- API authorization tests for all quiz endpoints;
- start with eligible caller-owned vocabulary;
- start response proves correct option IDs are not exposed;
- foreign-session submission is rejected without consuming the owner session;
- valid submission persists caller-owned `QuizResult` rows;
- duplicate submission fails;
- history is user-scoped; and
- a reliable static-session reset/serialization approach.

Do not lock unchanged `UserWord` counters as correct. R4 should add tests for its desired
atomic counter/timestamp behavior.

### Before R5

Must have:

- add vocabulary and duplicate behavior;
- independent ownership of the same canonical word by two users;
- user-scoped list/search;
- favorite ownership;
- preferred-definition ownership, same-word validation, and part-of-speech conflict;
- database relationship/unique constraint characterization; and
- deterministic seed/reset support.

### Before R7/R8

Must have:

- HTTP response/status snapshots or semantic assertions for representative success,
  validation, authentication, not-found, and service-failure cases across Users, Words, and
  Quiz controllers;
- middleware authorization behavior; and
- tests that distinguish controller-local exception mapping from future centralized error
  handling.

The minimum meaningful R6 completion is therefore not “add `WebApplicationFactory`.” It is
the host plus isolation, auth/authz, ownership, core vocabulary, core quiz, and CI checkpoints.

## 23. Recommended Implementation Phases

### Phase 1 — API host and deterministic configuration

- **Goal:** Boot the real API without production secrets, SQL Server, or external network.
- **Likely files:** `Program.cs`; test project file; new `Infrastructure/VocabularyAppApiFactory.cs`.
- **Tests:** Host starts; health substitute such as Swagger/API route discovery; fixture schema exists.
- **Dependencies:** Public `Program`, test JWT configuration, SQLite override.
- **Risk:** Startup-time JWT binding and duplicate service registrations.
- **Checkpoint:** A test client reaches an API route against isolated SQLite.

### Phase 2 — Isolation, seeding, and external seams

- **Goal:** Make repeated/parallel runs deterministic.
- **Likely files:** New API database reset/seed builders, token/client helper, fake dictionary handler; possibly a minimal quiz-session test reset seam.
- **Tests:** Two tests can create the same natural seed independently; no live external call occurs.
- **Dependencies:** Phase 1 factory.
- **Risk:** Static quiz state and shared SQLite connection lifetime.
- **Checkpoint:** Repeated clean runs have no data/session leakage by design.

### Phase 3 — Authentication and authorization API journeys

- **Goal:** Cover public auth endpoints, JWT middleware, protected routes, claims, validation, and status mappings.
- **Likely tests:** `Api/UsersApiTests.cs`, `Api/AuthorizationApiTests.cs`.
- **Dependencies:** Real test JWT helper and seeded users.
- **Risk:** Duplicating R2 edge cases; keep HTTP set representative.
- **Checkpoint:** Anonymous, valid-token, invalid-token, registration, login, profile, and password-change journeys are protected.

### Phase 4 — Ownership and vocabulary integration

- **Goal:** Protect cross-user boundaries and `UserWord` behavior before R5.
- **Likely tests:** `Api/VocabularyApiTests.cs`, `Services/WordServiceIntegrationTests.cs` if precise EF checks are needed.
- **Dependencies:** Two-user and canonical-word seed builders.
- **Risk:** SQLite collation differences and existing endpoint error semantics.
- **Checkpoint:** Cross-user reads/mutations fail and owner behavior/duplicates/preferences/favorites persist correctly.

### Phase 5 — Dictionary and quiz characterization

- **Goal:** Cover deterministic lookup boundaries and current safe quiz contracts before R4/R10/R12.
- **Likely tests:** `Api/DictionaryLookupApiTests.cs`, `Api/QuizApiTests.cs`; fake HTTP handler.
- **Dependencies:** Static-session isolation and vocabulary seeds.
- **Risk:** Randomness, time, static sessions, and accidentally blessing known R4 defects.
- **Checkpoint:** Cache hit/miss/failure and critical quiz ownership/secrecy/persistence/history behaviors are protected.

### Phase 6 — CI and reliability gate

- **Goal:** Make clean-checkout execution automatic and stable.
- **Likely files:** `.github/workflows/backend-tests.yml`; optional xUnit configuration only if evidence requires collection-level serialization.
- **Tests:** No new domain behavior required; run full suite and any agreed provider-specific lane.
- **Dependencies:** All prior phases stable locally.
- **Risk:** `win-x64` targeting on runner, native SQLite assets, and workflow secret assumptions.
- **Checkpoint:** CI restores, builds, and runs backend tests without production credentials or network dependencies; merge protection policy is documented.

## 24. Files Likely to Add

| File | Current Responsibility | Expected R6 Impact | Reason |
| --- | --- | --- | --- |
| `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppApiFactory.cs` | Does not exist | Add isolated `WebApplicationFactory<Program>` and configuration/service overrides | Central API host |
| `VocabularyApp.WebApi.Tests/Infrastructure/TestDatabase.cs` or equivalent | Does not exist | Create/reset isolated SQLite schema | Reliable API database lifecycle |
| `VocabularyApp.WebApi.Tests/Infrastructure/TestDataBuilder.cs` | Does not exist | Seed users, words, definitions, user words, results and return IDs | Deterministic concise setup |
| `VocabularyApp.WebApi.Tests/Infrastructure/TestApiClient.cs` or auth extensions | Does not exist | Register/login users and attach real bearer tokens | Reusable HTTP journeys |
| `VocabularyApp.WebApi.Tests/Infrastructure/FakeDictionaryHandler.cs` | Does not exist | Return deterministic provider JSON/status/errors and record calls | No live dictionary dependency |
| `VocabularyApp.WebApi.Tests/Api/UsersApiTests.cs` | Does not exist | Registration/login/profile/password HTTP contracts | Auth API coverage |
| `VocabularyApp.WebApi.Tests/Api/AuthorizationApiTests.cs` | Does not exist | Anonymous/invalid/valid token behavior | Middleware coverage |
| `VocabularyApp.WebApi.Tests/Api/VocabularyApiTests.cs` | Does not exist | Ownership, add, list/search, favorites, preferred definitions | R5 safety net |
| `VocabularyApp.WebApi.Tests/Api/DictionaryLookupApiTests.cs` | Does not exist | Cache/provider/fallback behavior | Lookup safety net |
| `VocabularyApp.WebApi.Tests/Api/QuizApiTests.cs` | Does not exist | Authorization, ownership, secrecy, submit, persistence, history | R4 safety net |
| `.github/workflows/backend-tests.yml` | No workflow exists | Restore/build/test on push/PR | Clean-checkout automation |

Names may be consolidated to keep the suite small. Responsibilities matter more than exact
file count.

## 25. Files Likely to Modify

| File | Current Responsibility | Expected R6 Impact | Reason |
| --- | --- | --- | --- |
| `VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj` | xUnit and SQLite test dependencies | Add `Microsoft.AspNetCore.Mvc.Testing` compatible with .NET 8 | Host real API in process |
| `VocabularyApp.WebApi/Program.cs` | Top-level startup/DI/middleware | Add public partial `Program` marker only | `WebApplicationFactory<Program>` discoverability |
| `VocabularyApp.WebApi/Services/QuizService.cs` | Static in-memory quiz sessions | At most add a narrow internal/test reset seam if collection isolation cannot suffice | Prevent session leakage; no persistence redesign |
| `VocabularyApp.WebApi.Tests/Infrastructure/RelationalDatabaseFixture.cs` | R2 service/database fixture | Prefer retain unchanged; optionally share stable seed utilities, not API host lifecycle | Preserve working R2 tests |
| `VocabularyApp.sln` | Includes all projects | Likely no change unless test project organization changes | Test project already included |

Production controllers, DTOs, services, entities, migrations, and application contracts
should not be modified merely to make tests convenient. Any defect exposed by R6 should be
recorded against its owning remediation unless it prevents the foundation itself.

## 26. Risks

- A global API factory/database can create order-dependent tests if reset is incomplete.
- Static quiz sessions can leak across tests and hosts even when databases are isolated.
- Random quiz order and wall-clock expiration can make fragile assertions flaky.
- SQLite may hide or create differences in case-insensitive matching, unique constraints,
  transactions, and SQL translation compared with SQL Server.
- Test DI replacement may accidentally leave the original SQL Server or typed `WordService`
  registration active.
- Startup JWT validation will fail before tests run if test configuration is applied too late.
- Live dictionary access would make tests slow and nondeterministic; it must be impossible in
  the harness.
- Characterization tests can accidentally freeze known R4/R5 defects if expected behavior is
  not distinguished from current behavior.
- The WebApi `win-x64` runtime identifier may constrain Linux CI.
- Over-consolidated helpers can hide setup and make tests harder to understand; keep builders
  explicit and domain-focused.

## 27. Open Questions

Repository evidence cannot answer:

1. Should backend CI be a required merge check, or initially informational?
2. Will CI run on Windows to match `win-x64`, or should cross-platform runtime targeting be
   addressed separately?
3. Does the team want a small SQL Server/container lane for provider-sensitive R4/R5 tests,
   or should that decision wait for those implementation plans?
4. Should R6 characterize the current invalid quiz option behavior as a temporary known
   defect, or is rejection explicitly part of R4's target contract?
5. Is anonymous access to `POST /api/words/add` intentional? The comment calls it an admin
   endpoint, but no authorization policy exists.
6. Which external provider failures must be contractually frozen before R10: only
   success/not-found/transport failure, or also timeout-specific behavior?

These questions affect implementation details and policy, but none prevents writing a
detailed R6 implementation plan.

## 28. Implementation Readiness Assessment

**Ready with Conditions.** There is enough repository evidence to create and execute an R6
implementation plan. The existing R2 foundation is technically valuable and should remain.
The planned work should focus on the missing API host, deterministic isolation, ownership,
vocabulary, quiz characterization, controlled dictionary HTTP, and CI—not rebuild password
tests.

VocabularyApp does **not yet** have a sufficiently complete backend integration-test safety
net for the major remediation work that follows. It has a strong authentication core but no
automated protection for the public HTTP surface or the ownership and quiz/data behaviors
most exposed by R4 and R5. Completing the six recommended phases, with the minimum pre-R4
and pre-R5 sets identified above, would satisfy the original R6 objective proportionally and
safely.
