# Restore Pronunciation Audio — Implementation Plan

## 1. Objective

Restore recorded English pronunciation playback by adding Merriam-Webster Collegiate Dictionary API as an optional audio-only enrichment while leaving WordsAPI authoritative for definitions, parts of speech, pronunciation text, and all existing lexical behavior.

Audio resolution must be cache-first, backend-only, and failure-isolated. A successful dictionary lookup must remain successful when audio is absent or Merriam-Webster fails.

## 2. Scope

### In Scope

- A provider-neutral pronunciation-audio service backed by Merriam-Webster Collegiate.
- Minimal Merriam-Webster response DTOs and documented MP3 URL construction.
- Secure backend configuration and a separate HTTP client.
- Lazy audio resolution when `Word.AudioUrl` is null.
- Reuse and persistence of the existing `Word.AudioUrl` property.
- Graceful Angular playback-failure feedback and session-local suppression of a broken control.
- Focused backend, Angular, regression, deployment, and production verification.
- Non-destructive handling of historical DictionaryAPI.dev URLs.

### Out of Scope

- Replacing or changing WordsAPI's lexical responsibility.
- Bulk cleanup or backfill of historical audio values.
- A database migration or provider-provenance schema in this remediation.
- A backend media proxy/cache unless direct Merriam-Webster media playback proves unsuitable and the terms permit proxying.
- Page redesign.
- Expanding WordsAPI IPA presentation. Existing pronunciation text may remain as-is; new IPA UX is an optional future enhancement.
- Changes to R5 identity, `UserWord`, preferred definitions, quiz data, or other user-owned state.

## 3. Current Architecture

- `VocabularyApp.WebApi/Services/WordService.cs` performs database-first lookup. A cache hit returns immediately. A cache miss uses its injected typed `HttpClient` for WordsAPI, maps definitions and pronunciation text, explicitly sets `AudioUrl = null`, saves the canonical word and definitions, and returns `WordLookupResponse`.
- `VocabularyApp.WebApi/Program.cs` registers `IWordService`/`WordService` through `AddHttpClient` with the WordsAPI base URL, headers, key, and a 10-second timeout.
- `VocabularyApp.Data/Models/Word.cs` stores nullable `Pronunciation` and nullable `[StringLength(500)] AudioUrl` on the canonical word.
- `VocabularyApp.WebApi/DTOs/WordDTOs.cs` exposes `WordDto.AudioUrl`; `WordService.MapToDto` and vocabulary projections copy the persisted value.
- `VocabularyApp.UI/src/app/models/word-lookup.model.ts` already models optional `audioUrl`.
- `word-lookup.component.ts` maps lookup `wordDto.audioUrl`, creates `Audio`, catches synchronous and rejected playback, logs to the console, and attempts speech synthesis. It provides no visible failure feedback or broken-control state. The direct vocabulary-search mapping currently omits `audioUrl`, while vocabulary detail uses the full lookup path.
- `word-lookup.component.html` already renders Play only when `currentWord.audioUrl` is truthy.
- `DictionaryLookupApiTests.cs`, `ControllableDictionaryHandler.cs`, and `VocabularyAppWebApplicationFactory.cs` provide the current WordsAPI integration-test seam. The Angular component spec covers vocabulary/R5 behavior but not audio.

## 4. Target Architecture

WordsAPI and Merriam-Webster have separate responsibilities and separate HTTP clients:

```text
GET /api/words/lookup/{word}
        |
        v
WordService: database-first lexical lookup
        |
        +-- cache miss --> WordsAPI --> persist Word + definitions
        |
        v
Canonical Word resolved
        |
        +-- nonblank AudioUrl --> reuse; no Merriam-Webster call
        |
        +-- null/blank AudioUrl --> IPronunciationAudioService
                                      |
                                      +-- no confident audio/failure --> null, log safely
                                      |
                                      +-- valid sound identifier
                                               |
                                               v
                                      documented MP3 URL construction
                                               |
                                               v
                                      persist AudioUrl when newly resolved
        |
        v
Return normal WordLookupResponse to Angular
```

The application-facing abstraction should be `IPronunciationAudioService`; `MerriamWebsterPronunciationService` is its provider-specific implementation. This is enough indirection for replacement/testing without a larger provider framework.

## 5. Merriam-Webster Provider Contract

Use the Collegiate JSON endpoint:

`GET https://www.dictionaryapi.com/api/v3/references/collegiate/json/{encodedWord}?key={apiKey}`

The backend needs only:

- `meta.id` and `meta.stems` for conservative matching;
- `hwi.hw` for the entry headword;
- `hwi.prs[]` in response order;
- `hwi.prs[].mw` only if useful for diagnostics/tests, not for UI expansion;
- `hwi.prs[].sound.audio` for the media base filename.

The API may return entry objects or a string array of spelling suggestions. Suggestions are not audio matches. The implementation must distinguish these response shapes and must not perform a second suggestion lookup automatically.

Verified provider references: Merriam-Webster's [Collegiate API product page](https://dictionaryapi.com/products/api-collegiate-dictionary) and [JSON/audio field documentation](https://dictionaryapi.com/products/json).

## 6. Configuration and Secret Management

Plan these keys:

```text
MerriamWebster:BaseUrl
MerriamWebster:ApiKey
MerriamWebster:TimeoutSeconds
```

- Default/placeholder non-secret settings may be added to `VocabularyApp.WebApi/appsettings.json` and `appsettings.Development.json` during implementation; no real key may be committed.
- The project already declares `<UserSecretsId>VocabularyApp-WebApi-LocalDevelopment</UserSecretsId>`, so local development should use .NET User Secrets for `MerriamWebster:ApiKey`. Environment variables remain a valid automation alternative.
- Production uses SmarterASP Pool Manager → Environment Variables. Required secret: `MerriamWebster__ApiKey`.
- If production values override defaults, use `MerriamWebster__BaseUrl` and `MerriamWebster__TimeoutSeconds` as well.
- Validate that BaseUrl is absolute HTTPS, the timeout is positive and bounded, and the key is present before attempting provider calls. Missing/invalid audio configuration must disable audio enrichment safely, not prevent application startup or dictionary lookup.
- Never place the key in Angular, responses, documentation, committed settings, or log messages.

## 7. Backend Design

Expected new files:

- `VocabularyApp.WebApi/Services/IPronunciationAudioService.cs`
- `VocabularyApp.WebApi/Services/MerriamWebsterPronunciationService.cs`
- `VocabularyApp.WebApi/DTOs/External/MerriamWebsterDtos.cs`
- Optionally `VocabularyApp.WebApi/Configuration/MerriamWebsterOptions.cs` if a small validated options type improves registration clarity.

Expected existing files to change:

- `VocabularyApp.WebApi/Program.cs`: register a separate typed client/service, BaseUrl, JSON accept header, bounded timeout, and options/configuration. Do not add the key as a default header because the documented API uses the `key` query parameter; ensure request/log handling never emits it.
- `VocabularyApp.WebApi/Services/WordService.cs`: inject `IPronunciationAudioService`, resolve only missing audio, persist only a newly obtained valid URL, and keep all audio exceptions outside lexical failure semantics.
- `VocabularyApp.WebApi/Services/IWordService.cs` and `VocabularyApp.WebApi/Controllers/WordsController.cs`: propagate `HttpContext.RequestAborted` through lookup if adding `CancellationToken` can be done compatibly. Provider-internal timeout cancellation must be handled as optional-audio failure; client-request cancellation may be allowed to propagate rather than doing unnecessary work.
- `VocabularyApp.WebApi.Tests/Infrastructure/VocabularyAppWebApplicationFactory.cs`: register an independent controllable Merriam-Webster handler/client so WordsAPI request assertions remain meaningful.
- A focused new service test file and/or `DictionaryLookupApiTests.cs` for integration behavior.

The service result can be `Task<string?> ResolveAudioUrlAsync(string requestedWord, CancellationToken cancellationToken)`. `null` covers no confident match, no pronunciation, disabled configuration, and handled provider failure. Structured internal logging distinguishes these cases; the application contract does not need a new public error type.

## 8. Audio Resolution and Caching Algorithm

Ordered behavior:

```text
normalize and validate requested term
load canonical Word and definitions from the database

if Word is absent:
    call WordsAPI using current behavior
    validate/map lexical response
    construct new Word and definitions

if Word.AudioUrl is nonblank:
    do not call Merriam-Webster
    return existing AudioUrl
else:
    try:
        resolvedUrl = audioService.ResolveAudioUrlAsync(canonical Word.Text)
    catch handled provider/configuration/parse failures:
        resolvedUrl = null

    if resolvedUrl is a valid allowed HTTPS Merriam-Webster media URL:
        set Word.AudioUrl = resolvedUrl
        save it with pending lexical inserts when possible
        otherwise call SaveChanges only for this actual update

return dictionary result whether resolvedUrl exists or not
```

For a new word, perform audio resolution before the existing save when practical so lexical data and a successful audio enrichment use one `SaveChangesAsync`. If audio fails, still save the lexical word and definitions. For a cached word with null audio, save only when resolution succeeds; do not issue `SaveChangesAsync` for a null result.

Do not request Merriam-Webster when `AudioUrl` is already nonblank. The first implementation treats existing URLs as usable optimistically; the browser failure path is session-local and non-destructive.

The plan intentionally does not add negative-result persistence. That keeps scope/schema small, but repeated lookups for an audio-less word may repeat provider calls. Monitor quota; add a bounded negative cache or persisted retry metadata only if observed request volume justifies it.

## 9. Merriam-Webster Response Mapping

Conservative deterministic selection:

1. If the top-level response is empty, return null.
2. If it is a suggestion string array, return null; never attach suggestion audio.
3. Normalize the requested/canonical word and candidate values for comparison by trimming, comparing case-insensitively, removing Merriam-Webster syllable separators such as `*` from `hwi.hw`, and removing a homograph suffix such as `:1` from `meta.id`. Do not apply broad fuzzy matching.
4. A candidate is eligible only when normalized `hwi.hw`, normalized `meta.id`, or an explicitly returned `meta.stems` value equals the requested canonical word. Exact headword/id match ranks before an exact stem match; response order breaks ties.
5. From the highest-ranked eligible entry, select the first `hwi.prs` item in response order whose `sound.audio` is nonblank and passes filename validation.
6. If no eligible entry has sound metadata, return null. Do not borrow audio from an unrelated entry or suggestion.

This policy supports exact entries and provider-declared inflections without speculative matching. Multiple homographs with the same normalized headword are deterministic: first eligible response entry, then first sound-bearing pronunciation.

## 10. Audio URL Construction

For Collegiate English MP3, construct:

`https://media.merriam-webster.com/audio/prons/en/us/mp3/{subdirectory}/{audio}.mp3`

Use Merriam-Webster's documented subdirectory rule, in this precedence:

1. `bix` when `audio` begins with `bix`;
2. `gg` when `audio` begins with `gg`;
3. `number` when the first character is a number or punctuation;
4. otherwise the lowercase first letter of `audio`.

Treat the audio identifier as a base filename, not as a general URL or path. Reject blank identifiers, path separators, traversal sequences, query/fragment delimiters, or characters outside the narrowly supported filename set before constructing the URL. URL construction should be a small pure method with unit cases for ordinary letters, `bix`, `gg`, numbers, punctuation, and malformed identifiers.

Use MP3 for the browser-compatible output. Do not assume `baseUrl + audioId + '.mp3'`; the language, country, format, and subdirectory segments are mandatory documented rules.

## 11. Historical AudioUrl Handling

- Null/blank: lazily attempt Merriam-Webster resolution during full word lookup.
- Existing nonblank URL: return it initially and skip Merriam-Webster. Do not bulk validate or rewrite it.
- Browser playback failure: catch it, show concise feedback, suppress/disable that Play control for the current displayed word/session, and leave the database untouched because a single client failure may be transient.
- Later successful Merriam-Webster resolution: replacement is appropriate only when the backend is actually asked to resolve missing/known-stale audio under a future explicit stale-marker or validated refresh path. This first small implementation has no browser-to-database stale report and therefore does not automatically overwrite nonblank historical values.
- A later audited host-retirement policy may clear or replace confirmed stale values, but it requires separate review and is not the default.

## 12. Database Impact

**NO DATABASE MIGRATION REQUIRED.**

Repository inspection confirms `AudioUrl` already lives on canonical `Word`, is nullable, is mapped through existing DTOs/projections, and has a 500-character maximum in `VocabularyApp.Data/Models/Word.cs` and the EF model. The documented Merriam-Webster HTTPS media URL shape fits within 500 characters. Existing tracked `Word` entities can be updated safely through the current `ApplicationDbContext`.

Persist only a newly resolved, validated URL. For new lexical rows, include it in the existing save when possible. For cache hits, call `SaveChangesAsync` only when null/blank becomes a valid URL. No migration, bulk SQL, or database cleanup is planned.

## 13. Angular Changes

Expected files:

- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts`
- `word-lookup.component.scss` only if an existing style cannot express a disabled state; avoid layout redesign.

Preserve `*ngIf="currentWord.audioUrl"`. Add small component state keyed to the currently displayed word/URL for playback failure. On `Audio.play()` rejection or synchronous setup failure:

- catch the failure with no unhandled rejection;
- display a concise toast/error such as “Pronunciation audio is unavailable.”;
- optionally retain the existing speech-synthesis fallback, but do not present it as Merriam-Webster audio;
- disable or suppress the broken Play control until the displayed word changes;
- reset failure state on a new lookup/detail view;
- never call an API that clears the database value.

Map `userWord.audioUrl` in `viewExistingWord` for consistency with the backend vocabulary DTO, or standardize detail display on the existing full lookup path. Keep all save-to-vocabulary and preferred-definition behavior unchanged.

## 14. Failure Semantics

| Condition | Dictionary Result | Audio Result | User Impact |
|---|---|---|---|
| Existing nonblank `AudioUrl` | Success | Existing URL returned; provider not called | Play remains available |
| Merriam-Webster 404/no entry | Success | Null | Definitions render; Play hidden |
| Suggestion-only/no confident exact or stem match | Success | Null | Definitions render; Play hidden |
| Matching entry without pronunciation | Success | Null | Definitions render; Play hidden |
| Merriam-Webster 401/403 | Success | Null; safe configuration warning logged | Definitions render; Play hidden |
| Merriam-Webster 429 | Success | Null; rate-limit warning logged | Definitions render; Play hidden |
| Timeout/network failure | Success | Null; bounded warning logged | Definitions render; Play hidden |
| Merriam-Webster 5xx | Success | Null; provider warning logged | Definitions render; Play hidden |
| Malformed JSON or audio metadata | Success | Null; parsing/validation warning logged | Definitions render; Play hidden |
| Browser media playback failure | Already successful | URL retained on server; disabled/suppressed locally | Concise feedback; component remains usable |
| WordsAPI failure on lexical cache miss | Existing behavior | Not attempted when no canonical dictionary result exists | Existing WordsAPI error semantics remain unchanged |

No Merriam-Webster failure may turn a successful lexical result into HTTP 503. No provider status, payload, exception, request URL containing a key, or raw internal error is returned to Angular.

## 15. Logging and Security

- Log provider name, outcome category, safe normalized word, and status code at appropriate levels.
- Never log the API key, full request URI/query string, response containing secrets, authorization material, or full exception data that embeds the URI.
- Use URL encoding for the word path and query construction that safely escapes the key.
- Accept only expected JSON and validate the audio identifier before constructing a fixed-host HTTPS URL.
- Keep the media host fixed to `media.merriam-webster.com`; never treat response metadata as an arbitrary outbound URL.
- Bound response time and avoid retry storms. Do not add automatic retries for 429 or general 5xx in the first version.
- Propagate request cancellation where supported and distinguish it from the audio client's own timeout.
- Keep Angular unaware of the API key and Merriam-Webster provider internals.

## 16. Backend Test Plan

Add focused unit tests for response shape, matching, and URL construction, plus integration tests around `WordService`:

1. Existing `AudioUrl` returns unchanged and Merriam-Webster is not called.
2. Null `AudioUrl` plus valid matching pronunciation resolves the documented MP3 URL.
3. Resolved URL persists and is returned in `WordDto.AudioUrl`.
4. Matching entry with no pronunciation leaves null and dictionary lookup succeeds.
5. Suggestion-only or unrelated entries do not assign audio.
6. Multiple eligible entries/pronunciations use the deterministic ordering rule.
7. Timeout/network exception leaves dictionary success intact.
8. 429 leaves dictionary success intact.
9. 5xx leaves dictionary success intact.
10. 401/403 leaves dictionary success intact and uses safe logging.
11. Malformed JSON/metadata leaves dictionary success intact.
12. URL construction covers `bix`, `gg`, numeric/punctuation, ordinary-letter, and rejected identifier cases.
13. API key is sent only to Merriam-Webster and is absent from API responses and captured logs.
14. Existing WordsAPI cache-miss mapping and failure tests remain unchanged in meaning.
15. Existing UserWord/R5 state remains unchanged.
16. Historical nonblank URLs are neither cleared nor overwritten merely because they exist.
17. Null/no-result path causes no unnecessary `SaveChangesAsync` side effect where testable.

Use a separate `ControllablePronunciationHandler` (or a generalized handler with separate instances) so tests can independently assert WordsAPI and Merriam-Webster request counts and headers/query handling.

## 17. Angular Test Plan

1. `audioUrl` present renders Play.
2. Null/blank `audioUrl` hides Play.
3. Play constructs/invokes audio with the expected URL.
4. Rejected playback is caught and produces concise visible feedback.
5. Synchronous audio setup failure does not crash the component.
6. Failed control is disabled/suppressed for the current word and resets on the next word.
7. Dictionary definitions render normally without audio.
8. Existing speech-synthesis fallback behavior is explicit and tested if retained.
9. Direct vocabulary mapping preserves `audioUrl` consistently.
10. Save-to-vocabulary behavior remains unaffected.

## 18. Regression Test Plan

- Run the full backend suite, especially `DictionaryLookupApiTests`, `VocabularyOwnershipApiTests`, and `QuizApiTests`.
- Run the full Angular unit suite and production build.
- Confirm database-first lookup and WordsAPI cache-miss behavior.
- Confirm duplicate save remains idempotent for `(UserId, WordId)`.
- Confirm preferred-definition updates preserve `UserWord.Id`, favorite state, notes, counters, sample sentences, quiz results, and ownership boundaries.
- Confirm audio enrichment changes only canonical `Word.AudioUrl`; it does not create duplicate canonical words or mutate definitions/user state.
- Confirm no R5 migration or schema behavior changes.

## 19. Production Configuration

In SmarterASP Pool Manager → Environment Variables, configure without recording values:

- `MerriamWebster__ApiKey` — required for audio enrichment.
- `MerriamWebster__BaseUrl` — optional if a safe source-controlled default is used.
- `MerriamWebster__TimeoutSeconds` — optional if a bounded default is used.

Retain the existing `WordsApi__ApiKey`, connection string, JWT, and other settings unchanged. Recycle/restart the application pool after setting variables. Verify the deployed process receives configuration without logging or exposing values.

## 20. Deployment Sequence

### Phase 1 — Configuration, abstraction, and DTOs

- Files: `Program.cs`, optional `MerriamWebsterOptions.cs`, `IPronunciationAudioService.cs`, `MerriamWebsterDtos.cs`, test infrastructure.
- Behavior: separate typed client, secure key/config binding, minimal contract.
- Gate: configuration validation tests and DTO/suggestion-shape tests pass; no real key is committed.

### Phase 2 — Merriam-Webster audio resolution

- Files: `MerriamWebsterPronunciationService.cs` and focused unit tests.
- Behavior: conservative matching, deterministic sound selection, documented MP3 subdirectory construction, handled provider failures.
- Gate: matching, failure, filename validation, and all directory-rule tests pass against representative fixtures.

### Phase 3 — Word lookup integration and persistence/cache behavior

- Files: `WordService.cs`, optionally `IWordService.cs` and `WordsController.cs`, integration factory/handler/tests.
- Behavior: existing URL skips provider; null URL resolves lazily; successful result persists; audio failure never changes dictionary success.
- Gate: focused integration tests pass and existing WordsAPI tests retain their results.

### Phase 4 — Angular playback error handling

- Files: `word-lookup.component.ts`, `.html`, optionally `.scss`.
- Behavior: existing visibility remains; failures show concise feedback and suppress/disable the broken current control.
- Gate: component tests demonstrate no crash/unhandled rejection and normal no-audio rendering.

### Phase 5 — Backend tests

- Files: service/unit tests, `DictionaryLookupApiTests.cs`, test handler/factory.
- Behavior: complete optional-enrichment, secret-boundary, caching, persistence, and failure matrix.
- Gate: full backend suite passes.

### Phase 6 — Angular tests

- Files: `word-lookup.component.spec.ts`.
- Behavior: visibility, playback, failure feedback/reset, normal lookup, and save behavior covered.
- Gate: full Angular suite and production build pass.

### Phase 7 — Regression verification

- Files: no intended production changes; test fixes only if a real regression is found.
- Behavior: R5/UserWord/quiz/ownership and DB-first lookup remain intact.
- Gate: regression checklist and local smoke tests pass; diff contains no migration.

### Phase 8 — Production configuration and deployment

- Files/state: deployment artifacts and SmarterASP environment variables only; no secret in repository.
- Behavior: provider enabled after licensing/branding checkpoint, then deployed using existing manual process.
- Gate: environment variables confirmed, production smoke tests pass, and rollback remains available.

## 21. Production Smoke Tests

After confirming licensing/branding and production configuration:

1. Authenticate and verify normal login.
2. Look up a new word known to have Merriam-Webster audio; confirm definitions come from the normal path, Play appears, and MP3 playback works.
3. Repeat the lookup; confirm the persisted URL is reused and no unnecessary provider request is indicated by logs/metrics.
4. Look up a word with no available/confident pronunciation; confirm definitions succeed and Play is absent.
5. Open an old cached word with a historical URL; confirm it is preserved and either plays or fails gracefully.
6. If safely reproducible, use a known invalid test URL in a non-production fixture/environment to confirm visible failure feedback and no component crash; do not corrupt production data merely to test failure.
7. Save a looked-up word to vocabulary, repeat the save, and confirm idempotent duplicate behavior.
8. Update a preferred definition and confirm the same `UserWord` and related R5 state remain intact.
9. Recheck login, vocabulary listing/detail, quiz access, and server logs for secrets/raw provider internals.

## 22. Rollback Plan

- Remove/disable the Merriam-Webster service registration and audio-resolution call, or omit the production key so enrichment safely returns no result.
- Redeploy the prior backend/Angular artifacts using the established deployment process.
- Leave WordsAPI and all dictionary endpoints operational.
- Preserve existing word, definition, `UserWord`, and `AudioUrl` data. Do not clear URLs during rollback.
- Because no migration is required, rollback needs no schema downgrade or data restoration.
- If only Angular failure UX causes an issue, roll back the UI independently while leaving backend lexical functionality intact.

## 23. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Free-use eligibility, daily limit, or branding obligations differ from assumptions | Mandatory pre-production terms/branding review; obtain appropriate license or do not enable production integration |
| Audio enrichment increases lookup latency | Cache-first behavior, bounded short timeout, no unnecessary calls, optional result |
| Repeated no-audio words consume quota | Monitor outcomes; add bounded negative caching later only if needed |
| Suggestions/inflections cause wrong pronunciation | Exact/provider-declared-stem matching only; no fuzzy or automatic suggestion lookup |
| Malformed identifier creates unsafe URL | Pure validated constructor with fixed HTTPS host and strict filename checks |
| Historical URLs fail | Visible nonfatal UI feedback; session-local suppression; no destructive clearing |
| Provider outage/rate limit affects dictionary | Catch and isolate all expected audio-provider failures; WordsAPI result remains authoritative |
| API key leaks through query URI logging | Never log full request URI; redact/avoid HTTP-client logging that includes query strings; secret-boundary tests |
| Concurrent null-audio lookups duplicate provider work | Accept small initial race; persistence is idempotent. Add per-key coalescing only if observed load justifies it |
| Direct media delivery terms or browser behavior differ | Verify terms and production playback before rollout; consider proxy only in a separately reviewed design |

## 24. Licensing / Branding Checkpoint

Before enabling Merriam-Webster in production, the developer must review the terms applicable to VocabularyApp and confirm:

- whether the application qualifies for non-commercial/free access;
- the applicable request limit and expected production volume;
- whether commercial licensing/contact is required;
- required Merriam-Webster logo, branding, attribution, or product-name presentation;
- whether constructing, storing, and directly playing the documented audio URLs is permitted for this use;
- whether any proxying or caching restriction affects the chosen direct-delivery design.

Merriam-Webster's current FAQ describes limited free non-commercial access and branding requirements, but this plan makes no legal conclusion. Record the verified outcome before production deployment. If terms are unacceptable or unclear, keep audio enrichment disabled and revisit provider selection.

## 25. Definition of Done

- [ ] WordsAPI remains the lexical/dictionary provider with existing behavior preserved.
- [ ] Merriam-Webster is used only for optional pronunciation audio.
- [ ] Provider communication and the API key remain entirely in ASP.NET Core.
- [ ] Existing nonblank audio skips the provider.
- [ ] Null audio resolves and persists a valid URL when a confident match exists.
- [ ] No-match, no-pronunciation, timeout, 401/403, 429, 5xx, network, and malformed-response cases preserve dictionary success.
- [ ] Documented `bix`, `gg`, number/punctuation, and first-letter directory rules are implemented and tested.
- [ ] No unrelated suggestion audio can be assigned.
- [ ] Play remains conditional on `audioUrl`.
- [ ] Playback failure is visible, nonfatal, and session-local.
- [ ] Historical URLs are preserved; no bulk cleanup occurs.
- [ ] No database migration is added.
- [ ] Backend and Angular focused tests pass.
- [ ] Full backend, Angular, R5/UserWord, quiz, and ownership regressions pass.
- [ ] No API key or secret exists in source, client output, responses, or logs.
- [ ] SmarterASP variables are configured securely.
- [ ] Licensing/branding checkpoint is completed before production enablement.
- [ ] Production smoke tests and rollback readiness are confirmed.

## 26. Implementation Readiness

The repository provides the required persistence and API/UI contracts; Merriam-Webster's response fields and MP3 directory rules are documented; the provider, matching policy, cache behavior, security boundary, failure semantics, tests, deployment, and rollback are specified. Licensing/branding verification is mandatory before production enablement but does not block coding or non-production testing with an authorized developer key.

READY FOR IMPLEMENTATION
