# R2 Password Hashing Final Review

## 1. Executive Summary

**Status: Ready with Conditions**

Source review and a compile-only build indicate that R2 meets its implementation and
security design. Active account flows no longer generate or directly verify the historical
salted SHA-256 format. Strict legacy verification remains available for migration, and
successful legacy authentication replaces the stored value before a JWT is issued.

The remaining conditions are manual: the developer must run the automated tests, an
authorized operator must validate schema metadata and candidate counts in every intended
environment, and staging authentication smoke tests should be completed before production
deployment. Codex did not run tests or access any deployed database during this review.

## 2. Scope Reviewed

The branch was compared with `master` using merge base
`24d8650a8d5af19131d3a7950a050884f21ea509`. The review covered:

- password security production code in `Security`, `UserService`, and DI registration;
- the `PasswordHash` EF concurrency configuration;
- the backend relational, password, login, concurrency, and logging tests;
- the retained obsolete `PasswordHelper`;
- the user model, migrations, and model snapshot;
- password and deployment documentation under `Docs` and `Docs/Updates`; and
- the branch diff for JWT, controller, DTO, UI, migration, and unrelated changes.

The R2 diff contains password-security code, DI wiring, concurrency configuration, backend
tests, and password documentation. No unrelated remediation work was found. R1 and later
remediation items were not changed.

## 3. Registration Review

**PASS** — `UserService.CreateUserAsync` creates the tracked `User` before calling
`IPasswordService.HashPassword`, persists the user before JWT generation, and preserves the
existing duplicate-username and duplicate-email responses. The plaintext request password
is neither assigned to an entity property nor logged. `PasswordService.HashPassword`
delegates to ASP.NET Core `IPasswordHasher<User>`.

## 4. Password Change Review

**PASS** — `UserService.ChangePasswordAsync` verifies the current credential through
`IPasswordService`, accepts successful modern or legacy outcomes, and safely rejects failed
or malformed/unknown outcomes without saving. It deliberately ignores any proposed hash of
the old password, hashes only `newPassword`, and performs one credential save.

`DbUpdateConcurrencyException` is handled explicitly with no retry or reload-and-overwrite.
The service returns `false` on conflict, leaving the newer committed credential intact.

## 5. Login & Transparent Migration Review

**PASS** — `UserService.LoginAsync` follows the required sequence:

1. Load the tracked user.
2. Verify through `IPasswordService`.
3. Reject failed and malformed/unknown outcomes without mutation or JWT.
4. Apply a required legacy-migration or modern-rehash replacement.
5. Set `LastLoginAt`.
6. Save and apply the appropriate failure policy.
7. Generate the JWT only after persistence handling completes.

For a valid legacy credential, `PasswordService` creates a modern replacement with the
framework hasher. For `SuccessRehashNeeded`, it also creates a modern replacement. Both
replacement paths require a successful save before token issuance. Wrong credentials,
required-save failures, and concurrency conflicts return no JWT.

`UpdateLastLoginAsync` has been removed from both `UserService` and `IUserService`; repository
search found no remaining reference.

## 6. Legacy Compatibility Review

**PASS** — `LegacyPasswordVerifier` is verification-only and reproduces the historical
algorithm: SHA-256 over UTF-8 bytes of the plaintext password followed by the stored Base64
salt text. It requires exactly one colon at position 45, two 44-character encoded segments,
canonical Base64, and exactly 32 decoded bytes per segment. Digest comparison uses
`CryptographicOperations.FixedTimeEquals`.

`PasswordService` delegates to `ILegacyPasswordVerifier` only after strict recognition.
No active service calls `PasswordHelper.HashPassword` or `PasswordHelper.VerifyPassword`.

## 7. Concurrency Review

**PASS** — `ApplicationDbContext.OnModelCreating` configures `User.PasswordHash` with
`IsConcurrencyToken()`. EF therefore includes the originally loaded password hash in
credential-related update predicates. Login and password change catch
`DbUpdateConcurrencyException` separately, issue no retry, and do not reload and overwrite.
The relational tests model stale writes with independent contexts and assert that the newer
credential remains stored.

No rowversion, timestamp, lock, retry mechanism, column, or migration was added.

## 8. Persistence Failure Review

**PASS** — Legacy migration and modern rehash are credential-critical. Any persistence
failure on these paths fails authentication before JWT generation. A timestamp-only,
non-concurrency persistence failure during ordinary modern login retains the prior
noncritical policy and may allow authentication. A concurrency failure always fails login,
including the timestamp-only path, because credential verification may be stale.

This policy is explicit in `LoginAsync`; no generic save helper suppresses a credential
write failure.

## 9. Logging Safety Review

**PASS (source inspection; automated execution pending)** — Scoped logs in `UserService`
and `UsersController` use username, email where pre-existing, user ID, and generic event
text. `PasswordService` and `LegacyPasswordVerifier` do not log. No scoped statement logs
plaintext/current/new passwords, stored or replacement hashes, salts, Base64 segments,
parsed bytes, or serialized credential request DTOs.

`AuthenticationLoggingTests` uses unique sentinels for registration, successful and failed
login, password change, malformed storage, migration failure, rehash failure, and
concurrency failure. Its helper checks both formatted log messages and captured exception
text.

## 10. Backend Test Coverage Review

The backend project contains 54 `[Fact]` methods and 13 inline theory data rows, for 67
statically discoverable test cases. This is a source count, not an execution result.

Relevant coverage includes:

- `RelationalDatabaseFixtureTests`: baseline relational persistence and fresh-context read;
- `LegacyPasswordVerifierTests`: historical success, wrong password, strict separators and
  lengths, invalid/noncanonical Base64, decoded lengths, malformed input, and exact
  historical algorithm ordering;
- `PasswordServiceTests`: modern creation/success/failure, rehash, legacy migration,
  malformed/unknown handling, and exception boundaries;
- `PasswordVerificationOutcomeTests`: replacement invariants and non-exposing `ToString`;
- `UserServiceAuthenticationTests`: modern registration, duplicate behavior, legacy and
  modern password changes, nonmutation, and ignoring old-password rehash output;
- `CredentialConcurrencyTests`: raw stale EF write, service conflict handling, and ordinary
  password-change success;
- `LoginMigrationTests`: legacy migration, modern login, wrong-password nonmutation,
  rehash persistence, malformed/unknown storage, required-save failure, and login race;
- `AuthenticationLoggingTests`: plaintext, stored hash, replacement hash, malformed value,
  persistence-failure, and concurrency log safety.

**MANUAL VALIDATION REQUIRED** — these tests were inspected but not run during Phase 10.

## 11. Database/Schema Review

**PASS by source inspection; deployed validation pending** — `User.PasswordHash` is required
and has no maximum-length annotation. The current model snapshot maps it as required
`nvarchar(max)`, which accommodates framework password-hasher output. The concurrency token
changes EF update behavior only and needs no schema column.

No R2 migration exists and no migration was created during this review.

## 12. Deployment Validation Review

**PASS for documentation; MANUAL VALIDATION REQUIRED per environment** —
`R2-Password-Hashing-Deployment-Validation.md` provides a metadata-only schema query and an
aggregate-only candidate-count query. It returns no hashes and clearly states that SQL
shape counts cannot establish Base64 or cryptographic validity. It includes malformed and
unknown account guidance, the five legacy-removal conditions, an observation period left
for team decision, backup/restore compatibility, and the separation between legacy
password SHA-256 and JWT HMAC-SHA256.

An authorized operator still must run these checks in every intended deployment
environment and record only schema metadata and aggregate counts.

## 13. JWT Scope Review

**PASS** — no JWT helper, settings, algorithm, key handling, issuer, audience, expiration,
or claim file appears in the R2 diff. `Program.cs` changes only register password services.
Token generation remains in its existing implementation and is now deliberately ordered
after required credential persistence. JWT HMAC-SHA256 is unrelated to password hashing and
was not changed.

## 14. PasswordHelper Retirement Status

**PASS** — `PasswordHelper` remains for staged historical retention and is marked
`Obsolete` with direction to use `IPasswordService`. Its public legacy generator still
exists in the retained file, but repository search finds no active caller; registration,
login, and password change cannot reach it. The only active SHA-256 password compatibility
path is verification through `PasswordService` and `LegacyPasswordVerifier`.

The helper and legacy verifier may be deleted only in a later dedicated change after all
documented removal conditions are satisfied.

## 15. Security Checklist

| Item | Result |
| --- | --- |
| Registration creates modern adaptive hashes only | PASS |
| Password change creates modern adaptive hashes only | PASS |
| Login supports modern hashes | PASS |
| Existing strict legacy hashes can authenticate | PASS |
| Successful legacy login replaces the stored legacy hash | PASS |
| Modern `SuccessRehashNeeded` is persisted | PASS |
| Wrong password does not mutate credentials | PASS |
| Malformed/unknown hashes fail safely | PASS |
| Required credential save occurs before JWT issuance | PASS |
| Failed required save returns no JWT | PASS |
| Credential concurrency conflict returns no JWT | PASS |
| Stale request cannot restore an older password | PASS |
| `LastLoginAt` failure policy is separated from credential-write policy | PASS |
| No active `PasswordHelper` call remains | PASS |
| Legacy SHA-256 generation is unreachable from active account flows | PASS |
| Active legacy support is verification-only | PASS |
| Digest comparison is fixed-time | PASS |
| Passwords, hashes, salts, and replacement payloads do not appear in logs | PASS by source inspection; MANUAL VALIDATION REQUIRED for test execution |
| `PasswordHash` fits the source-controlled database schema | PASS |
| Deployed `PasswordHash` shape is compatible | MANUAL VALIDATION REQUIRED |
| No unnecessary R2 migration exists | PASS |
| JWT behavior is unchanged by R2 | PASS |
| No full ASP.NET Core Identity migration occurred | PASS |
| No unrelated remediation work was introduced | PASS |
| Legacy-removal condition is documented | PASS |
| Rollback guidance remains compatible with modern and legacy hashes | PASS |
| Full automated regression suite passes | MANUAL VALIDATION REQUIRED |
| Environment legacy/malformed candidate counts are reviewed | MANUAL VALIDATION REQUIRED |

## 16. Remaining Manual Actions

The developer/operator must:

1. Run the full automated test suite:

   ```powershell
   dotnet test
   ```

2. Optionally run the backend project directly:

   ```powershell
   dotnet test VocabularyApp.WebApi.Tests/VocabularyApp.WebApi.Tests.csproj
   ```

3. Run the documented metadata-only schema query against every intended environment.
4. Review aggregate legacy, modern-or-unknown, and malformed candidate counts without
   exporting credential values.
5. Resolve or record a disposition for malformed/unknown accounts.
6. Perform staging smoke tests for registration, modern login, legacy migration, password
   change, failed authentication, and concurrent credential behavior as appropriate.
7. Confirm that a rollback artifact capable of verifying both modern and legacy hashes is
   retained; never roll back to SHA-256-only authentication after modern writes begin.

## 17. Merge Readiness

**Ready after manual validation.** The branch is source-reviewed, scope-clean, and builds
successfully. It should be merged only after the developer-run tests pass and review does
not uncover an R2 defect.

## 18. Deployment Readiness

**Not yet cleared for production deployment.** Deployment is ready only after tests pass,
the intended environment's schema is confirmed compatible, candidate counts and malformed
accounts are reviewed, staging smoke tests are satisfactory, and a dual-format-compatible
rollback artifact is confirmed.

## 19. Outstanding Risks

- Automated tests were not executed during Phase 10, so runtime regression status remains
  unconfirmed.
- Actual deployed schema compatibility has not been inspected.
- Environment-specific legacy and malformed/unknown candidate counts are unknown.
- The team has not yet recorded the legacy-removal observation-period duration.
- SQLite concurrency tests require developer execution; deployed SQL Server behavior should
  also be exercised during staging validation.

No source-code defect requiring correction was found during this review.

## 20. Final Recommendation

R2 is **Ready with Conditions**. Run the full test suite, complete the documented
environment and staging checks, and retain a dual-format-compatible rollback artifact. If
those checks pass, the branch is suitable to merge and proceed through the normal
production-release approval process. Do not remove legacy verification or `PasswordHelper`
as part of this release.
