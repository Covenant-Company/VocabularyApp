# F6 — Phase 4 Angular Review Experience results

Date: 2026-10-13 (America/New_York). Assessment: **CONDITIONAL COMPLETE — Phase 4 only**. Angular implementation, automated verification and production compilation are complete. Manual assistive-technology/mobile/zoom acceptance, integrated verification and production release evidence remain pending under their separate authorization.

## 1. Executive summary

Implemented a guarded `/reviews` experience with a dashboard entry, independent server review counts, the complete cursor-paginated due backlog, multiple-choice sessions, accepted-answer feedback, session progress/completion, durable resume and immutable submission recovery. Correctness, due dates, intervals and session availability remain server-authoritative.

Final results: **90 focused Angular tests passed**, then **248 Angular regression tests passed**, with **0 failed and 0 skipped** in both runs. The final Angular production build succeeded. Two existing vocabulary-component build warnings remain; no review-component budget warning was introduced. These overlapping test counts must not be summed.

## 2. Scope and authorization

The user authorized Phase 4 Angular implementation and safe local verification only. D1–D9 and P1–P5 were preserved. Changes are limited to Angular review code, dashboard/navigation, error normalization, tests/fixtures and this report. No backend, scheduler, entity, migration, production flag/configuration, dependency, CI/CD or deployment change was made. No live production API/database call, migration or enrollment script execution was performed. No branch operation, commit, push, merge or deployment occurred. Phase 5 integrated verification and Phase 6 release preparation were not begun.

## 3. Repository baseline

Normal `git branch --show-current`, `git status --short` and `git log -3 --oneline` failed the ownership safety check. No Git safety settings or overrides were changed. Read-only HEAD/ref/loose commit-object inspection verified:

- Branch `feature/f6-spaced-repetition`.
- HEAD `bf91b033b89b835b4915345f7b6a2c06bfd362e4` — Ignore generated .NET test results.
- Direct parent `be793575bada55b96ba4d290b882fbdb3b2c0128` — Complete F6 Phase 3 backend review services.

Before edits, a read-only Git index comparison found no tracked differences, allowing CRLF normalization. A SHA-256 baseline captured 305 existing source/configuration/documentation files, excluding generated/dependency/Git directories. The final comparison is limited to the expected Angular edits and this new report; no existing source was deleted. This supplements the blocked normal Git status rather than changing repository ownership policy.

The six required F6 documents and the actual Phase 3 controller, DTOs and service were reviewed alongside existing Angular routes, auth guard, ApiService/AuthService, error handling, dashboard, quiz and Jasmine/Karma infrastructure. Backend wire behavior matches the approved reference; no API contract correction was needed.

## 4. Files created and modified

Created under `VocabularyApp.UI/src/app`:

- `models/review.model.ts`.
- `services/review.service.ts`, `review-contract.ts`, `review.service.spec.ts`.
- `components/review-session/review-session.component.ts`, `.html`, `.scss`, `.spec.ts`.
- `testing/review-storage.ts`.

Created `Docs/Updates/F6-Spaced-Repetition/Phase-4/F6-phase-4-implementation-results.md`. Modified under `VocabularyApp.UI/src/app`:

- `app.routes.ts` — guarded lazy review route.
- `components/dashboard/dashboard.component.ts`, `.html`, `.spec.ts` — summary, review entry, disabled/error/retry handling and relevant regression tests.
- `services/api-error.ts`, `.spec.ts` — approved review operations/messages/validation fields and review-specific disabled handling.
- `testing/api-contract.fixtures.ts` — typed review fixtures based on the actual DTOs.

Existing ApiService, AuthService, login/signup, dictionary/My Words, quiz, global styles, package files, Angular configuration and backend sources remain unchanged. Builds/tests updated only normal generated output/cache directories in addition to these source changes.

## 5. Angular architecture

The standalone ReviewSessionComponent manages landing/question/feedback/completion presentation using CommonModule, FormsModule, RouterLink and existing application styles. A lazy route keeps the question UI out of the initial bundle.

ReviewService wraps the existing ApiService, so JWT headers and the configured API base URL are reused without duplicating authentication. Review models match the public DTOs, including explicit nullable members and option IDs 0–3. The narrow review-contract helper validates untrusted success bodies, UUIDs, UTC timestamps, options, receipts, availability and count consistency before UI state is accepted. Malformed success bodies enter the error channel; submission uncertainty remains recoverable.

Root-service lifetime permits an answer request to finish and retain its receipt handling after the route's view subscription is destroyed. Component subscriptions use takeUntilDestroyed. Logout/account-change events cancel old-account requests, clear recovery and remove previous-account data from the view.

## 6. Review API integration

| Endpoint | Angular integration |
|---|---|
| GET `/api/reviews/due` | Typed take/cursor request; opaque cursor URL-encoded; exact due-item fields and blocked availability. |
| GET `/api/reviews/summary` | Independent review totals/overlapping counts/earliest future due; never substitutes quiz statistics or zero for failure. |
| POST `/api/reviews/start` | New session sends questionCount; resume sends only resumeSessionId; ready/empty/unavailable discriminated with no local question generation. |
| POST `/api/reviews/submit` | Exactly sessionId/itemId/submissionId/selectedOptionId; receipt identity must match the immutable request before it is accepted. |

Responses use the existing R7 success/data envelope. Error normalization supports validation 400, authentication 401, unavailable 404, stale/expired/conflicting 409, review disablement 503, network errors, unexpected failures and malformed 200 bodies. Raw internal/provider/binding diagnostics are never shown. The new 503 reviews_disabled branch applies only to review operations and preserves existing dictionary/quiz 503 behavior.

## 7. Routing and navigation

`/reviews` uses the existing authGuard and a lazy standalone component. The dashboard has an accessible native “Review Vocabulary” link, live server counts and a retry action. A disabled review response hides the dashboard review link and reports availability explicitly. Review screens link back to dashboard and My Words. Existing route meanings and quiz navigation are unchanged.

## 8. Dashboard and due-backlog behavior

The landing view displays due, New, overdue, blocked, usable-due and total-enrolled counts and the earliest future review when supplied. It explains that due/New/overdue/blocked counts overlap. A positive due count is not treated as proof of playable questions; start's availability response decides.

TotalEnrolledCount=0 produces an enrollment-aware empty message with a My Words link. The review DTO cannot distinguish an empty saved vocabulary from saved legacy words awaiting enrollment, so the UI explicitly avoids claiming the user has no saved words. No additional vocabulary request or backend property was invented.

Due items stay in backend order; additional pages remain available until nextCursor=null. Appending deduplicates UserWordId while preserving the first occurrence. Unusable entries remain visible, labelled blocked. Temporary page failures retain loaded rows and the cursor for manual retry. An invalid/expired cursor offers explicit first-page refresh. The UI never generates cursors or exposes private definition identifiers.

## 9. Questions and accepted feedback

The server prompt contains the current target word and is displayed verbatim; no nonexistent public Word property is assumed. Four native labelled radios show the persisted options in received order. Option zero is accepted through explicit null checks. Selection alone does not submit; a labelled submit action sends the answer. Controls are effectively disabled through fieldset while submitting, after acceptance, during uncertainty or on terminal/disabled/authentication errors.

Feedback displays server isCorrect, selectedAnswer, correctAnswer, nextReviewAt, interval and status only after acceptance. Accepted receipts remain attached to their immutable items and visible in an accepted-answer details list. Progress and completion use server summaries; the UI does not advance schedules, invent mastery or manufacture outcomes for pending questions. The last answer's feedback remains visible until the user chooses completion.

## 10. Durable session resume

Recovery is stored as `vocab_review_v1:<authenticated-user-id>` in sessionStorage. The value contains only sessionId and an optional exact pending submission payload. It contains no JWT, answer key, options, definition-source IDs, prompt or vocabulary text.

On route return/refresh, pending replay takes priority. Only after acceptance or definitive resolution may resume use POST start/resumeSessionId. Resume preserves the exact server questions/options/receipts; the first pending question is presented when no particular recovered item needs feedback. Completed resumed sessions clear recovery metadata. Expiry is determined by backend responses; displaying expiresAt does not impose a browser-clock expiry policy.

## 11. Submission idempotency and recovery

Each new logical answer uses crypto.randomUUID once. The exact immutable payload is saved before HttpClient sends the request. Repeated in-flight sends share one observable/request. Navigation unsubscribe does not discard an in-flight receipt. Network/500/malformed-200/wrong-receipt/401/503 failures preserve the original payload and prohibit changing the answer or silently starting another session. Manual retry sends the same submission key and selection; there is no automatic retry loop.

A valid matching accepted receipt clears pending state; HTTP 400/404/409 definitive rejections also clear that exact rejected payload. Stale/expired/conflicting sessions require explicit restart and preserve visible accepted feedback. A received replay remains visible even if the following resume fails due to expiry.

If pending storage cannot be written, no answer request is sent. If storage becomes unavailable while clearing an already accepted payload, accepted feedback is retained in memory and the old stored payload is safe to replay later. A subsequent answer still requires a successful pending write. Malformed storage is ignored/removed safely, with no server outcome inferred. Logout/account changes remove review keys while leaving unrelated session storage untouched.

## 12. Feature-disabled handling

The frontend has no independent production feature switch. reviews_disabled produces “Vocabulary reviews are not available right now” and an unavailable-count explanation, not an empty backlog or zero due count. Recovery payloads survive this response. The dedicated screen offers “Check review availability” to refresh server status. Empty and unavailable successful sessions have separate messages for no due words, insufficient usable vocabulary, no usable due targets and ambiguous options. No production flag was changed.

## 13. Accessibility and responsive design

Native fieldset/legend/radios provide standard keyboard selection, native form submission and accessible labels without custom keyboard traps. Focus moves to the current prompt when a question is installed/advanced. Progress has an accessible label. Loading and accepted feedback use status/live regions; failures use alerts; correctness is expressed as text. Buttons have a minimum 44px height, visible focus outlines, wrapping definitions, flexible action rows and responsive layouts. Every displayed review date is labelled as the user's local time.

Headless DOM/browser tests verified four labelled radios, effective disabled state, option-zero form submission, question focus, live-feedback markup and long-text containment at 320px. Computed contrast ratios for the primary combinations are header 6.49:1, primary button 7.90:1, body 14.68:1, blocked label 7.09:1 and error text 7.60:1. The header uses the existing darker brand color to keep ordinary white text readable.

These checks do not constitute manual screen-reader acceptance, real touch-device testing or a complete 200% browser-zoom rehearsal. Native arrow/Tab behavior is relied upon; the test's synthetic form/change events do not claim to emulate every physical keyboard action. Those manual checks remain explicitly pending for the separately authorized acceptance work.

## 14. Automated test results and commands

Environment: Node **18.20.8**, npm **10.8.2**, installed Angular core **18.2.14**, Angular CLI **18.2.21**, Jasmine/Karma and **Chrome Headless 155.0.0.0 on Windows**. Existing dependencies were used; no install or upgrade occurred. HTTP tests use HttpTestingController with isolated fixtures/storage, not production API calls.

From `VocabularyApp.UI`:

```powershell
$env:CHROME_BIN = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/services/review.service.spec.ts --include=src/app/components/review-session/review-session.component.spec.ts --include=src/app/components/dashboard/dashboard.component.spec.ts --include=src/app/services/api-error.spec.ts
```

Final focused run: **exit 0; 90 passed, 0 failed, 0 skipped**, browser execution 1.381s (test time 1.218s), after compilation/launch. Covers transport/JWT/DTO checks, safe status normalization, summary/empty/blocked states, pagination/duplicates/cursor refresh, availability, native controls/zero/loading/feedback/progress/completion, immutable retry and reload recovery, persisted accepted receipts, stale/expired/conflict/disabled/authentication responses, storage failures, route unsubscribe, logout/user changes and responsive long text.

```powershell
$env:CHROME_BIN = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
npm test -- --watch=false --browsers=ChromeHeadless --reporters=dots
```

Final Angular-only regression run: **exit 0; 248 passed, 0 failed, 0 skipped**, browser execution 2.273s (test time 2.059s). Existing login/signup, authentication, dashboard, dictionary/My Words, API transport/error, quiz, toast and application-shell tests passed. This is authorized local Angular regression, not Phase 5 integrated frontend/backend/database verification.

Initial test compilation exposed new-test TypeScript issues with NodeList iteration, callback-populated variables and a union of Observable types. The first compiled run was 85 passed/1 failed out of 86 and also logged router-mock errors; the failure checked the radio's own disabled property instead of its effective fieldset-disabled state. Tests/mocks were corrected. No final test failure or baseline regression remains; no backend code was changed to accommodate a frontend test.

## 15. Production build results and environment limitations

```powershell
npm run build -- --configuration production
```

Final result: **exit 0**, application bundle generation complete in **9.466s**. Initial bundle **482.30kB**, estimated transfer **120.26kB**; lazy review component **25.94kB**, estimated transfer **6.36kB**. Output: `VocabularyApp.UI/dist/vocabulary-app.ui`.

Warnings are in existing unchanged WordLookupComponent files: NG8107 optional chaining at template line 273, and the stylesheet at 2.80kB exceeding its 2.05kB warning budget by 751 bytes. No budgets/configuration were relaxed. An earlier successful production build emitted the same warnings and an npm update notice; no npm upgrade was performed.

The first sandboxed build failed because Node could not lstat `C:\Users\Owner`. A second attempt used process-only NODE_OPTIONS `--preserve-symlinks --preserve-symlinks-main`; Angular's own workspace realpath still failed. Approval-reviewed local build/test executions outside that filesystem restriction then succeeded. No repository/Git safety configuration was changed and no deployment was involved. The final successful commands used the ordinary scripts, not those unsuccessful symlink flags.

## 16. Known limitations and technical debt

Recovery is intentionally per-tab sessionStorage; closed-tab/storage-loss/device-transfer recovery metadata is not promised. The backend retains accepted outcomes independently. An unavailable storage implementation blocks new answer submission rather than risking an unrecoverable logical key. A failed storage removal may leave safe stale resume/replay metadata, but it cannot advance schedules locally or expose another owner's recovery key.

Backend summary/due/start are separate live views, so concurrent changes may affect availability or later pages. The UI uses backend availability and explicit cursor refresh rather than pretending to reserve targets. Existing build warnings were left unchanged. Manual mobile/zoom/assistive-technology acceptance and actual cross-layer server integration remain future authorized verification.

## 17. Backend contract discrepancies

**None requiring API changes.** Models, routes and envelope handling match Phase 3 and the approved reference. The due DTO exposes only userWordId/word/nextReviewAt/reviewStatus/isUsable, and the question DTO exposes prompt rather than a separate word field; the UI uses those exact fields. TotalEnrolledCount is not a count of all saved vocabulary, so zero-enrollment wording accounts for pending legacy enrollment. No property, route, response format, endpoint or scheduling behavior was invented.

## 18. Phase 5 prerequisites

Developer review of Phase 4, then separate explicit Phase 5 authorization. Future integrated verification should exercise actual authenticated backend lifecycle, replay/refresh/expiry/content races, manual keyboard/screen-reader/mobile/200% zoom flows and server feature-disable behavior in an authorized isolated environment. Existing Angular and backend results are separate local evidence; they do not replace end-to-end or SQL Server testing.

## 19. Outstanding production-release gates

P6–P7 remain unresolved. SQL Server migration/enrollment/concurrency/backup/restore rehearsal remains pending against an explicitly authorized disposable target. Production runtime configuration persistence, disable/recycle behavior and durable Data Protection key storage require evidence. The additive F6 schema, including WordDefinition.ContentRevision, is required by the new backend binary even when both review flags are disabled. No frontend build makes that binary compatible with a pre-F6 database or authorizes feature activation.

## 20. Final readiness assessment and stop point

**CONDITIONAL COMPLETE — Phase 4.** All authorized Angular implementation and automated local verification/build work are complete, with manual acceptance and separate integration/release evidence explicitly pending. Successful compilation does not establish production readiness. Stop here for developer review; Phase 5/6, commit/push/merge, deployment, migration/enrollment, branch deletion and production flag activation were not performed and require separate authorization.
