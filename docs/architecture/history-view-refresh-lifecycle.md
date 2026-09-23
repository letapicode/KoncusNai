# History View Refresh Lifecycle

**Classification:** Local-history notification and shutdown lifecycle.

**Decision:** `CoalescingRefreshSession` owns application-level history refresh notification state. `App` supplies the refresh pass, observes the one task that starts each run, and reports failures. The standalone History and Workbench windows continue to own their feature-specific latest-query coordinators.

**Context and constraints:** A persisted dictation notification can arrive while an earlier refresh is reading local history. The previous implementation maintained `historyRefreshQueued` and `historyRefreshRunning` flags in `App`, started unowned tasks, and restarted from `finally`. Application exit neither canceled nor awaited that loop.

Requests accepted during an active refresh are coalesced into one subsequent pass. Passes never overlap. A request arriving after the run becomes idle starts a new run. The caller that starts a run owns its task and therefore owns failure reporting; coalesced callers do not attach duplicate observers. The session retains the first pass failure while completing any already-requested pass, then returns that failure to the initiating application boundary.

Disposal rejects later requests, cancels the active pass, and waits for it to unwind. `App.OnExit` disposes the refresh session before closing either history surface. `HistoryWindow.DisposeAsync` is idempotent and awaitable, allowing application shutdown to wait for the window's query and command coordinators rather than relying only on an `async void` close event.

History is always active. The application does not reload settings for history notifications; an open History or Workbench surface refreshes its bounded local snapshot through its feature coordinator.

**Approaches considered:**

1. **Keep two flags in `App`.** Rejected because flag transitions, failure ownership, cancellation, and shutdown are lifecycle infrastructure that require deterministic tests.
2. **Cancel the active refresh whenever another notification arrives.** Rejected because notifications are edge-triggered; coalescing guarantees a pass after changes that arrive during the current read.
3. **Run every notification concurrently.** Rejected because it adds redundant file reads and leaves final presentation ordering to timing.
4. **Reload settings from disk on every notification or search edit.** Rejected because history has no enablement or retention setting.
5. **Create a global settings singleton.** Rejected because hidden mutable state would make window behavior and tests harder to reason about.

**Verification:** `CoalescingRefreshSessionTests` covers a single pass, coalescing, non-overlap, subsequent runs, failure propagation, cancellation, repeated disposal, and rejection after disposal. Architecture guardrails prevent the manual flags from returning and require settings propagation plus awaited session/window shutdown.

**Lifecycle:** The refresh session does not own the file format or legacy migration.
