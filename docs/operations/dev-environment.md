# Developer Environment

## Required Tooling
- Windows 11
- .NET SDK 8.0.x (runtime alone is not sufficient)
- WiX Toolset v4 CLI (`wix`) for MSI packaging
- Git
- Visual Studio 2022 or VS Code + C# extension

## Verify .NET SDK
```powershell
dotnet --info
```

Expected: at least one `.NET SDKs installed` entry for 8.0.x.

## Build and Test
```powershell
dotnet restore DictateAnywhere.sln
dotnet build DictateAnywhere.sln --configuration Release
dotnet test DictateAnywhere.sln --configuration Release
```

## Fast Local App Run (No Installer)
Standard user scope:
```powershell
.\scripts\run-app.ps1
```

Elevated scope:
```powershell
.\scripts\run-app.ps1 -Scope Elevated
```

## One-Command Setup
```powershell
.\setup.ps1
```

The setup script installs missing prerequisites (including .NET SDK 8 via `winget` when needed), then runs restore, build, tests, packaging smoke checks, installer upgrade compatibility checks, and security compliance checks.

To also install WiX via setup:
```powershell
.\setup.ps1 -InstallWixToolset
```

To build an MSI after setup:
```powershell
.\scripts\build-installer.ps1 -Version 1.0.0
```

## Milestone 0 Spike Runner
Run feasibility spikes and generate report artifacts:

```powershell
.\scripts\run-milestone0-spikes.ps1 -RunHotkey -RunAudio -RunInference -RunInsertionMatrix -ContinueOnError
```

Playbook:
- `docs/release/milestone-0-spike-playbook.md`

Prepare spike prerequisites:
```powershell
.\scripts\setup-spike-prereqs.ps1
```

## Milestone 1 Acceptance Runner
Run MVP acceptance validation and generate matrix evidence:

```powershell
.\scripts\run-milestone1-acceptance.ps1 -ContinueOnError
```

Playbook:
- `docs/release/milestone-1-acceptance-playbook.md`

## Notes
- Current architecture targets Windows-specific APIs and uses `net8.0-windows`.
- Model files and runtime binaries are local-only by design.
