# Reader narration prefetch session

**Classification:** Significant internal boundary refactor. User-visible narration behavior, provider contracts, cache identity, and project references are unchanged.

**Decision:** `ReaderNarrationPrefetchSession` owns the one-section look-ahead operation used by Reading Studio. It identifies the exact document section and narration profile, prepares both speech and timing through `ReaderNarrationSession`, allows the foreground path to claim that work once, and owns replacement, cancellation, observation, and awaited asynchronous disposal of the background operation.

**Context and constraints:** Start Sooner mode prepares the next selected section during playback. The window previously held a cancellation source, task, and section index and also implemented background preparation and failure observation. Those fields formed an asynchronous lifecycle whose correctness depended on several window paths being updated together. Prefetch must stay opportunistic, remain entirely local, reuse the narration session's semantic caches and serialization gates, and never hide a foreground error.

`ReaderNarrationSession` remains the owner of synthesis requests, audio and timing caches, cache invalidation, timing resolution, and its alignment-service lifetime. `ReaderWindow` still chooses when look-ahead is appropriate, chooses the next section and current profile, and presents foreground preparation failures. The prefetch session borrows the narration session and does not clear or dispose it.

Claiming a buffer prevents a second foreground consumer from taking the same task, but the operation remains cancellable until it completes. This matters when a document, range, language, or voice changes while foreground preparation is waiting on prefetched work. Replacing or disposing a buffer cancels both synthesis and timing through the same token. Completed speech and timing live only in `ReaderNarrationSession`; the prefetch session adds no second cache.

**Approaches considered:**

1. **Extract the concrete one-operation session.** Selected because prefetch has a distinct identity and cancellation lifecycle and already has one durable preparation dependency.
2. **Expand `ReaderNarrationSession` with UI look-ahead state.** Rejected because caching and synthesis remain useful independently of a window's current/next navigation policy.
3. **Introduce a general background-job or preparation framework.** Rejected because there is one look-ahead operation and no second caller requiring a generic scheduler.
4. **Leave the fields in `ReaderWindow`.** Rejected because operation replacement, claiming, cancellation, exception observation, and disposal could not be tested without constructing WPF.

**Consequences:** The window loses its prefetch task, cancellation source, section identity, worker method, and exception observer. The new session is deliberately narrow and does not own progress copy, range selection, media playback, provider selection, or caching. Prefetch failures remain silent in the background; if the user requests that section, normal foreground preparation retries or awaits the claimed task and reports the actionable failure.

**Verification:** Unit tests cover speech-and-timing preparation, exact section/profile identity, duplicate suppression, single-claim behavior, replacement cancellation after a foreground claim, and cancellation plus awaited disposal. A source guardrail prevents `ReaderWindow` from regaining the removed lifecycle fields or worker implementation, and the repository background-operation guard rejects discarded task-producing calls. The App test project and complete solution validation remain required before the change is committed.

**Lifecycle:** This replaces the previous window-owned prefetch implementation without persistence or migration. Rollback is the single local commit. New look-ahead identity, claiming, cancellation, or background observation behavior belongs here; synthesis, timing, and cache policy belong in `ReaderNarrationSession`.
