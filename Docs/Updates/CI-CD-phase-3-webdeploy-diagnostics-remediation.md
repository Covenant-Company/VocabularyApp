# Phase 3 Web Deploy diagnostics remediation

## Reported failure and diagnosis

The user reports that Backend / Integration Tests, Frontend Tests, Build / Publish Artifact, and the production approval gate succeeded. The deployment script then validated the downloaded artifact, discovered Microsoft Web Deploy file version `7.1.9419.0650`, and began content synchronization. MSDeploy returned `-1`. The user manually checked production afterward and found the application online. These are supplied observations, not checks performed during this remediation.

The root cause of the **missing diagnostics** is definite: the original script captured both output streams but emitted only matches for `\bERROR_[A-Z0-9_]+\b`. All surrounding error messages, ordinary errors without those identifiers, and useful connection/argument details were discarded. Its final throw consequently reported only the exit code and recovery warning. The separate process-exception catch also discarded the exception message.

The root cause of the **MSDeploy failure itself remains unknown**. Exit code -1 does not establish authentication failure, invalid credentials, TLS failure, or a command construction defect. No definite MSDeploy command bug was found by static inspection. Target values and credential settings were populated according to the user; they have not been changed or independently validated.

## Existing invocation and capture

The script uses `System.Diagnostics.Process` with `ProcessStartInfo`, `UseShellExecute=false`, and `ArgumentList`. It does not invoke MSDeploy through Start-Process, the PowerShell call operator, Invoke-Expression, or a shell command string.

Both `RedirectStandardOutput` and `RedirectStandardError` are enabled. Both streams start `ReadToEndAsync()` before `WaitForExit()`, so they drain concurrently rather than blocking one pipe while reading the other. Results remain in memory; no native diagnostic file, transcript, or diagnostic artifact is created. The process's `ExitCode` is read directly, independently of PowerShell pipeline status or LASTEXITCODE.

The original literal password replacement protected only the subsequent error-identifier extraction. It was not a general sanitizer suitable for printing full messages. The remediation replaces this restrictive output stage with explicit sanitization.

## Changes made

Modified `scripts/ci/Deploy-SmarterAsp.ps1`. Created this document at `docs/Updates/CI-CD-phase-3-webdeploy-diagnostics-remediation.md`. The previous Phase 3 report remains a historical implementation record; this document supersedes its description of error-code-only logging.

The script retains concurrent, in-memory capture. It now captures ExitCode immediately after WaitForExit, before obtaining stream task results. On a nonzero exit it prints a failure notice, nonempty sanitized stdout/stderr sections, the exact captured exit code, and then throws. If both streams are empty, it explicitly reports that fact instead of printing empty sections. Successful deployment retains its existing success message and does not dump native output.

Process-start or output-capture exceptions now pass their message through the same sanitizer before the script throws. If an exit code was already obtained, it is also reported. A process that never starts has no native exit code; the script does not invent one. An output-capture failure may prevent complete stream diagnostics, but still fails the job.

## Sanitization boundary

Only `Write-SanitizedDiagnostic` writes native message text. It completes sanitization before writing any of that text. Sensitive matches become `***`.

- Replace the actual Web Deploy password and exact nonempty values of sensitive-named environment variables already available to the process. This includes password, secret, token, API-key, authorization, and connection-string names. No new secrets are fetched or supplied to the workflow.
- Also redact common URI-encoded, HTML-encoded, JSON-escaped, and Base64 representations of those values, plus the Basic-auth username/password token. Longer values are replaced first; equally long values are all retained for redaction.
- Remove quoted sensitive assignments, including multiline quoted values, before applying a conservative unquoted-assignment rule. The latter suppresses the remainder of the line rather than guessing where an unknown password ends. Matching is case-insensitive and covers `password`, `passwd`, `pwd`, username/user ID, secret/token/API-key fields, and connection-string names. MSDeploy comma-separated password arguments are covered in single-quoted, double-quoted, and unquoted forms.
- Suppress common XML secret configuration, JSON ConnectionStrings/JwtSettings containers, authorization headers including folded continuation lines, Basic/Bearer tokens, JWT-shaped tokens, URL user information, and connection-string tails. These rules also cover recognizable sensitive fields whose values are not available locally, including RapidAPI and production database configuration.
- Suppress echoed command tails beginning with msdeploy.exe or source/destination/verb arguments, so the script does not print its credential-bearing command line. This can remove part of an invalid-argument diagnostic; independent error text and codes remain useful.
- Remove terminal control sequences and prefix every emitted native line with `| `, preventing it from becoming a GitHub workflow command.
- If sanitization itself raises an exception, withhold the section and print a fixed message. Never print the sanitizer's raw input or exception details as a fallback.

GitHub's own secret masking remains an additional safeguard. No raw stdout/stderr, process argument array, environment dump, or credential file is logged or persisted. Debug mode remains disabled/rejected. The preexisting CLI tradeoff remains: the password exists in MSDeploy's process arguments and managed memory on the isolated runner.

Static review cannot certify a general-purpose secret detector for arbitrary unlabelled server text or every possible encoding. Known local secret values and recognizable sensitive formats are explicitly handled; ambiguous sensitive lines are intentionally over-redacted. Do not enable raw output to diagnose a withheld section. Production JWT/API/database secrets are not added to GitHub for redaction or deployment.

## Exit-code interpretation

The original code already captured the real process exit code. The remediation preserves it and moves capture earlier. Every nonzero value, including a negative value, still throws and fails the Actions step. The PowerShell host may itself exit with a generic failure status after the throw; the separate `MSDeploy exit code` log line records the native value.

`Process.ExitCode` is a signed Int32 representing the terminated process's status. Thus -1 can represent the 32-bit value `0xFFFFFFFF`; it is not evidence that a PowerShell pipeline lost or converted a different status. No conversion to positive/zero is performed and no failure cause is inferred from this number. See [Microsoft Process.ExitCode documentation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.exitcode).

## Deployment behavior and TLS unchanged

The workflow file is unchanged. Both CI test gates, build/publish dependency, current-run artifact-ID handoff, digest enforcement, production approval, master restriction, and least-privilege permissions remain intact. Artifact validation and MSDeploy discovery are unchanged.

The executable, provider quoting, arguments, endpoint/site/username/password mappings, Basic authentication, AppOffline, DoNotDeleteRule, disabled extension links, and zero automatic retries are unchanged. There is no automatic rollback. No GitHub Environment or credential change occurred.

HTTPS and certificate verification remain enabled. No insecure certificate bypass was present in the inspected command; none was added. No allowUntrusted, TLS downgrade, or connectivity check is introduced. No database credentials, connections, migrations, schema changes, or seeding are introduced.

## Expected next-run output

On a native failure with both streams populated, the log has this structure (placeholders are illustrative, not a reproduced failure):

```text
MSDeploy failed.
Sanitized stdout:
| <MSDeploy messages with sensitive material replaced by ***>
Sanitized stderr:
| <MSDeploy error codes and explanations with sensitive material replaced by ***>
MSDeploy exit code: -1
Web Deploy failed with exit code -1. Content may be partial or offline; manual recovery is required.
```

Only nonempty streams receive sections. Error messages can distinguish authentication/authorization, missing sites, connectivity/timeouts, certificate trust, WMSvc availability, provider/argument configuration, file access, and offline failures when MSDeploy supplies that information. No heuristic classification substitutes for those messages. A failure still fails the job; zero still follows the existing success path.

## Manual next steps

1. The user reviews and merges this remediation into master. Codex did not commit, push, merge, or trigger a run.
2. Let GitHub run Backend / Integration Tests, Frontend Tests, and Build / Publish Artifact. All must pass.
3. Review the new run/SHA and approve its existing production environment gate only when ready. Rerunning the old commit will still use the old script.
4. If deployment fails, inspect the sanitized stdout/stderr and actual exit code in the deployment step. Retain the run/SHA and sanitized messages for diagnosis; do not publish credentials, raw process dumps, or raw output files.
5. Check production/offline state manually before deciding on recovery or another attempt. The earlier online observation does not guarantee that a later failed sync leaves the site untouched. Use the actual diagnostic to choose a targeted correction; do not rotate credentials or bypass TLS speculatively.
6. If deployment succeeds, perform the existing manual application verification. Database migration remains separate.

## Static validation and execution restrictions

PowerShell parser inspection passed without executing the script or sanitizer. Redaction ordering, quoting, process capture, and failure paths were inspected statically. Workflow and publish-script SHA-256 values remained unchanged, confirming this remediation does not edit those gates or artifact production. No runtime redaction fixtures or tests were executed; actual diagnostic behavior remains for the next user-approved run.

**CODEX RAN NO TESTS. NO deployment, MSDeploy invocation, production access, credential validation, GitHub Actions trigger, database migration, commit, or push occurred. Stop after diagnostic remediation.**
