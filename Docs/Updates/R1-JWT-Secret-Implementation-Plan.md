# R1 — JWT Signing Secret Implementation Plan and Record

## 1. Objective

Remove JWT signing secrets from tracked configuration, require strong external configuration, and ensure token generation and validation use one validated settings object.

## 2. Implemented Changes

Commit `d8408910be9e98590d5330f593505195429621ae` completed the source work:

- removed `JwtSettings:SecretKey` from committed production and development settings;
- added `VocabularyApp.WebApi/Configuration/JwtSettings.cs`;
- required at least 32 UTF-8 bytes for the HS256 secret;
- validated issuer, audience, and expiration at startup;
- centralized signing-key and validation-parameter creation;
- registered validated settings once in `Program.cs`;
- updated `JwtHelper` to use the typed settings; and
- documented external configuration expectations.

No database schema or migration change was required.

## 3. Final Configuration Contract

Required keys:

```text
JwtSettings__SecretKey
JwtSettings__Issuer
JwtSettings__Audience
JwtSettings__ExpirationMinutes
```

The secret must be supplied through an approved external secret/configuration mechanism. Environment-specific issuer, audience, and expiry may remain in non-secret configuration if operational policy permits, but the signing secret must not be tracked.

## 4. Security Requirements

- Generate the production secret using a cryptographically secure random source.
- Use at least 32 random bytes; encode them safely for the chosen configuration mechanism.
- Use different secrets per environment.
- Treat every formerly committed secret as permanently compromised.
- Never print, log, document, email, or commit the new value.
- Rotate immediately if exposure is suspected.
- Restart all application instances after a rotation.

## 5. Test Requirements

- Application starts with valid external settings.
- Startup fails for missing, blank, or short secrets.
- Issued token is accepted by a protected endpoint.
- Wrong-signature and tampered tokens return 401.
- Expired tokens return 401.
- Incorrect issuer and audience return 401.
- Token generation and middleware validation use the same configured values.
- No test depends on a production or formerly committed secret.

Current integration coverage already verifies valid, malformed, tampered, expired, and authenticated identity behavior. A focused settings-validation test suite would strengthen direct coverage of each startup validation rule if not already covered indirectly.

## 6. Deployment Sequence

1. Generate a new environment-specific secret outside the repository.
2. Add the required settings to the hosting environment without exposing their values.
3. Deploy the R1-compatible application.
4. Restart/recycle every application instance.
5. Verify fresh login and access to a protected endpoint.
6. Verify a token signed with the retired key is rejected.
7. Monitor authentication failures for unexpected impact.
8. Record non-secret evidence in `R1-JWT-Secret-Deployment-Validation.md`.

## 7. Rollback Guidance

Do not roll back to a build or configuration containing the compromised secret. If an application defect requires rollback, use an artifact that supports external JWT configuration and the current rotated secret. Rotating back to a formerly committed value is not an acceptable rollback.

## 8. Status

**IMPLEMENTATION COMPLETE**

Production rotation and retired-token rejection require the evidence checklist in the deployment-validation document.

