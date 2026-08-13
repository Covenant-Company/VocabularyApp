# R17 — Accessibility, UI Consistency, Test Reliability, and Documentation Implementation Plan

## 1. Purpose

This document defines the phased implementation of R17 against the current `VocabularyApp` branch. It converts the verified findings in `Docs/Updates/R17-Accessibility-UI-Documentation-Analysis.md` into independently reviewable changes, tests, manual gates, and commit boundaries.

This is a plan, not an implementation record. No production code, tests, configuration, workflows, styles, or existing documentation were changed while creating it. No tests, builds, accessibility tools, or formatting tools were run. Future Codex implementation sessions must make only the phase-specific changes, tell the user exactly what to run, and stop for the user's runtime results before proceeding.

## 2. Source Analysis Summary

The implementation is driven by these verified facts:

- **Save for Later** is an enabled button without behavior or a backend concept. It must be removed, not implemented or repurposed.
- The dashboard's active route and three unavailable concepts are all rendered as clickable `div` cards. Only Vocabulary Builder navigates; the unavailable cards merely log to the console.
- Saved-vocabulary rows are also clickable `div` elements and contain a nested Favorite button. The detail action and Favorite action require separate native controls.
- The main lookup field and collection-filter field lack programmatic labels.
- Autocomplete is mouse-only. It lacks combobox/listbox/option semantics, stable option IDs, keyboard interaction, dismissal behavior, and active-state synchronization. Its array is rendered through two loops, so model indices can diverge from visual options.
- The preferred-definition editor is a custom conditional overlay without dialog semantics, Escape handling, focus containment, initial focus, or opener-focus restoration.
- `ToastService` is already root-provided, but the renderer and styles are owned by `WordLookupComponent`. Toasts lack live-region behavior and specifically named dismiss controls.
- Important asynchronous errors and status changes are generally visual only.
- Application files are valid UTF-8. R17 needs icon/emoji consistency and assistive-technology cleanup, not byte-encoding repair.
- The production component-style warning threshold is 2 kB, while `word-lookup.component.scss` is approximately 3,750 source bytes. Only a later production build can establish the emitted offender and size.
- `npm test` invokes `ng test` without explicit one-shot behavior. `app.component.spec.ts` is stale, toast tests use real timers, frontend CI is absent, and runtime evidence is still required before attributing non-termination to a particular cause.
- Current-facing READMEs and HTTP examples contradict the current routes, authentication rules, vocabulary endpoints, and quiz features.
- R14–R16 definitions of done are not present: `WordLookupComponent` remains 780 TS lines/443 template lines, feature calls still use generic `ApiService` strings and `any`, and ordinary collection browsing still requests `pageSize=1000`.

There is no material discrepancy between the authoritative analysis and the source state inspected for this plan.

## 3. Scope

### In Scope

- Remove dead and misleading affordances.
- Replace non-semantic navigation and row actions with native links/buttons.
- Give search fields programmatic labels.
- Implement an accessible editable-combobox interaction for lookup suggestions.
- Implement complete modal focus and keyboard behavior for the preferred-definition editor.
- Move toast presentation to the application shell and establish polite versus urgent announcement rules.
- Improve material asynchronous feedback, Favorite naming/state, and optional quiz selection/result semantics where contained.
- Normalize decorative emoji/icon handling without changing valid UTF-8.
- Naturally relocate/remove R17-owned styles, then resolve the actual measured budget outcome.
- Repair static frontend test defects, create a reliable one-shot command, make toast timers deterministic, and add frontend test CI consistent with the existing workflow style.
- Update current-facing developer documentation and API examples after behavior and commands stabilize.
- Perform user-run automated, keyboard, screen-reader, responsive, encoding, build, termination, and CI verification.
- Create the R17 completion record only after final evidence is supplied.

### Optional if Small and Safe

- Programmatically expose quiz answer selection and announce quiz errors/results without redesigning quiz interaction.
- Remove demonstrably unused R17-adjacent styles such as the unused toast exit keyframe.
- Respect reduced-motion preferences for R17-touched nonessential animations.
- Add a small integration test for route-persistent toast presentation if existing Angular test infrastructure can support it without introducing an end-to-end framework.

### Explicitly Out of Scope

- A Save for Later feature, migrations, archive state, or new product workflow.
- Analytics, preferences, administration, spaced repetition, notes, learning-state redesign, or other backlog features.
- Full R14 decomposition; full R15 typed API/auth/interceptor migration; full R16 server-side paging/search.
- Major route redesign, global state-management architecture, new design system, new icon library, broad UI framework, or visual redesign.
- Complete WCAG certification, unrelated backend work, database changes, or package upgrades.
- Rewriting historical assessment, remediation, analysis, or plan documents to remove their historical observations.

## 4. Implementation Principles

1. Use native HTML first: links for navigation, buttons for actions, labels for fields. Add ARIA only where native semantics cannot represent the combobox, dialog announcement details, and live feedback.
2. ARIA state and keyboard behavior are one contract. Do not add attributes whose state is not maintained by code and tests.
3. Keep each phase coherent and reversible. A phase must leave no duplicate interaction, toast host, or partial focus implementation.
4. Preserve current product behavior unless the behavior is the verified defect. Do not invent replacement features.
5. Make the smallest durable extractions that R17 directly needs; do not disguise R14 work as accessibility work.
6. Add or update tests in the same phase as the behavior they protect, but leave execution to the user.
7. Treat build warnings, runner termination, browser launcher behavior, and open handles as evidence-dependent. Static fixes may precede execution; conditional fixes require runtime evidence.
8. Prefer testable state transitions and injectable/deterministic timing over DOM tricks.
9. Keep UTF-8 unchanged. Treat terminal mojibake as a display/decoder concern unless source-byte evidence later proves otherwise.
10. Update current-facing documentation only after final behavior, commands, and CI are stable.

## 5. Current Architecture Constraints

`WordLookupComponent` currently owns most R17 surfaces. Phases 1–5 will therefore overlap `word-lookup.component.ts`, `.html`, `.scss`, and `.spec.ts`. They must be implemented sequentially and based on the immediately preceding phase, with no parallel edits to those files.

R17 must not depend on nonexistent R14 child components or R15 feature clients. Autocomplete can remain inside the page component for this remediation because extracting its network/data orchestration would broaden scope. The plan proposes two small durable extractions:

1. **`ToastHostComponent`**: required because presentation must move from a routed page to the application shell. It owns only rendering, announcement semantics, dismiss UI, and host styles; `ToastService` continues to own state/timing. This directly resolves R17, reduces `WordLookupComponent`, and matches R14's future application-toast boundary.
2. **`PreferredDefinitionDialogComponent`**: recommended as a contained extraction because a modal needs isolated element references, focus lifecycle, keyboard handling, and focused component tests. It receives editor data/state and emits close/save events; it must not move API calls or feature orchestration. This directly reduces the risk of implementing focus containment in the 443-line page template and aligns with R14's planned dialog extraction without becoming full decomposition.

If the dialog extraction proves disproportionately disruptive during phase inspection, keep the dialog template in `WordLookupComponent` but use the same explicit focus contract and tests. Record that deviation; do not introduce Angular CDK or another package solely to avoid a small custom implementation.

R16's async/query deficiencies remain visible. Phase 2 may reject stale suggestion responses only to protect the accessibility state for the current input; it must not redesign general paging, filtering, URL state, or feature services.

## 6. Phase Overview

| Phase | Objective | Major files | Risk | Runtime gate | Suggested commit intent |
|---|---|---|---|---|---|
| 1. Semantic Controls | Remove dead actions and establish native dashboard/word-row controls | Dashboard and word-lookup template/TS/spec | Medium | Targeted component tests plus keyboard smoke check | `fix(ui): remove dead controls and restore semantic navigation` |
| 2. Accessible Autocomplete | Implement one coherent editable-combobox state and interaction | Word lookup TS/template/spec | High | Targeted tests plus full autocomplete keyboard/mouse check | `fix(a11y): implement accessible search autocomplete` |
| 3. Accessible Definition Dialog | Establish modal semantics and complete focus lifecycle | Word lookup; new dialog component/spec/styles if extracted | High | Targeted tests plus manual focus lifecycle check | `fix(a11y): add definition dialog focus management` |
| 4. Shell Toasts and Feedback | Move toast UI to the shell and announce async feedback deliberately | App shell, new toast host, toast service/spec, routed templates | High | Shell/service tests plus route and screen-reader smoke check | `fix(a11y): move notifications to application shell` |
| 5. Strings and Style Ownership | Normalize decorative icons and finish R17-owned style cleanup | UI templates/TS/SCSS | Medium | Visual/AT/responsive smoke check; no budget decision yet | `fix(ui): normalize accessible icons and style ownership` |
| 6. Test Reliability and CI | Repair static test defects, create one-shot execution, diagnose using user evidence, add frontend CI | Package/config/specs/workflow/README command note deferred | High | User-run success, failure-exit, full-suite, and CI evidence | `test(ui): establish reliable one-shot Angular tests` |
| 7. Measured SCSS Resolution | Use production-build evidence to remove actual warning or justify budget | SCSS; `angular.json` only conditionally | Medium | User-run production build with recorded budget output | `fix(ui): resolve measured component style budget` |
| 8. Documentation Synchronization | Make current-facing docs and HTTP examples authoritative | READMEs, frontend summary, HTTP files | Low | Manual command/route/contract review | `docs: synchronize frontend routes tests and API examples` |
| 9. Final R17 Verification and Record | Run all final gates and record actual outcomes | Completion doc only after evidence; conditional fixes belong in owning phase | High | Full automated/manual matrix and CI/build evidence | `docs: record R17 accessibility remediation completion` |

Phases 2, 3, and 4 should remain separate commits because each changes a complex interaction model. Phase 6 may require two commits: static runner/test changes first, then evidence-driven conditional corrections and CI.

## 7. Phase 1 — Dead and Misleading Controls

### Objective

Remove the dead Save for Later action and replace dashboard and saved-word-row mouse-only affordances with correct native controls while preserving existing navigation and Favorite behavior.

### Why This Phase Exists

These are verified, high-confidence defects with no runtime uncertainty. Establishing semantic controls first reduces keyboard blockers without entangling later ARIA/focus work.

### Affected Files

- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.spec.ts`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` only if row activation needs a focused helper signature
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.scss` only if focus/interaction styling cannot be expressed by existing classes

### Implementation Steps

1. Remove the Save for Later button and its surrounding dead-only markup. Do not add a method, API call, model, tooltip, or replacement control.
2. Import `RouterLink` into the standalone dashboard component.
3. Render the active Vocabulary Builder card as an `<a>` with `[routerLink]="card.route"`, using its title/description as the accessible name/content. Remove imperative navigation for card activation if no longer used.
4. Render inactive analytics/preferences/admin concepts as non-interactive content, or remove them if the product-facing dashboard should show only real functionality. In either case remove `(click)`, pointer/not-allowed cursor signaling, interactive hover motion, and “Click to explore.” Preserve a truthful “Coming Soon” label only if retaining informational cards.
5. Keep Logout as a native `button`; make its `type="button"` explicit.
6. Restructure each saved-word row into a non-interactive layout container with a dedicated native detail button (or link only if URL navigation becomes real; current behavior is an in-page action, so button is preferred) and an independent sibling Favorite button.
7. Ensure the detail control has the word as its accessible name/content and receives visible focus. Do not wrap Favorite inside the detail control.
8. Give Favorite a dynamic accessible name such as `Add {word} to favorites` / `Remove {word} from favorites`; expose state with `aria-pressed` because it is a persistent toggle. Retain `stopPropagation` only if still necessary; the new sibling structure should make it unnecessary.
9. Give the saved-vocabulary filter a programmatic `<label>` (visually visible or an established visually-hidden utility); retain placeholder only as a hint.

### Accessibility Contract

- Tab reaches the Vocabulary Builder link, Logout, each saved-word detail button, and each Favorite button in logical order.
- Enter activates the link; Enter/Space activates buttons through native behavior.
- Inactive dashboard concepts are not focusable, have no control role, and do not advertise click behavior.
- Each saved-word detail and Favorite action is a sibling; no interactive element is nested in another.
- Favorite exposes both target word and next action in its name and exposes current state through `aria-pressed`.

### Tests to Add or Update

- Dashboard renders Vocabulary Builder as an anchor with `/vocabulary` `routerLink` and accessible text.
- Inactive concepts contain no anchor/button, click binding, `tabindex`, or interactive role.
- Logout remains a button.
- Save for Later text/control is absent while Add to My Vocabulary remains.
- Saved-word detail is a native button and calls `viewWordDetails` for the correct item.
- Favorite is a sibling, not a descendant, of the detail action.
- Favorite accessible name and `aria-pressed` change with `isFavorite`; Favorite activation does not open details.
- Collection filter has an associated label.

### Manual Verification

- Keyboard through dashboard and activate Vocabulary Builder and Logout.
- Confirm retained Coming Soon cards do nothing by mouse or keyboard and do not look clickable.
- In My Words, tab separately to word detail and Favorite; activate both with keyboard.
- Confirm no visual regression to row layout at narrow and desktop widths.

### Runtime Gate

The user runs from `VocabularyApp.UI`:

```powershell
npx ng test --watch=false --browsers=ChromeHeadless --include='src/app/components/dashboard/dashboard.component.spec.ts' --include='src/app/components/word-lookup/word-lookup.component.spec.ts'
```

Expected: the targeted specs pass and the process exits. Because the runner reliability phase has not yet occurred, any launcher/non-termination behavior is recorded without speculative configuration changes. The user also confirms the manual keyboard checks before phase 2.

### Risks

- Dashboard test setup may need Router/Auth test providers once it begins rendering and exercising `RouterLink`.
- Converting the whole word row to a button would create invalid nesting; the sibling requirement is non-negotiable.
- Removing imperative card navigation may leave unused `Router` injection/imports.
- Existing row CSS utility classes may need to move from the wrapper to the detail button to retain the hit target without making the Favorite area activate details.

### Rollback / Recovery Considerations

Revert the phase as one commit. If the word-row structure causes layout regression, retain the non-interactive wrapper and adjust only its child controls; do not restore clickable `div` behavior. Save for Later removal is independent and must not be replaced during recovery.

### Definition of Done

- No Save for Later control remains.
- Dashboard navigation is a real link and unavailable concepts are non-interactive.
- Saved-word detail and Favorite are separate native controls with correct names/state.
- Both search/filter and semantic-control tests added in this phase pass when the user runs them.
- Keyboard smoke checks pass.

### Suggested Commit Intent

`fix(ui): remove dead controls and restore semantic navigation`

## 8. Phase 2 — Search and Autocomplete Accessibility

### Objective

Implement a complete, testable editable-combobox interaction for dictionary/saved-word suggestions while retaining input focus and existing lookup behavior.

### Why This Phase Exists

Autocomplete is a primary Discover workflow and is currently mouse-only. The existing unused `selectedSuggestionIndex` and duplicated rendering loops cannot safely support ARIA by attributes alone; the state model must be corrected first.

### Affected Files

- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts`
- `VocabularyApp.UI/src/app/models/word-lookup.model.ts` only if a stable UI option identifier/type belongs on the view model
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss` only for focused/active option visibility or a visually-hidden label utility

No autocomplete component extraction is planned in R17; its API orchestration remains coupled to the page, and extracting it would pull R15 responsibilities into scope.

### Implementation Steps

1. Define one canonical ordered `visibleSuggestions`/option projection. Each item must have a stable DOM ID derived from a component-unique listbox ID and a stable per-response key/index. Section headings may be presentational group labels but must not occupy selectable indices.
2. Use a single option rendering sequence, optionally grouped with non-option headers, so `activeSuggestionIndex` always refers to the same order assistive technology and sighted users perceive.
3. Replace or rename `selectedSuggestionIndex` to represent **active** keyboard option, not a committed selection. Add derived getters for popup open state, active option, and active-descendant ID.
4. Reset the active index when the term changes, suggestions close, a response replaces options, or an option is selected. Preserve it only when the referenced option still exists by stable key.
5. Add a programmatic label for the main lookup input and stable unique input/listbox IDs.
6. Apply the editable-combobox contract to the input: `role="combobox"`, `aria-autocomplete="list"`, dynamic `aria-expanded`, `aria-controls` while/when appropriate, and `aria-activedescendant` only for a valid active option.
7. Render the popup as `role="listbox"` and each actual suggestion as `role="option"` with a unique ID and synchronized `aria-selected`.
8. Handle keydown at the input:
   - Arrow Down opens/enters the first option or advances, with an explicitly tested boundary policy (recommended: clamp at the last option rather than unexpected wrap).
   - Arrow Up moves backward; when opening from no active option, it may activate the last option if that policy is documented and tested.
   - Enter selects the active option when one exists; otherwise submits the typed term through existing lookup behavior. Prevent default only when the combobox handles it.
   - Escape closes suggestions and clears active state without clearing the input or moving focus.
   - Tab closes suggestions and allows normal focus movement; it does not select an option implicitly.
9. Keep DOM focus on the input during keyboard navigation. Scroll the active option into view if the popup is scrollable.
10. Preserve mouse/pointer selection. Use pointer/mousedown ordering or a narrowly scoped blur deferral so focus/blur does not destroy options before click activation.
11. Add outside-focus/click dismissal only with clean listener lifecycle; prefer template focus-boundary logic over unmanaged global listeners.
12. Protect state from stale async results using a minimal request/term correlation check or RxJS cancellation local to suggestions. Do not redesign all API services or collection search.
13. Ensure errors/short terms close the popup and clear active state. Ensure destroyed components cannot apply late suggestion state.

### Accessibility Contract

- DOM focus remains on the labeled text input while active options change.
- The input accurately reports popup expansion and active descendant.
- Arrow keys change one visually and programmatically active option.
- Enter activates exactly the active option; Escape dismisses; Tab exits normally.
- Mouse selection remains functional.
- Listbox option order, visual highlight, model index, DOM IDs, and ARIA state are always synchronized.
- Non-selectable headings are not announced as options.

### Tests to Add or Update

- Associated label/accessibility name and stable listbox relationship.
- Closed/open `aria-expanded`; `aria-controls` and `aria-activedescendant` presence rules.
- One listbox with the exact visible option order and unique option IDs.
- Arrow Down/Up initialization, movement, and boundaries.
- Visual active class and `aria-selected` follow the active index.
- Enter selects active saved-word and dictionary-search options; Enter without active option submits typed input.
- Escape closes without clearing term or moving focus.
- Tab closes without selection and does not prevent normal tab behavior.
- Mouse selection works and invokes lookup once.
- Short input, error, selection, and new response reset state.
- Stale response cannot reopen/replace suggestions for a newer term if the local safeguard is implemented.
- Section headings do not become options and separate suggestion types do not corrupt indexing.

### Manual Verification

- Test Arrow Down, Arrow Up, Enter, Escape, Tab, mouse selection, scroll visibility, rapid typing, API error, no existing matches, and dictionary-search action.
- Verify focus never enters individual option elements.
- Use a screen reader to hear label, expanded/collapsed state, active option, and option position without duplicated section content.
- Test at 200% zoom and 320px for popup overflow.

### Runtime Gate

The user runs:

```powershell
npx ng test --watch=false --browsers=ChromeHeadless --include='src/app/components/word-lookup/word-lookup.component.spec.ts'
```

Expected: all word-lookup specs pass and the process exits. The user then completes the autocomplete manual matrix and supplies any screen-reader mismatch before phase 3. Runtime-discovered async ordering defects may be fixed within phase 2; general R16 search/paging work remains deferred.

### Risks

- Highest risk is divergence among async results, option order, active index, and DOM IDs.
- Blur/click ordering can break mouse selection or unexpectedly close the popup.
- Overusing `preventDefault` can block normal typing, form submission, or Tab navigation.
- Global event listeners without teardown could create the very test-lifecycle issues R17 later diagnoses.
- `aria-activedescendant` pointing to a conditionally absent element is an accessibility regression.

### Rollback / Recovery Considerations

Keep the state-model/template/test change atomic. If outside-click behavior is unstable, retain Escape/Tab/focus-boundary dismissal and defer only the optional pointer-outside enhancement; do not ship ARIA state disconnected from keyboard behavior. Revert the phase rather than retain a partial combobox.

### Definition of Done

- One canonical option order drives rendering, keyboard state, and ARIA.
- The input is labeled and implements the stated editable-combobox contract.
- Keyboard, mouse, dismissal, reset, and stale-response tests pass when user-run.
- Screen-reader and responsive smoke checks find no blocking issue.

### Suggested Commit Intent

`fix(a11y): implement accessible search autocomplete`

## 9. Phase 3 — Preferred-Definition Dialog Accessibility

### Objective

Give the preferred-definition editor correct modal semantics and a complete, independently testable focus lifecycle.

### Why This Phase Exists

The current overlay is mouse-dismissible but has no dialog identity, keyboard close, focus containment, initial focus, or return focus. Incorrect partial focus handling can be worse than the current state, so this work is isolated from autocomplete.

### Affected Files

- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.spec.ts`
- Recommended new files:
  - `VocabularyApp.UI/src/app/components/preferred-definition-dialog/preferred-definition-dialog.component.ts`
  - `...component.html`
  - `...component.scss`
  - `...component.spec.ts`

Conditional fallback: keep markup/focus helpers in the word-lookup files if extraction would require moving HTTP/business orchestration. No package/configuration change is expected.

### Implementation Steps

1. Extract only dialog presentation and interaction if using the recommended component. Inputs: word, options, selected ID, loading, saving. Outputs: selection change, save, cancel/close. API lookup/save and collection state remain in `WordLookupComponent`.
2. Use native `<dialog>` with `showModal()` if current supported-browser behavior and Angular lifecycle can be implemented predictably without polyfills; otherwise use a custom element with `role="dialog"` and `aria-modal="true"`. Record the chosen mechanism in code/tests.
3. Give the heading a unique ID and associate it with the dialog via `aria-labelledby`; optionally associate word/instructions with `aria-describedby`.
4. Capture the exact opener element in `openDefinitionEditor()` before displaying the dialog.
5. After rendering/opening, place focus deliberately: recommended first focus is the currently selected radio option when options are ready; during loading use the Close button or dialog container with a clear strategy. Do not focus an unavailable Save button.
6. Handle Escape as Cancel/close unless saving must temporarily prevent closure. Prevent duplicate close events from native `cancel` plus component handlers.
7. Contain Tab/Shift+Tab within currently enabled dialog controls. If native `showModal()` is chosen, still verify actual browser focus containment and implement only missing behavior.
8. Backdrop click closes only when the event target is the backdrop/dialog surface, never when clicking content. Close, Cancel, and backdrop share one close path.
9. On every close path—Close, Cancel, Escape, backdrop, successful save, load failure—remove/hide the dialog, then restore focus to the saved opener if it remains connected and enabled. Use a documented fallback only if it does not.
10. Prevent background pointer interaction while modal. Ensure scroll/viewport behavior keeps header, options, and actions reachable.
11. Expose loading as polite status and save failure through the phase 4 feedback policy when available; in this phase at minimum ensure loading text is associated/announced without stealing focus.
12. Preserve radio label behavior and selected preferred definition.

### Accessibility Contract

- Opening announces a modal named “Choose Quiz Definition.”
- Focus moves inside only after the dialog exists, lands predictably, and never escapes via Tab/Shift+Tab.
- Escape, backdrop, Cancel, and Close are equivalent cancellation paths; save success is a close path.
- Focus returns to the exact Pick Quiz Definition opener on every close path when it still exists.
- Background content is neither focusable nor pointer-operable while modal.
- Radio definitions retain meaningful labels and native arrow-key behavior.

### Tests to Add or Update

- Dialog role/native element, modal state, `aria-labelledby`, unique heading ID.
- Initial focus during loading and after options load/current selection becomes available.
- Tab and Shift+Tab wrap/containment across enabled controls.
- Escape emits one close and does not save.
- Backdrop closes; inner click does not.
- Close and Cancel close; Save emits correct selected definition and saving state prevents duplicates.
- Focus returns to the exact opener for Cancel, Escape, successful save, and load failure.
- Radio inputs have definition-derived labels and current selection.
- No API service is introduced into the extracted presentational component.

### Manual Verification

- Open from several saved words; verify exact return target.
- Exercise Tab/Shift+Tab, Escape, backdrop, Close, Cancel, Save, loading, load failure, and save failure.
- Verify screen-reader dialog name/modal announcement and radio labels.
- Verify 320/375px, landscape, 200% zoom, long definitions, and virtual keyboard where practical.

### Runtime Gate

The user runs targeted dialog and word-lookup specs with `ng test --watch=false --browsers=ChromeHeadless --include=...`, using the actual new dialog spec path if extracted. Expected: pass and exit. The phase cannot proceed until the user confirms focus containment and restoration manually; DOM assertions alone are insufficient.

### Risks

- Angular conditional rendering can race focus placement.
- Native dialog `cancel`/close events can double-trigger component state if not unified.
- A hand-rolled focus trap can omit dynamically enabled/disabled controls.
- Extracting API calls would expand into R14/R15; keep orchestration in the parent.
- Removing the opener during route/view state changes requires a safe focus fallback.

### Rollback / Recovery Considerations

The extraction is a single responsibility boundary and can be reverted without data/API changes. If native dialog behavior is incompatible with supported targets, retain the component boundary and swap only its internal semantic/focus implementation. Do not fall back to the original inaccessible overlay after other phases depend on the component contract.

### Definition of Done

- Dialog name, modality, initial focus, containment, all close paths, and focus restoration satisfy the contract.
- Parent retains API/business orchestration.
- User-run tests pass and manual keyboard/screen-reader/mobile checks confirm behavior.

### Suggested Commit Intent

`fix(a11y): add definition dialog focus management`

## 10. Phase 4 — Application-Level Toast and Feedback Accessibility

### Objective

Move notifications to a single shell-level host, make timing testable, and announce urgent and non-urgent asynchronous feedback appropriately across routes.

### Why This Phase Exists

Toast state is already global but presentation is destroyed with the vocabulary page. Current toasts and several asynchronous messages are visual only. This phase establishes one application feedback boundary before final style and documentation work.

### Affected Files

- New `VocabularyApp.UI/src/app/components/toast-host/toast-host.component.{ts,html,scss,spec.ts}`
- `VocabularyApp.UI/src/app/app.component.ts`
- `VocabularyApp.UI/src/app/app.component.html`
- `VocabularyApp.UI/src/app/app.component.spec.ts` (shell behavior only; remaining runner cleanup continues in phase 6)
- `VocabularyApp.UI/src/app/services/toast.service.ts`
- `VocabularyApp.UI/src/app/services/toast.service.spec.ts`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` only to remove presentation exposure or align feedback calls
- `VocabularyApp.UI/src/app/components/quiz/quiz.component.html` and login/signup templates only for material inline async announcement semantics, if verified in phase scope

### Implementation Steps

1. Create a standalone `ToastHostComponent` that injects `ToastService`, renders its stream, and owns all toast DOM/styles/dismiss controls.
2. Render exactly one host adjacent to `router-outlet` in `AppComponent`; remove the page-local host atomically.
3. Move toast styles out of `word-lookup.component.scss` to the host stylesheet. Do not duplicate rules.
4. Define announcement policy:
   - success/info: non-interruptive `role="status"`/polite announcement;
   - actionable warning: polite by default unless immediate danger requires otherwise;
   - operation failure requiring immediate awareness: `role="alert"`/assertive.
5. Ensure only the new toast message is announced, not the entire existing list after every emission. Avoid redundant nested live regions.
6. Mark status icons decorative (`aria-hidden="true"`) and give each dismiss button a specific name such as `Dismiss success notification: {message}` or a concise type-aware equivalent.
7. Keep toast appearance from moving focus. After manual dismiss, leave focus on the dismiss button until removal, then allow normal browser fallback; do not move focus to arbitrary content.
8. Make timer behavior deterministic. Prefer an injectable scheduler/timer abstraction or explicit timer-handle management that tests can fake; clear handles on manual remove/clear and avoid timers mutating already-removed notifications.
9. Define route behavior: the single host remains mounted, queued toasts remain visible until dismissed/expired, and route navigation creates no duplicate announcement.
10. Make the host responsive by removing the fixed 20rem minimum at narrow widths and bounding it to viewport width.
11. Add `role="alert"` or polite status semantics to material inline asynchronous lookup/quiz/login/signup messages based on urgency. Do not turn validation hints or every static message into assertive alerts.
12. If the optional quiz selection/result improvement is small, expose selection state (native radio pattern or `aria-pressed` contract) and announce result heading/status. Otherwise record it as deferred.

### Accessibility Contract

- One shell host is present on every route and never duplicated.
- Success/info are polite; operation failures are urgent; static content is not needlessly live.
- Notifications never steal focus and each is independently dismissible by a named button.
- Decorative icons are silent.
- Navigation does not remove/reannounce surviving toasts unexpectedly.
- Important asynchronous inline errors/statuses are announced once with appropriate urgency.

### Tests to Add or Update

- App shell contains one ToastHost plus router outlet; remove obsolete generated title/heading expectations.
- Toast created on routed content appears in shell and remains represented after a simulated route-content replacement.
- Success/info status semantics and error alert semantics are correct.
- Dismiss control accessible name includes useful context and removes only its toast.
- Multiple toasts retain order and independent removal.
- Auto-removal at default/custom duration uses fake time and leaves no pending real timer.
- Manual remove/clear cancels scheduled removal safely.
- Route changes do not create another host or duplicate service subscription/announcement.
- Page-local toast markup is absent.
- Material inline errors/statuses have the chosen announcement semantics.

### Manual Verification

- Trigger add, Favorite, preferred-definition success/failure, multiple toasts, manual dismiss, expiry, and navigation while visible.
- Screen-reader check polite status versus urgent error and absence of repeated announcements.
- Keyboard dismiss without focus jumps.
- Test long toast at 320/375px, 200% zoom, and desktop.

### Runtime Gate

The user runs targeted app-shell, toast-host, toast-service, word-lookup, and any modified quiz/login/signup specs via the one-shot CLI flags. Expected: pass and exit with no real-time delay from toast duration. The user supplies route-transition and screen-reader results before phase 5.

### Risks

- Two hosts during migration would duplicate visuals and announcements.
- A live region around the whole list can reannounce existing notifications.
- Timer abstraction can accidentally alter production durations or removal ordering.
- Focus can be lost when a focused dismiss button auto-removes; manual dismissal behavior must be checked.
- Expanding inline alerts too broadly creates noisy screen-reader output.

### Rollback / Recovery Considerations

ToastHost is self-contained. If shell integration fails, revert host, shell, service timing, and page-host removal together; never leave both hosts. Inline feedback changes can be separately reverted if announcement testing exposes noise without affecting toast state.

### Definition of Done

- Exactly one shell-level toast host serves every route.
- Polite/urgent policy, accessible dismissal, deterministic timing, and responsive layout are tested.
- Page-local rendering/styles are removed.
- User confirms route persistence and announcement quality.

### Suggested Commit Intent

`fix(a11y): move notifications to application shell`

## 11. Phase 5 — UI String and Style Ownership Cleanup

### Objective

Finish R17-specific icon, text, focus, responsive, and stylesheet ownership cleanup without pretending valid source needs encoding repair or preemptively changing budgets.

### Why This Phase Exists

Earlier phases establish the final semantic structure and naturally move toast/dialog styles. Cleanup now avoids repeated churn and lets the later build measure the post-remediation CSS rather than the obsolete layout.

### Affected Files

- R17-touched templates and TS data containing user-facing emoji: dashboard, word lookup, quiz, toast host, dialog
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss`
- New toast/dialog SCSS files
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.scss` and `quiz.component.scss` only for verified responsive/focus fixes
- `VocabularyApp.UI/src/styles.scss` only for a genuinely shared utility such as visually-hidden content; avoid dumping component styles globally

### Implementation Steps

1. Reconfirm files are UTF-8 and do not rewrite encoding/BOM merely for consistency. Do not replace valid symbols because a terminal decoded them incorrectly.
2. Inventory emoji/symbols on the primary routes. For each, choose one:
   - remove when adjacent text already communicates meaning;
   - retain as decorative with `aria-hidden="true"` and separate text;
   - replace with existing inline SVG and accessible treatment where a visual icon helps;
   - retain meaningful text only when its cross-platform/AT behavior is deliberately acceptable.
3. Prefer text labels for arrows, spinner state, success/error words, and navigation where icons duplicate text. Do not add an icon dependency.
4. Confirm every R17-touched control has visible keyboard focus; add local `:focus-visible` styling only where Tailwind/native focus is insufficient.
5. Remove dead toast rules from word lookup and unused `slideOutRight` if still unreferenced. Remove duplication created by structural changes.
6. Keep toast styles with ToastHost and dialog styles with the dialog. Do not split letter-grid styles merely to chase source size.
7. Apply contained responsive fixes verified by the analysis: shell toast viewport bounds, dialog maximum height/scroll reachability, dashboard header wrapping/long username, and quiz action wrapping if still problematic.
8. Add reduced-motion handling only for R17-touched nonessential movement if it is a small local rule.
9. Record post-cleanup stylesheet source sizes for context, but make no claim about Angular emitted sizes.

### Accessibility Contract

- Decorative icons are silent and never form the sole accessible name.
- Meaning is conveyed in text and not by color/icon alone.
- Focus remains visible across touched controls.
- No source corruption or replacement characters are introduced.
- Narrow/zoomed layouts keep controls and dialog/toast content reachable.

### Tests to Add or Update

- Query critical icon-bearing controls/statuses to ensure meaningful text/accessibility names remain and decorative icons are hidden.
- Do not snapshot emoji glyph shapes.
- Retain a minimal string assertion only for deliberately important Unicode text if it guards a known regression.
- Add structural class/state tests only where responsive behavior depends on conditional template logic; CSS layout remains manual/build verification.

### Manual Verification

- Inspect dashboard, lookup, suggestions, saved words, dialog, toasts, and quiz for icon consistency and unintended screen-reader emoji announcements.
- Inspect principal screens for mojibake/replacement characters.
- Check focus visibility, reduced motion if implemented, 320/375px, tablet, desktop, landscape, and 200% zoom.

### Runtime Gate

No budget decision occurs here. The user runs any targeted specs changed in this phase and performs the visual/AT/responsive checks. Record current stylesheet ownership and defer production measurement to phase 7.

### Risks

- Broad symbol replacement can become visual redesign or remove useful cues.
- Moving styles can change Angular encapsulation/specificity.
- Globalizing styles to reduce a component budget can hide rather than solve ownership problems.
- Terminal output may again look corrupted if read without UTF-8; verify source/browser before changing files.

### Rollback / Recovery Considerations

Make icon/text changes separately reviewable from mechanical style relocation within the phase if the diff is large. Revert individual presentation decisions without reverting semantic controls or shell/dialog boundaries.

### Definition of Done

- No fake encoding repair or new icon library is introduced.
- Decorative/meaningful icon treatment is consistent on primary workflows.
- R17-owned styles live with their components; confirmed dead/duplicate rules are removed.
- Manual focus, screen-reader-symbol, responsive, and corruption checks pass.

### Suggested Commit Intent

`fix(ui): normalize accessible icons and style ownership`

## 12. Phase 6 — Angular Test Reliability and Frontend CI

### Objective

Repair static test/runner defects, establish a documented one-shot command, use user-run evidence to diagnose remaining non-termination, and add frontend tests to CI without speculative rewrites.

### Why This Phase Exists

The repository has a confirmed stale shell spec, real toast timers in tests, no one-shot package script, and no frontend CI. Runtime execution is required to distinguish watch-mode behavior, launcher/environment failure, pending timers, and actual lifecycle defects.

### Affected Files

- `VocabularyApp.UI/package.json`
- `VocabularyApp.UI/package-lock.json` only if npm script changes unexpectedly update metadata (normally not required)
- `VocabularyApp.UI/angular.json` only if a named CI test configuration is preferable and schema-supported
- `VocabularyApp.UI/src/app/app.component.spec.ts`
- `VocabularyApp.UI/src/app/services/toast.service.spec.ts`
- Other `VocabularyApp.UI/src/**/*.spec.ts` only when runtime evidence identifies a defect or preceding phases require coverage
- `.github/workflows/frontend-tests.yml` (new), or existing workflow only if combining is demonstrably cleaner
- `VocabularyApp.UI/README.md` is intentionally updated in phase 8, not here

### Implementation Steps

#### Static fixes justified before runtime

1. Replace stale AppComponent title/generated-heading assertions with shell expectations established in phase 4.
2. Refactor toast tests to use fake time/deterministic scheduler and finite subscriptions (`take(1)`, explicit cleanup, or equivalent), avoiding nested persistent subscriptions and real five/seven-second waits.
3. Add a package script such as `test:ci` that invokes Angular tests with `--watch=false --browsers=ChromeHeadless`. Keep `npm test` as the developer watch command unless a deliberate project decision changes it.
4. Decide whether flags belong directly in the script or a named Angular test configuration. Use the smallest configuration change that works with Angular 18; do not add another runner.
5. Ensure all R17 component tests from phases 1–5 are included by the ordinary full suite and do not rely on execution order.

#### Runtime diagnostic steps performed by the user

6. User runs `npm run test:ci`, records browser launch, spec failures, elapsed time, final summary, process exit, and exit code.
7. User runs a targeted toast-service/host subset to compare termination.
8. To prove failure behavior, create a temporary local failing assertion in a small spec, run `npm run test:ci`, confirm prompt nonzero exit, then restore the temporary edit before any commit. Codex must not create or run this probe; the user supplies the result.
9. If the full suite fails/hangs, user runs logical subsets using `--include` to isolate the responsible spec while preserving the same headless/no-watch mode.
10. Distinguish browser-launch failure, watch process, test failure, disconnect timeout, and pending async work from the recorded output.

#### Conditional fixes only after evidence

11. Fix only the isolated open timer/subscription/request/listener/fixture teardown defect. Do not mass-rewrite all tests.
12. Adjust Karma/browser timeouts or launcher flags only when logs prove an environment timing/launcher issue; document the evidence and narrow reason.
13. If ChromeHeadless is unavailable in the actual CI environment, choose the repository-supported browser setup based on CI evidence, not assumption.
14. Add a frontend workflow using the existing repository's checkout/setup/cache style, supported Node version, `npm ci`, and `npm run test:ci`. Avoid package installation/update.
15. Configure the workflow trigger/path policy consistently with backend CI. Do not add production build here unless intentionally part of the final gate and documented.

### Accessibility Contract

Not directly applicable to runtime configuration. This phase's contract is that all accessibility component tests are part of the reliable one-shot suite and failures cannot be hidden by a non-terminating watch process.

### Tests to Add or Update

- Accurate app-shell tests.
- Deterministic default/custom toast expiry and timer cancellation.
- Any teardown regression test justified by isolated runtime evidence.
- Verify the complete suite discovers R17 specs.
- Runner-level verification: successful suite exits zero; deliberate failure exits nonzero; both terminate.

### Manual Verification

- Review console output for skipped/disabled/focused specs.
- Confirm ChromeHeadless closes and PowerShell prompt returns.
- Confirm CI workflow runs on a representative branch/PR and reports a required/visible failure when a spec fails, according to repository policy.

### Runtime Gate

This is a hard evidence gate. The user must supply:

```powershell
cd VocabularyApp.UI
npm run test:ci
```

Expected: all specs pass, process terminates, exit code 0. The temporary deliberate-failure probe must terminate with nonzero exit and be reverted. If CI is added, its run must pass. Phase 7 must not treat test reliability as complete without these results.

### Risks

- Mistaking expected `ng test` watch behavior for an open-handle defect.
- “Fixing” non-termination by hiding failures, excluding specs, or increasing timeouts.
- CI browser availability may differ from local behavior.
- Earlier targeted specs may expose unrelated stale tests only in the full suite.
- Editing lockfiles without dependency changes creates noise.

### Rollback / Recovery Considerations

Split into two commits when conditional fixes are needed: (1) static spec/script changes, (2) evidence-driven lifecycle fix and CI. Workflow can be reverted independently without losing the local one-shot command. Never revert valid accessibility tests merely to obtain a green suite.

### Definition of Done

- Stale shell and timer tests are corrected.
- One documented package script runs the full suite headlessly once.
- User evidence proves zero-on-success, nonzero-on-failure, and termination.
- Any open-handle/launcher correction is tied to captured evidence.
- Frontend CI executes the same command successfully.

### Suggested Commit Intent

Primary: `test(ui): establish reliable one-shot Angular tests`

Optional second commit: `ci(ui): run Angular tests in continuous integration`

## 13. Phase 7 — SCSS Budget Verification and Evidence-Based Resolution

### Objective

Measure the final R17 stylesheet output, resolve an actual warning through ownership/dead-code cleanup where possible, and change the budget only with documented technical justification.

### Why This Phase Exists

The 2 kB warning and 4 kB error thresholds are known, but source byte size does not prove emitted output. Phases 4–5 naturally move/remove CSS, so measurement belongs after them and after the suite is reliable.

### Affected Files

- Actual stylesheet named by production build output
- Likely `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss`
- Toast/dialog SCSS only if named or duplicated
- `VocabularyApp.UI/angular.json` **conditional and last resort only**
- `Docs/Updates/R17-Accessibility-UI-Documentation-Completion.md` later records outcome; it is not created in this phase unless phase 9 has all evidence

### Implementation Steps

1. User runs the unchanged production build and supplies complete budget output, including component name, emitted size, threshold, and whether it is warning/error.
2. If clean, record that natural R17 ownership cleanup resolved or invalidated the historical warning. Do not touch budgets.
3. If a warning remains, map Angular's named component to source styles and inspect compiled contributors: duplicate selectors, unused animations, obsolete rules, overly broad nested output, and styles that belong to an already extracted host/dialog.
4. Make only justified CSS reductions/ownership corrections. Do not move CSS global solely to evade `anyComponentStyle`.
5. User reruns the production build and supplies output.
6. If cohesive, necessary styles still exceed 2 kB after cleanup, document why 2 kB is unjustifiably low for the measured component and propose the smallest deliberate `maximumWarning` adjustment. Preserve a meaningful `maximumError` gap.
7. Change `angular.json` only after user approval of that evidence-based exception, then user reruns build.
8. Record final emitted size, thresholds, warning/error state, and decision for the completion record.

### Accessibility Contract

CSS reduction must not remove visible focus, active combobox state, dialog containment/reachability, live notification visibility, reduced-motion behavior, or responsive access. Otherwise not applicable.

### Tests to Add or Update

No unit test should assert byte size. Retain component behavior tests; manually recheck state/focus/responsive presentation after CSS changes. The production build is the authoritative budget test.

### Manual Verification

- Compare dashboard, autocomplete active state, saved rows, dialog, toast types, focus rings, narrow layout, and 200% zoom before/after cleanup.
- Confirm no style leakage caused by moving selectors between encapsulated components.

### Runtime Gate

The user runs:

```powershell
cd VocabularyApp.UI
npm run build
```

Expected: successful production build with no unexplained SCSS budget warning. Any deliberate warning-threshold adjustment must be documented and the final build rerun. Do not proceed to docs with an unknown outcome.

### Risks

- Raising thresholds can conceal growth.
- Moving CSS globally can evade rather than solve the budget.
- Removing rules based on source search can miss dynamic class application.
- Angular output may name an unexpected component, invalidating assumptions about word lookup.

### Rollback / Recovery Considerations

Keep measured CSS cleanup separate from any conditional budget-config commit. Revert a config increase independently if later cleanup eliminates need. If visual regression appears, restore the necessary rule and reassess budget based on actual cohesive size.

### Definition of Done

- User-provided production output identifies the actual current state.
- Actual unnecessary CSS is removed without accessibility/layout regression.
- Build is clean, or a minimal deliberate budget exception is approved and documented.
- Final build succeeds and the budget outcome is ready for completion documentation.

### Suggested Commit Intent

`fix(ui): resolve measured component style budget`

If only documentation of a clean post-refactor build is needed, no source commit is required in this phase.

## 14. Phase 8 — Documentation Synchronization

### Objective

Update current-facing developer documentation and HTTP examples to match final routes, contracts, test/build commands, CI, and frontend architecture without altering historical records.

### Why This Phase Exists

Current documentation describes implemented vocabulary/quiz features as future work and examples call removed or incorrectly authenticated endpoints. Documentation is delayed until runtime commands and behavior are known.

### Affected Files

- `Docs/README.md`
- `Docs/FRONTEND-SUMMARY.md`
- `VocabularyApp.UI/README.md`
- `test-api.http`
- `VocabularyApp.WebApi/VocabularyApp.WebApi.http`
- Any other current-facing doc discovered to link to these commands/contracts

Historical and unchanged:

- `Docs/Vocabulary Builder Assessment.md/.pdf`
- `Docs/Vocabulary Builder — Plan of Action.md/.pdf`
- Existing R2/R3/R6 analysis, plan, review, validation, and completion records
- `Docs/Updates/R17-Accessibility-UI-Documentation-Analysis.md`
- This implementation plan

### Implementation Steps

1. Identify current authoritative docs explicitly; do not create a root README unless the repository owner requests it.
2. Update `Docs/README.md` project structure to include UI/tests and describe implemented authentication, dictionary lookup, vocabulary collection/favorites/preferred definition, and quiz capabilities.
3. Replace endpoint lists with controller-verified routes and correct authentication expectations.
4. Rewrite `Docs/FRONTEND-SUMMARY.md` from an old creation announcement into a factual current architecture/feature summary, including `/login`, `/signup`, `/dashboard`, `/vocabulary`, and `/quiz`. Do not advertise unavailable dashboard concepts as implemented routes.
5. Update `VocabularyApp.UI/README.md` with prerequisites, environment/base URL behavior, serve command, production build command, `npm test` watch purpose, reliable `npm run test:ci`, route/auth overview, test termination expectation, CI behavior, and deployment output. Remove unsupported `ng e2e` instructions unless actual infrastructure exists.
6. Replace stale `test-api.http` requests with variables and current authenticated examples: register/login, token use, lookup, vocabulary add/list/search/favorite/preferred-definition, quiz start/submit/history, profile/change-password/validate-token as useful. Do not include real secrets/tokens.
7. Replace `/weatherforecast` in `VocabularyApp.WebApi.http` with a small current smoke sequence or point it to the authoritative examples without duplication.
8. Validate JSON bodies against current DTO property names and controller routes; label auth requirements accurately.
9. Document the actual final SCSS outcome and test/CI command only where current-facing operational guidance needs it; detailed evidence belongs in completion record.
10. Check internal links and remove claims that HTTP examples constitute automated testing.

### Accessibility Contract

Not applicable to application interaction. Documentation should use clear headings, descriptive links, language-tagged code fences where helpful, and text rather than emoji-only status markers.

### Tests to Add or Update

No automated documentation test is required unless the repository already has one. Validate examples statically against `UsersController`, `WordsController`, `QuizController`, DTOs, `app.routes.ts`, `package.json`, `angular.json`, and workflow files. Do not execute HTTP requests in R17 documentation work.

### Manual Verification

- User reviews every documented command and endpoint against final source/runtime results.
- Confirm no production secret or usable JWT is included.
- Confirm historical documents remain unchanged and links render correctly.

### Runtime Gate

No new runtime command is introduced here. The user confirms documented `npm run test:ci` and `npm run build` results are the already verified phase 6/7 commands and approves route/API accuracy before phase 9.

### Risks

- Copying stale examples forward or documenting planned rather than actual behavior.
- Accidentally weakening R3's authenticated dictionary lookup requirement.
- Including fake but secret-like values that users may commit as real configuration.
- Rewriting historical records destroys audit context.

### Rollback / Recovery Considerations

Documentation files can be reverted independently. If one contract is uncertain, omit or mark it rather than guessing. Historical records must never be used as the rollback target for current-facing guidance.

### Definition of Done

- Current routes, auth rules, features, API examples, build/test commands, and CI behavior are accurately documented.
- Obsolete weather/word/profile examples and future-feature claims are removed.
- Historical R17 and earlier remediation records remain unchanged.

### Suggested Commit Intent

`docs: synchronize frontend routes tests and API examples`

## 15. Phase 9 — Final Accessibility Verification and Completion Record

### Objective

Re-run the full R17 evidence matrix, resolve only verified regressions in their owning areas, and create the completion record from user-supplied results.

### Why This Phase Exists

Component tests cannot prove screen-reader quality, focus visibility, mobile layout, production budget output, or process termination. R17 is complete only when all findings have a disposition and actual outcomes are recorded.

### Affected Files

- New, only after evidence: `Docs/Updates/R17-Accessibility-UI-Documentation-Completion.md`
- Conditional source/test/style/doc files only when final verification reveals a regression; fix in a small owning-area commit, rerun its gate, and document the deviation
- The analysis and implementation plan remain unchanged except for a genuine factual correction approved as documentation maintenance; completion should normally record deviations instead

### Implementation Steps

1. Freeze feature scope and collect final phase commits/results.
2. User runs the full one-shot Angular suite and production build; record commands, totals, duration, exit codes, and budget output.
3. Confirm frontend CI executes the same test command successfully.
4. User performs the complete keyboard workflow: login → dashboard → lookup/autocomplete → add/save → My Words/detail → Favorite → preferred-definition dialog → quiz.
5. Run the autocomplete matrix: Down, Up, Enter, Escape, Tab, mouse, rapid typing, empty existing results, API error, zoom/narrow viewport.
6. Run the dialog matrix: initial focus in loading/loaded states, forward/backward containment, Escape, backdrop, Close, Cancel, Save, failures, exact opener return.
7. Screen-reader smoke test: field labels, combobox state/active option, dialog name/modal state/radio labels, polite toast, error alert, Favorite target/action/state, quiz selection/result, and absence of decorative icon noise.
8. Responsive matrix: approximately 320px, 375px, tablet, desktop, landscape, 200% zoom, long username, word, definition, error, toast, and virtual keyboard where practical.
9. Inspect principal screens for replacement characters and mojibake. Treat any terminal-only rendering separately from browser/source evidence.
10. Re-audit no dead action, duplicate toast host, nested interactive control, stale current-facing route claim, focused/disabled spec, or excluded CI test remains.
11. If a gate fails, make the smallest fix in the responsible phase's files, have the user rerun targeted and full gates, and record the deviation. Do not expand product scope.
12. Create `R17-Accessibility-UI-Documentation-Completion.md` only when evidence is complete.

### Accessibility Contract

All contracts from phases 1–5 must hold together through the complete primary workflow, not only in isolated components.

### Tests to Add or Update

No speculative new suite is required. Add a regression test only for a concrete final-gate failure that can be automated. Manual screen-reader and layout findings remain recorded manual evidence.

### Manual Verification

The entire implementation-step matrix above is mandatory. Record assistive technology/browser/viewport used and any limitations; do not claim WCAG certification.

### Runtime Gate

Hard final commands supplied to the user:

```powershell
cd VocabularyApp.UI
npm run test:ci
npm run build
```

Expected: tests pass and terminate with exit 0; production build succeeds with no unexplained budget warning; frontend CI is green. Completion documentation waits for these plus the manual matrix.

### Risks

- Treating isolated test success as proof of integrated focus/announcement behavior.
- Recording intended rather than observed manual results.
- Fixing a late regression without rerunning both targeted and full gates.
- Creating completion documentation before CI/build/user evidence is available.

### Rollback / Recovery Considerations

Late fixes are separate small commits mapped to their owning phase. If a final accessibility contract cannot be satisfied safely, do not mark R17 complete; record it as a blocking issue rather than silently defer a required item. The completion document can be withheld until evidence exists.

### Definition of Done

- Full tests pass, terminate, and correctly signal failure.
- Production build succeeds with clean or explicitly justified budget state.
- Frontend CI passes.
- Keyboard, screen-reader, responsive, encoding, dialog, toast, and autocomplete matrices are completed from user evidence.
- Current-facing documentation is accurate.
- Completion record truthfully assesses every R17 definition-of-done item and any deferral/deviation.

### Suggested Commit Intent

`docs: record R17 accessibility remediation completion`

## 16. Cross-Phase Test Strategy

Tests are added with behavior, not deferred wholesale to phase 6:

- **Component DOM/interaction tests:** semantic tags, names/state, keyboard events, active option synchronization, focus lifecycle, toast roles, timer behavior.
- **Service tests:** ToastService ordering, removal, timing, and cleanup.
- **Shell integration tests:** one toast host, routed-content independence, no obsolete generated assumptions.
- **Full-suite runner checks:** discovery of every R17 spec, one-shot exit, correct success/failure code.
- **Manual-only evidence:** screen-reader announcement quality, actual browser focus trapping/restoration, visible focus, responsive layout, emoji pronunciation, virtual keyboard, and platform rendering.

No test should assert only that an ARIA string exists while ignoring behavior. No emoji glyph screenshot/snapshot is required. No new accessibility scanner or e2e framework is introduced unless separately approved as optional scope.

Until phase 6 creates `test:ci`, targeted phase commands use `npx ng test --watch=false --browsers=ChromeHeadless --include=...`. Codex never runs them; the user does. After phase 6, all phases use `npm run test:ci` for the full gate.

## 17. Manual Accessibility Verification Strategy

Manual verification proceeds incrementally:

- Phase 1: native dashboard/word-row controls and focus order.
- Phase 2: complete autocomplete keyboard/mouse/screen-reader behavior.
- Phase 3: dialog focus lifecycle and modal announcement.
- Phase 4: toast urgency, persistence, dismissal, and no repeated announcements.
- Phase 5: icon noise, focus visibility, zoom, and responsive layout.
- Phase 9: integrated primary workflow and full matrix.

Record browser, OS, screen reader/version, viewport, zoom, result, and defect for final verification. A smoke test is evidence of primary-flow usability, not a claim of complete WCAG conformance.

## 18. Runtime Build and Test Verification

Runtime evidence is supplied by the user and gates subsequent work:

1. **After phases 1–5:** targeted one-shot component/service tests plus the phase-specific manual interaction check.
2. **Phase 6:** full `npm run test:ci`; successful termination/zero exit; temporary intentional failure/nonzero exit; isolated diagnostics if needed; CI run.
3. **Phase 7:** baseline post-R17 `npm run build`; exact offender/size if warned; rebuild after evidence-based fix; final budget state.
4. **Phase 9:** final full suite, build, CI, and manual matrix.

Future Codex sessions must stop after reporting the exact user command and expected result. They may interpret the user's returned output and implement conditional fixes, but must not execute tests/builds themselves.

## 19. Git / Commit Strategy

- One primary commit per phase, with phases 2–4 never combined.
- Phase 6 may use two commits for static runner/spec work and CI/evidence-driven correction.
- Phase 7 separates CSS cleanup from a conditional budget-config change so the exception is auditable.
- Phase 9 late regression fixes use small owning-area commits before the completion record.
- Do not mix documentation synchronization with behavior changes.
- Before each commit, verify only phase-authorized files changed; preserve unrelated user changes.
- Do not commit the temporary deliberate-failure probe.

Suggested ordered intents are those in the phase overview. No commits are created during planning.

## 20. Documentation Update Strategy

Historical evidence remains immutable: assessments, the Plan of Action, R2/R3/R6 records, the R17 analysis, and this implementation plan. Current-facing documents—`Docs/README.md`, `Docs/FRONTEND-SUMMARY.md`, `VocabularyApp.UI/README.md`, and both HTTP files—are updated in phase 8 based on final source and user-verified commands.

The future completion record is additive. It records what actually happened, including deviations, rather than revising this plan after the fact. If later branches land R14–R16 before R17 implementation, re-audit affected phase files and document the changed architecture in completion/deviation notes.

## 21. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Multiple phases overlap WordLookup monolith | Strict sequential phases, targeted diffs/tests, two narrow extraction boundaries only |
| Async autocomplete state diverges from DOM/ARIA | One canonical option order, stable IDs, term/request correlation, behavior-first tests |
| Mouse selection breaks on blur | Explicit pointer/focus ordering tests and manual mouse check |
| Dialog focus implementation worsens access | Isolated component/contract, all close paths tested, mandatory manual focus gate |
| Saved rows become nested controls | Non-interactive wrapper with sibling detail/Favorite buttons and DOM test |
| Shell move duplicates toasts/announcements | Add shell host and remove page host atomically; test exactly one host/live event |
| Timers cause flaky/non-terminating tests | Deterministic timing and cancellation; isolate with user-run subsets |
| Watch mode mistaken for application open handle | Explicit no-watch command; capture exit/logs before conditional changes |
| CI launcher differs from local | Evidence-based launcher adjustment only; no timeout inflation without logs |
| SCSS threshold changed on source-size assumption | Build after natural cleanup; measure emitted output; config change last |
| Icon cleanup becomes redesign | Text/native semantics first; no new library; only R17-touched surfaces |
| R14–R16 scope absorbed | Keep APIs/paging/orchestration unchanged except minimal suggestion race guard |
| Docs describe plans instead of final behavior | Phase 8 after runtime stabilization; controller/route/script cross-check |

## 22. Deferred Work

- Save-for-later product design/persistence.
- Full `WordLookupComponent` decomposition beyond ToastHost and optional presentational dialog.
- Typed feature APIs, bearer interceptor/cookie analysis, centralized auth-error architecture.
- Bounded server-side collection paging/search/filtering and URL filter state.
- Analytics, preferences, admin, notes, spaced repetition, design system, routing redesign.
- Broad accessibility certification, automated scanner/e2e platform, and unrelated contrast/visual redesign work.
- Optional quiz semantics if phase 4 inspection shows they cannot be fixed as a small local change; document the exact deferral.

## 23. Final R17 Verification

R17 can close only when:

- Save for Later and all misleading click affordances are gone.
- Dashboard, saved-word detail, Favorite, search, autocomplete, dialog, toast dismiss, and quiz primary actions are keyboard usable.
- Combobox, dialog, toggle, and notification semantics match their actual interaction.
- One route-persistent shell toast host provides polite/urgent announcements without focus stealing or duplication.
- Important async errors/statuses are announced appropriately.
- Source remains valid UTF-8 and principal screens show no mojibake/replacement characters or disruptive decorative-icon announcements.
- `npm run test:ci` passes, returns zero, and terminates; an intentional failure returns nonzero and terminates.
- Frontend CI passes using the same command.
- `npm run build` succeeds with no unexplained budget warning; any exception is measured and justified.
- Current-facing developer docs and HTTP examples match final source.
- User-supplied keyboard, screen-reader, responsive, zoom, route, dialog, toast, and autocomplete results are recorded.

## 24. Planned Completion Record

During phase 9—never during planning—create:

`Docs/Updates/R17-Accessibility-UI-Documentation-Completion.md`

It must contain:

- phase/commit summary and deviations from this plan;
- files and durable extractions created;
- semantic, keyboard, focus, feedback, icon, responsive, and documentation changes;
- tests added/updated and user-supplied targeted/full results;
- test success/failure exit and termination evidence;
- final production build and exact SCSS budget outcome;
- frontend CI result;
- manual keyboard/screen-reader/device/zoom/encoding results supplied by the user;
- deferred optional/out-of-scope work;
- final assessment of every R17 definition-of-done item.

Do not claim a manual check passed without user evidence. If required evidence is missing, keep the completion record in draft or do not create it.

## 25. Final Definition of Done

The implementation plan is actionable when each future session can implement one phase without redesigning state, semantics, focus, tests, or runtime gates. R17 implementation itself is done only when all required phase definitions of done and final gates are satisfied, runtime-dependent findings have evidence-based outcomes, R14–R16 have not been absorbed, and the completion record accurately reflects the delivered result.

The intended final product state is:

- no visible dead action;
- native semantic navigation/actions wherever possible;
- keyboard- and screen-reader-usable primary workflows;
- correct combobox and modal focus contracts;
- consistent route-level feedback with appropriate announcements;
- valid UTF-8 with accessible icon treatment;
- a terminating, CI-enforced Angular unit suite;
- a clean or deliberately justified production style budget;
- current developer documentation and API examples;
- a truthful, evidence-backed R17 completion record.
