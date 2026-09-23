# Workbench speech session

**Classification:** Significant internal boundary refactor with a lifecycle-race correction. User-facing read-aloud commands, local processing, default synthesis request, and status copy remain compatible.

**Decision:** `WorkbenchSpeechSession` owns lazy local TTS creation, speech-preparation identity and cancellation, WAV playback state, runtime reset, and final disposal. `TextboxWorkbenchWindow` supplies the text, presents progress and failures, and maps session state to controls; it no longer stores the TTS service, cancellation source, preparation/playing flags, or audio player.

**Context and constraints:** Workbench read-aloud previously spread one lifecycle across four fields, three handlers, control-state calculation, settings-driven runtime reset, and final disposal. A settings reset cancelled synthesis and immediately disposed its service without waiting for the asynchronous preparation to finish. Read-aloud must remain local, preserve lazy model initialization, allow old audio to continue while a replacement is prepared, reject late results after cancellation, and keep WPF copy outside the session.

The session represents preparation and playback as independent flags because both can legitimately be true while a new response is synthesized and the previous response is still playing. A version check prevents cancelled or replaced synthesis from starting late playback. Reset marks the session unavailable, cancels preparation, stops playback, waits for the owned operation to unwind, disposes the current TTS service, and then permits lazy recreation. Final disposal performs the same ordered shutdown and also disposes the playback adapter.

`IWavAudioPlaybackService` is an internal composition seam around the WAV player. It exists so lifecycle, cancellation, reset, playback completion, playback failure, and disposal behavior can be tested without starting Windows audio. The runtime adapter uses WPF `MediaPlayer` on its owning dispatcher and adds no project reference.

**Approaches considered:**

1. **Extract one concrete speech session with a narrow playback seam.** Selected because service lifetime, cancellation, playback state, reset, and disposal change together and now have deterministic tests.
2. **Extract synthesis but leave playback and flags in the window.** Rejected because cancellation and control state would still have two owners and reset could still race playback.
3. **Reuse `ReaderVoicePreviewSession`.** Rejected because Workbench reads arbitrary response text, owns a lazily recreated service, and plays generated WAVs directly; the reader session resolves provider-qualified preview assets and has a different cache contract.
4. **Create a general media-job framework.** Rejected because there is one local speech workflow and no second caller requiring scheduler or pipeline abstractions.

**Consequences:** The 3,000-line Workbench window loses its speech service, player, cancellation source, and lifecycle booleans. All UI wording and exception-to-message mapping remain in the window. Natural playback completion and asynchronous media failure now clear session state before notifying the window, so the controls cannot remain stuck in a playing state after audio ends.

The session never holds its synchronization lock while invoking the dispatcher-bound playback adapter. This prevents a UI-thread stop from deadlocking with a synthesis continuation while the operation version still rejects playback that arrives after stop, reset, or disposal.

**Verification:** Unit tests cover lazy synthesis and request construction, explicit stop, natural completion, synchronous and asynchronous playback failure, service reuse, cancellation with a late result, ordered reset with service replacement, and ordered final disposal. A source guardrail prevents the removed lifecycle fields from returning to the window and preserves completion-aware delegation. Changed-file formatting, the App suite, project-reference guardrails, and the full Release solution suite are required before commit.

**Lifecycle:** No persistence, dependency, or migration changes are involved. Rollback is the single local commit. New Workbench speech preparation, cancellation, playback, reset, or disposal behavior belongs here; WPF messages and controls remain in `TextboxWorkbenchWindow`, and provider/model behavior remains behind `ITextToSpeechService`.
