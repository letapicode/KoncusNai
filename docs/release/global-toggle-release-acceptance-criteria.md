# Global Toggle Release Acceptance Criteria

## Scope
This document defines the release-acceptance criteria for the `Alt + Space` global-toggle rollout in `Dictate Anywhere` mode.

These criteria define what must be true for release sign-off.
They do not claim the criteria have already passed.

## Criterion 1
Global toggle hotkey strategy remains GO for the release candidate.

Evidence source:
- `artifacts/global-toggle-acceptance/global-toggle-hotkey-validation.json`
- `docs/release/global-toggle-hotkey-validation-report.md`
- `artifacts/global-toggle-acceptance/global-toggle-acceptance-results.json`
- `docs/release/global-toggle-acceptance-report.md`

Pass interpretation:
- Hotkey validation concludes `GO` or `GO_WITH_FALLBACK`.
- The active binding for the candidate machine is explicit (`Alt + Space` or `Win + Alt + Space`).
- Manual certified-app evidence uses the same active binding reported by the validation artifact.

Blocking conditions:
- Hotkey validation concludes `NO_GO`.
- The active binding is ambiguous.
- The acceptance report omits the hotkey strategy decision row.

## Criterion 2
Global toggle start/stop works reliably in the required app matrix.

Evidence source:
- `artifacts/global-toggle-acceptance/global-toggle-acceptance-results.json`
- `docs/release/global-toggle-acceptance-report.md`

Required matrix:
- Notepad
- Sticky Notes
- Chrome URL bar
- Microsoft Word
- VS Code
- Windsurf IDE
- Antigravity IDE
- Windows Terminal
- Slack

Pass interpretation:
- Each required app row is `PASS`.
- Operator evidence states one start press and one stop press produced one insertion attempt.
- Any fallback from `Alt + Space` to `Win + Alt + Space` is recorded explicitly.

Blocking conditions:
- Any required app row is missing.
- Any required app row is `FAIL`, `MANUAL_REQUIRED`, `NOT_RUN`, or `BLOCKED` without a deterministic explanation in evidence.

## Criterion 3
Transcript insertion is verified, or the app returns a deterministic blocked reason, per app.

Evidence source:
- `artifacts/global-toggle-acceptance/global-toggle-acceptance-results.json`
- `docs/release/global-toggle-acceptance-report.md`

Pass interpretation:
- Every required app row contains a method/result note:
  - `paste`
  - `typing`
  - `blocked`
- PASS evidence explicitly states how the visible result was confirmed:
  - verified text mutation
  - automation-captured final value
  - human-confirmed visible result note
- If blocked, the evidence states a deterministic reason consistent with product limits such as secure field or UIPI/admin boundary.

Blocking conditions:
- Evidence omits the final method/result note.
- Evidence does not explain how the visible result was confirmed.
- Evidence describes non-deterministic or unexplained failure.
- Evidence shows duplicate insertion, missing insertion, or ambiguous insertion outcome.

## Criterion 4
No regression in Workbench mode.

Evidence source:
- `artifacts/workbench-acceptance/workbench-acceptance-results.json`
- `docs/release/workbench-acceptance-report.md`

Required Workbench criteria:
- `Workbench textbox flow (record -> stop -> transcribe)`
- `Workbench no global insertion side effect`
- `Workbench overlay suppressed`

Pass interpretation:
- All required Workbench criteria are `PASS`.

Blocking conditions:
- Any required Workbench criterion is missing.
- Any required Workbench criterion is not `PASS`.

## Criterion 5
No regression in installer, release orchestration, or strict compatibility gating pipeline.

Evidence source:
- `artifacts/compatibility/compatibility-gate-results.json`
- `docs/release/compatibility-gate-report.md`
- `artifacts/release-orchestration/release-orchestration-results.json`
- `docs/release/release-orchestration-report.md`

Pass interpretation:
- Strict compatibility gate passes with global-toggle evidence in scope.
- Release orchestration completes without blocking failures when global-toggle evidence is included.
- Installer and packaging evidence required by the current release line remain `PASS`.

Blocking conditions:
- Strict compatibility gate contains any blocking row.
- Release orchestration ends `FAIL`.
- Global-toggle evidence is absent from the release pipeline for the target release.

## Sign-off Rule
Release sign-off for the global-toggle rollout requires all five criteria above to pass.

Supporting references:
- `docs/architecture/global-toggle-hotkey-strategy.md`
- `docs/release/global-toggle-acceptance-playbook.md`
- `docs/release/compatibility-matrix-and-gates.md`
