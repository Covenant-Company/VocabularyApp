# R2 Password Hashing Implementation Plan

## 1. Executive Summary

This document is the step-by-step implementation guide for replacing VocabularyApp's salted SHA-256 password storage with ASP.NET Core `PasswordHasher<User>` while preserving transparent access for existing users. It is based on `Docs/R2-Password-Hashing-Analysis.md` and a fresh inspection of the current branch, `fix/r2-replace-sha-256-password-hashing`.

The implementation will add one application-level password service, a strict verification-only legacy verifier, and backend tests. `UserService` will use only the modern hasher for registration and password changes. Login will accept modern or legacy values, persist required upgrades before creating a JWT, and fail safely if persistence or optimistic concurrency fails. Full ASP.NET Core Identity, JWT changes, password resets, UI work, and unrelated remediation are excluded.

This plan contains **10 implementation phases**. Complete and verify one phase before asking GitHub Copilot to begin the next.

## 2. Confirmed Baseline

The analysis remains current. No source discrepancy was found during the implementation-plan inspection.

| Baseline item | Current evidence |
| --- | --- |
| Legacy helper | Static `PasswordHelper` in `VocabularyApp.WebApi/Helpers/PasswordHelper.cs:6-56` generates and verifies salted SHA-256 |
| Exact legacy format | `44-character Base64 salt:44-character Base64 digest`; each encoded segment represents 32 bytes; total generated length is 89 characters |
| Historical algorithm | Generate 32 random salt bytes, Base64-encode them, compute `SHA256(UTF8(password + saltBase64Text))`, Base64-encode digest |
| Hash-generation call sites | `UserService.CreateUserAsync` at line 53 and `UserService.ChangePasswordAsync` at line 212 |
| Verification call sites | `UserService.LoginAsync` at line 108 and `UserService.ChangePasswordAsync` at line 205 |
| Registration flow | `POST api/users/register` -> `UsersController.Register` -> `UserService.CreateUserAsync` -> `PasswordHelper.HashPassword` -> tracked `User` -> `SaveChangesAsync` |
| Login flow | `POST api/users/login` -> `UsersController.Login` -> tracked username query -> `PasswordHelper.VerifyPassword` -> `UpdateLastLoginAsync` -> JWT |
| Password change | Authorized `POST api/users/change-password` -> user-ID claim -> `UserService.ChangePasswordAsync` -> verify current -> create legacy hash -> save |
| Reset functionality | No forgotten, token, or administrative password-reset flow exists in Web API, Data, or Angular source |
| Storage | `User.PasswordHash` is required `string`; committed SQL Server mapping is non-null `nvarchar(max)` (`User.cs:18-19`, initial migration line 39, snapshot lines 246-248) |
| `UserService` constructor | `ApplicationDbContext`, `JwtHelper`, `ILogger<UserService>` (`UserService.cs:15-20`) |
| DI | Scoped `ApplicationDbContext`, `IUserService -> UserService`, and `JwtHelper`; no password service registration (`Program.cs:20-21,44-49`) |
| Backend tests | No .NET test project exists in `VocabularyApp.sln`; Angular specs are creation-only smoke tests |
| Last-login persistence | `UpdateLastLoginAsync` loads/uses the tracked user, saves, catches every exception, logs, and suppresses it (`UserService.cs:161-176`) |

The current `PasswordHelper.VerifyPassword` accepts any two colon-separated strings, compares Base64 text with ordinary `==`, and catches all exceptions. R2 must preserve the exact historical algorithm for compatibility while tightening recognition and comparison.

## 3. Architectural Decisions

### Decision A — failed required hash-upgrade persistence

**Policy:** A legacy migration or modern `SuccessRehashNeeded` replacement must be successfully persisted before a JWT is issued. If the save fails, login returns failure and no token.

Why:

- issuing a JWT after a failed required save silently preserves the weak credential and causes repeated migration attempts;
- authentication state should not claim completion of a critical credential write that was lost; and
- registration already issues its JWT only after persistence, so this ordering is consistent.

This differs from `LastLoginAt`, which is currently treated as noncritical and whose exceptions are swallowed. The implementation must remove `UpdateLastLoginAsync` from `LoginAsync` and perform one intentional save. A credential-write or concurrency failure is caught in the service, logged without password/hash/salt content, and returned to the controller as `AuthResponse { Success = false, ErrorMessage = "An error occurred during login" }`. No exception details or token are returned. The existing controller will map the failed result to its existing unsuccessful login response. Do not reuse the invalid-password warning for an operational save failure; log a separate safe event using user ID.

For a normal modern login with no required hash replacement, failure to save only `LastLoginAt` may retain the existing noncritical policy: log safely and allow authentication. The code must distinguish this path explicitly. It must never suppress an exception from a save that includes `PasswordHash`.

### Decision B — concurrent migration versus password change

**Chosen strategy:** Configure `User.PasswordHash` as an EF Core concurrency token in `ApplicationDbContext.OnModelCreating` and use the tracked entity's original value for conditional writes.

EF will include the original `PasswordHash` in the `UPDATE` predicate. If another request changed the credential after this request read it, `SaveChangesAsync` affects zero rows and throws `DbUpdateConcurrencyException`. This is a narrow optimistic-concurrency guard over the credential itself; it needs no row-version column, broad lock, or database migration.

Affected files:

- `VocabularyApp.Data/ApplicationDbContext.cs`: configure `.Property(u => u.PasswordHash).IsConcurrencyToken()`.
- `VocabularyApp.WebApi/Services/UserService.cs`: handle `DbUpdateConcurrencyException` separately in login and password change; never overwrite after conflict.
- backend tests: coordinate two contexts to reproduce the stale-login/new-password race.

Conflict behavior:

- Login migration or modern rehash: fail safely, return no JWT, do not retry using the already submitted password, and ask the caller to authenticate again naturally. A retry inside the same request risks reasoning against a credential that has just changed.
- Normal modern login saving only `LastLoginAt`: if a password changed concurrently, treat the stale verification as invalidated and return no JWT rather than detaching/retrying the timestamp.
- Password change: return the existing `false` result, do not overwrite, and safely log a concurrency event. A later API-contract improvement may distinguish conflict from incorrect current password, but that is outside R2.

## 4. Target Design

After R2, the server-side flow is:

`UserService -> IPasswordService -> PasswordService -> IPasswordHasher<User>`

and, only for strict legacy candidates:

`PasswordService -> ILegacyPasswordVerifier -> LegacyPasswordVerifier`

Recommended conceptual API (illustrative signatures only):

```csharp
public interface IPasswordService
{
    string HashPassword(User user, string password);
    PasswordVerificationOutcome Verify(User user, string storedHash, string password);
}
```

`PasswordVerificationOutcome` should contain a status and optional replacement hash. Recommended statuses are:

- `Failed`: password did not verify;
- `Succeeded`: current modern hash is accepted with no write required;
- `SucceededRehashRequired`: modern verification returned `SuccessRehashNeeded`; replacement is modern;
- `SucceededLegacyMigrationRequired`: legacy verification succeeded; replacement is modern;
- `MalformedOrUnknown`: input was not a strict legacy value and could not be handled as a valid modern hash.

The service result must make it impossible for `UserService` to confuse success with a required replacement. It must never expose salt or parsed hash bytes. Keep classification out of controllers and `UserService`.

## 5. New Components

Recommended production components under a new `VocabularyApp.WebApi/Security` folder:

- `IPasswordService`: application-facing creation and multi-format verification contract.
- `PasswordVerificationStatus` and `PasswordVerificationOutcome`: immutable result model; replacement hash is populated only for the two required-write statuses.
- `PasswordService`: coordinates strict classification, legacy verification, framework verification, and modern replacement generation.
- `ILegacyPasswordVerifier`: narrow `IsLegacyFormat`/`Verify` contract, or one verification method returning recognized/succeeded flags.
- `LegacyPasswordVerifier`: temporary verification-only historical SHA-256 implementation.

Keep modern hashing inside `PasswordService` via injected `IPasswordHasher<User>`; a separate modern adapter is unnecessary unless tests reveal value. Do not create a general authentication framework.

## 6. Existing Components to Modify

- `UserService`: add `IPasswordService`; replace all four static helper calls; implement outcome handling; move successful-login persistence before JWT generation; handle concurrency.
- `ApplicationDbContext`: mark only `PasswordHash` as a concurrency token.
- `Program`: register framework and application password services.
- `PasswordHelper`: transition away from production use. During R2 either reduce it to a deprecated wrapper around verification-only logic or leave it unused while `LegacyPasswordVerifier` owns copied-and-tightened compatibility logic. The preferred end state for this change is no production reference to `PasswordHelper`; do not delete it yet.
- `Docs/README.md`: after tests pass, replace claims that SHA-256 is industry-standard with the modern design and temporary migration statement.
- `VocabularyApp.sln`: add the new backend test project.

`IUserService`, `UsersController`, request DTOs, JWT helper, Angular UI, and migrations should remain unchanged unless an implementation obstacle proves otherwise. The current service signatures can express the selected policies.

## 7. Legacy Hash Verification Design

`LegacyPasswordVerifier` must be verification-only and explicitly documented as temporary. It must have no public hash-generation operation.

Recognition and verification order:

1. Reject null/empty values.
2. Require exactly one colon; reject missing or extra delimiters.
3. Require exactly 44 characters before and after the colon.
4. Decode both with standard Base64 in exception-safe code.
5. Require both decoded arrays to be exactly 32 bytes.
6. Enforce canonical encoding by re-encoding decoded bytes and comparing to the original segments with ordinal comparison. This excludes permissive/noncanonical representations.
7. Reproduce the historical input exactly: concatenate submitted password with the original Base64 salt text, encode with UTF-8, and compute one SHA-256 digest.
8. Compare computed digest bytes to decoded stored digest bytes with `CryptographicOperations.FixedTimeEquals`.
9. Return recognized/failed for a strict candidate with the wrong password; return not-recognized for malformed structure. Do not throw endpoint-visible parsing exceptions.

Never “improve” the historical order, use raw salt bytes in the digest input, normalize the password, or change encoding. Any such change would lock out existing users.

## 8. Modern Hashing Design

Register and inject `IPasswordHasher<User>` implemented by `PasswordHasher<User>`. Use:

- `HashPassword(user, plaintext)` for registration, password change, legacy replacement, and modern rehash replacement;
- `VerifyHashedPassword(user, storedHash, plaintext)` for nonlegacy candidates.

Map framework results centrally:

- `Failed` -> application `Failed` (or `MalformedOrUnknown` when the encoded payload is structurally rejected; both authenticate as failure);
- `Success` -> `Succeeded`, no replacement;
- `SuccessRehashNeeded` -> generate a fresh modern hash and return `SucceededRehashRequired` with replacement.

Wrap only expected malformed-payload exceptions at this boundary and return `MalformedOrUnknown`; do not blanket-catch programmer or configuration failures. No Identity stores, `UserManager`, `SignInManager`, Identity entity inheritance, Identity EF tables, or JWT changes are needed.

## 9. Hash Classification Design

Classification is deterministic and centralized in `PasswordService`:

1. Ask the legacy verifier whether the value passes every strict legacy structural rule.
2. If it is a strict legacy candidate, verify only with the legacy algorithm. Wrong password returns `Failed`; success returns a modern replacement requirement.
3. If it contains a colon but fails strict legacy recognition, return `MalformedOrUnknown`. Do not pass a malformed colon-bearing value to the modern hasher.
4. If it is not a legacy candidate and contains no colon, pass it to the framework hasher in exception-safe code.
5. A framework success determines it is modern. A framework failure authenticates as failure; malformed framework payloads become `MalformedOrUnknown` for safe observability.

Do not try both algorithms until one succeeds. Never put format detection in `UsersController`, `UserService`, registration, and password change separately.

## 10. Registration Implementation

In `UserService.CreateUserAsync`:

1. Keep username/email queries and response behavior unchanged.
2. Construct the `User` with username, email, and timestamps first so the entity can be passed to the framework hasher.
3. Set `user.PasswordHash = _passwordService.HashPassword(user, request.Password)`.
4. Add and save exactly as today.
5. Keep JWT generation after successful save.

Afterward, registration cannot generate a legacy value. Verify by reloading the row, confirming strict legacy classification is false, and verifying it through `IPasswordHasher<User>`.

## 11. Login and Transparent Migration Implementation

Implement this explicit algorithm in `UserService.LoginAsync`:

1. Load the user with the current tracked username query; retain the original `PasswordHash` in EF tracking.
2. If absent, return the existing generic invalid-credentials result.
3. Call `_passwordService.Verify(user, user.PasswordHash, request.Password)`.
4. For `Failed` or `MalformedOrUnknown`, return the same generic invalid-credentials result without changing `PasswordHash`, `LastLoginAt`, or any entity state.
5. For `Succeeded`, set `LastLoginAt`. Save it intentionally. If a concurrency conflict occurs, fail with no JWT because verification may now be stale. A non-concurrency timestamp-only persistence failure may be logged and treated as noncritical if preserving current behavior.
6. For `SucceededRehashRequired` or `SucceededLegacyMigrationRequired`, require a nonempty replacement modern hash, assign it to `PasswordHash`, set `LastLoginAt`, and call one `SaveChangesAsync`.
7. If the required save throws `DbUpdateConcurrencyException`, fail safely with no retry and no JWT. If it throws another persistence exception, fail safely with no JWT. Do not allow a broad catch to convert either into success.
8. Only after the appropriate persistence policy completes, map `UserDto`, create JWT, and return success.

Remove the call to `UpdateLastLoginAsync` from `LoginAsync`. Do not call one helper that saves inside another method and then save again.

## 12. Password Change Implementation

In `UserService.ChangePasswordAsync`:

1. Load the tracked user with `FindAsync`.
2. Verify `currentPassword` through `_passwordService.Verify`.
3. Treat `Failed` and `MalformedOrUnknown` as false with no mutation.
4. For every success status—including legacy and `SuccessRehashNeeded`—ignore any suggested migration replacement and directly compute `_passwordService.HashPassword(user, newPassword)`.
5. Assign that one modern hash and call `SaveChangesAsync` once.
6. Because `PasswordHash` is a concurrency token, a concurrent credential update raises `DbUpdateConcurrencyException`. Log the safe user-ID event, return false, and do not retry or overwrite.
7. Preserve the existing controller-facing boolean behavior.

Never save a modern hash of the old password before hashing the new password. A successful password change is itself the migration.

## 13. Persistence Refactoring

Fold last-login mutation and save orchestration into `LoginAsync`; it is the method that knows whether the write is critical. Remove `UpdateLastLoginAsync` from the login path.

Preferred small refactoring:

- set `user.LastLoginAt` directly in `LoginAsync`;
- use one `SaveChangesAsync` for a successful login;
- branch exception policy based on whether a replacement hash was required;
- generate JWT only after required writes succeed.

`UpdateLastLoginAsync` is public on `IUserService` but repository search found no caller other than `LoginAsync`. Once removed from that path, remove the interface method and service method if a final reference search is empty. If retained for compatibility, it must never be used to persist a tracked credential change. Do not create a generic save helper that hides whether the write is credential-critical.

## 14. Concurrency Protection

In `ApplicationDbContext.OnModelCreating`, add the concurrency configuration inside the existing `modelBuilder.Entity<User>` block:

```text
Configure PasswordHash as IsConcurrencyToken()
```

No entity property or database column is added. EF retains the original value when the entity is queried and emits an update conditional on both `Id` and original `PasswordHash`.

Service rules:

- never overwrite EF's original password-hash value;
- do not reload and retry after `DbUpdateConcurrencyException`;
- clear/detach stale state if the scoped service will continue doing work, although normal request scope will end;
- return no JWT for login conflict;
- return false for password-change conflict.

Race test:

1. Use two independent `ApplicationDbContext` instances against the same relational test database.
2. Context A loads a legacy user and verifies the old password.
3. Context B loads the same user, changes it to a modern hash of a new password, and saves.
4. Context A assigns a migration hash of the old password and attempts to save.
5. Assert a concurrency conflict is handled, no JWT is returned, and a fresh context confirms the stored hash verifies only the new password.

**No database migration is required** because concurrency-token configuration changes EF update predicates, not schema.

## 15. Dependency Injection Changes

In `Program.cs`, add imports for the Data `User` and new Security namespace, then register:

- `IPasswordHasher<User> -> PasswordHasher<User>` as singleton or scoped; choose **singleton** because the framework implementation is stateless after options are captured and safe for reuse;
- `ILegacyPasswordVerifier -> LegacyPasswordVerifier` as singleton because it is stateless;
- `IPasswordService -> PasswordService` as singleton because both dependencies are singleton/stateless.

If implementation uses `IOptions<PasswordHasherOptions>` in a way that favors standard framework registration patterns, scoped lifetimes for all three are also safe. Keep lifetimes consistent and do not inject a scoped dependency into a singleton. The preferred plan is singleton for this stateless graph.

Change `UserService` constructor to accept `IPasswordService` and store it in a readonly field. Do not resolve services from `IServiceProvider`, instantiate `PasswordHasher<User>` inside methods, or add password services to controllers.

## 16. PasswordHelper Transition

### During R2

- replace all `PasswordHelper.HashPassword` and `VerifyPassword` call sites;
- put exact historical verification in `LegacyPasswordVerifier` with strict validation and fixed-time comparison;
- leave `PasswordHelper.cs` present but unused and mark it obsolete/internal if this can be done without exposing a generation path to production code;
- preferably remove or make private its public `HashPassword` method only after reference searches and tests prove no production path uses it. Because this planning task explicitly requires not deleting `PasswordHelper`, retain the file during initial R2.

### Migration window

Only `LegacyPasswordVerifier` may perform SHA-256, and only after strict legacy classification. No registration or password-change dependency may access it directly.

### Later removal change

After the removal condition in section 22 is met, delete `LegacyPasswordVerifier`, its interface/tests/classification branch, and the obsolete `PasswordHelper` file. Do not combine this deletion with initial R2 deployment.

## 17. Backend Testing Infrastructure

Create one project: `VocabularyApp.WebApi.Tests`, targeting `net8.0`, using xUnit and `Microsoft.NET.Test.Sdk`. Reference both WebApi and Data projects and add it to `VocabularyApp.sln`.

Suggested commands for the developer to execute in Phase 1 (do not execute during planning):

```powershell
dotnet new xunit -n VocabularyApp.WebApi.Tests -f net8.0
dotnet sln VocabularyApp.sln add VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
dotnet add VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj reference VocabularyApp.WebApi/VocabularyApp.WebApi.csproj VocabularyApp.Data/VocabularyApp.Data.csproj
```

Testing choices:

- Do not add Moq/NSubstitute initially. Small hand-written fakes for `IPasswordService`/`IPasswordHasher<User>` make outcomes explicit.
- Use real `PasswordHasher<User>` in component tests.
- Use SQLite in-memory with one kept-open shared connection for service persistence tests, after verifying it honors concurrency-token affected-row behavior. Use SQL Server integration coverage for the concurrency race if SQLite behavior differs.
- Avoid EF InMemory for concurrency/persistence tests because it does not represent relational update predicates.
- Use a hand-written capturing `ILogger<T>` to inspect formatted messages and exception text for sentinel passwords/hashes.
- Use a controlled fake `IPasswordHasher<User>` to return `SuccessRehashNeeded` and a deterministic replacement.
- `JwtHelper` is concrete; use valid test `JwtSettings` so real token generation proves whether a token was issued. Do not change production JWT code solely to mock it.

## 18. Unit Test Plan

### `LegacyPasswordVerifierTests`

- known valid historical hash/password succeeds;
- wrong password fails;
- invalid Base64 returns not-recognized/failure without throwing;
- extra colon fails;
- missing colon fails;
- decoded salt length other than 32 fails;
- decoded digest length other than 32 fails;
- noncanonical Base64 fails;
- malformed values never throw;
- a fixture generated with the exact old `SHA256(UTF8(password + saltBase64Text))` algorithm succeeds;
- a fixture using raw salt bytes or salt-before-password fails, guarding historical compatibility;
- repeated digest comparison uses the fixed-time byte-comparison code path (verify by focused code review plus behavioral tests; timing tests are unreliable).

### `PasswordServiceTests`

- `HashPassword` creates a nonlegacy value accepted by real framework verification;
- modern `Success` maps to `Succeeded` without replacement;
- modern `Failed` maps to failure;
- controlled `SuccessRehashNeeded` maps to required rehash with a modern replacement;
- strict legacy candidate invokes only legacy verifier;
- valid legacy password returns migration-required plus modern replacement;
- wrong legacy password returns failure with no replacement;
- malformed colon-bearing value does not invoke modern hasher;
- unknown/malformed no-colon value fails safely;
- framework malformed-payload exception expected by the adapter is contained;
- result invariants reject success-replacement statuses without a replacement hash.

## 19. Service/Integration Test Plan

All database assertions must reload the user from a fresh context so change-tracker state cannot create a false positive.

| # | Test/layer | Important setup | Expected database state | Expected service result |
| --- | --- | --- | --- | --- |
| 1 | `UserService.LoginAsync` integration | Seed strict legacy user; submit correct password | Hash becomes modern; last login set | Success with nonempty JWT |
| 2 | Login migration persistence | Same as 1; reload row | Stored value is not legacy and real modern verifier succeeds | Success only after save |
| 3 | Wrong legacy login | Seed legacy; wrong password | Original hash unchanged | Failure, no token |
| 4 | Wrong legacy timestamp | Same as 3 | `LastLoginAt` unchanged | Failure |
| 5 | Registration | Unique request with known password | New row has modern, nonlegacy hash | Success; existing registration contract preserved |
| 6 | Modern login | Seed real modern hash | Hash unchanged unless hasher requests rehash; last login set | Success with token |
| 7 | Modern rehash | Controlled hasher returns `SuccessRehashNeeded` and replacement | Replacement persisted | Success with token after save |
| 8 | Change from legacy | Seed legacy, correct current, new password | One modern hash of new password; old password fails | `true` |
| 9 | Change from modern | Seed modern, correct current, new password | New modern hash verifies new only | `true` |
| 10 | Wrong current password | Seed either format | Hash and other fields unchanged | `false` |
| 11 | Malformed stored value | Invalid Base64/colon/length | Row and timestamp unchanged | Failure, no token, no exception escape |
| 12 | Unknown stored value | No-colon unsupported payload | Row and timestamp unchanged | Failure, no token |
| 13 | Required-save failure | Context/test seam forces `SaveChangesAsync` failure after valid legacy verification | Original hash remains | Failure, no JWT |
| 14 | Login/change race | Two contexts; login reads old, change saves new, login saves stale replacement | New-password hash remains | Login failure, no JWT; change success |
| 15 | Plaintext logging | Unique sentinel in request; capture service/controller logs on success/failure/exception | Normal scenario state | Sentinel absent from every log entry/exception rendering |
| 16 | Stored-hash logging | Unique sentinel stored value; malformed/save-failure path | Unchanged when failing | Hash sentinel absent from logs |
| 17 | Successful last login | Valid modern login | `LastLoginAt` moves forward | Success |
| 18 | Failed last login | Wrong password/malformed/unknown | `LastLoginAt` unchanged | Failure |

Also test a normal modern-login concurrency conflict: another request changes the password before timestamp save; stale login must receive no JWT.

## 20. Logging and Telemetry

Use existing structured logging conventions with username for ordinary attempts and user ID for credential persistence events. Add only concise events such as:

- legacy migration completed for user ID;
- modern rehash completed for user ID;
- required credential persistence failed for user ID;
- malformed/unknown stored format encountered for user ID;
- credential concurrency conflict for user ID.

Never log plaintext/current/new password, hash, salt, modern encoded payload, request DTO, parsed bytes, or SQL parameters containing credential values. Do not introduce a large telemetry system. Existing structured logs plus optional counters for migration outcomes are sufficient. Capturing-logger tests must scan both formatted messages and exception text.

## 21. Database and Deployment Validation

**Do not create a password-column migration unless deployment inspection proves schema drift.** Source control maps the column to non-null `nvarchar(max)`.

Before staging/production deployment, use approved read-only database inspection to confirm:

- actual `Users.PasswordHash` type and maximum length can hold framework output;
- strict legacy-format count is understood;
- malformed/unknown count is understood and investigated without exporting values;
- any already-modern values are counted separately;
- no environment has a manually narrowed column.

Record only counts and schema metadata. Never paste hashes or credentials into tickets, logs, or this document. If drift shows a narrow column, stop deployment and create a separately reviewed schema correction; do not improvise truncation.

## 22. Legacy Migration Completion and Removal

The legacy verifier may be removed only when all conditions are true:

1. A read-only query using the same strict legacy predicate reports zero legacy hashes in every active environment.
2. Malformed/unknown accounts have an explicit disposition; they are not silently counted as migrated.
3. Operational migration/rehash logs show no legacy authentication for an agreed observation period (team must set the duration before deployment).
4. Backup retention and restore procedures cannot reintroduce a database containing legacy-only credentials without also deploying compatible verifier code.
5. An administrator records the zero-count verification and approves a later dedicated removal change.

Do not remove legacy support merely because time elapsed. Dormant users must not be forced to reset solely due to R2.

## 23. Documentation Changes

After implementation and tests pass, update only password-related statements in `Docs/README.md`:

- describe adaptive ASP.NET Core password hashing;
- state that existing salted SHA-256 values are verified temporarily and upgraded after successful authentication;
- state that new registration/password changes never create SHA-256 values;
- document the removal condition at a concise operational level.

Keep this implementation plan and the analysis as historical design records. Do not rewrite frontend, JWT, or unrelated remediation documentation.

## 24. Implementation Sequence

## Phase 1 — Establish Backend Test Infrastructure

### Goal

Create a runnable .NET test project and relational test fixture before changing authentication.

### Files

- Add `VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj`
- Add test fixture/support files
- Modify `VocabularyApp.sln`

### Changes

Create the xUnit project, references, shared SQLite fixture, JWT settings factory, and capturing logger. Add one baseline test that constructs the current `UserService` or verifies the fixture can persist/reload a `User`.

### Important Constraints

Do not alter production behavior. Do not use EF InMemory for concurrency tests. Do not add a mocking library unless hand-written fakes prove inadequate.

### Tests

Run the baseline fixture test and existing solution build.

### Verification

```powershell
dotnet build VocabularyApp.sln
dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
```

### Stop/Checkpoint

Build and baseline tests pass; only test infrastructure and solution membership changed.

## Phase 2 — Add Result Model and Verification-Only Legacy Component

### Goal

Capture the exact historical verifier behind a narrow, fully tested API.

### Files

- Add `Security/ILegacyPasswordVerifier.cs`
- Add `Security/LegacyPasswordVerifier.cs`
- Add `Security/PasswordVerificationStatus.cs`
- Add `Security/PasswordVerificationOutcome.cs`
- Add corresponding unit tests

### Changes

Implement strict delimiter/Base64/length/canonical checks and exact UTF-8 SHA-256 reconstruction with fixed-time byte comparison. Define result invariants.

### Important Constraints

No SHA-256 generation API. Do not change the historical algorithm. Do not touch `UserService` yet.

### Tests

Implement every legacy-verifier checklist item in section 18.

### Verification

Run focused tests, full tests, `dotnet build`, and `rg -n "SHA256" VocabularyApp.WebApi` to confirm SHA-256 exists only in old helper and new verifier.

### Stop/Checkpoint

All strict-format and compatibility tests pass; the new component cannot generate stored legacy values.

## Phase 3 — Add the Modern Application Password Service

### Goal

Centralize modern creation, modern verification outcomes, and legacy classification/migration results.

### Files

- Add `Security/IPasswordService.cs`
- Add `Security/PasswordService.cs`
- Add `PasswordServiceTests.cs`

### Changes

Inject framework and legacy verifiers, implement deterministic classification, map all framework outcomes, and generate replacements for legacy success/rehash-needed.

### Important Constraints

Do not try both algorithms indiscriminately. Do not catch all exceptions. Do not expose parsed credential content.

### Tests

Implement all `PasswordServiceTests` in section 18 using real and controlled framework hashers.

### Verification

Focused and full tests pass; a generated value is nonlegacy and verifies with real `PasswordHasher<User>`.

### Stop/Checkpoint

The password service behavior is complete and independent of `UserService`.

## Phase 4 — Register DI and Convert Registration

### Goal

Ensure all new accounts receive only modern hashes.

### Files

- Modify `Program.cs`
- Modify `UserService.cs`
- Add registration service tests

### Changes

Register the service graph, inject `IPasswordService`, construct `User` before hashing, and replace the registration `PasswordHelper.HashPassword` call.

### Important Constraints

Keep duplicate checks, response/JWT behavior, and DTOs unchanged. Do not convert login/password change yet in this checkpoint.

### Tests

Registration produces modern/nonlegacy hash and verifies with the framework; existing duplicate behavior remains.

### Verification

Build/test, then search to confirm only the password-change generation call remains: `rg -n "PasswordHelper.HashPassword" VocabularyApp.WebApi`.

### Stop/Checkpoint

Every newly registered row is modern; all tests pass.

## Phase 5 — Convert Password Change

### Goal

Make every successful password change store one modern hash of the new password.

### Files

- Modify `UserService.cs`
- Add password-change tests

### Changes

Verify through `IPasswordService`, reject failure/unknown without mutation, and directly hash the new password for every successful current format.

### Important Constraints

Never persist a migration hash of the old password. Preserve controller contract.

### Tests

Legacy-to-modern, modern-to-modern, wrong current password, malformed/unknown current hash.

### Verification

Build/test and confirm `rg -n "PasswordHelper.HashPassword" VocabularyApp.WebApi/Services` returns no results.

### Stop/Checkpoint

Production registration and password change cannot generate SHA-256.

## Phase 6 — Add Credential Concurrency Protection

### Goal

Make every credential write conditional on the password hash originally read.

### Files

- Modify `ApplicationDbContext.cs`
- Add concurrency integration tests

### Changes

Configure `PasswordHash` as a concurrency token. Add two-context password-change conflict test and validate SQLite relational behavior.

### Important Constraints

No row-version column, lock, retry loop, or migration. Do not accept last-writer-wins for credentials.

### Tests

Password-change conflict leaves the first committed new credential intact.

### Verification

Inspect generated SQL in a test/debug environment or assert `DbUpdateConcurrencyException`; run `dotnet ef migrations has-pending-model-changes` if available and confirm no schema migration is required.

### Stop/Checkpoint

Stale credential writes reliably conflict in the chosen relational test provider.

## Phase 7 — Refactor Login Persistence and Add Transparent Migration

### Goal

Support both formats, persist required upgrades before JWT issuance, and use one intentional successful-login save.

### Files

- Modify `UserService.cs`
- Possibly modify `IUserService.cs` only to remove now-unused `UpdateLastLoginAsync`
- Add login/migration service tests

### Changes

Implement section 11 algorithm; remove `UpdateLastLoginAsync` from login and, if unreferenced, from service/interface; handle required save failures and concurrency distinctly; create JWT last.

### Important Constraints

No token before required save. No mutation on verification failure. Do not swallow credential-write errors. Do not change `JwtHelper`.

### Tests

Service/integration scenarios 1-4, 6-7, 11-14, 17-18.

### Verification

Full build/test; source inspection shows JWT generation after required `SaveChangesAsync`; forced save failure returns no token.

### Stop/Checkpoint

Legacy and modern logins work, migration is durable, conflicts cannot restore old credentials, and all failure paths are nonmutating.

## Phase 8 — Retire Production PasswordHelper Usage and Validate Logging

### Goal

Isolate SHA-256 to temporary verification and prove credentials never enter logs.

### Files

- Modify `PasswordHelper.cs` without deleting it
- Add/complete logging tests

### Changes

Mark old helper obsolete/unreferenced and make legacy generation unreachable to normal flows. Add safe migration/conflict log events and sentinel logging tests.

### Important Constraints

Do not delete the helper during initial R2. Do not log hash format contents, salt, payload, or DTOs.

### Tests

Scenarios 15-16 plus source searches for prohibited log arguments.

### Verification

```powershell
rg -n "PasswordHelper\.(HashPassword|VerifyPassword)" VocabularyApp.WebApi
rg -n "SHA256" VocabularyApp.WebApi
dotnet test VocabularyApp.sln
```

First search has no production call sites; second shows only verification-only legacy code and the retained obsolete helper.

### Stop/Checkpoint

SHA-256 generation is unreachable from account flows and logs pass sentinel checks.

## Phase 9 — Deployment/Schema Validation and Documentation

### Goal

Confirm real schema assumptions and make password documentation accurate.

### Files

- Modify password-related text in `Docs/README.md`
- No migration file unless separately approved after proven drift

### Changes

Perform read-only environment checks from section 21, record non-sensitive counts, define the observation period, and update documentation.

### Important Constraints

Do not expose hashes. Do not create a migration based only on fear of length. Do not rewrite unrelated docs.

### Tests

Run full automated tests after documentation/source finalization.

### Verification

Schema is confirmed `nvarchar(max)` or deployment is stopped; documentation no longer calls SHA-256 industry-standard; `git diff --check` is clean.

### Stop/Checkpoint

Deployment evidence and legacy-removal rule are recorded and reviewed.

## Phase 10 — Final Regression and Security Review

### Goal

Demonstrate the complete R2 behavior and scope before merge.

### Files

- No planned production changes; only corrections discovered by review

### Changes

Run full solution build/tests, execute the security checklist, inspect diff/file scope, and perform staging smoke tests with synthetic legacy and modern accounts.

### Important Constraints

Do not bundle JWT, UI, reset, or other remediation work. Never test with real credentials in logs/scripts.

### Tests

All unit/service/integration tests plus registration, modern login, legacy migration, password change, and failure staging smoke tests.

### Verification

```powershell
dotnet build VocabularyApp.sln
dotnet test VocabularyApp.sln
git diff --check
git status --short
```

### Stop/Checkpoint

Every Definition of Done item is checked, review is approved, and rollback-compatible deployment artifacts are ready.

## 25. Git Commit/Checkpoint Recommendations

Use small commits after each phase passes; do not create these commits during planning.

1. `test: add backend authentication test infrastructure`
2. `feat: add strict legacy password verifier`
3. `feat: add adaptive password service`
4. `fix: use adaptive hashing for registration`
5. `fix: use adaptive hashing for password changes`
6. `fix: protect password writes with optimistic concurrency`
7. `fix: migrate legacy hashes during login`
8. `test: cover password migration failures and logging`
9. `docs: document adaptive password hashing migration`

At every boundary: build, run focused then full tests, inspect `git diff --stat`, confirm no unrelated file changed, and do not proceed with a red test suite.

## 26. Rollback Strategy

A naïve rollback to current SHA-256-only code is prohibited after the first modern hash is written: migrated and newly registered users would be unable to log in.

Deployment/rollback rules:

- deploy backward-compatible code that verifies modern and legacy formats before allowing modern writes;
- retain the last known-good artifact that understands both formats throughout the migration window;
- if defects appear, prefer a roll-forward fix;
- if rollback is unavoidable, roll back only to a version that still verifies both modern and legacy hashes;
- preserve database values; never convert modern hashes back to SHA-256;
- never attempt to recover or reconstruct plaintext passwords;
- do not restore an older database merely to regain legacy hashes, because that loses unrelated user data and invalidates migrated credentials;
- keep legacy verification deployed until section 22 removal conditions are satisfied.

If the defect is limited to migration writes, an emergency compatible release may temporarily disable new migration writes while retaining both verification paths and modern creation, followed promptly by a corrected roll-forward. This requires explicit security approval because it prolongs weak-hash retention.

## 27. Security Review Checklist

- [ ] Registration and password change have no reachable SHA-256 generation call.
- [ ] Legacy SHA-256 code exposes verification only and is labeled temporary.
- [ ] Strict legacy recognition requires one colon, canonical Base64, and two decoded 32-byte values.
- [ ] Historical digest comparison uses `CryptographicOperations.FixedTimeEquals`.
- [ ] Modern creation/verification uses `IPasswordHasher<User>`.
- [ ] Malformed/unknown values fail closed without endpoint-visible parsing errors.
- [ ] Wrong passwords and failed/unknown formats do not mutate hash or `LastLoginAt`.
- [ ] Required migration/rehash save succeeds before JWT generation.
- [ ] Required save failure or concurrency conflict returns no JWT.
- [ ] `PasswordHash` concurrency prevents stale login migration from restoring an old password.
- [ ] Logs and exception rendering contain no password, hash, salt, payload, or credential DTO.
- [ ] JWT helper/settings/architecture were not changed.
- [ ] Full ASP.NET Core Identity was not adopted.
- [ ] No password reset, UI redesign, database restructuring, or unrelated remediation was bundled.

## 28. Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Historical users cannot verify | Golden fixtures lock exact UTF-8 `password + saltBase64Text` behavior before service conversion |
| Malformed values throw | Central strict parser and narrow expected-exception handling; endpoint/service tests |
| Required save silently fails | Separate critical path, one save, no JWT until success, forced-failure test |
| Stale login restores old password | `PasswordHash` concurrency token; no retry; two-context race test |
| Concurrent password change makes stale normal login succeed | Treat concurrency conflict during successful-login timestamp save as login failure |
| Framework hash truncated in deployment | Read-only schema inspection; stop deployment on drift; no speculative migration |
| Sensitive values leak through logging | Structured content-free events and sentinel tests over messages/exceptions |
| Rollback locks out modern users | Retain dual-format compatible artifact; roll forward; prohibit SHA-256-only rollback |
| Dormant legacy accounts remain forever | Count strict legacy values and retain verifier until zero plus observation/backup conditions |
| Test provider hides SQL behavior | Use relational SQLite and validate affected-row concurrency; add SQL Server coverage if behavior differs |

## 29. Files to Add

| Proposed File | Purpose | Implementation Phase |
| ------------- | ------- | -------------------- |
| `VocabularyApp.WebApi/Security/IPasswordService.cs` | Application hashing/verification boundary | 3 |
| `VocabularyApp.WebApi/Security/PasswordService.cs` | Modern hasher adapter and format coordinator | 3 |
| `VocabularyApp.WebApi/Security/ILegacyPasswordVerifier.cs` | Narrow temporary legacy contract | 2 |
| `VocabularyApp.WebApi/Security/LegacyPasswordVerifier.cs` | Strict verification-only historical algorithm | 2 |
| `VocabularyApp.WebApi/Security/PasswordVerificationStatus.cs` | Explicit verification states | 2 |
| `VocabularyApp.WebApi/Security/PasswordVerificationOutcome.cs` | Status plus optional required replacement | 2 |
| `VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj` | .NET 8 xUnit test project | 1 |
| `VocabularyApp.WebApi.Tests/Security/LegacyPasswordVerifierTests.cs` | Legacy compatibility/malformed tests | 2 |
| `VocabularyApp.WebApi.Tests/Security/PasswordServiceTests.cs` | Modern/classification/result tests | 3 |
| `VocabularyApp.WebApi.Tests/Services/UserServiceAuthenticationTests.cs` | Registration/login/change persistence tests | 4-8 |
| `VocabularyApp.WebApi.Tests/Infrastructure/RelationalDatabaseFixture.cs` | Shared relational test database setup | 1 |
| `VocabularyApp.WebApi.Tests/Infrastructure/CapturingLogger.cs` | Sensitive-log assertions | 1, 8 |
| `VocabularyApp.WebApi.Tests/Infrastructure/ControlledPasswordHasher.cs` | Deterministic framework outcomes including rehash | 3, 7 |

File names may be consolidated where repository conventions favor fewer files, but responsibilities and test coverage must remain explicit.

## 30. Files to Modify

| Existing File | Exact Responsibility to Change | Why | Implementation Phase |
| ------------- | ------------------------------ | --- | -------------------- |
| `VocabularyApp.sln` | Add backend test project | Make authentication tests part of solution runs | 1 |
| `VocabularyApp.WebApi/Program.cs` | Register framework, legacy, and application password services | Replace static dependency with DI | 4 |
| `VocabularyApp.WebApi/Services/UserService.cs` | Modern creation; multi-format verification; migration; persistence ordering; concurrency handling | All credential flows converge here | 4-8 |
| `VocabularyApp.WebApi/Services/IUserService.cs` | Remove `UpdateLastLoginAsync` only if final search confirms no external caller | Avoid a misleading save-and-swallow credential path | 7 |
| `VocabularyApp.Data/ApplicationDbContext.cs` | Configure `PasswordHash` as concurrency token | Prevent stale credential overwrite without schema change | 6 |
| `VocabularyApp.WebApi/Helpers/PasswordHelper.cs` | Retain but obsolete/isolate; remove production use and generation reachability | Staged transition; do not delete during R2 deployment | 8 |
| `Docs/README.md` | Replace SHA-256 security claims with adaptive hashing/migration description | Keep user/developer docs accurate | 9 |

`VocabularyApp.WebApi/VocabularyApp.WebApi.csproj` and the test project may require test-only package references during Phase 1. Do not add a production Identity package unless compilation proves the Web SDK shared framework does not expose the required primitives; repository targeting strongly indicates it will.

## 31. Files Explicitly Not to Modify

| File/area | Why R2 does not require a change |
| --- | --- |
| `VocabularyApp.WebApi/Helpers/JwtHelper.cs` | HMAC-SHA256 JWT signing is unrelated and appropriate; token architecture remains intact |
| `VocabularyApp.WebApi/Configuration/JwtSettings.cs` | Password storage does not change issuer, audience, keys, algorithms, or expiration |
| `VocabularyApp.WebApi/Controllers/UsersController.cs` | Existing endpoints and service-result mapping can support selected policies |
| `VocabularyApp.WebApi/DTOs/UserDTOs.cs` | Current request/response shapes are sufficient; no hash metadata should be exposed |
| `VocabularyApp.WebApi/Controllers/WordsController.cs`, `QuizController.cs` | Unrelated features |
| `VocabularyApp.WebApi/Services/WordService.cs`, `QuizService.cs` | Unrelated services |
| `VocabularyApp.UI/src/app/**` | Registration/login payloads remain compatible; password-change UI is a separate feature |
| `VocabularyApp.Data/Migrations/**` | Committed `nvarchar(max)` fits modern output; concurrency-token metadata needs no schema change |
| `VocabularyApp.Data/Models/User.cs` | Fluent concurrency configuration avoids an entity/schema change |
| `test-api.http` | Manual examples need no contract change for R2 |
| Other remediation documents/items | R2 must not begin R3 or broaden security scope |

If direct implementation evidence contradicts one of these expectations, stop, document the reason in the PR, and obtain review before expanding file scope.

## 32. Definition of Done

- [ ] New registration stores an adaptive password hash.
- [ ] Password change stores an adaptive password hash.
- [ ] Existing SHA-256 users can authenticate.
- [ ] Successful SHA-256 login upgrades the stored value.
- [ ] Wrong legacy password does not modify the database.
- [ ] Modern `SuccessRehashNeeded` is persisted.
- [ ] Malformed/unknown stored values fail safely.
- [ ] Required credential-write failure prevents JWT issuance.
- [ ] Login migration cannot overwrite a concurrently changed password.
- [ ] Legacy SHA-256 generation is not used by production account flows.
- [ ] Legacy verification is isolated and temporary.
- [ ] No password or hash appears in logs.
- [ ] Automated backend tests cover both formats.
- [ ] All R2 tests pass.
- [ ] Full solution builds successfully.
- [ ] No unnecessary database migration was introduced.
- [ ] Deployed schema compatibility has been verified.
- [ ] Legacy-removal condition is documented.
- [ ] R2 documentation has been updated.
- [ ] No unrelated remediation work was included.
- [ ] Rollback artifact verifies both modern and legacy formats.
- [ ] Focused security review checklist is approved.
