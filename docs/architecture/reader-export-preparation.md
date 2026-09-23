# Reading Studio export-preparation architecture

## Decision record

**Decision:** `ReaderExportPreparationService` is the single adapter that turns an ordered reading selection into narration artifacts consumed by audiobook, reading-video, and YouTube publishing workflows. It reports typed narration, timing, and completion phases; `ReaderWindow` translates those phases into UI progress and invokes the existing exporters.

**Context and constraints:** Audio export, video export, and per-episode YouTube rendering previously repeated section traversal, narration-cache probes, synthesis calls, timing resolution, and `ReaderVideoExportSection` construction inside `ReaderWindow`. The underlying `ReaderAudioExporter`, `ReaderVideoExporter`, and `ReaderNarrationSession` already have focused responsibilities and should not absorb UI orchestration or each other's policies. Export preparation must preserve selected section order, reuse session caches, stop on cancellation, retain paragraph-direction metadata, and remain entirely local.

## Boundary and ownership

- `ReaderExportPreparationService` borrows the window-scoped `ReaderNarrationSession`. It owns ordered audio/video preparation and construction of complete `ReaderVideoExportSection` values. It has no independent cache or lifetime.
- `ReaderExportPreparationProgress` identifies the selected-section position, phase, relevant source section, prepared speech when available, and whether the current stage reused cached work.
- `ReaderWindow` owns file dialogs, cancellation sources, progress wording and estimated-progress presentation, appearance snapshots, completion notifications, and invocation of render/upload workflows.
- `ReaderAudioExporter` owns lossless WAV concatenation. `ReaderVideoExporter` owns pagination, frame rendering, FFmpeg provisioning, encoding, and temporary render files.
- `ReaderNarrationSession` remains the sole owner of synthesis/timing cache identity, serialization, retry-after-cancellation behavior, and alignment lifetime.

Audio preparation produces ordered `TextToSpeechResult` values. Video preparation additionally resolves a complete word-timing map and paragraph directions before constructing exporter input. YouTube uses the same single-section video path, so episode rendering cannot drift from normal video export.

## Approaches considered

1. **Selected: one concrete preparation adapter over the existing narration session.** This removes three duplicated flows without adding an interface, dependency, alternate cache, or general job framework.
2. **Move preparation into each exporter.** Rejected because exporters would then depend on speech providers and language profiles, making deterministic render tests and reuse of already-prepared input harder.
3. **Move all export UI and publishing into one coordinator.** Rejected because dialogs, WPF progress, YouTube authorization, resumable publishing, and local rendering have different lifecycles. Combining them would create a larger orchestration object instead of a cohesive boundary.
4. **Keep the loops in `ReaderWindow`.** Rejected because each new export destination could silently implement different cache, timing, cancellation, or direction behavior.

## Verification and extension rules

Automated tests cover section ordering, narration-cache reuse, complete timing, typed phase order, paragraph-direction metadata, reuse of both cached stages, cancellation before later sections, and empty-selection validation. A source guardrail prevents `ReaderWindow` from constructing `ReaderVideoExportSection` directly or bypassing the preparation service for audio, video, or YouTube preparation.

New export destinations must consume `PrepareAudioAsync`, `PrepareVideoAsync`, or `PrepareVideoSectionAsync`. Add a new method only when the destination requires a materially different prepared artifact; do not add another section loop or cache probe to the window. Provider and timing behavior belongs in `ReaderNarrationSession`, while rendering and file-format behavior belongs in the relevant exporter.

This change adds no dependency or persisted-data migration. Rollback is the local Git checkpoint that introduced the export-preparation boundary.
