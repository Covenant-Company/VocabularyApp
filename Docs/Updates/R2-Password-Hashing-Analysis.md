# R2 Password Hashing Analysis

## 1. Executive Summary

VocabularyApp currently stores passwords as a cryptographically random 32-byte salt plus one fast SHA-256 digest. `PasswordHelper.HashPassword` generates the legacy value, and `PasswordHelper.VerifyPassword` validates it (`VocabularyApp.WebApi/Helpers/PasswordHelper.cs:11-55`). New registration and authenticated password change both create this format; login and password change both verify it (`VocabularyApp.WebApi/Services/UserService.cs:53,108,205,212`). SHA-256 is deliberately fast and therefore unsuitable for password storage even though the implementation uses a unique random salt.

R2 should introduce an injectable application password-hashing boundary backed by ASP.NET Core's `IPasswordHasher<User>`/`PasswordHasher<User>`, retain only a narrowly scoped legacy verifier, route registration and password change through the modern hasher, and make login recognize, verify, and upgrade legacy values. Full ASP.NET Core Identity adoption is not required.

Transparent migration is feasible. Login loads a tracked `User`, already performs a write for `LastLoginAt`, and calls `SaveChangesAsync` through the same scoped `ApplicationDbContext` (`UserService.LoginAsync` and `UpdateLastLoginAsync`, lines 89-169). The important design condition is that hash-upgrade persistence must not inherit the current `UpdateLastLoginAsync` behavior of swallowing write failures. A successful response must not imply that a required credential migration was persisted when it was not.

Database evidence indicates **no schema change appears necessary**: `User.PasswordHash` is a required `string`, mapped to non-null `nvarchar(max)` with no maximum length (`User.cs:18-19`; initial migration lines 31-41; current snapshot lines 227-264). This accommodates both the 89-character legacy value and standard ASP.NET Core password-hasher output.

## 2. Current Architecture

The password-related server architecture is:

`HTTP/JSON -> UsersController -> IUserService/UserService -> static PasswordHelper -> User entity -> ApplicationDbContext -> SQL Server`

Authentication success additionally flows from `UserService` to `JwtHelper` and then back through `UsersController` as an `AuthResponse`.

| Component | Evidence | Current responsibility |
| --- | --- | --- |
| `UsersController` | `VocabularyApp.WebApi/Controllers/UsersController.cs`, `Register`, `Login`, `ChangePassword` | API endpoints, model-state checks, current-user claim extraction, status-code mapping, username/user-ID logging |
| `CreateUserRequest`, `LoginRequest`, `AuthResponse` | `VocabularyApp.WebApi/DTOs/UserDTOs.cs:14-44` | Registration/login input validation and authentication output; password is accepted only in request DTOs and is not returned |
| `ChangePasswordRequest` | `UsersController.cs:264-271` | Accepts required current password and a new password of 6-100 characters |
| `IUserService` | `VocabularyApp.WebApi/Services/IUserService.cs:5-40` | Service contract for account creation, login, and authenticated password change |
| `UserService` | `VocabularyApp.WebApi/Services/UserService.cs` | User lookup, hashing/verification calls, persistence, last-login update, JWT creation |
| `PasswordHelper` | `VocabularyApp.WebApi/Helpers/PasswordHelper.cs` | Static legacy hash creation and verification; not injectable or registered in DI |
| `User` | `VocabularyApp.Data/Models/User.cs:5-28` | EF Core entity containing required `PasswordHash` |
| `ApplicationDbContext` | `VocabularyApp.Data/ApplicationDbContext.cs:6-32` | Scoped EF Core unit of work and `Users` set; user keys and unique username/email indexes |
| `JwtHelper` | `VocabularyApp.WebApi/Helpers/JwtHelper.cs:9-45` | Generates JWT only after password verification/registration persistence |
| `Program` | `VocabularyApp.WebApi/Program.cs:20-52,105-108` | Registers SQL Server context, JWT authentication/authorization, scoped user service and JWT helper |

The Angular client posts registration and login passwords directly to the corresponding API endpoints (`VocabularyApp.UI/src/app/services/auth.service.ts:20-32`). Its persisted session contains only token and user DTO, not a password or password hash (lines 63-66). No client method or route for password change was found.

## 3. Current Registration Flow

1. `POST /api/users/register` reaches `UsersController.Register` (`UsersController.cs:32-35`). `[ApiController]` and explicit `ModelState` handling enforce the annotations on `CreateUserRequest`: username 3-100, valid email up to 200, password 6-100 (`UserDTOs.cs:14-27`). The Angular form is slightly narrower for username (3-50) and sends only username, email, and password (`signup.component.ts:24-29,44-56`).
2. The controller logs the username, then invokes `IUserService.CreateUserAsync` (`UsersController.cs:46-56`).
3. `UserService.CreateUserAsync` performs case-insensitive username and email existence checks with tracked EF queries (`UserService.cs:22-50`). Database unique indexes also exist (`ApplicationDbContext.cs:27-32`).
4. The service calls static `PasswordHelper.HashPassword(request.Password)` and assigns the result to `User.PasswordHash` (`UserService.cs:52-60`). This is the exact replacement-hasher entry point for registration.
5. `_context.Users.Add(user)` marks the entity Added and `_context.SaveChangesAsync()` inserts it into `Users.PasswordHash` (`UserService.cs:62-63`).
6. Only after persistence, the service maps a password-free `UserDto`, generates a JWT, and returns success (`UserService.cs:67-76`). The controller wraps it in HTTP 200; duplicate/business failures return HTTP 400.

The eventual implementation should hash via the new injected abstraction at step 4. New registrations must never invoke the legacy generator.

## 4. Current Login Flow

1. `POST /api/users/login` receives `LoginRequest` in `UsersController.Login` (`UsersController.cs:79-98`). Both username and password are `[Required]` (`UserDTOs.cs:30-37`).
2. `UserService.LoginAsync` queries `_context.Users.FirstOrDefaultAsync` by case-insensitive username (`UserService.cs:89-105`). There is no `AsNoTracking`, so the returned `User` is tracked by the scoped context.
3. The service calls `PasswordHelper.VerifyPassword(request.Password, user.PasswordHash)` (`UserService.cs:107-116`). A missing user or failed verification produces the same client message, `Invalid username or password`.
4. On success it calls `UpdateLastLoginAsync(user.Id)` (`UserService.cs:118-120`). `FindAsync` will normally return the already tracked entity, assigns `LastLoginAt`, and calls `SaveChangesAsync` (`UserService.cs:161-170`). Thus login already performs a database write in the same context.
5. The service then maps the tracked entity, generates a JWT through `JwtHelper.GenerateToken`, and returns success (`UserService.cs:121-132`). The controller returns HTTP 200; authentication failure becomes HTTP 401 (`UsersController.cs:100-109`).

The safest architectural migration point is inside the service-level credential verification operation while the tracked `User` is available, before JWT issuance. A successful legacy verification can replace `user.PasswordHash`; modern verification can do the same when it returns `SuccessRehashNeeded`. The password-hash update and `LastLoginAt` should then be persisted deliberately through the same scoped context, preferably in one `SaveChangesAsync` call.

The present `UpdateLastLoginAsync` catches and suppresses every persistence exception (`UserService.cs:172-176`). That is acceptable only for its stated noncritical timestamp purpose. If a hash upgrade is attached to the tracked entity before that call, a failed `SaveChangesAsync` would also fail to save the upgrade, but the exception would be swallowed and login would still issue a token. R2 must define and test a distinct persistence policy for credential upgrades.

## 5. Current Password Change/Reset Flows

### Authenticated password change

`POST /api/users/change-password` is protected by `[Authorize]` (`UsersController.cs:165-170`). The controller validates `ChangePasswordRequest`, obtains the `ClaimTypes.NameIdentifier` user ID from the JWT, and calls `ChangePasswordAsync(userId, currentPassword, newPassword)` (`UsersController.cs:174-201`). The service:

1. loads a tracked user with `FindAsync`;
2. verifies the current password through the legacy `PasswordHelper.VerifyPassword`;
3. generates another legacy value through `PasswordHelper.HashPassword(newPassword)`; and
4. persists it with `SaveChangesAsync` (`UserService.cs:196-216`).

Both verification and new-password generation need to move behind the R2 abstraction. Correctly changing a password should always store a modern hash, regardless of whether the current stored hash is legacy or modern. No Angular password-change UI/client call was found, but the server endpoint exists.

### Other password-setting flows

Repository-wide searches found no forgotten-password reset, token-based reset, administrative reset, initial-password import, or other password replacement path. Those flows do not exist in the inspected source. `test-api.http` covers manual registration and login requests only and does not exercise password change (`test-api.http:4-21`).

## 6. Existing Password Storage Format

`PasswordHelper` defines the complete legacy format (`VocabularyApp.WebApi/Helpers/PasswordHelper.cs:6-56`):

- Algorithm: one SHA-256 digest.
- Salt generation: a fresh 32-byte array filled by `RandomNumberGenerator.Create().GetBytes`; it is cryptographically random and generated per call (`HashPassword`, lines 13-18).
- Salt encoding: standard Base64, producing 44 characters for 32 bytes (`line 20`).
- Hash input: UTF-8 bytes of the literal concatenation `password + salt`, where `salt` is the 44-character Base64 text—not the raw salt bytes (`HashPasswordWithSalt`, lines 50-54). There is no separator between password and salt inside the digest input.
- Digest encoding: standard Base64 of the 32-byte SHA-256 result, also 44 characters (`line 55`).
- Stored form: exactly `$saltBase64:$digestBase64`, with one colon, no algorithm/version marker (`lines 21-24`). Values produced by this implementation are 89 characters: 44 + 1 + 44.

Verification splits on every colon and requires exactly two parts, recomputes SHA-256 with the first part as salt text, then uses ordinary string `==` against the second part (`PasswordHelper.VerifyPassword`, lines 30-43). The comparison is not explicitly constant-time. Because arbitrary salt text is accepted, verification recognizes a broader set than values actually generated by the application. Any split-count failure or exception returns `false` due to the blanket `try/catch` (`lines 32-47`); malformed values therefore fail closed today, though without format-specific diagnostics.

No actual user data or production hash was inspected.

## 7. Database Impact

`User.PasswordHash` is a non-nullable C# `string` initialized to empty and annotated `[Required]` (`VocabularyApp.Data/Models/User.cs:18-19`). `ApplicationDbContext` adds no length override (`ApplicationDbContext.cs:26-32`). The initial migration created `Users.PasswordHash` as non-null `nvarchar(max)` (`20251004202304_InitialCreate.cs:31-41`), and the current snapshot retains required `nvarchar(max)` (`ApplicationDbContextModelSnapshot.cs:227-264`). No later migration modifies this column; later designer files continue to represent it as required without a maximum.

**No schema change appears necessary.** `nvarchar(max)` is far larger than both the current 89-character value and the encoded output produced by the standard ASP.NET Core password hasher. There is no repository-visible maximum-length or nullability conflict. R2 should still verify the deployed database matches the committed migrations, but that is an environment validation, not evidence for creating a migration.

## 8. Proposed Architectural Direction

Introduce an application-specific, injectable credential-hashing boundary consumed by `UserService`. It should express outcomes the service needs—failed, succeeded, and succeeded with replacement hash—rather than expose format parsing throughout business logic. Internally it can:

- use `IPasswordHasher<User>`/`PasswordHasher<User>` for every new hash and for modern verification;
- delegate recognized legacy values to a narrowly scoped, verification-only legacy component; and
- return a modern replacement after a successful legacy verification or modern `SuccessRehashNeeded` result.

This boundary replaces `PasswordHelper.HashPassword` and `PasswordHelper.VerifyPassword` responsibilities at their three call sites, while isolating legacy logic for later deletion. Passing the `User` instance to `IPasswordHasher<User>` fits its API and current tracked-entity flow. An application abstraction remains beneficial because `IPasswordHasher<User>` alone does not define legacy recognition/migration policy, malformed/unknown handling, or the persistence-facing replacement result.

`VocabularyApp.WebApi` targets `net8.0` with the Web SDK (`VocabularyApp.WebApi.csproj:1-5`). The project does not reference or configure ASP.NET Core Identity stores, managers, or Identity EF models; only JWT bearer authentication is configured (`Program.cs:23-32`). The password-hashing primitives can be used independently from the ASP.NET Core shared framework. Using them does **not** require adopting full Identity, changing the `User` entity to an Identity type, replacing JWT, or adding Identity database tables.

## 9. Legacy Migration Analysis

The conceptual service flow should be:

1. Load the user as the current tracked query does.
2. Classify the stored value without using the submitted password or logging the stored value.
3. For a recognized modern value, call the modern hasher. On `Failed`, fail normally. On success, continue. On `SuccessRehashNeeded`, generate/accept the recommended replacement and mark `PasswordHash` for update.
4. For a strictly recognized legacy value, invoke only the legacy verifier. On failure, return the normal invalid-credentials result without mutation. On success, generate a modern hash and assign it to `PasswordHash`.
5. For malformed or unknown values, fail authentication safely, do not mutate, and optionally record a content-free diagnostic/metric.
6. Persist any required hash replacement before issuing the JWT. The existing last-login update can be coalesced into that save.

Password change should verify through the same multi-format boundary but always replace the stored value with a newly generated modern hash when the current password is correct. Registration should call only modern hash creation.

For this application's apparent size, migration completion can be established by an administrative, read-only count using the same strict legacy-format predicate, optionally paired with counters for successful legacy upgrades and unknown/malformed encounters. Remove the legacy verifier only after the database count is zero across all deployed databases/backups that may be restored and an agreed observation window shows no legacy authentications. Elapsed time alone is insufficient because dormant accounts may remain.

## 10. Hash Format Detection

The two current formats are structurally distinguishable without attempting both verifiers indiscriminately:

- A generated legacy value has exactly one colon, exactly 44 characters on each side, and each side canonically decodes from standard Base64 to exactly 32 bytes. Strict recognition should enforce all of these properties. This is safer than the current `Split(':')` count alone.
- ASP.NET Core `PasswordHasher<User>` output is a single Base64-encoded, versioned binary payload. It does not use the legacy colon delimiter. Its embedded format marker and structural validation should be left to the modern hasher rather than duplicating all framework parsing in application code.
- A colon-bearing string that fails strict legacy validation is malformed/unknown, not legacy.
- A no-colon string may be offered to the modern verifier inside an exception-safe adapter. A modern verification failure is authentication failure; values not structurally accepted by it are unknown/malformed. No fallback to legacy should occur after a value has failed strict legacy classification.

For future maintainability, all newly generated values should remain the framework's self-describing output; an additional application prefix is optional, not necessary for distinguishing the known current pair. Centralizing classification in one abstraction is essential. If explicit application prefixes are introduced later, the design must still recognize unprefixed ASP.NET Core hashes already written during R2 and must account for added length; the existing column has ample capacity.

## 11. Security and Logging Considerations

- No inspected server log template includes a plaintext password or `PasswordHash`. Logs use username, email on registration success, user ID, validation error messages, and exceptions (`UsersController.cs:46-66,97-113,190-206`; `UserService.cs:65,78-80,99-136,207-220`). `MapUserToDto` omits `PasswordHash` (`UserService.cs:225-234`).
- Request DTOs necessarily carry plaintext passwords from controller to `UserService`; they are not persisted directly or returned. Keep their lifetime and propagation limited to the credential boundary.
- Do not log stored values, submitted values, Base64 parsing input, or exceptions enriched with either. Telemetry should record only format category/outcome and non-sensitive identifiers if needed.
- The legacy string equality at `PasswordHelper.cs:42` is not explicitly constant-time. The verification-only replacement should compare decoded digest bytes with `CryptographicOperations.FixedTimeEquals` after strict decoding.
- Malformed legacy data currently fails closed because `VerifyPassword` catches all exceptions. The new boundary must preserve safe failure but should use narrow parsing/exception handling rather than a blanket catch that can hide programming failures.
- Random 32-byte salts are neither predictable nor reused by the generator. The weakness is the single fast SHA-256 operation, not salt generation.
- Model validation error logging contains annotation messages, not request values in current annotations. Avoid enabling request-body logging for these endpoints.
- Failed authentication currently does not intentionally mutate a user because `UpdateLastLoginAsync` occurs only after successful verification. R2 must preserve that property for failed, malformed, and unknown cases.

## 12. Persistence and Concurrency Considerations

The username lookup returns a tracked entity, and `FindAsync` in `UpdateLastLoginAsync` normally resolves that same tracked instance. This makes a single-save update of `PasswordHash` and `LastLoginAt` straightforward. Login currently writes, but the timestamp helper owns the save and suppresses failures (`UserService.cs:161-176`). R2 should make credential-upgrade persistence explicit and observable.

Meaningful edge cases are:

- **Concurrent legacy logins:** both requests can verify the same legacy value and generate different valid modern hashes. With no concurrency token on `User`, last writer wins. Both hashes represent the same password, so correctness is retained, but writes and migration metrics may duplicate. This does not justify broad locking for this application.
- **Password change racing login migration:** unlike two login upgrades, this can overwrite a newly changed password with a modern hash of the old password. The entity has no row-version/concurrency configuration (`User.cs`; `ApplicationDbContext.cs:26-32`). R2 should use optimistic concurrency or a conditional update/check based on the originally read hash for credential replacement, and treat a conflict by not overwriting newer credentials.
- **Persistence failure:** a legacy password was valid, but the required upgrade did not persist. The safest default is to fail login and withhold the JWT when a required credential write fails; otherwise repeated use of the weak hash is silently accepted. If product policy chooses availability over migration durability, that exception must be explicit, observable, and tested—not inherited accidentally from `UpdateLastLoginAsync`.
- **Atomicity:** one `SaveChangesAsync` for the hash replacement and `LastLoginAt` is sufficient; no multi-resource transaction is evident. JWT creation should follow the successful save.
- **Modern rehash recommendation:** treat it with the same persistence and concurrency rules as legacy migration.

## 13. Dependency Injection Impact

`ApplicationDbContext`, `IUserService -> UserService`, and `JwtHelper` are scoped registrations (`Program.cs:20-21,44-49`). `PasswordHelper` is static and has no registration; `UserService` calls it directly (`UserService.cs:53,108,205,212`). JWT bearer authentication and authorization are separate concerns (`Program.cs:23-32,105-106`).

The natural change is to register the application password-hashing boundary in `Program.cs`, normally scoped or singleton depending on whether its implementation is stateless, and inject it into `UserService` alongside the context, JWT helper, and logger. Register the framework `IPasswordHasher<User>` (or construct it behind the adapter through DI) and keep the legacy verifier internal or separately injectable for isolated tests. No controller injection is needed because credential decisions and persistence belong in the service layer.

## 14. Testing Baseline

The solution contains only the Web API and Data projects (`VocabularyApp.sln:6-9`); no .NET test project exists. Repository search found no unit, service, controller, integration, or authentication tests for backend registration, login, password verification/change, malformed hashes, or persistence.

Angular has `auth.service.spec.ts`, `login.component.spec.ts`, and `signup.component.spec.ts`, but each only asserts that the service/component can be created (`auth.service.spec.ts:5-15`; login and signup specs: lines 5-22). They do not exercise requests, form submission behavior, password handling, or server hashing. `test-api.http` contains manual registration/login examples and is not automated.

Current test seams are limited. `UsersController` already depends on `IUserService`, so controller behavior can be unit-tested with a fake/mock. `UserService` depends on an EF `DbContext`, injectable `JwtHelper`, and logger, but static `PasswordHelper` prevents controlled verification outcomes. R2's injected credential abstraction creates the needed seam. Service persistence tests should use a relational provider with realistic tracking/concurrency behavior (for example ephemeral SQL Server or SQLite where behavior is compatible), not only mocks; modern and legacy hasher units can be tested independently.

## 15. Required Test Coverage

| Scenario | Likely participating classes/layers |
| --- | --- |
| 1. Existing legacy user logs in | Credential abstraction/legacy verifier, `UserService.LoginAsync`, tracked `User`, test database; controller mapping optionally in an integration test |
| 2. Successful legacy login replaces SHA-256 hash | Same as 1 plus `ApplicationDbContext.SaveChangesAsync`; reload user and assert modern verification succeeds and value is no longer legacy |
| 3. Incorrect legacy password does not migrate | Legacy verifier, `UserService`, change tracker/database; assert hash and `LastLoginAt` are unchanged |
| 4. New registration creates only modern hash | `UsersController.Register` validation as appropriate, `UserService.CreateUserAsync`, modern hasher, database |
| 5. Password change creates only modern hash | Authorized controller claim path, `UserService.ChangePasswordAsync`, multi-format verification, modern hasher, database |
| 6. Modern hashes authenticate correctly | Modern adapter, `UserService.LoginAsync`, JWT helper/fake, database |
| 7. Modern hash upgrades on rehash recommendation | Fake/control modern hasher returning `SuccessRehashNeeded`, `UserService`, persistence |
| 8. Malformed stored hashes fail safely | Classifier/adapter with truncated, invalid Base64, extra-colon, wrong-length inputs; service/controller integration to assert normal failure |
| 9. Unknown formats fail safely | Classifier/modern adapter and service; assert no legacy fallback and no exception response |
| 10. Passwords are never logged | Controller/service tests with a capturing logger and unique sentinel plaintext |
| 11. Password hashes are never logged | Same capturing logger with unique sentinel stored value and thrown parsing/persistence paths |
| 12. Failed authentication does not mutate user | `UserService`, EF change tracker and reloaded database row for wrong password, malformed, and unknown values |

Additional R2 tests should cover registration validation boundaries, modern/legacy password change, write failure policy, concurrent/conditional replacement conflict, last-login persistence with an upgrade, legacy canonical-format recognition, modern verifier exceptions/malformed payload behavior, and removal-readiness query classification.

## 16. Files Likely to Change

| File | Current Responsibility | Expected R2 Impact | Reason |
| ---- | ---------------------- | ------------------ | ------ |
| `VocabularyApp.WebApi/Services/UserService.cs` | Registration, login, password change, credential persistence | Inject hashing boundary; replace four static calls; persist login upgrades and rehash recommendations safely | All password creation/verification paths converge here |
| `VocabularyApp.WebApi/Program.cs` | DI and authentication setup | Register modern hasher and application credential abstraction/legacy verifier | New services must be injectable |
| `VocabularyApp.WebApi/Helpers/PasswordHelper.cs` | Static SHA-256 generation and verification | Expected to cease production use; legacy verification logic may be narrowed/moved, but removal waits for migration completion | Current generator must never create new hashes; legacy verification remains temporarily necessary |
| `VocabularyApp.WebApi/Services/IUserService.cs` | User-service contract | Possibly refine return semantics only if persistence/migration outcomes require it; may remain unchanged | Existing public methods can support R2, so changes are not automatically required |
| `VocabularyApp.WebApi/VocabularyApp.WebApi.csproj` | Web API target/framework dependencies | Likely no package change; confirm password-hasher types resolve from the ASP.NET Core shared framework | Full Identity package/framework adoption is unnecessary |
| `VocabularyApp.Data/Models/User.cs` | User persistence model | Possibly add optimistic concurrency metadata if conditional replacement is implemented through EF concurrency; no hash-length change needed | Prevent password-change/login-upgrade lost updates |
| `VocabularyApp.Data/ApplicationDbContext.cs` | EF model configuration | Possibly configure credential concurrency if chosen; otherwise no password-column change | Concurrency is the only repository-supported potential model impact |
| New backend test project files (not currently present) | None | Add unit and integration coverage for hashing and auth flows | There is no backend automated baseline |
| `Docs/README.md` | Describes SHA-256 as current password security (`README.md:31,106`) | Update documentation after implementation | Current claims will become inaccurate and call SHA-256 “industry-standard” |

`UsersController.cs`, password request DTOs, Angular files, migrations, and JWT code do not appear to require R2 changes unless the implementation deliberately changes service result semantics or exposes a password-change UI. They should not be changed merely to accommodate hashing.

## 17. New Components Likely Needed

- An application credential-hashing/verifying interface that returns a verification outcome and optional replacement hash.
- A modern implementation/adapter over `IPasswordHasher<User>`.
- A verification-only legacy SHA-256 component containing strict format recognition and constant-time digest comparison. It must not expose a legacy hash-generation path to registration or password change.
- A centralized format classifier, either part of the application adapter or a small internal collaborator.
- Backend test project(s), test database fixture, capturing logger, and controlled/fake modern hasher for `SuccessRehashNeeded` and failure cases.
- Optionally, a small non-sensitive migration metric/counting facility. It is useful but should not be allowed to log credential contents.

Exact names and file placement belong in the later implementation plan.

## 18. Risks and Edge Cases

- Existing values that contain one colon but were not produced by the generator must not be accepted merely because a recomputed string happens to match; enforce decoded lengths and canonical Base64.
- Incorrect legacy passwords, malformed hashes, unknown formats, and modern verification failure must all return ordinary authentication failure and make no mutation.
- Framework verification must be wrapped at the application boundary so malformed values cannot become unhandled endpoint exceptions.
- A failed required rehash save can leave SHA-256 active; current swallowed timestamp-save errors make this especially easy to implement incorrectly.
- Concurrent legacy logins can generate multiple replacements; last-write-wins is harmless only when both are hashes of the same password.
- Concurrent password change versus login migration can restore the old password unless replacement is conditional/concurrency-protected.
- `nvarchar(max)` removes truncation risk in the committed schema, but deployment schema drift should be checked before release.
- Logging or exception enrichment must never include request DTOs, submitted passwords, or stored hashes.
- `UserService.CreateUserAsync` returns `ex.Message` to the controller (`UserService.cs:78-85`). It does not currently include passwords, but exposing internal exception text is outside R2 and should not be extended into hash parsing/persistence errors.
- Account enumeration timing remains possible because a missing user performs no hash work. Addressing it is a broader authentication-hardening item, not required to replace SHA-256.

## 19. Out-of-Scope Findings

- JWT uses HMAC-SHA256 for token signatures (`JwtHelper.cs:23-27`). This is a different, appropriate use of SHA-256 and must not be changed under R2.
- Registration returns a JWT from the API (`UserService.cs:67-76`) while the current Angular UI ignores it and redirects to login (`signup.component.ts:56-63`). This behavior mismatch is unrelated to password storage.
- `AuthResponse` returned by the server has no `ExpiresAt`, while the Angular `LoginResponse` expects it and passes it to `setSession`; `setSession` does not use the argument (`UserDTOs.cs:39-44`; `user.model.ts:14-20`; `auth.service.ts:27-30,63-67`). This is unrelated to R2.
- The manual REST file documents a `PUT /api/users/profile` endpoint that is not present in `UsersController` (`test-api.http:33-40`).
- Internal exception text is returned on registration failure (`UserService.cs:78-85`), while other flows return generic errors. General error-contract hardening is outside R2 except that new hashing errors must not leak secrets.
- No password reset flow exists. Designing reset tokens/email delivery is a separate feature.

## 20. Open Questions

Repository inspection cannot answer:

1. Does every deployed database match the committed `nvarchar(max)` schema, or has any environment drifted/manual schema been applied?
2. How many deployed accounts currently have strict legacy-format, modern-format, or malformed/unknown values? No database contents were inspected.
3. Should product policy fail an otherwise correct legacy login if its mandatory hash upgrade cannot be persisted, or permit login while emitting operational telemetry? Security favors failing before JWT issuance, but this is a product availability decision.
4. What deployment-wide observation window and backup/restore policy must be satisfied before declaring the legacy verifier removable?

## 21. Implementation Readiness Assessment

**Ready with Conditions.**

The repository provides enough evidence to create a detailed implementation plan: every current creation and verification call site is known; the exact legacy algorithm and format are known; registration, login, password change, tracking, save behavior, DI, schema, and test gaps are traceable; and the database column needs no repository-level migration.

Before implementation is finalized, the plan must resolve two conditions: (1) define the required-login behavior when an upgrade write fails, and (2) select a conditional/optimistic-concurrency strategy so a login upgrade cannot overwrite a concurrent password change. Before deployment—not before writing the plan—the team should also verify deployed column shape and obtain a non-sensitive baseline count of strict legacy/unknown formats. None of these conditions requires full ASP.NET Core Identity, JWT changes, forced password resets, or a database restructuring.
