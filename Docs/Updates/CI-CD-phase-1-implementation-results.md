# Phase 1 CI implementation results

Date: 2026-09-12. Scope: workflow configuration and static validation only.

**CODEX EXECUTION: Tests were NOT run by Codex.** No dependency installation, build, application startup, publish, GitHub Actions trigger, deployment, database migration, production access, commit, or push was performed.

**GITHUB ACTIONS CONFIGURATION: Tests ARE configured to run automatically on pushes to `master`.** A normal PR merge into `master` produces that push. There are no PR, development-branch, tag, manual-dispatch, or deployment triggers in this workflow.

## Files and existing workflow decision

- Modified `.github/workflows/backend-tests.yml` in place; display name is now `Phase 1 CI`.
- Modified `docs/Updates/CI-CD-analysis-and-implementation-plan.md` with a superseding implementation notice.
- Created this results document.

The only existing workflow previously restored/built the solution and ran its backend tests on every push and pull request. Retaining its filename and replacing its trigger/jobs avoids adding a second workflow for the same master push. No workflow was deleted. No existing deployment workflow was found.

The current user instruction overrides the historical analysis's proposal to remove automated tests. Tests remain prohibited for Codex execution but are mandatory GitHub gates.

## Trigger and graph

```yaml
on:
  push:
    branches:
      - master
```

```text
Backend / Integration Tests ---+
                              +--> .NET API / Angular Production Build
Frontend Tests ---------------+
```

The build job has `needs: [backend-tests, frontend-tests]` and no condition overriding normal success gating. Required steps have no failure suppression or `continue-on-error`. A failed or skipped test job prevents the build job from running, consistent with [GitHub's job dependency rules](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds).

Two separate test jobs provide independent failure diagnostics. One downstream build job keeps the .NET build, Angular build, and combined wwwroot staging in the same checkout without Phase 2 artifact transfer. Each operation has a descriptive step name. The test commands necessarily compile their own test inputs before executing; the downstream application build remains gated on both suites.

## Toolchain and exact commands

All jobs use `windows-2022`, matching the existing WebApi/Data `win-x64` runtime identifiers. Backend jobs install .NET `8.0.x`; all three solution projects target `net8.0`. Frontend jobs install Node `22.x` with its bundled npm and cache npm downloads using `VocabularyApp.UI/package-lock.json`.

No repository Node pin or engines requirement exists in package.json. Node 22 follows the analysis and is accepted by the locked Angular CLI engines and [Angular 18.2 compatibility table](https://angular.dev/reference/versions). The lock resolves Angular core 18.2.14 and uses lockfile version 3. Root dependencies and devDependencies match package.json. No dependencies or lockfiles were changed.

GitHub command steps for **Backend / Integration Tests**, from repository root:

```powershell
dotnet restore VocabularyApp.sln
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release --no-restore
```

There is no namespace/category filter: Security, Services, Integration, and infrastructure cases in this project are included. The test step builds its inputs; it deliberately does not use `--no-build`.

GitHub command steps for **Frontend Tests**, from `VocabularyApp.UI`:

```powershell
npm ci
npm test -- --watch=false --browsers=ChromeHeadless
```

The existing `test` script is `ng test`; angular.json uses the Karma builder and the lock includes the Chrome launcher. This uses the existing supported headless command without introducing a new script. It relies on Chrome being available on the hosted Windows image; missing Chrome fails the gate.

GitHub command steps for **.NET API / Angular Production Build**, after both gates pass, from repository root:

```powershell
dotnet restore VocabularyApp.sln
dotnet build VocabularyApp.sln --configuration Release --no-restore
```

Then from `VocabularyApp.UI`:

```powershell
npm ci
npm run build -- --configuration production
```

This uses the analysis's solution restore/build approach, compiling API, Data, and test assemblies without executing tests again. The production Angular configuration applies the environment replacement, output hashing, and existing bundle/style budgets.

The final GitHub step executes this PowerShell from the workflow:

```powershell
$browserOutput = Join-Path $env:GITHUB_WORKSPACE 'VocabularyApp.UI/dist/vocabulary-app.ui/browser'
$staticRoot = Join-Path $env:GITHUB_WORKSPACE 'VocabularyApp.WebApi/wwwroot'
if (-not (Test-Path -LiteralPath (Join-Path $browserOutput 'index.html') -PathType Leaf)) {
  throw 'Angular browser output is missing index.html.'
}
New-Item -ItemType Directory -Path $staticRoot -Force | Out-Null
Get-ChildItem -LiteralPath $browserOutput -Force |
  Where-Object { $_.Name -ne 'web.config' } |
  Copy-Item -Destination $staticRoot -Recurse -Force
if (-not (Test-Path -LiteralPath (Join-Path $staticRoot 'index.html') -PathType Leaf)) {
  throw 'API wwwroot is missing the staged Angular index.html.'
}
if (Get-ChildItem -LiteralPath $staticRoot -Filter web.config -Recurse -Force) {
  throw 'Angular IIS configuration must not be staged in API wwwroot.'
}
```

Repository inspection confirmed no npm/MSBuild target copies these files automatically. The application builder's outputPath is `dist/vocabulary-app.ui`, with browser content under `browser`. The copy stages the contents, including nested assets, directly under API wwwroot. It excludes the Angular-only IIS configuration because API startup already provides SPA fallback. The additional check rejects nested IIS configuration. No recursive deletion is performed.

Generated files exist only in the hosted runner checkout and are neither committed nor uploaded. No generated output was created locally. This validates the staging layout, not a published IIS package: no `dotnet publish`, archive, artifact upload, deployment script, or Phase 2 work is included.

## Database safety

Source inspection found no production database fallback in the existing test paths:

- `Infrastructure/VocabularyAppWebApplicationFactory.cs` selects `Testing`, overrides connection configuration, removes ApplicationDbContext and its options, and registers a factory-owned SQLite `Data Source=:memory:` connection. Its `EnsureCreated` operates on that replacement context.
- `Infrastructure/RelationalDatabaseFixture.cs` similarly owns private in-memory SQLite and destroys it when the connection is disposed.
- The factory supplies deterministic test JWT configuration and replaces the dictionary HTTP transport with `ControllableDictionaryHandler`; production JWT and dictionary credentials are unnecessary.
- `Program.cs` registers SQL Server for ordinary application execution but performs no startup migration or database initialization. `ApplicationDbContext` has no OnConfiguring fallback. Checked-in connection defaults target LocalDB, not production.
- Searches across the test project found SQLite registration and no direct SqlConnection or UseSqlServer path.

The workflow has only `contents: read` permission, uses hosted runners, and references no secrets or production environment. It provides no production database or hosting credentials. No SQL service, EF migration, Web Deploy, VSDeploy, FTP, or production connection command is configured.

This is a source-level assessment. SQLite does not validate SQL Server-specific migrations, collation, T-SQL, or provider concurrency behavior. Future changes that open a database before test DI substitution require renewed review.

## Static validation and limitations

- Parsed the workflow with the existing local js-yaml library; confirmed exactly the master push trigger, three jobs, and both build dependencies.
- Parsed package.json/package-lock.json, checked dependency agreement and CLI engines, and inspected Angular, solution, project, startup, test infrastructure, and packaging paths.
- Reviewed required command exit handling and staging PowerShell syntax without executing the workflow steps.
- No tests or builds were executed, so runtime suite results, package restore, hosted Chrome availability, production budgets, and Node 22 build compatibility remain for the first authorized GitHub run.
- Action references use official major version tags (`checkout@v4`, `setup-dotnet@v4`, `setup-node@v4`), following the existing workflow convention rather than the analysis's proposed full-SHA pins. Tags and SDK/Node patch versions can move; immutable pinning remains a reproducibility limitation.
- Git status is blocked by an ownership check, including with a command-scoped safe.directory exception. No persistent Git configuration was changed. Filesystem inspection confirms the current branch ref is `devops/ci-cd-pipeline` and a local `master` ref exists; remote branch settings and ancestry were not queried. No evidence contradicted the user's production branch designation.

Phase 1 configuration is complete. No deployment or database migration was performed or configured. Nothing was committed or pushed. Phase 2 has not been implemented.
