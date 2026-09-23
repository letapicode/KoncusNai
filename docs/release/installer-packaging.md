# Installer Packaging and Validation

## Installer Contract
- Packaging technology: WiX 5.0.2 MSI + WiX Burn setup EXEs.
- Install scope: per-machine (`Program Files`).
- Target runtime: `win-x64` self-contained.
- Startup registration: optional (`STARTUP_ON_LOGIN` MSI property: `auto` preserves the current Windows user's setting on upgrade/repair and stays disabled on a fresh install; explicit `0`/`1` overrides it).
- Settings/model data preservation: keep `%ProgramData%\DictateAnywhere` and `%LocalAppData%\DictateAnywhere` on uninstall.
- Distribution: one setup EXE containing the application MSI. Provider models are prepared independently and are never hidden inside the installer.

## Build Installer Artifacts
Prerequisites:
- .NET SDK 8
- WiX Toolset 5.0.2 (`wix` CLI on PATH) with
  `WixToolset.BootstrapperApplications.wixext` 5.0.2 for the small bundle

Command:
```powershell
.\scripts\build-installer.ps1 -Version 1.0.0 -DistributionMode small
```

Notes:
- `build-installer.ps1` publishes both `DictateAnywhere.App` and `DictateAnywhere.UiAccessHelper` into the installer payload.
- Both win-x64 publishes use locked restore and explicitly select each source project's `packages.win-x64.lock.json`. Separate RID locks prevent installer measurement or packaging from silently rewriting the 29 normal solution locks.
- ModelBenchmark, TtsCli, Spikes, and VoicePreviewGenerator are engineering tools under `tools` and are not publish inputs. Before refreshing the App staging leaf, `build-installer.ps1` uses `validate-installer-staging-path.ps1` to require the exact `<OutputRoot>/publish/DictateAnywhere.App` target, reject broad output roots, and reject a reparse point anywhere in the existing ancestor chain. `validate-installer-payload.ps1` then requires one root App executable and one root UIAccess helper while rejecting developer-tool residue, nested duplicates, and payload reparse points. `scripts/packaging-smoke.ps1` exercises the positive and fail-closed cases; see `planning/executable-ownership-and-distribution-audit.md`.
- `-SkipPublish` accepts only a pre-existing payload that passes the same validation; missing, malformed, ambiguous, or stale developer-tool output is rejected before WiX runs.
- Supply-chain compliance runs before packaging claims. The installer contains no model/runtime downloads or generated voice previews; any bundled third-party binary must be present with its exact SHA-256 in `installer/bundled-binary-manifest.json`. Fonts are covered by the canonical provenance inventory, and CI restore is locked to the checked-in NuGet graph.
- The App publish includes the root PolyForm Noncommercial license, disclaimer, privacy and security policies, model licenses, third-party notices, and the complete archived CrisperWhisper license under `legal/`.
- UIAccess mode remains optional and is enabled only with `-EnableUiAccess`.
- Build output includes:
  - `KoncusNai-<version>-x64.msi`
  - `KoncusNai-Setup-Small-<version>-x64.exe`
  - `KoncusNai-<version>-artifact-manifest.json`

UIAccess packaging mode (requires signed host + helper binaries in publish output):
```powershell
.\scripts\build-installer.ps1 -Version 1.0.0 -EnableUiAccess
```

Signed release build (recommended):
```powershell
.\scripts\build-installer.ps1 -Version 1.0.0 -DistributionMode small -SignInstallerArtifacts -SigningCertificateThumbprint <thumbprint> -VerifyArtifactSignatures
```

## Static Validation Gates
Run these in CI and before release packaging:

```powershell
.\scripts\packaging-smoke.ps1
.\scripts\installer-upgrade-compatibility.ps1
.\scripts\validate-size-budgets.ps1
```

These checks enforce:
- Program Files install path and per-machine scope.
- Startup option wiring.
- UIAccess property wiring and secure install constraints.
- Data directory creation and preservation policy.
- Runtime bundling strategy (self-contained publish harvest).
- Stable `UpgradeCode` and downgrade prevention rules.
- Exact App/UIAccess installer payload membership, including `-SkipPublish` reuse.
- Stable tracked/asset budgets and release-only publish/installer budgets documented in [size budgets and evidence](size-budgets.md).

## Scenario Validation (Fresh/Repair/Upgrade/Uninstall)

Inspect the compiled Koncus Nai MSI and test its startup preference conditions without installing:

```powershell
powershell.exe -NoProfile -File scripts/test-koncus-nai-installer.ps1 -MsiPath artifacts/koncus-rebrand/package/installer/KoncusNai-1.4.1-x64.msi
```

Use the path of the candidate being reviewed. This restricted package-session check
does not replace actual installation, upgrade, repair and uninstall scenarios.
Static-only mode:
```powershell
.\scripts\installer-scenario-validation.ps1
```

Dynamic MSI execution mode (requires elevated PowerShell):
```powershell
.\scripts\installer-scenario-validation.ps1 -RunDynamicScenarios -BaseInstallerPath .\artifacts\installer\KoncusNai-1.0.0-x64.msi
```

Validation logs are written to:
- `artifacts/installer-validation/<timestamp>/`
- `artifacts/installer-validation/<timestamp>/installer-scenario-summary.json`

Offline validation must distinguish installation from model readiness: setup is offline-capable, while transcription is offline-capable only after the chosen provider/model has been prepared and cached.
