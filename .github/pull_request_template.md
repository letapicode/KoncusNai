## Change summary

What user or system outcome does this change deliver? What is deliberately out of scope?

## Change classification

- [ ] Local
- [ ] Significant
- [ ] Architectural

Use the [Engineering Decision and Maintainability Rubric](../docs/quality/engineering-decision-and-maintainability-rubric.md). Reclassify upward if the change affects a contract, project reference, persistence/schema, security/privacy/privilege, dependency, installer/model delivery, or cross-cutting behavior.

## Engineering case

For a local change, complete this short form:

- **Problem and scope:**
- **Approach:**
- **Why this is the smallest safe change:**
- **Verification:**

For a significant or architectural change, replace the short form with the full decision record:

- **Decision:**
- **Context and constraints:**
- **Approaches considered:**
  1. Selected approach and why:
  2. Rejected material alternative(s) and why:
- **Consequences and accepted risks:**
- **Verification evidence:**
- **Lifecycle (migration, rollback, cleanup owner/due date if applicable):**

Do not fabricate alternatives, citations, or test results. Record the engineering rationale that a future maintainer needs; do not paste private reasoning or raw AI output.

## Rubric score

| Category | Score (0/1/2/N/A) | Evidence or note |
|---|---:|---|
| Intent and scope |  |  |
| Design and boundaries |  |  |
| Simplicity and cohesion |  |  |
| Correctness and failure behavior |  |  |
| Verification and evidence |  |  |
| Maintainability and readability |  |  |
| Compatibility and lifecycle |  |  |
| Security, privacy, and operations |  |  |
| Dependency and cost discipline |  |  |

- Applicable total: `__/__`
- Any applicable zero? If yes, describe the blocking issue or approved exception with owner/due date:

## Review checklist

- [ ] The change matches the stated outcome and has no unrelated cleanup.
- [ ] Existing contracts and project-reference guardrails are respected, or the exception is documented.
- [ ] Relevant automated checks passed; required manual/performance/soak/fault/compatibility evidence is attached or explicitly deferred.
- [ ] New dependency, persistence, security/privacy, installer, model, and privilege effects have been assessed where applicable.
- [ ] Code, tests, diagnostics, and comments explain durable intent rather than generated syntax.
