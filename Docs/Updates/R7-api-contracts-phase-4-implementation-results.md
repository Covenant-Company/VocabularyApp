# R7 Phase 4 — Provider Structural Failure Boundary

Date: 2026-10-01

Status: **IMPLEMENTED — EXECUTION VERIFICATION PENDING**

This records Phase 4 only under D1–D4 in the [approved implementation plan](R7-api-contracts-remediation-implementation-plan.md), addressing F03 and provider portions of F02/F12 from the [analysis](R7-api-contracts-remediation-analysis.md). The [Phase 1](R7-api-contracts-phase-1-implementation-results.md), [Phase 2](R7-api-contracts-phase-2-implementation-results.md), and [Phase 3](R7-api-contracts-phase-3-implementation-results.md) records remain historical checkpoints. Their execution verification remains pending; their statements that later phases had not begun describe those checkpoints. This is not overall R7 completion or production validation.

## Scope and files

| File | Phase 4 change |
|---|---|
| `VocabularyApp.WebApi/DTOs/External/WordsApiDtos.cs` | Honest nullable provider collections/elements; optional collection defaults preserve omission behavior. |
| `VocabularyApp.WebApi/Services/WordService.cs` | Complete structural validation before mapping/tracking, narrow transport exception handling and safer provider diagnostics. |
| `VocabularyApp.WebApi.Tests/Integration/DictionaryProviderContractTests.cs` | New provider contract and persistence assertions using the existing relational host and controlled handler. |
| `Docs/Updates/R7-api-contracts-phase-4-implementation-results.md` | This implementation record. |

No existing test or test infrastructure file was changed. Earlier working-tree changes were retained. No Angular production code, public DTO, controller, shared error helper, configuration, entity/context, migration/snapshot, dependency, CI/CD or release safeguard file was edited in this phase.

## Provider structure and classification

Typed System.Text.Json deserialization remains in use; no new library, global serializer setting or dynamic parser was introduced. Results are nullable with nullable elements and no fabricated default. Pronunciation and examples are nullable collections initialized empty, allowing omission while distinguishing explicit null. Their nullable string values/elements preserve existing absent/blank optional-content handling. Invalid JSON property types are rejected during deserialization.

| Condition | Result |
|---|---|
| Usable canonical cache hit | Existing 200 response, no provider request. |
| Genuine provider HTTP 404 | NotFound / `word_not_found`, public 404 with `No definitions found.` |
| Provider 401, 403, 429, 5xx or other unsuccessful non-404 status | ServiceUnavailable / `dictionary_unavailable`, public 503. |
| HttpRequestException, provider cancellation/timeout, IOException during transport/content reading | Same safe 503. TaskCanceledException is covered by OperationCanceledException. |
| Malformed JSON, unsupported typed JSON, null/non-object root, missing/null/blank canonical word | Same safe 503. |
| Missing, empty or null results; any null result element | Same safe 503 before tracking records. |
| Explicit-null examples or pronunciation; wrong collection/value type | Same safe 503 before tracking records. |
| Omitted or empty optional examples/pronunciation | Accepted; public example/pronunciation may be null. Null/blank optional strings remain ignorable under the existing mapping. |
| All parts of speech unsupported or all definitions missing/null/blank | Same safe 503; no invented Noun or substitute definition. |
| Valid supported entries mixed with structurally valid unsupported POS or blank definitions | Existing supported entries succeed; unsupported/unusable content is skipped. |
| Any structurally malformed entry mixed with otherwise valid entries | Entire response rejected as 503, including malformed entries that content filtering would otherwise skip. |
| Valid provider response followed by local persistence failure | Retained Phase 3 safe 500 `internal_error`; not classified as provider unavailable. |

All unavailable cases reuse the Phase 3 six-key application error envelope and the exact message: `Dictionary service is temporarily unavailable. Please try again.` The public response does not echo upstream status bodies, URLs, host, headers, credentials, exceptions or schema diagnostics. Provider exception logging now records only exception type, without the exception object/message. Unsupported-POS logging no longer copies upstream POS or canonical word text. Existing status diagnostics retain numeric status and the normalized lookup term; no full provider body is logged by this boundary.

## Persistence, cache and successful contract

Validation covers the entire deserialized collection before supported-definition filtering and before `_db.Words.Add` or `_db.WordDefinitions.Add`. No canonical Word, WordDefinition, POS or UserWord is staged by a structural-failure branch. Mapping requires at least one supported nonblank definition before staging. No new transaction, schema or persistence architecture was introduced. The authored failure cases assert empty canonical and UserWord tables and unchanged seeded POS identities/names/abbreviations in private SQLite; these assertions have not executed.

The DB-first query and membership handling remain intact. The new success cases persist a canonical word, replace the provider fixture with an exception, and assert a subsequent identical-word response is served from cache with only one total provider request. Existing initial-cache-hit and membership tests remain unchanged.

The GET `/api/words/lookup/{word}` route and successful envelope, nested flags, local IDs, casing and public word/definition properties remain unchanged. Existing trim behavior, case-insensitive supported POS matching, pronunciation selection and definition ordering remain. AudioUrl is still explicitly null for imported words; examples/synonyms are not substitutes for definitions. Lookup does not add personal vocabulary entries. The canonical save-failure test from Phase 3 remains applicable.

Provider host/key configuration, environment variables, AddHttpClient registration and the existing 10-second timeout are unchanged. No live provider call was made.

## Authored tests and existing expectations

`DictionaryProviderContractTests.cs` contains **6 theory methods / 46 InlineData cases**, counted statically, not discovered or executed:

| Coverage | Cases |
|---|---:|
| Unusable root, word, results, definitions and unsupported POS | 18 |
| Malformed entry after a valid entry, including filtered-out content | 8 |
| Malformed pronunciation with otherwise valid data | 4 |
| Genuine 404 and unsuccessful non-404 statuses, with untrusted body sentinel | 9 |
| Network, timeout, cancellation and stream failure, including logging secrecy | 4 |
| Successful mixed content, omitted/empty optional collections, optional strings, exact public shape, persistence and cache | 3 |

Tests reuse VocabularyAppWebApplicationFactory, private SQLite, real authentication helpers, ControllableDictionaryHandler, JsonContractAssert, ApiErrorContractAssert and CapturingLogger. Every new failure case checks Words, WordDefinitions, UserWords and unchanged POS data. Success cases check canonical identity, mapped content, nullable audio, no personal vocabulary insertion and no extra POS rows. Existing dictionary lookup tests, Phase 1 successful contract tests and Phase 3 safe error/save-failure tests remain unchanged.

**No existing test expectation was edited.** The intentional production correction is explicit-null collections/elements moving from the Phase 3 generic 500 fallback to 503 `dictionary_unavailable`. Existing genuine-404 and known provider-503 expectations remain. Additional IOException and OperationCanceledException coverage extends the narrow provider boundary; ordinary local database/programming failures still use the outer internal-error boundary.

## Static review, limitations and deferred work

The full Phase 4 source changes were reviewed for classification, null handling, validation ordering, public shape preservation, logging and scope. Git whitespace/status checks were used; new untracked files were checked separately. No static implementation blocker was identified. Compilation, serialization behavior, relational assertions and runtime outcomes remain unverified.

The service/controller lookup signature does not carry a client CancellationToken. This phase therefore does not distinguish client-aborted requests from provider cancellation or introduce cancellation propagation. Timeout testing is an authored deterministic exception fixture, not an elapsed-time measurement. SQLite fixtures do not establish SQL Server/IIS behavior. Concurrent canonical insert recovery, provider extraction and wider logging policy remain outside this change.

Phase 5 vocabulary remediation, Phase 6 quiz contracts, Phase 7 OpenAPI/cleanup and Phase 8 execution verification remain deferred. R3 authentication, R4 quiz behavior, R5 `(UserId, WordId)` identity, R6 infrastructure and PSH1 remain unchanged by this phase. No database migrations or dependency changes were introduced.

**NO TESTS WERE RUN. NO BUILDS WERE RUN. NO RESTORES WERE RUN.** No test discovery, compiler, package installation, application startup, local HTTP request, live API/provider call, database/EF command, application/API/provider/database verification, CI script or deployment script was executed. HTTP/database operations in the new tests are source for future authorized execution only.

All changes remain uncommitted and unstaged for review. No commit, push, merge, pull request, rebase, reset, revert, clean, deployment or GitHub settings change occurred. Work stops after Phase 4: **IMPLEMENTED — EXECUTION VERIFICATION PENDING**.

## Later verification — R7 Phase 8 (October 2, 2026)

The pending/no-execution statements above describe this phase at its original completion and remain historical evidence. The cumulative R7 implementation was subsequently execution-verified in Phase 8: final full backend suite **454 passed, 0 failed, 0 skipped**; final full Angular suite **193 passed, 0 failed, 0 skipped**; Release solution and Angular production builds passed. Phase 8 corrected anonymous Swagger serialization, stale owned-definition and SQLite timestamp assertions, and malformed XML documentation; see the complete failure history and warnings in [R7 Phase 8 verification results](R7-api-contracts-phase-8-verification-results.md).

Current cumulative status: **R7 API Contracts Remediation — IMPLEMENTED AND EXECUTION VERIFIED**, within the local verification limits recorded there. Existing dependency vulnerabilities remain a separate release-review concern. No production access, live dictionary-provider calls, commit, push or deployment occurred. This later note does not claim execution took place during the original phase.
