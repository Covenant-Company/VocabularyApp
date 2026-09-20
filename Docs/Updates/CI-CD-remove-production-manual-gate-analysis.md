# CI/CD — Remove Production Manual Deployment Gate — Analysis

Date: 2026-09-20. Repository: `Covenant-Company/VocabularyApp`.

Scope: static analysis only. This requested document is the sole authored change; no workflow, script, application, GitHub setting, branch, commit, or push was changed. No tests, builds, deployment scripts, migrations, or production probes were executed. Public GitHub documentation was consulted for platform semantics; authenticated repository/environment settings were not inspected. Git status was blocked by the repository ownership check, including a command-scoped safe-directory attempt; no persistent Git configuration was changed and no clean-tree claim is made.

Evidence labels: **Proven** means directly visible in inspected local source; **Documented/reported** means project records or the supplied production context; **Verify in GitHub** means external state not established by this analysis. Line references describe the inspected files, not an independently verified remote `master` revision. Current source takes precedence over historical implementation notes.

## 1. Executive Summary

**Outcome A applies to the gate removal itself:** no workflow change appears necessary. Retain the `production` environment and remove its required-reviewer approval requirement in GitHub. If a custom protection rule also requires a human decision, that specific rule needs corresponding adjustment; preserve unrelated automatic protections.

**Proven:** `.github/workflows/backend-tests.yml` already triggers on pushes to `master`, requires both test jobs before building, and requires the successful build before deploying. It has no dispatch prerequisite, approval action, or interactive prompt. **Documented/reported:** production approval is configured outside YAML, in the GitHub environment. **Verify in GitHub:** the exact live reviewers and any additional protection rules.

The key safety caveat is release ordering: existing documentation explicitly relies on the reviewer to reject superseded runs. Deployment concurrency prevents overlap but does not ensure that an older successful build cannot overwrite a newer release. Automatic deployment also retains manual recovery requirements and incomplete functional production coverage. Outcome A enables automation; it does not resolve those existing operational limitations.

## 2. Current Deployment Architecture

The only discovered Actions workflow is [.github/workflows/backend-tests.yml](../../.github/workflows/backend-tests.yml), named `CI / Publish Artifact`.

| Element | Current implementation / evidence |
| --- | --- |
| Trigger | `push.branches: [master]`, lines 3–6; no `pull_request`, `workflow_dispatch`, schedule, or path filter |
| Permissions | `contents: read`, lines 8–9 |
| Backend | `backend-tests`, display name `Backend / Integration Tests`, lines 12–32; unfiltered Release test project |
| Frontend | `frontend-tests`, display name `Frontend Tests`, lines 34–53; npm install and headless Chrome tests |
| Build | `build`, display name `Build / Publish Artifact`, lines 55–124; .NET build, Angular production build, staging, publish validation, PSH-1 checks, upload |
| Production | `deploy-production`, display name `Deploy to SmarterASP.NET`, lines 126–175 |
| Runner / limits | All jobs use `windows-2022`; tests 20 minutes each, build 30, deployment 20; final acceptance step 5 |
| Environment | `production`; no `environment.url` in YAML |
| Public target | Acceptance script hardcodes `https://myvocabularybuilder.org`; this is distinct from the MSDeploy handler URL |
| Artifact | `VocabularyApp-SmarterASP-Publish-${{ github.sha }}-${{ github.run_attempt }}`; upload ID passed as a build output |
| Deployment input | Exact artifact ID from this workflow run; clean temporary staging; digest mismatch fails |
| Execution | Tested-commit scripts invoke MSDeploy, then production HTTPS acceptance |

Relevant YAML, lines 126–136:

```yaml
deploy-production:
  name: Deploy to SmarterASP.NET
  needs: build
  if: github.event_name == 'push' && github.ref == 'refs/heads/master'
  runs-on: windows-2022
  timeout-minutes: 20
  environment:
    name: production
  concurrency:
    group: vocabularyapp-production
    cancel-in-progress: false
```

Lines 138–143 check out deployment scripts at `github.sha`, with persisted credentials disabled. Lines 157–162 download by `needs.build.outputs.artifact-id` using `actions/download-artifact@v8` and `digest-mismatch: error`. Deployment does not rebuild or select a mutable “latest” artifact.

## 3. Current Production Gate

The effective flow is tests → build/package/upload → production environment eligibility and approval → artifact validation → MSDeploy → HTTPS acceptance. Approval blocks the deployment job before its steps, not merely the sync invocation.

[Phase 3 implementation results](CI-CD-phase-3-controlled-deployment-implementation-results.md), lines 33–39, record an external environment requiring the repository owner's approval, allowing self-review, disabling administrator bypass, and restricting deployment to `master`. That record explicitly accepts user-supplied settings rather than verifying them. The current request reports this approval is still active.

Strings such as “approved master-push” and “Starting approved production” in [Deploy-SmarterAsp.ps1](../../scripts/ci/Deploy-SmarterAsp.ps1), lines 6–8 and 197, are descriptive messages. The script checks Actions/push/master context, not an approval token or reviewer decision. They do not create a second gate.

## 4. Source of the Manual Approval Requirement

| Repository-controlled | GitHub-hosted configuration |
| --- | --- |
| Trigger, branch condition, `needs`, environment name, concurrency, scripts and acceptance checks | Required reviewers, self-review/bypass options, wait timer, custom deployment protection rules, environment branch policies, secret/variable placement |
| No workflow approval action, prompt, manual dispatch requirement, or custom approval flag found | Exact live settings require inspection |

Referencing `production` does **not inherently require approval**. It makes the job subject to whatever protections that environment has. Required reviewers impose the human gate; custom rules can impose additional conditions. A wait timer delays execution but is not itself human approval. [GitHub environment protection reference](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments).

Branch PR reviews/status checks govern acceptance of changes to `master`; they are separate from the reported post-build environment approval. No repository-visible evidence identifies branch rules as this deployment pause's source. Do not remove branch protections to solve it.

## 5. GitHub Environment Analysis

**Keep `environment: { name: production }`.** It preserves the environment association, deployment records/status, environment-scoped configuration, and branch-specific deployment controls. An environment URL can be associated with jobs, but none is currently specified in YAML. Approval removal does not require adding one. [GitHub environment configuration](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments) and [job environment syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idenvironment).

Environment secrets and variables are available to jobs referencing that environment. Removing the reference could remove required values or change which same-named values resolve; it is unnecessary and risky here. [GitHub environment secrets and variables](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments).

Do not delete/recreate the environment or move credentials merely to remove approval. The automatic target remains an environment-associated production deployment with its automated safeguards intact.

## 6. Production Secret / Variable Scope

The complete user-configured deployment inputs in workflow lines 165–170 are:

| Name | YAML source | Apparent scope / certainty |
| --- | --- | --- |
| `SMARTERASP_WEBDEPLOY_PASSWORD` | `secrets.SMARTERASP_WEBDEPLOY_PASSWORD` | Documented production environment secret; actual storage and any same-name repository/organization secret require GitHub verification |
| `SMARTERASP_WEBDEPLOY_URL` | `vars.SMARTERASP_WEBDEPLOY_URL` | Documented production environment variable; actual scope requires verification |
| `SMARTERASP_WEBDEPLOY_USERNAME` | `vars.SMARTERASP_WEBDEPLOY_USERNAME` | Documented production environment variable; actual scope requires verification |
| `SMARTERASP_SITE_NAME` | `vars.SMARTERASP_SITE_NAME` | Documented production environment variable; actual scope requires verification |

The `secrets` and `vars` expressions alone do not prove storage scope. The Phase 3 record's “GitHub production value” table and operator checklist identify the intended environment placement. Earlier planning documents naming additional secret-based inputs are superseded by current YAML.

Other workflow step environment values are internal plumbing: `PSH1_PUBLISH_DIRECTORY` from publish output, `PUBLISH_ARTIFACT_ID` from build output, and `DEPLOY_DIRECTORY` from staging output. Scripts also use runner-provided `GITHUB_ACTIONS`, `GITHUB_EVENT_NAME`, `GITHUB_REF`, `RUNNER_TEMP`, `RUNNER_DEBUG`, `GITHUB_WORKSPACE`, `GITHUB_OUTPUT`, and program installation paths. These are not additional repository deployment credentials.

Production runtime configuration is separate: SQL connection, JWT signing key and WordsAPI key are not passed as workflow secrets. External hosting configuration is expected to supply runtime values such as `ConnectionStrings__DefaultConnection`, `JwtSettings__SecretKey`, and `WordsApi__ApiKey`; their configuration keys are established by `Program.cs` and `VocabularyApp.WebApi/Configuration/JwtSettings.cs`, while actual host values were not inspected. Packaging rejects embedded IIS environment variables/connection strings and pins reviewed non-secret `appsettings.json` content. No secret values were requested or exposed.

If the environment reference were removed, environment-only credentials/configuration would become unavailable. Empty/invalid inputs fail the deployment script's validation. Same-named values at another scope could instead select unintended configuration. Retaining the reference avoids both problems.

## 7. CI Dependency and Gate Chain

```text
push / merge resulting in a push to master
  ├─ backend-tests: restore + all backend/integration tests
  └─ frontend-tests: npm ci + headless Chrome tests
          both succeed
               ↓
build: .NET Release + Angular production build
  → stage browser assets
  → offline PSH-1 script checks
  → publish + package validation
  → actual published HTTPS policy/mutation checks
  → upload artifact, expose artifact ID
               ↓
deploy-production: successful build + push/master condition
  → production environment protections
  → serialized job; tested-commit scripts
  → exact artifact download + digest/layout validation
  → MSDeploy file synchronization
  → live HTTPS acceptance
```

The test jobs run in parallel; frontend tests do not depend on backend tests. `build.needs: [backend-tests, frontend-tests]` at line 57 requires both. `deploy-production.needs: build` at line 128 is sufficient transitively. There is no `always()` or `continue-on-error` bypass; the branch/event expression does not override normal dependency success requirements. Failed/skipped prerequisite jobs prevent dependent deployment. [GitHub job dependency syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds).

No dependency gap was found for the existing required jobs. Gaps are coverage/operations: no PR-triggered run in this workflow, no authenticated production database smoke check, no live HEAD probe, no release-order check, and no automatic recovery. Those are distinct from failure propagation.

## 8. Deployment Safety Controls

| Failure/risk | Existing automatic control | Practical limit |
| --- | --- | --- |
| Backend/integration test failure | Blocks build and deployment | SQLite integration tests do not validate production SQL Server migrations |
| Frontend test failure | Blocks build and deployment | Headless tests do not certify every live browser/user journey |
| Build failure | .NET/Angular failures stop build | Host runtime/configuration may still differ |
| Packaging failure | Publish exit checks; required outputs; settings digest; asset hashes; runtime/IIS validation | Validation covers the defined contract, not arbitrary defects |
| Missing/corrupt artifact | Upload requires files; artifact ID validation; exact-run download; digest mismatch error; repeated layout checks | Not a complete software supply-chain attestation |
| Invalid deployment inputs/tooling | HTTPS handler/site validation; credential presence/quoting; Microsoft Authenticode verification; debug rejection | Valid inputs can still refer to an unintended configured site |
| MSDeploy failure | Direct process exit-code capture; throw on nonzero/start/capture failure; sanitized diagnostics | Files may already be partially changed; no transactional restoration |
| Site startup/transport failure | Bounded readiness retries and live page/API/asset checks | Detection follows deployment; users may already experience downtime |
| HTTPS enforcement regression | PSH-1 package checks plus live redirect, rejection, CORS and HSTS assertions | Limited routes/methods; live HEAD is not checked |
| Acceptance failure | Throw fails deployment job | No automatic rollback or reopening/repair procedure |

Sources: workflow lines 86–175; [Publish-VocabularyApp.ps1](../../scripts/ci/Publish-VocabularyApp.ps1), lines 16–130; [Deploy-SmarterAsp.ps1](../../scripts/ci/Deploy-SmarterAsp.ps1), lines 13–84, 98–114, 179–229; [Test-ProductionHttps.ps1](../../scripts/ci/Test-ProductionHttps.ps1), lines 87–180.

Live acceptance checks HTTPS `/login`, HTTP navigation redirects preserving encoded paths/query strings, bounded redirect traversal, rejection of HTTP API requests and empty unsafe POSTs, anonymous HTTPS API 401/Bearer responses, rejection of specified cross-origin access, and one emitted same-origin JS asset. It requires HSTS exactly `max-age=300` without extra directives on checked HTTPS responses. It uses normal TLS validation, disables automatic redirects, and bounds response size/time. Authenticated login, database access, dictionary-provider availability and quiz/history operations are not exercised. Its success message explicitly retains manual authenticated smoke verification.

Human approval currently provides an opportunity to assess release timing, schema compatibility, rollback artifact availability, superseded runs and application readiness. It is not evidence that these checks were performed, and it cannot substitute for post-deployment health verification.

## 9. Database / Migration Behavior

**Proven:** this pipeline deploys application files through `contentPath` → `contentPath`. It contains no `dotnet ef database update`, migration bundle execution, SQL script invocation, database deployment provider, or production database credential mapping. `.sql` and `.ps1` files are forbidden in the application artifact.

[Program.cs](../../VocabularyApp.WebApi/Program.cs), lines 24–26, registers SQL Server. Startup contains no `Migrate`, `MigrateAsync`, `EnsureCreated`, or database seeding invocation; no migration-running hosted service was found. Inspected project files contain no custom migration build target. EF migrations exist in `VocabularyApp.Data/Migrations`, but packaging their compiled assembly does not execute them. `ApplicationDbContext.HasData` defines model seed data, not an independently executed startup seed operation.

The test factory uses its private SQLite database and calls `EnsureCreated` there (`VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs`, lines 88 and 108). That is not production schema management.

Removing environment approval introduces no automatic migration behavior. Normal application requests can still write production data after restart. Future code requiring a newer schema may deploy successfully but fail at runtime; the anonymous 401 acceptance probe does not establish SQL connectivity or schema compatibility. Coordinate any schema-changing release separately before it reaches automatically deployed `master`.

## 10. Rollback and Recovery State

| Question | Current finding |
| --- | --- |
| Previous artifact retained? | Every successful upload requests 30-day retention (workflow line 123), regardless of whether deployment succeeded. No dedicated “previous successful production” archive or indefinite retention is implemented; actual availability is unverified. |
| Can retained artifacts be redeployed? | Exact retained bytes can support manual hosting recovery. This workflow has no historical-artifact selector or dedicated rollback dispatch. Rerunning an old build can rebuild bytes and is not equivalent to restoring the original artifact; rerunning an old deployment is not a documented recovery mechanism. |
| MSDeploy rollback? | No configured backup/restore command, snapshot, slot switch, rollback provider, or application rollback automation. `DoNotDeleteRule` preserves destination-only files, not overwritten versions. |
| AppOffline after failure? | `-enableRule:AppOffline` delegates offline handling to MSDeploy. Failure/cancellation/timeout may leave the site offline or partially synchronized. |
| Guaranteed removal? | No. The script's `finally` only disposes the process and clears argument/credential references, lines 224–229. There is no remote file cleanup step or `if: always()` finalizer. |
| Recovery owner? | Operator, using sanitized diagnostics, hosting access and a known-good schema-compatible artifact. |

[Phase 3 implementation results](CI-CD-phase-3-controlled-deployment-implementation-results.md), “Synchronization, file locks, and failures” and “Database exclusion and manual recovery,” instruct retaining known-good bytes, inspecting partial/stale files and offline state, preserving host-owned content, and manually restoring compatible application files. [R5 production deployment](R5-production-deployment.md), section 15, documents manual application/schema recovery and requires verification before removing AppOffline/reopening traffic. Its database reversal instructions concern that historical migration, not a generic automatic-deployment rollback action.

Historical Phase 3 statements about `retryAttempts:0`, absence of smoke checks, and restricted diagnostic output are superseded by current scripts, PSH-1 acceptance and the later [sync-pass remediation](CI-CD-phase-3-msdeploy-sync-pass-remediation.md) / [diagnostics remediation](CI-CD-phase-3-webdeploy-diagnostics-remediation.md). Current code invokes one MSDeploy process without an explicit retry override; native operation attempts are distinct from workflow retries.

Without approval, “stop newer approvals” is no longer an incident containment mechanism. An operator must actively stop new deployments before recovery; a failed run does not lock out subsequent successful runs.

## 11. Master Branch Protection / Ruleset Findings

**Proven:** this workflow runs only after a push to `master`. It has no PR or merge-queue trigger. No branch-protection/ruleset configuration or CODEOWNERS file was discovered in the inspected repository files. CI execution on `master` does not prove required PRs, required status checks, review counts, push restrictions, force-push restrictions, or bypass policies.

**Verify in GitHub:** Settings → Rules → Rulesets, Settings → Branches where applicable, and any inherited organization rules. Establish who can push/merge, who can bypass rules, and whether required checks actually run before merge. Do not simply mark these push-only jobs as pre-merge requirements without ensuring a trigger supplies the checks. Changes to pre-merge validation, if desired, are separate work.

Direct pushes currently match the same deployment trigger as merged PRs. After approval removal, permission to update `master` effectively becomes permission to initiate production deployment, subject to successful CI and remaining environment restrictions.

## 12. Risks of Automatic Production Deployment

1. **Outdated release overwrite:** Phase 3 line 39 explicitly delegates rejection of superseded runs to the reviewer. The current scripts perform no current-head/deployed-version comparison. If older commit A builds slowly and newer B deploys first, A can deploy afterward. An old run rerun creates the same concern. GitHub concurrency serializes access, but waiting order is not commit order; the default pending slot can also replace waiting jobs. [GitHub concurrency behavior](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency).
2. **In-place deployment and unattended recovery:** a failed sync may leave partial files/AppOffline; failed acceptance does not restore the prior application. An operator must be available to respond.
3. **Schema/configuration compatibility:** tests and transport probes cannot prove the new application works against the live database/external configuration.
4. **Loss of release scheduling checkpoint:** any eligible push, including documentation-only changes because there is no path filter, can cause a deployment/restart after CI succeeds.
5. **Unverified branch controls:** removing the final human checkpoint increases reliance on who can update `master` and modify its credential-bearing workflow/scripts.
6. **Recovery artifact expiry and stale destination files:** 30-day artifact retention is not durable rollback storage; destination-only DLLs/assets/settings can survive because deletion is disabled.

These are operational limits, not reasons to weaken tests or remove the environment. The release-order concern is especially material because the proposed change removes its documented human mitigation. Do not describe the unchanged pipeline as guaranteeing newest-release-only deployment or safe unattended recovery.

## 13. Minimum Required Change

**Outcome A — configuration-only gate removal.** Disable required reviewers for the existing `production` environment. Inspect additional protection rules and adjust only any human-approval requirement that would still block automatic execution. Leave automated checks and branch restrictions intact.

Outcome B is not evidenced as necessary to make deployment automatic. A separately scoped stale-release safeguard may be warranted to replace the documented reviewer responsibility, but it is a safety follow-up, not an intrinsic GitHub approval-removal requirement. Outcome C is not supported: existing environment credentials/configuration are a reason to retain the environment.

Before implementing Outcome A, verify the actual live gate, branch controls, recovery artifact and incident ownership. Explicitly resolve or accept the release-order limitation. If the intended safety requirement is that an older run must never replace a newer production release, configuration-only removal is insufficient to satisfy that stronger requirement.

## 14. Recommended Target Architecture

Keep the existing chain: master push → parallel backend/frontend tests → successful build/package/security validation → exact artifact → `production` environment with no human approval requirement → serialized MSDeploy → mandatory HTTPS acceptance.

Preserve branch/event restrictions, `needs`, digest validation, tested-commit scripts, AppOffline, destination preservation, TLS validation, sanitized diagnostics, failure propagation and acceptance checks. Keep database migrations separately controlled. Keep authenticated smoke checks and recovery ownership explicit; they are not automated by this change.

Treat release-order hardening as a separately reviewed decision if unattended deployment must replace the reviewer's stale-run screening. This analysis does not redesign concurrency or rollback.

## 15. GitHub UI Changes Required

Future implementation only; none performed:

1. Open `Covenant-Company/VocabularyApp` → Settings → Environments → `production`. Record existing reviewer, self-review, bypass, timer, custom-rule and deployment-branch settings without copying secret values.
2. Inspect pending/running deployments and identify superseded runs before changing the gate. Do not assume existing queued runs will be unaffected by a settings change.
3. Disable **Required reviewers** and save the environment's protection settings. Do not delete the environment.
4. If an enabled custom protection rule demands human approval, adjust that particular requirement. Preserve automatic rules. A timer can remain if automatic-but-delayed deployment is acceptable; removing it is not required merely to eliminate human approval.
5. Preserve `master` deployment restrictions and all environment secrets/variables. Verify the three variable names and password-secret name remain present at the intended scope.

GitHub documents these controls under [Managing environments for deployment](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments). Branch protections are verification items here, not settings to remove.

## 16. Repository Changes Required

No YAML or deployment-script change is required for the narrow approval removal. In particular, do not remove `environment`, add `always()`, weaken `needs`, skip tests, suppress MSDeploy exit failures, or bypass HTTPS acceptance.

After a separately authorized settings change, update the operational instructions that currently say to approve deployments and stop newer approvals during recovery. Descriptive “approved” log wording could also be clarified, but neither documentation nor wording changes enable the automatic execution technically. This analysis leaves them untouched.

## 17. Verification Plan for a Future Implementation

Not executed in this task:

1. Confirm remote `master` contains the inspected workflow/scripts; inspect the live environment protections, configuration scopes, branch rules and recent approval-waiting job.
2. Record the pre-change settings, identify the current production SHA and retained known-good artifact, confirm schema compatibility and operator recovery access, and clear superseded pending runs through an authorized operational action.
3. Make only the environment approval change. Use the next authorized, low-risk master update to exercise the pipeline; avoid an unnecessary empty deployment trigger.
4. Observe both test jobs succeed, then build/package/PSH-1 checks/upload succeed. Confirm production starts without a review click and still targets the `production` environment.
5. Confirm artifact ID/SHA linkage, digest/layout validation, MSDeploy exit zero and successful final HTTPS acceptance; record the deployment result and perform documented authenticated smoke checks securely.
6. Verify negative test/build/packaging/download failure behavior through existing run evidence or isolated nonproduction validation. Do not deliberately corrupt production artifacts, fail live syncs, or disable protections to test propagation.
7. Evaluate overlapping commits and historical reruns in a nonproduction scenario if a release-order policy is required. Verify that policy separately; one successful sequential deployment does not prove ordering safety.
8. Confirm no migration/database deployment step was added and no deployment-scoped inputs were lost. Monitor the first automatic production runs with a recovery owner available.

## 18. Rollback Plan for the Gate Change

Restore the recorded required reviewers and associated self-review/bypass settings on the same `production` environment; restore any specifically changed custom approval rule. Preserve credentials, variables and branch restrictions. With no workflow change, no code revert is needed to restore the approval policy.

Reinstating reviewers is not an application rollback and does not reliably stop a deployment whose steps have already begun. Inspect active/pending runs before relying on the restored gate. Interrupting a sync can itself leave partial/offline state; use the existing incident recovery procedure where deployment has started. Contain subsequent deployments, recover known-good application bytes if needed, and verify health separately.

## 19. Open Questions / Items Requiring GitHub UI Verification

- Which exact required reviewers and custom deployment protection rules are currently active? Is a wait timer also configured?
- Are self-review/bypass and deployment-branch restrictions still as recorded? Does the live environment allow only the intended `master` branch?
- Are the three variables and password secret environment-scoped, and do same-named repository/organization values exist?
- What PR/status-check/push/bypass restrictions actually apply to `master`, including inherited organization rules? What supplies pre-merge checks?
- Does remote `master` match the inspected local source? Which SHA/artifact is currently live?
- Are there already approved, waiting, or superseded runs that could deploy after the change?
- Is an exact previous production artifact still available, and is a recovery copy retained beyond the configured 30 days?
- Who responds to failed automatic deployment/acceptance, and how will subsequent deployments be contained?
- Will the release-order limitation be explicitly accepted under an operating policy, or addressed in separately authorized work before unattended deployment?
- Does the host have any independently configured deployment/database hooks or backup features? None are established by repository evidence.

No live settings, current artifact inventory, hosting state or current database schema were certified by this analysis. Reported successful production operation and PSH-1 completion were not re-tested.

## 20. Final Recommendation

Retain the `production` environment and all existing automated quality/security gates. The reported manual approval comes from external GitHub environment protections, not from the environment name itself. The smallest technical change is Outcome A: remove required reviewers, and only any additional human-approval protection if actually present, in GitHub settings.

Proceed with that future settings change only after the live configuration and recovery readiness are verified and the release-order risk is resolved or explicitly accepted. No automatic migrations, transactional deployment, automatic rollback, or complete functional production verification exists in the inspected path. Removing approval changes who decides when a passing release goes live; it does not add those capabilities.

Analysis complete. Implementation intentionally not performed.
