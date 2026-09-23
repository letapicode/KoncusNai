# Koncus Nai Developer Guide

## Scope
This guide is for engineers working on module contracts, build/test loops, and release process.

## Architecture Entry Points
- Canonical documentation owners and planning dispositions:
  - `docs/documentation-map.md`
- Supported capabilities and explicit non-capabilities:
  - `docs/architecture/product-capability-matrix.md`
- Canonical small-context system map and diagrams:
  - `docs/architecture/system-architecture.md`
- Codebase quality rubric and score caps:
  - `docs/quality/codebase-health-rubric.md`
- Severity-ranked hardening backlog:
  - `planning/codebase-hardening-backlog.md`
- Global toggle hotkey strategy:
  - `docs/architecture/global-toggle-hotkey-strategy.md`
- UIAccess helper architecture:
  - `docs/architecture/uiaccess-helper-architecture.md`
- Project reference guardrails:
  - `docs/architecture/project-reference-guardrails.md`
- Language settings schema:
  - `docs/architecture/language-settings-schema.md`
- Model/language compatibility:
  - `docs/architecture/model-language-compatibility.md`
- Benchmark language scope:
  - `docs/architecture/benchmark-language-scope.md`
- UI redesign compatibility:
  - `docs/architecture/ui-redesign-compatibility.md`
- Workbench speech lifecycle:
  - `docs/architecture/workbench-speech-session.md`
- Workbench chat operation lifecycle:
  - `docs/architecture/workbench-chat-operation-session.md`
- Workbench general operation lifecycle:
  - `docs/architecture/workbench-operation-session.md`
- Workbench settings-menu refresh lifecycle:
  - `docs/architecture/workbench-settings-menu-refresh-session.md`
- Workbench history-query lifecycle:
  - `docs/architecture/workbench-history-query-coordinator.md`
- Standalone History query lifecycle:
  - `docs/architecture/history-window-query-lifecycle.md`
- History command lifecycle:
  - `docs/architecture/history-command-coordinator.md`
- History view refresh lifecycle:
  - `docs/architecture/history-view-refresh-lifecycle.md`
- History file-access coordination:
  - `docs/architecture/history-file-access-coordination.md`
- Core contracts:
  - `src/DictateAnywhere.Core/Contracts`

## Module Boundaries
- `DictateAnywhere.App`: tray host, settings UI, first-run workflow, and sole production composition root.
- `DictateAnywhere.Core`: pipeline coordinator and cross-module orchestration.
- `DictateAnywhere.Hotkeys`: global hotkey handling (`RegisterHotKey` flow).
- `DictateAnywhere.Audio`: WASAPI capture and buffering.
- `DictateAnywhere.Inference`: local provider workers for transcription, chat, TTS, OCR, and alignment.
  - Also owns reader TTS provider routing, Kokoro and Indic Parler worker adapters, automatic Indic Parler runtime provisioning, device/precision policy, long-text chunking, and sequential batch generation.
- `DictateAnywhere.Insertion`: clipboard/type insertion strategy and fallback.
- `DictateAnywhere.Models`: model download, checksum verification, activation.
  - Also owns manifest-backed supported-language metadata for model/runtime compatibility.
- `DictateAnywhere.Benchmark`: CPU benchmark and model recommendation.
  - Resolves candidate sets by requested transcription language scope and persists recommendation notes.
- `DictateAnywhere.Diagnostics`: structured local logs and error classification.
- `DictateAnywhere.Platform.Windows`: Win32/UIPI/integrity helpers.

Workbench-specific app components:
- `DictateAnywhere.App/Workbench/TextboxWorkbenchWindow.xaml*`
- `DictateAnywhere.App/Workbench/WorkbenchSettingsPolicy.cs`
- `DictateAnywhere.App/Workbench/WorkbenchSessionStateMachine.cs`
- `DictateAnywhere.App/Workbench/WorkbenchHotkeyRegistrationCoordinator.cs`
- `DictateAnywhere.App/Workbench/WorkbenchSpeechSession.cs`
- `DictateAnywhere.App/Workbench/WorkbenchChatOperationSession.cs`
- `DictateAnywhere.App/Workbench/WorkbenchOperationSession.cs`
- `DictateAnywhere.App/Lifecycle/LatestOperationSession.cs`
- `DictateAnywhere.App/Workbench/WorkbenchHistoryQueryCoordinator.cs`
- `DictateAnywhere.App/Workbench/WorkbenchAudioCaptureServiceAdapter.cs`
- `DictateAnywhere.App/Workbench/WorkbenchTranscriptionServiceAdapter.cs`

Global toggle rollout components:
- `DictateAnywhere.App/Experience/GlobalToggleSettingsPolicy.cs`
- `DictateAnywhere.App/Runtime/GlobalHotkeyRegistrationCoordinator.cs`
- `DictateAnywhere.App/Runtime/GlobalToggleHotkeyService.cs`

UI composition adapters:
- `DictateAnywhere.App/Composition/*`
- Windows and controls should consume these adapters instead of importing implementation-module namespaces directly.
- Guardrail enforcement test:
  - `tests/DictateAnywhere.App.Tests/UiLayerDependencyGuardrailTests.cs`

## Core Interfaces
- `IHotkeyService`
- `IAudioCaptureService`
- `ITranscriptionService`
- `ITextInsertionService`
- `IOverlayService`
- `IModelManager`
- `IBenchmarkService`
- `ISettingsStore`
- `IDiagnostics`
- `ITextToSpeechService`

## Local Build and Test
Bootstrap:
```powershell
.\setup.ps1
```

Manual build/test:
```powershell
dotnet restore DictateAnywhere.sln --force-evaluate
dotnet build DictateAnywhere.sln --configuration Release --no-restore
dotnet test DictateAnywhere.sln --configuration Release --no-restore
```

`Directory.Packages.props` owns direct package versions, and
`tests/Directory.Build.props` owns the common test-project/package contract while
explicitly importing the repository-wide build properties. All 29 projects have
checked-in package lock files; restore uses `--locked-mode`, and supply-chain
validation reconciles every resolved package with the canonical provenance
inventory. See `docs/quality/test-strategy.md` for the restore and verification
boundary.

Fast local app run without packaging:
```powershell
.\scripts\run-app.ps1
```

Stop source-run validation processes safely:
```powershell
.\scripts\stop-koncus-nai.ps1
```

The stop helper closes Koncus Nai and only an ownership-tracked Ollama process started by Koncus Nai. `-AllOllama` is an explicit destructive override for controlled test machines and should not be used in ordinary development cleanup.

Run the source app elevated:
```powershell
.\scripts\run-app.ps1 -Scope Elevated
```

Workbench mode local validation:
1. Run the app from source (`.\scripts\run-app.ps1`).
2. Open the Workbench from the main window or tray command.
3. Validate Record/Stop and the local textbox transcript flow.

## Quality and Acceptance
Quality suite:
```powershell
.\scripts\run-quality-suite.ps1
```

Documentation and supply-chain policy:
```powershell
.\scripts\validate-documentation-claims.ps1
.\scripts\security-compliance.ps1
```

Standalone project-reference guardrail check:
```powershell
.\scripts\validate-project-reference-guardrails.ps1
```

Compatibility gate:
```powershell
.\scripts\run-compatibility-gate.ps1 -ReleaseLine 1.x
```

Strict release gate:
```powershell
.\scripts\run-compatibility-gate.ps1 -EnforceReleaseEvidence -ReleaseLine 1.x
```

Milestone acceptance scripts:
- `scripts/run-milestone0-spikes.ps1`
- `scripts/run-milestone1-acceptance.ps1`
- `scripts/run-milestone2-reliability.ps1`
- `scripts/run-milestone3-elevated-acceptance.ps1`
- `scripts/run-workbench-acceptance.ps1`
- `scripts/run-global-toggle-hotkey-validation.ps1`
- `scripts/run-global-toggle-acceptance.ps1`

Workbench mode automated checks:
- `tests/DictateAnywhere.App.Tests/WorkbenchSessionStateMachineTests.cs`
- `tests/DictateAnywhere.App.Tests/WorkbenchHotkeyRegistrationCoordinatorTests.cs`
- `tests/DictateAnywhere.App.Tests/WorkbenchSettingsPolicyTests.cs`
- `tests/DictateAnywhere.App.Tests/WorkbenchOperationSessionTests.cs`
- `tests/DictateAnywhere.App.Tests/LatestOperationSessionTests.cs`
- `tests/DictateAnywhere.App.Tests/WorkbenchHistoryQueryCoordinatorTests.cs`

Global toggle automated checks:
- `tests/DictateAnywhere.App.Tests/GlobalToggleSettingsPolicyTests.cs`
- `tests/DictateAnywhere.App.Tests/GlobalToggleHotkeyServiceTests.cs`
- `tests/DictateAnywhere.Core.Tests/DictationPipelineCoordinatorTests.cs`

History persistence components and checks:
- `src/DictateAnywhere.App/History/HistoryFileAccessCoordinator.cs` owns path-scoped serialization across independent local-store instances.
- `tests/DictateAnywhere.App.Tests/HistoryFileAccessCoordinatorTests.cs`
- `tests/DictateAnywhere.App.Tests/LocalHistoryStoreTests.cs`
- `tests/DictateAnywhere.App.Tests/HistoryPersistenceArchitectureGuardrailTests.cs`
- `src/DictateAnywhere.App/History/HistoryQueryCoordinator.cs` owns the latest standalone History read snapshot without WPF dependencies.
- `src/DictateAnywhere.App/History/HistoryPersistenceFailureClassifier.cs` centralizes expected local-file failure policy.
- `tests/DictateAnywhere.App.Tests/HistoryQueryCoordinatorTests.cs`
- `tests/DictateAnywhere.App.Tests/HistoryPersistenceFailureClassifierTests.cs`
- `tests/DictateAnywhere.App.Tests/HistoryWindowArchitectureGuardrailTests.cs`
- `src/DictateAnywhere.App/History/HistoryCommandCoordinator.cs` owns serialized mutation execution, typed results, cancellation, and shutdown for both history surfaces.
- `src/DictateAnywhere.App/History/HistoryCommandContracts.cs` contains the narrow local-store command seams and result contract.
- `tests/DictateAnywhere.App.Tests/HistoryCommandCoordinatorTests.cs`
- `src/DictateAnywhere.App/Lifecycle/CoalescingRefreshSession.cs` coalesces application-level history notifications and owns refresh shutdown.
- `tests/DictateAnywhere.App.Tests/CoalescingRefreshSessionTests.cs`
- `tests/DictateAnywhere.App.Tests/HistoryViewRefreshArchitectureGuardrailTests.cs`

Reader TTS implementation and validation:
- `src/DictateAnywhere.Inference/TextToSpeech/ReaderTextToSpeechService.cs` is the single provider router. Provider identity must travel with every request; do not infer Indic Parler solely from a language that Kokoro also supports.
- `src/DictateAnywhere.Inference/TextToSpeech/IndicParlerTextToSpeechService.cs` owns the single persistent sequential worker boundary.
- `src/DictateAnywhere.Inference/TextToSpeech/IndicParlerRuntimeProvisioner.cs` owns automatic first-use runtime setup.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderLanguageRegistry.cs` is the provider-qualified Reading Studio registry for language, script, direction, timing, OCR, export, and voice capabilities.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderDocumentSession.cs` owns the current document and transactional draft/preview state; `ReaderEditableDocumentBuilder.cs` owns deterministic reconstruction of edited semantic sections. The window must not mirror either concern. See `docs/architecture/reader-document-session.md`.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderPlaybackSession.cs` owns selected-range, current-section, prepared-timing, seek/highlight, and completion state. WPF media, timers, cancellation, and rendering remain in the window. See `docs/architecture/reader-playback-session.md`.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderVoicePreviewSession.cs` owns narrator-preview cache resolution, cancellation, operation identity, and lifecycle state. The window owns WPF playback and presentation. See `docs/architecture/reader-voice-preview-session.md`.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderNarrationSession.cs` owns section synthesis serialization, content/voice-aware audio caching, timing caching, and alignment lifetime. Playback, export, publishing, and prefetch must use this boundary. See `docs/architecture/reader-narration-preparation.md`.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderNarrationPrefetchSession.cs` owns the single next-section look-ahead identity, claiming, cancellation, failure observation, and awaited shutdown. It delegates all synthesis, timing, and caching to `ReaderNarrationSession`. See `docs/architecture/reader-narration-prefetch-session.md`.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderExportPreparationService.cs` prepares ordered audiobook, video, and YouTube narration artifacts through the narration session. The window owns presentation; exporters own media output. See `docs/architecture/reader-export-preparation.md`.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderHighlightPolicy.cs` owns highlight semantics shared by the live reader and video exporter; surface adapters must not recreate style switches.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderTypographyCatalog.cs` is the only source for reader font compatibility, defaults, and live/video text geometry. See `docs/architecture/reader-typography.md`.
- `src/DictateAnywhere.App/Workbench/Reading/UnicodeReadingTextSegmenter.cs` owns grapheme-safe tokens, source spans, and paragraph direction. See `docs/architecture/unicode-reading-text.md`.
- `src/DictateAnywhere.App/Workbench/Reading/ReaderVoicePreviewCache.cs` includes provider identity in cache keys.
- `docs/guides/indic-parler-tts.md` is the authoritative device, precision, model-access, CLI, batch, and real-smoke-test guide.

The default reader path is CPU-first with `TTS_DEVICE=auto` and `TTS_DTYPE=auto`. Keep CPU and accelerator execution behind the same synthesis interface, load only one model instance per process, serialize jobs, and retry at most once on CPU after an automatic accelerator out-of-memory failure. Forced device selections must fail clearly instead of silently falling back.

## Packaging Workflow
Build dual installer artifacts:
```powershell
.\scripts\build-installer.ps1 -Version 1.0.0 -DistributionMode small
```

Validation:
```powershell
.\scripts\packaging-smoke.ps1
.\scripts\installer-upgrade-compatibility.ps1
.\scripts\installer-scenario-validation.ps1 -RunDynamicScenarios -BaseInstallerPath .\artifacts\installer\KoncusNai-Setup-Small-1.0.0-x64.exe
```

## Release Workflow (Developer View)
1. Complete feature work and tests.
2. Run quality suite and compatibility gate.
3. Build installer and validate packaging.
4. Retain generated candidate evidence under ignored `artifacts`; update durable templates or canonical summaries only when their contract changes.
5. Update `CHANGELOG.md` and release notes.
6. Tag and publish release artifacts.

One-command release runner:
```powershell
.\scripts\run-release-orchestration.ps1 -Version <major.minor.build>
```

Notes:
- Captures manual evidence without JSON editing (interactive prompts or explicit parameters).
- Rejects placeholder evidence values (`REPLACE_WITH_*`, `TBD`, etc.).
- Produces consolidated report:
  - `docs/release/release-orchestration-report.md`
  - `artifacts/release-orchestration/release-orchestration-results.json`

Release planning references:
- `docs/release/versioning-and-branching-strategy.md`
- `docs/release/release-checklist.md`
- `docs/release/rollback-checklist.md`
- `docs/release/phased-rollout-plan.md`
- `docs/release/diagnostics-feedback-loop-and-bugfix-queue.md`

## CI Expectations
- CI runs build + test + reliability + fault-injection + compatibility contract checks.
- CI also runs the project-reference guardrail check to catch new coupling and circular references early.
- CI failures are blocking for release candidate progression.

## Koncus Nai branding

Regenerate the icon family with `powershell.exe -NoProfile -STA -File scripts/generate-koncus-nai-icons.ps1`. The master and outlined wordmark live under `src/DictateAnywhere.App/Assets/Brand`.

Install the source launcher with `powershell.exe -NoProfile -File scripts/install-kn-launcher.ps1`, then use `run-kn`. The migration removes only owned legacy aliases and preserves customized or foreign commands. Validate it in isolated directories with `powershell.exe -NoProfile -File scripts/test-kn-launcher.ps1`.

The product name changes without migrating settings, history, models, credential entropy, helper/IPC identifiers, or installer UpgradeCodes. See [the implementation record](../../planning/koncus-nai-rebrand-implementation-2026-09-15.md).
