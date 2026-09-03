# R1 — JWT Signing Secret Analysis

## 1. Executive Summary

R1 addresses a critical secret-management defect: the repository formerly contained a JWT HMAC signing secret in committed application settings. Anyone with that value could potentially mint tokens accepted by an environment still using it.

Commit `d8408910be9e98590d5330f593505195429621ae` removed the committed secrets, introduced validated typed JWT configuration, and made the application fail at startup when secure external configuration is absent or weak. Later authentication integration tests cover valid, malformed, tampered, and expired tokens.

The source implementation is complete. Later production deployment records confirm that `JwtSettings__SecretKey` exists as an external SmarterASP application-pool variable and production login succeeds. The repository does not contain evidence proving when the formerly committed value was rotated or that a token signed with that exact retired value was rejected in production. That operational evidence remains the final R1 closeout condition.

## 2. Original Risk

Before R1, `SecretKey` values were committed in both `VocabularyApp.WebApi/appsettings.json` and `appsettings.Development.json`. `Program.cs` and `JwtHelper` read the secret directly from configuration with limited validation.

Removing a secret from the current file does not revoke it: Git history retains the old value. Every environment that could have used it must receive a new independently generated secret. Existing tokens signed with the retired key remain valid until expiry unless the environment rotates the key.

## 3. Current Architecture

- `JwtSettings` binds `JwtSettings:SecretKey`, issuer, audience, and expiration (`VocabularyApp.WebApi/Configuration/JwtSettings.cs:6-19`).
- Startup rejects a missing/blank secret, secrets under 32 UTF-8 bytes, blank issuer/audience, and nonpositive expiry (`JwtSettings.cs:21-47`).
- The same typed settings generate the HS256 signing key and token-validation parameters (`JwtSettings.cs:52-66`).
- `Program.cs` validates once at startup, registers the settings, and uses them for bearer authentication (`VocabularyApp.WebApi/Program.cs:27-34`).
- `JwtHelper` uses the same settings for token generation and direct validation (`VocabularyApp.WebApi/Helpers/JwtHelper.cs:9-64`).
- No JWT secret exists in the current application-settings files.

Production configuration uses the environment-variable form `JwtSettings__SecretKey`. Secret values must never be committed, logged, copied into documentation, or returned by an endpoint.

## 4. Current Validation Behavior

JWT validation requires:

- a valid issuer signing key;
- the configured issuer;
- the configured audience;
- an unexpired token with zero clock skew; and
- HS256 as an allowed algorithm.

Evidence: `VocabularyApp.WebApi/Configuration/JwtSettings.cs:55-66`.

Current integration coverage includes a usable issued token, malformed-token rejection, tampered-signature rejection, expired-token rejection, authenticated identity resolution, and anonymous rejection (`VocabularyApp.WebApi.Tests/Integration/AuthenticationApiTests.cs:18-45,117-139,172-234,251-266`). Test secrets are isolated fake values supplied by test infrastructure.

## 5. Production Evidence

`Docs/Updates/R5-production-deployment.md` records that production has an external `JwtSettings__SecretKey` application-pool variable and that login passed after the deployment restart. This proves external configuration is present and usable at that checkpoint.

It does not prove that the configured value differs from every formerly committed value. It also does not record a controlled request using a token signed with the retired key. R1 must not be described as fully operationally closed until those facts are confirmed without exposing either key or token.

## 6. Required Final Verification

1. Confirm through the hosting secret-management interface that production uses a newly generated value that is not any value formerly committed to Git. Record confirmation only, never the value.
2. Confirm development, staging, and production use independent secrets.
3. Restart/recycle the application after rotation.
4. Confirm a fresh login returns a token accepted by a protected endpoint.
5. Confirm a token signed with the retired key is rejected with HTTP 401.
6. Confirm malformed, tampered, and expired tokens remain rejected.
7. Confirm the deployed application fails safely if the secret is removed or is shorter than 32 bytes in a non-production test environment; do not intentionally break production for this check.
8. Confirm no JWT secrets appear in current tracked files, build artifacts intended for distribution, logs, or documentation.

## 7. Scope Boundaries

R1 does not change password hashing, authorization policy, refresh-token design, token revocation lists, user roles, session persistence, or OAuth/OpenID Connect. Those require separate work if desired.

## 8. Assessment

**SOURCE IMPLEMENTATION COMPLETE — PRODUCTION ROTATION VERIFICATION PENDING**

