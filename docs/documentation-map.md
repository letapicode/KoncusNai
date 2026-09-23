# Canonical documentation map

This map defines where current repository facts are owned. Start with the
[README](../README.md), then use the owner below instead of copying a contract
into another document. Dated evidence and historical plans remain useful only
for the commit or decision context they identify.

## Canonical owners

| Concern | Authoritative owner | Supporting entry point |
| --- | --- | --- |
| Repository navigation | [README](../README.md) | This map |
| Supported and retired product capabilities | [Product capability matrix](architecture/product-capability-matrix.md) | [Implemented features](guides/implemented-features.md) |
| Components, dependencies, state, and lifetime | [System architecture](architecture/system-architecture.md) | [Project-reference guardrails](architecture/project-reference-guardrails.md) |
| Intentional compiled APIs and assembly dependency evidence | [Public API and dependency contract](architecture/public-api-and-dependency-contract.md) | [Compiled baseline](architecture/public-api-baseline.json) |
| Contributor setup and command index | [Developer guide](guides/developer-guide.md) | [Test strategy](quality/test-strategy.md) |
| Test taxonomy and automated gates | [Test strategy](quality/test-strategy.md) | [Engineering rubric](quality/engineering-decision-and-maintainability-rubric.md) |
| Security threats and local-processing guarantees | [Threat model](security/threat-model.md) and [local-processing guarantees](security/local-processing-guarantees.md) | [Supply-chain provenance](security/supply-chain-provenance.json) |
| Dependency, model, runtime, asset, and CI-action provenance | [Supply-chain provenance](security/supply-chain-provenance.json) | [Dependency manifest](dependency-manifest.md) and [license inventory](license-inventory.md) |
| Installer payload | [Installer packaging](release/installer-packaging.md) | [Release checklist](release/release-checklist.md) |
| Tracked, bundled, publish, installer, and external footprint budgets | [Size budget policy](release/size-budget-policy.json) | [Size budgets and evidence](release/size-budgets.md) |
| Release execution order | [Release checklist](release/release-checklist.md) | [Test strategy](quality/test-strategy.md) |
| Hardening item scope and status | [Hardening backlog](../planning/codebase-hardening-backlog.md) | [Hardening ledger](../planning/codebase-hardening-ledger.md) |
| Packet sequence, commits, and outstanding gates | [Hardening ledger](../planning/codebase-hardening-ledger.md) | [Delegation plan](../planning/codebase-hardening-delegation-plan.md) |
| Actual shell and Dictation History workflow operator observations | [Window-shell manual evidence](quality/window-shell-manual-evidence.md) | [Window-shell inventory](architecture/window-shell-inventory.md) and [test strategy](quality/test-strategy.md) |

When a supporting document summarizes a fact, it links to the owner. It does
not become a second authority. Machine-readable policy owns facts that are
enforced mechanically; prose explains the policy and its operational meaning.

## Planning document disposition

| Document | Disposition | Reason |
| --- | --- | --- |
| [`planning/codebase-hardening-backlog.md`](../planning/codebase-hardening-backlog.md) | Current canonical | Owns item scope and completion status. |
| [`planning/codebase-hardening-ledger.md`](../planning/codebase-hardening-ledger.md) | Current canonical | Owns packet sequence, commits, evidence links, and next action. |
| [`planning/codebase-hardening-delegation-plan.md`](../planning/codebase-hardening-delegation-plan.md) | Current supporting | Owns packet dependencies and bounded packet descriptions; completed objectives may use historical wording. |
| [`planning/executable-ownership-and-distribution-audit.md`](../planning/executable-ownership-and-distribution-audit.md) | Current supporting | Records the WP-13/WP-28 executable disposition evidence. |
| [`planning/cache-inventory-and-key-matrix.md`](../planning/cache-inventory-and-key-matrix.md) | Current supporting | Records implemented cache ownership and key invariants. |
| [`planning/process-lifecycle-and-fault-matrix.md`](../planning/process-lifecycle-and-fault-matrix.md) | Current supporting | Records worker and process lifetime evidence. |
| [`planning/workbench-wp03-ownership-inventory.md`](../planning/workbench-wp03-ownership-inventory.md) | Current supporting | Records the completed Workbench extraction inventory. |
| [`planning/global-insertion-hardening-plan.md`](../planning/global-insertion-hardening-plan.md) | Historical | Dated design record retained for insertion rationale; current contracts are owned elsewhere. |
| [`planning/indic-parler-tts-integration-plan.md`](../planning/indic-parler-tts-integration-plan.md) | Historical | Dated implementation plan retained for TTS decision history. |
| [`planning/kokoro-local-read-aloud-plan.md`](../planning/kokoro-local-read-aloud-plan.md) | Historical | Dated implementation plan retained for read-aloud decision history. |
| [`planning/minimal-ui-and-reading-studio-refinement-plan.md`](../planning/minimal-ui-and-reading-studio-refinement-plan.md) | Historical | Dated UI proposal retained for design history, not current scope. |
| [`planning/reading-studio-draft-help-and-identity-plan.md`](../planning/reading-studio-draft-help-and-identity-plan.md) | Historical | Dated Reading Studio proposal retained for design history. |
| [`planning/reading-studio-experience.md`](../planning/reading-studio-experience.md) | Historical | Dated product proposal retained for design history. |
| [`planning/reading-studio-quality-plan.md`](../planning/reading-studio-quality-plan.md) | Historical | Dated quality plan retained for rationale, not current status. |

No planning document is deleted by WP-30: the historical plans contain unique
rationale, and their explicit classification prevents them from competing with
the current owners. Generated reports under ignored `artifacts`, `TestResults`,
coverage, `bin`, or `obj` locations are never durable documentation.

## Historical evidence

Historical evidence keeps its original date, commit, commands, paths, and
environment. A later relocation or policy change may add a present-day note,
but must not rewrite what was actually executed. In particular, the 2026-09-01
health baseline, the [performance baseline](release/performance-hardening-baseline.md),
and its dated machine-readable transcription evidence are not current-head test
runs.

Files named `*.template.json`, `release-notes-template.md`, and
`bugfix-queue-template.md` are durable input templates, not observations.
Machine-specific results produced from them belong under ignored `artifacts` and
must carry candidate commit and environment identity before release use.

Manual PASS or FAIL belongs only in its manual-evidence owner and requires an
actual observation record. Unexecuted shell, History, accessibility, DPI, and
screen-reader cases remain **Not tested**.

## Validation

Run the deterministic documentation check from the repository root:

```powershell
.\scripts\validate-documentation-claims.ps1
.\scripts\validate-documentation-claims.ps1 -SelfTest
```

The policy in [`documentation-policy.json`](documentation-policy.json) defines
the canonical set, unique fact owners, required paths and claims, historical
exclusions, and narrowly scoped retired claims. Validation reports actionable
file/line diagnostics, checks relative links and anchors, and does not scan
generated output as source documentation.
