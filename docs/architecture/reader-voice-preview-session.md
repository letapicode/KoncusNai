# Reading Studio voice-preview session architecture

## Decision record

**Decision:** `ReaderVoicePreviewSession` is the single owner of narrator-preview resolution, cancellation, operation identity, and `Idle`/`Preparing`/`Playing` lifecycle state. `ReaderWindow` owns the WPF `MediaPlayer`, status copy, progress visibility, and accessible button presentation.

**Context and constraints:** Voice preview previously split its cache operation, cancellation source, preparing flag, and playing flag between `ReaderVoicePreviewCache` and `ReaderWindow`. A preview could be cancelled while synthesis was completing, and correctness depended on several event handlers resetting the same fields. The preview button also presented a play action while preparation was in progress even though clicking it stopped the operation. The extraction must preserve bundled-preview preference, generated-preview caching, local-only synthesis, provider-qualified cache identity, and selection-change cancellation without adding a media framework or taking ownership of the shared speech service.

## Boundary and invariants

- `ReaderVoicePreviewSession` borrows the shared `ITextToSpeechService` and owns a `ReaderVoicePreviewCache`, the active cancellation source, lifecycle state, and monotonically increasing operation version.
- `ReaderVoicePreviewCache` remains the file/asset boundary: it validates bundled assets, resolves provider/language/voice-specific filenames, performs local synthesis when required, and atomically installs generated cache files.
- `ReaderWindow` owns `MediaPlayer.Open`, play/stop, media events, progress controls, accessible labels, and user-facing failure text.
- Starting another preview cancels the previous resolution. A completion may enter `Playing` only when its version is still current and its token has not been cancelled.
- Stop and disposal enter `Idle` before cancellation is signalled, so a late continuation cannot restore stale state.
- Playback completion/failure transitions only from `Playing`; stale WPF events in `Idle` or `Preparing` are ignored.
- Preparing and playing both expose the stop action. Starting document preparation, import, audio/video export, or publishing stops preview audio first.
- The session never disposes the borrowed speech service. The window retains that lifetime responsibility.

## Approaches considered

1. **Selected: one concrete lifecycle session over the existing cache.** This co-locates cancellation and state while preserving the tested asset/cache boundary.
2. **Move WPF `MediaPlayer` into the session.** Rejected because it would add dispatcher and media-event coupling to otherwise deterministic lifecycle logic.
3. **Add more Boolean resets to window event handlers.** Rejected because `preparing` and `playing` could still drift and late asynchronous completions would remain unversioned.
4. **Introduce a general audio-preview framework or interface hierarchy.** Rejected because Reading Studio has one preview workflow and no second implementation requiring polymorphism.

## Verification and extension rules

Unit tests cover cached success, explicit stop, late completion after cancellation, replacement by a newer operation, synthesis failure, playback completion, disposal, and rejection of work after disposal. Existing cache tests continue to cover native-language samples, provider identity, bundled-asset verification, and generated-file reuse. A source guardrail prevents `ReaderWindow` from regaining preview cancellation or lifecycle fields.

New preview resolution, cancellation, or lifecycle transitions belong in `ReaderVoicePreviewSession`. New asset validation or cache-file behavior belongs in `ReaderVoicePreviewCache`. WPF playback and copy remain in `ReaderWindow`. Do not introduce another preview cancellation source or mirror the session state in Boolean window fields.

This change adds no dependency or persisted-data migration. Existing cache files remain compatible. Rollback is the local Git checkpoint that introduced the voice-preview session boundary.
