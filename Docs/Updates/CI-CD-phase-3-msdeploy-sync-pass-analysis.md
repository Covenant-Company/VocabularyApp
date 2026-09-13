# Phase 3 MSDeploy zero sync-pass analysis

## Conclusion and confidence

**High confidence: the immediate cause is the script's explicit `-retryAttempts:0` argument.** It is passed unchanged to MSDeploy and matches the reported maximum sync-pass count of zero. The original implementation incorrectly used a native deployment-operation setting as though zero merely disabled retries of a complete failed deployment while allowing normal synchronization to proceed.

The smallest recommended remediation is to **remove that one argument**, allowing Web Deploy's normal bounded operation behavior. Do not implement a retry loop or change any other deployment option. **This document proposes the correction; no remediation has been implemented.**

The argument's presence and literal value are certain from source inspection. Its responsibility for this particular error is a high-confidence inference from the exact observed count and Microsoft's documented operation setting. No MSDeploy binary was executed or reverse-engineered, so this report does not claim knowledge of the precise internal instruction at which version `7.1.9419.0650` aborts, or proof of which remote side effects preceded it.

## Supplied observations

The user reports successful backend/integration tests, frontend tests, build/publish, production approval, artifact download and validation, and MSDeploy discovery. The discovered executable reports file version `7.1.9419.0650`. MSDeploy then reports that the maximum number of synchronization passes is zero, changes remain unapplied, and exits with -1. The user observes that production remains operational.

Those observations are accepted as supplied. Codex did not access GitHub Actions or production to reproduce or verify them. An operational website does not prove that its deployed bytes are unchanged or that the new artifact was deployed successfully.

## Exact source of zero

At the inspected revision, `scripts/ci/Deploy-SmarterAsp.ps1`, lines 182–187, contains:

```powershell
foreach ($argument in @(
    '-verb:sync', $source, $destination,
    '-enableRule:DoNotDeleteRule', '-enableRule:AppOffline',
    '-disableLink:AppPoolExtension', '-disableLink:ContentExtension',
    '-disableLink:CertificateExtension', '-retryAttempts:0'
)) { $start.ArgumentList.Add($argument) }
```

The offending argument is on line 186. It is a separate literal string appended directly to ProcessStartInfo.ArgumentList. It contains no variable expansion, numeric conversion, fallback expression, environment lookup, or provider-value quoting. Its PowerShell source quotes delimit the string; they are not part of the argument's value.

`ConvertTo-ProviderValue` is used only to construct source/destination provider values. It does not process `-retryAttempts:0`. `UseShellExecute=false` and direct `Process.Start()` mean no intermediary shell interprets the colon or substitutes zero. The foreach loop builds one process's argument list; it is not a deployment retry loop.

The workflow supplies only the artifact directory and four hosting configuration values to this step. It does not set a retry or sync-pass value, invoke MSBuild publishing, load a publish profile, or rebuild the application. No second sync-pass setting was found in the inspected deployment path. The zero is an explicit client override, not a missing GitHub variable or inferred host default.

## Microsoft syntax and operation meaning

Microsoft documents `DeploymentBaseOptions.RetryAttempts` as the count of attempts for a deployment operation, with a default of **5**. This is an operation-level setting; it should not be equated with a workflow wrapper that reruns MSDeploy after its process exits. [Microsoft RetryAttempts API reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.deployment.deploymentbaseoptions.retryattempts?view=iis-dotnet).

Microsoft's own Azure deployment task documents additional MSDeploy arguments in `-key:value` syntax and uses a positive `-retryAttempts:6` in its task defaults. This corroborates the colon syntax and positive setting; it does not mean VocabularyApp should adopt Azure's task-specific value or interval. [Microsoft Azure deployment task reference](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/azure-rm-web-app-deployment-v4?view=azure-pipelines).

Together with the observed error, these sources support this inference: **the native attempt setting also limits the synchronization passes available to this remote sync operation. Setting it to zero prevents normal synchronization from completing.** The native error reports the effective limit, not a count selected by PowerShell's exit-code handling. The error's suggestion about external destination changes is generic diagnostic wording, not evidence that SmarterASP or another publisher was modifying this site.

The available Microsoft references do not document every version-specific remote pass or precisely when a zero budget is checked. This analysis therefore does not prescribe `1` as a supposedly sufficient single attempt, assert an exact number of internal passes, or promise that the observed binary's complete runtime defaults were independently inspected. The documented default is a better basis than an invented minimal positive count.

## AppOffline, preservation, and endpoint responsibility

`AppOffline` controls offline handling for deployment; Microsoft's guidance identifies it as applicable to contentPath synchronization when addressing file locks. It does not supply the literal zero argument. There is no evidence here that removing it would fix the cause. Preserve it. [Microsoft Web Deploy file-lock/offline guidance](https://learn.microsoft.com/en-us/troubleshoot/developer/webapps/iis/deployment-migration/web-deploy-error-codes).

`DoNotDeleteRule` preserves destination-only content. Microsoft demonstrates using it with contentPath synchronization. It is not an attempt-count setting and should remain enabled. [Microsoft contentPath preservation example](https://devblogs.microsoft.com/dotnet/web-deploy-msdeploy-how-to-sync-a-folder/).

Offline handling and content rules can influence which operations occur during a deployment, but neither needs to malfunction to explain an explicit zero-pass budget. There is no evidence of a special interaction requiring either rule to be removed. Preservation also does not make deployment read-only: artifact-represented files can still be added or overwritten.

**Responsibility for the identified configuration defect is in our script.** The host need not be misconfigured to produce this error. The current evidence does not prove endpoint authentication, authorization, certificate trust, or destination mapping will all work after correction; an early native failure can conceal later problems. Do not change the endpoint, site, username, password, TLS policy, or hosting configuration based on this message.

## Production risk from the failed attempt

A zero allowed-pass count is consistent with an early abort and little or no content transfer. That is a plausible interpretation, not a verified no-change guarantee. The supplied output lacks a trustworthy completed-operation inventory, and no remote file comparison or server log inspection was authorized.

The failure occurred in a real `-verb:sync` invocation with AppOffline enabled, not in a dry run or transactional deployment. We cannot establish whether provider initialization, offline-file handling, or any destination write occurred before the error, nor whether cleanup ran. Partial modification cannot be ruled out. The online observation makes a persistent offline condition less likely at the time checked, but it does not establish file consistency or rollback.

Treat the attempt as **failed, with destination state unverified**. Do not claim successful deployment or automatic rollback. Before a later approved attempt, the user should retain the previous known-good artifact and, if required, manually inspect hosting/offline state and deployment records. Do not automatically delete files, restore a database, or redeploy during this analysis. No database operation is configured by this command.

## Minimal proposed remediation — not implemented

1. Remove only `'-retryAttempts:0'` from the argument array, leaving `'-disableLink:CertificateExtension'` as its final entry.
2. Explain in a nearby comment that native operation attempts must permit normal sync progress; complete deployment processes are not automatically rerun by this script. Correct the prior documentation's claim that zero is an appropriate way to disable native retries for this deployment.
3. Leave native retry interval/defaults alone. Do not add a PowerShell loop, a retry action, or repeated destructive recovery. Explicit `-retryAttempts:5` could document the published default, but omission is the smallest correction and avoids introducing a new tuning policy.
4. Preserve AppOffline, destination-only files, contentPath providers, credential transport and sanitization, TLS validation, artifact digest verification, production approval, and nonzero-exit failure handling. Continue deploying the existing artifact without rebuilding. No database or automatic rollback changes.

This recommendation restores a usable native operation budget; it is not a speculative attempt to mask an unknown failure with increasing retries. It does permit Web Deploy's normal bounded internal attempts, which must be distinguished from rerunning the entire workflow or executable after failure. The existing job timeout remains in place.

After a separately requested implementation and user-controlled merge, the next approved deployment should get past this zero-budget condition or expose a different actionable error. It is not guaranteed to succeed. GitHub must still run both test gates and artifact production before approval and deployment. That future run was neither triggered nor performed here.

## Files inspected and change boundary

- `scripts/ci/Deploy-SmarterAsp.ps1`: literal arguments, provider construction, process launch, stream capture, exit handling, and cleanup.
- `.github/workflows/backend-tests.yml`: dependencies, protected environment, artifact handoff/digest, variable mapping, and absence of a retry override.
- `docs/Updates/CI-CD-phase-3-controlled-deployment-implementation-results.md`: original command and the incorrect zero-retry rationale.
- `docs/Updates/CI-CD-phase-3-webdeploy-diagnostics-remediation.md`: diagnostic behavior and previously unresolved cause.

Only **this analysis document** was created. SHA-256 checks before and after document creation confirmed the four inspected files were unchanged. The earlier claim that no definite command bug was known is superseded by the newly supplied diagnostic and this analysis; the previous files remain untouched as requested.

**No remediation, tests, MSDeploy invocation, deployment, GitHub Actions trigger, production access, credential inspection, database configuration change, migration, commit, or push occurred.**
