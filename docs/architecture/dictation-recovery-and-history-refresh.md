# Dictation Recovery and Live History Refresh

## Decision

Represent insertion blocks with a typed reason, treat only ordinary target drift as transcript recovery, and publish an application-local notification only after local history persistence succeeds.

## Context and constraints

Transcription can finish after focus has moved away from the application where recording began. Windows may reject restoration of that original target. Previously, this expected insertion result became an exception before the completed transcript reached history. The same generic blocked outcome also covers secure fields, explicitly blocked applications, non-editable targets, and Windows privilege boundaries, so treating every block as recoverable would weaken the insertion-safety contract.

History is always-on and local. A recovery path must not claim that persistence succeeded before it did and must not introduce polling or a second history store.

## Selected design

- `InsertionResult` carries `InsertionBlockReason` and whether a recovery clipboard copy was actually created.
- `TargetChanged` is the only blocked reason accepted by the pipeline as a completed recovery. Other blocked reasons retain the error path.
- The insertion service creates the clipboard recovery copy. The pipeline records the transformed transcript once with the `global-hotkey-recovery` source.
- The completion message is selected after the history write and reflects the recovery copies that really exist.
- `CachingDictationHistoryRecorder` publishes through one application-owned notifier after its inner persistent recorder succeeds. The WPF composition root coalesces notifications received during an active refresh and updates only open history views on the dispatcher.
- The overlay reuses the monitor anchor captured for the recording session and renders later states through the same bottom-center indicator window. Normal completion uses a terse animated sequence (letter-spinning **Transcribing**, then a green check); target-recovery and error outcomes keep explanatory text so the animation never obscures a required action.

## Approaches rejected

- Matching error-message text was rejected because copy changes or localization would silently alter control flow.
- Recovering every blocked result was rejected because it conflates target drift with security and privilege decisions.
- Polling the history file was rejected because it adds latency, repeated I/O, and another lifecycle to manage.
- Refreshing before persistence was rejected because the UI could remain stale after reading ahead of the write.

## Consequences

The shared insertion result contract has one additional typed classification, but module dependencies remain unchanged. Local history is always on. If clipboard recovery fails but local history succeeds, the notification states that only history is available; if history fails, it is not reported as saved.

## Verification

Unit tests cover target-drift recovery, secure-field non-recovery, clipboard preservation, publish-after-persistence behavior, transcribing/completion presentation, bottom-center anchor reuse, and continuous chat-size persistence. Windows manual verification remains necessary for real focus restoration, monitor placement, DPI rendering, and Sticky Notes behavior.
