# CI/CD Phase 3 Completion

Completion date: **September 12, 2026**

## 1. Executive Summary

**CI/CD Phase 3 is COMPLETE. Production validation succeeded end-to-end.** VocabularyApp now builds and validates its application artifact after mandatory automated tests, requires manual production approval, and deploys that exact artifact to SmarterASP.NET using Microsoft Web Deploy.

The final production workflow and application smoke test passed, as confirmed by the repository owner for this completion record. Repository inspection verified the current workflow architecture and script configuration. No new production access, deployment, or test execution was performed while preparing this document. No workflow run ID, commit SHA, or public URL was supplied for this record, so none is inferred.

## 2. Final Pipeline

The workflow is [`.github/workflows/backend-tests.yml`](../../.github/workflows/backend-tests.yml), displayed as **CI / Publish Artifact**.

```text
Push / merge to master
        |
        +--> Backend / Integration Tests --+
        |                                 |
        +--> Frontend Tests --------------+
                                          |
                              Build / Publish Artifact
                                          |
                               Immutable artifact upload
                                          |
                            production environment approval
                                          |
                                Exact artifact download
                                          |
                              Validate downloaded artifact
                                          |
                                  Microsoft Web Deploy
                                          |
                             SmarterASP.NET production site
```

The backend and frontend test jobs are independent gates; both must pass before `build` runs. The separate `deploy-production` job, displayed as **Deploy to SmarterASP.NET**, depends on successful `build` and the protected environment approval. **The deployment job does not rebuild the application.**

## 3. Backend Validation

The backend targets **.NET 8**. `VocabularyApp.WebApi.Tests` completed with:

- **172 passed**
- **0 failed**
- **0 skipped**

The test foundation does not access the production database. Its integration infrastructure replaces the production DbContext configuration with private in-memory SQLite under the Testing environment, and substitutes the external dictionary transport. These tests remain mandatory in GitHub Actions.

## 4. Frontend Validation

The frontend uses **Angular 18**. Frontend automated tests pass, and the production Angular build succeeds. CI uses Node 22 and runs the existing headless Chrome test command before artifact production.

The build stages the Angular browser output under the backend's `wwwroot`. The production artifact includes the Angular index, JavaScript, CSS, and supporting assets. The Angular-only web.config is excluded so the backend's SDK-generated IIS configuration governs the combined application.

## 5. Artifact Model

Deployment consumes the exact immutable artifact produced by the successful build in the same workflow run. Upload assigns an artifact ID exposed through the build job output; download uses that ID rather than selecting a latest or historical package. Digest mismatch fails the download step. The artifact is validated during publishing and checked again before deployment.

The artifact name is `VocabularyApp-SmarterASP-Publish-${{ github.sha }}-${{ github.run_attempt }}`, with configured retention of 30 days. Publishing is performed by [`Publish-VocabularyApp.ps1`](../../scripts/ci/Publish-VocabularyApp.ps1). Deployment never rebuilds or substitutes a newly generated package.

The final artifact is **portable, framework-dependent .NET 8**. CI clears RuntimeIdentifier, sets `UseAppHost=false`, and keeps self-contained publishing disabled. Project-level defaults remain unchanged outside this CI publish override. The artifact contains the WebApi DLL and shared-framework runtime configuration, with no WebApi apphost EXE.

The Web SDK naturally generates this hosting entry point:

```xml
<aspNetCore processPath="dotnet"
            arguments=".\VocabularyApp.WebApi.dll"
            hostingModel="inprocess" />
```

Validation enforces the intended IIS entry point, shared .NET 8 frameworks, RID-free dependency target, required application/browser files, and exclusion of development settings, source/test folders, node_modules, and prohibited sensitive files.

## 6. Production Approval Gate

The deployment job references the GitHub environment **`production`**. Manual approval is required, with the repository owner as reviewer, self-review allowed, and administrator bypass disabled under the confirmed environment configuration. Deployment eligibility is restricted to **master**, reinforced by the workflow's master-push condition.

Deployment credentials and target settings are held in GitHub production environment configuration:

| Configuration | Storage |
| --- | --- |
| `SMARTERASP_WEBDEPLOY_PASSWORD` | Environment secret |
| `SMARTERASP_WEBDEPLOY_URL` | Environment variable |
| `SMARTERASP_WEBDEPLOY_USERNAME` | Environment variable |
| `SMARTERASP_SITE_NAME` | Environment variable |

No credential values are included in this document. The protected environment is the authoritative approval mechanism.

## 7. SmarterASP.NET Deployment

[`Deploy-SmarterAsp.ps1`](../../scripts/ci/Deploy-SmarterAsp.ps1) discovers and validates Microsoft Web Deploy on the Windows runner, then synchronizes the validated publish directory using contentPath and Basic authentication over HTTPS.

TLS/certificate validation remains enabled. Diagnostic stdout/stderr is sanitized before logging, and a nonzero native exit code fails the deployment job. `DoNotDeleteRule` preserves destination-only files; `AppOffline` remains enabled for file-lock handling. There is no custom deployment retry loop or automatic rollback.

The production application pool remains **InProcess**. No automatic database migrations occur. Web Deploy transport protection is distinct from the public site's pending HTTPS configuration described below.

## 8. Incident 1 — MSDeploy Sync Pass Failure

The first controlled production deployment failed inside MSDeploy. Initial logging exposed only the generic exit code **-1**, which did not reveal the underlying cause. Diagnostic remediation added safe sanitized stdout/stderr messages while preserving failure handling and secret protection.

The subsequent detailed error identified a maximum synchronization-pass count of **0** being exceeded. The root cause was the explicit `-retryAttempts:0` argument, originally intended to disable whole-deployment retries but preventing normal native synchronization progress. Removing that option allowed normal bounded internal Web Deploy behavior without adding a custom retry loop. Subsequent Web Deploy completed successfully.

Related records:

- [Web Deploy diagnostics remediation](CI-CD-phase-3-webdeploy-diagnostics-remediation.md)
- [Sync-pass analysis](CI-CD-phase-3-msdeploy-sync-pass-analysis.md)
- [Sync-pass remediation](CI-CD-phase-3-msdeploy-sync-pass-remediation.md)

## 9. Incident 2 — HTTP 500.31 Runtime Failure

After Web Deploy succeeded, the application returned **HTTP Error 500.31 — Failed to load ASP.NET Core runtime**. The supplied SmarterASP pool configuration supported .NET 8 and was 64-bit/InProcess. The deployed runtimeconfig targeted `net8.0` with shared .NET 8 framework references.

Temporary stdout logging identified `Microsoft.NETCore.App`, version `8.0.0`, architecture x64. The reported .NET location pointed to the deployed application directory, followed by **“No frameworks were found.”**

The previous artifact used a **win-x64 framework-dependent apphost EXE**, and web.config selected that executable. The remediation removed RID-specific publishing behavior by clearing RuntimeIdentifier for CI restore/publish and setting `UseAppHost=false`. Generated web.config changed to **dotnet + DLL**, retaining InProcess hosting and framework-dependent deployment.

Production evidence supported removing the failing apphost entry path, and the corrected portable artifact was successfully validated in production. The exact host-side mechanism that selected the application directory as the runtime location was not fully proven. A framework-dependent apphost does not inherently require an app-local runtime, so the incident is not evidence that all such artifacts are invalid on IIS.

Related records:

- [.NET runtime-resolution analysis](CI-CD-phase-3-dotnet-runtime-resolution-analysis.md)
- [.NET runtime-resolution remediation and local validation](CI-CD-phase-3-dotnet-runtime-resolution-remediation.md)

## 10. Final Production Validation

The owner-confirmed final workflow run completed successfully:

| Job | Result |
| --- | --- |
| Backend / Integration Tests | Passed |
| Frontend Tests | Passed |
| Build / Publish Artifact | Passed |
| Deploy to SmarterASP.NET | Passed |

The **production smoke test passed**, covering:

- Login
- Navigation/dashboard
- Dictionary lookup
- New provider-backed lookup
- Add to vocabulary
- My Words
- Pronunciation/audio
- Angular route refresh
- Logout

These are completed manual production checks supplied for this record, not newly automated pipeline checks or checks rerun during document preparation.

## 11. Database Safety

**EF/database migrations are NOT run automatically by CI/CD.** No production database credentials or migration execution are required by the deployment pipeline. Production database changes remain a separate controlled operation. Application deployment does not authorize schema changes or automated database rollback.

## 12. Security and Operational Safeguards

- Production secrets are kept out of source control and deployment artifacts; deployment secrets use the protected GitHub environment, while application runtime secrets remain external hosting configuration.
- Deployment logs sanitize sensitive data and do not dump credential-bearing argument arrays or raw environment values.
- TLS/certificate validation is not disabled for Web Deploy.
- Manual production approval remains required, and deployment remains restricted to master.
- Workflow permissions remain `contents: read`.
- Current-run artifact identity, digest verification, and package checks protect the artifact handoff.
- Production deployment concurrency prevents overlapping sync jobs, with cancellation of an active deployment disabled. Reviewers still need to avoid approving superseded releases.
- Failures remain visible and fail closed; no automatic rollback, custom retry loop, or database migration is hidden in the workflow.

## 13. Remaining Work / Follow-Up

**Public-site HTTPS/SSL still needs configuration.** Production currently loads over HTTP, and the browser displays **“Not secure,”** as reported by the owner. This remains a follow-up despite successful Phase 3 deployment and smoke testing. The Web Deploy connection already uses HTTPS with certificate validation; that does not configure the public application's HTTPS binding.

Future improvements may include a formal rollback strategy beyond the existing manual recovery concept, health checks, automated post-deployment verification, release tagging, and further database migration governance. These are not claimed as implemented Phase 3 features. Existing artifact retention and manual recovery guidance should be considered when planning those improvements.

This completion record is documentation only. No production code, workflow, or CI/CD script was changed; no deployment, commit, or push was performed to create it.

## 14. Final Status

Completion date: **September 12, 2026**

CI/CD Phase 3: COMPLETE

Production deployment: VERIFIED

Production smoke test: PASSED
