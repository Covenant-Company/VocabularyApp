# R3 Canonical Word Security Completion

## 1. Completion Status

**R3 status: COMPLETE**

Phases 1–4 are complete. The developer manually ran the full backend suite after Phase 3:

```text
Passed: 138
Failed: 0
Skipped: 0
Total: 138
```

Phase 4 changed documentation only. Codex did not run tests.

## 2. Problem Statement

R3 began with a public `POST /api/words/add` operation that accepted caller-authored canonical words and definitions. The controller also reported success after service failures. Subsequent analysis identified two broader trust problems: dictionary lookup was anonymous, and personal-vocabulary saves could manufacture missing canonical words from caller input.

The product owner established the final policy:

> Registration and login are the only anonymous API endpoints. Every other application/business API endpoint requires authentication.

> Users cannot directly create canonical words or canonical definitions. Canonical dictionary content may only be populated from validated trusted external dictionary provider responses.

> Adding a word to personal vocabulary creates a relationship to existing canonical data; it does not create canonical dictionary data.

## 3. Root Security Weaknesses

- Authorization was opt-in, so omitted `[Authorize]` metadata exposed lookup and canonical mutation.
- `POST /api/words/add` persisted caller-controlled canonical content.
- `AddToVocabularyAsync` created a missing canonical `Word` from caller word/pronunciation data.
- `LookupWordAsync` used the caller's normalized lookup term when the provider omitted canonical word text.
- Existing tests initially characterized rather than prevented the vulnerable direct-write behavior.

## 4. Final Authentication Architecture

`Program.cs` configures an ASP.NET Core fallback authorization policy using `RequireAuthenticatedUser()`. Authentication is therefore the default for current and future endpoints unless an explicit anonymous exception is present.

The only `[AllowAnonymous]` actions are:

- `POST /api/users/register`
- `POST /api/users/login`

Existing `[Authorize]` attributes remain where useful for clarity. Middleware order remains `UseAuthentication()`, `UseAuthorization()`, then `MapControllers()`.

### Final endpoint inventory

| Method | Route | Controller/action | Classification | Enforcement |
| --- | --- | --- | --- | --- |
| POST | `/api/users/register` | `UsersController.Register` | Anonymous bootstrap | `[AllowAnonymous]` |
| POST | `/api/users/login` | `UsersController.Login` | Anonymous bootstrap | `[AllowAnonymous]` |
| GET | `/api/users/profile` | `UsersController.GetProfile` | Authentication required | Fallback policy and `[Authorize]` |
| POST | `/api/users/change-password` | `UsersController.ChangePassword` | Authentication required | Fallback policy and `[Authorize]` |
| GET | `/api/users/validate-token` | `UsersController.ValidateToken` | Authentication required | Fallback policy and `[Authorize]` |
| GET | `/api/words/lookup/{word}` | `WordsController.LookupWord` | Authentication required | Fallback policy |
| POST | `/api/words/vocabulary/add` | `WordsController.AddToVocabulary` | Authentication required | Fallback policy and `[Authorize]` |
| GET | `/api/words/vocabulary` | `WordsController.GetUserVocabulary` | Authentication required | Fallback policy and `[Authorize]` |
| GET | `/api/words/vocabulary/search` | `WordsController.SearchUserVocabulary` | Authentication required | Fallback policy and `[Authorize]` |
| PUT | `/api/words/vocabulary/{userWordId}/favorite` | `WordsController.SetFavorite` | Authentication required | Fallback policy and `[Authorize]` |
| PUT | `/api/words/vocabulary/{userWordId}/preferred-definition` | `WordsController.SetPreferredDefinition` | Authentication required | Fallback policy and `[Authorize]` |
| POST | `/api/quiz/start` | `QuizController.StartQuiz` | Authentication required | Fallback policy and controller `[Authorize]` |
| POST | `/api/quiz/submit` | `QuizController.SubmitQuiz` | Authentication required | Fallback policy and controller `[Authorize]` |
| GET | `/api/quiz/history` | `QuizController.GetQuizHistory` | Authentication required | Fallback policy and controller `[Authorize]` |

No additional anonymous API action was found.

## 5. Canonical Dictionary Trust Boundary

Final supported workflow:

```text
Authenticated user
       |
       v
Dictionary lookup
       |
       v
Canonical cache hit?
    /          \
  yes           no
   |             |
return       external dictionary provider
cached data      |
                 v
          validate provider word text
                 |
                 v
          persist provider-supplied
          Word and WordDefinition data
```

Removed workflow:

```text
Caller-authored canonical content
             |
             X
             |
      Canonical repository
```

### Final canonical-write audit

Only one production creation path remains:

| File/class/method | Trigger | Word text | Pronunciation/audio | Definition/example | Caller fallback | Compliance |
| --- | --- | --- | --- | --- | --- | --- |
| `VocabularyApp.WebApi/Services/WordService.cs`, `WordService.LookupWordAsync` | Authenticated lookup cache miss with provider data | Trimmed, nonblank `first.Word` from provider | Provider phonetic/phonetics | Provider meanings/definitions | None | All canonical content is provider-derived; invalid provider word text fails before persistence |

No production path was found that modifies canonical word or definition content after creation from caller-controlled data.

**No remaining path was found by which caller-authored dictionary content can be persisted as canonical dictionary content.**

## 6. Personal Vocabulary Behavior

Final relationship workflow:

```text
Authenticated user
       |
       v
Add to personal vocabulary
       |
       v
Canonical word exists?
    /          \
  yes           no
   |             |
create          reject without
UserWord        Word, WordDefinition,
relationship   or UserWord mutation
```

`AddToVocabularyAsync` no longer creates canonical data. Existing canonical words retain the prior duplicate, preferred-definition, ownership, favorite, list, and search behavior.

## 7. Provider Validation

`LookupWordAsync` no longer assigns `first.Word ?? normalized`. It trims and validates the provider's canonical word field and rejects null, empty, or whitespace-only values before constructing or saving a `Word`. The caller's term remains valid for cache lookup and forming the provider request but cannot become persisted canonical content.

Valid provider results continue through the existing cache, pronunciation, definition, example, and part-of-speech mapping behavior.

## 8. Implementation Summary and Files

### Phase 1 — security contract

- Added `VocabularyApp.WebApi.Tests/Integration/R3SecurityContractApiTests.cs`.
- Updated `DictionaryLookupApiTests.cs` to use authenticated lookup clients.
- Converted the vulnerable canonical-add characterization in `VocabularyOwnershipApiTests.cs` into a security regression.
- Added action-classification coverage so new actions require an explicit security decision.

### Phase 2 — authentication enforcement

- `VocabularyApp.WebApi/Program.cs`: added authenticated-user fallback authorization.
- `VocabularyApp.WebApi/Controllers/UsersController.cs`: added `[AllowAnonymous]` only to registration and login.

### Phase 3 — canonical trust enforcement

- `WordsController.cs`: removed `POST /api/words/add` and `AddWord`.
- `IWordService.cs`: removed `AddWordAsync`.
- `WordService.cs`: removed direct canonical add, prohibited canonical creation from personal saves, and validated provider canonical word text.
- `VocabularyOwnershipApiTests.cs`: covered removed direct mutation and missing-canonical personal saves without database fabrication.
- `DictionaryLookupApiTests.cs`: covered null/empty/whitespace provider canonical word values without persistence.
- `R3SecurityContractApiTests.cs`: updated the final controller-action classification after route removal.

The removed-route regression accepts authorization/routing outcomes appropriate to caller context: anonymous callers may receive 401, while authenticated POST receives 404 or 405. The durable assertion is that the canonical POST operation does not exist and cannot mutate data.

## 9. Test Progression

| Stage | Passed | Failed | Skipped | Total | Meaning |
| --- | ---: | ---: | ---: | ---: | --- |
| Phase 1 RED | 131 | 3 | 0 | 134 | Public lookup and direct canonical add remained exposed |
| After Phase 2 | 133 | 1 | 0 | 134 | Lookup authorization was GREEN; direct add removal remained RED |
| Final Phase 3 GREEN | 138 | 0 | 0 | 138 | Full backend suite manually passed after trust-boundary fixes and added regressions |

The final results were supplied by the developer's manual execution. Codex did not execute tests.

Final regression command:

```powershell
dotnet test VocabularyApp.WebApi.Tests\VocabularyApp.WebApi.Tests.csproj
```

The full backend suite is the completion gate. Focused filters may be used for diagnosis, but do not replace the full run.

## 10. Database, Configuration, and Deployment Impact

R3 required:

- no database schema change;
- no EF Core migration;
- no data migration;
- no new secret or environment variable;
- no JWT configuration change;
- no Angular change;
- no SmarterASP.NET/IIS configuration change; and
- no deployment-topology change.

Deploy the API normally. The externally visible changes are that dictionary lookup requires a valid bearer token and the direct canonical-write POST operation is absent. Existing R1 production secret configuration remains authoritative.

## 11. R1 and R2 Regression Protection

Repository inspection found no R3 change to R1 JWT secret binding, signing, issuer, audience, token-validation semantics, or production environment-variable handling.

Repository inspection found no R3 change to R2 password hashing, legacy verification, login migration, registration hashing, password change, credential concurrency, or credential logging safeguards.

The final full backend result supplies regression evidence across the existing R1/R2 coverage.

## 12. Manual Deployment Smoke-Test Checklist

- [ ] Register a new user without a JWT; confirm success.
- [ ] Log in without a JWT; confirm a valid token is issued.
- [ ] Call dictionary lookup anonymously; confirm HTTP 401 and no provider/canonical persistence.
- [ ] Perform an authenticated cache-hit lookup; confirm existing canonical data is returned.
- [ ] Perform an authenticated controlled cache-miss lookup; confirm valid provider data is persisted and returned.
- [ ] Add an existing canonical word to personal vocabulary; confirm the `UserWord` relationship is created.
- [ ] Attempt to save a missing canonical word; confirm failure and no `Word`, `WordDefinition`, or `UserWord` fabrication.
- [ ] POST to `/api/words/add` anonymously and authenticated; confirm no usable operation and no mutation. Do not require one universal status across authorization/routing contexts.
- [ ] Inspect Swagger UI or `/swagger/v1/swagger.json`; confirm the direct canonical-write POST action is absent and the intended API surface remains.
- [ ] Smoke-test profile, vocabulary, and quiz routes without a token (401) and with a valid token (normal behavior).

Record environment, deployed build identifier, tester, date, and actual result. These deployment smoke tests are release validation, not unfinished application implementation.

## 13. Remaining Risks

- Canonical rows created before R3 may have unknown provenance. R3 closes active write paths but does not audit or clean historical production data.
- External provider content is trusted by policy but remains third-party data; provider quality and availability risks are unchanged and belong to provider/cache resilience work.
- Manual deployment smoke testing remains necessary to confirm the hosting environment reflects the reviewed build and authorization configuration.

No unresolved R3 source defect was found.

## 14. Definition of Done

| Criterion | Status |
| --- | --- |
| Registration/login are the only anonymous API actions | PASS |
| Secure-by-default fallback authorization is active | PASS |
| Dictionary lookup requires authentication | PASS |
| Anonymous lookup cannot reach provider/persistence | PASS — integration coverage and manual suite |
| Direct canonical add operation is removed | PASS |
| Personal saves cannot manufacture canonical data | PASS |
| Provider word text is validated without caller fallback | PASS |
| No other caller-authored canonical path remains | PASS — final source audit |
| Existing canonical personal-vocabulary save works | PASS — integration coverage and manual suite |
| R1 and R2 remain intact | PASS — source review and full regression suite |
| No migration/configuration/frontend/hosting change | PASS |
| Backend suite passes | PASS — developer-reported 138/138 |
| Final R3 documentation exists | PASS |
| Deployment smoke checklist documented | PASS |

R3 satisfies its implementation definition of done and is ready for review and commit. Merge/deployment should follow the normal process and include the manual deployment smoke checklist above.
