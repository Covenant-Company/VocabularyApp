# CI/CD — Remove Production Manual Deployment Gate — Implementation Plan

Date: 2026-09-20. Repository: `Covenant-Company/VocabularyApp`. Release branch: `master`.

Plan remediation only. This existing plan is the only file modified in this task. No workflow, script, other documentation, GitHub configuration, branch, commit or push was changed. No tests, builds, deployment scripts, migrations or live production checks were run.

Authoritative analysis: [CI-CD-remove-production-manual-gate-analysis.md](CI-CD-remove-production-manual-gate-analysis.md). Its conclusions were checked against the current local workflow, deployment/publish/acceptance scripts, application startup and deployment records. The user's new manually inspected GitHub evidence is VERIFIED planning evidence and supersedes this plan's earlier unknown-setting findings. The user's revised requirement supersedes the earlier decision to defer stale-run protection. Historical production completion records establish prior success, not current health; remote code and current health/recovery still require implementation-time checks.

Action categories used below: **GitHub UI configuration**, **source-controlled repository change**, **verification only**, **documentation**, and **deferred follow-up**. All implementation actions described are future actions, not actions performed in this planning session.

## 1. Executive Summary

Remove the required human approval from the existing GitHub `production` environment while keeping that environment and every automated CI/deployment gate. The current YAML already deploys a successful master-push artifact automatically once environment protections permit it.

The reviewer setting itself is an external GitHub configuration change. **The revised implementation requires source-controlled deployment queue and stale-commit protection BEFORE that setting is changed.** Select Option D: retain the production job's non-cancelling serialization, retain waiting jobs with `queue: max`, and check the candidate against live `master` immediately before MSDeploy starts. Do not cancel an active deployment on a new push.

| Decision | Classification | Planned treatment |
| --- | --- | --- |
| Required reviewer gate | GitHub UI configuration | Disable only the confirmed manual approval requirement on the existing environment |
| Environment, configuration, CI gates | Verification only | Preserve and compare before/after |
| Deployment queue and stale-run protection | **REQUIRED BEFORE MANUAL GATE REMOVAL** | Implement and validate Option D in section 10 while reviewers remain enabled |
| Automated authenticated/database smoke | **DEFERRED / NOT REQUIRED FOR THIS CHANGE** | No new automation/accounts/secrets; manual rollout smoke remains |
| Workflow/deployment guard | Source-controlled repository change | Narrow queue, guard, token wiring and offline validation changes; no application changes |
| Branch protection/rulesets | Deferred follow-up | VERIFIED NONE; do not add protections in this implementation |
| Current operational instructions | Documentation | Update only after verified automatic deployment |

**Design status: RESOLVED / READY for the future source-controlled implementation phase. Gate removal: NO-GO until that safeguard is validated on `master` and current production health/recovery are established.** Environment identity, reviewer source, configuration scope, master-only environment restriction and absence of branch protections are now verified. This readiness statement does not authorize execution in this planning task.

## 2. Objective

For a normal eligible push/merge to `master`, both existing test jobs must pass, then build/package/upload must pass, then production deployment must start without a human approval prompt. Existing MSDeploy failure handling and post-deployment HTTPS acceptance remain mandatory.

The actual test jobs run in parallel. The desired behavior does not require making them sequential. A failed required test or build must continue to prevent any production synchronization for that run.

## 3. Current-State Baseline

**Repository-proven** baseline in [.github/workflows/backend-tests.yml](../../.github/workflows/backend-tests.yml):

| Element | Exact current behavior / lines |
| --- | --- |
| Workflow | `CI / Publish Artifact`; only discovered workflow |
| Trigger | `push` to `master`, lines 3–6; no PR, dispatch, schedule or path filter |
| Backend | `backend-tests` / `Backend / Integration Tests`, lines 12–32; `dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release --no-restore` |
| Frontend | `frontend-tests` / `Frontend Tests`, lines 34–53; `npm ci`, then `npm test -- --watch=false --browsers=ChromeHeadless` |
| Build | `build` / `Build / Publish Artifact`; `needs: [backend-tests, frontend-tests]`, lines 55–57 |
| Production | `deploy-production` / `Deploy to SmarterASP.NET`; `needs: build`, lines 126–128 |
| Eligibility | `if: github.event_name == 'push' && github.ref == 'refs/heads/master'`, line 129 |
| Environment | `environment: { name: production }`, lines 132–133; no environment URL declared |
| Concurrency | No workflow-level concurrency. Job-level `group: vocabularyapp-production`, `cancel-in-progress: false`, lines 134–136; no `queue` property, so default pending replacement applies. No reusable workflow. |
| Permissions | `contents: read`, lines 8–9 |
| Runtime | `windows-2022`; test/deploy job timeouts 20 minutes, build 30; acceptance step 5 |

Build lines 75–115 restore/build .NET, build Angular production assets, copy `VocabularyApp.UI/dist/vocabulary-app.ui/browser` into API `wwwroot`, execute offline PSH-1 checks, invoke `scripts/ci/Publish-VocabularyApp.ps1`, and check the actual published HTTPS policy. Publish creates a unique `RUNNER_TEMP/VocabularyApp-publish-<guid>` directory, validates outputs/settings/runtime/assets and exposes `publish-directory` only on success.

Upload lines 116–124 publish that directory as `VocabularyApp-SmarterASP-Publish-${{ github.sha }}-${{ github.run_attempt }}` with `if-no-files-found: error` and 30-day retention. The numeric upload artifact ID is a build output. Deployment checks out `scripts/ci` at `github.sha`, prepares a unique `RUNNER_TEMP/VocabularyApp-deploy-<guid>` directory, and downloads that exact ID from the same run with `digest-mismatch: error` (lines 138–162).

Line 171 runs [Deploy-SmarterAsp.ps1](../../scripts/ci/Deploy-SmarterAsp.ps1), which validates the downloaded package and installed Microsoft executable, then launches one MSDeploy process with `-verb:sync`, source/destination `contentPath`, `DoNotDeleteRule`, and `AppOffline`. Lines 172–175 then run [Test-ProductionHttps.ps1](../../scripts/ci/Test-ProductionHttps.ps1) against `myvocabularybuilder.org`.

**VERIFIED by the user's GitHub inspection:** `production` has Required reviewers enabled for the repository owner/user, Prevent self-review disabled, Wait timer disabled, no custom protection rule observed, and administrator bypass not enabled. Selected branches and tags permits `master`. The four deployment inputs in section 8 are environment-scoped. No repository rulesets or classic branch protection are configured. The manual gate's source is therefore confirmed, not conjectural. Historical Phase 3 records corroborate the environment mechanism; they are no longer the only evidence.

## 4. Scope

| Item | Category | In scope? |
| --- | --- | --- |
| Inspect then remove required human production approval | GitHub UI configuration | Yes, in future implementation |
| Preserve environment, secret/variable scope, deployment history and unrelated protections | Verification only | Yes |
| Preserve tests, build, package/download validation, MSDeploy and HTTPS gates | Verification only | Yes |
| Observe a normal automatic master deployment and manual functional smoke | Verification only | Yes |
| Record evidence and update current operating instructions | Documentation | Yes, after successful verification |
| Non-cancelling deployment queue, immediate stale-commit guard and focused validation | Source-controlled repository change | Required before reviewers are disabled |
| New authenticated smoke automation | Deferred follow-up | No new test account or credentials |
| Migrations, SQL execution, automatic database rollback | Source-controlled repository change | Out of scope |
| CI/CD redesign, hosting migration, deployment architecture changes | Source-controlled repository change | Out of scope |
| Application behavior, PSH/HSTS policy changes, broader security work | Source-controlled repository change | Out of scope |
| Branch-rule changes, new PR triggers or secret relocation | GitHub UI configuration / source-controlled repository change | Verification only here; remediation needs separate scope if a blocker is found |

## 5. Preconditions

Verified settings do not need to be rediscovered as unknowns. Before implementation, make a brief drift check and retain their non-secret snapshot. Before saving the reviewer change, require the following **verification-only** evidence:

1. Correct repository and existing `production` environment identified; operator has permission to edit its protections and restore them.
2. Verified baseline remains: owner/user required reviewer, self-review allowed, no timer/custom rule observed, no administrator bypass, Selected branches and tags limited to `master`. Record the exact owner login for rollback without requesting credentials.
3. Required variable/secret names and scopes confirmed; any same-named repository/organization entries identified without copying credentials.
4. Remote `master` workflow/scripts match the relevant inspected baseline, including both test dependencies and absence of automatic migrations.
5. VERIFIED NONE for repository rulesets and classic branch protection is recorded, including the consequence that direct master pushes can release after CI. Hardening remains separate work.
6. Current production HTTPS and authenticated functionality are healthy; no unresolved deployment incident or schema incompatibility is present.
7. Current live SHA/run/artifact is identified, an exact known-good recovery artifact is accessible, and a named operator has hosting access and time to observe the first automatic deployment.
8. No active sync, previously approved deployment, approval-waiting deployment or older in-flight CI run can unexpectedly release during the settings change. Let an active deployment finish; cancel obsolete pre-deployment runs only through an authorized operational action and confirm terminal status.
9. Required queue/guard changes and focused offline validation are committed/merged to `master`; hosted configuration validation and an approved protected deployment of the safeguard revision pass. Stale-run protection cannot be waived as a deferral.
10. All pre-safeguard in-flight/queued runs are drained or canceled before removing reviewers; old run reruns must not be used as a release/recovery path. Re-running historical workflows uses historical code, so a new guard cannot retrofit them.
11. Automated authenticated smoke remains deferred; an existing-account manual rollout check is available. No new production account or credential is required.

Unknown or contradictory evidence means **NO-GO**, not permission to delete the environment or bypass checks.

## 6. GitHub Environment Changes

**Category: GitHub UI configuration.** Expected navigation:

```text
Covenant-Company/VocabularyApp
→ Settings
→ Environments
→ production
→ Deployment protection rules
```

Confirmed before-state: **Required reviewers enabled**, owner/user reviewer, **Prevent self-review disabled**, **Wait timer disabled**, **no custom rule observed**, **administrator bypass not enabled**, and **Selected branches and tags: master**. Preserve all of this except the required reviewer gate. Perform a drift check rather than treating the source of approval as unresolved.

**Only after the source safeguard is present and verified on master**, deselect **Required reviewers**, then select **Save protection rules**. Reopen the page and confirm the change persisted. Keep `production`, its history, master restriction and all inputs. [GitHub's environment settings documentation](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments).

Decision path for unexpected configuration:

- If reviewers are unexpectedly already disabled before the safeguard is installed, stop the rollout and restore the confirmed manual boundary through an authorized action; do not assume concurrency protection already exists.
- If a new custom rule or timer is now present, that is drift from verified evidence. Stop and reconcile it; this plan does not authorize deleting unrelated rules.
- Preserve branch restrictions and bypass posture. Never delete/recreate `production`, move secrets, or loosen branch protections to eliminate an approval prompt.

## 7. Repository Changes

**Category: source-controlled repository change. Required before gate removal:**

| Future file change | Exact purpose |
| --- | --- |
| `.github/workflows/backend-tests.yml` | Add `queue: max` to the existing production-job concurrency group; retain `cancel-in-progress: false`; pass the built-in `github.token` as step-scoped `RELEASE_GITHUB_TOKEN`; invoke new offline guard checks in `build` before upload |
| `scripts/ci/Assert-ProductionReleaseCandidate.ps1` (new) | Dot-sourceable assertion with injectable read-only request seam; validate Actions/master context and compare immutable candidate SHA with live master immediately before sync |
| `scripts/ci/Deploy-SmarterAsp.ps1` | Call assertion at the last pre-process-start boundary inside the existing cleanup-protected region; prevent the child process from inheriting the guard token; preserve existing native sync and cleanup behavior |
| `scripts/ci/tests/Test-ProductionReleaseCandidate.ps1` (new) | Offline deterministic fixtures and orchestration checks described in section 16; no production/API/MSDeploy calls |

No new GitHub permissions beyond existing `contents: read`, user-managed credentials, action dependency, application feature or migration is selected. The automatic `github.token` is not a newly stored environment secret.

The workflow contains no manual dispatch requirement, custom approval action, shell prompt or approval flag. The deployment script's “approved” messages are descriptive strings, not additional checks. Leave these strings alone in this narrowly scoped implementation.

Documentation edits in section 19 follow successful verification. If actual hosted GitHub rejects the planned `queue: max` property, do not silently revert to default single-pending concurrency: that would invalidate the queue guarantee. Keep reviewers enabled and resolve the compatibility issue before proceeding.

## 8. Production Environment / Secret Preservation

Retain exactly:

```yaml
environment:
  name: production
```

This preserves deployment association/status/history, environment-scoped inputs and remaining environment controls. A public environment URL could be configured separately, but adding one is unnecessary here. Environment-scoped inputs depend on the environment reference. [GitHub environment behavior](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments).

| Required input | Current YAML source, lines 167–170 | Scope evidence / future verification |
| --- | --- | --- |
| `SMARTERASP_WEBDEPLOY_URL` | `vars.SMARTERASP_WEBDEPLOY_URL` | VERIFIED production environment variable |
| `SMARTERASP_WEBDEPLOY_USERNAME` | `vars.SMARTERASP_WEBDEPLOY_USERNAME` | VERIFIED production environment variable |
| `SMARTERASP_SITE_NAME` | `vars.SMARTERASP_SITE_NAME` | VERIFIED production environment variable |
| `SMARTERASP_WEBDEPLOY_PASSWORD` | `secrets.SMARTERASP_WEBDEPLOY_PASSWORD` | VERIFIED production environment secret; never reveal value |

The workflow contexts alone do not establish scope, but the user's GitHub inspection now does. Retaining the environment reference is mandatory to preserve these inputs. Compare identity and input inventories before/after; leave all values untouched. A successful deployment corroborates access without printing credentials. Do not fetch/decrypt secret values or rotate them to perform this check.

`DEPLOY_DIRECTORY`, `PUBLISH_ARTIFACT_ID` and `PSH1_PUBLISH_DIRECTORY` are workflow-derived paths/outputs, not hosted configuration to move. SQL/JWT/WordsAPI runtime settings remain external host configuration and are not new GitHub deployment inputs.

## 9. CI Gate Preservation

```text
master push
  ├─ backend-tests ──┐
  └─ frontend-tests ┤ both must succeed
                    ↓
                  build
  .NET + Angular → stage → PSH-1 checks → publish/validate → upload
                    ↓
  deploy-production (push/master, environment, concurrency)
  exact artifact → validation → live-master guard → MSDeploy → HTTPS acceptance
```

`build.needs: [backend-tests, frontend-tests]` and `deploy-production.needs: build` enforce transitive success. There is no `always()` or `continue-on-error` override. Retain normal dependency success behavior and step failure propagation. Removing environment reviewers changes an external eligibility condition; it does not modify these dependencies. [GitHub job dependency syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds).

Verification must confirm that tests actually execute, not merely that familiar job names appear. No test filters/skips, artifact substitutes, approval bypass button, digest relaxation or acceptance suppression may be used to make the first run pass.

## 10. Out-of-Order Deployment / Concurrency Decision

**Classification: REQUIRED BEFORE MANUAL GATE REMOVAL. Category: source-controlled repository change. Selected design: Option D, non-cancelling production-job serialization plus a last-moment live-master guard, with a retained pending queue.** A human stale-run screening practice is no longer the selected mitigation.

### Existing behavior and option comparison

There is no workflow-level concurrency or reusable workflow. Tests and builds from different master pushes may overlap. Only the deployment job uses `vocabularyapp-production`, after `needs: build`. The script validates its Actions/push/master context but never checks whether its SHA is still current. `AppOffline` is managed by MSDeploy; the script's `finally` disposes the process and clears credential references, not remote files. Acceptance runs later in the same deployment job.

| Option | Repository-specific result | Decision |
| --- | --- | --- |
| A: current job concurrency, cancellation false | Prevents simultaneous production jobs and protects active sync; does not prevent slow A arriving after B. Default pending replacement can let obsolete A displace waiting C. | Necessary foundation, insufficient alone |
| B: job cancellation true | New arrivals can terminate a running sync or acceptance step; latest arrival can itself be an older slow build. | Reject |
| C: workflow-level concurrency | With cancellation true, a new push can kill active MSDeploy. With false, serializes all testing/build work but still orders arrivals rather than commit identity and permits historical reruns. | Reject as sole safeguard; unnecessary scope |
| D: job serialization plus immediate stale guard | Active job completes; obsolete candidates fail before the process starts; exact artifact and CI dependencies unchanged. Retain waiting jobs to avoid losing newest work. | Select |
| E: reusable workflow, external deployment coordinator or mutable “latest artifact” | None exists; moving deployment or adding persistent coordination is larger than needed for ordinary master-push races. | Reject for this scope |

### Exact concurrency configuration

Future YAML change, inside `jobs.deploy-production` only:

```yaml
concurrency:
  group: vocabularyapp-production
  cancel-in-progress: false
  queue: max
```

GitHub documents `queue: max` as retaining up to 100 pending entries instead of replacing the sole default pending entry. Queue order reflects when jobs start waiting, not commit order. It cannot be combined with `cancel-in-progress: true`. These semantics are why the guard is required even with the expanded queue. [GitHub concurrency documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency).

The lock spans checkout, artifact download/validation, guard, MSDeploy and final HTTPS acceptance. Do not move the guard into a separate job that releases the lock before synchronization. Do not use a SHA-specific concurrency group, which would allow different commits to deploy simultaneously. Do not introduce workflow cancellation. Tests/builds can finish independently; only successful builds enter deployment.

Queue capacity is finite, not a delivery guarantee. If the queue fills or a run is canceled manually, report the canceled release and arrange a normal current-master retry after the backlog clears; never report convergence to a release that did not run. Hosted validation must confirm `queue: max` is supported before approval removal. No fallback to the unsafe single-pending configuration is authorized.

### Guard contract and exact insertion point

1. Add `scripts/ci/Assert-ProductionReleaseCandidate.ps1`, defining `Assert-ProductionReleaseCandidate` without executing network calls when dot-sourced. Accept an injectable request function for offline fixtures; production uses a bounded HTTPS GET.
2. Derive candidate from immutable `GITHUB_SHA`, not checkout branch state, artifact name, run ID or `github.run_number`. Require valid 40-hex SHA, expected repository `Covenant-Company/VocabularyApp`, `GITHUB_ACTIONS=true`, `GITHUB_EVENT_NAME=push`, and `GITHUB_REF=refs/heads/master`.
3. Read exactly `GET https://api.github.com/repos/Covenant-Company/VocabularyApp/git/ref/heads/master`. Require HTTP 200, `ref == refs/heads/master`, `object.type == commit` and a valid full `object.sha`. Compare that live SHA with the candidate SHA using ordinal case-insensitive hex comparison. Do not use the local remote-tracking ref; it can be stale.
4. Use the built-in `${{ github.token }}` supplied only to the deployment step as `RELEASE_GITHUB_TOKEN`. Existing `contents: read` is sufficient; no PAT, Actions-write/deployments-write permission or environment secret is added. Use Bearer authentication, JSON Accept header, an explicit User-Agent, `Cache-Control: no-cache`, and supported API version `2026-03-10`. Restrict requests to the fixed GitHub API endpoint, disable redirects and use a 10-second timeout. No unbounded retries or fallback-to-allow on network errors. [GitHub Get a reference API](https://docs.github.com/en/rest/git/refs#get-a-reference).
5. In `Deploy-SmarterAsp.ps1`, dot-source/call the assertion **inside the existing outer try/finally, immediately before the existing “Starting ... synchronization” log and inner process-start try** (currently around lines 196–199). Artifact validation, executable validation and argument construction have already completed. There must be no checkout, download, approval wait, sleep or second job between assertion success and `Process.Start()`.
6. On mismatch, throw a constant, distinguishable `RELEASE_STALE` error with candidate/current SHAs if helpful. **Fail the job before production modification**; do not cancel another run, select another artifact, skip the guard, or convert it to a green no-op. Later HTTPS acceptance is skipped by normal failure semantics. A stale rejection is an intentional red deployment attempt, not a failed MSDeploy execution; logs must distinguish these cases.
7. Missing token/ref, bad JSON, unexpected status, redirect, timeout, 403/429 or other API failure throws a sanitized `RELEASE_GUARD_UNAVAILABLE`/context-validation error. No process starts. Do not print raw exceptions, headers, token or response bodies. A GitHub outage now blocks deployment safely; current production stays unchanged by that attempt.
8. Always clear the guard token/header references. `ProcessStartInfo.Environment` may already have captured the token before the guard clears it: explicitly remove `RELEASE_GITHUB_TOKEN` from that child environment alongside the existing deployment-password removal. Preserve the original outer cleanup. No guard token is passed to MSDeploy or retained for acceptance.

This is a simple candidate policy: **deploy only the current master commit after that commit's own CI passes**. It is not a “newest passing historical commit” selector. If B becomes head but fails CI, an older waiting A is not promoted as a fallback. The last successfully deployed application remains unless another in-progress deployment changes it.

### Safety argument and limits

The guard is evaluated only after this job owns the production lock. If B has already deployed and master has advanced normally to B or later, A's live-head comparison fails. If A passes the guard and B is pushed a moment later, B cannot deploy first because A retains the lock through synchronization and acceptance. A completes safely; B then checks current head and deploys if still current and successful. A head change between GET and process start therefore does not allow an older run to overwrite a **newer already-deployed** release within this serialized release path. It may allow A to finish after B was pushed, which is intentional active-deployment safety.

The guarantee concerns stale workflow scheduling under ordinary forward master updates with this unchanged safeguard. It is not branch authorization or an immutable artifact ledger: an authorized actor can deliberately rewind master, remove the guard, rerun pre-safeguard workflow code, or deploy outside Actions. A head-only guard cannot distinguish an intentional branch reset to old code from making that code the current candidate. Do not claim it prevents such administrative actions or deliberate content reverts. No force-push/branch-deletion or old-workflow recovery procedure is introduced; branch-rule hardening remains the explicitly deferred authority control. Future authorized rollbacks use a new reviewed revert commit or the separately controlled recovery procedure, not an unguarded historical workflow rerun.

### Required race scenarios

| Scenario | Expected behavior |
| --- | --- |
| 1. A building, B arrives | Both may finish tests/build; no active workflow cancellation. If master is B when A holds the deployment lock and checks, A fails stale before MSDeploy. B deploys only after its own gates pass. |
| 2. A waiting, B becomes newer | Waiting A does not cache an eligibility decision. Its final live check observes B and rejects A. If B arrives after A's successful check, A is an admitted active release and completes under the lock; B follows if eligible. |
| 3. A actively deploying, B ready | B waits; A is not canceled. Lock remains through A's HTTPS check. B then rechecks live head; if still B and gates passed, it deploys. If C superseded B, B fails stale and C is the candidate. Final production is B only if B actually deploys and passes acceptance. |
| 4. A deployed successfully, B fails CI | B never enters production; production remains A. Do not redeploy an older waiting run as fallback. |
| 5. A fails deployment, B succeeds CI | A fails visibly and may leave partial/offline content. The lock is released when the job ends; B can then check and deploy if current. B is an ordinary forward deployment, not guaranteed repair or automatic rollback. There is no new failure circuit breaker; operator must inspect/contain recovery as in section 12. B's success requires its own MSDeploy and acceptance; if B fails, manual recovery remains required. |
| 6. A/B/C rapid updates | Build completion/queue order may vary. With C as head, A and B fail when checked; C stays queued rather than being replaced by an older arrival. Any already-admitted active release completes safely before C. Within queue capacity and absent cancellation/API/deployment failure, C's successful run leaves production on C; no later stale A/B run passes the guard. |

Additional checks: rerunning a guarded old SHA while master is newer fails stale; API error prevents sync; duplicate runs of the **same current SHA** remain serial and are not treated as older commits; failed newer CI does not revive an obsolete queued artifact. These outcomes require the focused validation in section 16 before gate removal.

## 11. Authenticated Production Verification Decision

**Classification: DEFERRED / NOT REQUIRED FOR THIS CHANGE. Category: deferred follow-up.** New authenticated/database-backed automation is not included; retain current HTTPS acceptance and manual authenticated smoke during rollout.

`Test-ProductionHttps.ps1`, lines 141–155, tests an anonymous profile request expecting 401/Bearer and no development-origin CORS grant. Lines 158–173 verify an emitted JavaScript asset and explicitly state authenticated smoke remains manual. Other checks cover page readiness, redirects, insecure request rejection and HSTS. They do not prove successful login, SQL connectivity, schema compatibility or provider-backed behavior.

Prior Phase 3/PSH records report manual authenticated success. They do not establish a dedicated automation account, its current availability, or credential storage. No workflow authentication secret/account input exists for acceptance. Do not infer a reusable test identity from historical success.

The follow-up must separately address least-privilege account ownership, scoped credential/token storage, lockout/rate limits, bounded retries, deterministic read-only assertions, token/password redaction and account lifecycle. Automated write operations can modify real data; even login may update account state. Do not add a production account, store credentials, or implement write/cleanup tests in this change.

For rollout, an authorized operator uses an existing account securely to verify login and a database-backed read (profile/saved vocabulary) and the documented normal navigation/lookup behavior. Record pass/fail without user data or tokens. If no suitable existing account is available, the controlled-release functional acceptance remains incomplete; do not create one silently. Existing transport checks are sufficient to preserve the current automated gate, not to certify full application functionality.

## 12. Deployment Failure / Recovery Assessment

| Concern | Proven behavior | Classification / treatment |
| --- | --- | --- |
| Nonzero MSDeploy/start/output-capture failure | `Deploy-SmarterAsp.ps1` lines 196–221 throws; normal workflow processing fails job/run and does not execute the later acceptance step | **Acceptable existing operational risk**, provided recovery prerequisites below are met |
| Partial sync / AppOffline persists | In-place sync is nontransactional; AppOffline is delegated to MSDeploy; `finally` lines 224–229 only disposes process and clears references | **Acceptable existing operational risk**; do not promise cleanup on failure; independent recovery automation is future work |
| HTTPS acceptance fails after sync | Acceptance script line 180 throws; deployment job/run fails even though files may already be live | **Acceptable existing operational risk**; investigate/recover manually; no automatic rollback |
| Correct deployment status | Job conclusion follows failures; environment deployment status must be checked during rollout | **Verification only**; a green MSDeploy step alone is not full success |
| No accessible known-good artifact, hosting access or recovery operator | Source cannot prove these are available | **Blocking** until verified |
| Recovery artifact retention | Upload requests 30 days; no permanent archive of the last successful production release | **Recommended future remediation**; retain exact recovery bytes now through existing operational practice |
| No incident containment owner/procedure | A failed run does not prevent subsequent runs | **Blocking** until an operator can stop further releases and coordinate recovery |

Existing procedure: inspect sanitized diagnostics and host/offline state; preserve logs and host-owned content; recover exact known-good, schema-compatible backend/UI bytes using the manual hosting process; inspect stale/partial files; remove site-root `app_offline.htm` only when recovered content is ready; verify transport and authenticated behavior. Do not blindly mirror/delete host files, rerun failed sync repeatedly, or apply database Down/restore as part of application recovery.

References: [Phase 3 implementation results](CI-CD-phase-3-controlled-deployment-implementation-results.md), “Database exclusion and manual recovery”; [manual deployment guide](../Deployment/SmarterASP-Manual-Deployment.md); [R5 production record](R5-production-deployment.md), section 15. R5 schema rollback is a separate historical procedure, not authorization to reverse the current database.

A retained artifact can be manually redeployed as exact bytes. This workflow has no historical-artifact selector or rollback dispatch. Rebuilding an old commit is not equivalent to recovering its original artifact. `DoNotDeleteRule` preserves destination-only files, not old copies of overwritten files.

No architecture redesign is required solely to remove approval. During an incident, pause master release activity, restore the reviewer gate for future jobs and inspect/cancel obsolete jobs that have not begun sync. Already-started jobs require individual assessment; reinstating reviewers does not stop them. Treat forced cancellation during sync as a possible partial deployment.

## 13. Database Safety

**Category: verification only.** Preserve application-files-only deployment. Current workflow/scripts contain no `dotnet ef database update`, SQL execution, migration bundle or MSDeploy database provider. `Program.cs` configures SQL Server but does not invoke `Migrate`/`EnsureCreated` or register migration-running hosted services. Packaging prohibits application SQL/script files and deploys compiled files/static assets.

Existing EF migration classes and model `HasData` are not executed merely by copying binaries. The test fixture's SQLite `EnsureCreated` is test-local, not a production migration path. No automatic migration begins when reviewers are disabled.

Future schema-dependent changes still need separately coordinated migrations before incompatible code reaches automatically deployed `master`. Recheck the release diff for new startup/deployment migration behavior. Do not add database credentials or change migration governance for this task.

## 14. Master Protection / Ruleset Verification

**VERIFIED by manual GitHub inspection: No repository ruleset or classic branch protection currently protects `master`.** GitHub displayed “You haven't created any rulesets” and “Classic branch protections have not been configured.” Record these as known facts, with a brief drift check during implementation. Do not add rulesets, classic protection or required PR/status checks in this change. **Category: deferred follow-up** for hardening; **verification only** for the snapshot.

Repository evidence proves master-push eligibility and CI-before-deployment for this unchanged workflow. It does not prove CI-before-merge: this workflow has no PR or merge-queue trigger. Do not require its push-only checks for PRs without a separately planned trigger change.

After removal of the reviewer gate, a direct push to `master` that passes the CI/CD pipeline and current-candidate check can proceed automatically to production. This is **not a CI bypass**: the automated gates still execute. The environment's selected-master rule restricts the deployable branch but does not require PRs or prevent direct pushes/rewrites. Whoever can update workflow code can change its controls; the concurrency guard is not a substitute for branch authorization. Branch protection/ruleset hardening is separate recommended follow-up, not a prerequisite silently added by this plan.

## 15. Detailed Implementation Steps

The future implementer should execute the following in order, stopping on any failed prerequisite:

| Step | Category | Action and required result |
| --- | --- | --- |
| 1 | Verification only | Read this plan/analysis; compare remote master workflow and scripts with section 3; record SHA and any material drift |
| 2 | Verification only | Drift-check VERIFIED environment/reviewer/input/master restriction and VERIFIED NONE branch settings; record snapshot |
| 3 | Verification only | Establish current health, schema compatibility, live release/recovery artifact and recovery owner/access |
| 4 | Source-controlled repository change | On future branch `devops/remove-production-manual-gate`, implement only section 7's queue, guard, token isolation and offline checks; keep reviewers enabled |
| 5 | Verification only | Validate YAML, permission/needs/environment preservation and the offline scenarios in section 16; review the exact diff |
| 6 | Source-controlled repository change | Commit/push through the normal reviewed process and merge to master; reviewers are still enabled throughout |
| 7 | Verification only | Confirm GitHub accepts `queue: max`; full master CI passes; approve the current safeguard revision using the existing gate, then confirm guard, MSDeploy and acceptance pass |
| 8 | Verification only | Record safeguard SHA/run evidence; drain all pre-safeguard runs; finish active syncs; ensure no historical unguarded run is waiting or already approved |
| 9 | GitHub UI configuration | Only now disable Required reviewers on existing production and save; preserve master restriction and all other verified state |
| 10 | Verification only | Reopen settings; compare identity/input/history and unrelated protection state; restore gate on mismatch |
| 11 | Verification only | Observe next authorized normal master release: full gates, guard and automatic deployment without human approval; no old-run deployment shortcut |
| 12 | Verification only | Verify online/AppOffline state, HTTPS and manual authenticated smoke; record release identity and results |
| 13 | Documentation | Only after successful automatic production verification, publish completion evidence and update section 19 references |

Approval of the safeguard's protected rollout in step 7 is intentional: it demonstrates the protection exists before changing the release policy. The later automatic release is a separate observation. Neither step has been performed in this task.

## 16. Verification Plan

### Pre-change verification — verification only

Use the supplied VERIFIED settings as the baseline; perform a drift check and record the exact owner reviewer for restoration. Confirm remote code, current live release, recoverable artifact, hosting access and healthy HTTPS/authenticated behavior. Historical PSH completion is supporting evidence, not a substitute for a current health check. Keep Required reviewers enabled throughout source validation and rollout.

### Source and guard validation — verification only, future execution

Add `scripts/ci/tests/Test-ProductionReleaseCandidate.ps1` using the repository's existing self-contained PowerShell assertion style. Run it in the existing `build` job before artifact upload; preserve all existing test/build/PSH-1 commands. In a later implementation session, execute the focused offline script and validate the workflow syntax without executing the deployment script against production.

Required fixtures/structural checks:

- Candidate equals valid live ref: assertion returns normally; an injected process-start recorder is reached exactly once after the check.
- Candidate differs: `RELEASE_STALE`; process-start recorder never invoked and post-deploy acceptance not invoked. No success masking.
- Missing/invalid token/context/SHA/ref/type, malformed JSON, non-200, redirect, 403/429 and timeout: sanitized failure, zero process starts, no token in any emitted diagnostic.
- Token is removed from the captured child-process environment, not only from the parent environment after capture; existing deployment-password handling remains intact.
- Simulate all six section 10 event sequences with a serialized admission/process-start recorder; include B success before slow A admission, head advancing after A admission, C queued before obsolete A, latest-C CI failure, and old guarded-run rerun. Assert obsolete SHA never starts after a newer deployed SHA during ordinary forward updates.
- Inspect the real YAML/script integration: one shared deployment group; `queue: max`; cancellation false; no workflow-level cancellation; guard inside the deployment lock after package/tool validation and before native process start; guard failure cannot reach sync; original `needs`, artifact ID/digest, portable publish, AppOffline and acceptance remain.

The injectable seam must only substitute read-only requests and a nonproduction process-start recorder in offline validation, never introduce a workflow input or environment flag that bypasses the production guard. Do not mock by invoking real MSDeploy. Test fixtures must fail if the production call site no longer runs the guard before sync, not merely test equality in isolation.

Validate hosted YAML support and the built-in token's ref-read access on the protected safeguard rollout. Offline models verify decision logic; they do not prove the GitHub scheduler implementation. For actual multi-run queue timing validation, use an authorized isolated nonproduction harness with no production environment/credentials or MSDeploy calls, mirroring this job concurrency and recording entry/exit. Confirm active work is not canceled and a newer pending job is not displaced by an older late arrival. Do not add a second production deployment workflow or induce concurrent production writes as a test. Record hosted queue validation evidence before gate removal; if it cannot be established, leave reviewers enabled.

### Configuration verification — verification only

After source protection is verified and the reviewer setting is saved, reopen the same environment and compare with the before-state record. Required reviewers should be disabled; identity/history, environment input inventory, Selected branches and tags limited to master, disabled timer and administrator-bypass posture remain. No custom-rule change is planned. Do not log credential values.

### Controlled deployment — verification only

For the next normal, authorized master release, capture the run URL, full commit SHA and run attempt. Confirm:

1. Backend/integration and frontend tests execute and pass with unchanged commands.
2. Build, Angular staging, offline PSH-1 checks, publish validation, actual-config checks and upload all succeed; record artifact ID/name.
3. `Deploy to SmarterASP.NET` starts automatically after prerequisites, without “Review deployments” or administrative bypass. A runner/concurrency/timer wait is not a manual approval gate; identify its cause rather than bypassing it.
4. Deployment uses `production`, the tested commit scripts and the same run's exact artifact; digest validation passes. Record that the candidate SHA matched live master at the immediate pre-sync guard and that the deployment job used the required non-cancelling queue. An unexpected stale rejection requires checking current head, not bypassing the guard.
5. MSDeploy reports success and the final HTTPS acceptance step passes, including HSTS `max-age=300`, HTTP redirects/rejections, anonymous API challenge/CORS and asset checks.
6. The site loads fresh HTTPS content. Using existing hosting access, confirm site-root `app_offline.htm` is absent and there is no offline/partial-deployment symptom. A green MSDeploy step alone is not cleanup evidence; if root inspection is unavailable, record that limitation and obtain equivalent operator confirmation rather than claim guaranteed cleanup.
7. An operator verifies login, profile/saved-vocabulary read, navigation/direct refresh and normal dictionary UI behavior using an existing account. Avoid real-data writes unless separately authorized by the existing smoke procedure. Record sanitized results, never credentials/tokens or personal response contents.
8. Workflow, deployment job and environment deployment record show success for the intended release. The manual functional result is recorded separately; it is not a new automated gate.

### Negative paths — verification only

Recheck the unchanged dependency graph and inspect any available historical failed runs: backend or frontend failure prevents build; build/publish/upload failure prevents deploy; invalid/missing artifact or validation failure stops before MSDeploy; MSDeploy failure skips acceptance; acceptance failure fails the job after synchronization. Cancellation is not success. No production failure injection, deliberately broken master commit, invalid credential attempt or destructive smoke test is needed.

A sequential successful rollout alone does not validate stale-run safety. The required offline race fixtures, call-site integration checks and isolated hosted queue evidence above must already exist before removing reviewers. No test, fixture or harness was executed while remediating this plan.

## 17. Production Acceptance Criteria

The implementation is complete only when all applicable criteria have evidence:

- Same `production` environment retained; required configuration accessible and unrelated protections preserved.
- Both required test jobs and the full build/package chain passed for the deployed SHA.
- Required queue/guard changes were verified on master while reviewers were still enabled, and old unguarded runs were drained before the setting change.
- The subsequent controlled release started without any human review/bypass action, under preserved push/master/needs conditions and the immediate live-master guard.
- Exact artifact download, integrity/layout checks, MSDeploy and final HTTPS acceptance passed.
- Production online state/AppOffline removal and manual authenticated/database-backed functionality verified.
- No automatic migrations or new credentials/accounts introduced.
- Correct deployment/run status and release identifiers recorded; exact known-good recovery bytes remain available.
- Race/queue validation proves rejection of obsolete queued candidates and no automatic interruption of active MSDeploy; API failure blocks deployment before modification.
- Scope limits (queue capacity, branch-authority/rewrites, historical unguarded workflows and out-of-band deployments) and functional-coverage limits recorded; a recovery owner is available.
- Post-verification documentation reflects the new policy without rewriting historical deployment evidence.

Failure of any required criterion means the change is not verified complete; contain further deployment and apply section 18 as appropriate.

## 18. Rollback Plan

**Triggers:** unintended protection/configuration change, automatic release outside the accepted policy, inability to observe/recover releases, stale overwrite, unexplained production regression, or owner decision to restore manual control. A deployment failure also requires investigation; policy rollback alone does not repair files.

### Manual gate rollback — GitHub UI configuration

Open Settings → Environments → `production`; enable Required reviewers for the recorded owner/user, with Prevent self-review disabled and administrator bypass not enabled; save protection rules. Keep timer disabled, master-only selected branch restriction and all environment inputs. This restores the human boundary for jobs that have not already passed environment protection without deleting or changing the environment identity. No Git commit is needed for this rollback.

**Verification only:** reopen settings and compare with the before-state. For behavioral confirmation, observe the next authorized normal master run: after test/build success, production should wait for review with no deployment steps executed. Do not use a bypass or automatically approve merely to finish this check. No deliberately triggered test deployment is required; until such a run exists, report “settings restored; behavioral confirmation pending.”

Restoring reviewers does not revoke access from already-running jobs or reverse files already deployed. Inspect active and pending jobs separately; coordinate application recovery via section 12 if needed. Preserve HTTPS throughout recovery because existing HSTS clients retain policy.

### Concurrency/guard rollback — source-controlled repository change

If the queue/guard blocks valid releases or has an implementation defect, identify its reviewed commit(s). Restore Required reviewers **before** removing the safeguard from master, pause new releases and drain queued jobs that could run with mixed versions. Then revert the narrow safeguard commit through the normal Git review process: remove the new queue property/token wiring/offline-check invocation, remove the guard call/token child-environment handling added for it, and remove the new helper/fixtures if no longer referenced. Restore the prior known-good deployment behavior, including the original shared `vocabularyapp-production` group with cancellation false; never remove its serialization.

If unrelated changes share the commit, revert only the safeguard changes through a reviewed patch rather than discarding unrelated work. Validate the resulting YAML/dependencies and deploy only through restored manual approval. The source rollback does not restore the application artifact and does not change GitHub settings by itself. Record the new revert SHA and results. A run already executing old workflow code is unaffected by a repository revert, so inspect active jobs separately.

The paths are independent: restoring reviewers can contain future unattended releases while retaining the safeguard; reverting source restores prior scheduling but must never leave the required safeguard absent with reviewers disabled.

**Documentation:** add a dated rollback record and restore current operational instructions to the verified policy. Preserve the original successful/failed attempt evidence.

## 19. Documentation Updates

**Category: documentation. Future edits only, after production verification succeeds.** Create `Docs/Updates/CI-CD-remove-production-manual-gate-implementation-results.md` with settings evidence (no values), implementation time/operator, run URL/SHA/artifact, job/acceptance results, manual smoke result, residual risks and rollback readiness. Do not state success based only on saving a checkbox.

Search results identify these update targets:

| Existing file / location | Future treatment |
| --- | --- |
| `Docs/Deployment/SmarterASP-Manual-Deployment.md`, lines 372 and 670–671 | Update ongoing CI release instructions from approval to automatic deployment after CI; retain the manual hosting/recovery procedure and manual functional verification |
| `Docs/Updates/CI-CD-phase-3-completion.md`, lines 7, 75, 157–160 | Add a dated current-policy notice/link; supersede ongoing “approval remains required” and reviewer stale-run responsibility; preserve historical Phase 3 evidence |
| `Docs/Updates/CI-CD-phase-3-controlled-deployment-implementation-results.md`, environment, concurrency, recovery and operator-checklist sections | Add a supersession notice pointing to the current procedure; replace reliance on “stop newer approvals” in current guidance with incident containment; retain historical implementation detail |
| `Docs/Updates/PSH-1-https-ssl-production-hardening-completion.md`, line 83 | Clarify current CI/CD approval policy via dated notice/link; retain line 59's factual historical approvals and all PSH acceptance requirements |
| `Docs/Updates/PSH-1-https-ssl-production-hardening-implementation-plan.md` | Add current-policy pointer; preserve dated Release A/B approval records and historical plan sections |
| `Docs/Updates/CI-CD-phase-3-webdeploy-diagnostics-remediation.md`, operator steps; `CI-CD-phase-3-msdeploy-sync-pass-remediation.md`, deployment guidance; `CI-CD-phase-3-dotnet-runtime-resolution-remediation.md`, deployment guidance | Add concise historical/superseded-policy notices where forward instructions still tell the operator to approve |
| `Docs/Updates/CI-CD-analysis-and-implementation-plan.md`, historical dispatch/approval proposal | Extend its existing historical notice with a link to verified current operation; do not rewrite the original design as if it had always been automatic |
| Gate-removal analysis and this plan | Preserve as analysis/planning records; link to results via a dated status note if useful |

Do not globally replace every “approval” occurrence. Manual database migrations, recovery authorization, historical deployment approvals and unrelated reviews retain their meaning. Re-run a focused documentation search during implementation to catch new operational references. Documentation-only master merges still trigger CI/deployment and must contain the required safeguard. Include guard rejection/API failure meanings, current-head-only candidate policy, queue behavior, no branch protection, no automatic migrations and separate rollback paths in the completion record.

## 20. Git / Branch Strategy

**Source-controlled repository change:** the future implementation requires branch `devops/remove-production-manual-gate`. Keep the queue/guard/wiring/validation changes in a narrowly scoped reviewed commit for independent rollback. Validate, commit/push and merge through the normal process while Required reviewers remains enabled. Verify the safeguard revision on master before touching the approval setting.

**GitHub UI configuration:** removing/restoring reviewers remains external to Git; record its exact before/after state and timing independently. **Documentation:** completion/current-policy edits follow successful automatic deployment and may use the same branch if still appropriate or the normal documentation review process.

No branch, commit, push or merge was performed in this remediation task. The future source changes are required by this plan, but execution still belongs to a later implementation session.

## 21. Risks and Mitigations

| Risk | Mitigation / category |
| --- | --- |
| Wrong environment or wrong protection removed | Before/after identity and rule inventory; narrow edit; restore saved state on mismatch — verification only / GitHub UI configuration |
| Credentials become unavailable | Keep environment reference/identity and all input values untouched; confirm scope/access — verification only |
| Older queued release overwrites newer | Required current-master guard inside retained non-cancelling deployment queue; obsolete SHA fails before process start — source-controlled repository change |
| Obsolete late arrival replaces newest pending job | `queue: max` retains waiting jobs; guard handles arbitrary arrival order; queue saturation remains observable, finite-capacity failure — source-controlled repository change / verification only |
| GitHub ref API unavailable | Bounded authenticated read, strict response validation, sanitized fail-before-sync; no allow fallback — source-controlled repository change |
| Active MSDeploy interrupted | Retain deployment `cancel-in-progress: false`; avoid broad cancellation changes; assess active jobs individually — verification only |
| Failed/partial/offline production | Known-good exact artifact, named operator, hosting access and incident containment — verification only |
| Transport succeeds but functional app fails | Manual authenticated database-backed smoke; automation deferred — verification only / deferred follow-up |
| Direct push, branch rewind or workflow edit changes release behavior | VERIFIED no branch protections; document authority limits, do not use historical unguarded workflows; branch hardening deferred — documentation / deferred follow-up |
| Schema incompatible despite no migrations | Verify current release compatibility and preserve separately controlled database operations — verification only |
| Documentation publishes obsolete instructions | Update current guidance only after successful automatic release; retain historical records — documentation |

## 22. Deferred Follow-Up Work

All items here are **deferred follow-up**, not hidden implementation steps:

1. Branch protection/rulesets and review of push/force-push/workflow-change authority. None is configured; do not add them in this implementation. The required scheduling guard does not authorize branch rewinds or replace access controls.
2. Automated authenticated/database-backed smoke with secure account/token lifecycle and deterministic, minimally mutating behavior.
3. More durable recovery artifact retention and an explicitly reviewed artifact redeployment path; no automatic database rollback implied.
4. Better incident notification/containment and recovery automation if operational ownership proves insufficient.

Stale-run scheduling protection is no longer deferred: section 10 is mandatory. Do not combine unrelated HSTS strengthening, migration automation, hosting changes or application features with this gate removal. Deliberate branch-history rollback enforcement or a durable deployed-artifact ledger would need a separately reviewed design if later required; no such administrative rollback behavior is introduced here.

## 23. Final Implementation Sequence

1. **Verification only:** use verified settings; establish remote baseline, current health and recovery readiness; capture non-secret restoration snapshot.
2. **Source-controlled repository change:** implement required queue/guard/wiring/offline validation on `devops/remove-production-manual-gate`; reviewers remain enabled.
3. **Verification only:** validate syntax, permissions, original gates, token handling and required race/failure fixtures; establish isolated hosted queue evidence.
4. **Source-controlled repository change:** commit/push/review/merge through the normal process while the manual boundary remains.
5. **Verification only:** confirm the exact safeguard revision is on master, GitHub accepts its configuration and full CI passes; use existing reviewer approval for its protected deployment and verify guard/MSDeploy/acceptance.
6. **Verification only:** clear all old unguarded queued/approved runs, let active syncs finish, confirm rollback access and current health.
7. **GitHub UI configuration:** only now disable Required reviewers; preserve production identity, environment inputs and master restriction.
8. **Verification only:** observe a controlled normal master release without approval; require guard, exact artifact, MSDeploy, HTTPS and manual authenticated smoke success.
9. **Documentation:** record evidence and update current instructions; stop after successful verification, or restore policy and recover separately on failure.

No implementation step above has been executed in this remediation task.

## 24. Go / No-Go Criteria

| Readiness item | Status at plan remediation |
| --- | --- |
| Manual gate source | **VERIFIED**: production Required reviewers |
| Environment identity | **VERIFIED**: Covenant-Company/VocabularyApp, production |
| Deployment configuration scope | **VERIFIED**: password secret and three variables belong to production |
| Environment master restriction | **VERIFIED**: Selected branches and tags permits master; preserve |
| Repository rulesets | **VERIFIED NONE**; hardening deferred |
| Classic branch protection | **VERIFIED NONE**; hardening deferred |
| Safe scheduling design | **RESOLVED**: retained non-cancelling production queue plus immediate live-master guard; sections 10/16 define behavior and validation |
| Authenticated automation | **DEFERRED / NOT REQUIRED FOR THIS CHANGE**; manual rollout smoke retained |
| Database behavior | **UNCHANGED**: no migration/SQL/startup migration introduced |
| Rollback | **DEFINED**: independent Git source revert and environment reviewer restoration |
| Current health/recovery baseline | **PENDING implementation-time evidence**; historical successful operation is not current verification |
| Safeguard implementation/hosted validation | **NOT PERFORMED** in this planning-only task |

**READY for the future source-controlled safeguard implementation phase:** the concrete design is resolved and the earlier setting unknowns are resolved by verified evidence. Begin that phase only after establishing its health/recovery baseline; keep reviewers enabled.

**GO to remove reviewers** requires: the required source changes are reviewed, validated and on master; hosted queue support/token access and the six race scenarios have evidence; a protected rollout of the safeguard revision passes all CI/deploy/HTTPS gates; current production is healthy; exact recovery artifact/hosting access/owner are available; all old unguarded runs are drained; the environment/input/master restriction snapshot still matches; no migration or unrelated gate change exists; and both rollback paths are available.

**Current gate-removal assessment: NO-GO pending implementation/validation and current health/recovery evidence, not because the concurrency design is unresolved.** Do not disable Required reviewers early, waive stale-run protection as a deferral, fall back to cancellation true, remove the environment, or weaken CI. If queue support or guard correctness cannot be validated, remain NO-GO and resolve the specific defect with the manual boundary retained.

Plan remediation complete. Only this plan was edited; no workflow/GitHub setting, test execution, branch, commit or push was performed.
