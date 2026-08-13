# R17 — Accessibility, UI Consistency, Test Reliability, and Documentation Analysis

## 1. Executive Summary

R17 remains a **moderate-to-substantial frontend remediation** on the current branch (`fix/r17-fix-dead-ui-controls`). Static inspection confirms that most original UI and accessibility findings are still present: the visible **Save for Later** button is dead; dashboard navigation is implemented with clickable `div` elements; autocomplete is mouse-only and lacks combobox/listbox semantics; the preferred-definition overlay has no dialog semantics or focus management; and the toast renderer is page-scoped and has no live region. Documentation and HTTP examples are materially stale.

Two original findings need qualification:

- The source files inspected are valid UTF-8 and contain valid emoji and symbols. Mojibake seen when files are read without explicitly selecting UTF-8 is a terminal/decoder artifact, not confirmed repository corruption. The remaining problem is inconsistent, often decorative emoji and incomplete text alternatives, not damaged source bytes.
- Whether the current production build still emits an SCSS budget warning and whether the test process hangs cannot be established without the prohibited build/test runs. Static evidence makes both historical reports plausible: `anyComponentStyle` warns above 2 kB while `word-lookup.component.scss` is 3,750 source bytes, and `npm test` maps to `ng test` without a non-watch option.

The highest implementation risks are autocomplete interaction, custom-dialog focus behavior, and working inside the still-monolithic `WordLookupComponent`. Contrary to the requested assumption that R14–R16 may have changed the landscape, current source shows their definitions of done have **not** landed on this branch: `word-lookup.component.ts` remains 780 lines, its template remains 443 lines, components still construct endpoint strings through a generic API service, and vocabulary browsing still requests `pageSize=1000`.

Important additional findings include another non-semantic clickable `div` in the vocabulary list, unlabeled search fields, a toast close button without an accessible name, status/error messages without live announcement, a stale generated `AppComponent` spec that contradicts the current shell, and no frontend-test CI workflow.

No tests or builds were run for this analysis.

## 2. Original R17 Scope

The Plan of Action identifies dead or misleading controls, non-semantic dashboard cards, incomplete autocomplete and dialog accessibility, a page-local toast host, apparently damaged emoji, stale dashboard/documentation concepts, a component-style budget warning, and an Angular test process that does not reliably finish. Its intended outcome is a keyboard-usable application with appropriate semantics, consistent and announced feedback, clean or deliberately justified build budgets, a terminating test command, and documentation that matches the application (`Docs/Vocabulary Builder — Plan of Action.md`, R17, lines 935–993).

R17 should remediate existing workflows rather than introduce analytics, preferences, administration, a visual redesign, a new icon framework, or a new state-management architecture.

## 3. Current Architecture Relevant to R17

- `VocabularyApp.UI/src/app/app.routes.ts` defines `/login`, `/signup`, guarded `/dashboard`, guarded `/vocabulary`, guarded `/quiz`, a root redirect to `/dashboard`, and a wildcard redirect to `/login`.
- `AppComponent` is a minimal standalone shell. `app.component.html` contains only `<router-outlet />`; there is no shared header or toast host.
- `DashboardComponent` owns four hard-coded card concepts and performs imperative navigation through `Router.navigate`.
- `WordLookupComponent` still owns lookup, remote suggestions, dictionary rendering, adding words, collection loading/filtering, favorites, audio, preferred-definition overlay, highlighting, routing, and toast rendering. It is 780 TS lines with a 443-line template and a 186-line component stylesheet.
- `ToastService` is root-provided and stores notifications in a `BehaviorSubject`, but presentation is embedded at the top of `word-lookup.component.html`.
- `QuizComponent` is a separate route-level component and uses native buttons and labeled setup fields, but it does not use the shared toast service.
- `ApiService` remains a generic HTTP wrapper, while feature components assemble endpoint strings and parse `any` response/error shapes.
- Karma/Jasmine remains the Angular test stack through `@angular-devkit/build-angular:karma` in `angular.json`.

### R14–R16 state

The current architecture does not show the planned R14 decomposition, R15 typed feature clients/interceptor work, or R16 bounded server-side browser query. Direct evidence includes `WordLookupComponent`'s unchanged 780/443 size, calls such as `apiService.get<any>(...)` in `searchUserVocabulary`, `searchNewWord`, `loadVocabularyPage`, and `openDefinitionEditor`, and `loadVocabularyPage()` requesting `/words/vocabulary?page=${page}&pageSize=1000`. R17 therefore must either make careful contained changes in the monolith or coordinate with those prerequisites; it cannot assume extracted search, dialog, or toast components exist.

## 4. Finding-by-Finding Verification

| Original concern | Status | Current evidence | Recommendation |
|---|---|---|---|
| Save for Later has no behavior | **Still Present** | `word-lookup.component.html` lines 147–150 renders an enabled button with no event binding; no corresponding method, route, DTO, service, or endpoint exists | Remove it. Do not invent a second saved state inside R17 |
| Dashboard cards are clickable `div`s | **Still Present** | `dashboard.component.html` lines 17–42 uses an `*ngFor` `div` with `(click)`; `onCardClick()` imperatively navigates only the active card | Make the real route a native `a[routerLink]`; render unavailable concepts as non-interactive content or remove them |
| Autocomplete lacks complete keyboard/ARIA semantics | **Still Present** | Search template lines 64–105 has an unlabeled input and clickable `div` suggestions; no combobox/listbox roles or key binding; `selectedSuggestionIndex` is never changed | Implement the editable-combobox pattern with retained input focus, keyboard selection, dismissal, unique IDs, and matching ARIA state |
| Toasts are scoped to one page | **Still Present** | Renderer is `word-lookup.component.html` lines 2–32; shell is only `<router-outlet />`; service itself is root-provided | Move presentation to the app shell and add deliberate status/alert live-region behavior |
| Emoji strings are encoding-damaged | **No Longer Applicable** as literal corruption; **Partially Remediated** as consistency/accessibility | UTF-8 reads show valid `✅`, `❌`, `ℹ️`, `⚠️`, `📚`, `🔍`, arrows, and multiplication sign; `index.html` declares UTF-8. Default PowerShell decoding can display mojibake | Preserve UTF-8; replace unnecessary emoji with text or existing inline SVG and hide decorative symbols from AT where needed |
| Dashboard concepts/documentation are stale | **Still Present** | Dashboard advertises nonexistent `/analytics`, `/preferences`, `/admin`; docs describe the Angular frontend and quiz as future work; HTTP files target obsolete endpoints | Remove or clearly present non-features and refresh developer docs/examples to current routes/contracts |
| Angular build reports an SCSS budget warning | **Unable to Determine from Static Analysis** | Production `anyComponentStyle` is 2 kB warning/4 kB error; word-lookup SCSS is 3,750 source bytes; exact emitted size requires a build | First reduce/move toast and obsolete styles through natural boundaries; build later to identify emitted offender before changing budgets |
| Angular tests do not complete reliably | **Unable to Determine from Static Analysis**; strong configuration suspect | `npm test` is `ng test`; no `--watch=false`; no frontend CI; stale `AppComponent` assertions guarantee failures, while toast tests leave real removal timers scheduled | Add an explicit single-run script/CI command, repair obsolete tests, control timers, then reproduce with runtime diagnostics |

## 5. Save for Later Analysis

### Verified behavior

`word-lookup.component.html` lines 139–151 displays Save for Later beside Add to My Vocabulary whenever a dictionary result is being viewed outside the saved-vocabulary detail flow. The button is a normal enabled button but has no `(click)`, form behavior, disabled state, explanatory text, or decorative semantics. Searching the UI, API, data models, and controllers finds no `saveForLater`, bookmark, queue, archive, or equivalent capability.

The existing related workflows are distinct:

- `addToVocabulary()` posts to `/words/vocabulary/add` and makes the word part of the user's collection (`word-lookup.component.ts` lines 328–365).
- `toggleFavorite()` persists an `IsFavorite` flag through `PUT /api/words/vocabulary/{id}/favorite` (`word-lookup.component.ts` lines 620–640; `WordsController.SetFavorite`). Favorites only apply after a word is in the collection.
- Preferred definition selects quiz content for an already saved word; it is not a deferred-save state.

No backend capability supports a separate Save for Later lifecycle. Implementing it would require a product definition (relationship to Add and Favorite), persistence, contracts, migrations, filtering, and tests. That is new product work, not R17 cleanup. Removing it does not alter any executable workflow because the control currently causes no state change. **Required R17 recommendation: remove the control.** Renaming it to Favorite would be misleading because the current result may not yet have a `UserWord` ID.

## 6. Dashboard Accessibility Analysis

`dashboard.component.html` lines 17–42 repeats a `div` for every card and binds `(click)="onCardClick(card)"`. The element has no `tabindex`, keyboard handler, `role`, or accessible control semantics. Its text provides a visual name, but it is absent from the keyboard tab order and is announced as ordinary content. Hover, transform, cursor, and “Click to explore” styling create an interactive affordance unavailable to keyboard users.

`DashboardComponent.onCardClick()` (`dashboard.component.ts` lines 70–77) calls `router.navigate` for the active Vocabulary Builder card. A native `<a routerLink="/vocabulary">` is the correct element because the outcome is navigation and supports focus, Enter activation, browser link behavior, and link semantics without compensating ARIA.

The three inactive cards still receive click events but only log “coming soon” to the console. Their nonexistent routes are not declared in `app.routes.ts`. They should not be buttons or links. Required remediation is to remove them if they are stale product concepts, or render clearly non-interactive informational content without click/cursor/hover affordances. Turning them into disabled buttons would retain misleading controls.

The Logout control is correctly a native button. Dashboard tests contain only a creation assertion and do not cover routing or semantics (`dashboard.component.spec.ts`).

## 7. Search and Autocomplete Accessibility Analysis

### Current interaction

- Typing two or more characters calls `searchUserVocabulary()` immediately on every model change; there is no debounce or stale-request cancellation (`onSearchInput`, lines 69–83).
- Results are populated asynchronously with up to five saved-word suggestions plus one dictionary-search action (`searchUserVocabulary`, lines 85–121).
- Suggestions open whenever the array is nonempty and are selected only by mouse click (`word-lookup.component.html` lines 75–105).
- Selection calls `searchNewWord()` and clears the array (`selectSuggestion`, lines 124–132).
- There is no outside-click dismissal, blur behavior, Escape handling, Arrow Up/Down handling, or Tab policy.
- `selectedSuggestionIndex` exists and drives visual classes, but no method changes it. `onKeyUp()` only submits on Enter and is not bound in the template (`word-lookup.component.ts` lines 298–302).

### Semantic gaps

The search input has only placeholder text: it has no associated `<label>` or `aria-label`. The popup and options are `div` elements without `role="listbox"`, `role="option"`, unique IDs, selection state, or an input/popup relationship. Screen readers cannot identify expansion or the active result.

The appropriate target is an editable combobox whose DOM focus stays on the input. The input needs an accessible label, `role="combobox"`, dynamic `aria-expanded`, `aria-controls`, and `aria-activedescendant` only when an option is active. The popup should be a uniquely identified listbox and selectable rows should be options with stable unique IDs and `aria-selected`. Decorative section headings must not accidentally become selectable options. Arrow Down/Up should move the active option, Enter activate it, Escape dismiss without clearing typed text, and Tab should leave the widget normally while dismissing the popup. Mouse selection must continue to work without premature blur closing the popup.

The current template iterates the entire suggestion array twice and conditionally renders types inside each loop. Consequently visual position and `selectedSuggestionIndex` can diverge: an existing suggestion at index 0 is rendered in the first section, while the new-search item can retain its full-array index in the second. Implementation should establish one stable option collection/ID mapping.

R14 has not extracted this behavior; no R14 regression or remediation is present. R16's missing cancellation also means out-of-order HTTP responses can replace suggestions for newer input, an interaction and announcement risk that is closely related but should be coordinated rather than silently expanded into a full data-layer rewrite.

## 8. Dialog and Focus Management Analysis

The only custom modal found is the preferred-definition editor in `word-lookup.component.html` lines 389–440. `openDefinitionEditor()` sets `showDefinitionEditor`, starts a lookup request, and stores editor state; `closeDefinitionEditor()` only clears state (`word-lookup.component.ts` lines 528–572). It is a conditionally rendered pair of `div`s, not Angular CDK, native `<dialog>`, or another dialog library.

Confirmed gaps:

- No `role="dialog"`/native dialog semantics, `aria-modal`, or relationship to the “Choose Quiz Definition” heading.
- No initial focus placement. Focus remains on the opener behind the overlay.
- No Escape binding.
- No focus trap or inert/background-interaction control. Keyboard users can tab to controls behind the overlay.
- No opener reference or focus restoration after close.
- Clicking the backdrop closes it, but this mouse behavior has no keyboard equivalent.
- Loading and save state changes are not announced.

The close button is a native button and has `aria-label="Close"`; the radio inputs are wrapped by labels containing definition text, so they obtain names. Implementation may use CDK dialog only if CDK is already deliberately adopted for this contained need; currently it is not a dependency. A small custom solution is viable but must implement all focus lifecycle behavior correctly. Adding a new broad UI framework is out of scope.

## 9. Toast and User Feedback Analysis

`ToastService` is a singleton (`providedIn: 'root'`) using `BehaviorSubject<Toast[]>`. Any injected component can enqueue, remove, or clear a notification. In current use, only `WordLookupComponent` injects it, for add, favorite, and preferred-definition results.

Presentation is page-local at `word-lookup.component.html` lines 2–32. When navigation destroys that page, the renderer disappears even though the service retains state until its timer removes it. Other routes have no host. `AppComponent` imports only `RouterOutlet`, and its template has no shared renderer. R17 still requires a shell host.

No toast element has `role="status"`, `role="alert"`, or `aria-live`. Thus visual appearance does not reliably announce feedback. The close button has no accessible name; its visible `×` is insufficiently descriptive. Emoji duplicate the toast type and may be redundantly announced. Auto-removal defaults to five seconds (seven for errors), with real `setTimeout` calls and no timer-handle cleanup. Toasts do not intentionally steal focus, which is appropriate.

Implementation should distinguish non-urgent status from urgent error alerts rather than marking every message assertive. The shell host should persist across route transitions, give dismiss buttons specific accessible names, hide decorative icons from assistive technology, and avoid repeatedly re-announcing the entire toast list.

Inline errors in login, signup, word lookup, and quiz are separate visual systems; they also generally lack live-region semantics. Consolidating every feedback mechanism is not required, but important asynchronous errors/statuses should be announced consistently.

## 10. Encoding and User-Facing String Audit

### Verified source state

- `VocabularyApp.UI/src/index.html` declares `<meta charset="utf-8">`.
- Reading the relevant templates and TypeScript explicitly as UTF-8 yields valid Unicode: emoji, `←`, `→`, `×`, ellipsis, and smart punctuation.
- Repository searches for typical mojibake markers (`Ã`, `Â`, `â`, `ð`, replacement character `�`) outside the pre-existing R17 drafts did not identify corrupted application strings.
- Apparent strings such as `ðŸ“š` and `â†` arose when PowerShell decoded UTF-8 without the explicit encoding. They are display artifacts, not evidence that those bytes are corrupt.

### Remaining consistency/accessibility issue

Emoji are used extensively and inconsistently as icons in dashboard card data, navigation labels, source badges, status results, empty states, and toasts. Some are meaningful text, some duplicate neighboring text, and some are icon-only visual decoration. Rendering can differ by platform, and assistive technology may announce unwanted emoji names.

R17 should favor plain text for arrows/spinners/status when it remains clear, reuse the existing inline SVG approach where a visual icon materially helps, and mark decorative Unicode or SVG as hidden from assistive technology. No new icon package is justified. Encoding-sensitive tests should verify intended visible text only where it protects a prior corruption regression; snapshotting platform emoji rendering is not useful.

## 11. Styling and SCSS Budget Analysis

`angular.json` production budgets are:

- initial bundle: 500 kB warning, 1 MB error;
- any component style: 2 kB warning, 4 kB error.

The most likely component-style offender is `word-lookup.component.scss`, at 3,750 source bytes. It contains toast presentation/animations (roughly its first 75 lines), letter-grid/chip/tooltip rules, a mobile breakpoint, and `::ng-deep` highlight styling. The other component SCSS files are tiny: dashboard is 132 bytes, login/signup 216 bytes each, password input 75 bytes, quiz 29 bytes, and the shell is empty.

Source byte size is not emitted CSS size, so static inspection cannot confirm the exact warning or final byte count. No build was run. The historical warning remains likely because the configured 2 kB threshold is intentionally tight and R14 did not extract toast or vocabulary subcomponent styles.

Preferred remediation is to remove obsolete/duplicated rules and move styles with extracted responsibilities where natural—especially toast-host styles—then run a production build and inspect Angular's named offender. `slideOutRight` appears unused, while toast hover/button nesting may be simplified. Increasing the budget should occur only after measuring emitted CSS and documenting why a maintainable cohesive component legitimately exceeds 2 kB; it should not be the first response.

## 12. Angular Test Reliability Analysis

### Current configuration

- `package.json`: `"test": "ng test"`.
- `angular.json`: Karma builder with no `watch`, `browsers`, `singleRun`, or custom launcher settings.
- Dependencies are Jasmine 5.1, Karma 6.4, Chrome launcher 3.2, coverage, and the Jasmine HTML reporter.
- There is no separate Karma configuration file or custom test bootstrap.
- `.github/workflows/backend-tests.yml` is the only workflow; frontend tests are not run in CI.

### Confirmed defects

1. `app.component.spec.ts` is obsolete. It expects title `VocabularyApp.UI` and a generated “Hello, VocabularyApp.UI” heading, while `AppComponent.title` is `Vocabulary App` and the template contains only a router outlet. These tests should fail, although failure alone does not explain non-termination.
2. No repository script expresses the required one-shot test behavior. The ordinary `ng test` development command is configured without `--watch=false`; watch mode is the strongest static explanation for a process that appears not to terminate.
3. Frontend tests have no CI enforcement, so intended headless/non-watch behavior is undocumented and unverified.

### Strong suspects

- Historical invocations used the watch-oriented `npm test`/`ng test` command and waited for process exit. An explicit CI script such as a headless, no-watch run is required during implementation.
- `ToastService` schedules real five/seven-second timers for each notification. Toast specs call `success()`/`error()` repeatedly without fake time or teardown. These timers can prolong or destabilize browser test cleanup and should be controlled, although static analysis cannot prove they caused the historical timeout.

### Weaker possibilities

- Toast specs create persistent `BehaviorSubject` subscriptions and nested subscriptions. Jasmine/TestBed normally tears down test context, and the synchronous `BehaviorSubject` emission makes the `done` callbacks complete; this pattern is poor but not by itself a confirmed open handle.
- `DashboardComponent` and `LoginComponent` subscribe without explicit destruction. Fixture teardown generally destroys them, and their observables are not proven to schedule continuing work. These are lifecycle hygiene issues, not a demonstrated runner hang.
- No `setInterval`, periodic timer, global event listener, unresolved test HTTP request, `fakeAsync` misuse, or custom browser launcher was found in frontend tests.

### Runtime confirmation required

Later implementation must separately test (a) a one-shot headless command, (b) whether failures finish and return nonzero promptly, (c) whether toast timers leave the browser active, and (d) whether the full suite exits after all specs. Browser availability/launcher behavior and historical environmental timeouts cannot be inferred statically.

## 13. Documentation Audit

### Current/useful

- `VocabularyApp.UI/README.md` accurately identifies Angular 18.1.1, common serve/build commands, current production output location, IIS deployment, and `/api` production base URL. Its unit-test section is incomplete because it gives only the watch-oriented command.
- `Docs/README.md` has current high-level .NET 8, JWT-secret, and password-hashing guidance added by earlier security remediation.
- R2, R3, and R6 documents under `Docs/Updates` are authoritative for those remediations but are not a substitute for current frontend documentation.

### Stale or contradictory

- There is no root `README.md`; the principal overview is `Docs/README.md`.
- `Docs/README.md` omits `VocabularyApp.UI` from its project tree, calls `UserWordService`, `QuizService`, and the Angular frontend future enhancements even though vocabulary and quiz capabilities exist, lists obsolete word endpoints (`search`, `definitions`, `GET words/{id}`), omits current vocabulary/quiz routes, and claims HTTP testing is sufficient.
- `Docs/FRONTEND-SUMMARY.md` describes only login/signup/dashboard, says vocabulary lookup and quiz are future work, lists only user endpoints, and presents three dead dashboard concepts as intentional features. Its “complete” claim is misleading.
- `VocabularyApp.UI/README.md` retains generic CLI text, mentions `ng e2e` despite no e2e builder/package, and does not document application routes, authentication requirements, frontend architecture, environment setup, or a reliable single-run test command.
- `test-api.http` labels word lookup unauthenticated and calls removed `/api/words/search/{term}` and `/api/words/definitions/{word}` routes. Current `WordsController` has authenticated `GET /api/words/lookup/{word}` and vocabulary add/list/search/favorite/preferred-definition endpoints. It also documents a `PUT /api/users/profile` route that does not exist; `UsersController` exposes GET profile, change-password, and validate-token.
- `VocabularyApp.WebApi/VocabularyApp.WebApi.http` calls the removed template `/weatherforecast/` endpoint.
- Current quiz routes (`POST /api/quiz/start`, `POST /api/quiz/submit`, `GET /api/quiz/history`) are undocumented in the general HTTP examples.

R17 implementation should update the overview, frontend summary/README, actual UI routes, authenticated API examples, vocabulary operations, and quiz examples. Historical assessment/plan documents should remain historical rather than be rewritten to pretend their observations never existed.

## 14. Additional Accessibility Findings

### Required for R17

- Saved-word rows are clickable `div`s (`word-lookup.component.html` lines 329–347). They open word details but are not focusable or keyboard operable. Because each row also contains a favorite button, implementation needs valid nested-interaction structure: a real button/link for the detail action plus a sibling favorite button, not a button nested in a button.
- The main lookup field and the saved-vocabulary filter rely on placeholders and have no programmatic labels (template lines 64–66 and 276–281).
- Toast dismiss buttons lack accessible names (lines 25–29).
- Lookup and quiz asynchronous error/status messages have no `role="alert"`/live status behavior. This affects primary workflow feedback.
- Favorite buttons all expose the same static accessible name “Toggle favorite” rather than state and word, despite dynamic visual title text (lines 339–345). The name should identify the action, target word, and current outcome.

### Recommended but optional if small

- Quiz answer choices are native buttons, but selected state is color-only and not programmatically exposed. A radiogroup/radio pattern or `aria-pressed`-style single-selection semantics should convey selection; the chosen pattern must match expected keyboard behavior.
- Quiz results combine color with Correct/Incorrect text, so they are not color-only, but dynamic results and validation errors should receive focus or live announcement at sensible transitions.
- Loading spinners are textual Unicode and may be announced confusingly; status text already conveys loading and can own the accessible announcement while the symbol is hidden.
- Audio's visible “Play” text provides a name; its title is redundant but not a blocker.
- The alphabetical letter buttons are native, stateful, and labeled. Their tooltip IDs are unique per letter. This is an area already substantially accessible; manual testing should confirm disabled-state and tooltip behavior.

### Defer

A complete WCAG audit, broad color-contrast certification, typography redesign, new design system, and full application navigation redesign exceed R17. They should be separately scoped if later testing finds failures.

## 15. Responsive/Mobile Risks

Static source shows several positive responsive choices in word lookup (`p-3 sm:p-6`, wrapping header controls, responsive word header, four-column letter grid). Actual device testing was not performed.

Risks requiring manual verification:

- Toasts use fixed `top-4 right-4` positioning plus `min-w-80`; 20rem minimum width plus offsets can overflow or crowd a 320px viewport.
- The definition overlay uses `max-w-2xl` and a scrollable options area, but the dialog container itself has no viewport-height limit; large text/landscape keyboards may make header/actions inaccessible.
- Dashboard header uses a single `flex justify-between` row with `px-6`, without wrap or small-screen column behavior; long usernames can collide with Logout.
- Dashboard cards use large `p-8` and hover-only motion cues; touch behavior and layout should be checked.
- Quiz uses outer `p-6`, a fixed horizontal header, and a second button with `ml-3` rather than a wrapping action container; setup actions may overflow narrow screens.
- Long definitions, suggestion previews, toast text, and quiz answers need overflow/wrapping checks at 320/375px and 200% zoom.
- Modal focus/virtual-keyboard behavior cannot be assessed statically.

## 16. Interaction With R14–R16

### R14

Not implemented on this branch. The exact 780-line/443-line monolith cited in the plan remains, as do page-local toast presentation and the embedded definition editor. R17 changes to search, dialog, toast, and word rows will overlap the same files and carry higher regression/merge risk. A full R14 decomposition is not required inside R17, but extracting a shell toast host or tightly bounded dialog/search responsibility may be a natural boundary if coordinated.

### R15

Not implemented to definition of done. Components still construct endpoint URLs, use `any`, manually parse several error shapes, and rely on the generic `ApiService`. R17 should avoid undertaking the full auth/API architecture rewrite. It must, however, account for asynchronous search ordering and test mocking at the current boundary.

### R16

Not implemented to definition of done. `loadVocabularyPage()` explicitly requests 1,000 words and performs local substring and alphabetical filtering. The server has list/search endpoints, but the client workaround remains. R17 should not claim server-side paging/search is resolved and should avoid designing accessibility state around an assumed future query architecture.

### Earlier changes that do help R17

The separate `/quiz` component/route, root-provided toast state service, native letter buttons with labels/state, responsive word-page layout additions, favorites endpoint, and preferred-definition workflow provide real behavior that R17 can preserve. They also introduce new R17-relevant surfaces: the custom preferred-definition overlay, quiz selection semantics, and non-semantic saved-word rows.

## 17. Existing Test Coverage

- `word-lookup.component.spec.ts` covers creation, clearing detail when typing, local letter counts/selection, local filtering across fields, definition option construction, and highlighting. It does not test autocomplete interaction, semantics, dialog behavior, toast host, add behavior, word-row keyboard behavior, or favorite accessible names.
- `dashboard.component.spec.ts` has only a creation test and lacks obvious router/auth test providers; it does not verify navigation or semantics.
- `toast.service.spec.ts` covers creation, enqueue types, remove, and clear at a basic level. It does not control auto-removal time, verify duration, handle multiple toasts robustly, or test rendering/live regions.
- `app.component.spec.ts` is obsolete and contradicts the current title/template.
- Login/signup tests contain only basic creation coverage; auth/API service tests are also creation-only.
- No `quiz.component.spec.ts`, end-to-end suite, automated accessibility scanner, or frontend CI exists.

Some current tests characterize R16-era local filtering rather than the planned server-side behavior; they are not obsolete for current code, but they will need revision if R16 later lands.

## 18. Required Automated Test Coverage

During implementation, add or update tests for:

1. **Dead control:** template no longer exposes Save for Later, without changing Add or Favorite behavior.
2. **Dashboard:** active navigation is a real link with the correct `routerLink` and accessible name; unavailable cards are non-interactive and absent from tab order; logout remains a button.
3. **Autocomplete:** label and ARIA state; opening/closing; Arrow Down/Up bounds or wrapping policy; Enter selection; Escape dismissal; Tab exit; active descendant and selected option synchronization; mouse selection; stale-response behavior if addressed.
4. **Dialog:** dialog name/modality, initial focus, Escape, focus containment, backdrop close, return focus, loading/save transitions, and radio labeling.
5. **Toast:** app-shell rendering across routed content, status versus error announcement semantics, accessible dismiss names, manual dismissal, deterministic timed removal using fake time, and route-transition persistence.
6. **Vocabulary rows:** keyboard activation and independent favorite action; dynamic accessible favorite name/state.
7. **Quiz:** programmatic single-selection state and keyboard behavior if its pattern changes.
8. **Encoding/text:** a small assertion for intentionally retained visible Unicode/text only if needed to prevent the specific regression; do not snapshot emoji glyph rendering.
9. **Runner/build:** replace stale app-shell assertions; add a documented headless/no-watch script and CI job that proves the suite exits; run a production build that fails on errors and reports budget warnings clearly.

Component tests are appropriate for DOM semantics and keyboard events. App-shell route persistence and full Discover → Save → Understand → Review → Practice behavior merit integration/e2e coverage. Actual screen-reader announcement quality, focus visibility, mobile layout, and platform glyph rendering require manual checks.

## 19. Required Manual Verification

After implementation:

- Traverse login, dashboard, lookup, suggestions, save, saved-word review, favorite, preferred-definition editing, and quiz using keyboard only.
- Verify visible focus at every interactive element and no focus reaches hidden/background dialog content.
- In autocomplete, test Arrow Up/Down, Enter, Escape, Tab, mouse selection, no-results/errors, and rapid typing.
- Confirm dialog initial focus, logical tab order/trap, Escape/backdrop/Cancel/close behavior, and return to the exact opener.
- Smoke-test with at least one desktop screen reader: field label, combobox expanded state, option count/active option, dialog name/modality, selection state, inline errors, toast status/errors, and quiz selection/result.
- Trigger success/error/multiple toasts, dismiss them, navigate while one is visible, and verify announcements do not repeat excessively.
- Verify dashboard real navigation and that “coming soon” content is not presented as actionable.
- Check 320px, 375px, tablet, desktop, landscape, 200% zoom, long username/word/definition/error text, and virtual-keyboard dialog behavior.
- Inspect all principal screens for replacement characters/mojibake on supported browsers and operating systems.
- Run the production Angular build and record the exact budget output.
- Run the complete one-shot Angular suite headlessly and confirm both success and intentional failure return promptly with correct exit codes.

## 20. Scope Classification

### Required for R17

- Remove Save for Later.
- Replace dashboard navigation `div` with a semantic link and remove interactivity from unavailable cards.
- Make saved-word detail rows keyboard/semantically operable without nesting controls.
- Implement the search combobox keyboard and ARIA contract, including an accessible label and dismissal behavior.
- Add dialog semantics and complete focus lifecycle.
- Move toast presentation to the shell and add live-region/dismiss semantics.
- Label both search fields and correct material asynchronous feedback/favorite accessible names.
- Normalize user-facing icon use enough to avoid decorative AT noise; preserve UTF-8.
- Diagnose and resolve the actual component-style warning based on later build evidence, or document a deliberate threshold decision.
- Establish a reliable one-shot frontend test command, fix obsolete specs/timer handling, and add frontend CI.
- Update current READMEs/frontend summary, route documentation, and HTTP examples.
- Perform the automated and manual verification listed above.

### Recommended but Optional

- Expose quiz answer selection programmatically and improve quiz result/error announcements.
- Remove clearly unused `slideOutRight` and small obsolete style fragments while touching the owning styles.
- Add a minimal end-to-end critical-path test if infrastructure can be introduced without turning R17 into a testing-platform project.
- Add reduced-motion consideration for nonessential toast/dashboard animation if a small local change.

### Deferred / Out of Scope

- Implementing a Save for Later product state or backend.
- Building analytics, preferences, admin, spaced repetition, notes, or other new product features.
- Full R14 component decomposition, full R15 auth/API-client migration, or full R16 query redesign.
- Major route redesign into `/discover` and `/words`.
- New state-management architecture, icon/design framework, visual redesign, or complete WCAG certification.
- Unrelated backend or database changes/migrations.

## 21. Risks and Dependencies

- **Monolith overlap:** search, modal, toast, collection rows, and styles share `WordLookupComponent`; poorly sequenced edits can create regressions and merge conflicts with future R14 work.
- **Autocomplete race/focus:** keyboard state must remain synchronized while asynchronous responses arrive. Blur/mouse ordering is a common regression point.
- **Dialog correctness:** custom focus traps and restoration are easy to get subtly wrong; dynamic removal must not strand focus.
- **Toast announcements:** moving the host can cause duplicate subscriptions/renderers or repeated live-region announcements unless the page-local host is removed atomically.
- **Nested interaction:** converting a whole saved-word row naively can create invalid nested buttons around Favorite.
- **Budget uncertainty:** source size is not emitted size; budget edits without the later build would be guesswork.
- **Runner environment:** Chrome/headless availability and Angular CLI watch defaults must be distinguished from code-level open handles through runtime evidence.
- **Documentation authority:** historical remediation records should remain intact; current-facing documents must clearly become the source of truth.
- **Branch prerequisites:** current source does not contain R14–R16. If those changes exist elsewhere, implementation must rebase/re-audit before editing overlapping files.

## 22. Recommended Implementation Boundaries

These are evidence-based work areas, not a full implementation plan:

1. **Misleading/navigation controls:** remove Save for Later; make dashboard and saved-word navigation semantic.
2. **Search accessibility:** implement the complete combobox state/keyboard/ARIA unit and its tests.
3. **Overlay accessibility:** implement preferred-definition dialog semantics and focus lifecycle with tests.
4. **Feedback architecture:** establish the shell toast host, live-region policy, deterministic timing, and accessible inline async feedback.
5. **String/style cleanup:** normalize decorative icons, remove dead styles, then measure the production style budget.
6. **Test reliability/CI:** repair stale specs, create a single-run headless command, control timers, and prove termination in CI.
7. **Documentation synchronization:** update current-facing architecture/routes/API examples after behavior and commands are final.
8. **Manual verification:** keyboard, screen-reader, responsive, build, and termination checklist.

## 23. R17 Definition-of-Done Assessment

| Definition-of-done item | Assessment | Reason |
|---|---|---|
| No visible dead action remains | **Not Satisfied** | Save for Later is visible and inert; inactive dashboard cards also retain click affordances |
| Primary workflows are keyboard usable | **Not Satisfied** | Suggestions and saved-word rows are mouse-only; dialog focus behavior is absent |
| Accessibility semantics are appropriate | **Not Satisfied** | Dashboard, autocomplete, dialog, notifications, labels, and selected states contain material gaps |
| Feedback is consistently available and screen-reader compatible | **Not Satisfied** | Toast host is page-local with no live region; several async messages are visual only |
| Damaged encoding is removed | **Already Satisfied** for source integrity; **Partially Satisfied** for icon consistency | No actual UTF-8 corruption confirmed; emoji use still needs accessibility/consistency cleanup |
| Production build is clean or budget exception documented | **Requires Runtime Verification** | Static configuration/source size makes a warning plausible, but only a build identifies current emitted output |
| Angular tests terminate reliably | **Requires Runtime Verification** (configuration currently inadequate) | No single-run script/CI exists; stale tests and real toast timers require remediation and execution |
| Developer documentation matches the application | **Not Satisfied** | Overview, frontend summary, and both HTTP files contradict current routes and features |

## 24. Conclusion

R17 is **moderate-to-substantial frontend remediation**, not merely cosmetic cleanup. The dead button and dashboard link are small, but correct autocomplete behavior, custom-dialog focus management, application-level toast announcements, runner diagnosis, and documentation repair span core interaction and developer-confidence concerns.

The highest-risk areas are autocomplete's asynchronous keyboard state, dialog focus containment/restoration, and coordinating several edits within the unresolved R14 monolith. The encoding issue is less severe than originally reported: current source is valid UTF-8, so work should target consistent and accessible icon presentation rather than repairing nonexistent byte corruption. Build-budget and test-termination outcomes remain explicitly runtime-dependent and must be verified during the later implementation phase.
