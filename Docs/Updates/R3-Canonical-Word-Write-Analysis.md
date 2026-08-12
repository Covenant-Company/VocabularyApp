# R3 Canonical Word-Write Analysis

## 1. R3 Requirement

**Title:** Secure or remove the public canonical word-write endpoint  
**Priority:** Critical  
**Estimated size:** Small  
**Dependency:** Product decision about whether administrative word entry is needed.

The authoritative remediation plan identifies `POST /api/words/add` (`VocabularyApp.WebApi/Controllers/WordsController.cs:62`) as an administrative operation that is publicly accessible. It also reports HTTP 200 success even when the service fails.

The risks are:

- An unauthenticated caller can insert arbitrary words and definitions into the shared canonical dictionary.
- Polluted canonical data affects every user, lookup result, vocabulary entry, and quiz behavior that consumes it.
- False success responses conceal failed or partially completed writes.

The documented objective is to remove the endpoint if no current workflow needs manual canonical entry. If it must remain, R3 requires a real administrator role/claim model, an explicit authorization policy, request validation, and accurate status-code handling.

Documented acceptance criteria and completion conditions:

- Anonymous requests receive `401`, or the route no longer exists.
- If retained, an ordinary authenticated user receives `403`.
- If retained, an authorized administrator can add valid data.
- Service failures do not return success.
- Duplicate canonical entries are handled deterministically.
- No anonymous canonical mutation is possible.
- Authorization behavior is covered by integration tests.
- Endpoint responses accurately represent results.
- Documentation matches the product decision.

## 2. Current Architecture and Behavior

The relevant flow is:

```text
Anonymous HTTP POST /api/words/add
  -> WordsController.AddWord
  -> IWordService.AddWordAsync
  -> WordService.AddWordAsync
  -> ApplicationDbContext
  -> Words / WordDefinitions / PartsOfSpeech
  -> SQL Server
  -> HTTP 200 regardless of ServiceResult.IsSuccess
```

### Entry point and security boundary

`WordsController.AddWord` (`VocabularyApp.WebApi/Controllers/WordsController.cs:63`):

- Is exposed through `[HttpPost("add")]`.
- Has no `[Authorize]` attribute or authorization policy.
- Accepts an `AddWordRequest` directly from an untrusted caller.
- Checks only that the request and `Word` are nonempty.
- Calls `AddWordAsync`.
- Ignores `result.IsSuccess`.
- Always returns HTTP 200 unless the service throws past its own exception handler.

Authentication middleware is correctly registered in `Program.cs:29`, but it has no effect on this endpoint because the action is not protected.

### Input model and validation

`AddWordRequest` (`VocabularyApp.WebApi/Models/AddWordRequest.cs:3`) contains:

- `Word`
- `Definition`
- `Example`
- `PartOfSpeech`
- `Pronunciation`
- `PreferredWordDefinitionId`

It has no data annotations for required fields, length limits, or allowed values. The controller manually validates only `Word`.

Entity length constraints exist downstream:

- Word text: 100 characters.
- Pronunciation: 200 characters.
- Definition: 1,000 characters.
- Example: 500 characters.
- Part of speech: resolved against seeded values.

Oversized data therefore reaches EF/SQL before it fails.

### Service processing and persistence

`WordService.AddWordAsync` (`VocabularyApp.WebApi/Services/WordService.cs:153`):

1. Rejects a missing or blank word.
2. Searches for an exact `Word.Text` match without trimming or explicit case normalization.
3. If absent, creates a canonical `Word` and immediately calls `SaveChangesAsync`.
4. If a definition was supplied:
   - Resolves the submitted part of speech.
   - Silently falls back to `Noun` for blank or unknown values.
   - Creates a `WordDefinition` with `DisplayOrder = 1`.
   - Calls `SaveChangesAsync` again.
5. Returns a successful `ServiceResult`.
6. Catches all exceptions, logs them, and returns a failed `ServiceResult`.

There is no explicit transaction spanning the word and definition saves. Consequently, the word insert can commit before a definition insert fails.

The database enforces:

- Unique canonical `Word.Text` in `ApplicationDbContext.cs:35`.
- Unique `(WordId, PartOfSpeechId, DisplayOrder)` definitions at `ApplicationDbContext.cs:66`.
- Foreign-key integrity between definitions, words, and parts of speech.

### Response behavior

`ServiceResult<T>` (`VocabularyApp.WebApi/Models/ServiceResult.cs:3`) clearly distinguishes success from failure using `IsSuccess`.

The controller does not use that property. A service result such as:

```text
IsSuccess = false
Message = "Failed to add word"
Data = null
```

becomes:

```http
HTTP/1.1 200 OK

{
  "success": true,
  "data": null
}
```

Only an exception escaping the service produces HTTP 500, but `AddWordAsync` catches its own operational exceptions.

### Clients and legitimate usage

No legitimate caller of `/api/words/add` was found.

The Angular application uses the separate authenticated personal-vocabulary endpoint:

- `WordLookupComponent` (`VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts:347`) posts to `/words/vocabulary/add`.
- `WordsController.AddToVocabulary` (`VocabularyApp.WebApi/Controllers/WordsController.cs:90`) is protected with `[Authorize]`.

Neither `test-api.http` nor `VocabularyApp.WebApi/VocabularyApp.WebApi.http` provides a manual `/words/add` example.

### Closely related lookup behavior

Public `GET /api/words/lookup/{word}` (`WordsController.cs:28`) can also write canonical data on a cache miss. `LookupWordAsync` (`WordService.cs:60`) obtains data from `dictionaryapi.dev` and persists a `Word` and its definitions.

This differs materially from `/words/add`: the caller chooses the lookup term, but the definition content comes from the configured external provider rather than directly from arbitrary request fields. Nevertheless, it is technically an anonymous-triggered canonical write and creates ambiguity around the plan's literal completion condition, "No anonymous canonical mutation is possible."

## 3. Exact R3 Weakness

### Direct R3 findings

| Location | Current behavior | Risk |
| --- | --- | --- |
| `WordsController.AddWord` (`WordsController.cs:62`) | Public route with no authorization | **Critical:** anyone can insert shared dictionary content |
| `WordsController.AddWord` (`WordsController.cs:70`) | Ignores `ServiceResult.IsSuccess` | **High:** failed or partial writes are reported as successful |
| `WordService.AddWordAsync` (`WordService.cs:153`) | Accepts caller-authored canonical words and definitions | **Critical when exposed publicly:** global data pollution |
| `VocabularyOwnershipApiTests` (`VocabularyOwnershipApiTests.cs:262`) | Explicitly expects anonymous HTTP 200 and persistence | **High regression-test concern:** the current test protects the vulnerability |

### Closely related safety findings

- **No administrator identity exists.** `User` has no role/admin field, and `JwtHelper.GenerateToken` emits identity, username, email, and `jti` claims only. `Program.cs` registers default authorization without policies. Retaining the route would require a broader security design.
- **Weak request validation.** Only a nonblank word is checked; field limits and part-of-speech validity are not enforced at the HTTP boundary.
- **Non-atomic persistence.** A word can remain committed if its definition fails.
- **Duplicate behavior is indirect and inconsistent.** Existing words can receive another definition with fixed `DisplayOrder = 1`; a duplicate word/POS/order hits the database unique constraint and becomes a failed service result, which the controller then reports as success.
- **Concurrency is nondeterministic at the API level.** Two simultaneous inserts can both observe no word. The unique index prevents duplicate rows, but one operation can fail and still be reported as HTTP 200.
- **Exact matching is insufficiently normalized.** Leading/trailing whitespace and casing behavior depend partly on SQL collation, allowing inconsistent lookup behavior and possible variants.
- **Public lookup writes canonical cache data.** This must be explicitly interpreted against R3's definition of done.

### Unrelated technical debt

The following should not be folded into R3:

- Lookup maps provider failures to HTTP 404.
- API error envelopes are inconsistent.
- Public lookup has no rate limiting.
- Cancellation tokens are not propagated.
- General vocabulary identity and duplicate behavior belong to R5.
- General API contract and exception redesign belongs to R7/R8.
- Concurrent lookup cache misses belong to R13.

## 4. Affected Files and Components

### Must Change - recommended removal approach

- `VocabularyApp.WebApi/Controllers/WordsController.cs`
  - Remove `AddWord`, its route, comments, error handling, and dependency call.
- `VocabularyApp.WebApi/Services/IWordService.cs`
  - Remove the now-unused `AddWordAsync` operation so the public-write capability does not remain as an apparently supported service contract.
- `VocabularyApp.WebApi/Services/WordService.cs`
  - Remove the now-unused implementation. Keeping an unused canonical mutation method would leave confusing and potentially reusable attack surface.
- `VocabularyApp.WebApi.Tests/Integration/VocabularyOwnershipApiTests.cs`
  - Replace the current characterization test with an R3 acceptance test asserting route-not-found/non-success and no database mutation.

### May Change

- Remediation/update documentation to record the product decision and R3 result.
- HTTP examples only if a new R3-specific example is intentionally added; no obsolete `/words/add` example currently exists.
- Swagger operation documentation changes automatically when the action is removed.
- `AddWordRequest` should remain because authenticated personal-vocabulary saves use it. A separate DTO is optional and outside the minimum removal.
- Dictionary lookup tests may need an explicit test or comment documenting whether provider-backed caching is accepted under R3.
- If the endpoint is retained instead, `User`, migrations, JWT claims, authorization policy registration, administrative provisioning, request DTOs, controller, service, tests, and deployment procedures would all become affected.

### Should Not Change

- JWT key binding, validation parameters, issuer/audience, or signing algorithm from R1.
- Password hashing, legacy verification, login migration, or credential concurrency behavior from R2.
- Personal vocabulary endpoints or Angular save flow.
- Quiz services and counters.
- `UserWord` identity/schema.
- General API response standardization.
- Existing word/definition schema if the endpoint is removed.
- External dictionary provider architecture except for documenting the R3 scope decision.

## 5. Database Impact

For the recommended removal:

- **Schema change:** None.
- **EF Core migration:** None.
- **Data migration:** None.
- **Existing data cleanup:** Not required to close the vulnerability, although previously polluted production data may warrant a separate operational review.
- **Backward compatibility:** Only callers of the undocumented/unused administrative route lose access. Repository searches found no application caller.

Removing the endpoint does not require deleting `Word`, `WordDefinition`, or `PartOfSpeech`; those entities remain essential to lookup, vocabulary, and quiz functionality.

If the endpoint is retained with database-backed administrator roles, a schema migration and administrator-provisioning process would probably be required. A configuration-only allowlist claim could avoid a schema change but introduces separate identity-management and revocation concerns.

## 6. Configuration/Deployment Impact

For removal:

- No new secrets or environment variables.
- No `appsettings` change.
- No SmarterASP.NET setting change.
- No IIS or Angular `web.config` change.
- No CORS change.
- No JWT rotation.
- Deploy the updated API normally; after deployment, verify `/api/words/add` is unavailable and the Swagger document no longer advertises it.

The existing R1 production requirement for `JwtSettings__SecretKey` remains unchanged.

If authorization is chosen instead, deployment would need a reliable administrator-provisioning mechanism and documented claim/role lifecycle. Merely adding `[Authorize]` would not meet R3 because every ordinary user would then retain canonical-write access.

## 7. Existing Test Coverage

The R6 test project already provides a real HTTP/EF test foundation using `WebApplicationFactory` and relational SQLite.

Relevant coverage includes:

- `CanonicalWordAddIsCurrentlyAnonymousAndWritesSharedData` (`VocabularyOwnershipApiTests.cs:262`) proves the R3 vulnerability by expecting HTTP 200 and a persisted word.
- `RepresentativeVocabularyRoutesRejectAnonymousRequests` (`VocabularyOwnershipApiTests.cs:247`) confirms personal-vocabulary routes reject anonymous callers.
- `DictionaryLookupApiTests` protects:
  - canonical cache hits;
  - provider-backed cache misses;
  - persistence of provider results;
  - provider 404/500 behavior;
  - part-of-speech fallback.
- Vocabulary integration tests protect authenticated personal saves, ownership, favorites, preferred definitions, list/search isolation, and current duplicate behavior.
- Authentication tests protect JWT issuance and middleware behavior.
- R2 tests protect modern hashing, legacy migration, malformed hashes, logging, and credential concurrency.

No tests were executed during this analysis.

## 8. Missing Test Coverage

For the recommended removal, implementation should add or revise tests for:

- Anonymous `POST /api/words/add` returns route-not-found/non-success.
- An authenticated ordinary user also cannot reach the removed route.
- Failed requests do not add a `Word`.
- Failed requests do not add a `WordDefinition`.
- The authenticated `/api/words/vocabulary/add` route still works.
- Public dictionary lookup still behaves according to the explicit R3 scope decision.
- Swagger no longer exposes `/api/words/add`, if API-document verification is considered worthwhile.

If retention is chosen, additional tests are required for:

- Anonymous `401`.
- Ordinary authenticated user `403`.
- Administrator success.
- Missing/malformed admin claims.
- Invalid and oversized requests.
- Unknown part of speech.
- Duplicate word/definition behavior.
- Concurrent duplicate submissions.
- Service failure and partial-write rollback.
- Accurate conflict, validation, and server-error responses.
- Administrator provisioning and token claim issuance.

The existing characterization test must not merely be deleted; it should be converted into the central R3 security regression test.

## 9. Regression Risks

- **Personal vocabulary saving could be removed accidentally.** Both operations use `AddWordRequest` and similar service logic. Limit deletion to `AddWord`, `IWordService.AddWordAsync`, and its implementation; retain `AddToVocabularyAsync`.
- **Lookup caching could be unintentionally disabled.** `LookupWordAsync` also creates canonical records. Protect cache-hit and provider-backed persistence tests.
- **Swagger/API routing could retain or redirect the old path unexpectedly.** Verify route-not-found behavior through the real host.
- **R6's existing suite will intentionally fail until its vulnerable characterization test is updated.** Change the expectation in the same implementation phase.
- **R1 regression:** Adding a role system could tempt changes to JWT construction or validation. Removal requires no JWT changes and is therefore safer.
- **R2 regression:** Adding administrator properties or changing user persistence could touch registration/login/password migration. Removal avoids all password and user-schema code.
- **Previously integrated external clients may depend on the route despite no repository evidence.** Review production access logs or stakeholder knowledge before deployment.
- **Literal acceptance ambiguity:** Closing `/words/add` while leaving anonymous provider-backed caching may be judged inconsistent with "no anonymous canonical mutation." Resolve that wording before declaring R3 complete.

## 10. Recommended Technical Approach

Remove the endpoint and its service operation.

This is the safest approach because:

- The remediation plan explicitly prefers removal when there is no current workflow.
- Repository searches found no Angular, HTTP-script, or backend caller.
- The actual user workflow uses an authenticated, separate route.
- The application has no administrator role or claim model.
- Removal closes both the authorization defect and false-success response defect without introducing schema, JWT, password, or deployment complexity.
- It keeps R3 small and avoids prematurely designing an administration subsystem.

Alternatives:

- **Add `[Authorize]` only:** Insufficient; ordinary users could still mutate global data.
- **Add a hard-coded username/email check:** Brittle and inappropriate as an authorization boundary.
- **Add a configuration allowlist:** Possible, but creates identity mapping, rotation, audit, and operational concerns for a feature with no identified consumer.
- **Implement full roles/admin policy:** Technically sound if administration is genuinely required, but significantly expands R3 into schema, provisioning, JWT claims, and deployment work.
- **Keep the service method but remove only the controller:** Closes current HTTP exposure but leaves an unsupported mutation capability and stale contract. Removing both is cleaner and still narrowly scoped.

The provider-backed lookup cache should remain unchanged unless the product owner interprets R3 as prohibiting all anonymous-triggered persistence. Its content is provider-controlled and is part of the active lookup workflow, so changing it would be a distinct product and architecture decision.

## 11. Proposed Implementation Phases

### Phase 1 - Confirm Product and Scope Decision

- **Objective:** Formally choose removal and define whether provider-backed lookup caching is permitted.
- **Affected components:** Product/remediation documentation only.
- **Tests:** None executed in this phase.
- **Completion criteria:** Written decision that `/api/words/add` has no required consumer; explicit interpretation of anonymous lookup caching.
- **Dependencies:** Stakeholder confirmation and, ideally, production access-log review.

### Phase 2 - Lock the Desired Security Contract in Tests

- **Objective:** Convert the vulnerable characterization into the R3 acceptance test.
- **Affected components:** `VocabularyOwnershipApiTests`.
- **Changes:** Assert the route is unavailable to anonymous and authenticated ordinary callers and that the database remains unchanged.
- **Tests eventually required:** Focused R3 integration test.
- **Completion criteria:** Test expresses route removal and no arbitrary canonical write.
- **Dependencies:** Phase 1.

### Phase 3 - Remove the Public Canonical-Write Capability

- **Objective:** Eliminate the route and unused service operation.
- **Affected components:** `WordsController`, `IWordService`, `WordService`.
- **Changes:** Remove the action, interface member, and implementation.
- **Tests eventually required:** R3 integration test plus existing dictionary/vocabulary tests.
- **Completion criteria:** `/api/words/add` is not routed; no repository caller or service contract remains.
- **Dependencies:** Phase 2.

### Phase 4 - Documentation and API-Surface Verification

- **Objective:** Ensure public documentation matches the removal.
- **Affected components:** Swagger output, remediation/update documentation, HTTP examples if applicable.
- **Changes:** Record the decision and verify no stale route description remains.
- **Tests eventually required:** Optional Swagger document assertion; manual Swagger inspection.
- **Completion criteria:** Documentation and generated API surface omit the endpoint.
- **Dependencies:** Phase 3.

### Phase 5 - Full Regression Validation

- **Objective:** Confirm R3 did not affect active workflows or R1/R2 behavior.
- **Affected components:** Existing backend test suite and deployment smoke checks.
- **Tests eventually required:**
  - R3 route rejection.
  - Dictionary lookup/cache tests.
  - Vocabulary ownership tests.
  - Authentication/JWT tests.
  - Password hashing, migration, logging, and concurrency tests.
- **Completion criteria:** Developer-run suite passes; deployed route is unavailable; authenticated vocabulary save and lookup still work.
- **Dependencies:** Phases 3-4.

## 12. Open Questions / Decisions Needed Before Implementation

1. **Is manual canonical word administration actually required?**  
   Repository evidence says no. Unless an external operational workflow exists, removal should be approved.

2. **Has `/api/words/add` been used by an external client or production operator?**  
   Source searches cannot answer this; production access logs or stakeholder confirmation are needed.

3. **Does "no anonymous canonical mutation" include provider-backed lookup caching?**  
   The public lookup endpoint persists provider-supplied results. This should be explicitly accepted as trusted cache population or assigned separate remediation scope.

4. **Should existing canonical data be audited for pollution?**  
   R3 can close the exposure without data migration, but historical anonymous writes may already exist.

5. **What response is preferred after removal?**  
   Natural ASP.NET route absence will normally produce `404`. The remediation plan accepts route-not-found, so no compatibility tombstone appears necessary.

## Analysis Constraints

This report was produced during an analysis-only phase. No application source, schema, migration, configuration, or existing documentation files were changed, and no tests were run. This analysis document is the only repository addition made after the report was requested in Markdown form.
