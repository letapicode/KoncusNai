# Workbench settings-menu refresh session

**Classification:** Small internal lifecycle refactor with replacement ordering, cancellation, fault-observation, and shutdown corrections. Model discovery, selection, and settings-menu presentation remain compatible.

**Decision:** `LatestOperationSession` owns replaceable-operation cancellation, ordered replacement, completion, and disposal. The settings menu uses one instance for its refresh lifecycle. `TextboxWorkbenchWindow` retains the model query, WPF dispatcher yield, control updates, diagnostics, and user-facing failure message. The primitive lives under the app-level `Lifecycle` namespace because standalone History now has the same lifecycle requirement.

**Context and constraints:** Opening the compact settings menu previously launched model-option refresh as an unobserved task and stored its cancellation source directly in the window. Closing the menu cancelled and immediately disposed the source without waiting for the refresh. Window shutdown did the same, so a late refresh could still touch WPF controls. Reopening the menu could also start a replacement before the cancelled predecessor unwound. Separately, settings application refreshes the same controls and could overlap the menu task.

`RunLatestAsync` cancels the prior operation and waits for its completion before invoking the replacement. The session preserves the caller context so window-owned delegates can resume on the WPF dispatcher, but the session itself does not reference WPF. It supports typed results and caller cancellation because Workbench history search is a second real consumer of the same lifecycle semantics. Cancellation owned by replacement, menu close, caller, or shutdown is represented as an incomplete result. Unexpected failures propagate to the caller's fault boundary.

Settings application calls `CancelAndWaitAsync` before updating model controls. Window disposal starts settings-menu, chat, and general-operation session disposal before awaiting them, so all cancellation begins promptly and runtime services remain alive until their active work has unwound.

**Approaches considered:**

1. **Shared latest-operation session with feature-owned instances.** Selected because settings refresh and history query now share replacement ordering, cancellation ownership, and shutdown semantics. Their feature logic and session instances remain separate.
2. **Await the original method directly from the click handler.** Rejected because menu close and shutdown still need an owner that can cancel and wait, and rapid reopen still needs predecessor ordering.
3. **Add the menu refresh to `WorkbenchOperationSession`.** Rejected because a lightweight presentation refresh should not acquire the exclusive recording/import/settings command boundary or disable unrelated Workbench actions.
4. **Move model discovery into the session.** Rejected because discovery and WPF projection are business/UI behavior, not lifecycle ownership.
5. **Keep a settings-specific lifecycle implementation after history gained identical requirements.** Rejected because that would duplicate concurrency code now that a second concrete consumer exists.

**Verification:** Unit tests cover successful refresh state, cancellation, cancel-and-wait, ordered replacement, unexpected-failure propagation, repeated disposal, shutdown cancellation, and waiting for active work. A source guardrail prevents the raw cancellation field and unobserved task launch from returning to the window. Changed-file formatting, the App suite, project-reference guardrails, and the full solution suite are required before commit.

**Lifecycle:** No persistence, dependency, or migration changes are involved. Rollback is the local refactor commit. Keep model-query and presentation changes outside this session; extend it only for lifecycle semantics shared by genuine latest-request-wins operations.
