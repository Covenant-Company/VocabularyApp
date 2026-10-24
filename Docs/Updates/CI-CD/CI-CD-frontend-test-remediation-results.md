# Frontend CI test remediation results

## Scope and execution

**CODEX DID NOT RUN TESTS.** No frontend/backend test runner, GitHub Actions run, deployment, database operation, commit, or push was executed. No branch was created or switched. `.git/HEAD` identifies `fix/ci-frontend-tests`; Git status is unavailable because of the repository ownership check. No Git configuration was changed.

The user reports the first Phase 1 run passed all 172 backend tests, while frontend results were 27 passed and 7 failed out of 34. The frontend failure correctly blocked builds. These are supplied CI results, not results reproduced during this task.

## Seven failures and remediation

Paths below are relative to `VocabularyApp.UI/src/app/`.

| Spec file | Reported failing test | Root cause and correction |
| --- | --- | --- |
| `components/signup/signup.component.spec.ts` | SignupComponent should create | Standalone TestBed imported the component but omitted AuthService's HttpClient dependency and explicit routing setup. Supply AuthService and Router spies; ReactiveFormsModule already supplies the form infrastructure. |
| `app.component.spec.ts` | AppComponent should have the 'VocabularyApp.UI' title | Generated expectation was stale: current title is `Vocabulary App`. Assert that exact current value. |
| `app.component.spec.ts` | AppComponent should render title | Template now contains only a router outlet, not a starter h1. Replace the obsolete heading assertion with exactly one instantiated RouterOutlet directive; configure `provideRouter([])`. |
| `components/dashboard/dashboard.component.spec.ts` | DashboardComponent should create | Missing AuthService HTTP dependency; ngOnInit also requires currentUser$ and isAuthenticated and can redirect. Supply an authenticated user observable and auth spy plus a Router spy. Retain creation assertion and check initialized user and rendered welcome username. |
| `services/api.service.spec.ts` | ApiService should be created | Empty TestBed omitted HttpClient; ApiService also depends on AuthService. Supply HTTP testing providers and an AuthService getToken spy returning null. |
| `components/login/login.component.spec.ts` | LoginComponent should create | Missing AuthService HTTP dependency; ngOnInit additionally consumes ActivatedRoute.queryParams and isAuthenticated. Supply auth/router spies and a finite empty query-parameter observable with an unauthenticated state. |
| `services/auth.service.spec.ts` | AuthService should be created | Empty TestBed omitted HttpClient, and initialization reads browser storage. Supply HTTP testing providers and a scoped getItem spy returning null. Retain creation assertion and verify null user/unauthenticated state. |

All seven reported failures are explained by incomplete test configuration or stale assertions. No production defect was identified, and no production code or template was changed.

## Changed files and isolation

Modified the six spec files in the table. Created this document at `docs/Updates/CI-CD-frontend-test-remediation-results.md`, using the repository's existing lowercase `docs` convention. No shared testing helper was added: only two service specs need the short HTTP provider pair, while each component needs different explicit doubles.

Service specs register `provideHttpClient()` before `provideHttpClientTesting()`, so Angular replaces the network backend with its test backend. Both verify there are no outstanding requests in afterEach. This provider order follows [Angular HTTP testing guidance](https://angular.dev/guide/http/testing); installed Angular declarations confirm the APIs are available and HttpClientTestingModule is deprecated.

Component specs do not construct the real AuthService, avoiding its HTTP and storage dependencies. Router spies return resolved promises without performing navigation. Login's queryParams and dashboard's currentUser$ use finite `of(...)` observables. Each beforeEach creates fresh doubles. AuthService's storage spy is restored by Jasmine after each test and does not write or clear persistent storage. ApiService's auth dependency is mocked, so its creation does not read browser storage either.

The original creation assertions remain. Both stale AppComponent tests have replacements; the suite is retained. No tests were removed, skipped, focused, commented out, or marked pending. No new deprecated testing module was introduced; unrelated existing specs were not changed.

## CI configuration discrepancy

**No CI gate was weakened by this change: workflow and package scripts were left unchanged.** However, this branch's only workflow, `.github/workflows/backend-tests.yml`, contains the older backend-only workflow with unrestricted push/pull_request triggers and a single restore/build/test job. It does not contain the Phase 1 frontend gate or the two-test-job build dependency described in the user-supplied run results.

Consequently, this checkout cannot establish that the Phase 1 gate is currently present. When integrating these test fixes with the Phase 1 branch, preserve `npm test -- --watch=false --browsers=ChromeHeadless` and the build dependency on both successful test jobs. Branch ancestry and the remote workflow were not verified; no speculative workflow replacement or branch operation was performed in this test-remediation task.

## Static inspection and remaining limitations

Inspected affected components, templates, services, existing specs, user model, package.json, angular.json, tsconfig.spec.json, installed HTTP API declarations, and the current workflow. The frontend uses standalone components and the Karma builder with `ng test` as its npm script.

A TypeScript compiler API check with `noEmit` was attempted without invoking a test runner. It could not resolve the local Jasmine type definitions (TS2688), producing cascading missing test-global diagnostics. It did not establish type-check success. No package installation or configuration change was made to hide that limitation. Actual Angular compilation, DI, templates, and runtime behavior remain unverified until the user runs the suite.

## Manual verification

**MANUAL USER COMMAND — DO NOT EXECUTE** (from repository root, dependencies installed and Chrome available):

```powershell
cd VocabularyApp.UI
npm test -- --watch=false --browsers=ChromeHeadless
```

The full suite is the recommended verification because it also detects interactions with unchanged specs. Expected result for the supplied 34-test baseline: **34 passed, 0 failed, 0 skipped**. This is an expectation, not a claimed result. With these corrections integrated into the Phase 1 workflow, successful backend and frontend gates should allow the build stage to begin; build success itself is outside this remediation's verification.

No production behavior, deployment logic, database logic, CI failure handling, or coverage selection was changed. **CODEX DID NOT RUN TESTS. Nothing was committed or pushed.**
