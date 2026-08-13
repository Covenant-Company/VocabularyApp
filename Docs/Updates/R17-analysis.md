# R17 Remediation Analysis — Final Evidence Review

**Date:** 2026-08-13  
**Status:** Analysis only — no implementation performed  
**Repository State:** `fix/r17-fix-dead-ui-controls` branch  

## 1. Executive Summary

This document presents the final evidence-based analysis of R17 (Accessibility, Dead Controls, UI Consistency, and Documentation). Each finding is classified with rigorous distinction between confirmed facts, likely issues, and items requiring runtime verification.

**Key Result:** 6 confirmed defects exist (dead control, non-semantic dashboard/autocomplete/dialog, page-scoped toast, documentation drift). Most can be remediated with low risk. Manual accessibility testing is required before final deployment.

**Confidence Levels:**
- 100% confidence (direct code evidence): 11 findings
- 70–90% confidence (requires runtime verification): 3 findings
- Not verifiable statically: Keyboard/screen-reader accessibility

---

## 2. R17 Status Matrix

| R17 Area | Finding | Classification | Evidence Quality | Runtime Verify Needed | Severity |
|---|---|---|---|---|---|
| **R17-A: Save for Later** | Dead button visible with no handler or workflow | CONFIRMED DEFECT | Source code inspection | No | HIGH |
| **R17-B: Dashboard Semantics** | Cards are divs with click handlers, not native elements | CONFIRMED DEFECT | Source code inspection | Keyboard testing | HIGH |
| **R17-C: Autocomplete Semantics** | Input lacks ARIA combobox/listbox roles and keyboard support | CONFIRMED DEFECT | Source code inspection | Keyboard/AT testing | HIGH |
| **R17-D: Dialog Semantics** | Modal lacks dialog role, aria-modal, focus management | CONFIRMED DEFECT | Source code inspection | Keyboard/AT testing | HIGH |
| **R17-E: Toast Architecture** | Toast host is page-scoped (word-lookup), not app-shell-scoped | CONFIRMED DEFECT | Source code inspection | Route navigation test | MEDIUM |
| **R17-F: Encoding** | UTF-8 encoding is valid; no mojibake or corruption found | ORIGINAL CONCERN NOT CONFIRMED | File content inspection | No | N/A |
| **R17-F: Emoji Accessibility** | Emoji are used as primary labels without text alternatives | CONFIRMED ACCESSIBILITY GAP | Source code inspection | Screen-reader testing | MEDIUM |
| **R17-G: SCSS Budget** | Budget configured: 2kB warning, 4kB error; stylesheet is 3.75kB | CONFIRMED CONFIGURATION | File size & config inspection | Production build test | MEDIUM |
| **R17-G: SCSS Warning** | Whether production build actually triggers warning | BUILD IMPACT REQUIRES VERIFICATION | File size analysis suggests likely | Production build test | MEDIUM |
| **R17-H: Test Failure** | `ng test` exits with code 1 | OBSERVED FAILURE | Terminal history | Full test run diagnosis | HIGH |
| **R17-H: Stale Spec** | app.component.spec.ts expects 'VocabularyApp.UI', actual is 'Vocabulary App' | CONFIRMED STALE EXPECTATION | Source code inspection | No (direct match) | MEDIUM |
| **R17-H: Root Cause Complete** | Confirmed that stale expectations are THE complete root cause | ROOT CAUSE NOT YET CONFIRMED | One stale expectation found | Full test run required | MEDIUM |
| **R17-I: Documentation Drift** | FRONTEND-SUMMARY.md claims features "complete" that have known defects | CONFIRMED DRIFT | Documentation vs. source comparison | Review only | MEDIUM |
---

## 3. Detailed Findings

### R17-A — Save for Later Control

**Finding:** Dead button is visible in the UI with no handler or workflow.

**Evidence — Direct Source Code Inspection:**

1. **Button exists:** `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` line 149
   ```html
   <button class="px-4 py-2 bg-gray-100 hover:bg-gray-200 text-gray-700 rounded-lg font-medium transition-colors duration-200">
     🔖 Save for Later
   </button>
   ```

2. **No handler:** Button has no `(click)`, no `[(ngModel)]`, no method binding

3. **Codebase search:** Full search for `saveForLater`, `save.*later`, `bookmark` patterns returned **0 matches** in business logic

4. **Backend search:** No matching API endpoints or service calls

**Classification:** **CONFIRMED DEFECT** (100% confidence)

**Affected Files:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`

**Severity:** HIGH

**Manual Verification Required:** No — source code is unambiguous

---

### R17-B — Dashboard Card Semantics

**Finding:** Dashboard cards use non-semantic clickable divs instead of native interactive elements.

**Evidence — Direct Source Code Inspection:**

1. **Semantic element:** Template uses `<div>` not `<a>` or `<button>`

2. **Click handler:** Navigation is via JavaScript
   ```typescript
   onCardClick(card: any): void {
     if (card.isActive) {
       this.router.navigate([card.route]);
     }
   }
   ```

3. **No native affordances:** No explicit keyboard activation without JavaScript handler

**Classification:** **CONFIRMED DEFECT** (100% confidence)

**Affected Files:**
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts`

**Severity:** HIGH

**Manual Verification Required:** Keyboard testing — will confirm Enter/Space/Tab behavior

---

### R17-C — Autocomplete Accessibility

**Finding:** Search input and suggestion list lack ARIA semantics and keyboard support.

**Evidence — Direct Source Code Inspection:**

1. **Input lacks ARIA:** No `role="combobox"`, no `aria-expanded`, no `aria-controls`, no `aria-activedescendant`

2. **Suggestion list lacks semantics:** No `role="listbox"` on dropdown; no `role="option"` on items; no `aria-selected`

3. **Keyboard support:** No `ArrowUp`, `ArrowDown`, `Enter`, `Escape` key handlers in component; selection is mouse-only

**Classification:** **CONFIRMED DEFECT** (100% confidence)

**Affected Files:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`

**Severity:** HIGH

**Manual Verification Required:** Keyboard testing, screen-reader testing

---

### R17-D — Dialog Accessibility

**Finding:** Definition editor modal lacks dialog semantics and focus management.

**Evidence — Direct Source Code Inspection:**

1. **Modal markup lacks dialog role:**
   ```html
   <div *ngIf="showDefinitionEditor" class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
     (click)="closeDefinitionEditor()">
   ```
   - No `role="dialog"`, no `aria-modal="true"`, no accessible name

2. **Focus management:** No explicit focus trap logic, no focus restoration visible, no explicit Escape key handler

**Classification:** **CONFIRMED DEFECT** (100% confidence)

**Affected Files:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html` (lines 389–440)
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`

**Severity:** HIGH

**Manual Verification Required:** Keyboard testing, screen-reader testing, focus management testing

---

### R17-E — Toast Architecture

**Finding:** Toast host is rendered inside a page-level component instead of the application shell.

**Evidence — Direct Source Code Inspection:**

1. **Toast service is correctly defined:**
   - Scope: `providedIn: 'root'` (application singleton)
   - State: `BehaviorSubject<Toast[]>` (centralized)

2. **Toast rendering is page-scoped:**
   - Container is inside `word-lookup.component.html` (lines 1–30)
   - Only accessible when word-lookup is routed

3. **Application shell has no toast host:**
   - `app.component.html` contains only `<router-outlet />`
   - No toast container at shell level

**Classification:** **CONFIRMED DEFECT** (100% confidence)

**Affected Files:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/app.component.html`
- `VocabularyApp.UI/src/app/app.component.ts`

**Severity:** MEDIUM

**Runtime Impact:** Unknown without testing — requires route navigation test

**Manual Verification Required:** Test whether toasts survive route navigation

---

### R17-F — Encoding and Emoji

#### Encoding Status

**Finding:** UTF-8 encoding is valid; no corruption or mojibake detected.

**Evidence — Direct Source Code Inspection:**

1. **Emoji search:** 22 matches found, all rendering correctly; no replacement characters, no malformed sequences

2. **File encoding:** All inspected files are valid UTF-8

**Classification:** **ORIGINAL R17 ENCODING DEFECT NOT CONFIRMED** (100% confidence)

**Severity:** N/A (no defect found)

#### Emoji Accessibility Concern

**Finding:** Emoji are used as primary labels without text alternatives.

**Evidence — Direct Source Code Inspection:**

Examples:
- Success toast: `<span class="text-xl">✅</span>` — emoji only, no aria-label
- Error toast: `<span class="text-xl">❌</span>` — emoji only
- Status indicator: `🔖` without text label

**Classification:** **CONFIRMED ACCESSIBILITY GAP** (100% confidence)

**Affected Files:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html`

**Severity:** MEDIUM

**Manual Verification Required:** Screen-reader testing

---

### R17-G — SCSS Component Budget

**Finding:** Angular configuration includes a strict SCSS budget; component stylesheet is close to limits.

**Evidence — Configuration Inspection + File Size Analysis:**

1. **Budget configuration:**
   ```json
   {
     "type": "anyComponentStyle",
     "maximumWarning": "2kB",
     "maximumError": "4kB"
   }
   ```

2. **Component stylesheet size:**
   - **3,750 bytes** (confirmed via file system)
   - Warning: 2,048 bytes (exceeded by 183%)
   - Error: 4,096 bytes (at 92%)

**Classification (Part 1):** **CONFIRMED CONFIGURATION** (100% confidence)

**Classification (Part 2):** **BUILD WARNING STATUS REQUIRES VERIFICATION**

**Affected Files:**
- `VocabularyApp.UI/angular.json`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss`

**Severity:** MEDIUM

**Manual Verification Required:** Production build test

---

### R17-H — Angular Test Reliability

**Finding:** Test suite fails; stale spec expectations exist.

**Evidence — Terminal History + Source Code Inspection:**

1. **Observed failure:**
   - Command exit code: 1 (confirmed)
   - Status: **TEST FAILURE CONFIRMED**

2. **Stale spec expectations:**
   - File: `app.component.spec.ts` lines 15–18
   - Spec: `expect(app.title).toEqual('VocabularyApp.UI')`
   - Actual: `'Vocabulary App'`
   - Status: **DIRECT MISMATCH CONFIRMED**

3. **Root cause analysis:**
   - Confirmed: At least one spec assertion will fail
   - Likely: Generated template test also expects h1 tag that doesn't exist
   - Probable: Test harness may lack providers
   - Unresolved: Whether stale specs are THE complete root cause

**Classification (Part 1):** **OBSERVED FAILURE** (100% confidence)

**Classification (Part 2):** **CONFIRMED LIKELY CONTRIBUTING CAUSE** (100% confidence)

**Classification (Part 3):** **COMPLETE ROOT CAUSE NOT YET CONFIRMED** (70% confidence)

**Affected Files:**
- `VocabularyApp.UI/src/app/app.component.spec.ts`

**Severity:** HIGH

**Manual Verification Required:** Full test run with diagnostic output

---

### R17-I — Documentation Drift

**Finding:** Frontend documentation claims features are complete and working, but known defects exist.

**Evidence — Documentation vs. Source Code Comparison:**

1. **FRONTEND-SUMMARY.md claims:** "Angular Frontend Complete!" with all features marked as done

2. **Actual repository state:**
   - Save for Later button exists but is non-functional
   - Dashboard cards lack semantic accessibility
   - Search lacks ARIA

3. **Route documentation vs. reality:**
   - Documented: 4 dashboard cards equally functional
   - Actual: 1 active, 3 marked "Coming Soon"

**Classification:** **CONFIRMED DRIFT** (100% confidence)

**Affected Files:**
- `docs/FRONTEND-SUMMARY.md`
- `VocabularyApp.UI/README.md` (generic, not project-specific)

**Severity:** MEDIUM

**Manual Verification Required:** None

---

## 4. Accessibility Assessment

### Static Analysis Findings (Confirmed)

**Dashboard:**
- Non-semantic cards with JavaScript click handlers
- No explicit keyboard navigation
- No ARIA labels or roles

**Autocomplete:**
- Input lacks label and combobox semantics
- No keyboard navigation
- No live region for option announcement

**Dialog:**
- No dialog semantics
- No focus trap
- No explicit Escape handling
- No focus restoration

**Toasts:**
- No aria-live confirmed in component
- Icons are emoji only (no text alternative)

### Manual Verification Plan (Required)

**Keyboard-Only Smoke Test:**
1. Dashboard: Tab through cards, activate with Enter/Space
2. Search: ArrowUp/Down navigate suggestions, Enter selects, Escape closes
3. Dialog: Tab cycles within modal, Escape closes, focus returns to trigger
4. Toasts: (if reachable) Tab to close button, verify close works

**Screen-Reader Smoke Test:**
1. Dashboard: Each card announced with title and status
2. Search: Input announced with label; suggestions announced as list
3. Dialog: Announced as dialog with title; options announced with state
4. Toasts: Messages announced as status or alert

**Status:** Manual tests have **NOT YET BEEN PERFORMED** — this is static analysis only.

---

## 5. Build and Test Verification Status

### SCSS Budget

**Confirmed:**
- Budget configuration exists (2kB warning, 4kB error)
- Stylesheet is 3.75kB (exceeds warning)

**Requires Runtime Verification:**
- Run: `npm run build -- --configuration production`
- Capture: Whether budget warning is actually triggered

### Angular Tests

**Confirmed:**
- Test command fails (exit code 1)
- At least one spec has stale expectations

**Requires Runtime Verification:**
- Run: `ng test --watch=false --browsers=ChromeHeadless` with full verbose output
- Capture: Complete list of failing tests
- Diagnose: Whether stale specs are the only cause

---

## 6. Summary of Findings by Confidence Level

### 100% Confidence (Direct Code Evidence)

1. ✅ Save for Later button is dead (no handler, no workflow)
2. ✅ Dashboard cards are non-semantic divs with click handlers
3. ✅ Autocomplete lacks ARIA and keyboard support
4. ✅ Dialog lacks dialog semantics and focus management
5. ✅ Toast host is page-scoped, not app-shell-scoped
6. ✅ UTF-8 encoding is valid (original concern not confirmed)
7. ✅ Emoji accessibility gap exists (emoji without text alternatives)
8. ✅ SCSS budget is configured at 2kB/4kB
9. ✅ Test suite fails (observed from terminal)
10. ✅ app.component.spec.ts has stale title expectation
11. ✅ Documentation claims features are complete when defects exist

### 70–90% Confidence (Requires Runtime Verification)

1. 🔶 SCSS budget warning will trigger during production build (likely but not proven)
2. 🔶 Toast notifications are lost during route navigation (architectural risk but not observed)
3. 🔶 Stale specs are the complete root cause of test failures (likely but not confirmed)

### Not Verified (Cannot Verify Statically)

1. ❓ Keyboard-only accessibility (requires user testing)
2. ❓ Screen-reader accessibility (requires AT testing)
3. ❓ Focus trap correctness (requires keyboard testing)
4. ❓ Toast live-region announcements (requires AT testing)

---

## 7. Implementation Readiness

### Ready to Implement (High Confidence)

- R17-A: Remove Save for Later button
- R17-B: Convert dashboard cards to semantic elements
- R17-C: Add ARIA semantics to autocomplete
- R17-D: Add dialog semantics and focus management
- R17-E: Move toast host to app shell
- R17-F: Ensure emoji have text alternatives
- R17-I: Update documentation

### Requires Verification Before Implementation

- R17-G: Confirm SCSS budget warning via production build
- R17-H: Confirm test failure root causes via full test run

---

## 8. Remediation Sequence (Low-Risk Order)

1. **Phase 1:** Remove Save for Later (no dependencies)
2. **Phase 2:** Fix dashboard semantics (core navigation, affects keyboard testing)
3. **Phase 3:** Fix dialog semantics (complex focus logic)
4. **Phase 4:** Fix autocomplete (complex keyboard logic)
5. **Phase 5:** Move toast host (clean architectural change)
6. **Phase 6:** Fix SCSS budget (build validation)
7. **Phase 7:** Fix test stale specs (foundation)
8. **Phase 8:** Update documentation (no runtime risk)
9. **Phase 9:** Manual keyboard/AT smoke tests (validation)

---

## 9. Risk Assessment

| Risk | Area | Severity | Mitigation |
|------|------|----------|-----------|
| Focus trap logic traps keyboard users | Dialog | HIGH | Test Tab cycling, provide Escape route |
| Autocomplete selection breaks | Autocomplete | MEDIUM | Test all keyboard paths, preserve mouse |
| Toast notifications disappear | Toast | MEDIUM | Test route navigation, verify lifecycle |
| Layout regression | SCSS | MEDIUM | Validate responsive breakpoints |
| Test fixes hide real failures | Tests | HIGH | Understand root cause before fixing |

---

## 10. Files Likely to Change

**Definite:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts`
- `VocabularyApp.UI/src/app/app.component.html`
- `VocabularyApp.UI/src/app/app.component.ts`
- `docs/FRONTEND-SUMMARY.md`
- `VocabularyApp.UI/README.md`
- `VocabularyApp.UI/src/app/app.component.spec.ts`

**Likely:**
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss`

**Should NOT Change:**
- Backend files
- Auth logic
- Business logic
- Routes (unless adding missing handlers)

---

## 11. Analysis Status

- **Analysis complete:** ✅ YES
- **Implementation performed:** ✅ NO (analysis only)
- **Manual accessibility verification required:** ✅ YES (keyboard & screen reader testing)
- **Build verification required:** ✅ YES (production build for SCSS budget)
- **Test root cause confirmed:** ⚠️ PARTIALLY (one spec confirmed stale, but complete root cause not confirmed)
- **R17 ready for remediation:** ✅ YES (high-confidence findings ready; low-risk implementation path identified)

---

**Analysis completed 2026-08-13. No implementation has been performed. Ready for remediation phase when authorized.**

## 12. Files Expected to Change

Likely future changes:

- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.html`
- `VocabularyApp.UI/src/app/components/dashboard/dashboard.component.ts`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.html`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.ts`
- `VocabularyApp.UI/src/app/components/word-lookup/word-lookup.component.scss`
- `VocabularyApp.UI/src/app/app.component.html`
- `VocabularyApp.UI/src/app/app.component.ts`
- `VocabularyApp.UI/src/app/services/toast.service.ts`
- `VocabularyApp.UI/angular.json`
- `VocabularyApp.UI/package.json`
- `VocabularyApp.UI/src/app/app.component.spec.ts`
- `VocabularyApp.UI/src/app/services/toast.service.spec.ts`
- `docs/FRONTEND-SUMMARY.md`
- `VocabularyApp.UI/README.md`
- `docs/README.md`
- `test-api.http`

---

## 13. Files That Should NOT Be Changed

Unless new evidence proves otherwise, these files should remain untouched during the R17 remediation because they are not the direct source of the reported defects:

- `VocabularyApp.WebApi/Controllers/WordsController.cs` should not be refactored for UI-only R17 work unless a real workflow is explicitly added.
- `VocabularyApp.WebApi/Services/*` should not be altered for dead control cleanup or toast architecture changes unless a true backend requirement emerges.
- Application models, DTOs, and route definitions should not be redesigned.
- This analysis file is the only documentation file intentionally created in this analysis phase.

---

## 14. Scope Guard

R17 remediation should not introduce:

- Part 2 learning features
- analytics
- mastery
- review scheduling
- spaced repetition
- AI
- gamification
- major visual redesign
- new frontend state-management frameworks
- unrelated backend refactoring

The intent is a stabilization and correctness pass, not a redesign or feature expansion.

---

## 15. Definition of Done

The future developer should consider R17 complete only when all of the following are true:

- No visible dead action remains in the current application flow.
- Dashboard interactive elements use native semantics and keyboard activation.
- Autocomplete has proper labels, keyboard support, and ARIA states.
- Dialogs have accessible names, focus management, Escape behavior, and focus restoration.
- Toasts are rendered at the app-shell level with live-region announcements.
- Documentation matches the actual app and route flow.
- No misleading or stale controls remain.
- Angular production build passes within the configured budgets.
- Angular tests terminate reliably and pass.
- Keyboard-only and screen-reader smoke tests have been completed or explicitly marked as pending manual validation.

---

## 16. Final Recommendation

The safest starting point is the dead control and dashboard semantics, followed by the modal and autocomplete accessibility work. Those are the highest-signal correctness fixes and they are contained to the current UI. Once those patterns are stable, the toast shell should be moved to the application root, then the test stability and documentation drift should be addressed after UI behavior is verifiable.

This is the lowest-risk sequence for the future developer because it addresses the most visible user-facing inconsistencies first without broad rewrites or feature additions.

---

## 17. Classification Summary

### CONFIRMED
- Save for Later control is still visible and dead
- Dashboard cards are divs with click navigation
- Autocomplete semantics are missing
- Dialog semantics and focus behavior are missing
- Toast rendering is page-scoped
- SCSS budget issue is materially likely
- Test suite is currently failing
- Documentation drift is present

### PARTIALLY CONFIRMED
- Emoji/UTF-8 corruption is not clearly proven; the code uses emoji consistently but not in an accessibility-friendly way

### HYPOTHESIS
- Some test failures are likely due to stale specs and missing providers, but the exact full failure list is not proven here without a fresh run

### ALREADY RESOLVED
- None identified from the current repo state

### NOT VERIFIABLE
- Actual screen-reader and keyboard-only manual validation in this environment

---

## 18. Final assessment

The repository currently supports the conclusion that R17 is not “already done.” The current state still matches the original plan’s major concerns, though some details are more nuanced than the original description. The highest-confidence remediation priorities are the dead control, semantic navigation, autocomplete, dialog, toast shell, and documentation sync. These are all contained and do not require a redesign or new feature work.
