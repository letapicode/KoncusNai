# Manual Compatibility Matrix Playbook

## Objective
Validate dictation insertion behavior across target application categories on Windows 11 using release-candidate builds.

This playbook covers both:
- Tier 1 certified release evidence.
- New-app triage for targets that are not yet in the certified matrix.

## Preconditions
- Build and install latest MSI from `artifacts/installer`.
- Prepare the selected supported local transcription provider/model.
- Settings baseline:
  - Global toggle dictation enabled.
  - Standard recording/status indicator present.
  - Insertion method `Clipboard paste`.
  - Record the active ASR provider/model and rewrite provider/mode for the run.
- Use a non-admin target app set unless running elevated validation.

## Execution Script
- Recommended automation bootstrap:
  - `scripts/run-milestone1-acceptance.ps1 -ContinueOnError`
- Then perform the manual checks below and attach evidence.
- Dispatch logs alone are not sufficient evidence. Record how the visible result was confirmed for every PASS row.

## Matrix

| Surface Family | Target | Scenario | Expected Insertion | Expected Indicator |
|---|---|---|---|---|
| Classic Win32/RichEdit | Notepad, Sticky Notes | Dictate into a normal text surface | Text inserted at caret | Bottom-center pill on the target monitor |
| IDE / Electron Editor | VS Code, Windsurf, Antigravity | Dictate into a new file editor | Text inserted at caret | Bottom-center pill or explicit corner fallback captured |
| Browser URL bar | Chrome, Edge | Dictate into URL bar after `Ctrl+L` | Text inserted into URL bar | Bottom-center pill on the browser monitor |
| Browser page text / contenteditable | ChatGPT, Gemini, Claude | Dictate into prompt or editable page field | Text inserted into editable field | Bottom-center indicator evidence captured separately from insertion |
| Documents | Word | Dictate in blank document | Text inserted with expected spacing | Bottom-center pill on the document monitor |
| Chat | Slack (non-admin) | Dictate in message compose box | Text inserted in compose field | Bottom-center pill or explicit fallback captured |
| Terminal | Windows Terminal (non-admin tab) | Dictate command text at prompt | Text inserted without focus loss | Bottom-center pill on the terminal monitor |

The matrix above is the current Tier 1 baseline. Additional apps can be tested with the same generic runtime path, but they do not become release-blocking until they are promoted into the certified matrix.

## Profile-Specific Checks
- `Coding Notes`:
  - Confirm command phrase `insert code block` expands to fenced block template.
- `Email`:
- `Bullets`:
  - Confirm spoken formatting commands apply only when the current Settings toggle is enabled.

## Negative Scenarios
- Secure-field detection enabled:
  - Attempt dictation in a password-like field and verify insertion is blocked with clear message.
- Hotkey conflict:
  - Configure an already-registered hotkey and verify explicit conflict error.
- Clipboard preservation:
  - Copy known clipboard text before dictation, run insertion, verify clipboard restore behavior.

## New App Intake and Triage
Use this flow when testing a target app that is not already in the certified matrix.

Capture:
- App name, version, and process name.
- Windows build and whether the app is normal-integrity or elevated.
- Target surface description (editor, URL bar, compose box, terminal prompt, and so on).
- Insertion outcome:
  - `paste`
  - `typing`
  - `blocked`
- Indicator outcome:
  - `bottom-center-target-monitor`
  - `corner-fallback`
  - `missing`
- Secure-field or protected-control signal, if observed.
- Exact blocked message or failure symptom.

Decision:
- `generic fix`:
  - The failure reproduces on multiple normal text surfaces and points to the standard insertion path.
- `app-specific workaround`:
  - The issue is isolated to one target, and the workaround still fits product direction.
- `documented limitation`:
  - The target is effectively Tier 3 because it is elevated without UIAccess, secure/password protected, or does not expose a standard insertion surface.

Follow-up:
- Add evidence to the manual results artifact for the run.
- Promote the app into Tier 1 only if the product team wants it to become release-blocking.
- Update `docs/known-limitations/known-issues-and-mitigations.md` if the outcome is a standing limitation.

## Elevated/Admin Validation (Optional in MVP)
- Run only when signed UIAccess helper is configured and installed in Program Files.
- Launch elevated target app and validate insertion routing behavior.
- Record whether insertion succeeds via helper or is blocked with explicit explanation.

## Evidence to Capture
- Timestamped run notes (app + version + relevant settings).
- Acceptance report:
  - `docs/release/milestone-1-acceptance-report.md`
- Raw results:
- `artifacts/milestone1/milestone-1-acceptance-results.json`
- For new-app triage, include:
  - ASR provider used,
  - ASR model used,
  - rewrite provider used,
  - rewrite mode used,
  - insertion method outcome (`paste`, `typing`, or `blocked`),
  - indicator outcome (`bottom-center-target-monitor`, `corner-fallback`, or `missing`),
  - how the visible result was confirmed (`verified mutation`, `automation-captured final value`, or `human-confirmed visible result`),
  - integrity level,
  - secure/protected-field signals,
  - triage decision (`generic fix`, `app-specific workaround`, or `documented limitation`).
- Screenshots for any failures or blocked scenarios.
