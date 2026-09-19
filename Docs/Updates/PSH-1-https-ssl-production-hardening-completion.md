# PSH-1 — HTTPS/SSL Production Hardening Completion

**Status: COMPLETE**

**Production completion date: September 19, 2026**

The approved Release A HTTPS enforcement scope and initial Release B HSTS scope are production complete. Future HTTPS/HSTS strengthening is outside this completion. This record consolidates the supplied production evidence and repository history; no new production probes were performed while authoring it.

## Original problem

Production HTTP could serve the application without canonical server-side HTTPS enforcement. HTTPS initially failed because the production certificate was not installed, and HSTS was absent. Production-default CORS retained development-origin concerns, documentation assumed unverified HTTPS enforcement, and deployment needed a durable source-controlled policy and automated acceptance coverage. The analysis did not establish credential theft or compromise.

## Release A: HTTPS enforcement and transport hardening

Release A completed production verification on **September 18, 2026**. The operator installed the production certificate through SmarterASP.NET during the prerequisite HTTPS verification; certificate installation was not an application artifact change.

The source-controlled root IIS URL Rewrite policy became the HTTP enforcement authority:

- HTTP API requests receive **403**, rather than redirects.
- Other unsafe HTTP methods receive **403**, rather than redirects.
- Remaining HTTP GET/HEAD navigation receives **301** to canonical `https://myvocabularybuilder.org`, using the original path/query policy.

Development-only CORS retains local Angular access; Production no longer grants the development localhost origins. Publish/artifact validation and post-deployment HTTPS acceptance were added while preserving portable framework-dependent ANCM hosting through `dotnet` and the application DLL. Forwarded headers were deliberately not introduced, and SmarterASP.NET **1-Click Force HTTPS remains disabled**.

Direct production evidence established server-side HTTP `/login` 301, tested path/query preservation, HTTP API lookup 403, and HTTP POST with `Content-Length: 0` returning 403 without redirect. Chrome's browser-local `HttpsUpgrades` 307 was distinguished from the actual IIS 301. These observations cover the recorded requests, not every possible route or encoding.

Authenticated HTTPS login and word lookup succeeded; add-to-vocabulary and subsequent My Vocabulary retrieval succeeded. The observed **27 words** was historical smoke-test evidence, not a permanent application invariant. See the [detailed Release A production record](PSH-1-https-ssl-production-hardening-implementation-plan.md#18-release-a-production-completion).

## Release B: initial application-managed HSTS

Release B completed production verification on **September 19, 2026**. Standard ASP.NET Core `AddHsts` / `UseHsts` middleware applies the following policy to HTTPS responses in Production under the approved canonical-host policy for `myvocabularybuilder.org`:

```http
Strict-Transport-Security: max-age=300
```

The configuration is `MaxAge = TimeSpan.FromSeconds(300)`, `IncludeSubDomains = false`, and `Preload = false`. Middleware precedes Swagger, static files and authentication/authorization. Development, Testing, Staging, HTTP and unsupported hosts receive no application HSTS. Local HTTP development remains usable.

The five-minute duration is intentional for the initial rollout, not a permanent long-term policy. HSTS adds browser transport policy after successful HTTPS; it does not replace Release A's IIS control of initial HTTP redirects/rejections.

After workflow #60 deployed Release B, an independent PowerShell/curl check was performed:

```powershell
curl.exe -I https://myvocabularybuilder.org/login
```

The observed response included:

```http
HTTP/1.1 200 OK
Strict-Transport-Security: max-age=300
Server: Microsoft-IIS/10.0
```

HSTS was present with exactly 300 seconds; `includeSubDomains` and `preload` were absent. This directly verifies the tested HTTPS login HEAD response. Subsequent authenticated smoke testing successfully covered HTTPS application access, login, word search/lookup, My Vocabulary access and add-to-vocabulary. It was not exhaustive production testing. See the [detailed Release B production record](PSH-1-https-ssl-production-hardening-implementation-plan.md#20-release-b-production-completion).

## Validation and release traceability

Both deployment workflows were triggered by pushes to `master`, received production approval before deployment, and finished with **Success**. Each completed all four jobs successfully: **Backend / Integration Tests**, **Frontend Tests**, **Build / Publish Artifact**, and **Deploy to SmarterASP.NET**.

| Release | Implementation commit | Merge / deployed commit | Deployment workflow | Completion documentation commit |
|---|---|---|---|---|
| A | `745b490` — Implement PSH-1 HTTPS production hardening Release A | `1fdd3f5` — Merge PSH-1 HTTPS production hardening Release A | Merge PSH-1 HTTPS production hardening Release A **#58** | `a742306` — Document PSH-1 Release A production completion |
| B | `4e05588` — Implement PSH-1 Release B HSTS hardening | `8cb55ce` — Merge PSH-1 Release B HSTS hardening | Merge PSH-1 Release B HSTS hardening **#60** | `5cfa58e` — Document PSH-1 Release B production completion |

At authorship, `master` was clean at `5cfa58e`, one local documentation commit ahead of the local `origin/master` reference at `8cb55ce`. **`5cfa58e` had not yet been pushed**, as supplied by the user and consistent with those refs. No fetch or remote verification was performed. It is a local closeout documentation commit, not the deployed Release B revision.

Release B local validation, separate from workflow job-level evidence:

| Check | Result |
|---|---|
| Release build | Passed; zero warnings/errors |
| Complete backend suite | 196 passed |
| Offline script checks | 279 passed |
| Publish/artifact checks | 282 passed; published IIS policy retained and rewrite-removal mutation rejected |
| Angular production build | Passed |
| `git diff --check` | Passed |

Detailed Release A and B local validation remains in [implementation-plan sections 17–19](PSH-1-https-ssl-production-hardening-implementation-plan.md#17-release-a-implementation-record). Workflow success is not presented as independently observed per-probe logs or new CI test counts. No artifact ID/digest, exact verification time or workflow URL is invented.

## Final production posture and deliberate exclusions

The production certificate and HTTPS are operational. Source-controlled IIS enforcement upgrades safe navigation and rejects insecure API/unsafe HTTP requests under Release A. Production excludes Development localhost CORS grants. Application-managed, Production/canonical-host HSTS is active with **max-age=300 only**. CI/CD validates artifacts, uses production approval, and includes post-deployment HTTPS/HSTS acceptance checks.

PSH-1 did not change authentication/JWT behavior, database schema, EF migrations, WordsAPI behavior or Angular application functionality. It did not add forwarded headers, replace IIS enforcement with `UseHttpsRedirection`, enable SmarterASP.NET 1-Click Force HTTPS, enable `includeSubDomains`/`preload`, or adopt long-lived HSTS.

## Recovery, limitations and future considerations

- The short initial policy limits browser persistence risk. Removing middleware does not clear cached HSTS; clients may retain it for 300 seconds after their last received header. Recovery must preserve working HTTPS and certificates.
- Application-managed HSTS does not guarantee headers on IIS-generated/offline responses outside the ASP.NET Core pipeline. Automated anonymous API 401 acceptance proves the transport/authentication boundary; authenticated functionality is separately supported by smoke tests.
- Acceptance failure after deployment fails the job but does not imply automatic rollback. The established manual recovery procedure remains applicable.
- The existing Angular SCSS budget warning is unrelated to PSH-1. Publish reported NU1900 because NuGet vulnerability metadata was inaccessible; a fresh vulnerability audit was not completed.
- Any future max-age increase requires a separate deliberate decision, observation, review and rollout with certificate/HTTPS health confirmed. Subdomain coverage and preload require separate explicit decisions. None is scheduled or authorized by this record.

## References

- [PSH-1 historical analysis](PSH-1-https-ssl-production-hardening-analysis.md)
- [PSH-1 implementation plan and detailed completion evidence](PSH-1-https-ssl-production-hardening-implementation-plan.md)
- [SmarterASP.NET deployment guide](../Deployment/SmarterASP-Manual-Deployment.md)
- [Application documentation](../README.md)

This task created only this completion document. No existing file was modified; nothing was staged, committed, pushed, merged, deployed or changed in production, and no live production probe was performed.
