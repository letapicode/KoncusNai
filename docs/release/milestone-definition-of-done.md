# Milestone Definition of Done

## Scope
This document defines release gates for each milestone so completion is evidence-driven, not implied.

## Milestone 0 (Feasibility)
Required evidence:
- `docs/release/milestone-0-spike-report.md` exists and is recent for the target machine.
- `artifacts/spikes/milestone-0-spike-results.json` contains successful results for:
  - `hotkey`
  - `audio`
  - `inference`
  - `insertion-clipboard-notepad`
  - `insertion-clipboard-vscode`
  - `insertion-clipboard-chrome`
  - `insertion-typing-fallback`
- No unresolved blockers in the report.

Done when:
- All required spikes pass.
- No blocker invalidates MVP architecture.

## Milestone 1 (MVP)
Required evidence:
- Solution tests pass (`dotnet test DictateAnywhere.sln --configuration Release`).
- Acceptance report exists: `docs/release/milestone-1-acceptance-report.md`.
- Acceptance JSON exists: `artifacts/milestone1/milestone-1-acceptance-results.json`.
- Matrix criteria status:
  - VS Code insertion: `PASS`
  - Chrome URL bar insertion: `PASS`
  - Word insertion: `PASS` (or explicitly `BLOCKED` with reason and follow-up owner/date)
  - Slack insertion (non-admin): `PASS`
  - Current provider/model cold and warm latency: `PASS` against the committed transcription budgets

Done when:
- All MVP feature deliverables are implemented and tested.
- Acceptance matrix is complete or remaining blockers are explicitly tracked with mitigation and owner.

## Milestone 2 (Reliability Upgrades)
Required evidence:
- Benchmark recommendation flow regression tests pass.
- Long-run stability and fault-injection scenarios pass.
- Reliability regression script passes: `scripts/run-milestone2-reliability.ps1`.
- Reliability report exists: `docs/release/milestone-2-reliability-report.md`.
- Reliability JSON exists: `artifacts/milestone2/milestone-2-reliability-results.json`.
- Reliability report published under `docs/release/`.

Done when:
- No untriaged reliability regressions remain.

## Milestone 3 (Elevated Insertion, Optional)
Required evidence:
- UIAccess architecture and install/signing constraints documented.
- Static acceptance script passes: `scripts/run-milestone3-elevated-acceptance.ps1`.
- Elevated acceptance report exists: `docs/release/milestone-3-elevated-acceptance-report.md`.
- Elevated acceptance JSON exists: `artifacts/milestone3/milestone-3-elevated-acceptance-results.json`.
- UIAccess helper manifest declares `uiAccess=true` and packaging smoke checks pass.
- Dynamic admin-app matrix is executed in signed/elevated environment (or explicitly marked `MANUAL_REQUIRED` with owner/date).

Done when:
- Elevated insertion feature is functional and documented with risk controls.

## Milestone 4 (Differentiators)
Required evidence:
- Supported spoken-command transformation has unit and integration tests.
- Usability polish checklist complete.

Done when:
- Differentiator features are stable and do not regress MVP behavior.

## Cross-Release Compatibility Gate
Required evidence:
- Compatibility matrix contract exists: `docs/release/compatibility-matrix.json`.
- Release non-goals/limitations register exists: `docs/release/release-constraints.json`.
- Compatibility gate script report exists for target release line:
  - `docs/release/compatibility-gate-report.md`
  - `artifacts/compatibility/compatibility-gate-results.json`
- Strict gate passes for release candidates:
  - `.\scripts\run-compatibility-gate.ps1 -EnforceReleaseEvidence -ReleaseLine 1.x`

Done when:
- Release gate reports no `FAIL`, `BLOCKED`, `NOT_RUN`, or `MANUAL_REQUIRED` rows in strict mode.
