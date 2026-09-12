# CI/CD Analysis and Implementation Plan

> **Phase 1 implementation update (2026-09-12):** See [Phase 1 implementation results](CI-CD-phase-1-implementation-results.md) for the current configuration. The user's implementation request supersedes this historical plan's manual-only CI test policy and PR trigger proposal: GitHub Actions must run backend/integration and frontend tests automatically on pushes to `master`, and both must pass before builds. Codex must not execute tests. Phase 1 now also validates Angular-to-wwwroot staging; publishing, artifact retention, and deployment remain unimplemented. The analysis below is preserved as a historical record, not the current Phase 1 specification.

Analysis date: 2026-09-10. Repository: `Covenant-Company/VocabularyApp`. Production branch: `master`.

**Analysis and planning only. NO TESTS WERE RUN.** No dependency installation, build, publish, application startup, migration, production connection, workflow edit, commit, or push was performed. Commands below are proposals, not execution records. All initial workflows must leave test execution to the user.

## 1. Executive Summary

The repository is ready for a build-only CI implementation, subject to the first real build validating the proposed toolchain. Production automation is not ready until hosting credentials, target mapping, certificate trust, runtime architecture, preservation rules, and human approval controls are confirmed.

The most urgent finding is `.github/workflows/backend-tests.yml`: it currently runs `dotnet test` on every push and pull request. Phase 1 must replace that behavior, not merely add another workflow alongside it. It remains unchanged during this analysis. Pushing before that change can still trigger the existing automatic tests.

Build the .NET 8 solution and Angular production application without starting either application. Package Angular browser assets into backend `wwwroot` before publishing the WebApi project. Retain the existing `win-x64` targeting initially, explicitly framework-dependent. Use an immutable, secret-free publish artifact for a later manually dispatched, approved Web Deploy job. Never run production migrations as part of deployment.

## 2. Current Repository Architecture

`VocabularyApp.sln` contains WebApi, Data, and WebApi.Tests, with Debug/Release Any CPU solution configurations. WebApi references Data; Tests references both. Angular is a separate npm project and is not built by the solution.

WebApi hosts controllers, JWT authentication, dictionary HTTP integration, quiz services, and static SPA files. Data contains EF Core models, `ApplicationDbContext`, and SQL Server migrations. The test project combines security/service tests and HTTP integration tests. The frontend uses Angular routing and same-origin `/api` in production.

No applicable `AGENTS.md` was found in the repository or immediate parent. Direct `.git/HEAD` inspection names `devops/ci-cd-pipeline`. Git status and tracked-file enumeration were blocked by Git's ownership check, including a command-scoped safe-directory attempt. No persistent Git settings were changed. Paths below are verified filesystem paths; tracked status, branch ancestry, and remote state were not verified.

## 3. Current Deployment Architecture

The user reports a live SmarterASP.NET site deployed manually by ZIP. Repository deployment documentation describes publishing the backend with the embedded Angular output and copying/uploading the publish contents to IIS. The R5 deployment record identifies hosting application folder `/vocabularyapp` and static folder `/vocabularyapp/wwwroot`. These are hosting folder references, not proof of a public URL prefix or the Web Deploy application name.

`Program.cs` configures SQL Server but does not call `Database.Migrate`, `EnsureCreated`, or a production seeder. It validates JWT settings during startup. `UseDefaultFiles`, `UseStaticFiles`, and anonymous `MapFallbackToFile("{*path:nonfile}", "index.html")` serve the SPA. Swagger is enabled without an environment condition; HTTPS redirection is commented out. Hosting HTTPS behavior therefore needs confirmation.

The R5 record documents a missing `ConnectionStrings__DefaultConnection` application-pool variable causing a LocalDB connection attempt after deployment. Correcting the external variable and restarting resolved that incident. Preserve external configuration across deployments. These are historical records, not a fresh production inspection.

## 4. Repository Paths Relevant to CI/CD

| Purpose | Exact path |
| --- | --- |
| Solution | `VocabularyApp.sln` |
| API project / startup | `VocabularyApp.WebApi/VocabularyApp.WebApi.csproj`; `VocabularyApp.WebApi/Program.cs` |
| Data project / context | `VocabularyApp.Data/VocabularyApp.Data.csproj`; `VocabularyApp.Data/ApplicationDbContext.cs` |
| Migrations / snapshot | `VocabularyApp.Data/Migrations/`; `VocabularyApp.Data/Migrations/ApplicationDbContextModelSnapshot.cs` |
| Test project | `VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj` |
| Test source groups | `VocabularyApp.WebApi.Tests/Security/`; `VocabularyApp.WebApi.Tests/Services/`; `VocabularyApp.WebApi.Tests/Integration/`; `VocabularyApp.WebApi.Tests/Infrastructure/` |
| Angular project and lock | `VocabularyApp.UI/package.json`; `VocabularyApp.UI/package-lock.json` |
| Angular configuration | `VocabularyApp.UI/angular.json`; `VocabularyApp.UI/tsconfig.app.json`; `VocabularyApp.UI/tsconfig.spec.json` |
| Frontend production config | `VocabularyApp.UI/src/environments/environment.prod.ts`; `VocabularyApp.UI/src/index.html`; `VocabularyApp.UI/src/app/app.routes.ts` |
| Angular IIS file | `VocabularyApp.UI/public/web.config` |
| Generated browser output | `VocabularyApp.UI/dist/vocabulary-app.ui/browser/` |
| Backend static destination | `VocabularyApp.WebApi/wwwroot/` (absent in inspected checkout; create during packaging) |
| Runtime config | `VocabularyApp.WebApi/appsettings.json`; `VocabularyApp.WebApi/appsettings.Development.json`; `VocabularyApp.WebApi/Configuration/JwtSettings.cs` |
| Local launch config | `VocabularyApp.WebApi/Properties/launchSettings.json` |
| Existing workflow | `.github/workflows/backend-tests.yml` |
| Deployment guide | `docs/Deployment/SmarterASP-Manual-Deployment.md` |
| Production migration/deployment record | `docs/Updates/R5-production-deployment.md` |
| Test completion evidence | `docs/Updates/R6-backend-integration-test-foundation-completion.md`; `docs/Updates/R6-Backend-Integration-Testing-Final-Review.md` |
| Ignore rules | `.gitignore` |
| This deliverable | `docs/Updates/CI-CD-analysis-and-implementation-plan.md` |

The requested `Docs/Updates/...` resolves to the existing lowercase `docs/Updates/...` on Windows. Retain that existing casing for Linux/GitHub links. Additional lockfiles exist at `package-lock.json` and `VocabularyApp.Data/package-lock.json`; neither is the Angular installation target. No publish profile, `global.json`, Node version file, repository NuGet configuration, or `Directory.Build.*` customization was found. No other workflow was found.

## 5. .NET Build Analysis

All three projects target `net8.0`. Use `actions/setup-dotnet` with `8.0.x`, later pinning a validated SDK patch for reproducibility. Historical R6 verification used SDK 10.0.103 targeting .NET 8; this is not a requirement to adopt SDK 10. EF Core packages are 8.0.10; SqlClient is 5.2.2.

**Proposed build commands — not executed**, from repository root on a Windows runner:

```powershell
dotnet restore VocabularyApp.sln
dotnet build VocabularyApp.sln --configuration Release --no-restore
```

Building the solution compiles the test project but does not execute tests. The inspected project files have no test-running build hooks. The existing workflow has a separate explicit test step, which must be removed during implementation.

For the artifact job, after the Angular copy in section 7:

```powershell
dotnet restore VocabularyApp.WebApi/VocabularyApp.WebApi.csproj --runtime win-x64 -p:SelfContained=false
dotnet publish VocabularyApp.WebApi/VocabularyApp.WebApi.csproj --configuration Release --runtime win-x64 --self-contained false --no-restore --output artifacts/publish
```

Publish the WebApi project, not the solution. Publish intentionally builds again after static files exist; do not use `--no-build` against an earlier artifact set. A project-specific restore ensures matching RID/self-contained assets.

WebApi and Data both set `RuntimeIdentifier=win-x64`, `CopyLocalLockFileAssemblies=true`, and `IncludeNativeLibrariesForSelfExtract=true`. Neither explicitly sets self-contained, trimming, single-file, or AOT publishing. Keep these existing settings initially. The native extraction property alone does not establish single-file deployment. In .NET 8 a RID does not imply self-contained; explicitly specify the intended mode. [Microsoft .NET 8 RID behavior](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/runtimespecific-app-default)

Recommend framework-dependent Windows x64 deployment if the existing pool supports x64 and the .NET 8 ASP.NET Core runtime/hosting module. This preserves repository behavior and avoids bundling a runtime unnecessarily. Do not switch to RID-neutral output just because a generic example omits `--runtime`: the project still sets it. Removing both project RIDs or selecting win-x86 would be a separate compatibility decision if hosting evidence requires it. Self-contained publishing is a fallback only after checking provider constraints; IIS still needs the ASP.NET Core hosting module. [Microsoft publish reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish)

## 6. Angular Build Analysis

The manifest requests Angular `^18.1.0`, CLI `^18.1.1`, and TypeScript `~5.5.2`. The lock resolves Angular core 18.2.14, CLI 18.2.21, and TypeScript 5.5.4. No `engines` or `packageManager` requirement is specified by the project. Existing documentation records successful Node 20.19.0/npm 10.8.2 builds.

Use Node 22.x with npm 10.x for initial CI, recording exact installed versions and pinning patches after validation. Angular 18.1/18.2 lists Node 22 compatibility and TypeScript >=5.4 <5.6; Node 20 is now EOL while Node 22 remains LTS. Angular 18 itself is unsupported and needs a separately scoped upgrade. This analysis did not verify a Node 22 build. [Angular compatibility](https://angular.dev/reference/versions), [Node release status](https://nodejs.org/en/about/previous-releases)

**Proposed build commands — not executed**, working directory `VocabularyApp.UI`:

```powershell
npm ci
npm run build -- --configuration production
```

The UI lockfile parses as JSON, uses lockfile version 3, and its root dependency/devDependency declarations match `package.json`. It is structurally suitable for `npm ci`; transitive resolution, downloads, lifecycle scripts, and installation success remain unverified until Phase 1. The project scripts contain no install/prebuild/postbuild test invocation. Do not run npm from repository root or Data. Do not use `--omit=dev`, since the compiler is a dev dependency.

The application builder uses `outputPath=dist/vocabulary-app.ui`; browser files are under its `browser` subdirectory. Production replaces the environment file, hashes output, and enforces bundle/style budgets (initial warning 500 kB/error 1 MB; component style warning 2 kB/error 4 kB). Default build configuration is already production, but specify it explicitly.

`<base href="/">` and production `apiUrl: '/api'` assume deployment at the public origin root. If `/vocabularyapp` is a real URL subapplication, both frontend base/API paths and backend/IIS path handling need a separate change before deployment. Do not infer this from the physical folder name.

## 7. Angular-to-wwwroot Packaging Analysis

There is no npm script or MSBuild target automatically building/copying Angular into WebApi. The manual guide and R5 record require a copy before publishing. CI must implement it explicitly.

In a fresh runner checkout, create `VocabularyApp.WebApi/wwwroot`, then recursively copy the **contents** of `VocabularyApp.UI/dist/vocabulary-app.ui/browser` there, including nested assets. Do not copy the parent `dist` or `browser` folder itself. Assert `wwwroot/index.html` exists before publish. Any cleanup script must resolve and constrain paths to this checkout; never reuse it against a hosting folder.

Exclude the Angular-only `web.config` from this copy. It contains static IIS rewrite rules and a WebP MIME mapping, not ASP.NET Core process hosting. The backend already supplies SPA fallback. Nested IIS configuration can affect hosting, so it should not be treated as harmless merely because it is under `wwwroot`. Preserve the root `web.config` generated by Web SDK publishing, with `AspNetCoreModuleV2` and the appropriate application process. Verify WebP delivery manually later if relevant.

Post-publish, remove `appsettings.Development.json` from the staging output and reject unexpected secret-bearing configs/profiles. Keep only reviewed non-secret runtime defaults. Validate file presence and package structure statically; do not start the app or run a smoke-test command. Expected output:

```text
artifacts/publish/
  VocabularyApp.WebApi.dll
  VocabularyApp.WebApi.exe
  VocabularyApp.WebApi.deps.json
  VocabularyApp.WebApi.runtimeconfig.json
  VocabularyApp.Data.dll
  web.config
  appsettings.json
  [dependency assemblies/native assets]
  wwwroot/
    index.html
    [hashed JavaScript, CSS, favicon and assets]
```

## 8. Test Infrastructure Analysis — Inspection Only

The single backend test project uses xUnit 2.5.3, Microsoft.NET.Test.Sdk 17.8.0, MVC Testing 8.0.10, EF SQLite 8.0.10, and coverlet.collector 6.0.0. Security and Services contain authentication, password, logging, and concurrency cases. Integration contains HTTP authentication, ownership, dictionary, quiz, infrastructure, and relational safety cases. There is no separate integration-test project.

`Infrastructure/VocabularyAppWebApplicationFactory.cs` derives from `WebApplicationFactory<Program>` and runs the real application under `Testing`. It supplies deterministic JWT process environment values before the top-level application binds configuration. These remain for the test-process lifetime. It overrides the connection string and WordsAPI configuration in memory, removes the production context/options registrations, and registers SQLite using a factory-owned open `Data Source=:memory:` connection with foreign keys enabled. `CreateHost` uses `EnsureCreated` on that replacement context. Production migrations are not used.

`ControllableDictionaryHandler` replaces the real HTTP transport while retaining `WordService`, so dictionary scenarios do not require a live API. Failure/synchronization interceptors drive controlled persistence and race scenarios. Logging providers are cleared in the API factory.

`RelationalDatabaseFixture.cs` similarly owns an open in-memory SQLite connection, creates the EF model, supplies contexts, and closes the connection at disposal. Factory synchronous and asynchronous disposal close their connection in `finally`; closing destroys the private database. Separate factories isolate identical user data. Do not share one connection among arbitrary concurrent operations.

`IntegrationTestSeeder.cs` uses a fixed UTC timestamp, explicit scenario inputs, and real password hashing. `ApplicationDbContext` seeds eight fixed PartsOfSpeech IDs/timestamps. Password hashes may be salted; deterministic scenario state does not mean identical hash bytes.

`QuizApiCollection.cs` disables parallelization for the quiz collection. `QuizApiTests.cs` uses that collection, and `QuizApiTestBase` clears process-static quiz sessions before/after each test. There is no global xUnit parallelization disable.

Current DI substitution, private SQLite ownership, and absence of startup database access prevent the inspected integration path from opening production SQL Server. This is source-level reasoning, not a new runtime guarantee: future startup migrations or direct SQL connections before DI substitution could break it. Manual testing should use a clean process without production secrets. SQLite does not validate SQL Server migrations/T-SQL, collation, length enforcement, SQL translation, locking, or all provider-specific concurrency behavior.

Historical evidence: `docs/Updates/R6-backend-integration-test-foundation-completion.md`, Verification section, records 172 passed/0 failed/0 skipped in project and final solution runs, plus NuGet NU1900 metadata-access warnings. It also describes isolation limits. The manual deployment guide and R5 record describe successful Angular production builds. These results were not rerun or recertified here.

## 9. Manual Test Commands — DO NOT EXECUTE

All commands in this section are for the user alone, on the exact candidate commit in a local checkout. Backend requires a compatible Windows .NET SDK/runtime and restored packages. Angular requires UI dependencies and Chrome/Chromium available to Karma. No CI job may invoke these commands.

**MANUAL USER COMMAND — DO NOT EXECUTE:** all backend tests, including integration, from repository root:

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release
```

**MANUAL USER COMMAND — DO NOT EXECUTE:** HTTP integration namespace subset, from repository root:

```powershell
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release --filter "FullyQualifiedName~VocabularyApp.WebApi.Tests.Integration"
```

This subset excludes Infrastructure and Services tests; it is not a substitute for the complete backend command.

**MANUAL USER COMMAND — DO NOT EXECUTE:** frontend tests, working directory `VocabularyApp.UI`:

```powershell
npm test -- --watch=false --browsers=ChromeHeadless
```

The user may instead use interactive `npm test` locally. Record commit SHA, commands, results, date, and tool versions without credentials. These commands have not been validated by execution in this task.

## 10. Configuration and Secrets Inventory

| Value/key | Recommended location | Proposed GitHub mapping / handling |
| --- | --- | --- |
| VSDeploy service URL | GitHub `production` Environment secret | `SMARTERASP_WEBDEPLOY_URL`; exact control-panel URL |
| Site/application name | `production` Environment secret | `SMARTERASP_WEBDEPLOY_SITE`; exact delegated destination |
| Web Deploy username | `production` Environment secret | `SMARTERASP_WEBDEPLOY_USERNAME` |
| Web Deploy password | `production` Environment secret | `SMARTERASP_WEBDEPLOY_PASSWORD` |
| Public destination URL | Environment variable (non-secret) | `PRODUCTION_URL`; approval/deployment link, not Web Deploy endpoint |
| `ConnectionStrings:DefaultConnection` | SmarterASP application-pool environment | `ConnectionStrings__DefaultConnection`; no initial GitHub copy |
| `JwtSettings:SecretKey` | SmarterASP application-pool environment | `JwtSettings__SecretKey`; no initial GitHub copy |
| `WordsApi:ApiKey` | SmarterASP application-pool environment | `WordsApi__ApiKey`; no initial GitHub copy |
| `WordsApi:Host`, `WordsApi:BaseUrl` | Non-secret appsettings defaults; host overrides if needed | `WordsApi__Host`, `WordsApi__BaseUrl` |
| `JwtSettings:Issuer`, `Audience`, `ExpirationMinutes` | Non-secret appsettings defaults; production overrides as needed | `JwtSettings__Issuer`, `JwtSettings__Audience`, `JwtSettings__ExpirationMinutes` |
| ASP.NET Core environment | SmarterASP application-pool environment | `ASPNETCORE_ENVIRONMENT=Production`; avoid conflicting `DOTNET_ENVIRONMENT` |
| `Cors:AllowedOrigins` | Non-secret defaults / host origin list | `Cors__AllowedOrigins__0`, subsequent indexes as needed |
| `AllowedHosts`, `Logging:LogLevel` | Non-secret defaults / host overrides | Restrict host names and diagnostic verbosity appropriately |

Repository-level Actions secrets (category A) are unnecessary for build/artifact jobs. Prefer production Environment secrets (category B) for deployment credentials. If the repository plan lacks Environment secrets, repository secrets are a fallback only with restricted maintainers, trusted master-only dispatch, and documented weaker enforcement. Runtime secrets belong on the host (category C), not the artifact; public defaults belong in appsettings (category D).

`JwtSettings.BindAndValidate` requires a nonblank secret of at least 32 UTF-8 bytes, issuer, audience, and positive expiration minutes. Preserve existing valid values to avoid unintended token invalidation. Inspected appsettings files have LocalDB defaults, blank WordsAPI keys, and no JWT secret value. No actual secret values are reproduced. Never put provider keys in Angular environment files: browser bundles are public.

## 11. Proposed GitHub Actions Architecture

Use a Windows runner (proposed `windows-2022`, validating available tooling during implementation) to align with current RIDs and eventual `msdeploy.exe`. Pin reviewed action revisions to full commit SHAs at implementation time; do not invent pins now. Use official checkout/setup-dotnet/setup-node/upload-artifact/download-artifact actions.

Separate unprivileged compilation/artifact production from the credential-bearing deployment job. Default `permissions: contents: read`; grant artifact lookup `actions: read` only where needed. Configure timeouts, fail on nonzero native command exit codes, and never use `continue-on-error` to bless failed builds. Cache npm downloads using the UI lockfile; caches are not deployment artifacts. PR jobs receive no deployment secrets. Avoid `pull_request_target` for building PR code.

## 12. Phase 1 — CI / Build Validation Plan

Modify the existing workflow in place, retaining its filename initially to avoid two active workflows. Rename its displayed workflow/job to build validation; remove the automatic test step. Restrict triggers to pull requests targeting `master` and pushes to `master`.

Steps: checkout; set up .NET 8; restore solution; Release-build solution; set up Node 22 and npm 10; run UI `npm ci`; run explicit production build. Compilation includes tests as source but executes none. No packaging/deployment credentials, application startup, database access, migrations, HTTP checks, or automated tests. Require a stable `Build validation` status for merging. First successful CI execution will validate command compatibility, including Node 22 and package restore, which this analysis intentionally does not establish.

## 13. Phase 2 — Deployment Artifact Plan

Extend the validated workflow with an artifact job dependent on build success, limited to trusted pushes to `master`. Repeat the same build inputs in a clean checkout for the same SHA; copy UI output as section 7 describes, restore/publish WebApi, sanitize staging files, and perform file-only validation.

Upload the complete `artifacts/publish/` directory as `vocabularyapp-publish-<full-SHA>-<run-id>-<attempt>`. Also retain a plain ZIP of its contents for manual recovery and a release manifest containing commit, run/attempt, SDK/Node/npm versions, RID/mode, timestamp, and SHA-256 digest. Keep manifests outside the deployed folder. Suggested retention is 90 days within repository/account limits, plus a user-controlled secure copy of the current and previous production releases.

The deployment input is the extracted publish directory. A regular ZIP is a transport/archive format, **not** an MSDeploy package with a manifest. SmarterASP documents direct `contentPath` synchronization of a dotnet publish folder, so an MSDeploy package ZIP is unnecessary for the selected method. If the account requires a package provider instead, design that package separately after obtaining its requirements. [SmarterASP command-line publishing](https://www.smarterasp.net/support/kb/a2202/how-to-publish-_net-porject-using-msdeploy_exe-through-command-line.aspx)

No secret substitution, tests, deployment, or schema operations occur in Phase 2. An artifact with a missing index/root web.config or prohibited config file fails packaging and is not deployable.

## 14. Phase 3 — Controlled Production Deployment Plan

Create a dispatch-only deployment workflow on `master`. Inputs identify the successful build run, artifact, full tested SHA, and an explicit manual-tests-passed confirmation with evidence reference. A blank/false confirmation fails before deployment. A preflight job without hosting credentials checks the source workflow identity, repository, successful conclusion, trusted master push event, artifact run/attempt/digest, and exact SHA match. Reject PR artifacts, arbitrary URLs, expired artifacts, and mismatched commits.

The deployment job depends on preflight and targets the `production` Environment. Configure deployment branch restrictions to `master`, required reviewers where supported, and appropriate bypass restrictions. Serialize all production runs with one concurrency group and `cancel-in-progress: false`; recheck release order before deployment so a stale queued run cannot silently replace a newer release.

Download and deploy the previously built artifact without rebuilding it. Resolve/install a reviewed Microsoft Web Deploy version on the Windows runner; do not assume the hosted image includes it. Use the provider's documented `contentPath` source/destination model, service URL, Basic authentication over TLS, and `includeAcls=False`; disable app-pool/content/certificate extension links so deployment does not attempt server administration. Exact argument escaping, preservation filters, certificate setup, and offline sequencing require implementation review.

Pass credentials through environment variables to a script, never interpolating them into printed workflow source or logging the argument array. Avoid verbose/debug deployment logs, transcripts, shell tracing, and unrestricted diagnostic uploads. Check exit status and redact any captured failure output before persistence. Secret masking is a second line of defense, not permission to print values.

Use a controlled maintenance window and verified `app_offline.htm` behavior for file locks; do not assume the AppOffline rule works identically with every content provider. Preserve logs, host-owned files, and external configuration. Avoid blanket destination deletion. No database providers or connection strings participate. A failed/partial sync must remain a failed deployment requiring operator recovery; do not automatically restart or retry indefinitely.

## 15. Phase 4 — Optional Continuous Deployment

Only after Phases 1–3 are reliable and the user explicitly enables it, allow successful master build/artifact completion to request deployment automatically. Promote the same artifact with the same provenance checks. Tests remain manual. Preserve a human test attestation/approval before release; fully unattended deployment would require that attestation to already cover the exact SHA. Do not treat merge approval or green compilation alone as proof of test execution. This phase is not implemented or enabled now.

## 16. Manual Test Approval Gate

```text
Push candidate -> GitHub build succeeds -> immutable artifact
    -> USER runs all tests on that exact SHA
    -> USER records results and dispatches deployment
    -> provenance checks -> production approval -> Web Deploy
```

The strongest available native representation is dispatch plus a protected Environment approval, with evidence naming the exact tested SHA/artifact. Reviewers inspect the build result, manual results, schema compatibility, and rollback artifact. A changed SHA requires fresh manual testing and approval; never retarget a mutable branch after approval.

GitHub plan and visibility matter: required reviewers on Free/Pro/Team are available only for public repositories; private/internal Environment secrets require an eligible paid plan. Confirm this repository's plan and visibility. For two operators, prevent self-review. A sole operator cannot both initiate and approve with that restriction, so use an allowed self-approval policy or a second reviewer. If required reviewers are unavailable, trusted master-only manual dispatch with explicit attestation preserves user timing control but is weaker than an independently enforced approval gate. Do not claim equivalent protection. [GitHub deployment environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)

No scheduled jobs or automatic smoke tests substitute for the user's checks. Post-deployment functional verification also remains manual.

## 17. EF Core Production Migration Strategy

Migrations belong to `VocabularyApp.Data`; the EF startup project is `VocabularyApp.WebApi`. There is no discovered design-time context factory. EF tools may instantiate startup configuration, so even script generation needs valid nonproduction JWT/configuration and careful control of provider setup. Pin EF tooling to the project's EF version when designing a future migration procedure.

The migration chain begins with `20251004202304_InitialCreate` and currently ends with `20260829155134_CorrectUserWordIdentity`. SQL companion files and R4 audit/reconciliation scripts also exist. R5 performs a SQL Server `IF EXISTS`/`THROW 51000` precondition before replacing composite UserWord uniqueness with `(UserId, WordId)` uniqueness. Its Down operation restores the older index; it is not a data backup.

The R5 production guide documents backup, duplicate/consistency audits, write quiescence, explicitly targeted EF execution, history/index verification, and manual application checks. It records a successful historical migration and also a historical uncertainty about master containing the deployed R5 commit. Neither current schema nor branch ancestry was queried here.

Initial CI/CD must contain no `dotnet ef database update`, migration bundle execution, SQL execution, database deploy provider, or startup migration addition. Build/publish does not need a production connection. Application rollback must remain schema-compatible.

Future, separately authorized migration work should produce reviewed, version-bounded SQL (potentially idempotent), rehearse it manually on an isolated SQL Server copy, confirm backups/restoration and index locks/transaction implications, then use a separate dispatch workflow with production approval and a distinct migration credential. SQL Server collation, filtered indexes, duplicate data, T-SQL, and rollback/history state require provider-specific review. Prefer expand/contract schema changes. Never automatically apply Down or restore a database after application failure: backups can lose subsequent writes and require an explicit operator decision.

## 18. SmarterASP.NET Web Deploy Requirements

Obtain from the website's VSDeploy/Web Deploy panel or its downloaded publish settings: exact service URL (including supplied port/path/query), Site/Application name, username, password, and public destination URL. Map these to section 10. Do not infer them from the repository name, hosting folder, FTP login, or public domain. Keep downloaded profiles outside Git. Provider documentation describes both control-panel settings and profile import. [SmarterASP VSDeploy settings](https://www.smarterasp.net/support/kb/a367/use-web-deploy-function-to-publish-site-with-visual-studio-2012.aspx), [publish profile instructions](https://www.smarterasp.net/support/kb/a2286/how-to-publish-asp_net-core-web-app-visual-studio-2022.aspx)

Also confirm plan entitlement/enabled status, delegated contentPath permissions, runner network access, .NET 8 hosting module/runtime, x64 pool, public URL root, file ownership/preservation boundaries, application-pool environment persistence, and offline/restart support. The panel's GitHub option is not evidence that a controlled Actions deployment is configured; avoid enabling a second independent auto-deployer.

Provider examples use `allowUntrusted` and verbose output. Do not copy those defaults into production CI. Request a trusted certificate or establish a verified certificate trust arrangement; if only untrusted TLS is available, document the concrete tradeoff and resolve it before Phase 3. No endpoint validation or hosting change was performed.

## 19. Failure and Rollback Strategy

| Failure | Required result |
| --- | --- |
| Angular install/build/budget failure | Fail CI; no deployable artifact; production unchanged |
| .NET restore/build failure | Fail CI; no deployment; do not reinterpret warnings as test evidence |
| Publish or package validation failure | Fail artifact job; no promotion |
| Provenance/manual approval failure | Stop before hosting credentials/deployment |
| Web Deploy failure | Mark failed; assume partial content is possible; operator inspects sanitized evidence and maintenance state |

Retain current and previous production publish ZIPs/directories, manifests and digests. Restore the exact previous package through a separately approved redeployment or the existing manual upload process; never rebuild an old branch and assume equivalent bytes. Record each deployed SHA/artifact and maintain an independently accessible recovery copy before retention expires.

Shared IIS file synchronization is not atomic and may cause downtime. During recovery, preserve logs/external variables and intentionally remove only obsolete application-managed files using reviewed rules. Blanket mirroring can delete host content; never deleting stale files can leave old executable assets. Resolve this boundary before implementation. Keep database rollback separate and require schema compatibility before reopening traffic. No rollback automation is implemented.

## 20. Security Considerations

Use site-scoped deployment credentials if the provider offers them; ask about narrowing credentials if the profile uses an account-wide login. CI builders never receive runtime secrets. Upload only sanitized publish output, not the checkout, profiles, `.env`, user secrets, `.git`, binlogs, or diagnostic dumps. Keep `appsettings.Development.json` out of artifacts and explicitly configure Production on the host; launchSettings does not configure IIS production.

Existing ignore rules cover bin/obj/out/artifacts, publish-prefixed directories, archives, dist/node_modules, logs, `.env` variants, `secrets.json`, private certificates, publish profiles/settings, and the named hosting-password file. Ignore rules do not protect already tracked files or arbitrary secret filenames. `appsettings.json` and `appsettings.Development.json` are not generally ignored; keep them non-secret. Generated `wwwroot` is not explicitly ignored, so Phase 2 should add a narrowly scoped rule if it remains wholly generated. Review a general archive/profile-user rule if future tooling produces uncovered files; do not store credentials in generic ZIPs.

Protect master with reviewed PRs and required build checks. Require review for workflows and deployment scripts, restrict direct pushes/bypasses and repository write access, and verify deployment workflow changes cannot bypass the human gate. Artifact provenance checks prevent a successful PR build from being promoted as trusted master output. No credential history search was performed. Public Swagger and host HTTPS settings deserve a separate production configuration review; they do not justify changing application code during this planning task.

## 21. Risks and Mitigations

| Risk | Mitigation / stage |
| --- | --- |
| Existing workflow violates manual-test policy | Replace its test-running behavior first; Phase 1 blocker until changed |
| Toolchain/lock works only historically | First build-only CI validates Node 22/npm 10/.NET 8; user performs all tests |
| Angular 18 unsupported | Separate dependency upgrade effort; no silent upgrade in CI work |
| Missing Angular files or nested browser folder | Explicit recursive content copy and file assertions |
| Missing host connection/JWT variables | Operator confirms presence securely; preserve pool configuration |
| Wrong physical/public/Web Deploy path | Obtain authoritative VSDeploy mapping before deployment |
| Invalid TLS or broad credentials | Resolve trust and least privilege before Phase 3 |
| Approval unavailable for plan/solo maintainer | Document enforceable dispatch/reviewer choice before Phase 3 |
| Artifact swap or stale release | SHA/run/digest checks and serialized deployment |
| Partial deployment or deleted host files | Maintenance procedure, preservation filters, previous artifact |
| SQLite mistaken for SQL Server verification | User-run provider-specific migration rehearsal in separate scope |
| Git ownership prevents branch verification | Resolve locally before implementation; do not infer merge readiness |

## 22. Acceptance Criteria

Analysis completed: repository paths/project relationships, build/publish command proposals, lockfile structure, Angular packaging, test infrastructure, manual commands, configuration inventory, hosting requirements, migration separation, human gate, phased rollout, rollback, and security have been documented. Only this document was created. No tests, builds, migrations, production connections, workflow changes, commits, or pushes were performed. Git status verification remains unavailable due to ownership checks; no claim is made that the preexisting workspace was clean.

Future Phase 1 acceptance: only master push/PR triggers; backend/frontend compile successfully; no test execution or production credentials anywhere in the workflow set. Phase 2: exact SHA-linked secret-free artifact contains backend and SPA in the correct locations. Phase 3: confirmed target/trust/preservation configuration, user test evidence for that SHA, enforced agreed approval policy, successful controlled deployment, user-performed functional verification, and retained recovery artifact. No phase includes automatic migrations.

## 23. Recommended Implementation Sequence

1. Review this plan, resolve Git ownership/branch provenance, and ensure the first implementation change replaces existing automatic test behavior before pushing.
2. Implement Phase 1 only; validate builds and let the user run all tests manually.
3. Implement Phase 2 packaging and artifact retention; review package contents without contacting production.
4. Obtain VSDeploy details and confirm runtime, paths, certificate trust, host-file preservation, and GitHub approval capabilities.
5. Configure the production Environment/credentials and implement dispatch-only Phase 3 with provenance and manual-test gates.
6. Conduct the first deployment only after explicit user initiation/approval, a previous-version backup, and a scheduled maintenance window; user performs verification.
7. Consider optional continuous deployment and separate migration tooling only through later explicit authorization.

## 24. Unresolved Questions

- What are the exact VSDeploy endpoint, delegated application name, username, and credential scope? Supply secrets directly to GitHub, not this document.
- Does the current low-cost plan support the needed delegation, x64 .NET 8 hosting, TLS trust, and GitHub-hosted runner connectivity?
- Is `/vocabularyapp` only a physical folder, or a public URL prefix? What is the canonical HTTPS destination?
- Which host files/directories must survive synchronization, and how should offline/restart behavior work on this account?
- What GitHub visibility/plan and reviewer arrangement are available? Can production approval be enforced for this repository?
- Does current master contain the exact production changes and expected schema compatibility? Git ownership blocked confirmation; historical records alone are insufficient.
- What artifact retention/recovery storage and maintenance-window duration are acceptable?
- Does the first Node 22/npm 10 build succeed with the current lockfile? Static consistency is established; actual installation/build remains pending.

These questions block production enablement, not creation of the build-only workflow after authorization to implement. No additional user input was required to complete this analysis.

## 25. Exact Files That Would Be Created or Modified During Implementation

| Phase | File | Proposed change |
| --- | --- | --- |
| 1 | `.github/workflows/backend-tests.yml` | Modify in place to build-only backend/frontend validation; remove tests; master-only triggers |
| 1 | `docs/Deployment/SmarterASP-Manual-Deployment.md` | Update CI/toolchain guidance and distinguish historical/manual test responsibility |
| 2 | `.github/workflows/backend-tests.yml` | Add trusted-master artifact job after build validation |
| 2 | `scripts/ci/Publish-VocabularyApp.ps1` | Create reviewed build/copy/publish/sanitization/archive script; no tests or database operations |
| 2 | `.gitignore` | Add targeted generated-wwwroot handling and any reviewed artifact exclusions |
| 3 | `.github/workflows/deploy-production.yml` | Create dispatch-only provenance/approval/deployment workflow |
| 3 | `scripts/ci/Deploy-SmarterAsp.ps1` | Create scoped Web Deploy invocation with redaction, preservation and failure handling |
| 3 | `docs/Deployment/SmarterASP-GitHub-Actions.md` | Create operating procedure for secrets, manual evidence, promotion, failure and recovery |
| 1–3 | `docs/Updates/CI-CD-analysis-and-implementation-plan.md` | Update decisions/status after separately authorized implementation |

No production `.cs`, `.csproj`, Angular source, migration, or test source change is required for the baseline plan. Host-setting and GitHub Environment/branch-protection changes are external configuration tasks, not repository files. Optional runtime-version pin files or RID changes require a later explicit implementation decision and are not silently included. No migration workflow or Phase 4 workflow is proposed for initial creation.

**Stop point: this document is the sole deliverable of the current task. NO TESTS WERE RUN.**
