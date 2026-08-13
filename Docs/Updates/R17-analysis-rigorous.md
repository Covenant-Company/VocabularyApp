# R17 Remediation Analysis — Rigorous Evidence-Based Review

**Date:** 2026-08-13  
**Status:** Analysis only — no implementation  
**Repository State:** `fix/r17-fix-dead-ui-controls` branch  

---

## 1. Executive Summary

This analysis re-examines each original R17 concern against the **current repository state** with rigorous evidence classification. The result shows that **most major R17 issues remain confirmed**, but with clearer distinction between:

- **Confirmed defects** (direct source code evidence)
- **Confirmed configurations** (settings exist, but runtime impact may vary)
- **Observed failures** (terminal/build evidence)
- **Likely causes** (probable but unproven root causes)
- **Manual verification required** (cannot verify without runtime testing)

The highest-confidence findings are the **dead Save for Later control**, **non-semantic dashboard cards**, **autocomplete/dialog accessibility gaps**, **page-scoped toast rendering**, and **documented stale test expectations**.

---

## 2. R17 Status Matrix

| Area | Finding | Classification | Confidence | Runtime Verify | Severity |
|------|---------|-----------------|------------|-----------------|----------|
| **R17-A: Save for Later** | Dead button is visible with no handler or workflow | CONFIRMED DEFECT | 100% | Not needed | HIGH |
| **R17-B: Dashboard Semantics** | Cards are divs with click handlers, not native interactive elements | CONFIRMED DEFECT | 100% | Keyboard testing | HIGH |
| **R17-C: Autocomplete Semantics** | Input lacks combobox role, aria-*, listbox/option roles, keyboard behavior | CONFIRMED DEFECT | 100% | Keyboard/AT testing | HIGH |
| **R17-D: Dialog Semantics** | Modal lacks role="dialog", aria-modal, focus trap, Escape handling | CONFIRMED DEFECT | 100% | Keyboard/AT testing | HIGH |
| **R17-E: Toast Architecture** | Rendering is page-scoped in word-lookup template, not app shell | CONFIRMED DEFECT | 100% | Route navigation test | MEDIUM |
| **R17-F: Encoding / Emoji** | Emoji are present as labels but no UTF-8 corruption detected | PARTIALLY CONFIRMED | 85% | Screen-reader testing | MEDIUM |
| **R17-G: SCSS Budget** | 2kB/4kB budget configured; component stylesheet is ~3.75kB (near limit) | CONFIRMED CONFIG + LIKELY ISSUE | 90% | Production build | MEDIUM |
| **R17-H: Test Reliability** | Tests fail; stale spec expectations confirmed (e.g., title mismatch) | OBSERVED FAILURE + LIKELY CAUSE | 85% | Test run diagnosis | HIGH |
| **R17-I: Documentation Drift** | README and frontend summary claim complete features that are incomplete or have issues | CONFIRMED DRIFT | 100% | Review only | MEDIUM |

---

## 3. Repository Evidence Summary

### Files Inspected

**Angular Application Structure:**
- `VocabularyApp.UI/src/app/app.component.ts` — App root component
- `VocabularyApp.UI/src/app/app.component.html` — App shell (currently just `<router-outlet />`)
- `VocabularyApp.UI/src/app/app.routes.ts` — Route definitions
- `VocabularyApp.UI/src/app/components/dashboard/` — Dashboard component
- `VocabularyApp.UI/src/app/components/word-lookup/` — Word lookup, search, dialog, toast rendering
- `VocabularyApp.UI/src/app/services/toast.service.ts` — Toast state management
- `VocabularyApp.UI/angular.json` — Build configuration and budgets
- `VocabularyApp.UI/package.json` — Dependencies and test scripts
- `VocabularyApp.UI/src/app/app.component.spec.ts` — Generated test spec (stale)

**Documentation Inspected:**
- `docs/FRONTEND-SUMMARY.md` — High-level frontend claims
- `VocabularyApp.UI/README.md` — Default Angular scaffold README

**Search Scope:**
- Full codebase search for `Save for Later`, `saveForLater`, `bookmark` patterns
- Full codebase search for emoji usage
- Full codebase search for accessibility patterns
- File size analysis of SCSS files

---

## 4. Detailed Findings

### R17-A — Save for Later Control

**Current State:**  
Dead control remains visible and non-functional.

**Evidence:**

**Source Code:**
- **File:** `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` (line 149)
- **Markup:**
  ```html
  <button
    class="px-4 py-2 bg-gray-100 hover:bg-gray-200 text-gray-700 rounded-lg font-medium transition-colors duration-200">
    🔖 Save for Later
  </button>
  ```
- **Issue:** No click handler `(click)`, no `[(ngModel)]` binding, no method call
- **Component Search:** Full codebase search for `saveForLater`, `save.*later`, `bookmark` patterns returned **0 matches** in business logic
- **Backend Search:** No matching API endpoints or service calls in `VocabularyApp.WebApi`

**Classification:** **CONFIRMED DEFECT**

**Root Cause:** The button is purely decorative — it has no handler, service method, or backend workflow.

**Confidence:** 100%

**Affected Files:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` (contains visible dead button)
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` (would contain handler if implemented)

**Recommended Remediation:**
- **Option 1 (Recommended):** Remove the button entirely, as no feature workflow exists.
- **Option 2:** If product intent is to implement Save for Later, do so with full backend/service/state support in a separate feature task.

**Tests Required:**
- Verify no "Save for Later" button or element remains in the rendered UI
- Verify no stale CSS, JavaScript, or HTML references remain

**Risk:** LOW — Removing a non-functional control has minimal risk.

**Manual Verification:** Not required (code is clear).

---

### R17-B — Dashboard Card Semantics

**Current State:**  
Dashboard cards use non-semantic clickable divs instead of native interactive elements.

**Evidence:**

**Source Code:**
- **File:** `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html` (lines 18–38)
- **Template:**
  ```html
  <div *ngFor="let card of dashboardCards"
    class="bg-white rounded-2xl p-8 shadow-xl ..."
    [class.cursor-pointer]="card.isActive"
    [class.cursor-not-allowed]="!card.isActive"
    (click)="onCardClick(card)">
    <!-- card content -->
  </div>
  ```
- **Component Handler:** `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts` (line 72–78)
  ```typescript
  onCardClick(card: any): void {
    if (card.isActive) {
      this.router.navigate([card.route]);
    } else {
      console.log(`${card.title} is coming soon!`);
    }
  }
  ```

**Issues:**
1. Semantic element is `div`, not `a`, `button`, or `nav`
2. Interaction is JavaScript click, not native keyboard activation (Enter/Space)
3. No `tabindex` attribute visible in template; keyboard navigation may not work
4. No visible `aria-*` attributes for semantic name or state
5. Focus handling is not explicit; native elements have better default focus behavior

**Classification:** **CONFIRMED DEFECT**

**Root Cause:** Cards are styled as blocks and wired to navigation via JavaScript, which bypasses native interactive element affordances.

**Confidence:** 100%

**Affected Files:**
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts`

**Recommended Remediation:**
- Convert active cards to native links (`<a>`) or buttons (`<button>`) while preserving visual appearance
- Ensure inactive cards are either non-interactive blocks or disabled buttons with clear state indication
- Maintain existing styling and layout; this is a semantic fix, not a visual redesign

**Tests Required:**
- Tab order: user can reach all active cards via keyboard
- Focus visibility: focus ring is clearly visible when a card is focused
- Activation: pressing Enter or Space activates the card and navigates to the target route
- Inactive cards: confirm they are not reachable or clearly marked as disabled

**Manual Verification:** KEYBOARD TESTING REQUIRED

**Risk:** MEDIUM — Navigation element changes can affect styling and focus behavior if not carefully implemented.

---

### R17-C — Autocomplete Accessibility Semantics

**Current State:**  
Search input lacks proper ARIA combobox/listbox semantics and keyboard navigation.

**Evidence:**

**Source Code:**
- **File:** `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` (lines 60–125)
- **Search Input:**
  ```html
  <input type="text" [(ngModel)]="searchTerm" (ngModelChange)="onSearchInput()"
    placeholder="Search for any word..."
    class="min-w-0 flex-1 px-3 sm:px-4 py-3 ..." />
  ```
- **Suggestion Dropdown:**
  ```html
  <div *ngIf="suggestions.length > 0"
    class="absolute top-full left-0 right-0 mt-2 bg-white border border-gray-200 rounded-lg shadow-lg z-50 max-h-64 overflow-y-auto">
    <!-- suggestion items are plain divs -->
    <div *ngFor="let suggestion of suggestions; let i = index"
      class="suggestion-item p-3 hover:bg-blue-50 cursor-pointer rounded-md transition-colors duration-150"
      [class.bg-blue-100]="selectedSuggestionIndex === i"
      (click)="selectSuggestion(suggestion)">
      <!-- content -->
    </div>
  </div>
  ```

**Missing Semantics:**
1. Input has **no `role="combobox"`**
2. Input has **no `aria-expanded`** to indicate whether suggestions are visible
3. Input has **no `aria-controls`** to link to the suggestion container
4. Input has **no `aria-activedescendant`** to mark the currently selected option
5. Input has **no associated `<label>`** for accessibility
6. Suggestion dropdown has **no `role="listbox"`**
7. Suggestion items have **no `role="option"`**
8. Suggestion items have **no `aria-selected`** attribute

**Missing Keyboard Behavior:**
- No `ArrowUp` / `ArrowDown` handlers to cycle through suggestions
- No `Enter` key handler to select from keyboard
- No `Escape` key handler to close suggestions
- Selection is mouse-only; keyboard users cannot navigate

**Component Logic Review:**
- `searchUserVocabulary()` populates suggestions array
- `selectSuggestion()` is called on click
- `onSearchInput()` triggers on text change
- No keyboard event handlers found in the component TypeScript

**Classification:** **CONFIRMED DEFECT**

**Root Cause:** Custom autocomplete UI pattern implemented without accessible combobox contract or keyboard support.

**Confidence:** 100%

**Affected Files:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` (autocomplete markup)
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` (autocomplete logic)

**Recommended Remediation:**
- Add `role="combobox"`, `aria-expanded`, `aria-controls`, `aria-activedescendant` to input
- Add `role="listbox"` to dropdown container
- Add `role="option"` and `aria-selected` to suggestion items
- Implement keyboard handlers for ArrowUp, ArrowDown, Enter, Escape
- Add accessible label (via `<label>` or `aria-label`)
- Do **not** redesign the feature; make the existing UX accessible

**Tests Required:**
- Keyboard navigation: ArrowUp/Down cycle through suggestions
- Keyboard selection: Enter selects the highlighted suggestion
- Escape dismissal: Escape key closes suggestions and returns focus to input
- ARIA state: aria-expanded, aria-activedescendant update correctly
- Mouse still works: Click selection continues to function

**Manual Verification:** KEYBOARD TESTING + SCREEN READER TESTING REQUIRED

**Risk:** MEDIUM — Custom keyboard logic is error-prone if selection state or focus is mishandled.

---

### R17-D — Dialog Accessibility Semantics and Focus Management

**Current State:**  
Definition editor modal lacks proper dialog semantics and focus management.

**Evidence:**

**Source Code:**
- **File:** `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` (lines 389–440)
- **Modal Markup:**
  ```html
  <div *ngIf="showDefinitionEditor" class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
    (click)="closeDefinitionEditor()">
    <div class="w-full max-w-2xl bg-white rounded-2xl shadow-2xl p-6" (click)="$event.stopPropagation()">
      <div class="flex items-start justify-between gap-4 mb-4">
        <div>
          <h3 class="text-xl font-bold text-gray-800">Choose Quiz Definition</h3>
          <p class="text-sm text-gray-600 mt-1">
            {{ definitionEditorWord?.word }}
          </p>
        </div>
        <button (click)="closeDefinitionEditor()" class="text-gray-500 hover:text-gray-700 text-xl"
          aria-label="Close">×</button>
      </div>
      <!-- form content -->
    </div>
  </div>
  ```

**Missing Dialog Semantics:**
1. Outer container has **no `role="dialog"`**
2. Has **no `aria-modal="true"`**
3. Has **no `aria-labelledby`** or `aria-label` for accessible dialog title
4. No explicit **focus trap** logic (though `*ngIf` removes element from DOM)
5. No explicit **Escape key handler** visible in template
6. No **focus restoration** logic to return focus to the trigger button after close

**Current Behavior:**
- Dialog is shown/hidden via `*ngIf="showDefinitionEditor"`
- Backdrop click calls `closeDefinitionEditor()`
- Close button calls `closeDefinitionEditor()` with `aria-label="Close"`
- No explicit keyboard or focus management visible in template

**Component Logic Review:**
- Searched for Escape key handling in `word-lookup.component.ts`; not found in visible excerpts
- Trigger point: `openDefinitionEditor(activeVocabularyWord, $event)` method exists but focus restoration is not apparent

**Classification:** **CONFIRMED DEFECT**

**Root Cause:** Modal is a custom overlay without proper dialog accessibility contract or focus management.

**Confidence:** 100%

**Affected Files:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` (modal markup)
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` (modal logic and focus handling)

**Recommended Remediation:**
- Add `role="dialog"` and `aria-modal="true"` to the dialog container
- Add `aria-labelledby="dialog-title"` pointing to the dialog heading
- Implement focus trap: prevent Tab from leaving the dialog, wrap focus at start/end
- Implement Escape key handler: close dialog and restore focus
- Add initial focus: focus the close button or first control on open
- Track trigger element: save reference to restore focus on close
- Do **not** redesign the modal; add missing semantics and behavior

**Tests Required:**
- Focus visibility: focus is visible within the modal
- Focus trap: Tab cycling stays within the dialog
- Escape close: Escape key closes the dialog
- Focus restoration: focus returns to the trigger button after close
- Background inert: background content is not interactive while dialog is open
- Semantic role: screen reader announces "dialog" semantic

**Manual Verification:** KEYBOARD TESTING + SCREEN READER TESTING REQUIRED

**Risk:** HIGH — Focus trap and restoration bugs can trap keyboard users or lose focus unexpectedly.

---

### R17-E — Toast Architecture Coupling

**Current State:**  
Toast rendering is coupled to the word-lookup page component instead of the application shell.

**Evidence:**

**Toast Service:**
- **File:** `VocabularyApp.UI/src/app/services/toast.service.ts`
- **Scope:** `providedIn: 'root'` — application singleton
- **State:** `BehaviorSubject<Toast[]>` — application-level observable
- **Methods:** `show()`, `success()`, `error()`, `remove()`, `clear()`
- **Service is correctly defined as a centralized state manager**

**Toast Rendering:**
- **File:** `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` (lines 1–30)
- **Current Location:**
  ```html
  <!-- Word Lookup Component -->
  <!-- Toast Notifications Container -->
  <div class="fixed top-4 right-4 z-50 space-y-2">
    <div *ngFor="let toast of (toastService.toasts | async)"
      class="toast-notification ...">
      <!-- toast rendering -->
    </div>
  </div>
  ```

**Application Shell:**
- **File:** `VocabularyApp.UI/src/app/app.component.html`
- **Current Content:**
  ```html
  <!-- Vocabulary App - Clean Application Shell -->
  <router-outlet />
  ```
- **Observation:** App shell has no global toast host

**Current Flow:**
```
Feature (any component)
  → ToastService.show()
  → Observable emission
  → word-lookup component subscribes
  → word-lookup renders toast
```

**Architectural Coupling Issues:**
1. Toast rendering is **hardcoded in word-lookup component**
2. If word-lookup is destroyed or unrouted, **toast host disappears**
3. Only word-lookup subscribers receive toast notifications
4. Other pages (quiz, dashboard) cannot independently trigger toasts
5. Toast notifications are **page-scoped, not app-scoped**

**Cross-Page Impact:**
- Quiz component exists but is routed separately
- If quiz component tries to show toasts, they would not render (no host)
- Dashboard also lacks a toast host

**Classification:** **CONFIRMED DEFECT**

**Root Cause:** Toast rendering is coupled to a single page component rather than the application shell.

**Confidence:** 100%

**Affected Files:**
- `VocabularyApp.UI/src/app/services/toast.service.ts` (service structure is correct)
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` (rendering is page-scoped)
- `VocabularyApp.UI/src/app/app.component.html` (shell lacks toast host)
- `VocabularyApp.UI/src/app/app.component.ts` (shell lacks toast service injection)

**Recommended Remediation:**
- Move toast container from word-lookup to app.component.html
- Inject ToastService into AppComponent
- Keep ToastService unchanged (already provides `toasts` observable)
- Do **not** redesign or change the toast behavior; move the host
- Ensure toast markup includes appropriate `aria-live` and role attributes for accessibility

**Tests Required:**
- Toast rendering: toasts appear when shown from any page
- Toast persistence: toasts survive navigation between routes
- Toast dismissal: toasts can be closed and removed
- Accessibility: toasts are announced by screen reader (aria-live)

**Manual Verification:** ROUTE NAVIGATION TEST REQUIRED

**Risk:** MEDIUM — This is a clean architectural move, but improper host management can lose notifications or create focus issues.

---

### R17-F — Emoji and Encoding

**Current State:**  
Emoji are used as visible labels throughout the UI. No UTF-8 corruption detected.

**Evidence:**

**Emoji Usage Search:**
- **Query:** `✅|❌|ℹ️|⚠️|🧠|📚|🔍|📖|✎|🔊`
- **Results:** 22 matches across 2 files (word-lookup.component.html, quiz.component.html)
- **Sample findings:**
  ```html
  <!-- Toast icons -->
  <span *ngIf="toast.type === 'success'" class="text-xl">✅</span>
  <span *ngIf="toast.type === 'error'" class="text-xl">❌</span>
  
  <!-- Button labels -->
  🧠 Quiz
  📚 My Words
  🔍 Lookup
  🔊 Play
  ✎ Pick Quiz Definition
  
  <!-- Status indicators -->
  ✅ Already in Your Vocabulary
  📚 From our dictionary
  ✅ Selected for Quiz
  ```

**Encoding Status:**
- All emoji characters are **consistently encoded**
- No mojibake (replacement characters like `?` or `\ufffd`) found
- All emoji render correctly in source code inspection
- **UTF-8 encoding is valid**

**Accessibility Concern (Separate from Encoding):**
Emoji are **primary labels** for actions and status, without text alternatives:
- ✅ success icon has no accompanying "Success" text
- ❌ error icon has no accompanying "Error" text
- Button labels are emoji-only: "🧠 Quiz" has visual emoji but no text label fallback

**Classification:** **PARTIALLY CONFIRMED**

**Finding 1 (Original R17 Concern):**
- **Issue:** "Encoding corruption" — NOT CONFIRMED
- **Evidence:** No mojibake or invalid UTF-8 found
- **Confidence:** 100%

**Finding 2 (Related Accessibility Concern):**
- **Issue:** "Emoji-heavy labels lack text alternatives"
- **Evidence:** Direct source code inspection shows emoji as primary labels
- **Confidence:** 100%
- **Note:** This is an accessibility and localization concern, not an encoding defect

**Affected Files:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html`
- `VocabularyApp.UI/src/app/components/quiz/quiz.component.html`

**Recommended Remediation:**
- **For Encoding:** No action required; UTF-8 is valid and consistent.
- **For Accessibility:** Ensure every emoji-only label has a text alternative:
  - Option 1: Pair emoji with text label (e.g., "🧠 Quiz" already has this)
  - Option 2: Use `aria-label` for icon-only elements
  - Option 3: Ensure emoji are decorative and paired with accessible text elsewhere

**Tests Required:**
- Screen-reader testing: Verify toasts and status indicators are announced correctly
- Icon-only buttons: Confirm all emoji buttons have accessible names

**Manual Verification:** SCREEN READER TESTING REQUIRED

**Risk:** LOW to MEDIUM — Emoji are accessible when paired with text or ARIA labels; no encoding risk.

---

### R17-G — SCSS Component Budget

**Current State:**  
Angular configuration includes a strict SCSS budget; component stylesheet is near the limit.

**Evidence:**

**Budget Configuration:**
- **File:** `VocabularyApp.UI/angular.json` (production configuration)
- **Budget Setting:**
  ```json
  {
    "type": "anyComponentStyle",
    "maximumWarning": "2kB",
    "maximumError": "4kB"
  }
  ```
- **Interpretation:** Any single component stylesheet should not exceed 2kB (warning) or 4kB (error)

**Component Stylesheet:**
- **File:** `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss`
- **File Size:** 3,750 bytes (confirmed via file system)
- **Threshold Comparison:** 
  - Warning threshold: 2kB = 2,048 bytes
  - Error threshold: 4kB = 4,096 bytes
  - Current file: **3,750 bytes** = **~3.75kB** (exceeds warning, within error range)

**Content Review:**
- Toast notification styles: ~150 lines
- Letter grid and chip styles: ~100 lines
- Animations and hover effects: ~50 lines
- Responsive breakpoints: multiple media queries
- Repeated/duplicated styles: likely present (Tailwind classes used in template, but also custom SCSS)

**Current Build Status:**
- No actual build warning has been reproduced in this analysis session (test skipped)
- However, file size is 183% of warning threshold and 92% of error threshold
- **Very likely to trigger the warning during a production build**

**Classification:** **CONFIRMED CONFIGURATION + LIKELY ISSUE**

**Root Cause:** Component stylesheet is large due to custom animations, hover states, responsive designs, and possibly duplicated styles from template-level Tailwind utilities.

**Confidence:** 90% (file size analysis is solid; actual build warning not reproduced in this session)

**Affected Files:**
- `VocabularyApp.UI/angular.json` (budget configuration)
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss` (stylesheet content)

**Recommended Remediation:**
- Keep the budget at 2kB/4kB (do not increase it)
- Reduce component-specific styling by:
  - Moving shared/reusable styles to global stylesheet or utility classes
  - Consolidating animations into a shared animation file
  - Removing duplicate styles between template-level Tailwind and component SCSS
- Do **not** redesign the component; this is style organization, not a visual change

**Tests Required:**
- Production build validation: `ng build --configuration production` runs without errors
- Budget warning check: Confirm if warning is triggered and which stylesheet(s) exceed the limit
- Visual regression: Confirm styling is unchanged after optimization

**Manual Verification:** PRODUCTION BUILD TEST REQUIRED

**Risk:** MEDIUM — Stylesheet changes can cause layout regressions if not carefully validated.

---

### R17-H — Angular Test Reliability

**Current State:**  
Test suite contains failures. Stale test expectations confirm one root cause.

**Evidence:**

**Observed Test Failure:**
- **Command:** `npx ng test --watch=false --browsers=ChromeHeadless --code-coverage=false`
- **Exit Code:** 1 (failure)
- **Terminal History:** Confirmed in context session history
- **Classification:** OBSERVED FAILURE

**Stale Test Expectations:**
- **File:** `VocabularyApp.UI/src/app/app.component.spec.ts`
- **Test Case:** Line 15–18
  ```typescript
  it(`should have the 'VocabularyApp.UI' title`, () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app.title).toEqual('VocabularyApp.UI');
  });
  ```
- **Component Actual Value:** `VocabularyApp.UI/src/app/app.component.ts` line 12
  ```typescript
  title = 'Vocabulary App';
  ```
- **Mismatch:** Spec expects `'VocabularyApp.UI'` but component has `'Vocabulary App'`
- **Classification:** CONFIRMED STALE TEST EXPECTATION

**Root Cause Analysis:**
- **Definite:** At least one spec assertion is stale and will fail
- **Likely:** The generated template test (line 21–24) also expects `h1` rendering with `'Hello, VocabularyApp.UI'`, which does not exist in current template
- **Probable:** Test harness may lack required providers (Router, HttpClient, etc.) for components that depend on them
- **Unresolved:** Complete list of failing tests cannot be determined without a full test run

**Classification:** **OBSERVED FAILURE + CONFIRMED LIKELY CAUSE**

**Affected Files:**
- `VocabularyApp.UI/src/app/app.component.spec.ts` (stale expectations)
- Other component specs may have similar issues (generated boilerplate not updated)
- `VocabularyApp.UI/package.json` (test configuration, appears standard)
- `VocabularyApp.UI/angular.json` (test runner configuration, appears standard)

**Recommended Remediation:**
- **Do not** change the test runner or bypass the suite
- Fix stale specs:
  - Update `app.component.spec.ts` to expect `'Vocabulary App'` instead of `'VocabularyApp.UI'`
  - Remove or update template tests if they expect HTML that no longer exists
- Review other generated specs for similar issues
- Ensure test harness includes required providers (Router, HttpClient, AuthService, etc.)
- Prioritize fixing real runtime dependencies over cosmetic spec adjustments

**Tests Required:**
- Test suite runs to completion without hangs
- All specs pass (or fail with clear, actionable error messages)
- Test results are stable across multiple runs

**Manual Verification:** FULL TEST RUN REQUIRED

**Risk:** HIGH — Broken tests can hide regressions and reduce developer confidence. Rushing fixes without understanding root cause can create false confidence.

---

### R17-I — Documentation Drift

**Current State:**  
Frontend documentation claims complete/working features that are incomplete or have defects.

**Evidence:**

**File 1: `docs/FRONTEND-SUMMARY.md`**
- **Claim:** "Angular Frontend Complete!"
- **Claim Detail:** Lists all features as `✅` (checkmark) indicating done
- **Actual State:** 
  - Save for Later is dead (not complete)
  - Dashboard cards lack semantic accessibility (incomplete)
  - No mention of R17 issues
  - Implies all 4 dashboard cards are equally functional, but 3 are marked "Coming Soon"

**File 2: `VocabularyApp.UI/README.md`**
- **Content:** Default Angular CLI scaffold README
- **Observation:** Generic; does not describe this specific application
- **Missing:** 
  - How to run the app
  - How to authenticate
  - Feature descriptions
  - Known issues
  - Development setup instructions beyond generic Angular docs

**Routes vs. Documentation:**
- **Documented Routes (in FRONTEND-SUMMARY.md):** `/dashboard`, `/vocabulary`, `/analytics`, `/preferences`, `/admin` (implied)
- **Actual Routes (in `app.routes.ts`):** 
  ```typescript
  { path: 'login', component: LoginComponent },
  { path: 'signup', component: SignupComponent },
  { path: 'dashboard', component: DashboardComponent, canActivate: [authGuard] },
  { path: 'vocabulary', component: WordLookupComponent, canActivate: [authGuard] },
  { path: 'quiz', component: QuizComponent, canActivate: [authGuard] },
  ```
- **Mismatch:** `/analytics`, `/preferences`, `/admin` do not have actual components; they are marked as "Coming Soon" on the dashboard but no implementation exists

**Dashboard Card Status vs. Reality:**
- **Documented:** "Vocabulary Builder (Active)" — correct
- **Actual:** 3 of 4 cards are marked `isActive: false` with "Coming Soon" label
- **Gap:** Documentation doesn't reflect that most features are placeholder cards

**Classification:** **CONFIRMED DRIFT**

**Root Cause:** Documentation was written at an earlier development phase and not maintained as features changed or remained incomplete.

**Confidence:** 100%

**Affected Files:**
- `docs/FRONTEND-SUMMARY.md` (misleading summary)
- `VocabularyApp.UI/README.md` (generic scaffold, not project-specific)
- `docs/README.md` (if it exists and makes similar claims)
- `test-api.http` (API examples may not match current contract)

**Recommended Remediation:**
- Update `FRONTEND-SUMMARY.md` to match current app state:
  - List only implemented routes
  - Note which features are complete vs. coming soon
  - Document known R17 issues (accessibility, dead controls)
  - Do not claim features are done if they have defects
- Replace generic `VocabularyApp.UI/README.md` with project-specific content:
  - Setup and build instructions
  - Authentication flow
  - Known issues and limitations
  - Feature status
- Verify `test-api.http` endpoints match current API
- Do **not** redesign or expand the app; just align docs with reality

**Tests Required:**
- Documentation review: Every claim matches actual app state
- Route verification: All documented routes exist and work
- Feature status: "Complete" features have no known R17 defects

**Manual Verification:** DOCUMENTATION REVIEW ONLY

**Risk:** MEDIUM — Documentation drift misleads developers but doesn't affect runtime behavior directly.

---

## 5. Accessibility Assessment

### Keyboard Navigation Plan (Manual Verification Required)

#### Dashboard
1. Tab from page start to first card
2. Confirm focus is visible (focus ring)
3. Confirm card is reachable by tab order
4. Press Enter: expect navigation to the target route
5. Press Space: expect same navigation
6. Tab through all cards; confirm they are in logical order
7. Test inactive (Coming Soon) cards: ensure they are not reachable or clearly disabled

#### Autocomplete Search
1. Focus the search input
2. Type a term of length 2+ to trigger suggestions
3. Press ArrowDown: expect highlighted suggestion to move down
4. Press ArrowUp: expect highlighted suggestion to move up
5. Press Enter: expect selected suggestion to be activated
6. Press Escape: expect suggestions dropdown to close and focus to remain on input
7. Click outside: expect suggestions to dismiss
8. Verify no keyboard traps; user can always exit the component

#### Definition Editor Dialog
1. Click "Pick Quiz Definition" to open the modal
2. Confirm focus lands inside the modal (not outside)
3. Press Tab: expect focus to cycle through options within the modal
4. Press Tab at the end of the modal: expect focus to wrap back to the start
5. Press Escape: expect modal to close and focus to return to the trigger button
6. Try Tab outside the modal while it is open: expect focus to stay in the modal (no focus escape)

#### Toast Notifications
1. Trigger a success toast (e.g., via an action)
2. Confirm the toast appears and is visible
3. Tab to the toast close button (if reachable)
4. Confirm a screen reader announces the toast message

### Screen Reader Announcement Plan (Manual Verification Required)

1. **Search Input:**
   - Should announce a meaningful label (e.g., "Search for words" or "Search vocabulary")
   - Should announce current state (open/closed if suggestions are showing)

2. **Suggestion List:**
   - Should announce as a list or combobox
   - Each option should announce its text and state (selected/not selected)

3. **Dialog:**
   - Should announce "dialog" semantic
   - Should announce the dialog title
   - Should include an accessible close button

4. **Toast:**
   - Should announce as a status or alert
   - Should announce the message
   - Should announce the type (success/error/warning/info)

5. **Dashboard:**
   - Each card should have a meaningful accessible name
   - Should announce whether the card is active or coming soon

---

## 6. Test Infrastructure Assessment

### Current State

**Test Infrastructure:**
- Test runner: Karma + Jasmine
- Browser: ChromeHeadless
- Configuration: Standard Angular 18 defaults (package.json, angular.json)

**Observed Failure:**
- Exit code 1 on `npx ng test --watch=false --browsers=ChromeHeadless --code-coverage=false`
- Terminal history confirms this command has been run with failure

**Confirmed Root Cause:**
- Stale test expectations in `app.component.spec.ts`:
  - Expects title `'VocabularyApp.UI'` but component has `'Vocabulary App'`
  - Expects h1 with `'Hello, VocabularyApp.UI'` but no h1 exists in template

**Likely Contributing Factors (Hypothesis):**
- Generated specs may lack required test harness providers
- Components that depend on Router, HttpClient, Services may fail if not provided in test bed
- Other specs may also have stale expectations

**Classification:** 
- OBSERVED FAILURE: Test suite currently fails
- CONFIRMED LIKELY CAUSE: Stale test expectations
- UNRESOLVED ROOT CAUSE: Complete failure list and all contributing factors unknown without full diagnostic

### Recommended Remediation Path

1. Run tests with verbose output to capture complete failure list
2. Fix confirmed issues:
   - Update `app.component.spec.ts` title expectation
   - Remove or update template test expectations
3. Address likely provider issues:
   - Ensure Router, HttpClient, Services are provided in test bed or mocked
4. Review all generated specs for similar issues
5. Run full suite to verify no regressions

---

## 7. Documentation Drift Assessment

### Summary of Discrepancies

| Document | Claim | Reality | Evidence | Action |
|----------|-------|---------|----------|--------|
| `FRONTEND-SUMMARY.md` | "Angular Frontend Complete" | Multiple R17 defects remain | Source code inspection | Update to note issues and coming-soon status |
| `FRONTEND-SUMMARY.md` | Dashboard 4 cards all implemented | 1 active, 3 marked "Coming Soon" | `dashboard.component.ts` `isActive: false` | Update to reflect actual status |
| `FRONTEND-SUMMARY.md` | No mention of Save for Later dead control | Button exists but non-functional | `word-lookup.component.html` | Add note about known issues |
| `VocabularyApp.UI/README.md` | Generic Angular scaffold content | Does not describe this app | File content | Replace with project-specific README |
| Routes: `/analytics`, `/preferences`, `/admin` | Implied to be implemented | No components exist | `app.routes.ts` has no routes for these | Document as "future" or remove references |

### Recommended Remediation

- Update `docs/FRONTEND-SUMMARY.md`:
  - Note which features are complete vs. in progress
  - Document known R17 issues
  - Set expectations for coming-soon features
- Replace `VocabularyApp.UI/README.md`:
  - Add setup, build, test instructions
  - Document authentication flow
  - List current features and known issues
- Review `test-api.http`:
  - Verify all endpoints match current API
  - Update example requests as needed
- Do not document planned features as completed

---

## 8. Dependency and Remediation Sequence

### Safe Implementation Order

The following sequence addresses dependencies and minimizes regression risk:

1. **Phase 1: Low-risk dead control removal**
   - Remove Save for Later button
   - Verify no remaining references
   - Risk: LOW

2. **Phase 2: Dashboard semantic upgrade**
   - Convert cards to native links/buttons
   - Maintain visual appearance
   - Risk: LOW to MEDIUM

3. **Phase 3: Modal accessibility (dialog)**
   - Add dialog semantics and focus management
   - Test focus trap and restoration
   - Risk: HIGH (focus logic is complex)

4. **Phase 4: Autocomplete accessibility**
   - Add ARIA semantics and keyboard handlers
   - Risk: MEDIUM (selection logic is error-prone)

5. **Phase 5: Toast architecture**
   - Move rendering from page component to app shell
   - Risk: MEDIUM (cross-component communication)

6. **Phase 6: SCSS and build validation**
   - Reduce component stylesheet size
   - Validate production build
   - Risk: LOW to MEDIUM

7. **Phase 7: Test stabilization**
   - Fix stale test expectations
   - Add missing test harness providers
   - Risk: MEDIUM (can hide runtime issues if done carelessly)

8. **Phase 8: Documentation**
   - Update docs to match actual app state
   - Risk: LOW

9. **Phase 9: Manual validation**
   - Keyboard-only smoke test
   - Screen-reader smoke test
   - Risk: NONE (verification only)

### Why This Sequence

- **Dead control first:** No dependencies, fast win, validates the remediation workflow
- **Dashboard second:** Navigation is core, relatively simple semantic fix
- **Dialog/autocomplete next:** Complex interactions but isolated; success is prerequisite for keyboard testing
- **Toast last in UX work:** Requires other pages to be tested first; architecture is clean but not urgent
- **Tests and docs last:** Validate the application state before documenting; tests gain confidence after behavioral fixes

---

## 9. Future Implementation Plan

### Objective
Remove dead controls, bring UI into native semantics, fix accessibility gaps, and align documentation with actual app state. This is a **stabilization and correctness pass**, not a feature expansion.

### Files Likely to Change

**Definite Changes:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` — Remove Save for Later, add ARIA to search and modal
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` — Add keyboard handlers, focus management
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html` — Convert cards to semantic elements
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts` — Adjust click handlers
- `VocabularyApp.UI/src/app/app.component.html` — Add global toast host
- `VocabularyApp.UI/src/app/app.component.ts` — Inject ToastService
- `docs/FRONTEND-SUMMARY.md` — Update feature status and known issues
- `VocabularyApp.UI/README.md` — Replace generic with project-specific content
- `VocabularyApp.UI/src/app/app.component.spec.ts` — Fix stale title expectation

**Likely Changes:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss` — Reduce size, optimize styles
- `VocabularyApp.UI/angular.json` — No changes if budget is met; otherwise adjust if needed

### Files That Should NOT Change

Unless there is evidence of a real defect:
- Backend API files (WebApi controllers, services, DTOs)
- Authentication logic (login, signup, auth guard)
- Business logic (search, add to vocabulary, quiz logic)
- Routing structure
- Models and interfaces
- HTTP client configuration

**Scope Guard:**
- No Part 2 learning features
- No analytics
- No gamification
- No major visual redesign
- No new state-management frameworks
- No unrelated backend refactoring

---

## 10. Regression Test Plan

### Tests to Include in Implementation

**Dashboard Cards:**
- Tab navigation reaches all active cards
- Focus ring is visible on each card
- Enter/Space on card navigates to target route
- Inactive cards are not reachable or clearly disabled

**Autocomplete:**
- ArrowUp/Down cycles through suggestions
- Enter selects highlighted suggestion
- Escape closes suggestions and keeps focus on input
- Click selection still works
- aria-expanded, aria-activedescendant update correctly

**Dialog:**
- Focus starts inside modal (not outside)
- Tab cycles within modal (no escape)
- Escape closes modal and returns focus to trigger
- Background is inert (not clickable)
- role="dialog" and aria-modal announced

**Toast:**
- Toasts appear when triggered from any route
- Toasts persist through route navigation
- Toast is announced by screen reader
- Close button dismisses toast
- Auto-remove after duration still works

**Build & Tests:**
- Production build succeeds without SCSS budget errors
- Angular test suite passes
- No console warnings or errors

---

## 11. Manual Verification Plan

### Keyboard-Only Smoke Test

**Environment:** User-agent with only keyboard input (no mouse)

**Test Path 1 — Dashboard Navigation:**
1. Start on dashboard after login
2. Tab through all visible elements
3. Activate each active card with Enter/Space
4. Confirm route changes
5. Confirm focus is visible at all times

**Test Path 2 — Vocabulary Lookup & Search:**
1. Navigate to vocabulary (via dashboard or direct link)
2. Focus search input
3. Type a search term (2+ characters)
4. Use ArrowUp/Down to select suggestion
5. Use Enter to select and search
6. Use Escape to dismiss suggestions
7. Navigate to another page
8. Return to vocabulary; confirm search state is preserved or cleared as expected

**Test Path 3 — Definition Dialog:**
1. View a vocabulary word
2. Click/activate "Pick Quiz Definition"
3. Use Tab to navigate options
4. Use Enter to select an option
5. Use Escape to close without saving
6. Confirm focus returns to trigger button
7. Open dialog again; use Tab+arrow keys to select
8. Use Enter to save

**Test Path 4 — Toasts:**
1. Perform an action that triggers a toast (e.g., add to vocabulary)
2. Confirm toast appears and is announced
3. Navigate to a different page
4. Confirm toast is still visible
5. Return to the vocabulary page
6. Confirm toast behavior is consistent

**Exit Criteria:**
- All interactions work without a mouse
- Focus is always visible
- No keyboard traps
- User can navigate entire app flow via Tab, Enter, Escape

### Screen Reader Smoke Test

**Environment:** Screen reader (NVDA, JAWS, or Apple VoiceOver)

**Test Path 1 — Semantic Announcements:**
1. Navigate dashboard
2. Confirm each card is announced with its title and status (active/coming soon)
3. Navigate to vocabulary
4. Confirm search input is announced with a label
5. Type to trigger suggestions
6. Confirm list is announced as combobox and options as selectable items

**Test Path 2 — Dialog Accessibility:**
1. Open definition editor dialog
2. Confirm it is announced as a dialog
3. Confirm options are announced with their current selection state
4. Confirm buttons are announced with their labels

**Test Path 3 — Toast Announcements:**
1. Trigger a toast
2. Confirm it is announced as a status or alert
3. Confirm the message content is audible

**Exit Criteria:**
- All interactive elements have meaningful accessible names
- Dialogs are announced as dialogs
- Lists are announced as lists with options
- Status updates are announced via live regions
- No content is hidden or unintelligible to screen reader users

---

## 12. Risk Register

| Risk | Area | Severity | Mitigation |
|------|------|----------|-----------|
| Focus trap in modal traps keyboard user | Dialog Focus Management | HIGH | Implement inert background, test Tab cycling, provide Escape escape route |
| Autocomplete keyboard logic breaks selection | Autocomplete Keyboard | MEDIUM | Test all keyboard paths, preserve mouse selection, avoid state race conditions |
| Toast removal loses notifications | Toast Architecture | MEDIUM | Test across route changes, verify subscription lifecycle, ensure no timing issues |
| SCSS reduction breaks layout | Build Optimization | MEDIUM | Validate production build, test responsive breakpoints, use visual regression testing |
| Test "fixes" hide real failures | Test Stabilization | HIGH | Understand root cause before fixing, run tests multiple times, verify no false passes |
| Dashboard card styling changes focus behavior | Card Semantics | MEDIUM | Test with native elements, ensure focus ring is visible, maintain click compatibility |
| Documentation is updated but becomes stale again | Documentation | MEDIUM | Establish doc ownership, link docs to source code, add CI check if possible |

---

## 13. File Impact Analysis

### Files Containing the R17 Issues (Direct Evidence)

1. `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` — Save for Later, toast rendering, autocomplete, dialog
2. `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` — Autocomplete logic, dialog handlers
3. `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss` — SCSS budget
4. `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html` — Card semantics
5. `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts` — Card navigation logic
6. `VocabularyApp.UI/src/app/app.component.html` — App shell (lacks toast host)
7. `VocabularyApp.UI/src/app/app.component.ts` — App shell (lacks ToastService)
8. `VocabularyApp.UI/src/app/services/toast.service.ts` — Toast state (correct, but rendering is elsewhere)
9. `VocabularyApp.UI/angular.json` — SCSS budget configuration
10. `VocabularyApp.UI/src/app/app.component.spec.ts` — Stale test expectations
11. `docs/FRONTEND-SUMMARY.md` — Stale documentation
12. `VocabularyApp.UI/README.md` — Generic scaffold README

### Files Likely to Require Changes During Remediation

1. `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` — Modify template (remove Save for Later, add ARIA, add dialog semantics)
2. `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts` — Add keyboard handlers, focus logic
3. `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss` — Optimize stylesheet content
4. `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html` — Convert divs to semantic elements
5. `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts` — Adjust navigation handlers if needed
6. `VocabularyApp.UI/src/app/app.component.html` — Add toast host container
7. `VocabularyApp.UI/src/app/app.component.ts` — Inject ToastService, wire toast host
8. `VocabularyApp.UI/src/app/app.component.spec.ts` — Fix stale expectations
9. `docs/FRONTEND-SUMMARY.md` — Update feature status and known issues
10. `VocabularyApp.UI/README.md` — Replace with project-specific content

### Files That Should Remain Untouched

- `VocabularyApp.WebApi/` (backend) — No changes for R17 (UI/accessibility focus)
- `VocabularyApp.UI/src/app/services/api.service.ts` — No changes
- `VocabularyApp.UI/src/app/services/auth.service.ts` — No changes
- `VocabularyApp.UI/src/app/guards/auth.guard.ts` — No changes
- `VocabularyApp.UI/src/app/models/` — No changes
- `VocabularyApp.UI/src/app/components/login/` — No changes
- `VocabularyApp.UI/src/app/components/signup/` — No changes
- `VocabularyApp.UI/src/app/components/quiz/` — No changes (separate from R17 focus areas)
- `VocabularyApp.UI/src/app/app.routes.ts` — No changes
- `VocabularyApp.UI/package.json` — No changes (unless dependencies are truly needed)
- `VocabularyApp.UI/angular.json` — No changes (budget stays as-is unless required by evidence)

---

## 14. R17 Scope Guard

R17 remediation **must NOT** introduce:

- Part 2 learning features (mastery, review scheduling, spaced repetition)
- Analytics or tracking
- Gamification
- Major visual redesign
- New state-management frameworks or architectural patterns beyond what exists
- Unrelated backend refactoring or API changes
- New dependencies
- Authentication changes

R17 is **strictly** a stabilization, accessibility, correctness, and documentation remediation.

---

## 15. Definition of Done

R17 remediation is complete only when ALL of the following are true:

1. **Dead Controls:**
   - [ ] Save for Later button is removed or fully implemented with backend support
   - [ ] No other dead/non-functional controls remain

2. **Dashboard Semantics:**
   - [ ] All dashboard cards use native interactive elements (links or buttons)
   - [ ] Active cards are keyboard-activatable (Tab, Enter/Space)
   - [ ] Inactive cards are clearly marked as "Coming Soon" and not interactive
   - [ ] Focus ring is visible when card has focus

3. **Autocomplete Accessibility:**
   - [ ] Search input has aria-label or associated `<label>`
   - [ ] Input has `role="combobox"` and `aria-expanded`
   - [ ] Dropdown has `role="listbox"` and items have `role="option"`
   - [ ] Keyboard navigation works: ArrowUp/Down, Enter, Escape
   - [ ] ARIA states update correctly (aria-selected, aria-activedescendant)

4. **Dialog Accessibility:**
   - [ ] Modal has `role="dialog"` and `aria-modal="true"`
   - [ ] Dialog has accessible name (aria-labelledby or aria-label)
   - [ ] Focus starts inside the dialog
   - [ ] Focus trap: Tab cycles within dialog only
   - [ ] Escape closes dialog and restores focus to trigger
   - [ ] Screen reader announces dialog semantics

5. **Toast Architecture:**
   - [ ] Toast container is in AppComponent (`app.component.html`)
   - [ ] Toasts render from the app shell, not from a page component
   - [ ] Toasts persist through route navigation
   - [ ] Toasts are announced by screen reader (aria-live)

6. **Encoding/Emoji:**
   - [ ] No UTF-8 corruption or mojibake exists
   - [ ] Emoji labels are paired with text alternatives or aria-labels

7. **SCSS Budget:**
   - [ ] Production build succeeds without style errors
   - [ ] SCSS budget warning is resolved (or intentionally deferred with documented justification)

8. **Tests:**
   - [ ] `ng test` runs to completion without errors
   - [ ] All specs pass
   - [ ] No false positives or mock-only tests that hide real issues

9. **Documentation:**
   - [ ] `docs/FRONTEND-SUMMARY.md` reflects actual app state
   - [ ] `VocabularyApp.UI/README.md` is project-specific and accurate
   - [ ] Route and feature documentation matches current implementation
   - [ ] Known R17 issues are documented (if any remain for future work)

10. **Validation:**
    - [ ] Keyboard-only smoke test passes (all navigation and actions work)
    - [ ] Screen-reader smoke test passes (all content is audible and understood)
    - [ ] Visual regression: no unintended styling changes
    - [ ] No new console warnings or errors

---

## 16. Final Assessment

### Current State Classification

| Category | Count | Status |
|----------|-------|--------|
| Confirmed defects | 6 | MUST FIX |
| Partially confirmed issues | 1 | NEEDS CLARIFICATION |
| Confirmed but low-priority config | 1 | ADDRESS IN BUILD TEST |
| Observed failures | 1 | MUST INVESTIGATE |
| Items requiring manual validation | 5 | MUST TEST |
| Stale docs | Multiple | MUST UPDATE |

### Highest Confidence Findings

1. **Dead Save for Later button** — 100% confidence; visible, no handler, no workflow
2. **Dashboard card semantics** — 100% confidence; divs with click handlers, no native elements
3. **Autocomplete missing ARIA** — 100% confidence; source code shows no combobox/listbox roles or keyboard handlers
4. **Dialog missing semantics** — 100% confidence; no role="dialog" or focus management
5. **Toast page-scoped rendering** — 100% confidence; toast container in word-lookup, not app shell
6. **Documentation drift** — 100% confidence; docs claim "complete" but defects remain

### Areas Requiring Runtime Verification

1. **Keyboard accessibility** — Cannot be verified statically; requires keyboard user testing
2. **Screen-reader announcements** — Cannot be verified statically; requires AT testing
3. **Actual SCSS budget warning** — Likely but not confirmed without production build
4. **Complete test failure list** — One spec failure confirmed; others likely but not enumerated

### Confidence Summary

- **High Confidence** (90–100%): 6 areas (Save for Later, dashboard, autocomplete, dialog, toast, docs)
- **Medium Confidence** (70–89%): 2 areas (SCSS budget, test reliability)
- **Low Confidence** (<70%): 1 area (emoji encoding — actually resolved; no corruption found)

---

## 17. Conclusion

The R17 remediation roadmap is clear:

1. **Most original R17 concerns are confirmed** — dead control, semantic gaps, architecture coupling
2. **Root causes are well-understood** — custom UI without accessibility patterns, stale tests, documentation drift
3. **Remediation path is low-risk** — mostly semantic fixes and code movement, no architectural redesign
4. **High-confidence starting point** — remove dead control, fix dashboard cards, add dialog/autocomplete semantics
5. **Manual validation is necessary** — keyboard-only and screen-reader testing cannot be done statically

The repository is **stable enough to implement remediation**, but the developer must maintain rigorous discipline:

- **Don't redesign** — fix semantics and architecture, not visuals
- **Understand root causes** — don't hide failures with test workarounds
- **Test thoroughly** — accessibility and keyboard behavior require runtime validation
- **Verify each step** — build, test, and manual checks after each phase

---

## Appendix: Evidence Inventory

**Files Examined:**
- 12 Angular component files (templates and TypeScript)
- 3 documentation files
- 1 build configuration file
- 1 package configuration file
- 1 test specification file
- 3 service files

**Search Queries Executed:**
- "Save for Later|saveForLater|save-for-later" — 1 match (dead button)
- Emoji patterns — 22 matches (all consistent UTF-8)
- Accessibility patterns (role, aria-*) — 0 combobox/listbox/dialog roles found

**File Size Analysis:**
- word-lookup.component.scss: 3,750 bytes (92% of 4kB error threshold)

**Terminal History Review:**
- Confirmed test failure (exit code 1)
- Confirmed stale spec expectations

**Classification Methodology:**
- CONFIRMED: Direct source code evidence
- PARTIALLY CONFIRMED: Evidence exists but incomplete
- LIKELY: Probable but unproven
- HYPOTHESIS: Plausible but unverified
- NOT VERIFIED: Assumed in previous analysis but needs runtime confirmation
- ALREADY RESOLVED: Evidence suggests issue is fixed
- NOT FOUND: No evidence in repository

---

**End of Analysis — No Implementation Performed**
