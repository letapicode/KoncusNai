# Workbench operation session

**Classification:** Significant internal boundary refactor with cancellation, ordered-shutdown, control-state, and deferred-settings corrections. Recording, transcription, import, and settings behavior remain compatible.

**Decision:** `WorkbenchOperationSession` owns mutual exclusion, typed active-operation identity, cancellation, import-state projection, and ordered shutdown for Workbench settings application, audio import, document import, recording startup, and transcription. `TextboxWorkbenchWindow` retains the workflows, messages, progress presentation, state-to-control mapping, and deferred-settings action. `WorkbenchSessionStateMachine` continues to own the persistent recording and transcription state after a command lease ends.

**Context and constraints:** The window previously coordinated these workflows with a raw semaphore and a separate import boolean. Each caller manually acquired, released, and projected that state into controls. Window disposal could dispose capture, transcription, hotkey, OCR, and semaphore resources while an active workflow was still using or releasing them. Document import also omitted the deferred-settings handoff performed by audio import and transcription, so a settings request could remain pending after the import finished.

An operation lease is acquired synchronously because Workbench commands use non-blocking exclusivity: a second command is rejected or settings are deferred rather than queued behind stale user intent. Every lease exposes a cancellation token. The window passes it through capture startup and stop, transcription, the media-decoding boundary, document extraction, model/readiness refresh, history refresh, and hotkey registration. Disposing a lease completes the active command. Session disposal rejects new work, cancels the active command, and waits for its lease before the window disposes runtime services.

The shared completion path first releases the lease, then refreshes both Workbench and chat controls, and finally applies the most recent deferred settings when no operation or chat request remains active. This closes the document-import handoff gap without moving UI responsibilities into the lifecycle component.

**Approaches considered:**

1. **Typed operation session with disposable leases.** Selected because these commands share exclusivity, cancellation, shutdown, and completion rules but not business logic.
2. **Keep the semaphore and add a shutdown wait.** Rejected because import state, cancellation ownership, and deferred-settings completion would remain manual conventions distributed across handlers.
3. **Move all workflows into one coordinator.** Rejected because settings, capture, transcription, and file extraction have distinct collaborators and error presentation; combining them would create a broad service with unrelated reasons to change.
4. **Merge chat and general operation sessions.** Rejected for now because chat exposes explicit user cancellation and provider-specific progress, while the general session is cancelled only by its caller or shutdown. The window coordinates their UI availability and deferred settings without conflating those policies.

**Verification:** Unit tests cover typed exclusive acquisition, import-state projection, replacement after completion, caller cancellation, pre-cancelled callers, idempotent lease disposal, repeated session disposal, cancellation-aware shutdown, and waiting for the active lease. A source guardrail prevents the semaphore and import boolean from returning to the window and requires every operation kind to remain behind the session. Changed-file formatting, the App suite, project-reference guardrails, and the full solution suite are required before commit.

**Lifecycle:** No persistence, dependency, or migration changes are involved. Rollback is the single local commit. Add new non-chat Workbench commands here only when they require the same exclusivity and shutdown contract; domain workflow logic and WPF presentation remain outside the session.
