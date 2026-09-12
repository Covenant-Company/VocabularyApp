# Phase 3 controlled production deployment implementation results

## Objective and scope

Extend the proven master workflow with a separate, manually approved production deployment of its validated application artifact to SmarterASP.NET. Implementation is complete for review; production execution is deliberately unverified.

**CODEX RAN NO TESTS. CODEX PERFORMED NO DEPLOYMENT.** No MSDeploy invocation, production access, credential validation, GitHub Actions trigger, database connection, migration, local build/publish, commit, or push occurred. Public vendor documentation was consulted; no hosting account or application was accessed.

Modified `.github/workflows/backend-tests.yml`. Created `scripts/ci/Deploy-SmarterAsp.ps1` and this document. Existing lowercase `docs` casing is retained. The current request supersedes the historical analysis's dispatch-only proposal and manual-test policy.

## Existing gates and dependency structure

The trigger remains exclusively push to `master`. Backend / Integration Tests and Frontend Tests retain their exact successful commands, runners, and timeouts. `build` still requires both jobs. Its Release build, production Angular build, staging into API wwwroot, dotnet publish, package validation, artifact name, upload action, and 30-day retention are unchanged. `Publish-VocabularyApp.ps1` is unchanged.

The only additions inside `build` are an ID on the existing upload step and a job output exposing that upload's immutable artifact ID.

```text
Backend / Integration Tests --+
                             +--> Build / Publish Artifact
Frontend Tests --------------+             |
                                           v
                               deploy-production
                               production approval gate
                                           |
                                           v
                               Download exact artifact
                               Validate / discover Web Deploy
                               MSDeploy / result / STOP
```

`deploy-production`, displayed as **Deploy to SmarterASP.NET**, uses `needs: build`, a Windows 2022 runner, and a 20-minute timeout. Its master-push condition retains GitHub's implicit success requirement; there is no `always()` or failure suppression. Permissions remain `contents: read`, inherited from the workflow. No repository write permission is added.

## Production environment and approval

The job explicitly sets `environment: { name: production }`. The existing external GitHub Environment must continue to require the repository owner's approval, allow self-review, disable administrator bypass, and restrict deployment branches/tags to `master`. YAML references that protection; it does not create or verify those external settings. Configuration is accepted from the user's supplied state.

GitHub's protection gate applies before the job's steps and environment secrets become available. No custom approval flag, shell prompt, sleep, or dispatch trigger is added. See [GitHub environment protection](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments).

Job-level concurrency group `vocabularyapp-production` prevents overlapping deployments and uses `cancel-in-progress: false` so another run does not interrupt an active sync. GitHub concurrency is not a release-order guarantee. The reviewer must reject/cancel superseded runs and approve only the intended release; an older approved run must not subsequently overwrite a newer deployment. Pending runs can be replaced by GitHub's concurrency scheduling.

## Exact artifact handoff and validation

The upload keeps `VocabularyApp-SmarterASP-Publish-${{ github.sha }}-${{ github.run_attempt }}`. Download uses the upload's `artifact-id` through `needs.build.outputs.artifact-id`, rather than recomputing the attempt-dependent name. This also supports a deployment-only rerun using the successful build's original output. A missing ID fails before download; there is no latest-artifact fallback.

`actions/download-artifact@v8` receives no alternate repository, run ID, or API token, so it downloads only from the current workflow run. A single ID extracts directly into the newly created GUID directory under RUNNER_TEMP. `digest-mismatch: error` makes integrity mismatch fatal. A missing/expired artifact fails closed. See the [official download action inputs and single-ID behavior](https://github.com/actions/download-artifact).

Checkout is explicitly pinned to the tested `github.sha`, with credentials not persisted, to obtain the deployment script. No application rebuild or artifact transformation occurs in deployment.

Before any MSDeploy process can start, the script checks:

- Nonempty staging directory inside RUNNER_TEMP; no linked staging root.
- Nonempty WebApi DLL/executable, Data DLL, dependency/runtime JSON, appsettings.json, root web.config, and wwwroot/index.html.
- Nonempty Angular JavaScript and CSS bundles in wwwroot.
- No appsettings.Development.json or other non-root application settings, source/test directories, source/project/script/SQL files, profiles, keys, test binaries, hidden entries, filesystem links, nested web.config, or packaged app_offline.htm.

The original Phase 2 checks still establish the reviewed settings digest, ASP.NET Core IIS configuration, runtime targeting, and unchanged Angular assets before upload. Download integrity and repeated deployment layout checks preserve that handoff without building again. Any validation error prevents deployment.

## Microsoft Web Deploy discovery

The [official Windows 2022 runner inventory](https://github.com/actions/runner-images/blob/main/images/windows/Windows2022-Readme.md) lists `Microsoft.VisualStudio.Component.WebDeploy`. The job uses that Microsoft-installed component. The script probes the standard Program Files and Program Files (x86) `IIS/Microsoft Web Deploy V3/msdeploy.exe` locations, requires an existing file with a valid Microsoft Authenticode signature, and reports its file version. It never trusts a checkout executable or an arbitrary PATH match.

No installation or untrusted download is needed for the documented runner. Missing tooling or failed signature verification stops the job; there is no speculative fallback installer. If the hosted image changes, review its inventory and Microsoft's supported installation before changing this approach. The runner label and existing action major tags are mutable; this is validated discovery, not an immutable image/version pin. Codex did not invoke MSDeploy even for help/version discovery.

## Command, destination, and credentials

The command structure is:

```text
msdeploy.exe -verb:sync
  -source:contentPath=<downloaded-publish-directory>
  -dest:contentPath=<site/application>,computerName=<HTTPS-handler-URL>,
        userName=<environment-username>,password=<environment-secret>,
        authType=Basic,includeAcls=False
  -enableRule:DoNotDeleteRule
  -enableRule:AppOffline
  -disableLink:AppPoolExtension
  -disableLink:ContentExtension
  -disableLink:CertificateExtension
  -retryAttempts:0
```

This uses the provider's documented folder-to-site `contentPath` and Basic authentication approach; the downloaded artifact is a publish directory, not an MSDeploy package. ACL copying and server administration extension links are disabled. See [SmarterASP command-line deployment](https://www.smarterasp.net/support/kb/a2202/how-to-publish-_net-porject-using-msdeploy_exe-through-command-line.aspx).

| GitHub production value | Deployment use |
| --- | --- |
| `vars.SMARTERASP_WEBDEPLOY_URL` | Exact HTTPS MSDeploy handler endpoint, including supplied port/query |
| `vars.SMARTERASP_WEBDEPLOY_USERNAME` | Web Deploy username |
| `vars.SMARTERASP_SITE_NAME` | Exact delegated IIS site/application destination |
| `secrets.SMARTERASP_WEBDEPLOY_PASSWORD` | Password, supplied only to the final deployment step |

Site name is not inferred from repository name, a physical hosting folder, or public URL. Absolute paths, traversal segments, and URL-shaped destinations fail validation. The endpoint must be HTTPS with an MSDeploy.axd path, without embedded user information or a fragment. Supplied query parameters remain intact. The operator must keep the endpoint's site scope consistent with the destination setting. No account-specific values or optional public environment URL are hardcoded.

Provider values are quoted individually; ProcessStartInfo.ArgumentList performs Windows argument escaping without shell evaluation. Empty values, control characters, or values containing both single and double quote characters fail before contacting the host. A value containing either quote type alone uses the other delimiter. If the existing credential contains both quote types, stop and review an alternative transport; do not disclose it or silently modify it.

## Secret and TLS handling

The password is never copied to a GitHub variable, generated script, profile, artifact, or log file. Script source contains only environment-variable references. Debug execution is disabled and Actions debug mode is rejected. Process arguments and environment values are never printed. The child environment omits the password variable, although MSDeploy necessarily receives it as a command-line provider argument.

**Residual CLI tradeoff:** privileged processes on the ephemeral runner could inspect the MSDeploy command line or process memory. GitHub masking cannot prevent that. Keep deployment on the isolated hosted runner and do not enable process dumps, tracing, transcripts, or diagnostic artifact uploads. The script clears credential references after invocation, but does not claim secure erasure of managed memory.

Both native output streams are captured concurrently in memory. Raw output and process exception details are suppressed. Only standard `ERROR_*` identifiers after literal credential redaction, the native exit code, and script-generated status messages reach logs. This retains actionable error categories while deliberately sacrificing detailed native messages that might echo credentials. Further diagnosis belongs in the user's secure hosting tools; do not add raw output to public Actions logs.

Basic authentication runs over certificate-validated HTTPS. No `allowUntrusted`, certificate bypass, TLS downgrade, or custom certificate acceptance is enabled. SmarterASP's example includes `allowUntrusted`; this implementation does not adopt it. If the actual endpoint has an invalid/untrusted certificate, deployment fails and the user must resolve trust with the provider before retrying. No certificate exception is preauthorized.

## Synchronization, file locks, and failures

`DoNotDeleteRule` preserves destination-only files, including potential host files, uploads, and logs. It still adds/overwrites files represented in the artifact, including root web.config and appsettings.json. Production secrets must continue to live in the existing external hosting configuration. This is not an exact destination mirror: obsolete bundles, DLLs, and historical settings may survive. Removing obsolete application files requires a separately reviewed manual cleanup. See [Microsoft's folder synchronization guidance](https://devblogs.microsoft.com/dotnet/web-deploy-msdeploy-how-to-sync-a-folder/).

`AppOffline` requests Web Deploy-managed app_offline.htm handling for IIS content synchronization. ASP.NET Core's IIS module shuts down the application when this file is present, helping release locks. Web Deploy handles bringing the app back online; no control-panel automation, application-pool API, custom offline-file deletion, or host restart is added. See [Microsoft Web Deploy file-lock and offline error guidance](https://learn.microsoft.com/en-us/troubleshoot/developer/webapps/iis/deployment-migration/web-deploy-error-codes) and [ASP.NET Core app offline behavior](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/advanced?view=aspnetcore-8.0#app_offlinehtm).

This is an in-place deployment with possible downtime, not an atomic switch. Account-specific offline/delegation behavior remains for the first approved run. A lock error or unsupported rule must fail, not fall back to live copying. `retryAttempts:0` prevents automatic native retries. Any nonzero MSDeploy exit code throws and fails the GitHub job. There is no continue-on-error, success override, automatic rollback, or production smoke-test step.

After failure/cancellation/timeout, assume content may be partial and app_offline.htm may remain or may have been removed by Web Deploy cleanup. Do not assume a failed sync restored the old application. The user should stop newer approvals, inspect the safe error codes and hosting state, recover the intended content, and verify offline-file state before reopening service. Cleanup failure is a deployment failure too.

## Database exclusion and manual recovery

No SQL connection string or database credential enters GitHub Actions. No database provider, SQL execution, schema update, seed operation, EF command, or migration bundle is added. Program.cs contains no startup Migrate/EnsureCreated call. Deploying the application can restart normal application operation, but the pipeline does not migrate the production database.

Before approving production, retain the previous known-good artifact and its run/SHA outside the 30-day expiration window if necessary. Recovery is a user-controlled redeployment of those exact bytes using the existing manual hosting process, after confirming compatibility with the current schema. Inspect partial/stale files and preserve host-owned content; do not blindly mirror/delete the destination. The new workflow does not accept arbitrary historical artifacts and implements no rollback or database reversal.

## First production deployment: user procedure

1. Merge the reviewed Phase 3 PR into `master`. This is the user's action; Codex did not commit, push, merge, or trigger it.
2. Open the automatically created Actions run. Confirm Backend / Integration Tests and Frontend Tests both pass, followed by Build / Publish Artifact, publish validation, and artifact upload.
3. Confirm **Deploy to SmarterASP.NET** is waiting for the `production` required-reviewer approval. If that approval gate is absent, stop/cancel the run and repair the Environment protection before deployment.
4. Review the exact run SHA and artifact. Confirm the environment's three variables refer to the intended site's Web Deploy settings without displaying the password. Confirm the previous production artifact is retained, the release is schema-compatible, and a brief maintenance window is acceptable. Cancel superseded pending runs.
5. As the required reviewer, use GitHub's **Review deployments**, select **production**, and approve the intended deployment. Self-review is allowed under the supplied configuration; administrator bypass must remain disabled.
6. Watch artifact download, validation, Microsoft tooling discovery, and MSDeploy complete. A failed step requires investigation and manual recovery assessment before any rerun. Do not weaken certificate verification or reveal the password to troubleshoot.
7. Manually verify the live application: home/landing page; Angular navigation and direct-route refresh; login screen; dictionary lookup UI; existing API responses; JavaScript/CSS/static assets; and absence of ASP.NET startup errors. Confirm from the reviewed workflow/run that no production database migration was performed.
8. Record the deployed SHA, artifact ID, approval, result, and manual verification. Stop after deployment verification; database changes and rollback automation remain separate work.

These production steps are for the user only and were not performed by Codex.

## Static validation results and remaining limits

PowerShell parser inspection passed without executing the deployment script. Local js-yaml parsing and structural inspection passed for the master-only trigger, permissions, dependency gates, failure handling configuration, and current-run artifact-ID download with digest enforcement. Node required `--preserve-symlinks` to avoid a sandbox realpath restriction; no packages were installed. Text comparison confirmed all preexisting workflow content is unchanged except the upload ID/job output. The publish-script SHA-256 stayed unchanged.

Git status remains unavailable due to the repository ownership check, including a command-scoped safe-directory attempt. No persistent Git configuration changed, and no clean-working-tree claim is made. Runtime MSDeploy behavior, endpoint trust/access, actual secret character compatibility, and live application health remain deliberately unverified until the user's first approved run. No known static implementation blocker remains.

**CODEX RAN NO TESTS. No deployment, production access, migration, workflow trigger, commit, or push occurred. Stop after Phase 3 implementation.**
