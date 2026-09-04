# Restore Pronunciation Audio — Implementation Plan

## 1. Executive Summary

Restore pronunciation as a frontend-only capability in `WordLookupComponent`. The existing word-header button will no longer depend on `currentWord.audioUrl` and will no longer create an `HTMLAudioElement`. Instead, it will pronounce the displayed word directly through `window.speechSynthesis` and `window.SpeechSynthesisUtterance`.

The implementation will validate the word and browser capability, cancel queued/current speech, create an utterance with `lang = 'en-US'` and `rate = 0.95`, and speak it. Unsupported browsers will retain the existing control in a disabled, explanatory state. The component will cancel speech when it is destroyed and when the displayed word is abandoned for a new lookup/input.

This is intentionally an Angular-only change. All `AudioUrl` frontend models, API contracts, mappings, database fields, migrations, and current backend provider behavior remain intact for compatibility.

## 2. Scope

### In scope

- Replace the current URL-first pronunciation path in `WordLookupComponent` with browser speech synthesis.
- Make the existing pronunciation control independent of `AudioUrl`.
- Guard browser globals and empty/blank words.
- Cancel current/queued speech before each pronunciation.
- Cancel component-owned speech during relevant word transitions and component destruction.
- Preserve the existing icon, placement, and utility-class styling.
- Add an explicit button type and word-specific accessible label.
- Replace obsolete URL-audio component tests with deterministic speech-synthesis tests.
- Validate the focused Angular suite, full Angular suite, and production Angular build.

### Out of scope

- Changing WordsAPI or Merriam-Webster integration.
- Removing or deprecating `AudioUrl` contracts or stored values.
- Backend, DTO, entity, configuration, or database changes.
- Migrations or production data updates.
- Voice selection, voice menus, asynchronous voice loading, pitch, volume, or pronunciation caching.
- A new Angular speech service.
- Changes to lookup, vocabulary, favorites, definitions, or quiz behavior.

## 3. Current State

The pronunciation control is in the word header of `word-lookup.component.html`. It currently:

- renders only under `*ngIf="currentWord.audioUrl"`;
- passes the URL to `(click)="playAudio(currentWord.audioUrl!)"`;
- disables itself after `audioPlaybackFailed` becomes true;
- uses `new Audio(...)` as the primary playback mechanism.

`WordLookupComponent.playAudio` normalizes a provider URL, stops the prior `HTMLAudioElement`, creates a new element, and calls `play()`. Its private `playSpeechSynthesisFallback` already cancels speech, creates an utterance for `currentWord.word`, applies `en-US` and rate `0.95`, and speaks it—but it can only run after URL playback fails. A null `AudioUrl` hides the control, so the fallback is unreachable for the affected WordsAPI result.

The component implements `OnInit` only. It has no destruction cleanup. Existing component tests explicitly enforce the legacy URL-gated and `new Audio` behavior and must be replaced.

## 4. Files to Modify

| File | Planned changes |
|---|---|
| `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` | Implement direct word-based speech; add capability and cancellation helpers; implement `OnDestroy`; remove obsolete URL-audio state and methods; cancel speech during word transitions. |
| `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` | Remove the `audioUrl` gate and URL argument; bind to word-based speech; add supported/unsupported disabled state, explicit type, accessible label, and explanatory title while retaining icon/placement/classes. |
| `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts` | Replace four legacy audio tests with speech API, `AudioUrl` independence, guard, ordering, and cleanup coverage; retain unrelated behavioral tests. |

`word-lookup.component.scss` is not expected to change. The current Tailwind utility classes already include disabled opacity/cursor treatment, and the existing layout can be preserved.

## 5. Files Explicitly Out of Scope

### Angular contracts and infrastructure

- `VocabularyApp.UI/src/app/models/word-lookup.model.ts`
- `VocabularyApp.UI/src/app/services/api.service.ts`
- `VocabularyApp.UI/package.json`
- `VocabularyApp.UI/angular.json`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss`

In particular, keep both `WordLookupResult.audioUrl` and `VocabularyItem.audioUrl` unchanged.

### Backend and data layer

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
- all files under `VocabularyApp.Data/Migrations`

Keep `Word.AudioUrl`, `WordDto.AudioUrl`, `UserVocabularyItemDto.AudioUrl`, provider resolution, service mappings, JSON shape, and the nullable database column unchanged.

### Backend tests

- `VocabularyApp.WebApi.Tests/Services/MerriamWebsterPronunciationServiceTests.cs`
- `VocabularyApp.WebApi.Tests/Integration/DictionaryLookupApiTests.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/ControllablePronunciationAudioService.cs`
- `VocabularyApp.WebApi.Tests/Infrastructure/IntegrationTestSeeder.cs`

No backend tests need modification because backend behavior is not changing.

## 6. TypeScript Changes

### Lifecycle declaration

Update the Angular core import to include `OnDestroy` and change the class declaration to:

```typescript
export class WordLookupComponent implements OnInit, OnDestroy
```

Add `ngOnDestroy(): void` and have it safely cancel speech. This prevents pronunciation continuing after navigation away from the component and provides meaningful cleanup without subscriptions or extra lifecycle state.

### Browser support helper

Add a component property/getter with a clear name such as `isSpeechSynthesisSupported`. It must return true only when all required browser pieces exist:

```typescript
typeof window !== 'undefined' &&
'speechSynthesis' in window &&
!!window.speechSynthesis &&
typeof window.SpeechSynthesisUtterance === 'function'
```

The getter must not read browser globals before the `typeof window` guard. Although this application has no SSR target, this prevents tests or future non-browser rendering from failing at module/component evaluation.

### Replace the existing pronunciation methods

Replace public `playAudio(audioUrl: string)` and private `playSpeechSynthesisFallback()` with one public method:

```typescript
speakWord(word?: string | null): void
```

Use this logic in order:

1. Normalize the input with `word?.trim()`.
2. Return immediately when the normalized word is empty.
3. Return safely when speech synthesis or the utterance constructor is unsupported.
4. Inside a `try/catch`, capture `window.speechSynthesis`.
5. Call `speechSynthesis.cancel()` before constructing/starting the new request.
6. Construct `new window.SpeechSynthesisUtterance(normalizedWord)`.
7. Set `utterance.lang = 'en-US'`.
8. Set `utterance.rate = 0.95` to retain the existing learning-friendly setting.
9. Do not set `voice`, `pitch`, or `volume`.
10. Call `speechSynthesis.speak(utterance)`.
11. If synchronous setup or invocation fails, contain the exception and show the existing non-blocking `Pronunciation audio is unavailable.` toast. Do not clear or mutate the displayed word.

Do not add `getVoices()` or `voiceschanged` handling. Because no named voice is selected, the browser can select its best available voice for `en-US` and asynchronous voice-list loading is irrelevant.

An asynchronous `utterance.onerror` handler is optional and not required for the minimal implementation. If added, it must ignore intentional `canceled`/`interrupted` errors created by rapid clicks or cleanup and must never disable the control permanently. The preferred initial implementation is the simpler synchronous guard/`try-catch`; browser event behavior is inconsistent and the user can retry.

### Cancellation helper and transition points

Add one private helper, for example:

```typescript
private cancelSpeech(): void
```

It should perform the same support-safe lookup and call `window.speechSynthesis.cancel()` inside a `try/catch`. Cancellation failure must be nonfatal and should not generate a user-facing toast during input, lookup transitions, or destruction.

Call this helper:

- from `ngOnDestroy`;
- when `onSearchInput` clears the displayed result;
- at the start of `viewExistingWord`;
- at the start of `searchNewWord`.

`speakWord` should call the captured synthesizer's `cancel()` directly immediately before `speak()` so call ordering is unambiguous. The transition calls prevent an old word continuing while the next lookup loads. Duplicate safe cancellations caused by overlapping transition paths are acceptable but should be avoided where the existing call graph makes that easy.

### Remove obsolete component-only URL playback code

Remove from `WordLookupComponent`:

- `audioPlaybackFailed`;
- `pronunciationAudio: HTMLAudioElement | null`;
- resets of `audioPlaybackFailed` in lookup paths;
- `playAudio`;
- `handleAudioPlaybackFailure`;
- `normalizeAudioUrl`;
- `playSpeechSynthesisFallback`.

This removes the component's runtime dependency on `new Audio(...)`. It does **not** remove or stop mapping `audioUrl` fields; those contracts remain for compatibility. Keep `ToastService`, which is used elsewhere throughout the component.

## 7. Template Changes

Modify only the existing word-header pronunciation button.

### Rendering

Remove:

```html
*ngIf="currentWord.audioUrl"
```

The button is already nested inside a block that requires `currentWord`, so it does not need a second valid-word visibility check. A successfully mapped word is the availability condition; `speakWord` remains the final empty/whitespace guard.

### Click binding

Replace:

```html
(click)="playAudio(currentWord.audioUrl!)"
```

with:

```html
(click)="speakWord(currentWord.word)"
```

No URL is passed or dereferenced, proving that a null `AudioUrl` cannot block pronunciation.

### Supported and unsupported states

Keep the control visible and bind its native disabled state to browser capability:

```html
[disabled]="!isSpeechSynthesisSupported"
```

Use a conditional title:

```html
[title]="isSpeechSynthesisSupported
  ? 'Play pronunciation'
  : 'Pronunciation is not supported by this browser'"
```

This preserves layout and explains graceful degradation. Keep the existing disabled utility classes. Do not use `*ngIf` for browser support because a visible disabled control communicates why the feature is unavailable.

### Preserved presentation

- Keep the existing `🔊 Play` visible content.
- Keep its current location beside the displayed word.
- Keep existing background, hover, text, radius, spacing, and transition classes.
- Add `type="button"`.
- Add `[attr.aria-label]="'Pronounce ' + currentWord.word"`.

No custom keyboard handler is needed: a native enabled button already activates with Enter and Space. Native `disabled` correctly removes an unsupported control from activation.

## 8. Browser Support and Graceful Degradation

- Test both `window.speechSynthesis` and `window.SpeechSynthesisUtterance` at runtime.
- Unsupported browsers show the speaker control disabled with an explanatory title; clicking cannot invoke the handler through the UI.
- The handler repeats its support check so direct calls, tests, or future template changes remain safe.
- An empty or blank word returns before cancellation or utterance construction.
- A new valid pronunciation cancels the global speech queue before speaking, preventing overlapping/queued words on repeated clicks.
- Leaving the component or initiating another word cancels speech without changing lookup state.
- Browser/OS voice differences are accepted. `lang` requests American English, but no voice name or voice-loading workflow is imposed.
- The feature makes no application pronunciation HTTP request. The browser implementation may internally use device/platform-managed synthesis services; that is outside application control.
- A runtime failure remains nonfatal and retryable. Do not restore URL audio as a fallback in this implementation.

## 9. Accessibility

- Retain the semantic native `<button>`.
- Add `type="button"` to prevent accidental form submission if the markup is later moved into a form.
- Add the dynamic accessible name `Pronounce {word}`.
- Retain visible icon and text rather than using an icon-only control.
- Provide a meaningful unsupported title and native disabled state.
- Preserve keyboard activation through normal button semantics; add no custom key listeners.
- Keep pronunciation user-initiated. Do not auto-speak lookup results.
- Do not move focus, alter definitions, or use a disruptive modal/error flow.
- Do not add a live speaking announcement for this short single-word action unless later usability testing demonstrates a need.

## 10. Unit Test Changes

Modify only `word-lookup.component.spec.ts`.

### Remove or replace legacy tests

Replace these current expectations because they intentionally describe the behavior being retired:

1. `should show Play only when an audio URL is present`
2. `should play the expected audio URL`
3. `should handle playback failure without crashing and disable the current control`
4. `should reset playback failure for a new dictionary lookup without audio`

Do not remove unrelated lookup/vocabulary tests.

### Required new tests

1. **AudioUrl-independent control**
   - Set `currentWord` to a valid word with `audioUrl` omitted or explicitly null in an API response.
   - Enable the fake browser API and run change detection.
   - Assert the pronunciation button is present and enabled.

2. **Pronounces the displayed word from a click**
   - Render `currentWord.word = 'test'`.
   - Click the word-header pronunciation button.
   - Assert the fake constructor receives `test` and `speechSynthesis.speak` is called once with the constructed utterance.

3. **Trims and configures the utterance**
   - Call `speakWord('  example  ')`.
   - Assert utterance text is `example`, `lang` is `en-US`, and `rate` is `0.95`.
   - Assert the implementation did not choose a named voice or explicitly override pitch/volume.

4. **Cancels before speaking**
   - Record fake `cancel` and `speak` calls in a shared order array or use Jasmine invocation order.
   - Assert `cancel` occurs before `speak` for each pronunciation.

5. **Repeated fast requests do not queue**
   - Invoke `speakWord` twice.
   - Assert two cancellations and two speaks, with each cancellation preceding its associated speak.

6. **Unsupported speech controller**
   - Make `window.speechSynthesis` unavailable while retaining/restoring the original descriptor afterward.
   - Assert `speakWord` does not throw or create an utterance.
   - Assert the rendered control is disabled and its title explains unsupported pronunciation.

7. **Unsupported utterance constructor**
   - Provide the speech controller but make `window.SpeechSynthesisUtterance` unavailable.
   - Assert no cancellation, construction, or speak call and an unsupported UI state.

8. **Empty/missing input**
   - Exercise `undefined`, `null`, `''`, and whitespace.
   - Assert no cancellation, utterance construction, or speak call.

9. **Synchronous browser failure is contained**
   - Configure the fake constructor or `speak` method to throw.
   - Assert no exception escapes, the displayed word remains intact, and the expected toast is emitted if synchronous failure feedback is implemented.

10. **Component destruction cleanup**
    - With support enabled, call `ngOnDestroy()` and assert `cancel()`.
    - With support disabled, assert destruction does not throw.

11. **New search/input cancels prior speech**
    - Invoke the relevant transition method and assert cancellation while preserving the method's existing lookup/input behavior.

12. **Existing behavior regression coverage**
    - Keep the current tests for component creation, lookup mapping/clearing, vocabulary filtering/browsing, duplicate add success, and preferred-definition updates.
    - Preserve their request assertions so pronunciation cannot introduce a new HTTP request.

Tests must never invoke real speech or depend on installed voices.

## 11. Browser API Test Mocking Strategy

Use local Jasmine fakes in `word-lookup.component.spec.ts`.

### Setup

Before replacing browser properties, save their original property descriptors:

```typescript
const speechSynthesisDescriptor = Object.getOwnPropertyDescriptor(window, 'speechSynthesis');
const utteranceDescriptor = Object.getOwnPropertyDescriptor(window, 'SpeechSynthesisUtterance');
```

Install configurable replacements with `Object.defineProperty`:

- a `speechSynthesis` fake containing `cancel` and `speak` Jasmine spies;
- a constructable `SpeechSynthesisUtterance` fake that creates a mutable utterance record initialized with the supplied text.

The utterance record should expose only what the tests need: `text`, `lang`, `rate`, default `pitch`, default `volume`, `voice`, and optionally `onerror`. Capture created records in an array or inspect the object passed to `speak`.

For ordering, either compare Jasmine spies' `invocationOrder` values or push `cancel`/`speak` into a shared array. The latter is explicit and avoids coupling to matcher availability.

### Teardown

Restore the exact original descriptors in `afterEach`. If a property did not originally have an own descriptor, delete the test override so prototype behavior is visible again. Always cancel/replace any fake state between tests. This prevents a missing-API test from contaminating later component tests.

If the browser marks either property non-configurable in the actual Karma environment, use a narrow component test seam (protected getter/factory methods around the two browser globals) and spy on those methods through a typed test subclass or `component as any`. Do not introduce an application-wide service solely for mocking.

Do not call `getVoices()`, wait for `voiceschanged`, or perform audible playback in unit tests.

## 12. Regression Protection

| Regression | Protection |
|---|---|
| Control hidden when `AudioUrl` is null | Remove the URL `*ngIf`; render/test a word without audio. |
| Old provider audio still plays | Remove component `new Audio`, URL normalization, and URL click argument; verify only synthesis spies are called. |
| Multiple words overlap | Assert `cancel` precedes every `speak`. |
| Old word continues during a new search/navigation | Cancel during input/lookup transitions and destruction. |
| Browser globals throw in tests or future non-browser rendering | Guard both globals and restore all test descriptors. |
| Pronunciation triggers WordsAPI/application HTTP traffic | Keep handler free of `ApiService`; in click test assert no unexpected request through `HttpTestingController.verify()`. |
| API contracts change | Do not edit Angular models, DTOs, mappings, entities, or serializers. |
| Database schema/data change | Do not create, edit, or run migrations; do not clear `AudioUrl`. |
| Normal lookup changes | Limit edits to speech cancellation and pronunciation blocks; retain existing HTTP lookup tests. |
| Add-to-vocabulary changes | Retain duplicate-add and related existing tests unchanged. |
| Unsupported state is inaccessible | Use native disabled state plus explanatory title and accessible name. |
| Styling changes unintentionally | Retain current button content, location, classes, and SCSS. |
| New build warning/error | Run focused/full tests and the default production build; report baseline warnings separately. |

## 13. Implementation Sequence

1. Establish the focused test baseline using the existing component spec before editing.
2. Add reusable browser fakes and descriptor restoration to the component spec.
3. Replace the four URL-audio tests with initially failing tests for URL independence, speech invocation/configuration, ordering, unsupported APIs, empty input, and cleanup.
4. Update the component import/class declaration for `OnDestroy`.
5. Add the support getter and safe cancellation helper.
6. Replace `playAudio`/fallback code with `speakWord` and remove obsolete URL-audio state/reset code.
7. Add cancellation at destruction and the existing word-clearing/lookup transition points.
8. Update only the existing word-header button binding, condition, disabled/title attributes, button type, and accessible name.
9. Run the focused suite and correct only feature-related failures.
10. Run the full Angular suite and distinguish any known baseline failures from new failures.
11. Run the production Angular build and compare warnings to the pre-change baseline.
12. Review the final diff to confirm there are no backend, model, SCSS, package, migration, generated, or unrelated component changes.

## 14. Validation Commands

Run commands from `VocabularyApp.UI`.

### Focused component tests

```powershell
npx ng test --watch=false --browsers=ChromeHeadless --include='src/app/components/word-lookup/word-lookup.component.spec.ts'
```

Angular CLI 18's Karma builder supports the `--include` filter, and the repository uses that builder.

### Full Angular test suite

```powershell
npm test -- --watch=false --browsers=ChromeHeadless
```

### Production Angular build

```powershell
npm run build
```

`package.json` maps `build` to `ng build`, and `angular.json` sets the build target's default configuration to `production`.

The earlier audio implementation record documents a historical full-suite baseline of 23 passing and 7 unrelated failing tests involving missing providers and a stale app-title expectation. That record is context, not permission to accept failures blindly. Capture the current pre-change baseline during implementation; all focused pronunciation tests must pass, no new full-suite failure may be introduced, and unrelated legacy failures must be reported separately without weakening or skipping them. The prior build also documented an existing component-style budget warning; compare current output rather than assuming that historical warning still exists.

No migration or backend command is part of this implementation.

## 15. Acceptance Criteria

- [ ] A successfully displayed word has the existing speaker control regardless of whether `audioUrl` is populated, null, or omitted.
- [ ] Clicking the control constructs a browser `SpeechSynthesisUtterance` containing the trimmed displayed word.
- [ ] The utterance uses `lang = 'en-US'` and `rate = 0.95`.
- [ ] Voice, pitch, and volume are not explicitly selected/overridden.
- [ ] `speechSynthesis.cancel()` is called before each `speechSynthesis.speak()`.
- [ ] Repeated clicks do not queue or overlap multiple pronunciations.
- [ ] Starting another word/input flow and destroying the component stop prior speech safely.
- [ ] Empty, missing, or whitespace-only input creates no utterance and initiates no speech.
- [ ] Unsupported browsers do not throw and show the pronunciation control disabled with explanatory text.
- [ ] The button remains a native keyboard-operable button with visible icon/text, `type="button"`, and `Pronounce {word}` accessible name.
- [ ] Clicking pronunciation makes no Angular/API pronunciation request and does not require an `AudioUrl`.
- [ ] No backend code, contract, configuration, or database schema/data is changed.
- [ ] Existing Angular `audioUrl` model fields and backend `AudioUrl` contracts/mappings remain intact.
- [ ] No new Angular dependency or speech service is introduced.
- [ ] Focused `WordLookupComponent` tests pass.
- [ ] The full Angular suite has no new failures relative to the freshly captured baseline.
- [ ] The production Angular build passes with no new warning or error attributable to this feature.
- [ ] Existing lookup, errors, displayed definitions, vocabulary add, favorites, and preferred-definition behavior remain unchanged.

## 16. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Browser has a partial Web Speech implementation | Require both controller and constructor; contain synchronous failures. |
| Pronunciation differs across OS/browser | Set language only and allow the user agent to select the voice. |
| `cancel()` affects another future speech feature | Accept while this is the only speech user; centralize later if reuse actually appears. |
| A cancellation error is mistaken for playback failure | Do not add asynchronous error handling initially, or explicitly ignore canceled/interrupted events. |
| Component getter runs frequently during change detection | The capability check is a small constant-time property check with no side effect. |
| Browser test properties are read-only/non-configurable | Save/restore descriptors; fall back to narrow component getter/factory seams if required. |
| Speech continues after the displayed word changes | Cancel at current word transition entry points and destruction. |
| Removing URL component logic is confused with removing contracts | Limit removal to dead component playback state/methods; explicitly inspect final diff for retained mappings/models/backend. |
| Existing full-suite failures obscure regressions | Capture a fresh baseline, require the focused suite to be green, and compare failure identities exactly. |

## 17. Rollback Considerations

The change is isolated and does not alter persisted data or server contracts. Rollback consists of reverting the three Angular files to restore URL-gated `new Audio` playback and its prior tests.

Because `AudioUrl` fields, backend provider resolution, database data, and API response shape remain untouched, rollback requires:

- no database rollback;
- no migration;
- no data restoration;
- no backend configuration change;
- no API client coordination.

Do not delete historical audio values during implementation. Their preservation is what keeps rollback low risk.

## 18. Implementation Readiness

The implementation boundary, exact component/template changes, unsupported-browser behavior, lifecycle behavior, test substitutions, browser mocking approach, commands, acceptance criteria, and rollback path are defined. No unresolved technical or product blocker prevents implementation; the recommended defaults settle unsupported UI, label, and rate choices.

READY TO IMPLEMENT
