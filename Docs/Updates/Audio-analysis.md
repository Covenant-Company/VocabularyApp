# Pronunciation Audio Analysis

## 1. Executive Summary

Newly fetched words lack the play button because the current WordsAPI cache-miss path explicitly persists `Word.AudioUrl = null` (`VocabularyApp.WebApi/Services/WordService.cs:142-153`). That null is exposed through `WordDto.AudioUrl` (`WordService.cs:667-675`), consumed as `currentWord.audioUrl` (`VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts:199-213`), and fails the template's `*ngIf="currentWord.audioUrl"` condition (`word-lookup.component.html:120-126`). This behavior is repository-confirmed.

Older cached words may still show Play because `AudioUrl` is a nullable, persisted column on `Words`, cache hits return it unchanged, and there is no refresh or validation path (`VocabularyApp.Data/Models/Word.cs:13-20`; `WordService.cs:36-60,667-675`). Git history confirms the former DictionaryAPI.dev implementation selected the first nonblank `phonetics[].audio` value before the WordsAPI migration. Repository evidence supports that historical source as a mechanism, but production SQL is required to prove the origin of each current value.

Old playback can fail because the browser loads the stored external URL directly and the application neither validates it beforehand nor clears it after failure. Playback rejection is caught, logged only to the console, and followed by browser speech synthesis (`word-lookup.component.ts:424-485`). The exact production failure—404, authorization/hotlink rejection, TLS, redirect, media format, network policy, or another cause—is not proven without inspecting the stored hosts and browser/network error.

Recommended next step: run the read-only production audio audit in section 7, then verify whether the current WordsAPI subscription/API offers stable pronunciation media. Until that provider capability is verified, retain WordsAPI definitions, treat stored URLs as untrusted, and plan a small provider-migration follow-up that gives playback a visible failure state. Do not change R5.

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

## 7. Existing Database Audio Data

No production query or mutation was performed. Run these read-only queries against the confirmed production database:

```sql
-- Counts and age range for persisted audio references.
SELECT
    COUNT_BIG(*) AS TotalWords,
    SUM(CASE WHEN AudioUrl IS NOT NULL AND LEN(LTRIM(RTRIM(AudioUrl))) > 0 THEN 1 ELSE 0 END) AS WordsWithAudioUrl,
    SUM(CASE WHEN AudioUrl IS NULL OR LEN(LTRIM(RTRIM(AudioUrl))) = 0 THEN 1 ELSE 0 END) AS WordsWithoutAudioUrl,
    MIN(CASE WHEN AudioUrl IS NOT NULL AND LEN(LTRIM(RTRIM(AudioUrl))) > 0 THEN CreatedAt END) AS OldestAudioRow,
    MAX(CASE WHEN AudioUrl IS NOT NULL AND LEN(LTRIM(RTRIM(AudioUrl))) > 0 THEN CreatedAt END) AS NewestAudioRow
FROM dbo.Words;

-- Stored rows, without joining user-owned data.
SELECT Id, Text, AudioUrl, CreatedAt, LastUpdatedFromApi
FROM dbo.Words
WHERE AudioUrl IS NOT NULL
  AND LEN(LTRIM(RTRIM(AudioUrl))) > 0
ORDER BY CreatedAt, Id;

-- Distinct host-like values. Review relative/other values separately.
WITH Parsed AS
(
    SELECT CASE
        WHEN AudioUrl LIKE '%://%' THEN
            SUBSTRING(AudioUrl, CHARINDEX('://', AudioUrl) + 3, 500)
        WHEN AudioUrl LIKE '//%' THEN SUBSTRING(AudioUrl, 3, 500)
        ELSE NULL
    END AS HostAndPath
    FROM dbo.Words
    WHERE AudioUrl IS NOT NULL
      AND LEN(LTRIM(RTRIM(AudioUrl))) > 0
)
SELECT
    LOWER(CASE WHEN CHARINDEX('/', HostAndPath + '/') > 0
        THEN LEFT(HostAndPath, CHARINDEX('/', HostAndPath + '/') - 1)
        ELSE HostAndPath END) AS AudioHost,
    COUNT_BIG(*) AS UrlCount
FROM Parsed
GROUP BY LOWER(CASE WHEN CHARINDEX('/', HostAndPath + '/') > 0
    THEN LEFT(HostAndPath, CHARINDEX('/', HostAndPath + '/') - 1)
    ELSE HostAndPath END)
ORDER BY UrlCount DESC, AudioHost;

-- Recent words that have no usable audio reference.
SELECT TOP (100) Id, Text, CreatedAt, LastUpdatedFromApi
FROM dbo.Words
WHERE AudioUrl IS NULL OR LEN(LTRIM(RTRIM(AudioUrl))) = 0
ORDER BY CreatedAt DESC, Id DESC;
```

These establish volume, dates, and hosts but do not test URL reachability. Any later validation should avoid leaking full URLs if they contain tokens and should respect provider terms/rate limits.

## 8. Provider Capability Assessment

Repository-confirmed: the currently modeled `/words/{word}` response supplies pronunciation **text** through `Pronunciation`; the application contract models no pronunciation media and deliberately stores null audio (`WordsApiDtos.cs:5-17`; `WordService.cs:142-153`).

Not repository-confirmed: whether the current WordsAPI/RapidAPI product, subscription tier, another endpoint, or a newer response version provides stable pronunciation audio suitable for storage or playback.

**Requires external provider documentation verification.** Check the current official contract and subscribed plan for:

1. whether `/words/{word}` or a dedicated endpoint returns an audio/media URL or binary pronunciation;
2. response field and endpoint stability;
3. authentication requirements for browser playback versus server retrieval;
4. URL lifetime and caching/redistribution rights;
5. rate limits and cost for lookup, backfill, or proxying;
6. supported dialects/voices and missing-audio behavior.

## 9. Fix Options

| Option | Benefits | Risks | Scope | Recommendation |
|---|---|---|---|---|
| A — WordsAPI definitions plus a separate audio provider | Decouples definitions from audio; can choose a provider designed for media. | New dependency/key/cost, word matching, rate limits, licensing, cache invalidation, and provider outages. | Backend client/mapping/cache policy; Angular failure UX; possibly provenance/validation fields. Migration not inherently required. | Strong candidate if WordsAPI has no suitable media; verify provider terms first. |
| B — Use a WordsAPI audio capability | One vendor and existing authentication/billing; potentially smallest backend change. | Capability is unverified; URLs may require auth, expire, disallow browser use, or add cost. | Provider DTO/client and tests; Angular error UX; optional refresh/backfill. | Preferred only if official current documentation confirms stable playable media and acceptable terms. |
| C — Disable stale stored audio and show no Play until supported | Smallest reliable behavior; eliminates misleading dead controls and calls to unknown hosts. | Removes audio for rows that might still work; speech-synthesis button is currently also hidden when URL is null. | Backend/UI policy plus optional read-only audit and separately approved cleanup. No schema change. | Safest immediate containment; do not bulk-clear until host/count audit and approval. |
| D — Backend proxy/cache for audio | Same-origin playback, centralized authentication/validation, better observability and stable app URLs. | Highest operational/security/cost burden; bandwidth, storage, SSRF controls, licensing, range requests, content validation. | New backend endpoint/service/cache, configuration, monitoring, Angular URL use; DB/storage design may change. | Consider only if provider authentication/CORS/lifetime makes direct playback unreliable and terms permit caching/proxying. |
| E — Store a provider/key reference instead of external URL | Avoids persisted expiring URLs; enables resolution/refresh and provenance. | Requires provider coupling and a request at playback/refresh; provider changes still need handling. | Model/API changes and likely migration/backfill if persisted; backend resolver and Angular contract. | Good longer-term design if chosen provider exposes stable pronunciation IDs; excessive for an unverified source. |

The smallest reliable sequence is C as containment/UX correction, then B if WordsAPI documentation supports it; otherwise A. Add D only when direct media delivery is demonstrably unsuitable. E is justified only with a stable provider identifier.

## 10. Recommended Target Behavior

- Show Play only when the application has a nonblank audio reference that is supported by the selected provider policy.
- If no media exists, omit Play. If product wants speech synthesis to remain available, label it separately rather than presenting it as provider-recorded pronunciation.
- On playback rejection, show a short non-blocking message such as “Pronunciation audio is unavailable,” then optionally offer/use speech synthesis.
- Disable the failed Play control for the current displayed word to prevent repeated failing requests; do not mutate the database from the browser failure alone.
- Treat historical and new URLs through the same validation/failure policy, while using stored provider provenance if a future model adds it.
- Keep lookup, save, favorites, preferred definitions, and quiz behavior unchanged.

## 11. Data Cleanup / Backfill Strategy

Do not bulk-clear or backfill before running the section 7 audit and selecting a supported provider. The safest initial strategy is:

1. leave existing values untouched during analysis;
2. make playback failure visible and nonfatal;
3. classify stored hosts/counts/dates;
4. after provider selection, refresh audio lazily on the next canonical lookup or through an explicit rate-limited maintenance job;
5. clear a stored value only after a trusted validation result or when a reviewed host-specific retirement rule applies.

Lazy refresh minimizes production load, cost, and destructive data changes. A bulk clear is reasonable only if the audit proves all non-null values belong to a retired provider and product accepts immediate loss. Backfill is optional and should be rate/cost/licensing controlled; it need not block support for newly looked-up words. No schema migration is required for simple replacement URLs, but provider provenance, stable reference keys, validation timestamps, or failure state would require a separately reviewed model/migration decision.

## 12. API / Backend Impact

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

## 13. Angular Impact

The existing model and conditional button can remain with small changes. Add component state for playback-in-progress/failure, a user-visible non-blocking error, and tests around `Audio.play()` rejection. Decide explicitly whether speech synthesis is an automatic fallback or a separately labeled action. Avoid permanently hiding or clearing server data solely from one transient browser failure. Ensure both lookup and vocabulary-detail mapping carry the same audio property (`word-lookup.component.ts:135-170,199-213`).

## 14. Test Plan

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

## 15. Risks and Edge Cases

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

## 16. Scope Recommendation

Track this as a **provider-migration follow-up**, not R5 and not merely an isolated Angular bug. Recommended title: **Restore Pronunciation Audio**. If the project requires a remediation identifier, assign the next unreserved identifier during planning rather than reusing R5.

The provider migration directly changed audio population, while the stale-data and failure-UX work spans provider mapping, caching policy, backend response behavior, Angular playback, and production data assessment. That scope is larger than a one-line UI fix but does not require reopening canonical word identity.

## 17. Implementation-Planning Readiness

The exact unanswered question is whether the current subscribed WordsAPI/RapidAPI contract offers stable pronunciation media that may be cached or played by this application, through which endpoint/field, with what authentication, URL lifetime, licensing, rate limit, and cost. The production host/count audit is also needed to choose a safe historical-data policy.

NOT READY FOR IMPLEMENTATION PLANNING
