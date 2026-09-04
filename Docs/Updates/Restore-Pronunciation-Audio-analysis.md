# Restore Pronunciation Audio — Analysis

## 1. Executive Summary

Pronunciation is currently owned by the Angular `WordLookupComponent`. The word header contains a **Play** button, but the template renders it only when `currentWord.audioUrl` is truthy. Clicking it calls `playAudio(audioUrl)`, which creates an `HTMLAudioElement` with `new Audio(...)`. Browser speech synthesis already exists in the component, but only as a private fallback after URL playback fails. Consequently, a word with no `AudioUrl` has no pronunciation control and can never reach that fallback.

The smallest reliable restoration is to make the existing control invoke browser speech synthesis directly with the displayed word. The component should detect support for both `window.speechSynthesis` and `SpeechSynthesisUtterance`, trim and validate the word, cancel queued/current speech, construct one utterance, set `lang = 'en-US'` and a conservative rate, and call `speak()`. No audio URL and no pronunciation-time application API request are needed.

The initial implementation should remain frontend-only. `AudioUrl` is persisted and appears in public backend and frontend contracts, existing cached data, backend provider behavior, and backend tests. Removing it is unnecessary for browser speech and would create avoidable migration and compatibility risk. Its eventual retirement, along with the Merriam-Webster audio integration, should be a separate decision and change set.

## 2. Current Pronunciation Implementation

### Control and owner

- The pronunciation control is in `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`, in the displayed-word header at lines 122–127.
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` owns all current pronunciation state and behavior.
- The public click handler is `playAudio(audioUrl: string)` at lines 428–456.
- There is no public `pronounceWord` or `speakWord` method. The only speech method is the private `playSpeechSynthesisFallback()` at lines 483–500.

### Visibility and enablement

The template currently uses:

```html
<button *ngIf="currentWord.audioUrl"
        (click)="playAudio(currentWord.audioUrl!)"
        [disabled]="audioPlaybackFailed">
```

Therefore:

- the button is absent when `AudioUrl` is null, undefined, or an empty string;
- it is present for any truthy string, without validating that the URL can play;
- it becomes disabled after URL playback fails;
- a failed control remains visible with the title `Pronunciation audio unavailable` until lookup state resets.

The button already has visible text (`🔊 Play`) and a title, but no explicit `type="button"` or `aria-label`. It is not currently inside a form, so omission of `type` is harmless today but making the type explicit is a low-cost safeguard.

### Playback path

`playAudio`:

1. returns immediately after a prior playback failure;
2. rejects a null/blank URL through `normalizeAudioUrl`;
3. pauses and rewinds the previous `HTMLAudioElement`;
4. normalizes protocol-relative and HTTP URLs to HTTPS;
5. creates `new Audio(normalizedAudioUrl)`, sets `preload = 'auto'`, and invokes `play()`;
6. on synchronous construction failure or a rejected `play()` promise, sets `audioPlaybackFailed`, shows an error toast, and calls speech synthesis as a fallback.

The fallback trims `currentWord.word`, checks `typeof window !== 'undefined'` and `window.speechSynthesis`, calls `cancel()`, constructs `new SpeechSynthesisUtterance(text)`, sets `lang = 'en-US'` and `rate = 0.95`, then calls `speak()` inside a `try/catch`.

There is no template `<audio>` element. The dependency is programmatic `new Audio(...)`. Current runtime audio can come from persisted legacy URLs or the current Merriam-Webster pronunciation-audio integration. WordsAPI supplies dictionary data and pronunciation text but its modeled response has no audio field. DictionaryAPI.dev is no longer present in current application code; earlier project documentation identifies it as the historical source of at least some stored URLs.

The component implements `OnInit`, not `OnDestroy`. It does not stop its `HTMLAudioElement` or speech synthesis when the component is destroyed or when the displayed word is cleared/replaced.

## 3. Current AudioUrl Data Flow

The current end-to-end flow is:

```text
Merriam-Webster audio lookup / historical stored URL
  -> Word.AudioUrl (nullable database column)
  -> WordDto.AudioUrl or UserVocabularyItemDto.AudioUrl
  -> JSON audioUrl
  -> WordLookupResult.audioUrl / VocabularyItem.audioUrl
  -> currentWord.audioUrl
  -> template *ngIf and playAudio(audioUrl)
  -> new Audio(url).play()
```

### Angular models and mapping

- `VocabularyApp.UI/src/app/models/word-lookup.model.ts`
  - `WordLookupResult.audioUrl?: string`
  - `VocabularyItem.audioUrl?: string`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`
  - `viewExistingWord` maps `userWord.audioUrl` into `currentWord.audioUrl` (lines 135–174).
  - `searchNewWord` maps lookup `wordDto.audioUrl` into `currentWord.audioUrl` (lines 188–218).
  - `viewWordDetails` delegates to the full lookup path, so its eventual detail view receives the lookup DTO mapping.
- `VocabularyApp.UI/src/app/services/api.service.ts` is a generic HTTP wrapper. It neither declares nor transforms `AudioUrl`; normal JSON camel-casing yields `audioUrl` to the component.

### Backend contracts and mappings

- `VocabularyApp.WebApi/DTOs/WordDTOs.cs`: nullable `WordDto.AudioUrl` returned in `WordLookupResponse.Word`.
- `VocabularyApp.WebApi/DTOs/UserVocabularyDTOs.cs`: nullable `UserVocabularyItemDto.AudioUrl` returned by vocabulary list/search operations.
- `VocabularyApp.WebApi/Services/WordService.cs`:
  - injects `IPronunciationAudioService`;
  - on a cache hit, attempts to fill a missing `Word.AudioUrl` before mapping (lines 45–54 and 224–243);
  - on a WordsAPI cache miss, creates the word with null audio, then optionally resolves audio before saving (lines 152–186);
  - maps `uw.Word.AudioUrl` into vocabulary list and search DTOs (lines 500–524 and 582–606);
  - maps `word.AudioUrl` into `WordDto.AudioUrl` (lines 725–735).
- `VocabularyApp.WebApi/Services/IPronunciationAudioService.cs` defines the optional audio URL lookup abstraction.
- `VocabularyApp.WebApi/Services/MerriamWebsterPronunciationService.cs` implements that abstraction and constructs provider MP3 URLs.
- `VocabularyApp.WebApi/Program.cs` registers the WordsAPI client and the Merriam-Webster pronunciation client.
- `VocabularyApp.WebApi/Configuration/MerriamWebsterOptions.cs`, `VocabularyApp.WebApi/DTOs/External/MerriamWebsterDtos.cs`, and the `MerriamWebster` configuration sections support that backend lookup.
- `VocabularyApp.WebApi/DTOs/External/WordsApiDtos.cs` has pronunciation text but no audio URL/media field.

### Entity and database

- `VocabularyApp.Data/Models/Word.cs` defines nullable `Word.AudioUrl` with a 500-character maximum.
- `VocabularyApp.Data/Migrations/20251030144345_AddAudioUrlColumn.cs` adds the effective nullable `Words.AudioUrl` column; the model snapshot and later migration designers retain it.
- `VocabularyApp.Data/Migrations/20251030143529_AddAudioUrlToWords.cs` and its designer also appear in the migration history and are related historical artifacts that should not be edited for this feature.

### Compatibility decision

`AudioUrl` should remain in the contracts and database during this restoration. It is not needed by the new browser pronunciation path, but retaining it:

- avoids a breaking API contract change for any uninspected/older client;
- preserves existing database data and rollback options;
- avoids an unrelated destructive migration;
- keeps current backend tests and provider integration stable;
- lets the frontend change be isolated and reversible.

The new frontend must not use `AudioUrl` to decide whether pronunciation is available. After migration to speech synthesis, `audioUrl` can remain mapped but unused by this component. A later cleanup may deprecate and remove it only after consumers, production data, provider cost/latency, and deployment compatibility are assessed.

## 4. Files Involved

### Expected Angular implementation/test files

| File | Expected impact |
|---|---|
| `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` | Replace URL-first playback with direct speech synthesis; add support/word guards and cleanup; remove obsolete component audio failure/audio-element state if no longer used. |
| `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` | Stop gating the button on `audioUrl`; call the word-based handler; expose an accessible supported/unsupported state. |
| `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts` | Replace URL-audio tests with speech synthesis behavior, guard, cleanup, and regression tests. |
| `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss` | Probably no change. Existing utility classes can express the button state. Change only if an unsupported-state treatment cannot be expressed cleanly in the template. |

### Angular files that are related but normally unchanged

| File | Reason to inspect/preserve |
|---|---|
| `VocabularyApp.UI/src/app/models/word-lookup.model.ts` | Contains both optional `audioUrl` properties. Keep them for compatibility in this phase. |
| `VocabularyApp.UI/src/app/services/api.service.ts` | Transports lookup/vocabulary responses but has no audio-specific behavior. |
| `VocabularyApp.UI/angular.json` | Confirms a browser-only application target and Karma test configuration; no SSR target exists. |
| `VocabularyApp.UI/package.json` | Defines the test and production build commands. No dependency is required for the native browser API. |
| `VocabularyApp.UI/src/index.html` | Its language metadata is not a substitute for setting utterance `lang`; no change is required for this feature. |

### Backend/data files that appear related but should not change

- `VocabularyApp.WebApi/Services/WordService.cs`
- `VocabularyApp.WebApi/Services/IPronunciationAudioService.cs`
- `VocabularyApp.WebApi/Services/MerriamWebsterPronunciationService.cs`
- `VocabularyApp.WebApi/Configuration/MerriamWebsterOptions.cs`
- `VocabularyApp.WebApi/DTOs/External/MerriamWebsterDtos.cs`
- `VocabularyApp.WebApi/DTOs/External/WordsApiDtos.cs`
- `VocabularyApp.WebApi/DTOs/WordDTOs.cs`
- `VocabularyApp.WebApi/DTOs/UserVocabularyDTOs.cs`
- `VocabularyApp.WebApi/Program.cs`
- `VocabularyApp.WebApi/appsettings.json`
- `VocabularyApp.WebApi/appsettings.Development.json`
- `VocabularyApp.Data/Models/Word.cs`
- `VocabularyApp.Data/Migrations/20251030143529_AddAudioUrlToWords.cs` and designer
- `VocabularyApp.Data/Migrations/20251030144345_AddAudioUrlColumn.cs` and designer
- `VocabularyApp.Data/Migrations/ApplicationDbContextModelSnapshot.cs` and later migration designers
- `VocabularyApp.WebApi.Tests/Services/MerriamWebsterPronunciationServiceTests.cs`
- `VocabularyApp.WebApi.Tests/Integration/DictionaryLookupApiTests.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/ControllablePronunciationAudioService.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/IntegrationTestSeeder.cs`

These backend tests should remain unchanged because the recommendation deliberately preserves backend behavior and contracts. They remain useful regression coverage.

## 5. Browser Speech Synthesis Analysis

### Support and capability detection

Speech synthesis is broadly available in current browsers, but support must still be treated as a runtime capability. MDN describes `window.speechSynthesis` and `SpeechSynthesisUtterance` as widely available while noting that some parts vary by browser. Relevant references are the [window speechSynthesis documentation](https://developer.mozilla.org/en-US/docs/Web/API/Window/speechSynthesis), [SpeechSynthesisUtterance documentation](https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesisUtterance), and [voiceschanged documentation](https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesis/voiceschanged_event).

The guard should verify all required globals, not only `window.speechSynthesis`:

```typescript
typeof window !== 'undefined' &&
'speechSynthesis' in window &&
typeof window.SpeechSynthesisUtterance === 'function'
```

Using `window.SpeechSynthesisUtterance` consistently makes the dependency explicit and easier to replace in tests. If unsupported, the safest UX is a visible disabled button with an explanatory accessible label/title such as “Pronunciation is not supported by this browser.” Hiding the button is acceptable but gives users no explanation; disabling is preferable if the product wants consistent layout and discoverability.

### Invocation and repeated clicks

On every valid click:

1. trim the displayed word;
2. return without constructing an utterance if it is empty;
3. call `window.speechSynthesis.cancel()`;
4. construct a fresh `SpeechSynthesisUtterance(trimmedWord)`;
5. set `lang = 'en-US'`;
6. set `rate = 0.95` (preserving the already chosen, slightly slower pronunciation rate);
7. call `speak(utterance)`.

`cancel()` removes queued utterances and stops current speech, so repeated fast clicks restart the word instead of layering or queueing pronunciations. Calling from the button click also keeps playback directly tied to a user gesture.

### Voice, rate, pitch, and volume

- Keep `lang = 'en-US'` explicit so the browser requests an American English voice rather than inheriting page or system language.
- Keep `rate = 0.95`, which the application already uses and is reasonable for learning pronunciation. `1` is also defensible; this is a product preference, not a technical requirement.
- Do not explicitly set pitch or volume. Their defaults are appropriate, preserve user/device expectations, and avoid implying consistent output across engines.
- Do not select a named voice in the initial implementation. Voice names and installed voices differ across browser, OS, language packs, and user settings. The user agent should choose the best available voice for `en-US`.
- Because the design does not call `getVoices()` or select a voice, asynchronous population of the voice list and the `voiceschanged` event are not concerns. Adding voice-loading state would be unnecessary complexity.

The exact accent, quality, timing, and even whether synthesis uses an implementation-managed network service are browser/device concerns. The application makes no pronunciation API call and sends no `AudioUrl`, but it cannot promise identical or necessarily offline synthesis on every platform.

### Cleanup and errors

`WordLookupComponent` should implement `OnDestroy` and call `speechSynthesis.cancel()` when supported. It should also cancel when changing/clearing the displayed word if audible speech continuing after navigation within the component would be confusing. Since `cancel()` acts on the window-wide speech queue, this recommendation assumes VocabularyApp remains the only speech-synthesis user; if another feature later speaks concurrently, ownership should be coordinated in a service.

Synchronous access/construction/speak errors should be caught so lookup remains usable. A non-blocking toast can explain that pronunciation is unavailable. An `utterance.onerror` handler may also provide feedback, but it should ignore cancellation/interruption caused intentionally by another click or cleanup; otherwise normal rapid clicks could produce false error messages. Browser error-event behavior is not perfectly uniform, so it must not affect word state.

No `audioPlaybackFailed` latch is needed for the normal design. A transient synthesis error should not permanently disable pronunciation for the displayed word. The user can retry, and unsupported capability is handled separately.

### Accessibility

- Keep a native `<button>` and visible “Play”/“Pronounce” text.
- Add `type="button"`.
- Provide an `aria-label` containing the action and preferably the word, for example `Pronounce test`.
- Ensure unsupported state is represented by the native `disabled` attribute and explanatory title/adjacent text if the button remains visible.
- Do not auto-play on lookup; speech must remain user initiated.
- Do not move focus or modify definitions when pronunciation runs.
- A live “speaking” state is not necessary for this small feature unless product testing shows a need. Avoid toggling the button label rapidly because single-word utterances are short.

## 6. Recommended Architecture

Put the logic directly in `WordLookupComponent`.

This is the simplest fit because:

- only one component and one control currently pronounce words;
- the component already owns both the displayed word and pronunciation behavior;
- the implementation is a small guarded browser call;
- no external dependency or cross-component state is required;
- Jasmine can stub the window APIs at the component boundary;
- the Angular project has no SSR target, so a small runtime guard is sufficient.

A reusable `SpeechService` would be justified later if quiz, vocabulary lists, accessibility narration, configurable voices, centralized status, or multiple components need synthesis. Introducing it now would not materially improve the feature.

The public method should accept the word to pronounce (for example, `pronounceWord(word?: string | null)`) or read `currentWord.word`. Passing the rendered word from the template makes the input explicit; reading component state makes the template shorter. Either is sound. Passing `currentWord.word` is recommended because it is directly testable and avoids accidentally speaking stale state.

## 7. Proposed User Behavior

```text
Word is displayed
  -> pronunciation control is shown based on browser capability, not AudioUrl
  -> user clicks the control
  -> Angular trims and validates the displayed word
  -> Angular cancels any queued/current synthesis
  -> Angular creates an en-US utterance at rate 0.95
  -> browser/device pronounces the word
```

- A null `AudioUrl` has no effect on the control.
- No audio endpoint is called by the click.
- Fast repeated clicks restart rather than overlap/queue speech.
- Empty text does nothing safely.
- Unsupported browsers show a disabled explanatory control (recommended) or no control if product explicitly chooses hidden behavior.
- A synthesis failure is nonfatal and does not alter lookup, definitions, or vocabulary state.

## 8. Testing Strategy

### Existing tests

`VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts` currently tests:

- showing **Play** only when an audio URL exists;
- constructing `Audio` with the expected URL and calling `play()`;
- URL playback rejection, error toast, failure state, and disabled control;
- resetting that failure on a new lookup without audio;
- general lookup/vocabulary behaviors unrelated to audio.

The first four tests encode behavior that should be replaced. The unrelated tests should remain and continue to pass.

### Exact recommended component tests

1. With a displayed nonempty word and supported APIs, the pronunciation button is present even when `audioUrl` is null/omitted.
2. Clicking the button calls `speechSynthesis.cancel()` before `speechSynthesis.speak()`.
3. The object passed to `speak()` contains the exact trimmed displayed word.
4. The utterance has `lang === 'en-US'`.
5. The utterance has the selected rate (`0.95` if the existing choice is retained); pitch and volume remain defaults.
6. Two rapid invocations cancel before each speak and do not depend on an audio URL.
7. If `speechSynthesis` or `SpeechSynthesisUtterance` is unavailable, the component does not throw, does not construct/speak an utterance, and the template follows the chosen disabled/hidden policy.
8. Null, undefined, blank, or whitespace-only words do not call `cancel()`, construct an utterance, or call `speak()`.
9. A synchronous constructor/speak error is contained and, if implemented, displays the expected non-blocking message without changing `currentWord`.
10. `ngOnDestroy` cancels active/queued speech when supported and remains safe when unsupported.
11. Existing lookup response mapping, searching, adding to vocabulary, vocabulary details, favorites, and preferred-definition tests remain unaffected.

### Jasmine stubbing approach

Do not depend on Chrome's real speech queue or installed voices in unit tests. Save the original property descriptors for `window.speechSynthesis` and `window.SpeechSynthesisUtterance`, replace them with configurable fakes for each test, and restore the descriptors in `afterEach`.

The speech controller fake needs `cancel` and `speak` Jasmine spies. The utterance constructor fake should return a mutable object with at least `text`, `lang`, `rate`, `pitch`, `volume`, and optional `onerror`. Capture constructed instances or inspect the argument passed to `speak`. Use call order matchers (`cancel` before `speak`) or a small shared order array. For unsupported cases, redefine one or both window properties as `undefined`. This isolates tests from read-only browser typings and machine-specific voice availability.

If direct `Object.defineProperty` replacement proves awkward in the configured Chrome/Karma version, place two tiny protected component accessors around the globals and spy on those accessors. Do not add a production service solely to simplify mocks.

### Repository commands

Run from `VocabularyApp.UI`:

```powershell
npx ng test --watch=false --browsers=ChromeHeadless --include='src/app/components/word-lookup/word-lookup.component.spec.ts'
npm test -- --watch=false --browsers=ChromeHeadless
npm run build
```

The first is the focused component suite. `package.json` defines `test` as `ng test` and `build` as `ng build`; `angular.json` makes the default build configuration production, so `npm run build` is the repository's production Angular build. The full suite has documented pre-existing failures in the earlier audio implementation record; implementation validation should distinguish unchanged baseline failures from new regressions rather than weakening tests.

## 9. Regression Risks

| Risk | Mitigation |
|---|---|
| Button disappears when `AudioUrl` is null | Remove the `*ngIf="currentWord.audioUrl"` dependency and test a word with no URL. |
| Click still invokes provider audio | Change the click binding and remove/retire `playAudio`, `new Audio`, URL normalization, and fallback-only flow in the component. Test that speech is invoked directly. |
| Browser globals break Karma or non-browser execution | Guard `window` and both required APIs; fully stub and restore globals in tests. |
| Multiple utterances overlap or queue | Call `cancel()` immediately before each `speak()`. |
| Rapid-click cancellation reports a false failure | Ignore intentional cancel/interrupted utterance errors or omit asynchronous error UI in the minimal first version. |
| Speech continues after leaving the component or changing words | Cancel on destruction and, if implemented, before clearing/replacing the current word. |
| Unsupported browsers present a dead control | Detect support once/through a getter and use a native disabled or hidden state with explanatory accessible text. |
| Voice differs by browser/OS | Specify language, not a voice name; document that device voice determines output. |
| Browser chooses no suitable en-US voice | Treat as a nonfatal synthesis failure; do not fall back to URL/provider logic unless product explicitly retains a second mode. |
| Accessibility regresses | Retain native button, visible text, keyboard behavior, explicit type, and meaningful accessible name; never auto-play. |
| Word lookup/add/favorite/preferred-definition behavior changes | Limit production edits to the pronunciation block and preserve existing unrelated component tests. |
| DTO/database compatibility breaks | Keep `AudioUrl` properties, mappings, entity, migrations, and backend tests unchanged. |
| Existing backend still performs optional Merriam-Webster work that UI no longer uses | Accept for compatibility in this scoped restoration; measure and decide decommissioning separately. |

## 10. Backend Impact

No backend change is required to make pronunciation work through browser speech synthesis. The click path is entirely within Angular and the browser.

The current backend will continue attempting Merriam-Webster audio resolution during word lookup and will continue persisting/returning `AudioUrl`. That work becomes unnecessary for this UI feature, but removing it in the same change would broaden scope, alter observable lookup behavior and latency, invalidate backend tests, require configuration/deployment review, and potentially affect unknown consumers. There is no compelling reason to combine those concerns with restoration of the button.

Backend retirement can later consider disabling new audio resolution while temporarily retaining the nullable contract/column, followed by explicit deprecation and eventual schema cleanup. Existing migrations must never be rewritten to achieve that.

## 11. Compatibility Considerations

- Preserve `WordDto.AudioUrl`, `UserVocabularyItemDto.AudioUrl`, Angular `audioUrl` model fields, `Word.AudioUrl`, and the database column in the initial release.
- Preserve JSON response shape so older deployed frontends or other consumers are not broken.
- Do not clear historical `AudioUrl` values as part of a frontend behavior change.
- Browser pronunciation is generated speech, not provider-recorded dictionary audio; exact pronunciation and voice will vary by installed/system voices.
- `en-US` intentionally chooses an American-English language preference. If VocabularyApp later supports dialect selection, it should be a separate user preference.
- The current Angular build is client-rendered. Although `@angular/platform-server` appears transitively in the lock file, there is no server entry point, SSR build target, or `provideServerRendering` configuration. The runtime guard is still appropriate future-proofing.
- The application does not need a new package, permission prompt, backend secret, or CSP media-source rule for native speech synthesis.

## 12. Recommended Implementation Scope

### In scope

- Change the existing word-header control to be independent of `AudioUrl`.
- Replace URL playback with direct `SpeechSynthesisUtterance` playback of the displayed word.
- Add support and empty-input guards.
- Cancel queued/current speech before each new pronunciation.
- Set `lang = 'en-US'` and retain `rate = 0.95` unless product chooses the default rate.
- Add safe component destruction cleanup.
- Add/replace focused Angular tests and run the full Angular suite and production build.
- Keep the existing button styling unless a small accessibility adjustment is needed.

### Out of scope

- Backend, DTO, entity, migration, configuration, or provider changes.
- Dropping or backfilling `AudioUrl`.
- Removing Merriam-Webster integration.
- Adding voice selection, voice-loading UI, pronunciation caching, recorded audio, or a new Angular service.
- Changing word lookup, add-to-vocabulary, favorites, quiz-definition selection, or quiz behavior.

## 13. Open Questions / Decisions

No technical blocker remains. The following are implementation-time product choices with recommended defaults:

1. **Unsupported browser presentation:** visible disabled control with an explanatory title/accessible label (recommended) versus hidden control.
2. **Label:** retain `🔊 Play` for minimal UI change (recommended) or use `🔊 Pronounce` for greater semantic clarity.
3. **Rate:** retain the existing `0.95` (recommended) or use the browser default `1.0`.
4. **Backend follow-up:** leave the now-unused Merriam-Webster/`AudioUrl` pipeline intact for compatibility in this change (recommended), then separately decide whether provider calls should be disabled or the contract formally deprecated.

These choices do not prevent planning. If no different product direction is supplied, the recommended defaults are sufficient.

## 14. Readiness for Implementation Planning

The current control, owning component, data flow, affected files, browser behavior, test seams, backend boundary, and regression risks are identified. The feature can be implemented as a focused Angular change without changing application API or database contracts.

**READY FOR IMPLEMENTATION PLANNING**
