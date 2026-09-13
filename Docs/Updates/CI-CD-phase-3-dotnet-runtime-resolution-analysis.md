# Phase 3 .NET runtime resolution analysis

## Observed failure

The user reports that tests, publishing, approval, and Web Deploy succeeded, but IIS returns HTTP 500.31. The supplied pool configuration is x64, InProcess, with advertised .NET 8 support. Supplied stdout:

```text
You must install or update .NET to run this application.

App: h:\root\home\ripcody-001\www\VocabularyApp\VocabularyApp.WebApi.exe
Architecture: x64
Framework: 'Microsoft.NETCore.App', version '8.0.0' (x64)
.NET location: h:\root\home\ripcody-001\www\VocabularyApp\

No frameworks were found.
```

This proves runtime discovery failed in that invocation, with the app directory reported as the runtime location. It does not prove the provider lacks .NET 8 globally. Nor does a RID-specific framework-dependent apphost normally require an app-local runtime: host configuration, runtime installation/registration, or leftover host files could also influence resolution. No production inspection was performed. The exact host-side reason for selecting this location remains unverified.

## Repository configuration

Inspected the workflow, publish/deploy scripts, WebApi and Data project files, and test project/infrastructure. No repository publish profiles or Directory.Build files were found. Both WebApi and Data specify `RuntimeIdentifier=win-x64`. Neither sets SelfContained, PublishSingleFile, or UseAppHost. IncludeNativeLibrariesForSelfExtract is present but does not itself enable single-file publishing.

The workflow builds Angular production output and copies browser contents into WebApi wwwroot before calling Publish-VocabularyApp.ps1. That script restores with `--runtime win-x64 -p:SelfContained=false` and publishes with `--runtime win-x64 --self-contained false --no-restore`, Release, and no debug symbols.

Consequently the artifact is RID-specific and framework-dependent, not self-contained. Default apphost generation produces VocabularyApp.WebApi.exe. Web SDK generation uses that apphost for the IIS process path; the observed runtimeconfig references shared .NET 8 frameworks. The EXE requirement in both artifact validators currently enforces this layout.

Local SDK 8.0.419 inspection of `Microsoft.NET.Sdk.Publish.TransformFiles.targets` confirms the web.config transformation passes the evaluated UseAppHost setting to its transform task and disables that transform's apphost mode when RuntimeIdentifier is empty. Subsequent local publishing verified the generated dotnet/DLL entry point; no handwritten IIS configuration was needed.

Simply removing `--runtime win-x64` is insufficient because the project files still set the RID. Removing the RID alone also does not explicitly disable default executable generation. CI restore and publish must consistently override the project RID and disable apphost generation.

## Recommended minimal remediation

Keep project defaults untouched for unrelated builds. For CI publish restore and publish, pass `-p:RuntimeIdentifier= -p:UseAppHost=false` and retain explicit framework-dependent settings. A command-line global property overrides the project value, including referenced-project evaluation. Verify the evaluated properties and actual output locally.

Let the Web SDK generate web.config naturally with `processPath="dotnet"`, `arguments=".\VocabularyApp.WebApi.dll"`, and `hostingModel="inprocess"`. Adapt both validators to require the DLL and reject the obsolete apphost; strengthen publish validation to enforce the exact generated portable entry point and framework-dependent runtime configuration.

Microsoft documents UseAppHost=false for portable DLL publishing and the dotnet/DLL IIS hosting pattern: [publishing overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [IIS web.config guidance](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/host-and-deploy/iis/web-config.md).

This removes the failing apphost entry path and delegates framework resolution to the installed dotnet host. It is a supported artifact correction, not proof that all hosting runtime-registration issues are resolved. The installed x64 .NET 8 runtime and IIS module must still be discoverable by the pool. Framework version 8.0.0 in runtimeconfig is normal for a .NET 8 framework-dependent artifact; it does not require installing exactly patch 8.0.0.

## Alternatives and boundaries

- Removing both project RIDs would affect all local builds; a CI-only global override is narrower.
- UseAppHost=false alone removes the EXE but leaves a RID-specific artifact; clear the RID too.
- Self-contained publishing would bundle runtime files and change servicing and artifact size; it is unnecessary for the requested installed-runtime approach.
- A handwritten web.config or server-side artifact edit would duplicate SDK behavior and break reproducibility.
- Changing pool settings, switching to OutOfProcess, changing runtime versions, or modifying production environment variables is outside this correction.

Preserve tests/build gates, approval, artifact IDs/digests, Angular behavior, InProcess hosting, Basic authentication, HTTPS/certificate checks, diagnostic sanitization, MSDeploy options, destination-only preservation, and nonzero-exit handling. Do not introduce credentials, migrations, database changes, retries, or rollback automation.

DoNotDeleteRule means an earlier EXE or runtime file can survive on the destination. The new web.config will no longer reference the EXE, but this remediation does not delete unknown server files or certify their harmlessness. If startup still fails, the user should collect new stdout and review host runtime discovery and stale host files through a separately authorized investigation.

Local restore/build/tests/publish are explicitly authorized by the latest request, superseding earlier no-test instructions. Production deployment, hosting changes, commits, and pushes remain prohibited.
