# Global Toggle Acceptance Playbook

## Purpose
Capture rollout evidence for Dictate Anywhere global toggle mode with `Alt + Space`:
- first press starts recording exactly once
- second press stops/transcribes/inserts exactly once
- insertion outcome is recorded per target app with method note (`paste`, `typing`, or `blocked`)

Release sign-off criteria that consume this evidence are defined in:
- `docs/release/global-toggle-release-acceptance-criteria.md`

## Prerequisites
- Windows 11 x64 machine.
- App runs from source (`.\scripts\run-app.ps1`) or installed build.
- The selected supported local transcription provider/model is prepared.
- In Settings -> `Experience`, click `Apply Global Toggle Preset`, then save.

## Hotkey Strategy Check
Run the machine-local hotkey validation first:
```powershell
.\scripts\run-global-toggle-hotkey-validation.ps1
```

Use the generated validation artifact as the source of truth for the active binding:
1. Confirm the app is in `Dictate Anywhere` mode.
2. Confirm recording mode is `Toggle to talk`.
3. Read `docs/release/global-toggle-hotkey-validation-report.md`.
4. Use the reported active binding for the full certified-app matrix:
   - preferred path: `Alt + Space`
   - fallback path: `Win + Alt + Space`

## Required App Matrix
Run one short dictation cycle in each target. For every row, state whether insertion happened via `paste`, `typing`, or `blocked`.

1. Notepad
   - Evidence key: `global-toggle-notepad`
2. Sticky Notes
   - Evidence key: `global-toggle-sticky-notes`
3. Chrome URL bar
   - Evidence key: `global-toggle-chrome-url-bar`
4. Microsoft Word
   - Evidence key: `global-toggle-microsoft-word`
5. VS Code editor
   - Evidence key: `global-toggle-vs-code`
6. Windsurf IDE editor
   - Evidence key: `global-toggle-windsurf-ide`
7. Antigravity IDE editor
   - Evidence key: `global-toggle-antigravity-ide`
8. Windows Terminal (non-admin)
   - Evidence key: `global-toggle-windows-terminal`
9. Slack compose box (non-admin)
   - Evidence key: `global-toggle-slack`

For each app:
1. Focus the target text surface.
2. Press the active global toggle hotkey once.
3. Speak a short phrase.
4. Press the active global toggle hotkey a second time.
5. Verify only one insertion attempt occurs.
6. Record the active hotkey, whether the final method was `paste`, `typing`, or `blocked`, and explicitly say the insertion happened exactly once.
7. If the result is `blocked`, record the deterministic reason.

## Finalize Evidence
Interactive capture:
```powershell
.\scripts\run-global-toggle-acceptance.ps1 -EnforceEvidence
```

Non-interactive validation against a pre-populated artifact:
```powershell
.\scripts\run-global-toggle-acceptance.ps1 -NonInteractive -EnforceEvidence
```

Attach outputs:
- `docs/release/global-toggle-hotkey-validation-report.md`
- `artifacts/global-toggle-acceptance/global-toggle-hotkey-validation.json`
- `docs/release/global-toggle-acceptance-report.md`
- `artifacts/global-toggle-acceptance/global-toggle-acceptance-results.json`

Release sign-off review after evidence capture:
1. Compare the generated artifact against `docs/release/global-toggle-release-acceptance-criteria.md`.
2. Confirm Workbench evidence and strict compatibility gate also pass for the same release candidate.

April 19, 2026 follow-up:
- Run `.\scripts\run-browser-surface-acceptance.ps1 -EnforceEvidence` to capture the expanded browser/search/Windsurf surface matrix in `docs/release/browser-surface-acceptance-playbook.md`.
