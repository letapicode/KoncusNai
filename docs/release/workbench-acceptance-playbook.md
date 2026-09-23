# Workbench Acceptance Playbook

## Purpose
Capture release evidence for Textbox Workbench mode:
- record/stop/transcribe path is reliable
- no global insertion side effects
- overlay stays hidden

## Prerequisites
- Windows 11 x64 machine.
- App runs from source (`.\scripts\run-app.ps1`) or installed build.
- The selected supported local transcription provider/model is prepared.

## Scenario 1: Textbox Flow
1. Open Settings -> `Experience`.
2. Select `Textbox Workbench` and save.
3. In Workbench window, click `Record`.
4. Speak a short phrase.
5. Click `Stop`.
6. Verify transcript appears in the Workbench textbox.
7. Record evidence for key:
   - `workbench-record-stop-transcribe-textbox`

## Scenario 2: No Global Insertion Side Effects
1. Keep a second target app open (for example Notepad).
2. Keep focus on Workbench and run one more record/stop cycle.
3. Verify no text is inserted into external apps during the Workbench session.
4. Record evidence for key:
   - `workbench-no-global-insertion-side-effects`

## Scenario 3: Overlay Suppressed
1. Stay in Workbench mode.
2. Run at least one record/stop cycle.
3. Verify overlay does not appear during recording/transcribing.
4. Record evidence for key:
   - `workbench-overlay-suppressed`

## Finalize Evidence
1. Fill `executor` and `machine` fields (no placeholders).
2. Run:
   ```powershell
   .\scripts\run-workbench-acceptance.ps1 -EnforceEvidence
   ```
   Or write evidence directly from command line:
   ```powershell
   .\scripts\run-workbench-acceptance.ps1 `
     -Executor "<operator>" `
     -Machine "<machine>" `
     -TextboxFlowStatus PASS `
     -TextboxFlowEvidence "<textbox flow evidence>" `
     -NoGlobalInsertionStatus PASS `
     -NoGlobalInsertionEvidence "<no-global-insertion evidence>" `
     -OverlayHiddenStatus PASS `
     -OverlayHiddenEvidence "<overlay-hidden evidence>" `
     -EnforceEvidence
   ```
3. Attach outputs:
  - `docs/release/workbench-acceptance-report.md`
  - `artifacts/workbench-acceptance/workbench-acceptance-results.json`
