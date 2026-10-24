# R1 — JWT Signing Secret Deployment Validation

> Record confirmations only. Never paste signing keys, tokens, connection strings, or other secrets into this document.

## 1. Deployment Gate

- [x] Source-controlled JWT secret values were removed in commit `d8408910be9e98590d5330f593505195429621ae`.
- [x] The application validates JWT configuration during startup.
- [x] Production records confirm `JwtSettings__SecretKey` is supplied externally through SmarterASP application-pool configuration.
- [x] Production login and authenticated application access have succeeded with external configuration.
- [ ] An operator has confirmed the production value is newly generated and differs from every formerly committed value.
- [ ] Development, staging, and production are confirmed to use independent signing secrets.
- [ ] Rotation/restart date, environment, operator, and non-secret change reference are recorded.

## 2. Post-Rotation Verification

- [ ] Fresh production login returns a JWT.
- [ ] The fresh JWT is accepted by a protected endpoint.
- [ ] A token signed with the retired committed key is rejected with HTTP 401.
- [ ] A malformed token is rejected with HTTP 401.
- [ ] A tampered token is rejected with HTTP 401.
- [ ] An expired token is rejected with HTTP 401.
- [ ] No unexpected authentication errors are observed after the restart.
- [ ] No secret value appears in application logs, deployment records, or tracked files.

Later production smoke testing establishes the first two behaviors generally, but this checklist leaves them open until they are explicitly tied to the documented R1 rotation event.

## 3. Safe Verification Procedure

1. Before rotation, create a short-lived test token signed with the retiring key in a controlled environment, or retain a pre-rotation test-user token without recording it.
2. Configure the new production secret through SmarterASP Pool Manager.
3. Restart/recycle the application pool.
4. Log in using a designated test account and call a protected endpoint.
5. Call the same protected endpoint with the retained retired-key token; expect HTTP 401.
6. Do not paste either token or secret into tickets, screenshots, console transcripts, or this document.

If retaining an old token is no longer possible, record the limitation rather than reconstructing or exposing the old secret. Confirming that the configured value differs from the historical one remains mandatory.

## 4. Deployment Record

Environment: Production

Rotation confirmed: Pending

Rotation/restart date: Pending

Operator: Pending

Non-secret change reference: Pending

Fresh login/protected endpoint: Previously observed; R1-specific confirmation pending

Retired-key token rejected: Pending

Rollback required: Not recorded

Final status: **PENDING PRODUCTION ROTATION VERIFICATION**

