# Milestone 3 Elevated Insertion Acceptance Playbook

## Purpose
Validate optional elevated insertion behavior and UIAccess release gating for admin-app scenarios.

## Preconditions
- Windows 11 machine.
- Repository dependencies installed (`dotnet`, `wix`).
- Elevated PowerShell for dynamic scenarios.
- Signed `DictateAnywhere.App.exe` and `DictateAnywhere.UiAccessHelper.exe` when validating UIAccess dynamic paths.

## Static Acceptance (CI-safe)
Run:
```powershell
.\scripts\run-milestone3-elevated-acceptance.ps1
```

Expected:
- Insertion elevated-routing unit tests pass.
- Helper policy tests pass.
- Packaging contract checks pass (UIAccess properties + signing policy fields + helper manifest).

Artifacts:
- `docs/release/milestone-3-elevated-acceptance-report.md`
- `artifacts/milestone3/milestone-3-elevated-acceptance-results.json`

## Dynamic Acceptance (signed binaries required)
Run from an elevated PowerShell session:
```powershell
.\scripts\run-milestone3-elevated-acceptance.ps1 -RunDynamicScenarios
```

The script validates:
- Admin session.
- Signed app/helper binaries in publish output.

Then execute the manual admin-app matrix below.

## Manual Admin-App Matrix
1. Launch elevated Notepad.
2. Launch elevated Windows Terminal.
3. Launch an elevated Office app (if available).
4. In Koncus Nai settings, enable elevated insertion.
5. Dictate short phrases into each elevated target.
6. Confirm insertion succeeds and method/evidence is logged.

Capture evidence:
- Screenshot or recording per target app.
- Relevant log snippets from `%LocalAppData%\DictateAnywhere\logs`.
- Final status rows added to acceptance report.
