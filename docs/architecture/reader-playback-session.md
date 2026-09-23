# Reading Studio playback-session architecture

## Decision record

**Decision:** `ReaderPlaybackSession` is the single owner of Reading Studio's selected range, current section, activation state, prepared word-timing map, duration, highlight cursor, and terminal playback state. `ReaderWindow` performs WPF media and presentation side effects by applying explicit session transitions.

**Context and constraints:** Playback previously depended on nine mutable fields spread across range selection, single/full-range preparation, previous/next navigation, media completion, seek, document replacement, and narration invalidation. Those fields could describe impossible combinations, and changing sections did not itself guarantee that old timing and highlights were cleared. A tenth field, `currentSectionSpeech`, was assigned and reset but never read. The extraction must retain `MediaPlayer` behavior, prefetch and cancellation lifetimes, narration caching, range semantics, and reliable replay without turning the session into a WPF wrapper.

## Boundary and invariants

- `ReaderPlaybackSession` owns indices, selected-range activation, current timing, media duration, highlighted word, and whether the selected range reached its end.
- `ReaderWindow` owns `MediaPlayer`, dispatcher timers, cancellation sources, asynchronous preparation, prefetch tasks, rendering, controls, and user-facing messages.
- A document reset requires at least one section and returns to section zero with an inactive, unprepared one-section selection.
- A selected range must be ordered and contained by the current document. Selecting another range clears prepared state and requires explicit activation before playback/export.
- Moving to another section always clears the prior timing map, duration, highlight, and terminal state. Prepared state is derived from the presence of a timing map rather than maintained as a second Boolean source of truth.
- Media completion advances only inside the activated range. Completing the final section retains its prepared timing for replay, marks the final word, and enters terminal state.
- Seek clamps to the current media duration and resolves the highlighted word from the same timing map used by playback. Replay uses one shared end-of-media tolerance.
- Preparation invalidation preserves the user's selected indices but deactivates the range and discards all section-specific state.

## Approaches considered

1. **Selected: one pure playback session with explicit transitions.** This makes the coupled invariants independently testable while keeping media and UI lifecycles in their existing owner.
2. **Wrap `MediaPlayer` and dispatcher timers in a playback service.** Rejected because it would move WPF event wiring and thread-affinity concerns without improving the domain state model.
3. **Extract only a range value object.** Rejected because section navigation, prepared timing, highlight, seek, and completion must still change together; leaving them in the window would preserve the original split ownership.
4. **Introduce a general state-machine framework.** Rejected because there is one concrete workflow and direct transition methods are easier to audit.

## Verification and extension rules

Unit tests cover initial state, range validation, bounded navigation, preparation reset/completion, automatic advance, final completion, seek clamping, replay, invalidation, inactive-completion rejection, and document replacement. The existing WPF initialization test verifies that the window's pause handler applies the session transition. A source guardrail prevents `ReaderWindow` from regaining the removed range and prepared-playback fields.

New selection, navigation, seek, or playback-completion state belongs in `ReaderPlaybackSession`. New `MediaPlayer`, timer, cancellation, progress, or rendering behavior belongs in `ReaderWindow` or an existing focused adapter. Do not mirror session state in window fields or store synthesized speech in the playback session; narration artifacts remain owned by `ReaderNarrationSession`.

This change adds no dependency or persisted-data migration. Rollback is the local Git checkpoint that introduced the playback-session boundary.
