# History Command Coordinator

**Classification:** Local-history command boundary and lifecycle.

**Decision:** `HistoryCommandCoordinator` is the single application boundary for dictation record/update/delete and chat save/delete commands. It returns typed `Succeeded`, `Disabled`, `NotFound`, `Unavailable`, or `Canceled` outcomes, translates only expected persistence failures, preserves caller cancellation, and owns serialized execution and ordered shutdown.

`IDictationHistoryCommandStore` and `IChatHistoryCommandStore` are narrow internal seams implemented by the local stores. They expose only mutation capabilities needed by the coordinator. Read behavior remains in the query boundaries, and physical file ordering remains in `HistoryFileAccessCoordinator`.

**Context and constraints:** Standalone History and Workbench previously created stores inside UI code. Seven Workbench paths separately implemented dictation edits, bulk deletion, renaming, automatic recording, chat renaming, bulk deletion, and automatic chat saving. Expected file failures could escape event handlers, and Workbench shutdown did not own writes nested inside transcription or chat operations.

Accepted commands are serialized and never replaced by a newer command. This is intentionally different from latest-query-wins behavior: replacing or rejecting an automatic write could silently lose a completed transcript or chat. Direct UI handlers check `IsBusy` to reject duplicate clicks before submission, while automatic writes already accepted by the coordinator wait their turn. Caller cancellation can remove queued work without executing it. Disposal prevents new commands, cancels active and queued commands, and waits until every accepted command unwinds.

The coordinator receives immutable record/identifier snapshots per call and uses always-on local stores. Expected environmental failures become `Unavailable` with the original exception for diagnostics. Unexpected failures propagate to guarded UI boundaries, so programming defects remain observable.

**Approaches considered:**

1. **One serialized command coordinator shared by both surfaces.** Selected because command result policy and shutdown semantics are identical, while each window still owns messages, confirmation dialogs, selection, and refresh behavior.
2. **Use `LatestOperationSession`.** Rejected because a newer delete or rename must not cancel an already accepted save, and automatic writes must never be discarded as stale.
3. **Reuse Workbench general or chat operation sessions.** Rejected because automatic history writes run inside those operations. Reacquiring the parent session would create nested ownership and blocked commands.
4. **Rely only on `HistoryFileAccessCoordinator`.** Rejected because file serialization prevents corruption but does not own UI cancellation, queued-command lifetime, typed outcomes, or shutdown.
5. **Put reads and writes in one repository-style class.** Rejected because latest-wins reads and serialized commands have different concurrency semantics and failure/result shapes.

**Verification:** `HistoryCommandCoordinatorTests` covers outcome mapping, serialization without dropped queued work, queued caller cancellation, expected-failure translation, unexpected-failure propagation, busy-state cleanup, disposal cancellation, repeated disposal, and rejection after disposal. Source guardrails prevent standalone History or Workbench from constructing mutation stores directly. Local-store concurrency tests verify physical file safety.

**Lifecycle:** The coordinator is per-window lifecycle state; the static file-access coordinator remains process-wide resource protection.
