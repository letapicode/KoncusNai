# Koncus Nai administrator guide

## Distribution contract

Koncus Nai ships one x64 Windows application MSI and one WiX Burn setup EXE. Transcription and narration models are not bundled; each Windows user prepares the selected provider/model once and its runtime cache remains under that user's local application data.

Interactive setup:

```powershell
.\KoncusNai-Setup-Small-<version>-x64.exe
```

Silent setup, repair, and uninstall use the standard Burn switches:

```powershell
.\KoncusNai-Setup-Small-<version>-x64.exe /quiet /norestart /log .\install.log
.\KoncusNai-Setup-Small-<version>-x64.exe /repair /quiet /norestart /log .\repair.log
.\KoncusNai-Setup-Small-<version>-x64.exe /uninstall /quiet /norestart /log .\uninstall.log
```

The MSI installs per-machine under Program Files. User settings, local history, provider runtimes, and model caches live outside the install directory and are preserved on uninstall by policy.

## Validation

Run static package checks on every candidate:

```powershell
.\scripts\packaging-smoke.ps1
.\scripts\installer-upgrade-compatibility.ps1
.\scripts\security-compliance.ps1
```

Run install/repair/upgrade/uninstall scenarios from an elevated test machine:

```powershell
.\scripts\installer-scenario-validation.ps1 -RunDynamicScenarios -BaseInstallerPath .\artifacts\installer\KoncusNai-Setup-Small-<version>-x64.exe
```

For upgrade evidence, also pass `-UpgradeInstallerPath` with the newer candidate. Test provider preparation separately with the actual provider/model and record whether first-use network access is required. Do not claim offline transcription unless that exact runtime and model were already prepared on the tested Windows account.

History is local plaintext protected by the Windows account and filesystem ACLs. Diagnostic bundles exclude raw history by default.
