# R6 backend integration-test foundation completion

Date: 2026-09-05

Status: **COMPLETE**, with the provider and environment limitations below.

## Objective and repository state

Provide repeatable backend integration coverage for security, ownership, vocabulary,
quiz, and later service changes without external credentials or shared databases.
The current repository, rather than the older implementation plans, was authoritative.

Before this change:

- `VocabularyApp.WebApi.Tests` already contained xUnit security, relational service,
  and API integration tests (166 passing cases during this session).
- `VocabularyAppWebApplicationFactory : WebApplicationFactory<Program>` already
  exercised the real middleware, controllers, services, EF context, and JWT handler.
- `Program.cs` used top-level statements with an existing public partial `Program`;
  no entry-point change was needed. `ApplicationDbContext` used `UseSqlServer` in production.
- JWT configuration validated the signing key, issuer, audience, expiration, and
  lifetime. Registration/login were anonymous; profile, change-password,
  validate-token, vocabulary, dictionary lookup, and quiz routes were protected
  through explicit authorization or the authenticated fallback policy.
- `/api/users/register` and `/api/users/login` supplied real authentication flows.
  Vocabulary add/list/search/favorite/preferred-definition operations enforced
  ownership using the authenticated user's ID.
- `/api/quiz/start`, `/submit`, and `/history` used `QuizService`, including static
  sessions, ownership validation, and transactional persistence. R4 counter and
  R5 identity changes were already present, with regression tests. They were retained.
- An isolated SQLite fixture, API authentication/ownership helpers, deterministic
  dictionary handler, seed helpers, quiz collection, and backend CI workflow existed.

Older R6 review documents describe earlier behavior and deferred runtime validation.
This report records the current implementation and actual runtime results.

## Architecture and changes

Retained the existing test project, dependencies, solution membership, infrastructure,
and `.github/workflows/backend-tests.yml`. No new project or production code was needed.

Each API test creates its own factory and private, open SQLite in-memory connection.
`EnsureCreated` creates a relational schema from the production `ApplicationDbContext`
model, including its eight fixed parts-of-speech seed rows. This is SQLite's relational
provider, not EF Core InMemory. Tests add only their own data and capture generated IDs.

Both relational connection constructors now explicitly enable foreign keys.
The factory now disposes its connection through both `Dispose` and `DisposeAsync`,
using `finally` so host disposal errors do not bypass connection cleanup. Closing
the private in-memory connection destroys the database; no disk database or destructive
SQL against a configurable database is used.

`IntegrationTestSeeder` now fixes all directly seeded creation/addition timestamps
to `2026-01-02T03:04:05Z`. API-created timestamps, salted hashes, JWT timestamps,
and unique credential suffixes remain naturally variable; tests assert stable behavior
rather than those incidental values. Fixed credentials in the new parallel isolation
test prove that database independence does not rely on random usernames.

## Configuration and external-service safety

The factory replaces production context/options registration with its privately owned
SQLite connection, overrides the configured connection string, and selects `Testing`.
Provider smoke tests confirm SQLite is the resolved application provider.
No production database is accessed by this suite.

Synthetic JWT settings are provided before host startup through process environment
variables and reinforced through test configuration. The existing static initializer
sets the same values once for the process lifetime; tests must not mutate those keys
concurrently. No real signing secret is required.

The existing typed HTTP client still executes `WordService`, but its primary handler
is `ControllableDictionaryHandler`. It returns registered responses, records requests,
and throws on unknown paths without a network transport fallback. The configured API
key is synthetic. Existing cache, mapping, errors, timeout, malformed-response, and
API-key tests all ran through this stub; no live RapidAPI calls occur.

## Helpers and coverage

`ApiTestClientHelper` retains registration, real login/JWT acquisition, bearer client
creation, and disposable two-user ownership helpers. `IntegrationTestSeeder` supplies
canonical words, definitions, users, and owned vocabulary. Direct token creation is
limited to explicitly named authentication edge-case test logic.

Retained API coverage includes valid/invalid registration and login, accepted and
rejected JWTs, anonymous denial, profile isolation, password changes, vocabulary saves
and retrieval, cross-user mutation rejection, per-user searches, quiz authentication,
session ownership, history isolation, and rejected submissions leaving persisted state
unchanged. Existing R4/R5 concurrency and transaction regression coverage also passes.

Added `RelationalHostSafetyTests` with six executed cases:

1. Concurrent factories register identical credentials independently.
2. Synchronous factory disposal closes and destroys its database.
3. Asynchronous factory disposal closes and destroys its database.
4. The canonical-word unique index rejects duplicates (SQLite extended error 2067).
5. A foreign key rejects an orphan definition (SQLite extended error 787).
6. Transaction rollback leaves no word visible to a fresh context.

## Isolation and parallelization

Factory-owned databases and handlers allow separate test classes/factories to run
in parallel; there is no assembly-wide parallelization disable. Do not share a factory's
single connection among arbitrary simultaneous database operations. Existing specific
concurrency tests use controlled interceptors for their scenarios.

The retained quiz collection disables its parallel execution because production quiz
sessions are process-static and its setup/teardown clears the shared session dictionary.
Other tests retain xUnit's normal parallel behavior. Removing that restriction requires
host-owned session storage or cleanup restricted to each test's sessions; neither
production refactoring belongs to this R6 completion.

## Verification

Executed from the repository root with installed SDK 10.0.103, targeting .NET 8:

| Command | Result |
| --- | --- |
| `dotnet restore` | Exit 0; all three backend projects restored |
| `dotnet build` | Exit 0; 0 errors, 6 NU1900 warnings; final build 6.35 s |
| `dotnet test` before adding the new test file | 166 passed, 0 failed, 0 skipped; 18 s |
| `dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj` | 172 passed, 0 failed, 0 skipped; 24 s |
| `dotnet test` final solution run | 172 passed, 0 failed, 0 skipped; 17 s |

The expanded suite passed twice in separate test processes. Cleanup, constraints,
rollback, host isolation, authentication, ownership, and dictionary isolation all passed.
No unrelated failing tests were found. The final build and solution run include the
additional synchronous-disposal `finally` hardening.

NU1900 warnings mean NuGet could not reach `https://api.nuget.org/v3/index.json` for
vulnerability metadata in this environment. Compilation and tests succeeded; this is
not a warning-free build and vulnerability auditing was not validated. A clean checkout
requires a compatible .NET SDK/runtime and NuGet package access/cache, but no database
server, LocalDB, production secrets, or live dictionary provider. Existing Windows CI
uses .NET 8; this session did not execute the remote workflow.

SQLite validates the common EF relational model, uniqueness, foreign keys, and
transactions. It does not execute SQL Server migrations or prove SQL Server collation,
length enforcement, SQL translation, locking, or provider-specific concurrency behavior.
A separate SQL Server migration/provider test lane remains useful follow-up work.

Git status inspection was blocked by Git's repository ownership check, including
command-scoped safe-directory attempts. No global Git settings were changed. No commit
or push was performed.

## Files changed in this completion

- Modified `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs`
- Modified `VocabularyApp.WebApi.Tests/Infrastructure/RelationalDatabaseFixture.cs`
- Modified `VocabularyApp.WebApi.Tests/Infrastructure/IntegrationTestSeeder.cs`
- Created `VocabularyApp.WebApi.Tests/Integration/RelationalHostSafetyTests.cs`
- Created `docs/Updates/R6-backend-integration-test-foundation-completion.md`

The existing lowercase `docs` directory is retained (the requested `Docs` path resolves
to it on Windows). No frontend or production source files were changed.

R6 satisfies its definition of done as a relational integration-test foundation with
the disclosed provider limits. It supplies a verified safety net for further R4 work
and later security, identity, contract, exception handling, dictionary-provider, and
vocabulary-service changes without implementing those remediations here.
