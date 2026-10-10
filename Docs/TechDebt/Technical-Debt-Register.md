# VocabularyApp Technical Debt Register

F6 — Spaced Repetition remains the next development priority. This register does not change the existing F6–F17 roadmap priorities.

## TD-001 — Dependency Vulnerability Assessment

**Category:** Security / Dependency Maintenance  
**Priority:** Deferred — Monitor  
**Status:** Open  
**Blocks F6:** No

### Background

During R7 verification, dependency tooling reported 91 vulnerability findings, including 4 critical findings. Their applicability to VocabularyApp has not yet been established.

See the [R7 Phase 8 verification results](../Updates/R7-api-contracts-phase-8-verification-results.md) for the recorded dependency warnings.

### Decision

- Defer a formal dependency vulnerability assessment.
- Do not initiate broad dependency upgrades.
- Continue monitoring security advisories and existing dependency warnings.
- Investigate credible, applicable security threats when identified.
- Do not block F6 development.

### Reassessment triggers

- A confirmed vulnerability affects a production dependency.
- A credible exploit affects our deployed configuration.
- A critical security update requires timely action.
- A dependency becomes unsupported and presents a material security risk.

### Closure criteria

Document the assessment and disposition of relevant findings, including any necessary remediation or explicitly accepted risks.
