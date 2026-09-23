# Project Reference Guardrails

## Scope
This document defines the project-reference and UI source-layer guardrails enforced in CI.

## Current Guardrails
- `DictateAnywhere.Core` and `DictateAnywhere.Platform.Windows` are the foundation layers and must not reference other product projects.
- The swappable runtime modules stay behind contracts plus platform adapters:
  - `DictateAnywhere.Audio`
  - `DictateAnywhere.Hotkeys`
  - `DictateAnywhere.Inference`
  - `DictateAnywhere.Insertion`
- Those swappable modules may only reference:
  - `DictateAnywhere.Core`
  - `DictateAnywhere.Platform.Windows` where Windows interop is required.
- Supporting modules (`Overlay`, `Settings`, `Models`, `Diagnostics`, `Benchmark`) may only depend on `DictateAnywhere.Core`.
- Executable hosts remain explicit:
  - `DictateAnywhere.App` is the current composition root.
  - `DictateAnywhere.UiAccessHelper` is a narrowly scoped helper host.
  - `DictateAnywhere.ModelBenchmark`, `DictateAnywhere.TtsCli`, `DictateAnywhere.Spikes`, and `DictateAnywhere.VoicePreviewGenerator` are retained engineering tools under `tools`.
  - The generator's App-internals dependency and direct Core-contract consumption are explicit in the machine-readable graph and preserve its exact `InternalsVisibleTo` assembly identity.
- UI code-behind guardrail:
  - WPF windows/controls in `src/DictateAnywhere.App/{Settings,FirstRun,Workbench,Hotkeys}` may not directly import implementation namespaces (`Audio`, `Inference`, `Models`, `Settings`, `Platform.Windows`, `Benchmark`, `Microsoft.Win32`).
  - UI code-behind uses `DictateAnywhere.Core.Contracts` plus app composition adapters in `DictateAnywhere.App.Composition`.

## What This Enforces Now
- No circular project-reference graph across production and tool projects.
- No new direct project dependency that bypasses the current contract-oriented module layout.
- The hotkey, audio, inference, and insertion modules remain independently swappable at the project boundary.
- UI code-behind cannot call Win32/inference/model/settings implementation APIs directly; those calls are routed through app composition adapters.

## What Remains Intentionally Open
- `DictateAnywhere.App` is still the composition root project and keeps project references to implementation modules by design.
- Deeper physical split into separate `App.UI` and `App.Composition` projects is still optional future work, not a release blocker for the current guardrail level.
- `DictateAnywhere.Production.slnf` and `DictateAnywhere.DeveloperTools.slnf` declare build roles; `DictateAnywhere.sln` remains the complete validation boundary.

## Source of Truth
- Machine-readable policy:
  - `docs/architecture/project-reference-guardrails.json`
- Compiled API and dependency evidence:
  - `docs/architecture/public-api-baseline.json`
  - `docs/architecture/public-api-and-dependency-contract.md`
- Enforcement:
  - `tests/DictateAnywhere.Core.Tests/ProjectReferenceGuardrailTests.cs`
  - `tests/DictateAnywhere.Core.Tests/PublicApiBaselineTests.cs`
  - `tests/DictateAnywhere.App.Tests/UiLayerDependencyGuardrailTests.cs`
  - `scripts/validate-project-reference-guardrails.ps1`
  - `scripts/validate-public-api.ps1`
