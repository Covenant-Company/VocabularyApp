# R6 Backend Integration Testing Final Review

## 1. Executive Summary

**R6 status: Ready for Final Developer Validation.**

R6 now provides layered backend coverage from password/security components through relational service behavior and real HTTP API journeys. A separate GitHub Actions workflow restores, builds, and tests the solution on Windows without production secrets or deployment coupling. Codex performed source review and compile-only validation; the developer must still run the full suite and validate the workflow after push.

## 2. R6 Objective

R6 establishes a deterministic safety net for critical backend behavior before later security, persistence, contract, quiz, and dictionary remediations. It exercises the real ASP.NET Core pipeline while isolating database, authentication, external HTTP, users, and the current process-static quiz sessions.

## 3. Final Test Architecture

### Unit/component

- `LegacyPasswordVerifierTests`
- `PasswordServiceTests`
- `PasswordVerificationOutcomeTests`

These protect password formats, verification outcomes, and malformed-input behavior without HTTP or database infrastructure.

### Service/database integration

- `UserServiceAuthenticationTests`
- `LoginMigrationTests`
- `CredentialConcurrencyTests`
- `AuthenticationLoggingTests`
- relational fixture tests

These exercise authentication persistence, transparent rehashing, concurrency, failure behavior, and logging safety against relational SQLite infrastructure.

### API integration

- `ApiHostSmokeTests`
- `IntegrationInfrastructureTests`
- `AuthenticationApiTests`
- `VocabularyOwnershipApiTests`
- `QuizApiTests`
- `DictionaryLookupApiTests`

These use `WebApplicationFactory<Program>` and the real middleware/controller/service pipeline. The layers complement one another: focused component tests cover edge cases while API tests protect representative end-to-end contracts and ownership boundaries.

## 4. Infrastructure Review

- Each `VocabularyAppWebApplicationFactory` owns an open in-memory SQLite connection and replaces the production SQL Server registration.
- `EnsureCreated` initializes only the factory-owned database; independent-factory isolation has explicit coverage.
- Test JWT settings are deterministic, synthetic, and supplied before host startup. Registration/login helpers obtain tokens through the real API.
- Unique usernames and emails prevent test collisions; generated entity IDs are captured rather than assumed.
- The factory-owned `ControllableDictionaryHandler` is fail-closed for unregistered paths, records requests, and prevents silent live-provider access.
- `QuizService.ClearQuizSessionsForTesting` is an internal, clear-only seam exposed to the test assembly. Quiz tests clear state before and after each test.
- No production connection string, JWT secret, provider credential, or developer secret is required.

## 5. Authentication Coverage

Coverage includes registration, login, usable JWT issuance, authenticated identity resolution, anonymous protected routes, malformed/tampered/expired tokens, profile ownership, invalid credentials, and the password-change boundary. Assertions favor status codes, stable envelope fields, claims, and database effects over incidental message text.

## 6. Vocabulary and Ownership Coverage

Coverage protects authenticated personal saves, user-scoped list/search, inclusion of owned data, exclusion of other users' data, favorite ownership, preferred-definition ownership, cross-user mutation rejection, same-word independent user state, definition-to-word integrity, invalid/missing IDs, duplicate-save characterization, and relevant EF relationships.

There is no individual saved-item read endpoint and no remove/archive endpoint, so direct-read and removal scenarios are not applicable. Current duplicate behavior is characterized for `(UserId, WordId, PartOfSpeechId)` without declaring that composite identity to be R5's desired design.

## 7. Quiz Coverage

Coverage includes anonymous rejection for start/submit/history, authenticated creation from owned vocabulary, answer-key secrecy, session ownership, valid submission, caller-owned result persistence, duplicate submission rejection, unknown session handling, invalid option behavior, foreign-session question behavior, and user-scoped history.

The start response is asserted not to expose correct-option or correctness fields. Phase 5 deliberately does not assert `UserWord.CorrectAnswers`, `TotalAttempts`, `LastReviewedAt`, or `LastCorrectAt`, because R4 owns those corrections.

Current characterization shows that an unknown option ID is accepted and scored incorrect, while an answer keyed to another session's question is ignored as unanswered. These behaviors are not endorsed as future contracts.

## 8. Dictionary Lookup Coverage

Coverage includes local cache hit without provider access, controlled cache miss, provider-success mapping and persistence, provider 404, provider 500, and unknown part-of-speech fallback to `Noun`. All provider responses are factory-scoped and deterministic.

## 9. Isolation and Parallel Safety

Static review indicates the architecture is deterministic by design:

- API databases and fake-provider configuration are factory-owned.
- Test users and words use unique values.
- No fixed database IDs or test-order assumptions were found in R6 API tests.
- No assembly-wide xUnit parallelization disable exists.
- Only `QuizApiTests` belongs to a nonparallel collection because production quiz sessions are process-static.
- Quiz sessions are cleared both before and after each quiz test.

Runtime repeatability remains subject to developer validation.

## 10. CI Review

Workflow: `.github/workflows/backend-tests.yml`

- Triggers: all `push` and `pull_request` events.
- Runner: `windows-latest`.
- Permissions: read-only repository contents.
- SDK: .NET 8 (`8.0.x`).
- Commands:
  - `dotnet restore VocabularyApp.sln`
  - `dotnet build VocabularyApp.sln --configuration Release --no-restore`
  - `dotnet test VocabularyApp.sln --configuration Release --no-build`

The workflow contains no production secrets, SQL Server settings, JWT secrets, provider credentials, publish steps, FTP actions, deployment calls, or production database operations. It does not depend on untracked `Docs/Updates` content or machine-specific paths.

## 11. R3 Finding

`POST /api/words/add` currently permits anonymous writes to shared canonical word data.

This remains a confirmed security/remediation finding. **R3 is the next recommended remediation.** R6 did not change the endpoint.

## 12. R4 Readiness

**Ready with Conditions.**

Authentication, ownership, answer secrecy, valid submission, result persistence, invalid/unknown session behavior, duplicate behavior, history isolation, and static-session isolation have implementation coverage. The conditions are successful developer-run focused/full tests and successful CI execution. R4 must define desired invalid-option behavior and add expectations for atomic counter/timestamp updates without weakening the stable ownership and persistence tests.

## 13. R5 Readiness

**Ready with Conditions.**

Two-user vocabulary isolation, cross-user mutations, favorites, preferred definitions, duplicate behavior, search/list isolation, deterministic generated IDs, and EF relationships have implementation coverage. The conditions are successful developer-run tests and CI. R5 may intentionally update identity-specific duplicate expectations while preserving ownership behavior.

## 14. R7/R8 Findings

- Framework model-validation `ProblemDetails` and application-defined envelopes coexist.
- Missing-profile/resource behavior and authentication failures use differing 404/401 semantics.
- Password-change failure cases share broad 401 behavior.
- Ownership and missing-resource vocabulary mutations currently map to 400 rather than a consistent 403/404 policy.
- Dictionary provider 500 currently maps to the same API 404 as provider not-found.
- Unknown quiz option IDs are accepted and scored incorrect.
- Answers for questions outside the submitted session are ignored rather than rejected.

R7/R8 should standardize contracts deliberately and update characterization tests in the same reviewed change.

## 15. R10/R12/R13 Findings

### R10

Cache hit, controlled cache miss, provider mapping/persistence, not-found, provider failure, and unknown part-of-speech fallback are characterized. Provider extraction and error-policy redesign remain deferred.

### R12

Quiz sessions remain process-static. Tests isolate them with a narrow reset seam and quiz-only serialization. Invalid-option and cross-session-answer validation limitations are characterized; session architecture and lifecycle redesign remain deferred.

### R13

Concurrent cache-miss behavior remains intentionally deferred. The current provider/cache architecture lacks a clean deterministic coordination seam, and R6 did not introduce production refactoring solely to enable that test.

## 16. Known Test Limitations

- Codex did not execute tests, so runtime correctness and repeated-run stability are not yet confirmed for this phase.
- SQLite `EnsureCreated` is not a migration-validation or SQL Server parity lane.
- Quiz randomization is constrained through fixed modes and invariant assertions, but the underlying production selection remains random.
- Concurrent dictionary cache misses are not covered.
- No direct saved-item read or removal tests exist because those API routes do not exist.

## 17. Manual Developer Validation Required

Codex did not run tests. The developer must run:

```powershell
dotnet test
```

Focused Phase 5 runs may be used for diagnosis, but the completion gate requires the full solution suite.

## 18. CI Validation Required

After committing and pushing, verify the `Backend tests` GitHub Actions workflow completes restore, Release build, and the full test suite successfully. Any workflow failure must be understood before R6 is considered complete.

## 19. R6 Definition of Done Review

| Item | Status |
| --- | --- |
| Existing R2 tests remain intact | PASS (source review) |
| API integration harness exists | PASS |
| Test host never uses production database | PASS (configuration review) |
| Relational database isolation exists | MANUAL VALIDATION REQUIRED |
| Multiple users authenticate deterministically | MANUAL VALIDATION REQUIRED |
| Anonymous protected requests are rejected | MANUAL VALIDATION REQUIRED |
| Valid JWT identity resolves correctly | MANUAL VALIDATION REQUIRED |
| Vocabulary ownership is covered | PASS (tests implemented) |
| Cross-user mutations are protected | MANUAL VALIDATION REQUIRED |
| Favorites are covered | PASS (tests implemented) |
| Preferred definitions are covered | PASS (tests implemented) |
| Duplicate vocabulary behavior is characterized | PASS (tests implemented) |
| Quiz creation is covered | PASS (tests implemented) |
| Correct answers are not exposed | MANUAL VALIDATION REQUIRED |
| Quiz ownership is covered | MANUAL VALIDATION REQUIRED |
| Invalid submission behavior is characterized | PASS (tests implemented) |
| Quiz persistence/history are covered | PASS (tests implemented) |
| Static quiz state is isolated in tests | MANUAL VALIDATION REQUIRED |
| Lookup cache hit is covered | PASS (tests implemented) |
| Provider-backed lookup is controlled | PASS (infrastructure/tests implemented) |
| Real dictionary traffic is impossible from tests | PASS (fail-closed design review) |
| Concurrent cache miss is explicitly deferred to R13 | PASS |
| CI backend test workflow exists | PASS |
| CI requires no production secrets | PASS |
| No unrelated remediation fixes were bundled | PASS (diff review) |
| No database schema change was introduced by R6 | PASS |
| Full suite passes locally and in CI | MANUAL VALIDATION REQUIRED |

## 20. Merge Readiness

R6 may be merged after the developer runs the full local suite successfully, commits and pushes the changes, and verifies the backend workflow succeeds. No merge or deployment should occur solely on the basis of compile-only validation.

The unresolved governance question is whether `Backend tests` should eventually become a required status check before merging to `master`. Recommendation: make it required after the initial workflow has demonstrated stable execution, but do not change branch protection as part of R6 implementation.

## 21. Recommended Next Remediation

**R3 — secure or remove the public canonical word-write endpoint.**

This should follow R6 once final local and CI validation are green, assuming no new blocker is discovered.

## 22. Final Recommendation

Proceed with developer validation and GitHub Actions validation. If both are green, R6 has met its safety-net objective and is suitable for merge. Address the anonymous canonical-write finding in R3 next; retain all other characterized issues for their assigned remediations.
