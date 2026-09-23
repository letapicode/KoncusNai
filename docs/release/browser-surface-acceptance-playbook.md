# Browser Surface Acceptance Playbook

## Purpose
Capture the April 19, 2026 follow-up evidence for browser/search-surface hotkey leakage and per-surface IDE behavior. This checklist is supplemental to the certified global-toggle matrix:
- it separates browser `URL bar`, `page search field`, `page prompt/contenteditable`, and `browser chrome` non-target surfaces,
- it records whether the global toggle `started`, was `reserved`, `leaked`, or `focus-drifted`,
- it records insertion outcome, overlay outcome, and latency notes per surface.

Use this after the normal global-toggle hotkey validation and certified matrix run:
- `docs/release/global-toggle-acceptance-playbook.md`
- `scripts/run-global-toggle-hotkey-validation.ps1`

## Required Follow-Up Surfaces
1. Chrome URL bar
   - Key: `global-toggle-chrome-url-bar`
2. Google search box in Chrome
   - Key: `global-toggle-google-search-box`
3. ChatGPT Search mode in Chrome
   - Key: `global-toggle-chatgpt-search-mode`
4. ChatGPT prompt/composer
   - Key: `global-toggle-chatgpt-prompt`
5. Gemini prompt
   - Key: `global-toggle-gemini-prompt`
6. Claude prompt
   - Key: `global-toggle-claude-prompt`
7. Chrome browser chrome non-target surface
   - Key: `global-toggle-chrome-browser-chrome`
8. Edge URL bar
   - Key: `global-toggle-edge-url-bar`
9. Edge page search field
   - Key: `global-toggle-edge-search-field`
10. Edge browser chrome non-target surface
    - Key: `global-toggle-edge-browser-chrome`
11. Windsurf editor buffer
    - Key: `global-toggle-windsurf-ide`
    - This preserves the existing release key, but the row now means the editor surface specifically.
12. Windsurf integrated terminal
    - Key: `global-toggle-windsurf-terminal`
13. Windsurf command palette or search field
    - Key: `global-toggle-windsurf-command-palette`
14. Sticky Notes
    - Key: `global-toggle-sticky-notes`
15. Antigravity editor surface
    - Key: `global-toggle-antigravity-ide`

## What To Capture Per Row
For every surface, record:
1. `status`: `PASS`, `FAIL`, or `MANUAL_REQUIRED`
2. `hotkeyOutcome`: `started`, `reserved`, `leaked`, or `focus-drifted`
3. `insertionOutcome`: `paste`, `typing`, `blocked`, or `not-applicable`
4. `overlayOutcome`: `visible`, `hidden`, `wrong-surface`, or `not-applicable`
5. `latencyNotes`: short note about perceived lag or timing breakdown
6. `evidence`: short proof with either:
   - visible result confirmation for successful insertion, or
   - deterministic reason for reserved/leaked/focus-drifted/blocked behavior

## Recommended Procedure
For each browser scenario:
1. Focus the exact intended surface first.
2. Press the active global toggle binding once.
3. If recording starts, speak a short phrase and press the binding again.
4. Note whether focus stayed on the intended surface or drifted to browser chrome/menu.
5. Record overlay behavior and whether the final insertion route was paste, typing, blocked, or not applicable.

For Windsurf:
1. Run the editor buffer, integrated terminal, and command palette/search surfaces separately.
2. Record focus classification and insertion route independently for each one.

For Antigravity:
1. Record whether the issue is correctness, visible lag, or both.
2. Include a short latency note even when the insertion succeeds.

## Generate The Evidence Artifact
Interactive:
```powershell
.\scripts\run-browser-surface-acceptance.ps1 -EnforceEvidence
```

Non-interactive validation of a pre-filled artifact:
```powershell
.\scripts\run-browser-surface-acceptance.ps1 -NonInteractive -EnforceEvidence
```

Outputs:
- `docs/release/browser-surface-acceptance-report.md`
- `artifacts/browser-surface-acceptance/browser-surface-acceptance-results.json`
- `artifacts/browser-surface-acceptance/manual-browser-surface-results.json`
