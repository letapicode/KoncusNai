# Workbench chat operation session

**Classification:** Significant internal boundary refactor with shutdown, cancellation, and deferred-settings race corrections. Chat providers, prompts, and history persistence remain compatible; Stop now cancels model setup as its existing UI affordance implied.

**Decision:** `WorkbenchChatOperationSession` owns mutual exclusion, typed active-operation identity, model-work cancellation, busy/cancelable state, and ordered shutdown for Workbench model setup, chat completion, and quick local replies. `TextboxWorkbenchWindow` retains provider-specific work, progress presentation, messages, timers, transcript mutation, and the deferred-settings action.

**Context and constraints:** The window previously coordinated these operations with a semaphore, a busy boolean, and a nullable cancellation source. Each caller manually acquired and released the semaphore. Window disposal cancelled an active completion but disposed the chat service and semaphore without waiting for the request to unwind, so the request could use a disposed service or release a disposed semaphore. Settings updates deferred during model setup were not applied when setup completed because only completion and quick-reply paths performed that handoff.

An operation lease is acquired synchronously because callers only need non-blocking exclusivity. The lease kind determines cancellation policy: model setup and model completion receive the session-owned token exposed by the lease, while the in-process quick reply completes naturally. Disposing the lease completes the active operation. Session disposal rejects new work, cancels model work when possible, and waits for the active lease before runtime services are disposed.

The window completes every lease in `finally`, refreshes controls after session state is idle, and then applies deferred settings. This single completion path is also used by model setup, closing the previous settings handoff gap. The Stop control is now based on `CanCancel`, so it is offered for model setup and completion but hidden for the immediate local reply.

**Approaches considered:**

1. **Typed operation session with disposable leases.** Selected because the three workflows share lifecycle rules but not business logic. It makes exclusivity, cancellation, and shutdown independently testable.
2. **Keep the semaphore and add a disposal wait.** Rejected because busy and cancellation ownership would remain duplicated across callers, and model-setup settings completion would still require another manual convention.
3. **Move provider setup and chat generation into one coordinator.** Rejected because provider behavior, transcript mutation, UI progress, and lifecycle are separate reasons to change.
4. **Make every operation cancelable.** Rejected because the synchronous in-process quick reply has no meaningful cancellation point. All model setup APIs do support cancellation, so their operation token is propagated through download, activation, provisioning, repair, and readiness refresh.

**Verification:** Unit tests cover typed exclusive acquisition, operation replacement after completion, model-work cancellation, non-cancelable quick replies, idempotent lease disposal, repeated session disposal, cancellation-aware shutdown, and waiting for an active quick reply. A source guardrail prevents the semaphore, busy boolean, and cancellation-source fields from returning to the window. Changed-file formatting, the App suite, project-reference guardrails, and the full solution suite are required before commit.

**Lifecycle:** No persistence, dependency, or migration changes are involved. Rollback is the single local commit. New Workbench chat operation kinds must declare their cancellation policy here; provider implementations and UI behavior remain outside this session.
