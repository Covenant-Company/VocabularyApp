# PSH-1 — HTTPS/SSL Production Hardening Implementation Plan

Date: 2026-09-18. Planning only. Authority: [completed PSH-1 analysis](PSH-1-https-ssl-production-hardening-analysis.md), read in full and reconciled against the current repository. No factual contradiction requiring an analysis amendment was found.

This document specifies future implementation and rollout. It does not authorize or record implementation, production changes, a deployment, or Git operations. Only this plan document was created during this task.

## 1. Decisions and verified baseline

The certificate for **myvocabularybuilder.org** is installed and HTTPS login, Angular/API lookup and authenticated vocabulary addition have been manually verified. HTTP login still serves the application; Force HTTPS has no rules and its 1-Click feature has not been enabled. HSTS was absent on the tested HTTPS 304 response. Production is same-origin, and no extra proxy boundary requiring forwarded headers is established.

Implement one IIS transport policy in the source-controlled root WebApi web.config. Reject insecure API requests and unsafe HTTP methods; redirect browser GET/HEAD navigation to the fixed canonical HTTPS origin. Preserve portable framework-dependent publishing, ANCM V2 in-process hosting and existing application contracts.

Use **two production releases**:

- **Release A:** IIS enforcement, production CORS separation, artifact validation, and acceptance automation. HSTS remains absent from application code.
- **Release B:** only after A's live enforcement is accepted, add application HSTS with **MaxAge = five minutes (300 seconds)** and require it in acceptance. This release also proves the IIS rule survives another normal deployment.

This avoids claiming that pipeline check ordering prevents users receiving HSTS: middleware starts serving headers as soon as deployed. No runtime activation switch or new production configuration secret is needed. Prepare both changes locally if useful, but do not merge/deploy B before A acceptance.

**SmarterASP.NET 1-Click Force HTTPS must remain disabled throughout implementation, rollout and recovery.** Do not add UseHttpsRedirection, host HSTS, or another competing redirect mechanism.

## 2. Exact IIS policy design

Create **VocabularyApp.WebApi/web.config**, a complete Web SDK-compatible root configuration. Use configuration/location with path="." and inheritInChildApplications="false"; place rewrite/rules inside that location's system.webServer, as a sibling of handlers and aspNetCore. Preserve the ANCM handler (path="*", verb="*", modules="AspNetCoreModuleV2", resourceType="Unspecified") and aspNetCore processPath="dotnet", arguments=".\VocabularyApp.WebApi.dll", hostingModel="inprocess", stdoutLogEnabled="false". Do not include environment variables, secrets, certificates or an Angular SPA rewrite.

Define the following ordered local rules. This is a design specification, not an installed rule:

| Order / rule name | Match and conditions | Action |
|---|---|---|
| 1 — PSH1-Reject-InsecureApi | URL pattern ^api(?:/|$), case-insensitive; HTTPS matches ^OFF$ case-insensitively | CustomResponse 403 / Forbidden, fixed nonsensitive description “HTTPS required”; stopProcessing=true |
| 2 — PSH1-Reject-InsecureMethods | URL pattern .*; HTTPS off; REQUEST_METHOD does **not** match ^(?:GET|HEAD)$ | Same 403; stopProcessing=true |
| 3 — PSH1-Redirect-To-Canonical-Https | URL pattern .*; HTTPS off; REQUEST_METHOD matches ^(?:GET|HEAD)$ | Redirect to https://myvocabularybuilder.org{UNENCODED_URL}; redirectType=Permanent (301); appendQueryString=false; stopProcessing=true |

The root application is served at / publicly, as verified by /login and /api. A hosting storage folder called /vocabularyapp does not change these match paths.

UNENCODED_URL supplies the original path/query request target; explicitly disabling automatic query appending avoids duplication. Do not use a decoded rule back-reference as a substitute without demonstrating preservation. Fix the authority as myvocabularybuilder.org: never construct it from arbitrary HTTP_HOST or redirect to the uncertified temporary name. Test original escaping, repeated query keys, %20, %2F in a query value, %26 and %25; do not change global IIS escaping/request-filtering settings to make a failing test pass. Fragments are not transmitted to IIS and are outside server-side preservation.

Choose 301 for safe navigation because HTTPS is the permanent destination and the provider documents permanent redirects. The preceding rejections prevent replay or POST-to-GET behavior on sensitive requests. IIS URL Rewrite does not support 308 as its native Redirect action. A redirect is a response, not a proxy forward; HTTPS requests fail the off condition and continue to ANCM. Rule ordering, original URL variables and query behavior follow the [Microsoft URL Rewrite reference](https://learn.microsoft.com/en-us/iis/extensions/url-rewrite-module/url-rewrite-module-configuration-reference).

Raw URL encoding can vary with installed Rewrite behavior; validate the exact outgoing Location on IIS, not just the XML. Do not promise byte preservation from source inspection alone. [IIS team's encoding compatibility discussion](https://blogs.iis.net/iisteam/url-rewrite-v2-1/).

No file/directory exclusion: HTTP assets, login and Swagger navigation must upgrade too. No wildcard ACME bypass is proposed. Before deploying, confirm whether the provider's certificate renewal requires a locally served HTTP challenge. If so, obtain the exact provider requirement and review a narrowly scoped challenge exception; never let it fall through to the functional SPA over HTTP.

Provider Force HTTPS documentation is sufficient evidence to select URL Rewrite for implementation. Module availability, section delegation/locks and actual encoding behavior remain rollout checks. Inspect parent/global rules; do not insert clear/remove directives that erase unknown provider rules. Unexpected inherited redirection is a stop condition for deployment review, not a reason to enable 1-Click.

The source IIS configuration is intended for production publishing. The documented local workflow uses dotnet run/Kestrel and Angular localhost:4200; Kestrel does not execute IIS XML, so local HTTP remains available. Do not use the production publish package as an unmodified localhost IIS deployment. No local IIS hosting workflow is currently established that needs a second policy.

## 3. Published web.config lifecycle and mandatory checks

Current evidence: no WebApi source web.config exists; the Web SDK generates it. Publish-VocabularyApp.ps1 clears RuntimeIdentifier, uses UseAppHost=false and framework-dependent publish, validates root ANCM configuration, strips development settings and rejects nested web.config. The workflow stages Angular excluding its public/web.config before publishing.

Planned lifecycle:

1. Checkout includes the new WebApi root web.config with the rules and ANCM configuration.
2. Existing dotnet publish processes that file using the Web SDK. Keep SDK transformation enabled; no project-property or publish-profile change is planned.
3. Inspect the **actual output** at publish-directory/web.config after publish. Do not rely on a source-file test or assume transformations preserved custom elements.
4. Existing upload preserves the validated artifact; artifact ID/digest binds download to that build.
5. Revalidate downloaded root web.config immediately before MSDeploy.
6. Existing contentPath synchronization copies the complete root file to the application destination. IIS then interprets its rules before managed request handling.

Create scripts/ci/Assert-Psh1WebConfig.ps1 with a reusable assertion function accepting the actual XML path. Dot-source it from Publish-VocabularyApp.ps1 and Deploy-SmarterAsp.ps1 using PSScriptRoot. Keep it in scripts/ci, not the application artifact; the existing deployment sparse checkout already includes that directory.

Assertions must parse XML and fail on missing, disabled, reordered, duplicated or altered PSH1 rules; verify conditions, method restrictions, literal HTTPS authority, original-target substitution, appendQueryString=false, 301 and 403 actions, stopProcessing, and the expected location. Require exactly the planned three local rules and one redirect action in this owned configuration; any new rule requires explicit review. Validate ANCM V2/dotnet/DLL/inprocess, and forbid secrets/environment-variable content and competing redirect/HSTS configuration in the artifact. Retain existing portable-runtime, nested-config and file-exclusion checks.

Failure must throw before publishing the artifact path/upload or before starting MSDeploy, respectively. Tests must prove that deletion of the rewrite section **from generated output** fails validation. Compare the downloaded policy structurally, not by searching for a rule-name substring.

DoNotDeleteRule preserves files absent from the artifact; it does not merge edits to a root file present in both locations. The rule now travels in that root file every deployment. External IIS/parent configuration is not directly synchronized. Where 1-Click would write remains unknown and irrelevant while disabled.

## 4. HSTS implementation — Release B

Exact application changes in Program.cs:

- Register AddHsts with MaxAge=TimeSpan.FromMinutes(5), IncludeSubDomains=false and Preload=false.
- After builder.Build(), before Swagger, default/static files, CORS, authentication and authorization, install HSTS only when app.Environment.IsProduction().
- Within that production pipeline use a rejoining UseWhen branch whose predicate matches Request.Host.Host to myvocabularybuilder.org case-insensitively; invoke UseHsts inside it. Let the HSTS middleware check Request.IsHttps.
- Keep built-in localhost exclusions. Do not clear them. Do not broaden the host match to arbitrary subdomains or the provider temporary host.
- Remove the obsolete commented UseHttpsRedirection line or replace it with a concise comment identifying the IIS owner; do not activate it.

Final order: host-scoped production HSTS → existing Swagger/UI → default/static files → Development-only CORS → authentication → authorization → controllers/fallback. HSTS sets a browser policy on HTTPS responses; IIS independently upgrades HTTP navigation before the app. They are different responsibilities, not duplicate redirect mechanisms.

Expected header on qualifying responses: **Strict-Transport-Security: max-age=300**, without includeSubDomains or preload. No HSTS in Development, Testing, Staging, on HTTP, or on unsupported hostnames. App-generated static responses and auth challenges should include it when eligible; IIS-generated errors/offline pages are outside application middleware coverage.

Five minutes limits the initial browser commitment while providing a measurable policy. After at least 48 hours of stable HTTPS, a successful subsequent deployment, validated renewal ownership and a recovery rehearsal, a separately reviewed change may increase to one day, then later longer periods following observation and renewal verification. No automatic duration escalation or preload enrollment is part of PSH-1.

HSTS only affects clients that learn/support it; removing middleware does not erase cached policy. [Microsoft HTTPS/HSTS guidance](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-8.0).

## 5. Minimal production CORS separation

Use **Development-only policy registration and middleware**, retaining configuration-driven exact origins inside Development. Production has no cross-origin client requirement, so do not register/use AllowAngular outside Development.

In Program.cs, move the existing origin binding/normalization and AddCors policy inside builder.Environment.IsDevelopment(). Remove the hard-coded localhost fallback. Use the configured nonempty development list (or fail clearly in Development if it is missing), with the existing AllowAnyHeader/AllowAnyMethod, no AllowCredentials and no wildcard origin. Gate UseCors("AllowAngular") with the same Development condition.

Remove the Cors section from **appsettings.json**. Keep **appsettings.Development.json unchanged**, retaining:

- http://localhost:4200
- https://localhost:4200

This is more robust than setting an empty production array: configuration providers can merge array entries, and the existing fallback can restore localhost origins. Ignoring CORS configuration entirely outside Development prevents stale Cors__AllowedOrigins overrides from re-enabling a production policy. Confirm the deployed environment is Production before approval.

The same-origin browser does not require Access-Control-Allow-Origin. No new entry for https://myvocabularybuilder.org is needed. CORS is not authentication or a server-side ban on all cross-origin requests; removing grants does not replace JWT protection.

Update the reviewed base-settings SHA-256 digest in Publish-VocabularyApp.ps1 using its existing CRLF normalization/Trim/UTF-8 algorithm after reviewing the sole intended settings delta. Retain digest enforcement on both source and output.

Existing integration requests do not need cross-origin browser permission. Their Testing environment should continue without CORS. Do not alter API authorization to accommodate tests.

## 6. Forwarded headers and explicit non-changes

**Forwarded headers — No change.** Do not add UseForwardedHeaders, ForwardedHeadersOptions, KnownProxies/KnownNetworks or an enabling environment switch. In-process IIS obtains native scheme information; no additional offload proxy is established. Arbitrary forwarded headers could make client-controlled input influence transport decisions.

Validate HTTPS does not redirect and HSTS appears. If evidence establishes a scheme mismatch, stop and investigate topology; do not “fix” a loop by trusting every X-Forwarded-Proto value.

Keep Angular /api URLs, routes, JWT validation/format, password/auth contracts, WordsAPI, pronunciation, quiz, UserWord/R5 behavior, database schema and migrations unchanged. No new public health endpoint or authentication exception is needed for this plan.

## 7. CI/CD acceptance script and exact probe contract

Create **scripts/ci/Test-ProductionHttps.ps1**. The production entry point targets the fixed canonical origin only. Use .NET HttpClient with automatic redirects disabled, normal certificate validation, no cookies/default credentials/auth headers and no bearer token. Bound response size and dispose responses/clients.

Use fixed labels and expected values for reporting. Never dump response bodies, all headers, raw exception text, or arbitrary Location URLs. Report status, failed check label, retry count and a sanitized failure category. Verify headers internally; do not print Authorization, cookies, tokens, API keys, deployment variables or credentials.

Probes:

| Probe | Required result |
|---|---|
| Fresh HTTPS GET /login | 200, text/html, expected app-root marker; no redirect/certificate failure |
| HTTP GET /login, redirects disabled | Exactly 301; Location exactly https://myvocabularybuilder.org/login |
| HTTP GET /login?psh1=one&psh1=two&encoded=a%2Fb%20c%26d%25 | Exactly 301; HTTPS canonical authority, identical path and query without loss/duplication |
| HTTP GET /login/psh1%20path?value=a%25b | Exactly 301 with the escaped path and query preserved; validate Location without asking Angular to interpret this synthetic route |
| Redirect-chain traversal of the above | Inspect each Location before requesting; only canonical HTTPS after the initial HTTP request, no user-info/alternate port/host; maximum five hops; final 200 expected HTML |
| HTTP GET /api/users/profile | 403, no redirect or sensitive application data |
| HTTP POST /api/users/login with empty body, no credentials | 403; no redirect; no login business action should execute |
| HTTPS GET /api/users/profile without credentials | Exactly 401, WWW-Authenticate has Bearer scheme, no redirect, no SPA HTML |
| Representative same-origin JS asset discovered from accepted HTML | HTTPS 200, expected asset type; HTTP counterpart upgrades; never fetch an external URL from HTML |
| HSTS on /login, the HTTPS API 401 and the JS asset | Release B: one policy with max-age=300 and no includeSubDomains/preload; Release A: record not required, do not treat absence as failure |

Send Cache-Control: no-cache and no If-None-Match/If-Modified-Since; treat an unexpected 304 as a failed fresh-content check. Test HTTPS CORS denial using a localhost Origin on the safe profile request: no Access-Control-Allow-Origin and no credentials grant. Same-origin behavior itself must remain functional.

**API choice is deliberate:** no existing anonymous deterministic successful GET API was found. UsersController.profile is explicitly protected; an unauthenticated request is challenged before database/service execution. The automated expectation is **401 authentication-boundary success**, not a claim of functional authenticated 200. Do not call register/login with real credentials, add a test identity, or make an endpoint anonymous. Words lookup is protected by the fallback policy and may call WordsAPI/persist dictionary data if authenticated, so it is unsuitable for this credential-free deterministic gate. Manual authenticated lookup supplies separate 200 functional evidence.

Allow at most six readiness attempts, five seconds apart, with ten-second per-request timeouts, then run the full contract. Retry only transient startup/network availability or 502/503; fail immediately for certificate validation failures and deterministic redirect/HSTS mismatches once the app is ready. Set a five-minute step timeout. No infinite loop or unbounded retry. A cached browser 301/HSTS upgrade cannot mask HTTP results because HttpClient has no browser state.

Structure the script so dot-sourcing defines functions without network activity. Separate request execution from response assertions; offline tests supply scripted responses without broadening the production target list.

## 8. Workflow placement and failure semantics

Modify **.github/workflows/backend-tests.yml**:

- Retain existing backend/frontend tests and build dependencies.
- Run the new offline PowerShell tests before publishing/uploading.
- Keep the existing validated artifact ID/digest, production environment, master-only deployment guard, AppOffline and noncanceling concurrency.
- Add the production acceptance step **immediately after Deploy-SmarterAsp.ps1 succeeds**, within deploy-production and the same concurrency group.
- Give this step no MSDeploy secret environment block. Existing sparse checkout of scripts/ci provides the script.
- Release A invokes the transitional script without RequireHsts; Release B changes the call to **-RequireHsts**. Document the former as incomplete PSH-1 acceptance, not a permanent opt-out. Final workflow must require HSTS.
- No continue-on-error, unconditional success override or automatic rollback.

| Situation | Outcome |
|---|---|
| Tests/artifact validation fail | Pre-deployment failure; do not upload/deploy invalid package |
| MSDeploy fails | Existing deployment failure handling; no acceptance claim; investigate partial/offline state |
| MSDeploy succeeds, HTTPS works, HTTP still returns application 200 | Acceptance step throws; deployment job fails; production content already changed |
| Redirect works, HSTS missing in Release B | Acceptance fails; do not report PSH-1 complete |
| Network timeout after bounded startup retry | Acceptance fails as availability/verification failure; operator distinguishes transient outage from configuration failure |
| Automated probes pass | Transport accepted for that release; manual authenticated checks still required |

Post-deployment checks cannot undo content already synchronized. A failed job must explicitly say “content synchronization succeeded; production acceptance failed; manual recovery required.” No safe automated rollback exists in the current pipeline.

Workflow currently triggers on push to master only. Do not imply a PR already runs these gates: run local/premerge validation and review evidence, then master CI repeats before the production job. No trigger change is needed for PSH-1. A named GitHub production environment does not prove required reviewers are configured; the operator must confirm environment protection/approval before either release, or withhold the master merge until equivalent release authorization is arranged.

## 9. Automated testing by layer

**Application integration tests:** create VocabularyApp.WebApi.Tests/Integration/HttpsHardeningApiTests.cs. Extend the existing sealed VocabularyAppWebApplicationFactory with an optional environmentName constructor parameter defaulting to Testing; use the value in ConfigureWebHost instead of its fixed Testing literal. Preserve every SQLite connection, fake dictionary handler, interceptor, test JWT safeguard, lifecycle and disposal behavior. Do not change global process environment per test. Assert the actual host environment in each new environment-specific test so a late callback cannot silently invalidate coverage.

Use BaseAddress https://myvocabularybuilder.org with redirects disabled; this remains TestServer traffic, not DNS/network access. Exercise the real Program pipeline using:

- Production HTTPS profile challenge: 401 plus HSTS in B.
- Production HTTP profile challenge: no HSTS; do not expect IIS 301/403 inside TestServer.
- Production unsupported host, and Development/Testing/Staging HTTPS: no HSTS.
- Production Swagger JSON response: HSTS present in B, proving ordering before Swagger.
- Production Origin localhost and temporary origin: no ACAO on challenge/preflight; configuration injection must not enable a production grant.
- Development OPTIONS preflight requesting POST/content-type/authorization: each configured localhost origin is allowed exactly; unlisted origin is not; no Access-Control-Allow-Credentials.
- Development HTTP remains usable; Production same-origin auth challenge and existing authenticated tests retain their original contracts.
- Preserve the existing test factory's configuration seam without using its integration-tests.example origin as proof of development defaults. Read/assert the two actual configured localhost origins. Keep existing tests’ default Testing behavior intact.

Do not duplicate IIS redirection in application middleware just to satisfy an integration test. TestServer does not execute web.config.

**Published-artifact validation:** shared XML validator runs on generated output and downloaded artifact. Create **scripts/ci/tests/Test-Psh1Scripts.ps1**, a dependency-free PowerShell harness that uses isolated temporary XML fixtures and throwing assertions. Include valid output plus removed rewrite, disabled/misordered rules, HTTPS condition missing, destination drift, query duplication, wrong status, duplicate redirect, and changed ANCM cases. Test all failures are actually rejected. Run validator once on real publish output, then remove its rewrite section in a temporary copy and prove rejection; never mutate the artifact destined for upload.

**Acceptance-script tests:** in the same offline harness, dot-source the script and feed synthetic responses for success, HTTP 200, wrong host, changed query, loop, TLS failure category, 401 versus misleading HTML 200, missing/duplicate HSTS, and prohibited directives. Confirm request-count/time bounds and that a deliberately sensitive fixture marker does not appear in captured diagnostics. Do not install Pester or other packages just for PSH-1.

**Existing regression suites:** run the full backend unit/service/integration suite, including authentication, ownership, dictionary fake-provider, quiz concurrency and relational safety/R5 coverage; run all existing Angular tests and production build. No new UI tests are needed for unchanged Angular code. No real SQL Server, production database, WordsAPI or migrations are used by automated regression tests.

Implementation-time commands: from repository root, run `dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj --configuration Release` and `pwsh -NoProfile -File scripts/ci/tests/Test-Psh1Scripts.ps1`; from VocabularyApp.UI, run the existing dependency restore followed by `npm test -- --watch=false --browsers=ChromeHeadless` and `npm run build -- --configuration production`. Reproduce the workflow's Angular staging and portable publish flags for artifact inspection. The CI-only publish script requires its documented workspace/temp/output variables; do not invoke Deploy-SmarterAsp.ps1 to validate a local package. These commands have not been run during planning.

## 10. File-level change map

All paths are relative to repository root. This is the complete intended implementation change set, not files changed during planning.

| File | Planned Change | Reason | Risk | Verification |
|---|---|---|---|---|
| VocabularyApp.WebApi/web.config (new) | Complete root ANCM config plus three ordered IIS transport rules | Reproducible single redirect authority | High: IIS load/encoding/loop errors | Real publish XML + IIS/live probes |
| VocabularyApp.WebApi/Program.cs | Development-only CORS; later host-scoped Production HSTS; remove misleading commented redirect | Separate environments and header owner | Medium: middleware ordering/environment | Real-pipeline tests and live checks |
| VocabularyApp.WebApi/appsettings.json | Remove base Cors section only | Remove stale production origins | Low/Medium: digest/configuration drift | Reviewed diff, digest and CORS tests |
| scripts/ci/Assert-Psh1WebConfig.ps1 (new) | Shared root XML assertions | Fail closed before upload/deploy | Medium: false acceptance/rejection | Negative fixtures and actual output |
| scripts/ci/Publish-VocabularyApp.ps1 | Call validator; update reviewed settings digest | Validate actual published policy | Medium: packaging regression | Full publish and mutation test |
| scripts/ci/Deploy-SmarterAsp.ps1 | Call validator before MSDeploy only | Reject altered downloaded policy | Low/Medium: preflight failure | Offline validator + existing deploy guards retained |
| scripts/ci/Test-ProductionHttps.ps1 (new) | Bounded credential-free live acceptance, RequireHsts phase control | Detect real transport regressions | Medium: false success/failure | Offline response tests and two releases |
| scripts/ci/tests/Test-Psh1Scripts.ps1 (new) | Offline XML/response-policy tests | Verify security checks fail on defects | Low: fixture scope | Run with pwsh; no network or dependencies |
| .github/workflows/backend-tests.yml | Offline script tests; post-MSDeploy acceptance; B requires HSTS | Enforce release acceptance | Medium: job failure semantics | Workflow review, master runs, sanitized logs |
| VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs | Optional environment selection, default Testing | Exercise actual Production/Development branches | Medium: R6 isolation regression | Full relational/auth/concurrency suites |
| VocabularyApp.WebApi.Tests/Integration/HttpsHardeningApiTests.cs (new) | Environment/HSTS/CORS tests | Protect behavior, not IIS simulation | Low | Full backend suite |
| docs/README.md | Replace unqualified HTTPS claim with actual ownership and release status | Accurate operator expectations | Low | Review against deployed evidence |
| docs/Deployment/SmarterASP-Manual-Deployment.md | Update only relevant root config, portable publish, enforcement, HSTS, probes and recovery guidance | Prevent manual overwrites/duplicate authority | Low | Match current scripts and accepted rollout |
| Docs/Updates/PSH-1-https-ssl-production-hardening-implementation-plan.md | Append sanitized A/B execution evidence during implementation | Record completion without rewriting historical analysis | Low | Artifact IDs, outcomes, timestamps; no secrets |

Inspected, important **unchanged** files/surfaces:

| File/surface | Reason to keep unchanged |
|---|---|
| appsettings.Development.json; Properties/launchSettings.json | Existing localhost origins/HTTP development are correct |
| VocabularyApp.WebApi.csproj; test csproj | Existing Web SDK, dependencies, portable overrides and test packages suffice |
| Angular environments, angular.json, auth/API services, public/web.config | Same-origin production URL is correct; Angular IIS config remains excluded |
| Controllers, JWT/security/services, Data project and migrations | No route/auth/schema/business redesign |
| Existing analysis document and historical R5/R6 records | Preserve evidence history |
| SmarterASP.NET Force HTTPS; certificate installation | Keep 1-Click disabled; certificate already installed |

## 11. Small implementation phases

Phases describe reviewable code changes; releases determine production activation.

| Phase | Exact files (from section 10) | Intended change / dependencies | Tests and completion | Rollback consideration |
|---|---|---|---|---|
| 1 — IIS source policy | WebApi/web.config | Define root policy and ANCM coexistence; baseline capture first | XML review; rule table satisfied | Keep prior root file outside served content |
| 2 — CORS separation | Program.cs, appsettings.json, Publish-VocabularyApp.ps1 digest | Development-only permission, no production defaults | Local Angular and CORS tests; digest passes | Targeted CORS revert must retain HTTPS |
| 3 — Gates and Release A acceptance | Assert-Psh1WebConfig.ps1, Publish/Deploy scripts, Test-ProductionHttps.ps1, Test-Psh1Scripts.ps1, workflow, test factory and HttpsHardeningApiTests.cs | Validate policy and automate transport checks, initially no HSTS requirement; depends on 1–2 | Full suites, real output/mutation tests, offline probes | No auto rollback; acceptance must fail clearly |
| 4 — Release A documentation/rollout | README, manual deployment guide, this plan record | Operator prerequisites and deploy A per section 12 | HTTPS/redirect/403/CORS/manual smoke pass | A becomes known-good HTTPS recovery artifact |
| 5 — HSTS and Release B gates | Program.cs, HttpsHardeningApiTests.cs, workflow, Test-Psh1Scripts.ps1 | Add 300-second HSTS; switch workflow to RequireHsts; depends on accepted A | Full suites; HSTS matrix/probes; no unsafe directives | Prefer return to A while retaining working HTTPS |
| 6 — Release B closeout | README, manual guide, this plan record | Deploy B, verify persistence, record final acceptance | All section 14 criteria; manual auth smoke | Record artifact/config provenance and recovery status |

No phase changes hosting settings during implementation without separate rollout authorization. No phase adds packages, SQL changes or migrations.

## 12. Precise production rollout sequence

1. Before code work, preserve the current source state and confirm the intended branch. Before any production deployment, securely retain the current root config and known-good artifact outside the publicly served directory. Do not expose host settings or secrets in Git.
2. Operator confirms current HTTPS login works, certificate renewal/challenge requirements, Production environment, effective parent/site rules and rewrite delegation. Confirm 1-Click remains disabled. No repeat certificate installation.
3. Implement/review phases 1–3 on a branch. Run backend/frontend tests, production Angular build, offline script tests and the existing publish sequence with portable flags. Do not invoke the deploy script locally.
4. Inspect actual publish output with the validator and mutation test; retain sanitized validation evidence. Review planned changes against the file map, including settings digest.
5. Use the repository's PR/review process; commit/push only when implementation is authorized. The current master-push workflow has production side effects: merge Release A only within the approved release process.
6. Master CI runs backend/frontend gates, build, staging, publish checks and artifact upload. Verify artifact ID/digest. Production environment approval occurs before MSDeploy; confirm required-reviewer protection actually exists rather than inferring it from YAML.
7. Approve and deploy A through existing MSDeploy. AppOffline may cause brief downtime; keep existing destination-file preservation and TLS validation.
8. Immediate acceptance: HTTPS login readiness → no-follow HTTP /login and query checks → bounded chain → HTTP API/method rejection → HTTPS profile 401 and asset → production CORS denial. No HSTS requirement yet; do not claim PSH-1 complete.
9. Operator manually logs in over HTTPS; checks lookup and a controlled existing vocabulary item, pronunciation, notes/favorite behavior and quiz/history as appropriate. Verify browser schemes/mixed-content state without recording tokens. No HTTP credentials and no automated data-changing smoke.
10. Record A results and retain its artifact/root configuration as the HTTPS-enabled recovery baseline. Resolve any failure before B. Confirm renewal and recovery readiness for HSTS.
11. Implement/review phase 5; run full gates again. Merge B only after A sign-off and production approval arrangements.
12. CI builds/validates B; production approval; MSDeploy; repeat all A checks and require HSTS on fresh HTTPS HTML/asset/API responses. This deployment must preserve the same IIS policy from A.
13. Repeat manual authenticated HTTPS smoke. Record B acceptance, artifact/revision, operator/time, HSTS duration and Force HTTPS disabled state. Update implementation status documentation; preserve the historical analysis.
14. Observe initial HTTPS stability; do not automatically lengthen max-age. Any later policy increase is separately reviewed.

No live probes, environment approvals, deployments or Git operations in this sequence were executed during planning.

## 13. Manual rollback and recovery

The existing pipeline has **no automatic rollback**. Keep HTTPS service/certificates available even when reverting application changes. Pause further release approvals while investigating; do not repeatedly deploy unknown artifacts.

| Failure | Diagnosis and recovery |
|---|---|
| Rewrite causes IIS configuration failure (for example 500.19) | Check sanitized IIS error/module/section details. Operator uses secure hosting file management to restore the captured prior root web.config exactly, preserving its ANCM settings. Do not leave malformed partial XML |
| Restoring pre-A config reopens HTTP | This is emergency service restoration, not secure completion. Keep application unavailable/maintenance-protected from credential entry until a corrected source-owned redirect/rejection policy is installed. Retest raw HTTP before reopening; never silently accept the old insecure baseline |
| Redirect loop | Compare effective IIS rules/scheme and confirm duplicate authority is absent. Restore known-good A config if available; otherwise use prior config with the above maintenance precaution. Do not enable Force HTTPS or arbitrary forwarded headers. Re-test direct HTTPS and raw HTTP before reopening |
| HSTS issue | Preserve certificate and HTTPS. Redeploy A for app regression if appropriate; existing clients can retain B's policy for up to 300 seconds since their last header. To actively clear it, a reviewed HTTPS-served max-age=0 response is required; absence of the header alone does not clear it. Restore HTTPS first if TLS itself fails |
| CORS regression | Confirm it is actually cross-origin (same-origin does not need CORS). Fix the Development condition/config or restore only the prior CORS behavior in a reviewed build, leaving IIS/HSTS intact. A temporary restoration of insecure origin grants must be explicitly recorded and promptly corrected |
| Acceptance fails after sync | Inspect only sanitized status/headers and provider diagnostics. Fix forward for a small understood error; redeploy known-good A for B regressions; restore root config for isolated IIS faults. Verify artifact compatibility and offline handling, and repeat all acceptance checks |
| Certificate renewal/expiry problem | Use existing host support/renewal arrangements; do not bypass TLS validation or add repository keys. HSTS clients cannot be recovered by offering HTTP |

Do not blindly rerun the current deploy script against a pre-PSH artifact: the new validator should reject its missing policy. A pre-A emergency restoration requires an explicitly reviewed manual recovery process and immediate enforcement restoration. A full old artifact may also overwrite root config; retain/reapply the known-good policy as a reviewed recovery package where compatible. DoNotDeleteRule is not a backup or rollback mechanism.

No migration rollback is needed: there are no database changes. Do not delete existing destination files/old artifacts as a side effect of PSH-1 recovery.

## 14. Objective completion criteria

All must pass before final PSH-1 sign-off:

1. Trusted HTTPS /login returns the expected application successfully.
2. Raw HTTP /login returns 301 to https://myvocabularybuilder.org/login.
3. Original path, repeated query keys and tested escaping are preserved without duplication.
4. HTTPS requests do not re-redirect; bounded traversal finds no loop/downgrade/off-origin hop.
5. Production HTTP no longer serves functional application content: safe navigation upgrades, APIs and unsafe methods reject before business execution.
6. Eligible Production HTTPS responses have exactly max-age=300 with no includeSubDomains/preload; no HSTS on Development/Testing/unsupported hosts.
7. Development HTTP and both configured localhost Angular origins remain usable.
8. Production has no development-origin CORS grants or wildcard/credential grants, including stale external origin overrides.
9. Same-origin Angular/API HTTPS works; manual authenticated lookup and normal vocabulary/quiz/pronunciation behavior pass.
10. Actual published and downloaded root web.config pass structural policy/ANCM checks; missing rewrite reliably fails validation.
11. Policy survives Release B's normal MSDeploy synchronization.
12. CI runs mandatory post-deployment transport acceptance and final RequireHsts checks; failure marks the deployment unaccepted.
13. Automated profile 401 is accurately labeled a transport/auth-boundary check; manual authenticated functional success is separately recorded.
14. Full existing backend and frontend suites, new targeted tests and production build pass; R6 SQLite/fake-provider isolation remains intact.
15. Production smoke and manual authenticated acceptance pass with no secret values logged.
16. SmarterASP.NET 1-Click Force HTTPS remains disabled; no competing app/host redirect or HSTS owner is introduced.
17. Certificate renewal/required challenge behavior is confirmed; no validation bypass or certificate material enters source/artifacts.
18. No API/Angular/JWT contracts, database schema/migrations, WordsAPI or R5 behavior changes.
19. Documentation identifies owners, deployment persistence, initial HSTS policy, accepted artifacts and manual recovery procedure.
20. Release B is accepted; Release A alone is not PSH-1 completion.

## 15. External prerequisites, scope boundaries and references

Remaining manual prerequisites constrain **deployment**, not starting implementation: effective IIS config/delegation and encoding checks, renewal/challenge requirements, production environment selection, GitHub approval protection, secure backup/artifact access and manual authenticated smoke. No unsupported assumption that a missing detail is already verified should be recorded as fact.

No need to discover where the inactive 1-Click feature would write before coding. If rewrite support, HTTPS scheme or renewal conflicts appear during validation, hold release and review that concrete issue. Do not implement a speculative alternate authority in advance.

**Out-of-scope follow-up:** JWT storage, CSP/other headers, server-header removal, rate limiting, auth redesign, secret rotation, new certificate automation, CDN/proxy adoption, SQL transport redesign and unrelated documentation overhaul. None is necessary for the specified HTTPS policy.

Repository evidence re-inspected includes Program/settings, WebApi/test project files, the R6 factory, controllers and fallback auth, publish/deploy scripts, the sole workflow, Angular index/environment behavior, and relevant README/manual deployment documentation. The analysis was read in full. Framework references above support IIS/HSTS mechanics; provider capabilities derive from the user's accepted verification record, not a claim that the assistant changed or independently audited the hosting account.

## 16. Implementation readiness

The mechanism, file set, regression tests, artifact safeguards, two-release activation order, production probes and recovery responsibilities are specified. Unknown provider implementation details do not prevent safe local implementation; they are explicit release checks. This plan does not authorize execution.

Only this implementation-plan document was created. The analysis and all application/configuration/deployment files remain unchanged; no production settings, commit or push were performed.

**READY FOR PSH-1 IMPLEMENTATION**

## 17. Release A implementation record

September 18, 2026: **Release A implemented and locally validated; ready for review, not deployed.** Sections 1–16 retain the approved planning record. Release B and full PSH-1 production completion remain pending.

### Implemented files and behavior

| File | Release A change |
|---|---|
| `VocabularyApp.WebApi/web.config` | Source-owned IIS policy and portable ANCM configuration |
| `VocabularyApp.WebApi/Program.cs` | Register and apply CORS only in Development; remove fallback origins |
| `VocabularyApp.WebApi/appsettings.json` | Remove shared CORS origins; existing Development configuration retained |
| `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs` | Environment selection and independence from staged static assets; retain R6 SQLite/fake-provider isolation |
| `VocabularyApp.WebApi.Tests/Integration/HttpsHardeningApiTests.cs` | Eleven CORS, authentication-boundary and HSTS-deferral cases |
| `scripts/ci/Assert-Psh1WebConfig.ps1` | Strict XML policy/ANCM validator with sanitized failures |
| `scripts/ci/Publish-VocabularyApp.ps1` | Validate actual output and update reviewed settings digest |
| `scripts/ci/Deploy-SmarterAsp.ps1` | Validate downloaded root policy before MSDeploy |
| `scripts/ci/Test-ProductionHttps.ps1` | Bounded, credential-free Release A acceptance |
| `scripts/ci/tests/Test-Psh1Scripts.ps1` | Offline acceptance fixtures and source/published XML mutation checks |
| `.github/workflows/backend-tests.yml` | Offline and actual-artifact gates; mandatory acceptance after successful deployment |
| `Docs/README.md` | Accurate pending-deployment status and CORS architecture |
| `Docs/Deployment/SmarterASP-Manual-Deployment.md` | Source policy, portable publish, deployment and recovery guidance |
| This implementation plan | Release A completion evidence and remaining gates |

The first IIS rule rejects HTTP `/api` and `/api/...` requests with 403. The second rejects other HTTP methods except GET/HEAD with 403. The third returns 301 for remaining HTTP GET/HEAD requests to the fixed canonical `https://myvocabularybuilder.org` host, preserving the original path/query through `{UNENCODED_URL}` with query appending disabled. Every rule stops processing; HTTPS does not match. Root IIS configuration is the sole transport-enforcement authority. SmarterASP.NET 1-Click Force HTTPS must remain disabled.

ANCM retains `processPath="dotnet"`, `arguments=".\VocabularyApp.WebApi.dll"`, in-process hosting and existing stdout settings. No app redirect authority, forwarded-header middleware/trust configuration, HSTS middleware/header/IIS policy, database migration, route/authentication contract or business behavior change was introduced. The future 300-second HSTS policy is not active.

Only Development grants the two configured localhost Angular origins; no wildcard or credential grant was added. Production, Testing and Staging do not activate the policy, even with stale origin configuration. The test factory retains its isolated database, controllable dictionary provider and existing persistence interception.

### Validation evidence

| Verification | Result |
|---|---|
| `dotnet restore VocabularyApp.sln` | Passed |
| `dotnet build VocabularyApp.sln -c Release --no-restore` | Passed, 0 warnings, 0 errors |
| Full backend/integration suite | **183 passed, 0 failed, 0 skipped** (172 existing plus 11 new cases) |
| Focused new integration cases | 11 passed, 0 failed, 0 skipped |
| CI-equivalent ChromeHeadless frontend suite | **34 passed, 0 failed, 0 skipped** |
| Angular production build | Passed; existing word-lookup SCSS budget warning, 2.80 kB against 2.05 kB |
| Offline script suite without artifact | 114 checks passed, 0 failed; Windows PowerShell and PowerShell 7 |
| CI publish script, portable framework-dependent output | Passed from clean temporary source snapshot with actual production Angular assets |
| Actual published root configuration and offline suite | **117 checks passed, 0 failed**; actual file accepted and a temporary copy with rewrite removed rejected |
| Final whitespace review | `git diff --check` passed |

The first Development test run exposed a stale local static-assets manifest pointing to absent staged Angular content. The API fixture now deliberately avoids that manifest; all final application tests passed. The first local publish correctly rejected an ignored `Archive/publish` tree swept into output by the Web SDK. The successful publish used a clean temporary source snapshot corresponding to checkout contents and the actual built Angular assets. Existing package safeguards were not weakened, and the ignored archive was not changed. Generated workspace staging files were subsequently hash-checked against Angular output and removed.

The clean publish emitted NU1900 because the sandbox could not reach the NuGet vulnerability-data service; cached dependency restoration and publication succeeded. This is not a successful fresh vulnerability audit. PowerShell 7.4.6 was obtained as an official portable archive with its published SHA-256 verified; no system installation was required.

The acceptance fixtures cover success without HSTS, TLS failure without retry, bounded transient readiness retries, unexpected HTTP success, wrong-host/query/encoding redirects, insecure API redirects, API HTML responses, production CORS grants, cached responses, external asset references, loops, excessive hops and unsafe redirect targets. XML mutations cover missing/reordered/disabled rules, rejection/redirect conditions, duplicate rules and damaged ANCM values. Sanitization fixtures ensure sensitive marker values in exceptions/configuration/redirect targets do not reach returned failure diagnostics. The live client uses normal TLS validation, no cookies/default credentials, no automatic redirects, ten-second request timeouts and bounded response buffering. It prints fixed check labels and allowlisted categories, not response bodies, raw exceptions, authorization data or query URLs. Existing deployment sanitization and secret scoping remain intact.

### CI semantics and limits

Backend/frontend/build gates, artifact upload/download integrity, the production environment declaration, deployment concurrency, MSDeploy options and existing diagnostics remain intact. Publish validates its actual root XML; deployment validates the downloaded file again. Acceptance runs only after successful MSDeploy, in the same deployment job. A failure explicitly reports that deployment completed but production acceptance failed, fails the job, and requires manual recovery. There is no automatic rollback.

No production acceptance request, deployment, provider-setting change, certificate action, database action, commit, push, merge or PR occurred. SmarterASP.NET 1-Click was untouched. No claim is made that these IIS rules have executed successfully on the provider. Offline fixtures and TestServer do not prove IIS module/delegation support, escaping behavior, certificate renewal compatibility or authenticated production functionality. The automated HTTPS API probe proves only the anonymous Bearer 401 boundary and does not depend on WordsAPI; authenticated functional smoke remains manual. Repository environment declarations do not prove GitHub required-reviewer settings.

### Remaining production checklist and Release B gate

1. Review these uncommitted changes; commit/push through the normal authorized review process. Preserve the current production root configuration and known-good artifact securely outside served content.
2. Confirm valid HTTPS, renewal/challenge requirements, Production environment, effective parent/site rules and URL Rewrite delegation. Keep 1-Click disabled. Verify production environment approval protection.
3. Run normal CI gates and inspect the validated artifact identity/digest. Obtain production approval, then allow the existing MSDeploy flow to deploy Release A.
4. Require successful HTTPS login, exact HTTP 301 host/path/query preservation (including repeated keys/escaping), bounded traversal without loops, safe HTTP API/empty-method 403 probes, HTTPS asset/API checks and denial of development CORS grants.
5. Complete manual Angular/API and authenticated HTTPS smoke, including normal lookup, vocabulary, pronunciation and quiz behavior; check mixed content without recording tokens. Record deployment revision/artifact, operator/time and acceptance evidence.
6. Retain accepted Release A as the HTTPS-enabled recovery baseline. Only after review, normal Git/CI gates, approved successful deployment and every automatic/manual check above may Release B implement the separately reviewed 300-second HSTS policy, without subdomain or preload directives.

For failure after deployment, pause further approvals and use sanitized provider diagnostics. Restore a compatible known-good HTTPS-enabled artifact/root configuration or apply a reviewed fix, preserving ANCM and working certificates. If emergency restoration of pre-A configuration reopens HTTP, protect the application with maintenance/unavailability until enforcement is restored and retested. Do not bypass the new validator to blindly deploy an old artifact, enable a competing redirect owner, or treat destination-file preservation as a backup. Section 13 contains the detailed manual recovery procedure. Release A readiness for review does not authorize deployment or Release B.
