# Phase 2 artifact implementation results

## Scope and Phase 1 preservation

Phase 2 extends the proven pipeline to publish, validate, and upload a downloadable application package, then stop. **CODEX RAN NO TESTS.** No local build/publish, dependency installation, application startup, workflow trigger, production access, deployment, migration, commit, or push was performed.

The resumed checkout identifies `devops/ci-cd-phase-2-artifact` in `.git/HEAD`. The three requested CI/CD documents and proven Phase 1 workflow are now present. The earlier missing-workflow discrepancy is resolved by the updated checkout. Git status remains blocked by its ownership check; no Git settings or branches were changed, and remote ancestry was not independently verified.

Modified `.github/workflows/backend-tests.yml`. Created `scripts/ci/Publish-VocabularyApp.ps1` and this document. No application, project, test, dependency, configuration-source, or database files changed.

Exact trigger remains:

```yaml
on:
  push:
    branches:
      - master
```

Workflow display name is now `CI / Publish Artifact`. The existing `build` job is named `Build / Publish Artifact` and retains `needs: [backend-tests, frontend-tests]`. Existing job runners, timeouts, .NET 8/Node 22 setup, npm cache, test commands, Release build, production Angular build, and staging logic are preserved. No failure suppression, skipped tests, alternate trigger, or condition bypass was added.

```text
Backend / Integration Tests ----+
                               +--> Build / Publish Artifact
Frontend Tests ----------------+      .NET Release build
                                      Angular production build
                                      Angular -> API wwwroot
                                      WebApi restore/publish
                                      Sanitize and validate
                                      Upload artifact -> STOP
```

One job retains the existing checkout and Angular output rather than repeating builds in another job. Any test, build, publish, validation, or upload failure fails its job and prevents subsequent steps. Native restore/publish exit codes are checked explicitly in the script. The output path is exposed to the upload action only after validation succeeds.

## Exact GitHub Actions commands

Commands below describe workflow configuration; Codex did not execute them.

Backend gate, repository root:

```powershell
dotnet restore VocabularyApp.sln
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release --no-restore
```

Frontend gate, `VocabularyApp.UI`:

```powershell
npm ci
npm test -- --watch=false --browsers=ChromeHeadless
```

Build job, repository root:

```powershell
dotnet restore VocabularyApp.sln
dotnet build VocabularyApp.sln --configuration Release --no-restore
```

Then in `VocabularyApp.UI`:

```powershell
npm ci
npm run build -- --configuration production
```

The existing inline PowerShell staging step in the workflow copies contents of `VocabularyApp.UI/dist/vocabulary-app.ui/browser` to `VocabularyApp.WebApi/wwwroot`, including nested assets. It excludes the browser-root `web.config`, checks index.html before/after copying, and rejects nested web.config. That script remains unchanged. The next command, from repository root, is:

```powershell
./scripts/ci/Publish-VocabularyApp.ps1
```

Within that script, `$project` resolves to `$env:GITHUB_WORKSPACE/VocabularyApp.WebApi/VocabularyApp.WebApi.csproj` and `$publishDirectory` is a newly created `$env:RUNNER_TEMP/VocabularyApp-publish-<random-guid>` directory:

```powershell
dotnet restore $project --runtime win-x64 -p:SelfContained=false
dotnet publish $project --configuration Release --runtime win-x64 --self-contained false --no-restore --output $publishDirectory -p:DebugType=None -p:DebugSymbols=false
```

The script then removes only published `appsettings.Development.json`, validates the output, and writes `publish-directory` to GITHUB_OUTPUT. Source configuration is not deleted. The full file operations and validation rules are reviewable in the script. No recursive deletion is used; a GUID directory under the resolved runner temporary directory avoids stale packages. Publish output is neither cached nor placed under tracked source folders.

## Runtime and IIS decisions

Both WebApi and Data already specify `RuntimeIdentifier=win-x64` and target .NET 8. Preserve this existing target; explicitly choose framework-dependent publishing with `--self-contained false`, consistent with the repository analysis. A project-specific restore matches those settings. Publishing rebuilds after Angular staging so the Web SDK discovers static assets; `--no-build` would risk omitting them. Debug symbols are disabled for the publish build.

The package requires Windows x64, the .NET 8 ASP.NET Core runtime, and IIS ASP.NET Core Module V2 on the eventual host. Those hosting requirements remain subject to future deployment verification; no host was contacted. This package is not self-contained and does not supply production configuration.

The root web.config is generated by the Web SDK, never copied from Angular. Validation requires an AspNetCoreModuleV2 handler, in-process hosting, and either the WebApi executable or dotnet with the WebApi DLL argument. Embedded environmentVariable or connectionStrings elements are rejected. Any nested web.config, including under wwwroot, fails validation.

## Artifact and contents

Upload uses `actions/upload-artifact@v7`, the stable major shown in the [official action documentation](https://github.com/actions/upload-artifact) and [latest release](https://github.com/actions/upload-artifact/releases/tag/v7.0.1) inspected during implementation. Existing setup/checkout action versions are unchanged.

- Name: `VocabularyApp-SmarterASP-Publish-${{ github.sha }}-${{ github.run_attempt }}`.
- Path: `${{ steps.publish.outputs.publish-directory }}`; only validated publish-directory contents are uploaded.
- Retention: 30 days, subject to repository policy.
- Empty upload: `if-no-files-found: error`.
- Hidden files: excluded by the action and rejected by validation.
- No overwrite option, separate source archive, or deployment upload is configured.

Expected downloadable archive contents at its root:

```text
VocabularyApp.WebApi.dll
VocabularyApp.WebApi.exe
VocabularyApp.WebApi.deps.json
VocabularyApp.WebApi.runtimeconfig.json
VocabularyApp.Data.dll
web.config
appsettings.json
[runtime dependencies, native libraries, supporting publish files]
wwwroot/
  index.html
  [Angular production JavaScript, CSS, and assets]
```

The full SHA in the artifact name and the Actions run associate the package with its source commit. A separate manifest/ZIP is unnecessary for this requested scope: the artifact action creates the downloadable archive. This extends the historical analysis's artifact proposal without adding a second expensive job or deployment tooling.

## Validation and configuration safety

The script fails before upload for:

- Missing/empty WebApi DLL/executable, Data DLL, dependency/runtime JSON, root web.config, appsettings.json, or wwwroot/index.html.
- Missing root Angular JavaScript or CSS bundles.
- Any missing or changed browser output file after staging/publish, except the deliberately excluded root Angular web.config. Each asset is compared by SHA-256, covering nested images and other generated assets too.
- Nested web.config, incorrect IIS process/module/hosting model, or embedded IIS runtime environment/connection values.
- A runtime configuration lacking the framework-dependent ASP.NET Core 8 reference.
- Hidden entries or filesystem links; obvious src, obj, bin, node_modules, Properties, test/TestResults directories; testhost/xunit/test-project files; source/project/script/SQL files, source maps, PDBs, publish profiles, private-key/certificate files, secrets-named files, or launchSettings.
- Any appsettings file except the reviewed root appsettings.json.

The published appsettings.json is retained unchanged with reviewed LocalDB defaults, a blank WordsAPI key, no JWT SecretKey, public issuer/audience/CORS values, and logging defaults. Its SHA-256 is pinned after CRLF-to-LF normalization and trimming, both before and after publish. Therefore a changed connection string, injected key, extra property, or other configuration change blocks packaging instead of silently shipping it. Future intentional non-secret edits require reviewing the file and updating the digest in the same reviewed change. Configuration contents are not printed on failure.

Production connection strings, JWT secrets, and API credentials must remain external host configuration in a later phase. No secrets expressions, hosting environment, or credential input were added to the workflow. The Angular production environment contains only production=true and same-origin `/api` configuration. Published development settings are removed without changing source settings.

These guards cover reviewed configuration and obvious sensitive/package files; they are not a universal secret detector for arbitrary strings embedded in assemblies or browser assets. Source review remains necessary for future changes. Existing third-party runtime dependencies emitted by Web SDK are retained, rather than guessing which DLLs can be removed.

## Database and deployment safety

The existing integration infrastructure still uses private in-memory SQLite under Testing, with substituted DbContext registrations. No test infrastructure changed. Program.cs contains no startup migration/EnsureCreated call. Publishing compiles/packages the application without launching it. No production SQL connection, database secret, SQL command, migration command, Web Deploy, VSDeploy, FTP, hosting credential, or production connectivity check is configured. Uploading to GitHub Actions artifacts is the endpoint of Phase 2.

## Static validation and manual verification

PowerShell parser validation passed without executing the script. YAML parsed successfully, confirming the master-only trigger, both test commands, unchanged needs dependencies, and upload settings. Runtime publishing and generated-package validation have not been executed by Codex. The first post-merge GitHub run must verify actual Web SDK output and the artifact upload; future SDK or package layout changes may legitimately require reviewing validation rules. Major action tags and SDK/Node patch versions remain mutable, as in Phase 1.

After the user merges into master:

1. Open the resulting Actions run and confirm Backend / Integration Tests, Frontend Tests, and Build / Publish Artifact are green.
2. Confirm publish/validation and upload steps succeeded; failed gates must leave downstream work skipped.
3. Confirm the SHA-named artifact appears in the run's Artifacts section and download it.
4. Inspect the extracted root API files, framework-dependent runtime configuration, ASP.NET Core web.config, wwwroot/index.html, JavaScript/CSS, and nested assets.
5. Verify there is no nested web.config, development settings, source/test directories, profiles, or obvious secrets. Review retained appsettings defaults and associate the artifact with the run SHA.
6. Retain the artifact only as needed within the 30-day window. **Do not deploy it yet.**

**CODEX RAN NO TESTS. No deployment, production access, migration, commit, or push occurred. Phase 3 was not implemented.**
