# F6 dashboard review counts error analysis

Date: 2026-10-23 (America/New_York). Scope: repository investigation and this report only. No fix implemented. Production remains NO GO.

## Finding and confidence

The reported dashboard message, “Vocabulary reviews are not available right now.” with “Retry review counts,” matches the implemented disabled-feature path. **Most likely explanation: the running backend has `Reviews:Enabled=false`.** Both flags default to false and neither committed appsettings file supplies a Reviews override. Applying migrations does not enable these flags.

This is a confirmed source-code explanation, **not a confirmed runtime diagnosis**. The browser's actual request status/body, running API version and effective configuration were not inspected. Authentication, database mismatch or an integration failure are not established by the screenshot. The exact message strongly favors `reviews_disabled` over those alternatives: ordinary 401, network errors and 500 responses map to different messages.

## Angular component and request flow

- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts`: `ngOnInit()` subscribes to the current user, resets review state on owner change and calls `loadReviewSummary()` when a user exists and `isAuthenticated()` succeeds. Missing/locally expired authentication redirects to login. Each qualifying user emission can request counts; no client feature-flag check precedes the request.
- `dashboard.component.html`: the Vocabulary reviews section is rendered unconditionally within the dashboard. Loading displays “Loading review counts…”. Success displays due/overdue/new counts and blocked-word information. Errors display `reviewSummaryError`; stored prior counts are hidden while that error exists. The review link is hidden only when `reviewsDisabled` is true.
- `loadReviewSummary()` prevents overlapping calls, clears the displayed error, calls `ReviewService.summary()`, then stores validated counts or normalizes the error. `reviewsDisabled` is set from `failure.code === 'reviews_disabled'`. Failure does not fabricate zero counts.
- `VocabularyApp.UI/src/app/services/review.service.ts`: `summary()` calls `ApiService.get('/reviews/summary')`, validates through `requireReviewSummary`, and cancels on identity change. `review-contract.ts` checks the success envelope, nonnegative counts/count relationships and UTC timestamps; malformed success data enters the error channel with “Unable to load review counts.”
- `api.service.ts` prefixes the endpoint with the environment API URL. Development `environment.ts` selects `http://localhost:5190/api`, yielding **GET `http://localhost:5190/api/reviews/summary`**. The production environment replacement is separate; the running build's effective URL remains unverified.
- `api-error.ts`: a review-operation HTTP 503 body with top-level `code: "reviews_disabled"` explicitly produces the exact reported message and code. It does not classify every 503 as feature-disabled.

“Retry review counts” appears for **any** nonempty `reviewSummaryError`, including the deliberate disabled state. Clicking calls the same `loadReviewSummary()` method; it neither enables reviews nor enrolls words. An unchanged disabled flag yields the same result. The dashboard already distinguishes disabled from infrastructure failure internally, but presents both using the error property and retry button. Existing `dashboard.component.spec.ts` explicitly expects this unavailable message and hidden review link for disabled reviews. This behavior is intentional in current source/tests; the retry presentation can be clearer.

The frontend has no independent, authoritative feature-availability setting or discovery endpoint here. A single authenticated summary request is currently how it discovers that reviews are disabled. Suppressing all requests based on an invented Angular flag would create a second configuration source. Keeping the panel with a distinct unavailable state is the smallest UI improvement; hiding the entire panel would be a separate product choice.

## Backend endpoint and response contracts

`ReviewsController` has `[Authorize]` and route `api/reviews`; `GetReviewSummary()` handles GET `summary` and delegates through `Run()` to `ReviewService.SummaryAsync(ownerId)`. Invalid/missing numeric owner claim returns application `invalid_token`; exceptions become safe `internal_error` through `ApiErrorResults`, with only exception type logged by this controller.

`SummaryAsync()` first checks `IOptionsMonitor<ReviewOptions>.CurrentValue.Enabled`. **When false it returns before beginning a database transaction or executing the summary queries.** When true it opens a serializable transaction, counts owner-scoped `UserWordReviewStates`, determines next future review, and inspects due saved-word definitions in bounded batches to calculate blocked/usable counts. It returns a server UTC timestamp and commits the read transaction. This path reads persisted enrollment; it does not enroll vocabulary.

| Condition | Expected response | Frontend interpretation |
|---|---|---|
| Enabled=true; authenticated; queries succeed | 200 `{ "success": true, "data": { dueCount, newCount, overdueCount, blockedCount, usableDueCount, totalEnrolledCount, nextReviewAt, asOfUtc } }` (field list schematic) | Validated counts displayed; zero enrollment may legitimately mean zero counts. |
| Enabled=false; authenticated | 503 application envelope with `code: "reviews_disabled"` | Exact reported unavailable message; review link hidden; retry currently still shown. |
| EnrollmentEnabled=false, Enabled=true | Same 200 summary contract, assuming successful queries | Reads existing states normally. Enrollment off alone does not disable summary; new saved words may lack review state. |
| Both flags false | Same authenticated 503 disabled response | Enrollment value does not change this gate. |
| Missing/invalid/expired JWT | Framework 401, possibly empty body, before action/service | “Please sign in to continue.” |
| Authenticated principal lacks usable owner claim | 401 application envelope, `code: "invalid_token"` | Same sign-in message. |
| SQL/service exception while enabled | 500 application envelope, `code: "internal_error"` | “An internal error occurred. Please try again.”; not the disabled message. |
| Transport/CORS/API unreachable | Angular status 0 | Connection message. |
| 403 | No ordinary summary-specific 403 branch/policy in inspected code | Generic normalization/fallback or sanitized known body message; no dedicated forbidden navigation. Not evidence of the disabled gate by itself. |

The disabled JSON envelope is `{ "success": false, "data": null, "error": "Reviews are temporarily unavailable. Please try again later.", "message": "Reviews are temporarily unavailable. Please try again later.", "code": "reviews_disabled", "traceId": "<request trace>" }`. Other application failures use the same shape with the appropriate error/code. Successful results contain only `success` and `data`. No endpoint/envelope mismatch was identified in this path.

## Feature flags and development configuration

`ReviewOptions` defines section `Reviews`, with `Enabled=false` and `EnrollmentEnabled=false`. `Program.cs` binds that section and the service reads current options via `IOptionsMonitor`. `ReviewStateCoordinator.EnrollmentEnabled` independently gates coordinated enrollment; migration DDL does not set either option.

`WebApplication.CreateBuilder(args)` supplies normal application configuration: base appsettings, environment-specific appsettings, Development user secrets when applicable, environment variables, then command-line overrides (later providers win for the same key). Environment variables `Reviews__Enabled` and `Reviews__EnrollmentEnabled` can override the defaults. Effective running values are **UNVERIFIED**; no user-secret contents, process environment or runtime options were retrieved.

Committed launch profiles select Development and API ports 5190/7196; they supply no Reviews or database override. The WebApi project declares a UserSecretsId. Existing flags and settings were not modified. Enabling flags is not a diagnostic fix and would require separate local execution approval and data protection under the revised Phase 6B plan.

## Database selection without connecting

`Program.cs` passes `Configuration.GetConnectionString("DefaultConnection")` to SQL Server registration. Parsed repository settings nominate database **`VocabularyAppDb`** in base configuration and **`VocabularyAppDb_Dev`** in Development. No connection string is reproduced here. Standard Development launch therefore intends the migrated development database if no higher-precedence override exists.

Development user secrets, `ConnectionStrings__DefaultConnection`, command-line arguments or a different launch environment can select another database. None was read from the running API. **Effective runtime database: UNVERIFIED.** Migration success against `VocabularyAppDb_Dev` does not prove that the API uses it. The design factory's separate design-only target does not select the runtime API database. If a genuine disabled response is confirmed, the summary made no database query and cannot validate database connectivity/schema.

## Authentication findings

`ApiService.getAuthHeaders()` obtains the stored token through `AuthService.getToken()` and attaches Bearer authorization on summary requests when a token exists. `app.config.ts` enables DI interceptors but no HTTP interceptor implementation/registration was found; attachment is explicit in ApiService. No global interceptor here refreshes tokens or redirects on 401/403.

`AuthService.isAuthenticated()` checks token structure/expiry as a UI hint; the backend validates signature and lifetime with zero clock skew. A token can expire after dashboard initialization, or pass the UI hint while failing backend validation; those conditions can cause a summary 401 and sign-in message. A retry does not re-run the dashboard's initial authentication guard. They do not ordinarily produce the exact disabled text. No actual user token was retrieved, decoded or printed.

## Minimal diagnostic and proposed correction

1. In the owner's existing browser DevTools Network panel, inspect the already-issued `reviews/summary` request. Record only request URL, HTTP status, and response `code`/sanitized message (plus traceId if investigating a failure). **Do not copy Authorization headers, cookies, tokens or a full HAR.** No database connection is needed.
2. If it is 503 / `reviews_disabled`, confirm expected gating and propose only a dashboard presentation change: represent disabled availability distinctly, use clear unavailable wording, and show “Retry review counts” for recoverable errors rather than the known disabled state. Preserve disabled review navigation and do not show invented zero counts. Update the focused dashboard spec if separately approved.
3. If 401, investigate authentication through the normal sign-in flow; if 500, correlate its trace with existing sanitized backend diagnostics before proposing database verification; if status 0, inspect target/API/CORS availability. If 200 is rejected, inspect the sanitized summary shape against `requireReviewSummary`. None of these alternatives justifies automatic migrations, configuration changes or new infrastructure.

No runtime cause is yet confirmed. Owner-supplied status/code is the smallest missing evidence. Code changes require separate approval after findings review. Any new runtime API/browser automation, SQL inspection, local flag/configuration change, build or test execution also requires its own authorization; none was performed here.

## Inspection inventory and task boundary

Inspected: dashboard component/template/spec; ReviewService; ApiService; api-error; review-contract; AuthService; app.config; development environment and Angular environment replacement configuration; ReviewsController; ReviewService backend; ReviewStateCoordinator; ReviewOptions; ApiErrorResults; ApiErrorResponse/SuccessResponse DTOs; Program.cs; parsed appsettings base/Development database names and Reviews sections; launchSettings; WebApi csproj UserSecretsId; JWT lifetime settings; and existing ReviewApiTests/ReviewEnrollmentTests references. Existing test source is supporting evidence only; tests were not run.

Only this analysis document was created. No source fix, feature-flag/connection-string change, SQL connection/query/migration/write, seed, backup/restore, app/API startup, browser automation/authenticated request, build/test/restore/install, CI/CD change, Git operation, production access or deployment occurred during this investigation. Development/rehearsal databases and all existing files/branches were preserved.
