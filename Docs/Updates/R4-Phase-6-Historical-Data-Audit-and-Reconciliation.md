# R4 Phase 6 — Historical Data Audit and Conditional Reconciliation

## Decision

Recommendation: **audit first, then conditionally reconcile**.

No reconciliation operation is implemented in this phase. The target database has not been inspected, and repository evidence shows several ways in which surviving `QuizResult` rows may not represent complete or unambiguous lifetime history. Any data-changing reconciliation requires review of the audit output and separate explicit approval.

## What Can Be Reconstructed

For a surviving, owner-consistent set of `QuizResult` rows belonging to one `UserWord`, the schema contains the fields needed to derive:

| `UserWord` field | Derivation from surviving results |
|---|---|
| `TotalAttempts` | `COUNT(*)` |
| `CorrectAnswers` | count where `IsCorrect = 1` |
| `LastReviewedAt` | `MAX(AttemptedAt)` |
| `LastCorrectAt` | `MAX(AttemptedAt)` where `IsCorrect = 1`, otherwise `NULL` |

`QuizResult.UserId`, `UserWordId`, `QuizSessionId`, `IsCorrect`, and `AttemptedAt` are required columns in the current model. `QuizResult.UserWordId` references `UserWords.Id`, and `QuizResult.UserId` independently references `Users.Id`. The audit therefore accepts a row as owner-consistent only when `QuizResult.UserId = UserWord.UserId`.

This reconstructs the aggregate implied by surviving, accepted result rows. It does **not** prove the user's complete historical learning state.

## Historical Limitations

- Deleting a `UserWord` cascades deletion to its `QuizResult` rows. Deleted history cannot be measured or recovered from this schema.
- Before R4 Phase 3, quiz submission persisted results without updating the four `UserWord` aggregates. Stored and derived values are therefore expected to disagree for some historical data.
- Before R4 Phase 5, the database did not prevent duplicate `(UserId, QuizSessionId, UserWordId)` rows. A duplicate cannot be assumed invalid without reviewing how it was produced.
- `QuizResult.UserId` and `UserWordId` are independent foreign keys. The schema prevents missing referenced users/words under normal foreign-key enforcement, but it does not itself require both rows to have the same owner.
- The `UserWord` foreign key uses cascade delete, so an absence of results may mean either no attempts or lost history. Resetting such a row to zero/null would be an unsupported assumption.
- The February 2026 `AddQuizSessionIdToQuizResult` migration assigned one generated session ID to all legacy rows sharing the same exact `AttemptedAt`. That grouping did not include `UserId`, so a legacy session ID can span users when timestamps coincide. It also cannot prove the original client-side session boundary.
- `QuizResult` existed with `IsCorrect` and `AttemptedAt` from the initial migration, but source structure alone cannot prove that every historical write was correct, complete, or produced by the current Rule A semantics.
- `UserAnswer = NULL` is valid for unanswered R4 questions. Null/empty prevalence is diagnostic and is not, by itself, corruption.
- Cascade-deleted results and any activity that occurred before result persistence are irrecoverable from this database. Full lifetime accuracy therefore cannot be guaranteed.

## Audit Artifacts

- `docs/Updates/R4-Phase-5-Quiz-Result-Duplicate-Audit.sql` is the focused deployment gate for the Phase 5 unique index.
- `docs/Updates/R4-Phase-6-Historical-Quiz-Audit.sql` is the comprehensive SQL Server audit. It is read-only and intentionally omits answer text.

The Phase 6 audit reports:

1. result inventory, date range, and empty session identifiers;
2. duplicate Phase 5 uniqueness keys;
3. `QuizResult`/`UserWord` ownership mismatches;
4. missing referenced users or `UserWord` rows;
5. stored and owner-consistent derived aggregates for every surviving `UserWord`;
6. negative or internally impossible counters;
7. stored and derived timestamp inconsistencies;
8. null/empty answer prevalence split by correctness, without classifying unanswered rows as corrupt;
9. session IDs spanning multiple users;
10. sessions with more than 20 results, repeated words, or multiple attempt timestamps;
11. results dated before the vocabulary entry was added; and
12. nonzero learning state with no surviving result history.

Production uses SQL Server, so the audit uses SQL Server syntax. SQLite is the integration-test provider and is not the target of this operational audit.

## Anomaly Classification and Approval

| Category | Default classification | Required action |
|---|---|---|
| Duplicate uniqueness keys | Blocking | Do not apply the Phase 5 index; determine which rows, if any, are duplicates under an explicitly approved policy. |
| Ownership mismatch | Blocking | Investigate provenance; do not use the row for another user's aggregates. |
| Orphaned reference | Blocking | Investigate database integrity and foreign-key enforcement. |
| Impossible counters | Blocking for reconciliation | Determine whether stored state or history is authoritative. |
| Stored/derived mismatch | Reviewable | Expected for some pre-R4 history, but not safe to overwrite without assessing completeness. |
| Timestamp mismatch | Reviewable | Establish whether history is complete and whether legacy timestamps are trustworthy. |
| Null/empty answer | Informational | Valid under Rule A; investigate only unusual correct/null combinations or provider-specific legacy behavior. |
| Multi-user or unusual session shape | Reviewable, possibly blocking | Account for the legacy timestamp-based session backfill before judging the session. |
| Nonzero state without results | Irrecoverable/ambiguous | Do not automatically reset; history may have been cascade-deleted or never persisted. |

The audit produces evidence; it does not choose an authoritative duplicate, delete rows, or modify aggregates.

## Conditional Reconciliation Design

Reconciliation is deferred pending audit review. If later approved, it must be a separate maintenance operation that:

- uses only an explicitly approved set of owner-consistent results;
- applies an approved duplicate policy rather than silently counting or deleting duplicates;
- assigns derived values instead of incrementing stored values;
- defines how zero-result and irrecoverable-history rows are treated;
- records the deployment cutoff and whether pre-cutoff values represent lifetime or forward-only accuracy;
- captures before/after values and affected-row counts;
- is run first on a restorable staging copy; and
- is idempotent, with a second run changing zero rows.

Until those decisions are approved, no reconciliation SQL or application code should be created or run.

## Phase 5 Migration Readiness

`20260819000000_AddQuizResultSubmissionUniqueness` is not currently proven safe for an existing database. Source correctness is insufficient; target data must pass review.

At minimum:

1. Run the focused Phase 5 duplicate audit.
2. Run the comprehensive Phase 6 audit.
3. Stop if any duplicate uniqueness groups, ownership mismatches, or integrity blockers appear.
4. Review ambiguous session and aggregate findings.
5. Obtain explicit approval for any cleanup or reconciliation policy.
6. Re-run both audits after approved cleanup.
7. Apply the unique-index migration only when the duplicate query returns zero rows and deployment is approved.

The migration must never be used as a discovery mechanism: allowing index creation to fail partway through deployment is not an audit strategy.

## Recommended Deployment Sequence

1. Commit/review the R4 source changes and record the application commit/version.
2. Choose and record the R4 deployment cutoff timestamp.
3. Back up the target database and verify recovery capability.
4. Run both audit scripts against a restored or staging copy first.
5. Run both scripts against the intended target using the approved read-only workflow.
6. Preserve and review the outputs without answer text or other unnecessary sensitive data.
7. If blocking or ambiguous anomalies exist, stop. Approve a cleanup, conditional reconciliation, or forward-only policy separately.
8. If reconciliation is approved, implement and test it separately, capture before-values, run it twice in staging, and verify the second run is a no-op.
9. Re-run the audits and require zero duplicate uniqueness groups.
10. Apply the Phase 5 uniqueness migration in an approved non-production environment and verify the index and duplicate-key behavior.
11. Apply the approved database change and deploy the compatible application in the controlled production sequence.
12. Run focused smoke checks and monitor duplicate/persistence failures.

If reconciliation is rejected or history is materially incomplete, retain historical values under an explicitly documented policy and treat aggregates as reliably maintained only from the recorded R4 cutoff forward. Do not label them as reconstructed lifetime totals.

## Manual Approval Points

Explicit approval is required before:

- selecting or deleting any duplicate row;
- excluding anomalous rows from derived aggregates;
- assigning reconstructed values to `UserWords`;
- resetting zero-result words;
- declaring a cutoff-based forward-only policy;
- applying the Phase 5 uniqueness migration to an existing database; or
- deploying the resulting database/application change.

## Remaining Risks

- Surviving rows may be internally valid but historically incomplete.
- Legacy session IDs are approximate because of timestamp-only backfill.
- Deleted history cannot be recovered.
- Duplicate cleanup may change both result history and the correct aggregate derivation.
- R5 may later change `UserWord` identity and how histories are merged or reassigned.
- Audit queries can be expensive on a large database; run on staging first and use the approved operational window for the target.

