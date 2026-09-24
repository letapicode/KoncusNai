# Koncus Nai

> **Pre-release source project:** the private source baseline at `d1b532f` passed GitHub CI on September 24, 2026. Koncus Nai is work-in-progress source, not a supported `v1` release; do not redistribute an installer built from this checkout as an official Koncus Nai release. Later commits require their own validation.

Koncus Nai was previously named Nilo and Notype (and originally Dictate Anywhere). The burnt-orange KN monogram
is the current application mark. Existing data paths, executable names and installer
upgrade identities retain `DictateAnywhere` for compatibility. The source checkout
can use any folder name; its location does not change existing user-data paths.

For a source-install Start menu shortcut and command, run `powershell.exe -NoProfile -File scripts/install-kn-launcher.ps1`.
Use `run-kn` to launch this checkout. After moving source, rerun the launcher installer
with `-PreviousRepoRoot '<previous checkout>'` to retarget an exact owned launcher
and shortcut; customized or foreign entries are rejected. Add `-SkipPathUpdate`
when the existing per-user launcher directory is already on PATH.
The source launcher builds on first use and requires the .NET SDK. The installer also registers `run-kn` for Windows Run, Command Prompt, and PowerShell.

Koncus Nai is a Windows 11 local-first application for global dictation, local chat, local history, and Reading Studio document narration/export. The repository retains the historical `DictateAnywhere.*` project and namespace names for compatibility. Dictation and chat history are saved locally without an application password or automatic retention limit.

## Start here

The `d1b532f` private source baseline passed its automated CI gates. The owner is preparing public work-in-progress source visibility while AI4Bharat, Nyra, Ampixa, and optional-runtime rights questions remain open; this is not a claim of third-party permission. See [the public-source audit](planning/public-source-pre-release-audit-2026-09-17.md), [the owner decision record](docs/release/public-source-owner-decision-record.md), [the runtime rights review packet](docs/release/public-source-rights-review-packet.md), [the release checklist](docs/release/release-checklist.md), and [TODO.md](TODO.md) for tested changes, remaining decisions, and installation limitations. Local audit installers are not approved public releases.

Koncus Nai is **source-available** under [PolyForm Noncommercial 1.0.0](LICENSE); it is not OSI-approved open source. The license permits noncommercial use, modification, and redistribution. Commercial use requires separate written permission from Ram Adhikari through [koncusnai@gmail.com](mailto:koncusnai@gmail.com). Third-party code, models, and assets remain under their own licenses.

Before running or contributing, review the [project license](LICENSE), [disclaimer](DISCLAIMER.md), [privacy behavior](PRIVACY.md), [security policy](SECURITY.md), [model terms](MODEL_LICENSES.md), and [third-party notices](THIRD_PARTY_NOTICES.md).

Read these in order; they are the canonical small-context entry points:

1. [`docs/documentation-map.md`](docs/documentation-map.md) â€” canonical-document navigation and planning-document disposition.
2. [`docs/architecture/system-architecture.md`](docs/architecture/system-architecture.md) â€” system context, boundaries, runtime flows, invariants, and authoritative code entry points.
3. [`docs/architecture/product-capability-matrix.md`](docs/architecture/product-capability-matrix.md) â€” supported capabilities and explicit non-capabilities.
4. [`docs/guides/developer-guide.md`](docs/guides/developer-guide.md) â€” contributor setup and current build, test, packaging, and release commands.
5. [`planning/codebase-hardening-backlog.md`](planning/codebase-hardening-backlog.md) â€” hardening item scope and status.
6. [`planning/codebase-hardening-ledger.md`](planning/codebase-hardening-ledger.md) â€” packet commits, evidence, dependencies, and the next action.

Do not treat dated planning documents or generated release reports as current architecture unless one of the canonical documents links to them.

## Architecture in one sentence

`DictateAnywhere.App` is the WPF host and sole production composition root; `DictateAnywhere.Core` owns stable cross-boundary contracts/orchestration; implementation modules own Windows, audio, hotkey, inference, insertion, overlay, settings, model, and diagnostics mechanics, while developer executables live under `tools`.

## Local build

```powershell
.\setup.ps1
dotnet restore DictateAnywhere.sln --locked-mode
dotnet build DictateAnywhere.sln --configuration Release --no-restore
dotnet test DictateAnywhere.sln --configuration Release --no-restore --maxcpucount:1
```

The canonical solution remains the complete developer and CI validation boundary. The checked-in filters make narrower build intent explicit without changing dependency resolution:

```powershell
dotnet restore DictateAnywhere.Production.slnf
dotnet build DictateAnywhere.Production.slnf --configuration Release --no-restore
dotnet restore DictateAnywhere.DeveloperTools.slnf
dotnet build DictateAnywhere.DeveloperTools.slnf --configuration Release --no-restore
```

Production projects live under `src`; retained benchmark, CLI, validation, and asset-generation executables live under `tools`. Only the App and UIAccess helper are installer publish inputs.

Run from source:

```powershell
.\scripts\run-app.ps1
```

## Models and credentials

Model weights are downloaded separately into the current Windows user's local application data and are excluded from Git. Cohere Transcribe and Indic Parler are gated Hugging Face downloads: accept the terms on their model pages, install the Hugging Face CLI, and run `hf auth login` for the same Windows account. Koncus Nai reads the standard Hugging Face credential store; it does not save your token in application settings.

Before selecting or using any third-party model offered through Koncus Nai, review that model's current upstream license, model card, and access conditions. You are responsible for complying with its terms and obtaining any permission your intended use requires, including for generated output, redistribution, or commercial use. See [MODEL_LICENSES.md](MODEL_LICENSES.md) for the recorded model revisions and links.

These user-facing notices do not grant third-party rights or resolve the publisher's remaining model and optional-runtime obligations. All existing model choices remain available under their recorded access and acknowledgement gates.

Generated voice previews are not included in this repository or release payload. Reading Studio creates a preview locally after the user selects and prepares the relevant narration provider.

CrisperWhisper is optional and is **not licensed for ordinary production or commercial use**. Its upstream terms restrict the weights and every output, including transcripts, timestamps, and derived annotations, to non-commercial research. Koncus Nai requires explicit acknowledgement before those weights can be selected or used for Reading Studio alignment. See [MODEL_LICENSES.md](MODEL_LICENSES.md) for exact pinned revisions, approximate download sizes, and official license links.

### First-use dictation speed and timing

**Expect possible first-use delay.** The first few dictations after opening Koncus Nai may take longer than later ones, even for a short phrase. Koncus Nai starts background readiness work for the selected model; loading a worker or model and warming system caches may contribute. The exact reason and number of slower attempts have not been measured for every setup; a faster third attempt is possible, not guaranteed. The tray menu shows the selected model's readiness state. Do not judge steady-state speed from the first attempt alone: try several similar dictations with the same model after it reports **Ready**. If delays continue, the timing fields below help identify the slow stage.

The green CI run did not measure an installed model or explain why a user's first two dictations may feel slower than the third. For a comparable manual check, open the app normally, keep the same provider, model, language, target app, and recording mode, then dictate the same non-sensitive short phrase three times at a similar speaking pace. Note whether model readiness was still warming before the first attempt. Compare `Recording started.` and `Dictation stop-to-visible timing completed.` entries by `operationId` in the local logs under `%LOCALAPPDATA%\DictateAnywhere\logs`. The numeric `recordingOverlayMs`, `captureStartMs`, `transcribingOverlayMs`, `captureFinalizationMs`, `transcriptionWallMs`, `modelReportedMs`, `transformationMs`, `insertionMs`, and `stopToVisibleMs` fields separate the visible stages. Cohere and CrisperWhisper also log `workerColdStart`, worker startup, and inference or invocation timings; match those by time and model because their worker correlation IDs differ from the dictation operation ID. Do not commit raw logs, recordings, transcripts, models, or credentials.

Run the broader quality suite:

```powershell
.\scripts\run-quality-suite.ps1
```

Validate current documentation and the supply-chain contract directly:

```powershell
.\scripts\validate-documentation-claims.ps1
.\scripts\security-compliance.ps1
```

## Engineering rules

- Fix open S0 issues before production release work.
- Preserve behavior with focused evidence before structural extraction.
- Give every workflow one state, cancellation, and lifetime owner.
- Keep production service construction at the composition root.
- Keep one canonical descriptor for each provider.
- Never overwrite unknown future persistence schemas.
- Never claim a feature, provider, model, or language that the current runtime cannot complete.
- Do not add an assembly or abstraction solely to reduce file size.
