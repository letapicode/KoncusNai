# Engineering Decision and Maintainability Rubric

## Purpose

This rubric keeps Koncus Nai understandable as it evolves, including when AI helps produce code. It evaluates the change, its evidence, and its future maintenance cost; it does not attempt to detect how the code was authored.

The objective is not to require a long essay for every edit. The objective is to make consequential decisions explicit, reject unjustified complexity, and leave the next maintainer enough context to safely change the system again.

## Operating rule

Every pull request must have a problem statement, a verification statement, and an honest assessment of its scope. A change that is small and local may use the short form. A consequential change must include a decision record before review completes.

Record the engineering case for the approach that was selected and the material alternatives that were actually considered. Do not fabricate alternatives and do not paste private reasoning or raw model output. The record should contain the decision, constraints, evidence, rejected options, and trade-offs that another engineer needs to evaluate the change.

## First classify the change

| Level | Use when | Required record |
|---|---|---|
| Local | A contained bug fix, refactor, test, copy change, or internal implementation detail with no contract, persistence, security, or module-boundary impact. | Short form: problem, approach, verification, and why the scope stayed local. |
| Significant | A feature or behavior change, a shared contract change, a notable performance/reliability trade-off, or a change touching more than one production module. | Short form plus a concise decision record. |
| Architectural | A new dependency, project reference, persistence/schema change, public/shared contract, security/privacy/privilege change, installer/model-delivery change, or a replacement of a cross-cutting mechanism. | Full decision record and reviewers appropriate to the affected boundary. |

When uncertain, classify upward. Reclassification is not a failure; it is how the review catches hidden scope.

## Non-negotiable stop conditions

Do not approve a change while any applicable item below is unanswered, failed, or deferred without an owner and due date.

1. **Unclear outcome.** The requested behavior and acceptance criteria cannot be stated plainly.
2. **Unexplained boundary breach.** The change violates the contract-oriented module structure or needs a new project reference without an approved architectural rationale. See [project reference guardrails](../architecture/project-reference-guardrails.md).
3. **Unprotected risk.** A change to privacy, local-processing guarantees, elevation/UIPI, model integrity, installation, or external input has no threat/risk assessment and appropriate test or evidence.
4. **Evidence gap.** Tests, manual checks, benchmark evidence, or a justified reason they cannot exist are missing.
5. **Semantic duplication or dead path.** The change adds a second source of truth, a parallel implementation, unused abstraction, silent fallback, or speculative extension without a clear retirement/ownership plan.
6. **Unmanaged compatibility cost.** It changes persisted data, an interface, a user workflow, installer behavior, or a supported compatibility surface without migration, compatibility, or rollback handling.
7. **Score failure.** An applicable rubric category scores `0`, or the total is below the required threshold.

## The scoring rubric

Score every applicable category `0`, `1`, or `2`. Mark categories that truly do not apply as `N/A` and state why. A high total never compensates for a zero in an applicable category.

| Category | 0 - stop | 1 - needs review judgment | 2 - ready |
|---|---|---|---|
| Intent and scope | Problem, user impact, or non-goals are unclear. | Intent is present but scope/risk is imprecise. | Clear outcome, boundaries, and acceptance criteria; diff matches them. |
| Design and boundaries | Bypasses contracts, creates a cycle, or mixes layers. | Boundary impact is plausible but insufficiently explained. | Uses existing contracts/boundaries or records why a change is necessary. |
| Simplicity and cohesion | Adds speculative abstraction, duplicate flow, or unrelated cleanup. | Some indirection is justified but could be reduced. | Smallest coherent design; one source of truth; responsibilities are easy to name. |
| Correctness and failure behavior | Happy path only, undefined failure behavior, or data loss risk. | Important failures are handled but edge cases need reviewer attention. | Success, failure, cancellation, and recovery behavior are explicit and covered where relevant. |
| Verification and evidence | No relevant check and no explanation. | Partial automated/manual evidence. | Relevant tests pass; targeted manual, performance, soak, fault, or compatibility evidence is included when warranted. |
| Maintainability and readability | Opaque naming, dense logic, magic values, or a hard-to-change design. | Readable but needs comments, extraction, or a follow-up. | Intent-revealing names, focused units, localized complexity, and comments explain non-obvious *why*. |
| Compatibility and lifecycle | Breaks callers/data/users or lacks rollback. | Compatibility effect exists but mitigation is incomplete. | Migration, versioning, fallback/rollback, and deprecation ownership are explicit when applicable. |
| Security, privacy, and operations | Relevant boundary is ignored. | Risks are identified but evidence/mitigation is incomplete. | Least privilege, local-processing/security rules, diagnostics, and operability are preserved or deliberately updated. |
| Dependency and cost discipline | New dependency/runtime cost is unjustified or untracked. | Rationale exists but maintenance/license/security cost is incomplete. | Existing capability reused where suitable; any new dependency follows the manifest, license, security, and ownership rules. |

### Decision thresholds

- **Local change:** no applicable `0`; at least 75% of available points; reviewer can explain the behavior from the diff and short form.
- **Significant change:** no applicable `0`; at least 80% of available points; decision record is complete.
- **Architectural change:** no applicable `0`; at least 85% of available points; decision record includes alternatives, migration/rollback, and boundary-owner review.

These are review thresholds, not a way to turn engineering into scorekeeping. A reviewer may request a stronger design even when the numeric threshold passes, and may approve a well-evidenced exception only when the record names the risk, owner, and due date.

## Decision record format

Use this format in the pull request description. Copy it to a durable architecture or release document when the decision will guide future changes.

```md
### Decision record

**Decision:** What we are doing and what outcome it produces.

**Context and constraints:** User/problem context, invariants, affected modules,
compatibility constraints, and out-of-scope work.

**Approaches considered:**
1. Selected approach - why it best satisfies the constraints.
2. Rejected approach(es) - material trade-off or evidence that ruled each out.

**Consequences:** Benefits, accepted costs/risks, and any temporary compromise.

**Verification:** Automated tests, manual scenarios, performance/soak/fault/compatibility
checks, or an explicit reason a check is not feasible.

**Lifecycle:** Migration, rollback, cleanup/deprecation owner and due date, if applicable.
```

For a local change, the short form is enough:

```md
**Problem and scope:** ...
**Approach:** ...
**Why this is the smallest safe change:** ...
**Verification:** ...
```

## Review flow

1. Author classifies the change before implementation or before asking for review.
2. Author writes the short form or decision record from observable engineering facts.
3. Author scores applicable categories and links evidence.
4. Reviewer checks stop conditions first, then challenges the selected approach and score.
5. Reviewer records approved exceptions with a named owner and due date.
6. Architectural decisions that remain relevant after merge are promoted to `docs/architecture/`; release-risk decisions go to `docs/release/`.

## Evidence by risk

Use the smallest credible evidence set that matches the risk; do not run unrelated checks merely to inflate a checklist.

| Change characteristic | Minimum evidence to consider |
|---|---|
| Deterministic domain logic | Focused unit tests covering normal and failure/edge behavior. |
| Contract or module interaction | Unit/integration tests at the boundary plus project-reference guardrail validation if references change. |
| Windows interop, insertion, hotkeys, audio, or UI thread behavior | Automated coverage where feasible and a documented manual Windows scenario if environment-dependent. |
| Latency, repeated work, or resource use | Performance regression or benchmark evidence against the relevant budget. |
| Recovery, long-running coordination, or external process handling | Fault-injection and/or soak evidence relevant to the failure mode. |
| Settings, model metadata, installer, or persisted data | Migration/compatibility checks and a rollback or recovery path. |
| Privacy, local processing, privilege, dependency, or bundled binary change | Security/compliance checks plus the relevant architecture/security documentation update. |

The existing [quality strategy](test-strategy.md) and CI workflow remain the source for executable release gates. This rubric makes sure a reviewer knows which evidence should be relevant before relying on those gates.

## AI-assisted contribution rules

AI assistance is allowed, but it does not lower the bar or become the explanation for a design. The author remains accountable for the merged code.

- Treat generated code as an untrusted draft: read it, simplify it, and verify it against the product's contracts and failure modes.
- Do not accept an abstraction because it sounds general. Name the current callers and the likely next change it makes cheaper; otherwise keep the design local.
- Do not paste generated tests that merely mirror implementation details. Tests should demonstrate a behavior, invariant, regression, or boundary contract.
- Do not invent citations, benchmark results, alternatives, or test outcomes. Unknown is an acceptable answer; unsupported certainty is not.
- Prefer deletion, consolidation, and direct use of an existing contract over another wrapper, helper, option, or configuration path.
- Keep comments for durable rationale, surprising constraints, or non-obvious trade-offs. Remove comments that narrate obvious syntax.

## Examples of healthy challenges

- "Why is a new interface safer than extending the existing contract? Which two callers need different implementations?"
- "Could this recovery path be represented in the typed result already used by the coordinator?"
- "What happens when this cancellation races with process output or UI dispatch?"
- "What evidence shows the fallback is reachable and does not silently mask an error?"
- "If this schema changes, how does an existing user recover or roll back?"
- "Which existing test or guardrail would fail if this boundary is accidentally crossed again?"

## Ownership and evolution

The pull-request author completes the record. The reviewer verifies it against the diff and may require a higher change classification. Maintainers update this rubric when an incident, review pattern, or quality-suite gap exposes a missing control.

Use the rubric to make disagreement productive: challenge assumptions, retain evidence, and choose the smallest design that safely solves the real problem.
