# Phase 3 MSDeploy sync-pass remediation

## Failure and minimal correction

The approved deployment failed with MSDeploy reporting a maximum synchronization-pass count of zero and exit code -1. The [preceding analysis](CI-CD-phase-3-msdeploy-sync-pass-analysis.md) identified the explicit `-retryAttempts:0` argument as the cause with high confidence. It incorrectly attempted to disable whole-deployment retries through a native operation setting that prevented normal synchronization progress.

Removed only `'-retryAttempts:0'` and its preceding comma from the MSDeploy argument array in `scripts/ci/Deploy-SmarterAsp.ps1`. The final argument is now `'-disableLink:CertificateExtension'`. No replacement retryAttempts value or retry interval was added: Web Deploy can use its normal/default bounded internal behavior without introducing an arbitrary tuning value.

The script still invokes MSDeploy once per approved deployment job. No PowerShell retry loop, GitHub step retry, repeated process invocation, continue-on-error, or automatic rollback was introduced. Native internal attempts are distinct from rerunning an entire failed deployment.

This document supersedes the earlier Phase 3 implementation report's description of zero native retries. Earlier analysis and implementation records remain unchanged as historical documents.

## Preserved behavior

- The workflow file is unchanged: backend/integration and frontend test gates, build/publish gate, production environment approval, and master-only deployment eligibility remain intact.
- Exact current-run artifact handoff, digest verification, and package validation remain intact. Deployment does not rebuild the application.
- `AppOffline` handling and `DoNotDeleteRule` destination-only file preservation remain enabled. All provider arguments, extension-link settings, and quoting remain unchanged.
- Basic authentication over HTTPS and certificate validation remain unchanged. No certificate bypass, credential change, or environment configuration change was made.
- Secret sanitization, concurrent stdout/stderr capture, sanitized failure diagnostics, direct native exit-code capture, and failure on any nonzero exit code remain unchanged.
- No database credentials, database configuration changes, EF Core migrations, schema operations, or seeding were added.

## Files and static validation

Modified `scripts/ci/Deploy-SmarterAsp.ps1`. Created `docs/Updates/CI-CD-phase-3-msdeploy-sync-pass-remediation.md`. No other files were edited.

PowerShell parser inspection passed without executing the script. Before/after source comparison confirmed the only script change is removal of the offending argument and comma. No retryAttempts override remains in the script. The workflow SHA-256 remained unchanged, confirming that this remediation did not alter the CI gates or artifact configuration.

## Expected next controlled deployment

After the user merges the reviewed remediation into master, GitHub must still pass both test jobs and Build / Publish Artifact, then wait for production approval. The approved job downloads and validates the exact artifact and invokes MSDeploy once, allowing normal bounded native operation attempts.

The explicit zero-pass failure should no longer occur. This is not a guarantee of deployment success: any subsequent endpoint, certificate, permission, file-lock, or other error must still produce sanitized diagnostics and fail the job. The user should review those diagnostics and manually verify production after deployment. No production verification or retry was performed during this implementation.

**CODEX RAN NO TESTS. No deployment, MSDeploy invocation, production access, GitHub Actions trigger, migration, commit, or push occurred. Stop after this minimal remediation.**
