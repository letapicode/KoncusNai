# Extreme edge-case review and fix plan

Date: 2026-09-14. Reviewed baseline: `0173a35` plus the current working tree.

Status: review and planning only. No production code or test source was changed by this review. Existing Workbench/accessibility edits were present, and another change to the hardening backlog appeared during the review; those changes are not part of this work.

## Scope and confidence

This review followed global dictation capture, chunk transcription, insertion and undo, clipboard access, settings/history persistence, Reader narration/export, local workers/model acquisition, and publishing. It concentrated on boundary conditions and failure ordering, rather than style or architecture cleanup. This is a targeted risk review, not a claim that every file or native integration has been exhaustively audited.

The twelve findings below have concrete source-level failure paths. Unless explicitly stated otherwise, their proposed reproductions have **not** been executed end to end. Passing existing tests does not validate the missing cases. The priorities use P1 for potential data loss, unsafe delivery, or major workflow failure, and P2 for narrower resource/format failures; they do not change the canonical backlog's severity/status fields.

## Prioritized findings

| ID | Priority | Failure | Main owner |
| --- | --- | --- | --- |
| EC-01 | P1 | A valid full-negative PCM sample overflows silence analysis | Audio |
| EC-02 | P1 | Switching fields within one window bypasses captured-target validation | Insertion |
| EC-03 | P1 | A paste that already happened can be followed by duplicate typing | Insertion |
| EC-04 | P1 | Elevated insertion bypasses the configured application blacklist | Insertion/helper |
| EC-05 | P1 | A torn final history line absorbs the next successful append | History |
| EC-06 | P1 | One syntactically valid but malformed chat record blocks unrelated saves | History |
| EC-07 | P1 | Clipboard timeout permits late mutations after the caller has recovered | Clipboard |
| EC-08 | P1 | Clipboard restoration overwrites newer user data; rich formats are lost | Clipboard |
| EC-09 | P1 | “Undo last insertion” can undo an unrelated edit or another document | Insertion/undo |
| EC-10 | P1 | Failed dictation sessions abandon still-running transcription tasks | Dictation pipeline |
| EC-11 | P1 | A checkpoint failure discards a successfully returned upload identity | Publishing |
| EC-12 | P2 | Chunked recording still retains the entire recording and an unbounded task backlog | Audio/pipeline |

### EC-01 — Full-negative PCM overflow

**Evidence:** `src/DictateAnywhere.Audio/WasapiAudioCaptureService.cs:360,379`; `src/DictateAnywhere.Audio/Pcm16AudioTransform.cs:170,183,215,226`.

These calls pass a `short` to `Math.Abs`. For valid PCM16 value `-32768`, the selected overload cannot represent the positive result and throws `OverflowException`. Assigning the result to an `int` afterward does not fix overload selection. Conversion/gain clipping can produce this exact value.

**Trigger:** Capture a clipped negative sample at a position visited by the trailing-silence or trim scans. For the simplest trailing scan, the final two input bytes are `00 80`. Silence analysis runs outside the conversion-only exception guard in `OnDataAvailable`; finalization can throw as well. The session can fail despite receiving valid PCM.

**Fix:** Widen each sample before taking its absolute value. Audit all PCM amplitude calculations together. Keep the callback's failure reporting and buffer state consistent if later analysis fails.

**Regression gate:** Exercise `-32768`, `-32767`, zero, and `32767` in the start, interior, and end of buffers; include gain-induced clipping, chunk boundaries, and final flush. Assert capture/transcription completion, not just conversion output.

**Evidence actually executed:** A direct runtime call to `Math.Abs((short)-32768)` threw `System.OverflowException`. This was an API-level check using the available PowerShell runtime, not a full application reproduction.

### EC-02 — Stale field identity survives foreground validation

**Evidence:** `src/DictateAnywhere.Insertion/WindowsTextInsertionService.cs:110-120,399-425,470-502`.

`GetInitialFocusContext` returns the captured context. `TryRestoreCapturedTargetAsync` accepts an unchanged foreground window handle, without checking the focused editable element. For a captured editable target resolved as `RawFocusedElement`, subsequent editable-focus recovery immediately skips restoration. Preflight consequently evaluates the old field, while input dispatch goes to the currently focused field.

**Trigger:** Start dictation in field A, then focus field B in the same window while transcription runs. Field B may be a different form control, a password field, or a different browser tab's editor. The top-level window still matches. An old non-password classification can therefore authorize input into the new target.

**Fix:** Carry a target identity that includes the process instance, top-level window, and editable control/automation identity. Reacquire and validate the current target immediately before dispatch and after asynchronous focus recovery. If the original editor cannot be established, return a target-changed recovery outcome. Apply the same identity contract across the helper boundary. Treat reused window handles as a new target unless process identity also matches.

**Regression gate:** Same HWND/PID with A-to-B field changes; normal-to-password changes; browser tab changes; destroyed/reused HWND; target changes during clipboard acquisition. Assert zero input events to the replacement target. Include a positive case that safely restores the original editor.

### EC-03 — Verification ambiguity causes duplicate delivery

**Evidence:** `src/DictateAnywhere.Insertion/WindowsTextInsertionService.cs:542-566,630-643,728-774`; `src/DictateAnywhere.Core/Contracts/InsertionResult.cs`.

Clipboard setup failure and “paste dispatched but verification timed out” both become `UnknownOutcome`. The fallback path types the entire text for either case. The verifier also requires the observed value to change, so replacing selected text with identical text is treated as failure even though the paste succeeded.

**Trigger:** Select `hello` in a normal editable control, dictate `hello`, and paste. The visible value remains unchanged; verification times out; Unicode fallback types a second `hello` after the first paste collapsed the selection. Delayed/stale UI Automation values produce the same double-delivery risk. A focus switch during verification can additionally send the fallback to another target.

**Fix:** Separate “definitely no input dispatched” from “possibly or partially delivered.” Permit automatic fallback only in the first case. Preserve dispatch certainty through partial `SendInput` results. After possible delivery, return an uncertain result with recovery rather than replaying text. Strengthen verification using target identity and selection/edit context; a substring match alone is not proof of this operation.

**Regression gate:** Identical selection replacement, delayed observation beyond the verification window, stale observation, partial paste shortcut dispatch, and focus changes after dispatch must each produce at most one delivery attempt. Clipboard acquisition failure before input should still permit safe fallback.

### EC-04 — Elevated helper skips the user's blacklist

**Evidence:** `src/DictateAnywhere.Insertion/WindowsTextInsertionService.cs:275-304`; `src/DictateAnywhere.UiAccessHelper/Program.cs:54-70`.

The privilege-boundary branch invokes the elevated helper before the blacklist check. The helper constructs its insertion service with an empty `BlockedProcessNames` list. A successful helper response returns before host blacklist evaluation.

**Trigger:** Enable elevated insertion, blacklist an application, and dictate into an elevated instance of that application with a working signed helper. The helper can insert despite the configured block.

**Fix:** Evaluate application policy before routing to the helper. Pass enough target/policy context for the helper to validate the authorized target again after process startup. Preserve structured block reasons in the response. The helper already enables secure-field detection; do not misdescribe this as the helper having no secure-field protection.

**Regression gate:** For a blacklisted elevated target, assert the helper is never called and no recovery clipboard copy is created as a side effect of a policy block. Verify an allowed elevated target still works and a target change during helper startup is rejected.

### EC-05 — Torn JSONL tail consumes the next record

**Evidence:** `src/DictateAnywhere.App/History/VersionedJsonLinesFile.cs:34-49,74-80`; `tests/DictateAnywhere.App.Tests/LocalHistoryStoreTests.cs:35-52`.

Append writes the next serialized record immediately followed by a newline; it does not ensure that the existing final record ends with a delimiter. Reads skip the entire resulting malformed line. The corruption test appends `not-json` **with a newline**, so it misses a real interrupted-write tail.

**Trigger:** A crash, canceled write, or disk-full error leaves `{"version":1,"record":...` without a final newline. The next append succeeds but is concatenated into the damaged line and disappears from reads. Even a complete JSON object without a trailing delimiter causes the next record to merge with it.

**Fix:** Under the existing file lease, inspect the tail and establish a record boundary before appending. Preserve the damaged tail for recovery rather than silently discarding it. Define and test what “append succeeded” promises about flushing and durability. Keep framing repair bounded; do not read the whole file merely to inspect its last bytes.

**Regression gate:** Truncated JSON, complete JSON without newline, incomplete UTF-8 tail, LF/CRLF boundaries, cancellation/disk-full injection, and repeated appends after each case. A newly acknowledged record must be independently readable. Existing valid records must survive.

**Related preservation issue:** `RewriteAsync` decodes with `StreamReader` and writes new lines, so its byte-for-byte preservation comment is stronger than the implementation for invalid UTF-8, different newline encodings, or a missing final delimiter. Either preserve raw untouched spans or narrow the documented guarantee explicitly.

### EC-06 — Semantic corruption poisons an otherwise healthy chat file

**Evidence:** `src/DictateAnywhere.App/History/ChatHistoryRecord.cs:18-23`; `src/DictateAnywhere.App/History/LocalChatHistoryStore.cs:38,54,86,94-101`; `src/DictateAnywhere.App/History/VersionedJsonLinesFile.cs:231-262`.

The JSONL component checks deserialization and envelope version, but not record validity. A version-1 chat record with `messages: [null]` can deserialize. `Normalize()` dereferences each message without rejecting null entries. Every chat save scans and normalizes existing records before committing the replacement file.

**Trigger:** One such record is present among otherwise healthy history. Recent reads can fail when it is included; any later save, rename, or delete that scans it can fail, even for an unrelated conversation. The temporary-file mechanism protects the original file, but the user cannot persist new chats through that store.

**Fix:** Validate/normalize persisted records at the per-record boundary. Preserve malformed entries as opaque data, expose a bounded corruption notice, and continue operating on valid entries. Do not simply catch all rewrite exceptions, since genuine write failures must still reach the caller. Do not generate a fresh identity on every read for a persisted record with a missing ID; reject it or assign a stable repair identity through an explicit repair path.

**Regression gate:** A valid chat before and after a null-message record; malformed/missing required values; missing conversation IDs; malformed records outside the recent-read window. Reads, saves, rename, and delete of valid records must succeed while malformed data remains recoverable.

### EC-07 — Clipboard timeout does not terminate the operation

**Evidence:** `src/DictateAnywhere.Insertion/WindowsClipboardController.cs:193-245`; `src/DictateAnywhere.Insertion/WindowsTextInsertionService.cs:620-628`.

Each operation starts a background STA thread. A timeout stops waiting and returns an error, but the callback keeps running. If `SetUnicodeText` completes later, `clipboardMutated` in the caller was never set because the call threw, so that attempt has no normal restoration path. Later insertion/recovery operations can already have started.

**Trigger:** A delayed clipboard owner/COM call outlives the five-second timeout, then completes after fallback or after the user has copied something else. The abandoned operation can overwrite the newer clipboard contents. Repeated blocked operations also accumulate outstanding STA threads.

**Fix:** Give clipboard work one bounded owner and retain observation of timed-out work. Treat a timed-out mutation as potentially committed, not definitely failed. Prevent later operations from racing unresolved mutations. If a hard native-operation deadline is required, isolate the operation in a terminable process; a timeout on a managed wait does not stop native work. Choose this boundary before implementation rather than adding more independent threads.

**Regression gate:** Hold a controllable clipboard adapter past timeout, let the caller recover, then release it. Assert no untracked late mutation or overlapping transaction; all outstanding work must be observed. Native delayed-rendering behavior requires a controlled Windows integration case.

**Correction to the preliminary investigation:** No guaranteed process crash from `ManualResetEventSlim.Set()` after disposal is claimed. The available runtime allowed that call. The defensible defect is the unowned late operation.

### EC-08 — Clipboard restore destroys concurrent or richer content

**Evidence:** `src/DictateAnywhere.Insertion/WindowsTextInsertionService.cs:647-652,1033-1044`; `src/DictateAnywhere.Insertion/WindowsClipboardController.cs:110-140`.

Restoration always writes the old snapshot, without checking whether the clipboard still contains this insertion's temporary value. Separately, snapshot capture accepts Unicode text before checking for other formats, so an HTML/RTF/custom-format clipboard item with a text representation is flattened to plain text.

**Triggers:** (1) The user copies B while insertion is verifying; cleanup restores A and destroys B. (2) Copy formatted content from a rich editor, perform a verified dictation with restoration enabled, then paste the original copied content elsewhere: formatting/custom data is gone.

**Fix:** Associate temporary clipboard ownership with a sequence/version and conditionally restore only while that ownership remains current, with the comparison and restore protected against a new clipboard writer. Inspect all relevant formats before choosing a text-only snapshot. Preserve supported formats or choose typing before mutation when a faithful snapshot is unsupported. Coordinate recovery-copy behavior with this policy.

**Regression gate:** A third-party write between dispatch and restoration must survive, including a new write containing identical text. Mixed Unicode+HTML/RTF/custom data must remain intact or trigger a pre-mutation fallback. Cover cancellation during verification too.

### EC-09 — Undo is tied to an app, not the inserted edit

**Evidence:** `src/DictateAnywhere.Insertion/WindowsTextInsertionService.cs:201-257,1000-1012,1054-1058`.

The undo record remembers process ID/name, time, and method. The check permits a generic Ctrl+Z in the same process for two minutes. It does not identify the document/editor or establish that dictation remains the most recent undoable action.

**Trigger:** Dictate, type an unrelated sentence, then use the dictation undo hotkey: the later edit is undone. Or switch to another document/window served by the same process and undo there.

**Fix:** Bind undo eligibility to the actual target and verified post-insertion state. Invalidate eligibility on intervening edits or target changes. If the provider cannot prove the edit to undo, return a safe unavailable result; do not present an arbitrary editor Ctrl+Z as removal of the last dictation. Coordinate target identity with EC-02.

**Regression gate:** Same-process different window/editor, intervening typing/paste/undo, unavailable target identity, changed document content, and immediate unchanged-target undo. Assert no shortcut for any ineligible case.

### EC-10 — Error recovery abandons chunk tasks

**Evidence:** `src/DictateAnywhere.Core/Services/ChunkedTranscriptionSession.cs:17-22,60,89-109`; `src/DictateAnywhere.Core/Services/DictationPipelineCoordinator.cs:326-333,598-605,742-764,826-829`.

Chunk tasks use the runtime token, not a session-owned cancellation source. `Dispose()` only marks the session disposed and unsubscribes from capture. It neither cancels queued/running tasks nor drains their results. If capture finalization throws before `CompleteAsync`, error recovery disposes the session and resets to idle while those tasks remain active under the still-running runtime.

**Trigger:** Queue slow transcription, then cause microphone failure or a final-flush exception. Immediately start another dictation. Work from the failed session can continue consuming the same model resources and contend with the new session; failures from abandoned tasks lack normal session observation.

**Fix:** Give each dictation session its own linked cancellation source and an awaited stop/drain lifecycle. Close chunk admission under the same synchronization used by enqueue. On failure, cancel and observe all accepted tasks before dependent resources are reused/disposed; define bounded isolation for a provider that ignores cancellation. Make late captured callbacks harmless rather than throwing into the audio callback.

**Regression gate:** Block a chunk, fail final flush, initiate error recovery, then request another session. Verify cancellation, observation, and resource ordering. Include a provider that ignores cancellation until released and a callback already in flight during disposal.

### EC-11 — Successful upload receipt is lost to checkpoint failure

**Evidence:** `src/DictateAnywhere.App/Workbench/Publishing/YouTubePublishingCoordinator.cs:124-147,165-175,232-235`.

After `UploadWithRetryAsync` returns a valid video ID, the `finally` block drains checkpoints **before** applying that ID to the episode. If a queued save failed, drain throws and skips the receipt assignment. The generic catch marks the episode failed. A failed checkpoint also faults the queue's predecessor chain, preventing subsequent queued saves from running.

**Trigger:** Report an upload session URI, make that checkpoint write fail, then let the publisher return success. The coordinator can persist “Failed” without the returned video ID even though the remote video exists. Later recovery can lose track of the completed upload; whether an actual duplicate is created depends on remote-session recovery behavior and is not asserted as reproduced.

**Fix:** Treat the remote completion receipt as a separate irreversible boundary. Retain the returned video ID before checkpoint-drain errors can mask it. Persist a completion/reconciliation record independently of a failed progress checkpoint, and distinguish “upload failed” from “uploaded, local receipt persistence failed.” Do not automatically start a fresh upload while completion is unresolved. Define recovery for cancellation immediately after remote success as well.

**Regression gate:** Fake publisher returns an ID while session-checkpoint persistence fails; completion-save failure; cancellation immediately after success; restart from the last persisted state. Assert the returned ID remains available and recovery does not blindly re-upload. No live uploads are needed for these tests.

### EC-12 — Streaming does not bound recording memory

**Evidence:** `src/DictateAnywhere.Audio/WasapiAudioCaptureService.cs:21,183,236-237`; `src/DictateAnywhere.Core/Services/ChunkedTranscriptionSession.cs:20,60,82`.

Audio is written to `sessionBuffer` for the entire recording even when chunking is enabled. `StopAndFlushChunkAsync` still creates and normalizes a copy of that entire recording through `StopCaptureCoreAsync`, although its caller only uses the remainder. Transcription has a concurrency limit of one but no bound on queued chunk tasks or their pending audio.

**Trigger:** Leave toggle recording active for a long time, especially while inference is slower than capture. Raw 16 kHz mono PCM16 alone grows by 115,200,000 bytes per hour, before `MemoryStream` capacity, pending chunks, and finalization/trim copies. Finalization can cause a memory spike precisely when the user stops to recover their work.

**Fix:** Separate full-recording capture requirements from streaming final-flush requirements. Avoid retaining/copying the full PCM stream when the consumer only needs chunks. Bound queued work by audio duration/bytes and provide an explicit backpressure/stop/spill policy that never silently drops speech. Release completed chunk audio promptly. Preserve any genuine full-recording consumers.

**Regression gate:** A long synthetic capture with a deliberately slow provider must stay within a declared memory/backlog budget; stop/final-flush must not allocate another full-session copy; the combined transcript must cover all accepted audio or explicitly report a controlled limit.

## Implementation sequence

Do not bundle these into a broad rewrite. Start each packet with a deterministic regression that fails for the stated trigger, then make the smallest cohesive fix.

1. **Audio arithmetic (EC-01):** Widen all PCM amplitude calculations and prove capture/trim/flush behavior at numeric extremes.
2. **Insertion authorization and delivery (EC-02, EC-03, EC-04):** Define target identity and delivery certainty once, enforce policy before helper routing, and prove no wrong-target or duplicate fallback dispatch. These protections precede undo changes.
3. **History framing and validation (EC-05, EC-06):** Repair append framing under the existing lease and isolate semantic corruption during reads/mutations. Preserve evidence bytes and retain honest disk-error reporting.
4. **Clipboard transaction ownership (EC-07, EC-08):** Decide the STA lifetime boundary, resolve late completion, protect newer clipboard writes, and handle multiple formats faithfully. Integrate with packet 2's dispatch-certainty rules.
5. **Undo eligibility (EC-09):** Reuse packet 2's target identity and add intervening-edit invalidation. Fail safely where certainty is unavailable.
6. **Dictation lifetime and memory (EC-10, EC-12):** Establish session cancellation/drain first, then implement bounded chunk buffering/full-recording separation. Validate slow and cancellation-ignoring providers.
7. **Publishing commit/recovery (EC-11):** Separate the remote receipt from progress persistence and test failed local journaling after remote success. This is independent of the earlier packets and must precede reliance on resumable publishing.

**Completion criteria for every packet:** The new regression demonstrates the original failure and passes after the fix; existing affected tests pass; no raw user text/audio is added to diagnostics; state and cleanup have one owner; relevant canonical documentation is updated. Run the full Release solution gate at implementation boundaries as required by the repository. Native focus/clipboard cases need controlled Windows integration evidence after unit tests; keep live publishing out of automated validation.

## Additional leads requiring a separate decision or deeper reproduction

- **Settings shape/version parsing:** `JsonSettingsStore.cs:203-224` assumes an object root and treats non-Int32 schema numbers as missing/legacy. Review `null`, arrays, and out-of-range schema values with startup/autosave recovery. Require preservation of the original bytes rather than silently migrating an unrecognized schema. Do not weaken existing future-schema protection by broadly swallowing parse exceptions.
- **History reachability:** `HistoryQueryCoordinator` reads 200 records and Workbench reads 100 before filtering. Confirm whether users are promised full-history search; if so, implement filtering/paging at the storage boundary. Unlimited retention currently does not imply older records are reachable through these queries.
- **Worker framing limits:** `PersistentPythonWorkerClient` bounds accumulated stderr only after `ReadLineAsync` returns. A worker emitting a huge line without a newline can allocate beyond that bound. Define maximum protocol/log line sizes and test malformed worker output before choosing a fix.
- **Document import cancellation/size:** Non-PDF extraction uses synchronous whole-document reads inside `Task.Run`; cancellation does not interrupt an already-running extraction. Review decompressed DOCX/EPUB size budgets and close-during-import behavior with bounded synthetic fixtures.

One tempting cache finding was deliberately excluded: Reader playback speed is applied to the media player, so its absence from the speech-synthesis cache key alone does not prove a narration-cache defect.

## Validation performed during this review

| Existing test scope | Result |
| --- | --- |
| Insertion project, Release | 52 passed |
| Audio project, Release | 23 passed |
| Settings project, Release | 21 passed |
| App filter: LocalHistoryStoreTests, YouTubePublishingTests, ReaderNarrationSessionTests, WorkbenchChatSendControllerTests | 28 passed |
| Total existing tests above | 124 passed, 0 failed, 0 skipped |

The App-filter final result used `--no-build --no-restore` after the preceding build invocation. No new regression tests were written, and the full solution suite was not run for this planning-only review.

An attempted probe against the compiled .NET assemblies could not start the bundled PowerShell executable because Windows returned Access Denied, including after an escalation attempt. Therefore no source-level reproduction is claimed from that probe. The arithmetic API check and the correction concerning the clipboard completion event are recorded explicitly above. No actual microphone, clipboard, target-app editing, user-history mutation, model download, or remote upload was performed.

## Implementation and regression evidence — 2026-09-14

The user subsequently authorized implementation. All twelve findings and the four
additional leads now have code changes and regression coverage. Existing unrelated
Workbench/accessibility changes were preserved. No live publishing was performed.

| Finding | Implemented behavior | Regression evidence |
| --- | --- | --- |
| EC-01 | Widen PCM amplitudes before absolute value in transform, silence analysis, and capture. | Numeric-extreme PCM tests and capture/flush tests; overflow regressions were observed failing before the fix. |
| EC-02 | Carry process lifetime and focused-field identity through restoration, dispatch, and helper capture; check each typing batch. | Same-window field/password change, target change during clipboard acquisition, authorized-target rejection, and focus change during long typing. |
| EC-03 | Automatic fallback requires proven absence of dispatch; unchanged or unverifiable target text cannot trigger replay. | Unchanged-value paste and partial-dispatch failure both send no typing fallback. |
| EC-04 | Enforce application blacklist and secure-field policy before elevation. | Blacklisted elevated target never invokes the helper or writes a recovery clipboard copy. |
| EC-05 | Repair a missing LF boundary under the history lease before append. | Valid, truncated, and future-version unterminated tails retain readable subsequent records. |
| EC-06 | Validate persisted identity/messages/text before normalization; preserve undecodable/corrupt byte spans on rewrite. | Null chat messages, missing identity, and invalid UTF-8 survive unrelated save/rename/delete without poisoning valid records. |
| EC-07 | One outstanding STA call owns its completion; abandonment is allowed only before the native write commits. | Timed-out callback cannot later commit; overlapping operations are rejected; no discarded faulted completion tasks. |
| EC-08 | Compare clipboard sequence ownership under the native clipboard lock; preserve rich/custom data through typing fallback. | New user copy wins over restoration/recovery; rich data is not replaced even when typing fails. |
| EC-09 | Refuse generic external Ctrl+Z because the OS provides no dictation edit-transaction identity. | Undo sends no shortcut after verified insertion, intervening text edits, or editor/app changes. Native application Undo remains available to the user. |
| EC-10 | Cancel and await the session's single worker on every finalization/disposal path. | Failed final flush with a cancellation-ignoring provider cannot return idle or insert stale text until that provider finishes. |
| EC-11 | Retain remote receipt before checkpoint drainage; save it independently of cancellation; preserve newer in-memory recovery over a stale retry request. | Checkpoint failure, receipt-save failure, stale-request retry, controller recovery, cancellation after success, late progress, and uncertain upload without a session. No known completed episode is uploaded twice. |
| EC-12 | Streaming capture does not retain the full recording; audio queue and completed transcript have explicit limits. | Long synthetic capture keeps the session buffer empty; slow-provider overload fails without inserting incomplete text. |
| Settings lead | Unrecognized roots/schema declarations are preserved and read-only; historical negative integer version compatibility remains. | Arrays/null, out-of-range and nonnumeric versions, duplicate declarations, future-schema and legacy migration coverage. |
| History lead | Storage filters across the entire file before applying the matching-result limit, with a bounded retained payload budget. | Matches older than 220 subsequent entries are reachable through store and standalone query paths. |
| Worker lead | Bounded stdout framing retains read-ahead; stderr reads fixed chunks without waiting for a newline. | CRLF/EOF/read-ahead, oversized lines, buffered cancellation, and a real Python fixture that floods stdout without a newline and is terminated before restart. |
| Import lead | Shared expanded-input budget, checked file size, cancellation during reading/between stages, prohibited XML DTDs, and bounded HTML/RTF regex work. | Small compressed input cannot bypass the shared expansion limit, repeated entries consume the budget again, canceled extraction stops subsequent reads, and XML entity expansion is rejected. |

The canonical capability matrix records the exact limits and conservative behavior.
The public API baseline was explicitly regenerated and reviewed: 1,976 signatures,
216 public type signatures, no new packages/project edges/friendships, and three
new framework references. All clipboard consumers/fakes and the App/helper protocol
were migrated together; the API guard remains enabled.

### Acceptance and practical limits

- Release solution build: zero warnings and zero errors.
- Final solution acceptance: `dotnet test DictateAnywhere.sln -c Release --nologo
  --no-build --no-restore -m:1` completed successfully across all twelve suites:
  **1,209 passed, zero failed, five existing opt-in integration tests skipped**
  (three real-model tests and two video-export tests). Evidence is recorded in
  `artifacts/edge-case-acceptance-serial.log` (ignored local evidence).
- The simultaneous no-build run hit three timeout failures in existing UI/process
  tests that had passed earlier. Sequential execution passed with the same
  assertions and timeouts; no test was weakened to hide those timing failures.
- `git diff --check`: passed. Initial sandbox-only process/local-state test
  failures were rerun outside the sandbox; the inference and App suites passed.
- Native microphone, external editor/browser focus, real clipboard delayed
  rendering, and installed signed UIAccess-helper integration still require
  controlled manual Windows validation. Automated tests use deterministic fakes
  and isolated worker fixtures; they do not establish universal third-party UIA
  reliability. Win32 input dispatch cannot be atomic with another app's focus.
- A provider that never cooperates with cancellation can delay shutdown; session
  resources stay owned rather than being reused unsafely. Abrupt process loss
  combined with unavailable local storage can require manual YouTube reconciliation.
