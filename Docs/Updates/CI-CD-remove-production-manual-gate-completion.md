# CI/CD — Remove Production Manual Deployment Gate — Interim Completion

Documentation review date: 2026-09-20. Overall task status: **PENDING final automatic no-reviewer deployment validation**.

## 1. Current Status

**IMPLEMENTATION COMPLETE**

**MANUAL GATE REMOVED**

**FINAL AUTOMATIC DEPLOYMENT VALIDATION PENDING**

This is an interim completion record. The safeguard was implemented and merged to `master`, validated in hosted workflow **#62** using the existing manual approval gate, and followed by successful manual production smoke testing. Required reviewers was then disabled on the retained `production` environment. A subsequent master push has not yet supplied the final no-reviewer deployment evidence recorded in section 11. The overall task is not fully complete.

Repository: `Covenant-Company/VocabularyApp`. Release branch: `master`. Documentation worktree branch: `devops/remove-production-manual-gate`; inspected HEAD: `f985605`.

Evidence sources: the [analysis](CI-CD-remove-production-manual-gate-analysis.md), the revised [implementation plan](CI-CD-remove-production-manual-gate-implementation-plan.md), inspected committed source/history, prior local verification, and the user's verified hosted/production/settings evidence supplied for this record. Hosted results and environment settings were not independently re-queried during document preparation. No tests, production probes, deployments or GitHub changes were performed to author this record. No run URL or artifact ID was supplied, so neither is invented.

## 2. Objective and Manual-Gate Baseline

Allow a successful master release to deploy automatically after all existing automated prerequisites, without a human deployment approval prompt. Preserve the production environment, its configuration, master restriction, exact-artifact deployment and HTTPS acceptance checks.

The approval originated from **Required reviewers** on the GitHub `production` environment, not from an approval command in YAML. Before removal, the owner/user was the required reviewer, Prevent self-review was disabled, the wait timer was disabled and administrator bypass was not enabled. The environment used Selected branches and tags with `master` permitted.

The original analysis identified a configuration-only operation for removing reviewers. The revised implementation plan additionally required stale-release protection before performing that operation. This record reflects that revised implementation order; the historical analysis and plan retain their original phase-specific observations.

## 3. Source Implementation and Commits

| Commit | Description |
| --- | --- |
| `b491e06` | Document production manual gate removal plan |
| `87a5427` | Add production release candidate safeguard |
| `f985605` | Merge production release candidate safeguard |

The source safeguard was merged to `master`. The inspected local branch also points to `f985605`.

| File | Implemented change |
| --- | --- |
| [.github/workflows/backend-tests.yml](../../.github/workflows/backend-tests.yml) | Retained production serialization/cancellation policy, added `queue: max`, passed the built-in token only to the deployment step, and added offline guard validation before artifact upload |
| [Assert-ProductionReleaseCandidate.ps1](../../scripts/ci/Assert-ProductionReleaseCandidate.ps1) | New dot-sourceable context/live-master assertion with an injectable read-only request seam for offline validation |
| [Deploy-SmarterAsp.ps1](../../scripts/ci/Deploy-SmarterAsp.ps1) | Calls the guard immediately before synchronization/process start; excludes the release token from the child environment and clears it afterward |
| [Test-ProductionReleaseCandidate.ps1](../../scripts/ci/tests/Test-ProductionReleaseCandidate.ps1) | New deterministic offline guard, integration, credential-handling and race-scenario assertions |

Production job concurrency is:

```yaml
concurrency:
  group: vocabularyapp-production
  cancel-in-progress: false
  queue: max
```

No workflow-level cancellation or SHA-specific deployment group was added. The lock covers the complete deployment job, including checkout, artifact retrieval/validation, guard, MSDeploy and HTTPS acceptance.

## 4. Safety Design and Preserved Gates

The guard requires the expected GitHub Actions context: Actions enabled, push event, `refs/heads/master`, repository `Covenant-Company/VocabularyApp`, valid full 40-character hexadecimal `GITHUB_SHA`, and a release token. It compares that immutable candidate SHA with the live master SHA obtained from:

```text
GET https://api.github.com/repos/Covenant-Company/VocabularyApp/git/ref/heads/master
```

The request uses Bearer authentication, GitHub JSON Accept, an explicit User-Agent, no-cache, API version `2026-03-10`, disabled redirects, a 10-second timeout and a bounded response buffer. It requires HTTP 200, the exact master ref, object type `commit`, and a valid full SHA. Equality uses an ordinal case-insensitive hexadecimal comparison.

The assertion runs inside the existing cleanup-protected outer `try/finally`, after package/tool validation and argument construction, immediately before the synchronization log and `Process.Start()` boundary. Stale candidates throw **`RELEASE_STALE`** before MSDeploy. Invalid context throws **`RELEASE_GUARD_INVALID_CONTEXT`**; missing token and API/transport/ref-validation failures throw **`RELEASE_GUARD_UNAVAILABLE`**. Errors do not select another artifact, allow deployment on failure, or report stale attempts as success.

`RELEASE_GITHUB_TOKEN` is sourced from `${{ github.token }}` only on the deployment step. Existing `contents: read` remains sufficient; no PAT or user-managed secret was added. The token is explicitly removed from the captured MSDeploy child-process environment and cleared from the parent environment afterward. Existing WebDeploy-password handling remains intact.

Both test jobs still gate `build` through `needs: [backend-tests, frontend-tests]`; deployment still requires `needs: build` and a master push. Angular/.NET builds, portable publish, PSH-1 assertions, package validation, exact current-run artifact ID, digest enforcement, MSDeploy exit handling, AppOffline and final HTTPS acceptance remain in place. The deployment job does not rebuild the application.

An admitted active deployment completes without automatic cancellation merely because a newer push arrives. Waiting candidates check freshness only after obtaining the production lock. This prevents ordinary out-of-order forward master releases from overwriting a newer deployed commit. The policy is current-master-only, not selection of the newest passing historical artifact.

## 5. Local Verification Evidence

These are the verified results from the implementation session; they were not rerun while creating this document.

| Verification | Result |
| --- | --- |
| `Test-ProductionReleaseCandidate.ps1` | **1,008 assertions passed** |
| `Test-Psh1Scripts.ps1` | **279 passed, 0 failed** |
| `js-yaml` workflow structural checks | **10 assertions passed** |
| `git diff --check` on implementation | **Passed** |

Offline coverage included all six planned scenarios: A building when B arrives; A waiting when B becomes current; A actively deploying while B waits; A successful with B failing CI; A failing deployment with B making an independent forward attempt; and three rapid A/B/C updates. It also covered guarded old reruns, duplicate current-SHA runs, head advancement after admission, API failures, the actual guarded process boundary using a fake process, credential redaction and token isolation.

These fixtures made no real GitHub API, production or MSDeploy calls. They validate guard logic and modeled scheduling, not exhaustive live GitHub scheduler behavior.

## 6. Hosted Validation — Manual Gate Still Enabled

Workflow: **CI / Publish Artifact**. Run: **#62**. Commit: **`f985605`**.

| Stage / observation | Verified result |
| --- | --- |
| Backend / Integration Tests | Passed |
| Frontend Tests | Passed |
| Build / Publish Artifact | Passed |
| Artifact production | Successful |
| Initial production eligibility | Deploy to SmarterASP.NET stopped at Required reviewers |
| Approval | Existing production gate was intentionally used; deployment approved manually |
| Deploy to SmarterASP.NET | Passed |
| Overall workflow | **Success** |
| Total workflow duration shown | **6m 29s** |
| Production deployment job duration shown | **33s** |

Run #62 demonstrated that GitHub accepted the updated workflow/concurrency configuration and that the live guard admitted the current master candidate before MSDeploy. The deployment job retained its mandatory post-MSDeploy HTTPS acceptance step.

This was a **manually approved** validation of the safeguard. It is not evidence that deployment proceeds without review after Required reviewers is disabled. A single successful hosted run also does not establish every queue saturation or overlapping-run scenario.

## 7. Production Smoke-Test Evidence

Following workflow #62, the user verified:

- Production loaded successfully over HTTPS.
- Authenticated login succeeded.
- Dictionary lookup succeeded and returned definitions.
- My Words loaded successfully.
- Manual authenticated production smoke testing passed.

No database migration or database schema change was part of this work. The implementation does not add EF commands, SQL execution, startup migrations, migration bundles or database rollback behavior. Authenticated automated production testing remains separate from the existing transport checks.

## 8. GitHub Environment Change

**After** successful hosted safeguard validation and production smoke testing, Required reviewers was disabled at:

```text
Covenant-Company/VocabularyApp
→ Settings → Environments → production
→ Required reviewers
```

Verified state supplied after that change:

| Setting / resource | State |
| --- | --- |
| Required reviewers | Disabled |
| Wait timer | Disabled |
| Administrator bypass | Disabled |
| `production` environment | Retained |
| Environment-scoped secret/variables | Retained |
| Selected deployment branch restriction | Retained; `master` remains the production branch |
| Repository branch protection/rulesets | No protection/ruleset added in this task |

The environment remains required: it supplies secret `SMARTERASP_WEBDEPLOY_PASSWORD` and variables `SMARTERASP_SITE_NAME`, `SMARTERASP_WEBDEPLOY_URL`, and `SMARTERASP_WEBDEPLOY_USERNAME`. No values are recorded here. Keeping the environment preserves this access and its deployment association/history/status.

The source-controlled release guard and GitHub's Required reviewers setting are independent controls. Disabling reviewers does not disable the guard or bypass test/build dependencies.

## 9. Known Limits and Deferred Work

- Repository rulesets and classic branch protection were verified absent during planning and remain deferred; none was added here. A direct master push can initiate automatic production release after all CI and guard requirements pass. This is not a CI bypass.
- Automated authenticated/database-backed smoke remains deferred. Manual authenticated smoke is still required for rollout verification; transport acceptance alone does not certify database functionality.
- No automatic migration behavior was introduced. Schema-dependent releases still require separate coordination.
- AppOffline and synchronization remain in-place operations. A failed sync may leave partial/offline content; acceptance failure does not automatically restore the previous release. Recovery remains manual, and another eligible run can subsequently attempt deployment.
- The retained queue is finite. Configuration acceptance in run #62 does not certify exhaustive hosted race/saturation behavior.
- The guard protects normal forward master scheduling with the safeguard present. It does not prevent deliberate branch rewinds, edits removing the guard, pre-safeguard historical workflow reruns or out-of-band deployments. It is not a branch authorization control or an immutable artifact ledger.
- Artifact retention remains 30 days; retain exact known-good recovery bytes outside that window when necessary.

## 10. Rollback

**Immediate configuration rollback:** if automatic deployment behavior is unacceptable, re-enable Required reviewers on the existing `production` environment for the recorded owner/user. Preserve the environment, master restriction and all secrets/variables. Restore the recorded self-review/bypass posture and verify the setting. This restores a human boundary for jobs that have not already passed environment protection; it does not stop or undo an already-running sync.

**Independent source rollback:** if the concurrency/guard implementation needs reverting, first restore Required reviewers and contain pending work. Revert the narrow safeguard changes through the normal reviewed Git process, preserving the prior shared production concurrency group and `cancel-in-progress: false`. Do not leave reviewers disabled while removing the safeguard.

**Application recovery:** neither policy rollback nor a Git revert restores production files. Use the existing [manual deployment/recovery guidance](../Deployment/SmarterASP-Manual-Deployment.md), inspect partial content and AppOffline state, and restore exact schema-compatible known-good artifacts when required. Do not introduce automatic database rollback or indiscriminately interrupt an active MSDeploy operation.

## 11. Pending Final Acceptance

**PENDING — a new master push must demonstrate the complete release path with Required reviewers disabled.**

- [ ] Backend / Integration Tests pass.
- [ ] Frontend Tests pass.
- [ ] Build / Publish Artifact passes.
- [ ] Production deployment begins without entering **waiting for review** or requiring an approval/bypass click.
- [ ] The guard admits the current master SHA.
- [ ] MSDeploy succeeds.
- [ ] Existing HTTPS/PSH-1 production acceptance checks succeed.
- [ ] Manual authenticated production smoke testing succeeds.

The future **completion-document commit itself is intended to provide the controlled master push** for this validation, once reviewed and delivered through the normal release process. Creating this uncommitted file does not trigger that release. No commit, push, merge, pull request or deployment was performed during document preparation.

After the intended run, record its number/URL, commit SHA, job results, absence of review waiting, guard/deployment/HTTPS results and manual smoke evidence. Do not reuse run #62 as proof of no-reviewer behavior. Do not mark the overall task fully complete until all pending acceptance items are verified.

## 12. Interim Outcome

**IMPLEMENTATION COMPLETE**

**MANUAL GATE REMOVED**

**FINAL AUTOMATIC DEPLOYMENT VALIDATION PENDING**

This record updates the current state without altering the historical analysis/plan or claiming the final automatic validation has occurred. Only this document was created and is left uncommitted for review.
