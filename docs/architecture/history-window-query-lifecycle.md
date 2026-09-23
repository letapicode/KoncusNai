# History Window Query Lifecycle

**Classification:** Local-history read and window-lifecycle boundary.

**Decision:** `HistoryQueryCoordinator` owns the latest standalone History query, captured search text, bounded local-store read, filtering, expected-failure translation, and stable result construction. `HistoryWindow` owns view-model projection, selection restoration, status text, diagnostics, and controls.

The reusable `LatestOperationSession` lives under `DictateAnywhere.App.Lifecycle`. Workbench settings refresh, Workbench history query, and standalone History query each own separate instances. Mutations use the serialized `HistoryCommandCoordinator` because accepted writes must not be replaced as stale.

**Context and constraints:** Window load, each search-text change, local save/delete completion, and application history notifications could all start `RefreshHistoryAsync`. The previous window read the search box after asynchronous file I/O and accepted every completion. An obsolete request could therefore render newer text under the wrong request identity or overwrite a newer result. Closing the window did not cancel or wait for active reads or mutations. Expected file failures from `async void` mutation handlers were also only partially observed.

The coordinator captures settings and search text before I/O. A replacement cancels its predecessor and waits for it to unwind, and only the latest completed snapshot reaches WPF. Caller cancellation remains distinguishable from replacement cancellation. Expected-unavailable outcomes are typed. Unexpected failures propagate to a guarded UI boundary, are logged without history contents, and produce an unavailable state instead of terminating the dispatcher.

`HistoryPersistenceFailureClassifier` is the single policy for expected local-file failures. It is shared by both query coordinators and `HistoryCommandCoordinator`. Programming failures are not translated by coordinators; they remain observable at the diagnostic UI boundary.

The window has one lifetime cancellation source. Closing it starts query and command-coordinator disposal together, awaits both, and only then disposes the lifetime source. Its idempotent `DisposeAsync` also lets application shutdown await that work explicitly. A late operation cannot be left unobserved or continue updating an active window after shutdown.

Application-level record notifications are coalesced separately as documented in `history-view-refresh-lifecycle.md`. History is always active and no settings or tray toggle controls it.

**Approaches considered:**

1. **Feature-specific query coordinator over a shared lifecycle primitive.** Selected because both history surfaces need the same ordering semantics but return different snapshots and retain different presentation behavior.
2. **Reuse `WorkbenchHistoryQueryCoordinator` directly.** Rejected because the standalone window does not query chat history or render Workbench grouping, and importing a Workbench feature into History would invert ownership.
3. **Put cancellation fields back in `HistoryWindow`.** Rejected because replacement ordering, cancellation-source disposal, and shutdown waiting are concurrency infrastructure with deterministic unit coverage.
4. **Swallow every exception in the coordinator.** Rejected because expected environmental failures and programming defects require different observability. The coordinator translates only the shared expected-failure set.
5. **Move save/delete behavior into the query coordinator.** Rejected because commands and read snapshots have different inputs, ordering, and outcomes. Commands use their dedicated serialized coordinator.

**Verification:** `HistoryQueryCoordinatorTests` covers filtering, read limits, stale-query replacement, caller cancellation, expected failure translation, and unexpected failure propagation. `LatestOperationSessionTests` covers the shared replacement and disposal primitive. Classifier and architecture guardrail tests protect failure policy and window ownership.

**Lifecycle:** Mutation architecture is documented separately in `history-command-coordinator.md`. The current application has no encrypted-history import lifecycle.
