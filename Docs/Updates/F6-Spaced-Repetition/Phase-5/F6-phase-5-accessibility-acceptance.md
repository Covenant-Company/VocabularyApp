# F6 Phase 5 accessibility acceptance

Date: 2026-10-13. This is a verification checklist, not production release authorization. Use only an explicitly isolated frontend/backend with synthetic vocabulary/accounts and test-only review configuration. Do not point these checks at production or alter production flags.

## Executed automated checks

ChromeHeadless/Karma exercised the real Angular template/styles with mocked HTTP: labelled fieldset/legend and four same-name native radios, effective disabled state, option-zero form submission, prompt focus, polite accepted-feedback live region, error/loading markup, wrapping in a 320px **container**, 100-character words/1,000-character definitions, no container horizontal overflow, minimum 44px button/link/option-card targets and a visible focus-within outline. Recorded ASP.NET HTTP/OpenAPI evidence was validated by Angular's real parsers separately.

These are browser DOM/layout tests. They are **not** physical keyboard/touch or real assistive-technology acceptance. A 320px container is not a claim of a real 320px mobile viewport; CSS/browser zoom was not treated as proof of 200% browser zoom.

## Developer manual acceptance checklist — all PENDING

Record tester, date, isolated application revision/URL, browser/OS/device, screen reader/version and observed result per row. A row becomes passed only after actual execution. NVDA, JAWS, VoiceOver and physical devices were not used during this task.

| Check | Steps in isolated test environment | Expected result | Current status |
|---|---|---|---|
| Protected entry and initial focus | Sign in with a synthetic user; use Tab/Enter on dashboard “Review Vocabulary”; navigate back and reopen | Correct protected route, readable heading/counts and no hidden-focus trap | Pending |
| Native radio keyboard flow | Start a ready session; Tab to the radio group; use arrow keys/Space to select option zero; Tab to Submit; press Enter | Native selection works; zero remains selected; selection alone sends nothing; one submission | Pending |
| Focus and accepted feedback | Submit correct and incorrect answers; wait for feedback; use Next; repeat through completion | “Correct”/“Incorrect”, correct answer and schedule announced without relying on color; focus moves to next prompt; accepted choices cannot be changed | Pending |
| Screen-reader structure | With a named actual reader, navigate headings, counts, fieldset/legend, labels and progress | Meaningful labels/order; four distinct option labels; accepted and pending progress truthful | Pending |
| Loading/error announcements | Delay an answer; interrupt networking after send; retry; inject disabled/401/expired/stale responses only through local test tooling | Loading/alerts/live feedback announced; original selection/key retained during uncertainty; no silent new session; sign-in/restart actions reachable | Pending |
| Resume/refresh | Answer one item; refresh; repeat after simulating a lost acknowledgement in isolated tooling | Pending replay precedes resume; option ordering/accepted feedback preserved; expiration does not erase committed outcomes | Pending |
| True 320px viewport | Set browser responsive viewport to width 320px; inspect overview/backlog/question/error/completion with long text | No horizontal page overflow; content readable; controls remain at least 44px high and reachable | Pending |
| Actual 200% browser zoom | At desktop width use the browser's own 200% zoom, not CSS zoom; inspect every state and keyboard flow | Content reflows without clipping/overlap or lost controls/focus | Pending |
| Touch and mobile | Use a named physical phone/tablet, including portrait/landscape; select/submit/load more/resume | No accidental duplicate submits; comfortable targets; wrapping definitions and dates | Pending |
| Disabled and blocked states | In isolated configuration return reviews_disabled, blocked words and all availability reasons; refresh status | Disabled is not zero due; blocked backlog entries visible; unavailable explanations and safe retry/navigation accessible | Pending |

No SQL Server target, production test identity, external dictionary access or deployment is needed or authorized by this checklist. If a manual setup requires additional infrastructure or dependencies, obtain its separate authorization. Full browser-to-backend E2E remains pending because this repository has no configured E2E framework and none was added.
