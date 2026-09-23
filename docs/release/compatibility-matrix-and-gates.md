# Compatibility Matrix and Release Gates

## Source of Truth
- Compatibility contract (machine-readable): `docs/release/compatibility-matrix.json`
- Global app coverage policy: `docs/release/global-app-coverage-policy.md`
- Release non-goals and limitations register: `docs/release/release-constraints.json`

## Supported Windows Targets

| Status | OS | Minimum Build | Architectures |
|---|---|---:|---|
| Supported | Windows 11 22H2+ | 22621 | x64 |
| Planned | Windows 10 22H2 | 19045 | x64 |

## Hardware Classes and Expected Defaults

| Class | Description | Expected Default Model | Rule |
|---|---|---|---|
| `older-i5` | Older i5-class CPU (8th-10th gen range) | `cohere-transcribe-03-2026` | Use measured warm latency and accuracy budgets to choose among supported providers/models. |
| `modern-i5` | Modern i5-class CPU (11th gen+) | `cohere-transcribe-03-2026` | Keep the product default unless the current benchmark proves another supported choice is better. |

## Baseline Runtime Target
- `Dictate Anywhere` targets the currently focused text control or window at the caret.
- Baseline behavior is generic and does not require app-specific integrations for normal text surfaces.
- Session-monitor resolution is best-effort, not universal:
  - anchor lookup prefers `caret bounds`, then `focused editable element bounds`, then `focused window bounds`; the resolved bounds select the monitor for the bottom-center indicator, with the top-right corner panel retained as an unavailable-anchor fallback.
- Release certification is by representative surface family evidence, not by claiming one Windows caret API works identically everywhere.
- Certified app entries exist for release evidence and gating, not because those apps use bespoke insertion code paths.

## Compatibility Tiers
- `Tier 1`:
  - Certified apps that must pass every release in scope.
- `Tier 2`:
  - Best-effort apps tracked through exploratory or issue-driven evidence, but not release-blocking unless promoted.
- `Tier 3`:
  - Unsupported or blocked contexts that must return deterministic UX messaging.

## Tier 1 Certified App Matrix

| Tier | Category | Target | Evidence Source |
|---|---|---|---|
| Tier 1 | IDE | VS Code editor | Milestone 1 criterion: `VS Code insertion` |
| Tier 1 | Browser | Chrome URL bar | Milestone 1 criterion: `Chrome URL bar insertion` |
| Tier 1 | Docs | Microsoft Word document | Milestone 1 criterion: `Word insertion` |
| Tier 1 | Terminal | Windows Terminal (non-admin) | Manual evidence key: `windows-terminal-non-admin` |
| Tier 1 | Chat | Slack compose box (non-admin) | Milestone 1 criterion: `Slack insertion (non-admin)` |

## Tier 1 Certified Global Toggle Rollout Matrix
- Required release evidence for the Alt+Space rollout:
  - machine-local hotkey validation decision (`GO` or `GO_WITH_FALLBACK`)
  - `Classic Win32/RichEdit`: `Notepad`, `Sticky Notes`
  - `Word document surface`: `Microsoft Word`
  - `Chromium URL bar`: `Chrome URL bar`, `Edge URL bar`
  - `Chromium page text/contenteditable`: `ChatGPT`, `Gemini`, `Claude`
  - `Electron and IDE editors`: `VS Code`, `Windsurf IDE`, `Antigravity IDE`
  - `Terminal`: `Windows Terminal`
  - `Chat compose`: `Slack`
- Each evidence note must say whether insertion completed via `paste`, `typing`, or ended `blocked`.
- Each evidence note must separately capture indicator behavior, including the monitor used by the bottom-center pill and whether the corner-panel fallback appeared.
  - `bottom-center-target-monitor`
  - `corner-fallback`
  - `missing`
- Each PASS evidence note must also explain how the visible result was confirmed: verified mutation, automation-captured final value, or human-confirmed visible result.

## Tier 3 Known Limitations
- Elevated/admin targets can be blocked by UIPI without the signed UIAccess path.
- Secure/password fields are intentionally blocked.
- Protected controls can reject standard insertion paths and must surface deterministic blocked messaging.

## Global Toggle Release Acceptance Criteria
- Source of truth:
  - `docs/release/global-toggle-release-acceptance-criteria.md`
- Release sign-off requires all of the following:
  - hotkey strategy remains `GO` for the candidate machine,
  - global toggle start/stop reliability across the required app matrix,
  - verified insertion evidence or deterministic blocked reason per app,
  - no regression in Workbench mode,
  - no regression in installer/release orchestration/strict compatibility gate flow.

## Extensibility Regression Guardrail
- Source of truth:
  - `docs/release/extensibility-regression-and-rollback.md`
- Strict release gating now requires the English baseline regression artifact:
  - `artifacts/english-baseline-regression/english-baseline-regression-results.json`
- This gate exists to prove that incremental language/model work has not changed the prior English-only behavior baseline.
- Multilingual acceptance evidence is conditionally required only when
  - `releaseGatePolicy.requiresMultilingualAcceptanceEvidence = true` in `docs/release/compatibility-matrix.json`.

## Non-Goals and Known Limitations
- Per-release non-goals/limitations are tracked in `docs/release/release-constraints.json`.
- Each release candidate must reference an explicit `releaseLine` entry (current baseline: `1.x`) before sign-off.

## Release Gate Command
Strict release gate:
```powershell
.\scripts\run-compatibility-gate.ps1 -EnforceReleaseEvidence -ReleaseLine 1.x
```

Static contract/evidence sanity check (CI-safe):
```powershell
.\scripts\run-compatibility-gate.ps1 -ReleaseLine 1.x
```

## Required Evidence Artifacts
- `artifacts/milestone1/milestone-1-acceptance-results.json`
- `artifacts/milestone2/milestone-2-reliability-results.json`
- `artifacts/compatibility/manual-app-matrix-results.json` (required in strict release mode)
- `artifacts/installer-validation/<timestamp>/installer-scenario-summary.json` (must include install, repair, upgrade when applicable, and uninstall scenarios)
- `artifacts/workbench-acceptance/workbench-acceptance-results.json` (must show PASS for textbox flow, no-global-insertion side effect, and overlay suppression)
- `artifacts/global-toggle-acceptance/global-toggle-hotkey-validation.json` (must show `GO` or `GO_WITH_FALLBACK` for the candidate machine)
- `docs/release/global-toggle-hotkey-validation-report.md`
- `artifacts/global-toggle-acceptance/global-toggle-acceptance-results.json` (must show PASS for the required global-toggle app matrix)
- `artifacts/english-baseline-regression/english-baseline-regression-results.json` (must show PASS for the required English baseline regression criteria)
- `artifacts/multilingual-acceptance/multilingual-acceptance-results.json` (required only when multilingual UI is enabled in release gate policy)
- `docs/release/global-toggle-release-acceptance-criteria.md` (defines final rollout sign-off criteria and evidence mapping)
- `docs/release/extensibility-regression-and-rollback.md` (defines English baseline regression coverage and rollback triggers for future language/model rollout work)
- Workbench capture workflow: `docs/release/workbench-acceptance-playbook.md`
- Global toggle capture workflow: `docs/release/global-toggle-acceptance-playbook.md`
- Multilingual capture workflow (when enabled): `docs/release/multilingual-acceptance-playbook.md`
- Optional elevated evidence when shipping UIAccess scenarios:
  - `artifacts/milestone3/milestone-3-elevated-acceptance-results.json`
