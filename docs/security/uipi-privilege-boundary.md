# UIPI and Privilege Boundary Behavior

## What Windows Enforces
- Windows User Interface Privilege Isolation (UIPI) blocks lower-integrity processes from injecting input into higher-integrity windows.
- `SendInput` and similar insertion paths are constrained by integrity level.

## Default Behavior
- App runs at normal integrity by default.
- Insertion is expected to work in normal user apps.
- Insertion may fail in:
  - elevated/admin applications,
  - secure/protected input fields,
  - some hardened terminal/sandbox contexts.

## User-Facing Handling
- If insertion is blocked, the app reports a clear message and next steps.
- Settings expose an optional elevated insertion toggle with explicit risk copy.
- Diagnostics classify these failures as `InsertionBlocked`.

## Optional Elevated Path
- UIAccess helper bridge can be enabled from settings.
- Elevated mode requires:
  - signed host + helper binaries,
  - secure installation under `Program Files`.
- If requirements are not met, elevated insertion remains blocked with explicit diagnostics.
