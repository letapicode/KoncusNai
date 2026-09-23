# Workbench history query coordinator

**Classification:** Local-history read boundary with stale-search, cancellation, failure, and shutdown ownership.

**Decision:** `WorkbenchHistoryQueryCoordinator` owns the latest dictation/chat history query, captured search text, parallel bounded local-store reads, filtering, expected persistence-failure translation, and stable result construction. `TextboxWorkbenchWindow` owns selection restoration, view-model projection, status text, diagnostics emission, and controls. History mutations remain outside this coordinator and use `HistoryCommandCoordinator`.

**Context and constraints:** Every search keystroke previously started an uncancelled `async void` sequence that read dictation history and then chat history. Each method read the search box only after disk I/O, so an older request could observe newer text, finish out of order, and overwrite a more recent result. Dictation and chat refresh methods also duplicated enablement, locking, filtering, and failure behavior, and window shutdown did not own outstanding history reads.

The coordinator captures settings and search text at request time. Its feature-owned `LatestOperationSession` cancels the obsolete query and waits for it before starting the replacement. Dictation and chat stores are independent files, so the accepted query reads them concurrently under their existing store locks. Only the latest completed query reaches WPF. Caller cancellation still propagates; replacement and shutdown cancellation return no snapshot. Disabled and expected-unavailable outcomes are typed, while unexpected failures reach one guarded UI boundary and are recorded without history contents. Expected persistence failures are classified by the shared History-layer policy used by both history surfaces.

The returned collections are detached arrays of normalized records. They are stable snapshots for projection; the coordinator never returns WPF view models or touches selection. Reading both histories after a dictation-only or chat-only mutation costs one additional bounded read, but it gives the sidebar one coherent snapshot and removes separate refresh races. The current 100-record bound is preserved.

**Approaches considered:**

1. **One latest-query coordinator returning both histories.** Selected because the sidebar displays and searches both collections from one search box and needs one accepted request identity.
2. **Add cancellation independently to the two window methods.** Rejected because their results could still be accepted from different request generations and enablement/failure policy would remain duplicated.
3. **Move mutations into the query coordinator.** Rejected because querying and mutation commands have different inputs, outcomes, ordering, and failure semantics. Mutation ownership is implemented separately by `HistoryCommandCoordinator`.
4. **Return Workbench view models.** Rejected because title presentation, selection, and WPF state belong to the window layer.

**Verification:** Coordinator tests cover filtered dictation/chat snapshots, stale-query cancellation and ordering, caller cancellation, and expected persistence failure. Latest-operation tests cover typed results, replacement, cancellation, unexpected failures, and ordered disposal. A source guardrail prevents direct bounded history reads and split refresh methods from returning to the Workbench window.

**Lifecycle:** Add query behavior here only when it contributes to a read snapshot; keep mutations, dialogs, migration, and WPF projection in their own boundaries.
