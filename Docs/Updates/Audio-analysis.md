# Pronunciation Audio Analysis

## 1. Executive Summary

Newly fetched words lack the play button because the current WordsAPI cache-miss path explicitly persists `Word.AudioUrl = null` (`VocabularyApp.WebApi/Services/WordService.cs:142-153`). That null is exposed through `WordDto.AudioUrl` (`WordService.cs:667-675`), consumed as `currentWord.audioUrl` (`VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts:199-213`), and fails the template's `*ngIf="currentWord.audioUrl"` condition (`word-lookup.component.html:120-126`). This behavior is repository-confirmed.

Older cached words may still show Play because `AudioUrl` is a nullable, persisted column on `Words`, cache hits return it unchanged, and there is no refresh or validation path (`VocabularyApp.Data/Models/Word.cs:13-20`; `WordService.cs:36-60,667-675`). Git history confirms the former DictionaryAPI.dev implementation selected the first nonblank `phonetics[].audio` value before the WordsAPI migration. Repository evidence supports that historical source as a mechanism, but production SQL is required to prove the origin of each current value.

Old playback can fail because the browser loads the stored external URL directly and the application neither validates it beforehand nor clears it after failure. Playback rejection is caught, logged only to the console, and followed by browser speech synthesis (`word-lookup.component.ts:424-485`). The exact production failure—404, authorization/hotlink rejection, TLS, redirect, media format, network policy, or another cause—is not proven without inspecting the stored hosts and browser/network error.

External provider verification now establishes that the current documented WordsAPI contract exposes pronunciation/IPA text, including through `GET /words/{word}/pronunciation`, but does not expose a playable audio URL, media file, or audio stream. The smallest reliable direction is therefore to retain WordsAPI for definitions and lexical data, select a dedicated pronunciation-audio provider during implementation planning, preserve historical URLs pending audit, and make playback failures visible and nonfatal. Do not change R5.

## 2. Current Audio Architecture

| Layer | Current behavior | Evidence |
|---|---|---|
| Entity/storage | `Word.AudioUrl` is nullable `string`, maximum 500 characters. Audio belongs to canonical `Word`, not `WordDefinition` or `UserWord`. | `VocabularyApp.Data/Models/Word.cs:5-20` |
| Database | Nullable `nvarchar(500)` column `Words.AudioUrl`; it is persisted. | `VocabularyApp.Data/Migrations/20251030144345_AddAudioUrlColumn.cs:11-18`; `ApplicationDbContextModelSnapshot.cs:334` |
| Lookup API | `WordDto.AudioUrl` is returned inside `WordLookupResponse.Word`. | `VocabularyApp.WebApi/DTOs/WordDTOs.cs:3-10,28-35`; `WordService.cs:667-675` |
| Vocabulary API | `UserVocabularyItemDto.AudioUrl` exists and list/search projections read `uw.Word.AudioUrl`. | `VocabularyApp.WebApi/DTOs/UserVocabularyDTOs.cs:3-18`; `WordService.cs:442-465,524-547` |
| Angular lookup model | `WordLookupResult.audioUrl?: string`. | `VocabularyApp.UI/src/app/models/word-lookup.model.ts:17-23` |
| Angular vocabulary model | `VocabularyItem.audioUrl?: string`. | `word-lookup.model.ts:47-62` |
| Lookup mapping | Backend `wordDto.audioUrl` becomes `currentWord.audioUrl`. | `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts:199-213` |
| Display | Play is rendered only for a truthy `currentWord.audioUrl`. | `word-lookup.component.html:120-126` |
| Playback | Component creates `new Audio(normalizedUrl)`, calls `play()`, and falls back to speech synthesis on failure. | `word-lookup.component.ts:424-485` |

`Pronunciation` is separate phonetic text on `Word` (`Word.cs:13-17`). WordsAPI pronunciation text is mapped into it and the UI displays it independently (`WordService.cs:142-152`; `word-lookup.component.html:137`). It is not an audio URL.

One secondary UI mapping omits audio: the direct vocabulary-search mapping builds a `WordLookupResult` with pronunciation but no `audioUrl` (`word-lookup.component.ts:135-170`). `viewWordDetails`, however, calls the full lookup endpoint (`word-lookup.component.ts:488-497`), whose mapping does include audio. This omission is worth covering in future tests but does not explain the repository-confirmed new WordsAPI behavior, because new rows already store null.

## 3. Provider Mapping

The configured base is `https://wordsapiv1.p.rapidapi.com/`, with RapidAPI host/key headers (`VocabularyApp.WebApi/Program.cs:60-75`; `VocabularyApp.WebApi/appsettings.json:10-13`). Lookup calls relative endpoint `words/{escaped-term}`, producing the effective `/words/{word}` request (`WordService.cs:63-68`).

The current provider contract contains:

- `WordsApiResponse.Word`
- `WordsApiResponse.Results`
- `WordsApiResponse.Pronunciation`, a dictionary of pronunciation text
- result definition, part of speech, and examples

It contains no audio/media property (`VocabularyApp.WebApi/DTOs/External/WordsApiDtos.cs:5-17`). The service selects pronunciation text, then explicitly assigns `AudioUrl = null` to a new canonical word (`WordService.cs:142-153`).

Behavior by path:

| Path | Audio behavior |
|---|---|
| Database cache hit | Returns stored `word.AudioUrl` unchanged; no provider request, validation, refresh, or clear (`WordService.cs:36-60,667-675`). |
| Provider cache miss | Current DTO maps pronunciation text but no media field (`WordsApiDtos.cs:5-17`). |
| New `Word` insert | Explicitly persists `AudioUrl = null` (`WordService.cs:147-173`). |
| Existing `Word` update | No provider-refresh/update path was found; an existing canonical word exits through the cache-hit branch. |
| Add to vocabulary | Creates or returns `UserWord`; it does not modify canonical word audio. Repository-wide writes show the only current explicit assignment is the cache-miss insert. |
| Preferred-definition update | Mutates `UserWord.PreferredWordDefinitionId` and synchronized POS only; it does not write `Word.AudioUrl`. |

Current code contains no DictionaryAPI.dev DTO or request. Git commit `a243ba5` removed `DictionaryApiDTOs.cs`, the `/api/v2/entries/en/{word}` call, and mapping from the first nonblank `Phonetics.Audio`; it introduced the current WordsAPI DTO and explicit null assignment. This directly confirms that the provider migration stopped populating audio for new rows.

## 4. New-Word Behavior

The exact flow is:

1. Cache miss calls WordsAPI `/words/{word}` (`WordService.cs:63-86`).
2. `WordsApiResponse` deserializes pronunciation text but has no audio member (`WordsApiDtos.cs:5-17`).
3. The new canonical `Word` receives `AudioUrl = null` and is saved (`WordService.cs:142-173`).
4. The saved entity is reloaded and mapped; `WordDto.AudioUrl` remains null (`WordService.cs:175-194,667-675`).
5. Angular assigns that null to `currentWord.audioUrl` (`word-lookup.component.ts:199-213`).
6. The Play button requires `*ngIf="currentWord.audioUrl"`, so it is not rendered (`word-lookup.component.html:120-126`).

This is deterministic current behavior, not an R5 side effect. The null mapping was introduced in provider-migration commit `a243ba5`, which precedes the R5 implementation commit.

## 5. Historical/Cached Word Behavior

Historical values can remain indefinitely because `Words.AudioUrl` is persisted and nullable, while lookup treats any matching `Word` as authoritative cache data and does not refresh it (`Word.cs:16-20`; `WordService.cs:36-60`). `LastUpdatedFromApi` exists but is not used to expire or refresh cache data (`Word.cs:19-20`). No current application write clears a stored URL.

Repository history proves the pre-WordsAPI path could persist DictionaryAPI.dev `phonetics[].audio`. It does not prove that every production non-null URL came from that provider; imports, manual changes, or other historical code/data would require database evidence. The application does not store provider provenance, validation time, HTTP status, or expiry, so a URL's origin and health cannot be inferred from the row alone.

Cache-hit mapping returns the stored URL through `WordDto` (`WordService.cs:42-60,667-675`). Vocabulary list/search responses also expose it (`WordService.cs:450-465,532-547`). There is no reachability check, HEAD/GET validation, or scheduled cleanup.

## 6. Playback Implementation

`playAudio` normalizes protocol-relative URLs to HTTPS and rewrites `http://` to `https://`, pauses the previous `HTMLAudioElement`, constructs `new Audio(url)`, sets preload, and calls `play()` (`word-lookup.component.ts:424-466`). It does not use an `<audio>` element in the template.

Synchronous construction errors and rejected `play()` promises are caught. Both are logged to the browser console and invoke `SpeechSynthesisUtterance` for the current word (`word-lookup.component.ts:440-485`). There is no visible toast/error, button disablement, invalid-URL marker, retry state, backend report, or database cleanup. If speech synthesis also fails or is unavailable, the user receives no visible explanation.

Potential failure classes include a removed/stale URL, 403/hotlink policy, redirect/authentication requirements, unsupported content type/codec, TLS/certificate failure, CSP or other browser policy, and network failure. Mixed content is partially mitigated by rewriting HTTP to HTTPS, but that rewrite can itself fail when the host has no equivalent HTTPS resource. Direct cross-origin media can play without exposing media bytes to application code in many cases; CORS is more critical for credentialed access or programmatic media processing. Still, provider cross-origin/hotlink policies can prevent loading. The current code does not establish which class caused the observed production failures.

## 7. Read-Only Production Audio Audit

No production query or mutation was performed. Run these read-only queries against the confirmed production database:

```sql
-- A. Total words with a populated AudioUrl.
SELECT COUNT_BIG(*) AS WordsWithAudioUrl
FROM dbo.Words
WHERE AudioUrl IS NOT NULL
  AND LEN(LTRIM(RTRIM(AudioUrl))) > 0;

-- B. Total words with AudioUrl NULL (blank values are reported separately).
SELECT COUNT_BIG(*) AS WordsWithNullAudioUrl
FROM dbo.Words
WHERE AudioUrl IS NULL;

SELECT COUNT_BIG(*) AS WordsWithBlankAudioUrl
FROM dbo.Words
WHERE AudioUrl IS NOT NULL
  AND LEN(LTRIM(RTRIM(AudioUrl))) = 0;

-- C. Recently inserted words with AudioUrl NULL.
SELECT TOP (100) Id AS WordId, Text, AudioUrl, CreatedAt
FROM dbo.Words
WHERE AudioUrl IS NULL
ORDER BY CreatedAt DESC, Id DESC;

-- D and E. Distinct host/domain values and counts by host/domain.
-- Invalid or relative values are grouped instead of causing parsing failures.
WITH HostValues AS
(
    SELECT CASE
        WHEN AudioUrl LIKE '%://%' THEN SUBSTRING(AudioUrl, CHARINDEX('://', AudioUrl) + 3, 500)
        WHEN AudioUrl LIKE '//%' THEN SUBSTRING(AudioUrl, 3, 500)
        ELSE NULL
    END AS HostAndPath
    FROM dbo.Words
    WHERE AudioUrl IS NOT NULL
      AND LEN(LTRIM(RTRIM(AudioUrl))) > 0
), ParsedHosts AS
(
    SELECT CASE
        WHEN HostAndPath IS NULL OR HostAndPath = '' THEN '[MALFORMED_OR_RELATIVE]'
        ELSE LOWER(LEFT(HostAndPath, CHARINDEX('/', HostAndPath + '/') - 1))
    END AS AudioHost
    FROM HostValues
)
SELECT AudioHost, COUNT_BIG(*) AS UrlCount
FROM ParsedHosts
GROUP BY AudioHost
ORDER BY UrlCount DESC, AudioHost;

-- F. Sample populated rows. The entity key Id is aliased as WordId.
SELECT TOP (100) Id AS WordId, Text, AudioUrl, CreatedAt
FROM dbo.Words
WHERE AudioUrl IS NOT NULL
  AND LEN(LTRIM(RTRIM(AudioUrl))) > 0
ORDER BY CreatedAt DESC, Id DESC;
```

These establish volume, dates, and hosts but do not test URL reachability. Any later validation should avoid leaking full URLs if they contain tokens and should respect provider terms/rate limits.

## 8. External WordsAPI Capability Verification

External provider documentation has now been verified. The current production base URL remains `https://wordsapiv1.p.rapidapi.com`. WordsAPI supports the full lookup `GET /words/{word}` and the dedicated `GET /words/{word}/pronunciation` endpoint. Its documented pronunciation capability is pronunciation/IPA/phonetic text.

Supported by WordsAPI:

- definitions and lexical results;
- pronunciation/IPA text in the documented full-word response;
- a dedicated pronunciation endpoint.

Not present in the current documented contract:

- a playable pronunciation-audio URL;
- MP3, WAV, or other audio media;
- a pronunciation audio stream or another directly playable browser resource.

Therefore, **WordsAPI currently exposes pronunciation/IPA information but no documented playable audio-media URL/resource**. This conclusion is limited to the current documented contract; it does not claim that WordsAPI can never add audio later. It also agrees with the repository contract, which models pronunciation text, models no WordsAPI media field, and deliberately stores null audio for new words (`WordsApiDtos.cs:5-17`; `WordService.cs:142-153`).

## 9. Fix Options

| Option | Benefits | Risks | Scope | Recommendation |
|---|---|---|---|---|
| A — WordsAPI plus a separate audio provider | Decouples lexical data from playable media. | Adds a dependency, matching policy, limits, cost, licensing, and outage handling. | Backend client/cache policy and Angular failure UX; migration is not inherently required. | **Recommended primary direction.** Select the provider during implementation planning. |
| B — Use WordsAPI directly for audio | Would retain one vendor. | The current documented contract exposes no playable resource. | Not presently actionable. | **Do not recommend** unless new direct, current evidence proves playable WordsAPI media; no such repository evidence exists. |
| C — Show no audio button when no usable source exists | Truthful and consistent with current null behavior. | Temporarily provides no recorded audio. | Small Angular availability/failure UX change; no cleanup required. | Appropriate interim behavior; do not discard historical values that may still work. |
| D — Dedicated provider with backend proxy/cache | Supports authenticated or short-lived media and centralizes policy. | Adds bandwidth, storage, SSRF, range, validation, licensing, and operational burdens. | Backend media endpoint/cache and Angular contract; storage design may change. | Use only if direct HTTPS playback is unsuitable and terms permit it. |
| E — Store a stable pronunciation reference | Avoids fragile or expiring URLs and enables resolution/refresh. | Couples resolution to a provider and may require persistence changes. | Provider-dependent resolver and possibly a migration. | Prefer when the selected provider supplies stable IDs; do not force it before selection. |

The smallest reliable solution consistent with the current architecture is A with C's truthful UI behavior: retain WordsAPI, add one backend client for a dedicated audio source, keep the existing audio response contract where practical, and handle playback failure in Angular. Add D only if direct delivery is demonstrably unsuitable. Use E when the selected provider offers a stable reference safer than storing its URLs.

## 10. Recommended Architecture Direction

Keep WordsAPI responsible for definitions and lexical data. Its IPA/pronunciation text may continue to be captured or displayed separately, but it must not be treated as playable audio. Add a dedicated pronunciation-audio provider for recorded audio, with credentials and provider lookup kept in the backend so Angular never needs a secret. Do not revert to DictionaryAPI.dev merely to regain audio unless a later provider comparison establishes it as the best reliable source.

This separation is safer because dictionary changes cannot silently redefine the audio contract, each provider is evaluated against its actual capability and terms, audio-specific failure/rate/cache policy stays isolated, and the existing lexical lookup remains stable. Preserve historical URLs until the audit classifies them; treat them as untrusted legacy references and replace them only when newly resolved audio is valid.

## 11. Recommended Target Behavior

- Show Play only when the application has a nonblank audio reference that is supported by the selected provider policy.
- If no media exists, omit Play. WordsAPI IPA text alone must never cause Play to appear. If IPA is surfaced, display it separately from the audio control. If product wants speech synthesis to remain available, label it separately rather than presenting it as provider-recorded pronunciation.
- On playback rejection, show a short non-blocking message such as “Pronunciation audio is unavailable,” then optionally offer/use speech synthesis.
- Disable the failed Play control for the current displayed word to prevent repeated failing requests; do not mutate the database from the browser failure alone.
- Treat historical and new URLs through the same validation/failure policy, while using stored provider provenance if a future model adds it.
- Keep lookup, save, favorites, preferred definitions, and quiz behavior unchanged.

## 12. Historical Audio Strategy

Do not bulk-clear or backfill before running the section 7 audit and selecting a supported provider. The safest initial strategy is:

1. leave existing values untouched during analysis;
2. make playback failure visible and nonfatal;
3. classify stored hosts/counts/dates;
4. distinguish valid historical audio, stale historical audio, no audio, and newly resolved provider audio in the eventual policy;
5. after provider selection, resolve or validate lazily on access and replace an old value only when a new provider result is valid;
6. clear a value only after confirmed failure and an approved policy, or when a reviewed host-specific retirement rule applies.

Lazy refresh minimizes production load, cost, and destructive data changes. A bulk clear is reasonable only if the audit proves all non-null values belong to a retired provider and product accepts immediate loss. Backfill is optional and should be rate/cost/licensing controlled; it need not block support for newly looked-up words. No schema migration is required for simple replacement URLs, but provider provenance, stable reference keys, validation timestamps, or failure state would require a separately reviewed model/migration decision.

## 13. Proposed Audio-Provider Requirements

Provider selection is the first implementation-planning activity, or a short comparison immediately before planning. Do not select a provider from this repository analysis alone. It must offer:

- English pronunciation audio;
- stable HTTPS media or a supported playback endpoint;
- a browser-compatible format;
- reliable word lookup and predictable not-found behavior;
- acceptable rate limits and production reliability;
- clear licensing, usage, storage, redistribution, proxy, and caching terms;
- reasonable cost or free-tier capacity for current VocabularyApp usage;
- backend-compatible authentication that never exposes secrets in Angular.

## 14. API / Backend Impact

A future implementation will need a typed audio-source contract rather than another implicit URL assignment. Depending on the selected option:

- extend the provider DTO/client or add a dedicated audio-provider client;
- normalize only allowed HTTPS hosts/schemes and reject malformed/untrusted URLs;
- decide cache lifetime and whether cache hits may refresh audio without replacing definitions;
- preserve unrelated `Word`, definitions, and all `UserWord` state;
- keep `WordDto.AudioUrl` compatible or add explicit audio availability/source fields;
- distinguish provider “no audio” from provider outage;
- add structured, non-secret logging and rate-limit handling;
- for a proxy, enforce host allowlists, response-size/content-type limits, timeouts, redirects, range requests, and SSRF protections.

No R5 identity, `UserWord`, preferred-definition, or quiz schema change is implicated.

## 15. Angular Impact

The existing model and conditional button can remain with small changes. Add component state for playback-in-progress/failure, a user-visible non-blocking error, and tests around `Audio.play()` rejection. Decide explicitly whether speech synthesis is an automatic fallback or a separately labeled action. Avoid permanently hiding or clearing server data solely from one transient browser failure. Ensure both lookup and vocabulary-detail mapping carry the same audio property (`word-lookup.component.ts:135-170,199-213`).

## 16. Test Plan

Backend tests:

- provider response with valid audio maps, persists, and returns it;
- provider response with no audio persists/returns null without failing lookup;
- cache hit with valid stored audio returns it without unnecessary provider call;
- cache hit with null audio follows the chosen refresh/no-refresh policy;
- stale existing URL follows the chosen validation/refresh policy;
- provider failure does not corrupt or clear cached definitions/audio unexpectedly;
- malformed/untrusted audio reference is rejected safely;
- audio update preserves word text, pronunciation, definitions, `UserWord` rows, favorites, notes, counters, preferred definitions, and quiz/history relationships;
- if proxying, authentication is server-side and range/content-type/size/redirect/SSRF cases are covered.

Angular tests:

- Play visible for a valid nonblank `audioUrl`;
- Play hidden for null/blank audio;
- `new Audio`/playback is invoked with the normalized expected URL;
- protocol-relative and HTTP normalization follows the approved policy;
- rejected playback is caught, shows the approved message, and does not crash;
- stale URL failure disables only the current control or otherwise follows target UX;
- speech-synthesis fallback behavior is explicit and tested;
- lookup/save and existing R5 behavior remain unaffected;
- vocabulary-detail and fresh lookup mappings behave consistently.

Production smoke testing should cover one newly looked-up uncached word and one audited old cached word. Verify button visibility, audible playback or explicit no-audio behavior, failure messaging, browser console/network response, and that no canonical/user data changes unexpectedly.

## 17. Risks and Edge Cases

- Provider URLs may expire, redirect, require headers, or prohibit storage/proxying.
- Browser autoplay rules may reject `play()` even when media is valid; a direct user click usually helps but is not absolute.
- Rewriting HTTP to HTTPS can create an invalid endpoint.
- Multiple dialects or recordings require deterministic selection and labeling.
- Homographs/canonical casing can return a pronunciation for the wrong sense or locale.
- A transient failure must not permanently erase a valid URL.
- Automatic validation/backfill can consume quota or trigger provider rate limits.
- Proxying creates SSRF, bandwidth, content-validation, caching, and licensing responsibilities.
- Existing URLs lack provenance, so host/date audits are necessary before cleanup.
- Speech synthesis is device/browser dependent and is not equivalent to provider-recorded pronunciation.
- Direct vocabulary-search mapping currently omits `audioUrl`, creating a potential inconsistent UI path.

## 18. Scope Recommendation

Track this as a **provider-migration follow-up**, not R5 and not merely an isolated Angular bug. Recommended title: **Restore Pronunciation Audio**. This is not R5, this is not a database-identity issue, and it is follow-up work from the DictionaryAPI.dev → WordsAPI provider transition. If the project requires a remediation identifier, assign the next unreserved identifier during planning rather than reusing R5.

The provider migration directly changed audio population, while the stale-data and failure-UX work spans provider mapping, caching policy, backend response behavior, Angular playback, and production data assessment. That scope is larger than a one-line UI fix but does not require reopening canonical word identity.

## 19. Implementation-Planning Readiness

The former blocker—external WordsAPI capability verification—is resolved: the current documented contract does not expose playable audio media. No other architectural question prevents planning. Selecting a dedicated audio provider, confirming its licensing/authentication/delivery constraints, and using the production audit to refine the legacy-data policy can safely be explicit early activities in the implementation plan.

READY FOR IMPLEMENTATION PLANNING
