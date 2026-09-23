# Notype codebase health rubric

## Purpose

This is the release and refactoring scorecard for Notype. It exists to prevent a green build, a lower line count, or a visually cleaner design from being mistaken for a better system.

Use it before approving architecture work, after every hardening milestone, and before calling a build production-ready. Every score must cite reproducible evidence. Intent, code volume, and test count are not evidence by themselves.

The companion execution backlog is `planning/codebase-hardening-backlog.md`. The canonical system map is `docs/architecture/system-architecture.md`.

## Non-negotiable rules

1. Preserve behavior before changing structure.
2. Fix known correctness and data-integrity risks before aesthetic refactors.
3. Remove a feature only after its product status, callers, migrations, packaging, tests, and documentation have been audited.
4. Introduce a boundary only when it owns a named responsibility, lifecycle, invariant, or change cadence.
5. Do not split files merely to satisfy a size target. A smaller file with the same tangled state is not an improvement.
6. Every workflow has one state owner and one cancellation/lifetime owner.
7. Current runtime models must not carry legacy fields solely to deserialize old data. Put compatibility in migration adapters.
8. A provider has one canonical descriptor for identity, capabilities, models, readiness, setup, and construction.
9. A passing unit suite does not replace real Windows, model, packaging, accessibility, performance, or recovery evidence.
10. Do not claim support for a feature, language, provider, or operating condition that the current runtime cannot select and complete.
11. Generated-looking indirection is a defect, not polish: reject wrappers, managers, base classes, generic repositories, and new projects that do not remove duplication or establish a real responsibility/lifetime boundary.
12. Prefer small reviewable changes with characterization tests. A large rewrite must prove that incremental replacement cannot meet the same invariant with lower migration risk.
13. A lower-level language is not a performance plan. Optimize the measured stage; require end-to-end, resource, accuracy, deployment, and recovery evidence for runtime changes.

## Severity taxonomy

| Level | Meaning | Required response |
| --- | --- | --- |
| **S0 — Stop ship** | Credible data loss, security/privacy failure, destructive migration, unrecoverable corruption, or a primary workflow that contradicts the product contract. | Freeze release work affecting the area. Fix and add a regression test immediately. |
| **S1 — Critical** | High change-amplification, lifecycle/concurrency ambiguity, major misleading behavior, or a structural defect likely to produce S0 failures. | Make it the next engineering milestone. Do not build substantial new behavior on top of it. |
| **S2 — High** | Material maintainability, reliability, performance, or testability weakness with a safe workaround. | Schedule before the next feature expansion or release-candidate hardening pass. |
| **S3 — Medium** | Local duplication, tooling debt, weak diagnostics, or non-critical repository friction. | Batch into focused cleanup milestones with automated checks. |
| **S4 — Low** | Naming, small dead artifacts, formatting, and low-risk consistency work. | Clean opportunistically; never displace higher-severity work. |

Severity describes urgency, not effort. A five-line data-loss fix outranks a month-long UI decomposition.

## Scoring method

Each category is scored from 0 through 5. Multiply `score / 5` by the category weight. Sum the weighted points for a maximum of 100.

| Score | Maturity | Evidence standard |
| ---: | --- | --- |
| 0 | Undefined | No owner, invariant, or reliable behavior. |
| 1 | Ad hoc | Happy path exists; behavior is implicit, duplicated, or fragile. |
| 2 | Partial | Main path works; important failures or boundaries remain assumption-driven. |
| 3 | Defined | Responsibility and contract are named; representative automated evidence exists. |
| 4 | Verified | Failure, concurrency, performance, packaging, and operational evidence pass for the supported scope. |
| 5 | Sustained | Verification is automated where practical, human gates are current, and regression controls make backsliding difficult. |

### Grade bands

| Total | Grade | Interpretation |
| ---: | --- | --- |
| 93–100 | A | High-quality production system with current, broad evidence. |
| 85–92 | B | Production-ready; remaining debt is bounded and explicitly accepted. |
| 75–84 | C | Release candidate; material hardening remains. |
| 60–74 | D | Functional beta; architecture or operational confidence is insufficient. |
| Below 60 | F | Unsafe to call production-ready. |

### Score caps

- Any open S0 issue caps the total at **59**.
- Any advertised but unreachable or knowingly misleading primary feature caps the total at **69**.
- Any unsupported settings downgrade/forward-version path caps the total at **74**.
- Missing release evidence for a claimed production environment caps the total at **79**.
- Fixture-only model/language validation presented as production proof caps the total at **84**.
- A score above 92 requires current performance, recovery, accessibility, packaging, security, and real-device evidence.

## The 100-point rubric

### 1. Product contract and scope — 7 points

Full-credit evidence:

- One canonical inventory states what Notype supports now, what is experimental, and what is retired.
- The UI, settings schema, runtime selection, installer, tests, and documentation agree with that inventory.
- First-run and settings choices produce the promised runtime behavior.
- Retired capabilities are either removed or isolated in explicitly non-production tooling.
- Every material feature has a named owner and acceptance scenario.

Automatic deductions:

- Minus at least 2 points for every advertised primary capability that normalization or runtime selection makes unreachable.
- Score 0 if the product can silently discard a user choice while reporting that it was applied.

### 2. Architecture, cohesion, and dependency direction — 12 points

Full-credit evidence:

- The production project graph is acyclic and enforced.
- `App` is the only production composition root; UI surfaces do not call static service factories.
- Global dictation, workbench dictation, chat, reading, history, settings, publishing, and process lifecycle have explicit boundaries.
- UI code renders state and forwards intent; coordinators own workflows and policies.
- `Core` contains stable cross-boundary contracts, not an accumulation of unrelated DTOs.
- New assemblies are justified by independent deployment, dependency, security, or lifecycle boundaries.
- Shared UI reuse is compositional: common theme/chrome mechanics are centralized while feature state remains owned by each workflow.

Review heuristics, not mechanical targets:

- Window/control code-behind should normally stay below 500 lines.
- Workflow coordinators should normally stay below 400 lines.
- Methods should normally stay below 50 lines.
- Exceptions require a written cohesion rationale and focused tests.

### 3. Correctness and data integrity — 10 points

Full-credit evidence:

- Settings, history, publishing jobs, caches, and generated output use atomic or recoverable writes.
- Unknown future schemas fail visibly and are never overwritten.
- Every supported migration fixture round-trips to the current schema without losing user intent.
- Invariants are validated at boundaries, not reconstructed differently by several callers.
- Duplicate submissions, retries, cancellation, and partial failures are idempotent or explicitly recoverable.
- Unicode text, document structure, and local history records round-trip without silent corruption.

Score 0 conditions include known destructive migration behavior or silent loss of persisted user data.

### 4. State, concurrency, cancellation, and lifetime ownership — 9 points

Full-credit evidence:

- Every long-running workflow has one state machine or immutable state owner.
- Every timer, event subscription, media player, worker, process, stream, semaphore, and cancellation source has a documented owner and deterministic disposal path.
- Fire-and-forget work terminates at an exception-observing boundary.
- Competing operations have explicit arbitration rules: reject, cancel previous, queue, coalesce, or serialize.
- Shutdown waits for or safely abandons work without corrupting state.
- Concurrency tests cover repeated clicks, rapid settings changes, window closure, process failure, and cancellation races.

### 5. API, contract, and provider design — 7 points

Full-credit evidence:

- Public interfaces represent stable capability boundaries and use typed expected-failure results.
- Provider identity, capabilities, models, setup, readiness, construction, and fallback policy come from one canonical registration.
- Current contracts contain no duplicated legacy aliases.
- Optional behavior is explicit; there are no silent provider, model, device, precision, language, or exact-to-estimated fallbacks.
- Executable-project public surfaces are minimized; internals are not public merely for tests.

### 6. Simplicity, duplication, and dead-code control — 7 points

Full-credit evidence:

- Every executable, tool, script, provider, binary, manifest, and bundled asset has a documented caller or retirement decision.
- Shared mechanisms are consolidated only when they have the same invariants and failure semantics.
- Abstractions have at least two real consumers or a documented boundary/lifetime reason and make the call path easier to follow.
- Dead production code and its tests, packaging paths, documentation, and assets are removed together.
- No redundant generated placeholders or source-adjacent build residue is tracked.
- A clean-checkout source-size budget and publish-size budget are monitored.

Do not award points for deleting compatibility code until supported upgrade paths are proven.

### 7. Performance and resource efficiency — 8 points

Full-credit evidence:

- Startup, first interaction, dictation latency, chat first-token latency, narration real-time factor, OCR throughput, export throughput, and steady-state memory have defined budgets.
- Cold and warm measurements are separated and recorded on the supported CPU baseline and at least one accelerator path where claimed.
- Dictation evidence separates capture finalization, audio preparation, worker/model startup, model inference, normalization/transformation, target restoration/insertion, parent/child memory, and process count.
- Provider/model construction does not eagerly allocate unused runtimes.
- Long documents and recordings use bounded memory, streaming/chunking, and bounded concurrency.
- UI-thread blocking, allocation hotspots, process churn, and repeated I/O are measured before optimization.
- CI guards deterministic micro/contract budgets; scheduled or release gates collect realistic model/device evidence.

### 8. Reliability, resilience, and recovery — 8 points

Full-credit evidence:

- Worker startup, health check, timeout, malformed protocol, crash, restart, and disposal are deterministic.
- Audio device loss, model corruption, network loss during setup/publishing, disk-full, permission denial, and target-window loss have tested recovery behavior.
- Retry policies are bounded, observable, and restricted to transient failures.
- Prepared work is reused only when cache identity proves it still matches inputs.
- The app can restart after a crash without treating partial output as complete.

### 9. Security, privacy, and supply-chain integrity — 8 points

Full-credit evidence:

- Threat model and privacy claims match every current feature and network boundary.
- Secrets and sensitive text are absent from logs, crash bundles, command lines, temporary files, and publishing state unless explicitly protected.
- Local plaintext history is accurately disclosed, relies on Windows account/filesystem access, and is excluded from diagnostics unless explicitly exported.
- Encryption formats that remain for credentials or other protected state are versioned and have tamper/corruption behavior.
- Downloaded models, runtimes, ffmpeg, helper binaries, fonts, and packages have license, provenance, version, and integrity policy.
- Privilege/UIAccess behavior follows least privilege and has signed-release evidence.
- Dependency and action versions are centrally controlled; vulnerability review is part of release evidence.

### 10. Verification and test architecture — 8 points

Full-credit evidence:

- Tests are organized by behavior and risk, with fast deterministic unit tests at the base.
- Integration tests cover serialization, filesystem, process, Windows interop, and composition boundaries.
- A small end-to-end suite covers each primary user journey.
- Structural tests inspect compiled/project structure where practical rather than fragile source strings.
- Coverage is collected and interpreted by risk; mutation or fault-seeding is used on critical policy/state code.
- Flaky, hardware, model, and manual tests are separately labeled and have a scheduled evidence owner.
- A failure identifies one behavior; giant multi-assert smoke tests are decomposed.

### 11. Observability and supportability — 5 points

Full-credit evidence:

- Every primary operation has a correlation/session ID and structured stage, duration, outcome, and failure category.
- Diagnostics distinguish user action, environment failure, model failure, application defect, and cancellation.
- Logs are bounded, redacted, locally discoverable, and exportable.
- Release builds expose version, provider/model identity, runtime readiness, and relevant fallback decisions without sensitive content.
- Support documentation maps user-facing errors to remediation.

### 12. UX, accessibility, and internationalization — 6 points

Full-credit evidence:

- Keyboard-only operation, focus order, screen-reader names, high contrast, text scaling, DPI scaling, reduced motion, and error recovery pass for primary workflows.
- Long text, empty state, narrow windows, localization expansion, RTL, mixed direction, grapheme clusters, and supported scripts are verified.
- Destructive and expensive operations communicate scope, progress, cancellation, and outcome.
- UI state derives from workflow state rather than loosely synchronized Boolean flags.
- First-party windows share theme, continuous-surface, title-bar, spacing, and caption-control primitives without inheriting feature workflows from a common base window.

### 13. Build, packaging, release, and operations — 3 points

Full-credit evidence:

- A clean machine can restore, build, test, publish, package, install, upgrade, uninstall, and roll back through documented commands.
- Production and developer-only executables are separated in solution/release workflows.
- The MSI and setup EXE contents match the single-distribution contract.
- Generated evidence is retained without committing machine-specific residue.
- Release gates are reproducible and failures are actionable.

### 14. Documentation and developer/AI context — 2 points

Full-credit evidence:

- Root README, system map, build guide, product inventory, and current backlog agree.
- The system map fits in a small context window and identifies boundaries, invariants, ownership, runtime flows, and authoritative files.
- Decisions explain why, alternatives, migration, rollback, and expiry/review date.
- Superseded plans are archived or removed; stale claims fail a documentation check.

## Required evidence record

Every grading pass must record:

- Commit SHA and build configuration.
- Date, machine/OS, CPU, RAM, GPU, and relevant runtime/model versions.
- Score per category with links to tests, reports, or manual evidence.
- Open S0–S2 items and accepted risks with owner and expiry date.
- Commands executed and skipped gates with reasons.
- Previous score and an explanation for every changed category.

Use this compact form:

```text
Commit:
Build/date:
Environment:
Total and grade:
Score caps applied:
Category evidence:
Open S0/S1/S2:
Accepted risks and expiry:
Skipped evidence:
Reviewer:
```

## Change-review questions

Before merging a material change, answer:

1. Which named responsibility and invariant does this change affect?
2. Which component owns the resulting state and lifetime?
3. Does it add another source of truth, provider catalog, setting alias, or fallback?
4. What failure, cancellation, concurrency, migration, and rollback paths were tested?
5. What was deleted or simplified?
6. What measurement proves any performance claim?
7. Which rubric category changes, and what evidence justifies the new score?
8. What documentation becomes stale if this behavior changes again?
