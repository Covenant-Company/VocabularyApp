# Phase 3 portable .NET runtime-resolution remediation

## Change and rationale

The [analysis](CI-CD-phase-3-dotnet-runtime-resolution-analysis.md) establishes that CI previously produced a win-x64 framework-dependent apphost artifact. Supplied production stdout reports failure to find Microsoft.NETCore.App 8.0.0 with the app directory selected as the .NET location. The correction removes the failing EXE entry path and produces a portable DLL artifact that uses the installed dotnet host. The exact host-side reason for the old search location remains unverified; successful production startup still requires the installed .NET 8 runtime to be discoverable by IIS.

Modified:

- `scripts/ci/Publish-VocabularyApp.ps1`: override the project RID and disable apphost for restore/publish; adapt and strengthen artifact validation.
- `scripts/ci/Deploy-SmarterAsp.ps1`: remove the EXE requirement and reject an EXE in the portable artifact. No invocation, credentials, synchronization rules, diagnostic sanitization, or exit handling changed.

Created this document and `docs/Updates/CI-CD-phase-3-dotnet-runtime-resolution-analysis.md`. No project files, workflow, Angular source, application logic, or database configuration were edited. Ignored local validation output remains under `artifacts/runtime-resolution`, and the Angular build refreshed ignored dist/cache output. Temporary WebApi/wwwroot staging created for validation was removed afterward; it was absent before this task.

## Exact command changes

Old publish restore:

```powershell
dotnet restore $project --runtime win-x64 -p:SelfContained=false
```

New publish restore:

```powershell
dotnet restore $project -p:RuntimeIdentifier= -p:UseAppHost=false -p:SelfContained=false
```

Old publish:

```powershell
dotnet publish $project --configuration Release --runtime win-x64 --self-contained false --no-restore --output $publishDirectory -p:DebugType=None -p:DebugSymbols=false
```

New publish:

```powershell
dotnet publish $project --configuration Release -p:RuntimeIdentifier= -p:UseAppHost=false --self-contained false --no-restore --output $publishDirectory -p:DebugType=None -p:DebugSymbols=false
```

The empty command-line RuntimeIdentifier global property overrides the win-x64 value in both WebApi and its Data project reference for this publish. UseAppHost=false explicitly prevents platform-specific executable generation. Project defaults for unrelated builds remain intact. Self-contained mode stays disabled. No single-file setting or runtime roll-forward override was added.

Before, the supplied IIS configuration launched `processPath=".\VocabularyApp.WebApi.exe"`. The locally verified SDK-generated result is:

```xml
<aspNetCore processPath="dotnet" arguments=".\VocabularyApp.WebApi.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
```

No web.config was hardcoded or manually substituted. InProcess and AspNetCoreModuleV2 remain unchanged. Normal publishing leaves temporary diagnostic stdout logging disabled.

## Artifact checks

The publish validator still checks required application files, Angular bundles and individual asset hashes, reviewed settings digest, prohibited source/test/sensitive files, nested IIS configuration, and framework-dependent hosting. It now rejects the WebApi EXE, requires the exact dotnet/DLL entry point, checks both shared .NET 8 frameworks and net8.0, rejects includedFrameworks, and requires the RID-free `.NETCoreApp,Version=v8.0` dependency target.

The deployment validator requires the DLL and rejects an EXE in the downloaded artifact. All other deployment behavior remains unchanged, including immutable artifact handoff, digest verification, production approval, AppOffline, destination-only file preservation, HTTPS/certificate validation, and failure diagnostics. An old EXE-based artifact will intentionally fail the new validator; this job must consume the new matching artifact.

## Local validation results

The latest user request explicitly authorized local validation, including tests. Earlier task-specific no-test restrictions did not apply to this work.

| Validation | Result |
| --- | --- |
| Solution restore | Passed with .NET SDK 8.0.419 using the existing NuGet package cache |
| Release solution build, `--no-restore` | Passed: 0 errors; NU1900 warnings and an existing CS1998 test warning |
| Release WebApi.Tests, `--no-restore --no-build` after the build | **172 passed, 0 failed, 0 skipped** |
| Angular `npm run build -- --configuration production` | Passed; existing word-lookup stylesheet budget warning (2.80 kB versus 2.05 kB) |
| Evaluated publish properties | RuntimeIdentifier empty, UseAppHost=false, SelfContained=false, TargetFramework=net8.0; PublishSingleFile unset |
| Corrected restore/publish command sequence | Passed from a clean scratch source copy using SDK 8.0.419 |
| Generated web.config | dotnet/DLL, InProcess, AspNetCoreModuleV2, stdout disabled |
| Required DLL/deps/runtimeconfig/config/index files | Present and nonempty |
| Framework dependence | Both Microsoft.NETCore.App and Microsoft.AspNetCore.App reference 8.0.0; no includedFrameworks; RID-free dependency target |
| Apphost/runtime binaries | No root WebApi.exe, hostfxr.dll, hostpolicy.dll, or coreclr.dll |
| Angular integrity | All 5 browser assets match the production build by SHA-256; JS/CSS and index present |
| Excluded contents | No source/test/node_modules folders, development settings, nested web.config, prohibited sensitive/source files, links, or hidden entries |
| Reviewed appsettings digest | Passed unchanged |
| PowerShell syntax | Both modified scripts passed parser inspection |
| Workflow preservation | SHA-256 unchanged: `1038BDC1484CCAEF32700118A29A332335E02D61AC7F79DE373A04BB3B1CADEC` |

The solution commands were run against VocabularyApp.sln and VocabularyApp.WebApi.Tests.csproj from an ignored scratch working directory whose global.json selected installed SDK 8.0.419. Test execution used the existing private SQLite fixtures and substituted dictionary transport; it did not access production SQL or run EF migrations. Frontend tests were not run locally; their unchanged GitHub gate remains mandatory.

Initial SDK 10 restore succeeded, but final .NET build/test/publish validation used SDK 8 to match CI's major version. SDK 8 first-use writes needed a scratch DOTNET_CLI_HOME. An initial restore with that empty home failed to reach NuGet; pointing NUGET_PACKAGES to the existing cache resolved dependencies. NU1900 warnings mean vulnerability metadata could not be refreshed. No package versions or audit configuration were changed.

The Angular build initially encountered the sandbox's filesystem realpath restriction. The same authorized build succeeded outside the sandbox. Local Node was 18.20.8, whereas unchanged CI uses Node 22; no Node configuration was changed.

The first local publish generated the correct IIS entry point but failed content validation because preexisting ignored Archive/publish directories inside the local WebApi folder were included by the Web SDK. No user archives were removed or validator weakened. A fresh scratch copy of the nonignored project sources and newly built browser assets reproduced a clean CI checkout, and the same publish commands passed all artifact checks there. Published development settings were removed exactly as the CI script does. The validated output is `artifacts/runtime-resolution/publish-clean`.

PowerShell 7 was not available locally. The corrected native commands were executed directly and the produced package was checked using equivalent PowerShell file/XML/JSON/hash inspection; the complete CI PowerShell script was parser-checked rather than executed end to end. Actual GitHub execution remains the final verification of that wrapper.

## Production verification and risks

After the user merges a reviewed change, both test gates and artifact production must pass before the existing manual production approval. The user must approve deployment and verify startup using the new dotnet/DLL web.config. Deployment success alone does not establish application health.

The shared host still needs a discoverable compatible x64 .NET 8 runtime and IIS hosting module. Portable publishing does not eliminate native dependencies from packages such as SqlClient or guarantee operation on every OS. It removes the application RID/apphost selection while retaining package runtime assets selected by the installed host.

Destination preservation can leave a prior EXE or host/runtime files on production. The new web.config no longer selects the EXE, but no automatic server cleanup is added. If startup still fails, obtain new user-collected stdout and inspect runtime discovery/stale files separately. This change does not claim to have repaired or inspected the existing production installation.

No SmarterASP settings, GitHub secrets/environment values, TLS policy, approval gate, database configuration, or automatic migration behavior changed. No automatic rollback or deployment retry loop was added.

Recommended branch: `fix/ci-portable-dotnet-publish`.

Recommended commit message: `fix(ci): publish portable framework-dependent WebApi artifact`.

**Local build and 172 backend/integration tests passed. Codex performed no production deployment, MSDeploy invocation, GitHub Actions trigger, production access, hosting change, database migration, commit, or push.**
