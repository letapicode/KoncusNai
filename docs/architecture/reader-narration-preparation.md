# Reading Studio narration-preparation architecture

## Decision record

**Decision:** `ReaderNarrationSession` is the single preparation boundary for section speech and display-token timing. `ReaderWindow` selects a section and narration profile, reports progress, controls playback, and coordinates exports; it does not construct provider requests, serialize synthesis, or own preparation caches.

**Context and constraints:** Playback, range preparation, audio export, video export, YouTube rendering, and next-section prefetch all need the same synthesized section audio. Most also need the same resolved word timeline. Those workflows previously repeated cache checks and synthesis calls in `ReaderWindow`, while request construction, serialization, cache invalidation, and timing lifetime were UI concerns. A voice or edited section could only avoid stale cache reuse if every event remembered to clear index-keyed dictionaries. The speech service must remain shared with voice preview, and all preparation stays local.

## Boundary and ownership

- `ReaderNarrationProfile` is an immutable, normalized snapshot of the provider, language, voice, description, and word-timing strategy selected for one operation. It is the only type that constructs a `TextToSpeechRequest` for section narration.
- `ReaderNarrationText` composes the exact transcript used by both synthesis and forced alignment, including the semantic-title pause.
- `ReaderNarrationSession` owns synthesis serialization, narration and timing caches, timing resolution, and the alignment-service lifetime.
- `ReaderExportPreparationService` adapts ordered audio/video export selections to this session and constructs complete video-export sections without owning another cache.
- `ReaderWindow` owns UI cancellation sources, prefetch scheduling, progress stages, media players, export coordination, and the shared speech-service lifetime. The session borrows that service because voice preview also consumes it.
- `ReaderTimingResolver` remains the policy boundary that selects native, forced-aligned, or deterministic timing and validates a complete map.

The session exposes explicit cache probes only so the UI can distinguish “using prepared narration” from “creating narration” in progress copy. All cache misses must still flow through `GetSpeechAsync` or `GetTimingAsync`; workflows must not store results themselves.

## Cache identity and concurrency

Speech identity includes section index, exact narration text, normalized language and provider IDs, voice ID, and voice description. Timing identity additionally includes the audio path, audio duration, and timing strategy. This prevents an edited section, changed voice, replaced audio file, or changed timing policy from receiving stale work even if a UI invalidation is missed.

The audio cache also verifies that its generated file still exists. Missing files become cache misses. Synthesis and timing each use a session-wide asynchronous gate and recheck the cache after entering it, so concurrent playback, export, or prefetch requests for the same identity do not duplicate expensive work. Cancellation releases the gate and a later request may retry normally.

`Clear` invalidates speech and timing together when the document or active selection changes. This is an eager memory cleanup and UI-state reset; cache-key identity remains the correctness backstop.

## Approaches considered

1. **Selected: one session component behind the existing speech and alignment contracts.** It removes repeated orchestration without adding a new interface or changing provider modules.
2. **Keep index-keyed dictionaries in the window.** Rejected because correctness depends on scattered event handlers and every new workflow must duplicate the preparation sequence.
3. **Create a general job/pipeline framework.** Rejected because the current workflows need one concrete two-stage operation. A generic scheduler would add lifecycle and callback complexity without a second use case.
4. **Move voice preview into the session.** Rejected because previews have a separate on-demand cache and playback lifecycle. Sharing the speech-service instance does not make preview a section-preparation responsibility.

## Verification and extension rules

Automated tests cover exact request construction, semantic-title narration, cache reuse, content and voice identity, concurrent de-duplication, audio-specific timing identity, unified invalidation, cancellation recovery, and dependency disposal. A source guardrail prevents `ReaderWindow` from regaining provider request construction, timing-resolver construction, or private preparation caches.

New playback workflows must use this session, and new export workflows must use it through `ReaderExportPreparationService`. New request fields that affect generated audio must be added to `ReaderNarrationProfile` and the speech cache key in the same change. New timing inputs must likewise be reflected in the timing key. Do not add a parallel cache or provider-specific branch to the window.

This change has no persisted-data migration and adds no dependency. Rollback is the local Git checkpoint that introduced the session boundary.
