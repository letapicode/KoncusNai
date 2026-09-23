# Notype codebase hardening backlog

## Mandate

This is the canonical, ordered implementation queue. When asked to "work on it," take the first unblocked incomplete item, make the smallest reviewable change that satisfies it, run the proportionate gates, record evidence, and continue in order. Do not substitute broad rewrites, speculative abstractions, or line-count-only refactors for the stated outcome.

An item is complete only when every done criterion is met. Adding a wrapper, partial class, base window, service locator, generic repository, or new project without removing duplicated ownership is not completion.

## Fixed product decisions

These decisions are approved and are not implementation questions:

1. Dictation and chat history are saved locally by default and cannot be disabled in Settings.
2. History has no application-level encryption, password lock, password-based transfer, or automatic retention limit. The operating-system account and file permissions are the privacy boundary.
3. Stored history is unlimited: Notype does not automatically delete old entries. Unlimited storage does not mean loading every entry into memory; queries must remain paged or bounded.
4. Manage Dictation History remains. Users can search, select, edit, save, and delete dictation entries.
5. The standalone window is named **Dictation History**. It shows the selected entry's date/time, editor, Save, and Delete actions without repeating "Dictation" or duplicating the selected title in the detail pane.
6. First-party windows share the same theme, continuous background, title-bar treatment, spacing tokens, and window controls. Reuse will be implemented with small composition primitives and styles, not a deep base-window hierarchy.
7. A whole-application Rust rewrite is out of scope unless measurements prove managed orchestration is a material bottleneck. Native or lower-level inference runtimes may be evaluated independently when they support the selected model and demonstrably improve latency or memory without unacceptable accuracy loss.
8. Compatibility with the retired encrypted-history format is out of scope. Notype neither reads nor deletes those old files.

## Engineering quality gate

Every task must satisfy all of these rules:

- Preserve one clear owner for state, lifetime, cancellation, persistence, and errors.
- Prefer deletion and direct code over speculative layers. An abstraction needs at least two real consumers or a documented boundary/lifetime reason.
- Keep changes cohesive and reviewable; establish characterization tests before behavior-preserving extraction.
- Do not copy UI shells, provider catalogs, settings aliases, or failure policy into another location.
- Do not introduce silent fallbacks, unbounded in-memory collections, unobserved tasks, orphan processes, or destructive migrations.
- Make performance claims only from repeatable cold/warm measurements with machine, model, and runtime identity.
- Update tests and canonical documentation in the same change that changes behavior.
- Run `dotnet test DictateAnywhere.sln --configuration Release --no-restore` at each task boundary unless the task documents a narrower interim gate; run packaging/real-device gates when the affected boundary requires them.

## Severity

| Severity | Release policy |
| --- | --- |
| **S0 — Stop ship** | Must be closed before a production release. |
| **S1 — Critical** | Complete before substantial new feature development. |
| **S2 — High** | Complete before calling the codebase production-hardened. |
| **S3 — Medium** | Complete in focused quality milestones. |
| **S4 — Low** | Opportunistic cleanup after higher-risk work. |

## Execution map

```mermaid
flowchart LR
  Safe[S0 protect settings data] --> History[S1 simplify local history]
  History --> Shell[S1 unify window shell]
  Shell --> Measure[S1 measure dictation latency and memory]
  Measure --> Own[S1 composition and workflow ownership]
  Own --> Harden[S2 providers, workers, caches, reliability]
  Harden --> Prove[S2 security, accessibility, release evidence]
  Prove --> Polish[S3/S4 documentation and residue]
```

## S0 — Stop ship

### DATA-001 — Prevent future-schema settings overwrite

- **Status:** Complete (2026-09-01).
- **Problem:** `JsonSettingsStore` maps a settings schema newer than this app to defaults. Opening Settings can then auto-save those defaults over the newer file.
- **Implementation:**
  1. Return a typed unsupported-version result or a dedicated exception carrying found/current versions.
  2. Keep the original file byte-for-byte untouched.
  3. Disable all automatic save paths for the unsupported load and present a clear downgrade/newer-version message.
  4. Offer only explicit backup/reset recovery; never reset automatically.
- **Done when:** A future-version fixture is unchanged after startup, opening/closing Settings, and shutdown; all three paths are automated.
- **Dependencies:** None.

## S1 — Critical product simplification and architecture

### HISTORY-001 — Introduce always-on local history

- **Status:** Complete (2026-09-01).
- **Problem:** Dictation/chat stores combined encryption, retention, and record persistence even though the current product requires direct, unlimited local history.
- **Implementation:**
  1. Define `LocalDictationHistoryStore` and `LocalChatHistoryStore` over a shared, versioned JSONL record-file component only where the mechanics are genuinely identical.
  2. Preserve serialized per-file access, atomic replacement for mutations, corruption isolation, cancellation, and disk-full/error reporting.
  3. Remove retention deletion. Append and mutation operations must not materialize an unbounded history file unnecessarily.
  4. Add bounded/paged query contracts so UI callers never load unlimited history into memory. Preserve newest-first search behavior and stable entry IDs.
  5. Delete encrypted-history readers, password decryption, startup recovery UI, and migration tests. Do not delete old user files from disk.
  6. Add partial-write, corrupt-line, concurrent access, edit, delete, and large-history paging tests.
- **Done when:** New records are local plaintext JSONL, no encrypted-history compatibility code runs or ships, no automatic age/count deletion occurs, and a large fixture is queried with bounded memory.
- **Dependencies:** DATA-001.

### HISTORY-002 — Remove encryption, password, retention, and enablement surfaces end to end

- **Status:** Complete (2026-09-01).
- **Scope:** `AppSettings`, settings DTO/migrations, Settings UI, runtime creation/restart, tray commands, Workbench, standalone History, diagnostics export, product docs, and tests.
- **Implementation:**
  1. Remove current-model fields `EnableEncryptedHistory`, `EncryptedHistoryRetentionLimit`, `EnableHistoryPasswordLock`, `HistoryPasswordSalt`, and `HistoryPasswordVerifier`.
  2. Remove old history field names from current models and delete history-specific compatibility readers.
  3. Delete `HistoryPasswordSession`, `HistoryPasswordProtector`, encrypted store implementations, disabled-history recorder/branches, password UI/actions, retention input, history enable toggle, and any tray toggle.
  4. Keep one Settings action: **Manage Dictation History**. History-saving behavior is no longer a setting and no runtime restart is required for it.
  5. Replace "encrypted/protected history" diagnostics and documentation with accurate local-history privacy language. Continue excluding raw history from diagnostic bundles by default.
  6. Remove tests that assert retired switches/cryptography and replace them with always-on local-history behavior tests.
- **Done when:** No history encryption/password compatibility code ships; dictation/chat records are always saved locally; Settings exposes no history password, transfer-password, retention, or enable controls; Release tests pass.
- **Dependencies:** HISTORY-001.

### UI-SHELL-001 — Establish small reusable first-party window shell primitives

- **Status:** Closed by operator direction (2026-09-14). Reusable primitives, the inventory-derived 11-window/51-state/12-case matrix, and unit/STA coverage are complete. The narrower dated WP-25 observations remain direct PASS evidence where recorded. The operator explicitly declined the remaining WP-09 shell matrix and tray-alternative execution; those cases are **SKIPPED / NOT TESTED**, not PASS, and this administrative closure does not claim complete manual shell conformance. Explicit Dark/Light selection remains canonical; System-theme tracking is not supported.
- **Problem:** Workbench, Settings, Reading Studio, YouTube publishing, Help/About, and other specialized windows still implement related chrome inconsistently. Dictation History's former native white title bar is fixed, but the wider shell inventory and adoption work remains.
- **Implementation:**
  1. Inventory each first-party window's actual chrome needs: standard, tool, toast, or specialized editor.
  2. Add a narrow theme/window behavior that applies native DWM theme at handle creation and whenever application theme changes.
  3. Centralize shared shell brushes, continuous-surface background, title typography, caption-button styles/commands, drag region, double-click maximize, resize border, and accessibility names in resource/style primitives.
  4. Provide a small compositional standard frame for ordinary windows with title, optional leading content, optional actions, and body content.
  5. Let Workbench and Reading Studio keep specialized layouts while consuming the same tokens/behavior. Do not introduce a `BaseWindow` with workflow hooks or force all windows through one giant control.
  6. Add focused tests for theme application, command wiring, and required accessibility metadata; visually verify dark/light, maximized, DPI, and keyboard states.
- **Done when:** Ordinary windows no longer copy title-bar markup/handlers; all first-party windows use the same theme and shell tokens; native white title bars are absent; specialized windows remain independently maintainable.
- **Dependencies:** None. Implement before UI-HISTORY-001 and use incrementally elsewhere.

### UI-HISTORY-001 — Simplify and theme Dictation History

- **Status:** Closed by operator direction (2026-09-14). Implementation and WP-10-AUTO-01 are complete, covering 8 owners, 21 state-aware workflow states, 15 focused tests, isolated scratch storage, and 9 operator cases. Save/dirty-state and refresh/draft-retention corrections remain covered. The operator explicitly declined the remaining History workflow and environment matrix; those cases are **SKIPPED / NOT TESTED**, not PASS, and no destructive real-History operation was performed.
- **Implementation:**
  1. Set window/title-bar text to **Dictation History** and render it through the standard continuous shell.
  2. Keep the left search field and dictation-entry list. Use a compact date/time-aware list title generated from transcript content.
  3. Keep the right transcript editor plus Save and Delete.
  4. Show only the selected entry's localized date/time as detail metadata. Remove the large `Dictation` label, redundant selected title, and repeated source/type labels.
  5. Preserve selection and caret sensibly across save/refresh; provide clear empty, loading, error, no-results, and deletion states.
  6. Add keyboard shortcuts/access keys where appropriate, automation names, correct focus order, 200% text/DPI behavior, and dark/light visual verification.
- **Done when:** The History window visually reads as one Notype surface; it contains no redundant type/title text; search, select, edit, save, delete, and date/time behavior pass focused tests and manual visual checks.
- **Dependencies:** HISTORY-002, UI-SHELL-001.

### PERF-TRANSCRIBE-001 — Establish the dictation latency and worker-memory baseline

- **Status:** Complete (WP-11, 2026-09-05). Current-head Release evidence covers the transcript-free controlled 3/7/11-second Cohere model path and a controlled application stop-to-visible matrix. The application matrix retained 39 attempts, accepted five cold and five warm samples per length by application-reported captured duration, and rejected nine mistimed captures without relabelling them. All 39 visible insertions were directly operator-confirmed as exact fixture matches; accepted results have unique operation IDs, finite numeric named stages, `ClipboardPaste`/`VerifiedInserted`, one leaf worker, owned-process memory sampling, and no owned orphan after graceful tray exits. Controlled lifecycle evidence covers loaded idle, cancellation, worker reset, and post-cancellation cleanup. The CPU-only runtime makes GPU utilization/VRAM residency not applicable; adapter capacity was not substituted. The 7- and 11-second controlled model-path peak working sets exceed the 10 GiB budget, providing a measured WP-12 target rather than an optimization claim.
- **Problem:** The code records some Cohere timings, but there is no end-to-end, comparable answer for perceived delay or Python memory.
- **Inspection clue, not a benchmark:** On the 2026-09-01 audit host, the running Notype process reported about 125 MiB private memory. Its Cohere worker chain contained a tiny Python launcher and one real child with about 15.2 GiB private committed memory but about 10 MiB resident working set at that idle instant. This is consistent with a single model/framework process whose pages were heavily trimmed; it is not proof of two loaded models. Reproduce under controlled cold/warm load before acting.
- **Implementation:**
  1. Measure hotkey/capture start, capture finalization, PCM-to-WAV preparation, worker/model cold start, model inference, response normalization, text transformation, target restoration/insertion, and total stop-to-visible-text latency.
  2. Record cold and warm runs for short, medium, and long clips with provider/model/runtime version, CPU, RAM, GPU/VRAM, device mode, audio duration, and accuracy fixture identity.
  3. Sample parent and child process working set/private bytes/commit, CPU, GPU/VRAM where available, and worker count during startup, idle, warm inference, and post-cancellation.
  4. Confirm by test/trace that only the selected ASR model starts and that runtime restart cannot leave duplicate workers or model copies.
  5. Commit a reproducible benchmark command, raw result schema, baseline report, and explicit latency/memory budgets. Keep user audio/text out of committed evidence.
- **Done when:** We can attribute p50/p95 stop-to-text latency and peak/steady memory to named stages, distinguish cold model load from warm inference, and reproduce the result on the supported baseline machine.
- **Dependencies:** None. This is measurement, not optimization.

### PROD-001 — Resolve profiles and experience-mode product contradictions

- **Status:** Complete (2026-09-01). Profiles, voice snippets, app-profile rules, and experience-mode selection are absent from the current settings/runtime contract; historical JSON values are ignored at the migration boundary.
- **Problem:** First run and documentation expose profiles while `MinimalSettingsPolicy` normalizes profiles, snippets, application rules, and experience mode away.
- **Implementation:** Produce a caller/UI/runtime matrix. Present the small set of observable user capabilities for keep/retire approval. Once decided, either make each exposed choice work end to end or remove its UI/current fields while keeping legacy migration only.
- **Done when:** Every exposed choice changes supported behavior and first run, Settings, runtime, installer, and product inventory agree.
- **Dependencies:** None. Do not guess whether a user-facing workflow should survive.

### ARCH-001 — Establish one production composition root

- **Status:** Complete (2026-09-01). `ApplicationComposition` is the sole process composition root and owns explicit Workbench, Reader, Settings, first-run, History, runtime/readiness, productivity, window, and tray factories. `AppServiceFactory` and hidden production construction from WPF surfaces are removed. Architecture guardrails prevent UI surfaces from reaching construction helpers.
- **Problem:** Windows and helpers call static service factories, obscuring construction and lifetimes.
- **Implementation:** Define one immutable application composition object with explicit process/window/session/operation lifetimes; construct it in `App`; inject feature factories/contracts; remove production service construction from WPF windows and helpers; guard the boundary using compiled/project inspection where practical.
- **Done when:** Implementation construction occurs only under the composition root, each disposable has one documented owner, and UI/controller tests can use fakes without hidden production services.
- **Dependencies:** HISTORY-002 and PROD-001 shape the remaining services.

### LIFE-001 — Extract process lifecycle from `App.xaml.cs`

- **Status:** Complete (2026-09-01). `ApplicationHost` owns runtime/readiness replacement, deferred settings, startup failures, and deterministic disposal; `WindowCoordinator` owns Workbench/Settings/History windows and refresh subscriptions; `TrayCommandCoordinator` owns the tray surface, timer, subscriptions, and serialized command boundary. Focused tests cover runtime deferral/recovery/disposal and tray serialization/failure/disposal.
- **Implementation:** Extract `ApplicationHost` for startup/shutdown/failures, `WindowCoordinator` for window ownership, and `TrayCommandCoordinator` for serialized tray actions. Keep WPF overrides as thin event adapters.
- **Done when:** `App.xaml.cs` contains bootstrap/event forwarding, host behavior is testable without WPF windows, and shutdown deterministically awaits runtime, windows, warmups, and owned processes.
- **Dependencies:** ARCH-001.

### UI-001 — Decompose Workbench by workflow ownership

- **Status:** Complete (2026-09-02). Workbench workflow state, operation/cancellation lifetime, persistence, resources, and recovery policies have one authoritative owner as recorded in `planning/workbench-wp03-ownership-inventory.md`. `WorkbenchPresentationReducer` is the single enablement/visibility projection; cohesive views own only WPF presentation and narrow intent forwarding; `ApplicationComposition` remains the construction root. Root XAML is 336 lines and code-behind is 1,723 lines. WP-04 found no remaining business rule or independently testable workflow in the window, so the above-normal code-behind size is an explicit audit-backed exception rather than a reason for partial/base/helper file churn.
- **Implementation order:** Characterize behavior; define cohesive state/intent contracts; extract dictation, chat, history, import/OCR, and quick-settings controllers one at a time; split matching XAML controls only after state ownership moves. Reuse the existing history query/command mechanics until HISTORY-001 replaces their store boundary.
- **WP-01 evidence (2026-09-02):** `WorkbenchSettingsApplicationController` now owns normalized/current/pending settings, latest-update replacement, hotkey-registration preference, the `SettingsApply` lease, quick-settings query cancellation, chat/read-aloud reset, dictation reconfiguration, and concise hotkey/failure outcomes. `TextboxWorkbenchWindow` applies theme resources and renders the typed result. Non-WPF tests cover immediate application, recording/chat deferral, latest-pending replacement, cancellation, fallback, and registration failure. UI-001 remains in progress pending WP-02 through WP-04.
- **WP-02 evidence (2026-09-02):** `WorkbenchHistoryInteractionController` now owns dictation/chat selection identities, loaded-dictation composer provenance, edit/rename/delete effects, selected-record cache synchronization, chat loading/rename mark-saved actions, and refresh reconciliation. `TextboxWorkbenchWindow` only obtains dialogs/confirmation, forwards confirmed input, and renders typed directives. Non-WPF tests cover daily loading, loaded/unrelated/manual-edit deletion behavior, cache handling, rename cancellation/success, chat load and unsaved/persisted rename, refresh reconciliation, and concise daily labels. UI-001 remains in progress pending WP-03 and WP-04; no manual UI, locale, or accessibility evidence is claimed.
- **WP-03 evidence (2026-09-02):** `WorkbenchPresentationReducer` derives immutable `WorkbenchPresentationState` from dictation, operation, chat, history-selection, read-aloud, and view-local presentation snapshots; conflicting busy/readiness states are normalized before controls render. `TextboxWorkbenchWindow` dispatches this state and remains the composition, dialog, event-routing, theme, and top-level disposal boundary. `WorkbenchQuickSettingsView` owns text-size debounce/commit and rendering suppression; `WorkbenchOperationalStatusView` owns transient-outcome timing; `WorkbenchComposerView` owns hotkey text and deferred expansion affordance rendering; `WorkbenchSidebarView` owns sidebar visibility and selection rendering; and history interaction owns the dictation session identity. Focused non-WPF reducer and STA view tests cover busy precedence, invalid readiness, timer disposal, text-size disposal, prompt event suppression, and sidebar tooltip/accessibility behavior. The New chat and Settings redundant sidebar tooltips are absent while their automation names remain. UI-001 remains in progress pending WP-04; no manual UI, locale, DPI, accessibility, or hardware evidence is claimed.
- **WP-04 evidence (2026-09-02):** The final audit mapped every Workbench state, operation, disposable, persistence call, cancellation/recovery policy, presentation decision, WPF responsibility, composition edge, and important test boundary. Obsolete rendering wrappers and consecutive duplicate renders were removed; transcript rendering no longer calculates sidebar action state outside the reducer. Composer/progress and operational-status timer/deferred-callback shutdown is deterministic, the read-aloud dispatcher handoff is abortable, and raw playback exception detail is diagnostic-only. Focused Workbench/guardrail/markup tests passed 146/146; the full Release solution gate passed 822 with 2 explicit hardware/model skips and 0 failures. No manual UI, accessibility, locale, DPI, microphone, hotkey, model, or hardware evidence is claimed.
- **Done when:** The window renders state and forwards intent; workflow tests do not access controls; duplicated busy/cancellation flags are gone; no controller is a renamed copy of the window; code-behind is normally below 500 cohesive lines or has an explicit audit-backed exception showing only WPF responsibilities remain.
- **Dependencies:** ARCH-001. Coordinate with HISTORY-001 rather than duplicating history work.

### UI-002 — Decompose Reading Studio by workflow ownership

- **Status:** Complete (2026-09-03). `ReaderPreparationController` owns immutable current-section/full-range preparation commands, the shared preparation lease, generation/stale-result policy, narration/cache/prefetch choice, timing/media-open sequencing, estimated progress, cancellation, and safe outcomes. `ReaderExportController` and `ReaderPublishingController` own typed export/publishing commands, shared leases, artifact/job/checkpoint lifetime, cancellation, and safe failure policy. `ReaderDocumentSession` owns draft transactions, `ReaderPlaybackSession` owns range/prepared-playback state, narration/preview/prefetch sessions retain their resource/cache ownership, and `ReaderOperationSession` remains the single arbitration boundary shared with import/export/publishing. Primary playback is behind a narrow WPF media adapter. `ReaderPresentationReducer` is the single enablement/visibility/status/progress path; cohesive sidebar, document/editor, and transport views own their WPF rendering and emit typed intent.
- **Implementation order:** Characterize preparation/playback/edit/export behavior; define `ReaderState`; extract preparation, playback plus WPF media adapter, draft transactions, and export/publish preparation; then split sidebar/page/transport views.
- **WP-05 evidence (2026-09-02):** `ReaderPreparationController` exposes typed immutable requests, phases, safe statuses, directives, and results without WPF dependencies. Focused non-WPF tests cover uncached/cached/exact-prefetch/mismatched-prefetch section work, ordered and mixed-cache ranges, first-section activation, narration/timing/media failures, narration/timing/media/range cancellation, stale invalidation, shared-operation conflicts, retry, next-section prefetch, and deterministic disposal with no late state mutation. `WpfReaderPlaybackMedia` is the only primary `MediaPlayer` owner and cleans up pending open handlers/cancellation. `ApplicationComposition` builds the per-window session graph; `ReaderWindow` creates the dispatcher-bound media adapter, forwards preparation intent, and renders typed outcomes. The focused controller suite passes 18/18, the Reader regression slice passes 140/140, and the full Release solution passes 840 with 2 explicit hardware/model skips and 0 failures. UI-002 remains in progress pending WP-06 through WP-08; no manual playback, voice, GPU, model, accessibility, locale, DPI, or hardware evidence is claimed.
- **WP-06 evidence (2026-09-02):** `ReaderExportController` now owns immutable audio/video commands, `AudioExport`/`VideoExport` leases, ordered export preparation, typed progress/results, cancellation/failure policy, and same-directory temporary artifacts that preserve existing destinations until a successful atomic commit. The WPF renderer is isolated behind `IReaderExportEngine`; command tests construct no window or control. `ReaderPublishingController` owns source fingerprints, exact recovery compatibility, publishing workspace/job construction, `YouTubePublish` lease lifetime, episode preparation/rendering, cancellation, and safe outcomes. `YouTubePublishingCoordinator` serializes and awaits upload-session checkpoints, reuses the newest session on bounded transient retry, skips completed work, and persists a safe resumable error instead of raw exception text. `ApplicationComposition` constructs the production graph; `ReaderWindow` now captures immutable intent, presents dialogs/modals, and renders typed state/results. The focused WP-06 fault suite passes 16/16, the Reader/export/publishing regression slice passes 66/66, and the full Release solution passes 852 with 2 existing hardware/model skips and 0 failures. UI-002 remains in progress pending WP-07 and WP-08. No manual playback, encoding, upload, GPU, model, accessibility, locale, DPI, or hardware evidence is claimed.
- **WP-07 evidence (2026-09-03):** `ReaderPresentationReducer` derives one immutable presentation state from document, playback, operation, preparation, export, publishing, preview, draft, and view-local snapshots, with the active `ReaderOperationKind` defining conflicting-state precedence. `ReaderSidebarView` owns range/language/voice/appearance/export selections, programmatic-selection suppression, accessibility metadata, and the WPF preview player; `ReaderDocumentView` owns draft/page/focused-reading rendering, typography/direction/highlighting, typed progress rendering, and disposable render/debounce timers; `ReaderTransportView` owns playback controls, timeline rendering, and seek suppression. The views emit typed intent and contain no workflow controllers or operation sessions. `ReaderWindow` is reduced from 2,166 to 830 lines and retains only cross-view/controller routing, document import and dialogs, top-level playback/media coordination, completion presentation, chrome, and deterministic disposal. This exceeds the normal 500-line target because document-import extraction and playback-controller redesign are explicitly outside WP-07; moving those remaining cohesive top-level responsibilities into forwarding wrappers would create false ownership. Reading Studio now defaults to Midnight when no selection exists, while `ApplyThemeSelection` preserves an explicit existing theme. Focused pure-reducer, STA-view, initialization, accessibility/guardrail, and Reader regression tests cover state precedence, typed selection/seek intent, suppression, default/preserved theme, draft disposal, and UI-independent boundaries. The Reader regression slice passes 160/160, the complete app test project passes 538/538, and the full Release solution passes 863 with 2 existing hardware/model skips and 0 failures. UI-002 remains in progress pending WP-08. No manual playback, voice, publishing, accessibility, keyboard-only, locale, DPI, GPU, model, or hardware evidence is claimed.
- **WP-08 evidence (2026-09-03):** The final ownership matrix is canonical in `docs/architecture/system-architecture.md`. The audit corrected unobserved import/draft-preview shutdown work, voice-preview completion racing speech disposal, queued dispatcher callbacks reaching disposed views, and a reproduced draft/appearance bug that exposed a reading surface beside the editor. `ReaderWindow` now names and observes the remaining WPF tasks, cancels every command owner before awaiting shutdown, treats import cancellation as cancellation, and rejects late results/callbacks. The existing reducer now selects the exclusive draft/page/focused surface; the document view only renders it. New pure/STA tests prove surface precedence, appearance rerender exclusivity, active OCR-import cancellation without document replacement, preview-before-speech disposal, and queued callback rejection; guardrails cover named task/dependency ownership. Shared live/video tokenization, timing-map, sentence, direction, typography, grapheme, layout, and highlight-plan invariants remain green. The Reader filter passes 166/166, the app project passes 544/544, and the full Release solution passes 869 with 2 existing hardware/model skips and 0 failures. The 921-line code-behind and 433-line XAML retain an audit-backed exception for top-level routing, dialogs/import, playback-media commands, completion/chrome/focus presentation, and deterministic shutdown; forwarding-only extraction would worsen ownership. Midnight is the no-selection default and explicit supplied themes are preserved; there is no persisted Reader theme setting. No manual playback, voice, video, publishing, screen-reader, keyboard-only, high-contrast, locale, DPI, GPU, model, or hardware evidence is claimed.
- **Done when:** One state owner controls enablement/status; window close cancels/disposes every operation; code-behind is normally below 500 cohesive lines; live playback and export share timing/highlight policy.
- **Dependencies:** ARCH-001.

### SET-001 — Separate current settings from legacy migration formats

- **Status:** Complete (2026-09-01; current schema advanced to 19 on 2026-09-17). The current schema reads/writes through `CurrentSettingsDocument`; earlier schemas are isolated in `Migrations/LegacySettingsReader` and convert once into the current contract. `AppSettings` contains no active-model alias, profiles, snippets, app rules, or experience mode. Future schemas remain non-destructive, golden upgrade coverage passes, and the oldest supported versioned input is documented as schema 1 (with best-effort pre-version import).
- **Implementation:** Define a minimal current `AppSettings`; define a correctly named current DTO; move every historical schema reader/DTO into `Migrations`; convert once at the boundary; remove legacy aliases such as `ActiveModelId` from runtime code; keep golden fixtures and document the oldest supported upgrade.
- **Done when:** Runtime code never branches on historical schemas, current serialization contains no retired product fields, supported fixtures migrate/round-trip, and future schemas are non-destructive.
- **Dependencies:** DATA-001, HISTORY-002, PROD-001.

### PROVIDER-001 — Create one canonical provider registration model

- **Status:** Complete (2026-09-01). `LocalTranscriptionProviderRegistry` is the single registration point for metadata, presentation/license text, capabilities/languages, selectable model descriptors, construction, and model-manager acquisition. Settings, language compatibility, runtime construction, readiness, diagnostics identity, and model management consume the registration, with consistency tests for operational metadata, descriptions, license notices, and manageable advertised models.
- **Implementation:** Define one transcription provider descriptor covering stable ID, privacy/operation metadata, capabilities/languages, model descriptors, acquisition/readiness, construction, and lifetime. Derive Settings, model management, selection, readiness, and diagnostics from it. Apply to chat/TTS only when the same real duplication exists.
- **Done when:** Adding or removing a transcription provider requires one registration plus implementation/tests, and consistency tests prove every selectable model is manageable and constructible.
- **Dependencies:** ARCH-001, PROD-001.

### CONCUR-001 — Standardize async operation boundaries

- **Status:** Complete (2026-09-02). Genuine UI/native callbacks are the only `async void` boundaries. Hotkey message/release loops, undo and productivity actions, Workbench read-aloud, Settings autosave, Reader narration prefetch, CrisperWhisper warm-up, WASAPI stopped-capture disposal, overlay auto-hide, and late timed-out overlay work now have named task owners plus cancellation and awaited or explicitly bounded shutdown. Settings/publishing deferred loads are awaited at their event boundaries. Coalescing refresh and history disposal pumps expose and retain their completion tasks. A repository guard rejects common discarded task-producing calls, and focused cancellation/overlap/failure/shutdown tests cover the changed owners.
- **Implementation:** Keep `async void` only at genuine events; route work through a typed operation runner; name and own background warmup/prefetch tasks; document cancellation/arbitration for overlapping operations; add shutdown/race tests and a guard for unapproved discarded tasks.
- **Done when:** Every background operation is observed, named, canceled/disposed where safe, and has one arbitration owner.
- **Dependencies:** LIFE-001, UI-001, UI-002.

## S2 — High-priority hardening

### PERF-TRANSCRIBE-002 — Optimize the measured transcription bottleneck

- **Status:** Complete (WP-12, 2026-09-05; no production change). A fresh Release comparison used five independently process-cold and twenty reused-worker warm samples for each 3/7/11-second fixture length. All 75 baseline requests passed the fixed accuracy criterion with one leaf worker and no owned orphan. Worker/model startup dominated cold latency; model inference dominated warm latency; WAV preparation and .NET orchestration remained negligible. Exploratory 4/6/8-thread CPU screens and a load-only four-thread cap did not satisfy the repeatable 20% rule without a warm or cold regression. Explicit inference-only execution was already guaranteed by the installed Transformers `generate()` implementation. No supported dependency-free dtype/quantization path was demonstrated, so no speculative product toggle, host-specific thread rule, runtime dependency, or production change was introduced. Sanitized evidence is in `docs/release/transcription-optimization-evidence-2026-09-05.json`.
- **Implementation:** Rank changes by measured impact. First verify selected-provider lazy startup, persistent worker reuse, no duplicate model instances, cancellation cleanup, and minimal audio-file/protocol overhead. Then benchmark supported model/runtime options such as quantization, ONNX Runtime, CTranslate2, or another provider-specific native runtime only if compatible. Do not reintroduce the retired command-line Whisper provider.
- **Decision rule:** Do not rewrite the WPF/orchestration layer in Rust. A runtime change must show a repeatable material gain (target at least 20% in the constrained metric), stay within an agreed accuracy regression threshold, preserve failure/cancellation semantics, and reduce rather than multiply deployment complexity.
- **Done when:** The chosen change meets its budget on cold/warm runs with accuracy and resource evidence, or the baseline proves inference/model choice—not app orchestration—is the remaining limit and that conclusion is recorded.
- **Dependencies:** PERF-TRANSCRIBE-001, PROVIDER-001 where provider metadata changes.

### REMOVE-001 — Retire unreachable production capabilities

- **Status:** Complete (WP-13, 2026-09-05). The retired command-line Whisper provider remains removed. The current caller/owner/build/distribution audit in `planning/executable-ownership-and-distribution-audit.md` found no remaining unreachable executable: App and UIAccess are production roles; ModelBenchmark, TtsCli, Spikes, and VoicePreviewGenerator have current engineering owners/callers and are excluded from installer publishing. No speculative deletion was made.
- **Disposition:** Retain every current executable. WP-28 owns relocating retained developer utilities, separating production/developer solution build intent, and making package roles structurally explicit; it must preserve the callers and assembly/protocol contracts recorded by WP-13.
- **Implementation:** Prove each candidate has no supported caller or release role; remove production code, packaging, scripts, tests, and claims in one coherent series. Move a genuinely useful probe under `tools` only with an owner, caller, and current-provider support.
- **Done when:** No selectable/runtime/package path references retired providers, normal builds cannot package developer tools, and clean build/installer/upgrade gates pass.
- **Dependencies:** PROD-001 and PROVIDER-001. Audit before deletion.

### UI-003 — Replace SettingsPanel mutation flags with one editing model

- **Status:** Complete (WP-14, WP-15, WP-16). SettingsDraft and SettingsValidationResult model all settings domains. SettingsOperationController owns save revisions, race-safe async refreshes, and atomic import recovery. SettingsPanel is decomposed into a cohesive 405-line view host delegating speech presentation to SettingsSpeechSectionPresenter.
- **Implementation:** Introduce `SettingsDraft`, validation result, and focused model/device/import-export controllers. One save coordinator owns revision, autosave, and unsupported-schema state. History configuration is absent; Manage Dictation History is a simple navigation action.
- **Done when:** Controls bind to one draft/state, concurrent refresh/save/import is deterministic, behavior tests avoid WPF, and code-behind is normally below 400–500 cohesive lines.
- **Dependencies:** SET-001, ARCH-001.

### CORE-001 — Reduce and organize the Core public surface

- **Status:** Complete (WP-17). Public Core types inventoried across all solution consumers. Core public surface reduced from 94 to 80 canonical domain contracts. Provider registration metadata relocated to `DictateAnywhere.App.Runtime`, single-consumer `LocalGreetingResponder` moved to `DictateAnywhere.App.Workbench`, `ChatHistoryRecord` and `IDictationHistoryStore` moved to `DictateAnywhere.App.History`, and `DictationSessionStateMachine` internalized. Enforced via automated `CorePublicSurfaceTests` whitelist guardrail.
- **Implementation:** Inventory every public type by consumer; group contracts by domain; internalize single-assembly details; move provider metadata to its owner; document permitted dependencies.
- **Done when:** Every public contract has two boundary consumers or a documented stability reason and API review shows no accidental surface.

### INFRA-001 — Organize inference by capability and worker infrastructure

- **Status:** Complete (WP-18). Reorganized `DictateAnywhere.Inference` into discoverable capability subdirectories (`Transcription/`, `Chat/`, `TextToSpeech/`, `Alignment/`, `Ocr/`, `Workers/`). Retained concrete provider policies within respective services and centralized common process/protocol mechanics in `PersistentPythonWorkerClient` and `IPersistentWorkerClient`. Added comprehensive `WorkerProtocolContractTests` verifying request serialization (`lower_snake_case`), response deserialization, error wrapping, empty payload detection, and case-insensitivity. Verified with clean solution Release test pass (923 passed, 2 skipped, 0 failed).
- Create discoverable capability namespaces/folders (`Transcription`, `Chat`, `TextToSpeech`, `Alignment`, `Ocr`, `Workers`); share process/protocol mechanics without forcing unrelated provider policy into a generic framework; split projects only for demonstrated dependency/deployment/lifetime reasons.
- **Done when:** Dependencies are acyclic, provider types are discoverable, and worker behavior has one contract-test suite.
- **Dependencies:** PROVIDER-001.

### CACHE-001 — Version and prove cache identity

- **Status:** Complete (WP-19). Established canonical cache inventory and typed identity matrix in `planning/cache-inventory-and-key-matrix.md`. Updated concrete cache owners without artificial frameworks: `ReadingAudioCache` and `ReaderNarrationSession` now validate audio existence/integrity and recover from corrupt/deleted files; `ReaderVoicePreviewCache` versions and hashes sample text into file identity with atomic temp writes and corruption recovery; `ReaderWindow.xaml.cs` preserves valid section audio across range adjustments (`clearNarrationCache: false`); `HuggingFaceSnapshotModelManager` detects 0-byte corrupt snapshots. Added 26 tests across `CacheIdentityAndRecoveryTests`, `ReadingAudioCacheTests`, `ReaderVoicePreviewCacheTests`, and `HuggingFaceSnapshotModelManagerTests` proving content-affecting changes miss and appearance-only changes safely reuse prepared work. Full Release test gate passing (949 passed, 2 skipped, 0 failed).
- Inventory narration, timing, preview, OCR/model, and prepared-export caches; define typed keys containing every output-affecting input; document eviction/corruption recovery.
- **Done when:** Parameterized tests prove output-affecting changes miss and appearance-only changes safely reuse prepared work.
- **Dependencies:** PROVIDER-001, UI-002.

### REL-001 — Complete worker/process lifecycle fault coverage

- **Status:** Complete (WP-21; Review 4 corrections applied). Produced the canonical process inventory, lifecycle state model, and fault matrix in `planning/process-lifecycle-and-fault-matrix.md`. Review 4 added reset-before-retry for malformed responses from still-live Python workers, bounded Python cleanup waits, retirement of an unhealthy owned llama.cpp server before replacement, bounded llama.cpp/UIAccess cleanup, and standard-input transport for UIAccess text so sensitive payloads are absent from process arguments. Pre-existing third-party processes (external Ollama daemons, system llama-server instances, browsers) remain neither killed nor adopted.
- Contract-test Python workers, local servers, OCR/alignment, ffmpeg, UIAccess, and publishing for startup timeout, missing dependency, malformed protocol, stderr flood, crash, cancellation, restart, orphan cleanup, and shutdown.
- **Done when:** Each owned process has an explicit state model; fault injection passes; pre-existing third-party processes are never killed as owned.
- **Dependencies:** INFRA-001, CONCUR-001.

### OBS-001 — Standardize operation diagnostics

- **Status:** Complete (WP-20; Review 4 correction applied). The standardized correlated event contract, bounded/redacted diagnostics, and primary-workflow instrumentation remain authoritative. Review 4 removed the settings presentation status detail channel that could expose raw exception messages; technical details remain in `IDiagnostics`, while UI status stays short and actionable.
- Standardize operation IDs, stage names, durations, provider/model/runtime identity, typed outcomes, retry/fallback, and redaction; map user errors to remediation codes.
- **Done when:** One diagnostic bundle reconstructs a failed primary workflow without raw user text/audio and correlation tests cover nesting/cancellation.

### SEC-001 — Close privacy, secret, and supply-chain evidence

- Update the threat model to state that history is local plaintext protected by OS account/filesystem access, not application encryption. Keep raw history out of logs/bundles. Inventory and pin NuGet/Python/model/runtime/binary/font provenance, hashes, licenses, credentials, and CI actions.
- **Status:** Complete (WP-22 and WP-23, 2026-09-06; win-x64 publish-lock coverage added by WP-31). Privacy boundaries retain local plaintext history under Windows account/filesystem protection, exclude raw history/audio from bundles, recursively scrub credential-shaped fields, and keep UIAccess text on redirected standard input. WP-23 adds one machine-readable provenance owner; 29 normal locked NuGet project graphs plus 13 explicit win-x64 production publish graphs covering the same 47 resolved package/version pairs; three exact hash-checked Python environments covering 221 package/version pairs; immutable model revisions and GGUF/Ollama digests; SHA-256 verified llama.cpp/FFmpeg and managed transcription snapshots; signed-publisher policy for Ollama/Python installers; tracked asset/font/voice-manifest integrity; and commit-pinned CI actions. Compliance fails closed on missing, malformed, mutable, duplicate, unsafe-path, unlicensed, unowned, zero-entry, or hash-mismatched evidence. The optional Windows ROCm installer path is explicitly blocked until an exact compatible wheel is reviewed; it cannot silently install from a mutable index.
- **Done when:** Machine-readable compliance fails on missing provenance/integrity/license entries and the documented privacy boundary matches actual storage/network behavior.
- **Dependencies:** HISTORY-002 [Done], WP-20 [Done], REMOVE-001.

### ACCESS-001 — Execute the primary-workflow accessibility matrix

- Test Workbench, Settings, Dictation History, Reader, Publishing, first run, and tray alternatives for keyboard-only use, screen-reader names, focus order, high contrast, 200% text, DPI, reduced motion, narrow windows, RTL/mixed text, and error recovery.
- **Status:** Closed by operator direction (2026-09-14) with an explicit manual-evidence waiver. WP-24 automation and the state-aware preflight are complete. WP-25 source-inspection audit (2026-09-14) confirmed all custom XAML button/slider/toggle/list styles have correct keyboard focus visuals (either `AppKeyboardFocusVisualStyle` via direct setter or `BasedOn` inheritance, or inline `IsKeyboardFocused` template triggers). Three new automated regression tests added: `KeyboardFocusVisual_AllCustomButtonStyles_HaveFocusRingDeclared`, `TabNavigation_AllPrimaryWindowInteractiveControls_AreFocusableAndTabStops`, and `RTLAndReducedMotion_CodePaths_ExistInSource`. Evidence document committed at `docs/quality/accessibility-manual-evidence.md` with 15 structured manual test cases (ACC-KB-01 through ACC-TRY-01); keyboard, HC, RM, NW, and RTL cases are NOT TESTED pending operator execution; screen-reader and 200% DPI cases reserved for live-session availability. No code defects found in WP-25. The full Release gate passes 13/13 accessibility tests and the 1,028+ Release gate with 2 skips, 0 failures.
- **Done when:** Automated semantic checks plus dated manual evidence exist and no S0/S1 accessibility defects remain.
- **Dependencies:** Complete major XAML changes first.

### TEST-001 — Rebalance tests toward behavior and risk

- **Status:** Complete (2026-09-05). WP-26 defines executable deterministic, Windows/WPF, process-integration, model-integration, hardware, and manual-evidence categories; adds discovery-checked focused commands; converts Cohere and FFmpeg opt-in false passes to explicit prerequisite skips; separates composition, Workbench, and Reader guardrail failure ownership; separates About-window runtime and XAML-content failures; bounds all App STA thread joins; and makes fault/soak reliability filters reject zero discovery. WP-27 serializes solution discovery, publishes deterministic Cobertura/TRX/JSON evidence in CI, enforces a measured 62.5% line / 53.0% branch aggregate floor plus non-disappearing file-level migration, current-policy, redaction, and snapshot-recovery floors, and requires four isolated controlled faults to be killed. The current taxonomy discovers 1,041 tests (967 deterministic); the measured deterministic baseline is 63.45% line / 54.13% branch, the bounded fault run killed 4/4 with no survivors, exclusions, timeouts, equivalent cases, or invalid executions, and the full Release gate passed 1,036 with five explicit prerequisite skips and zero failures.
- Split giant WPF tests; replace source-string shape assertions with compiled/project checks where practical; add controller/state tests; label unit/integration/model/hardware/manual suites; collect coverage; apply mutation/fault testing to migration/state/policy code.
- **Done when:** Failures identify one behavior, fast deterministic tests run locally, CI publishes coverage, release-only suites are explicit, and critical policies meet an agreed mutation threshold.

### PKG-001 — Separate production, operational, and experimental executables

- **Status:** Complete (WP-28 plus Review 5, 2026-09-05). ModelBenchmark, TtsCli, and Spikes moved from `src` to `tools` without changing assembly names, namespaces, protocols, command syntax, or package versions. The full solution remains the complete validation boundary; checked-in production and developer-tool solution filters make narrower build roles executable. Project-reference validation covers both source roots, and installer staging refreshes only the exact validated leaf. Review 5 corrected the cleanup guard so an existing reparse point anywhere in the staging ancestor chain is rejected before recursive deletion, not only when the final leaf is a reparse point. Payload validation still rejects missing/ambiguous App/UIAccess executables, developer-tool residue, unexpected product executables, and payload reparse points, including `-SkipPublish` reuse. Both solution-filter restore/build gates, every executable build, project/packaging/upgrade guardrails, and the full Release suite pass; the suite has 1,039 passes and five explicit prerequisite skips.
- Classify App, UIAccess helper, TtsCli, ModelBenchmark, Spikes, and VoicePreviewGenerator; make installer inputs explicit; put retained developer tools under `tools`; clarify normal build intent with solution filters/solutions.
- **Done when:** Every executable has an owner/caller/distribution status and production packaging cannot include tools accidentally.
- **Dependencies:** REMOVE-001.

## S3 — Medium-priority quality work

### BUILD-001 — Centralize dependency and test-project configuration

- **Status:** Complete (WP-29, 2026-09-06; lock policy completed by WP-23 and RID publish coverage added by WP-31). `Directory.Packages.props` is the sole owner of nine unchanged direct package versions, while `tests/Directory.Build.props` owns the identical test contract for all 12 test projects. WP-29 proved zero migration differences across 319 non-empty direct/transitive project-framework entries. WP-23 subsequently checked in all 29 complete normal NuGet lock files and made CI restore use locked mode; WP-31 adds 13 separately selected win-x64 publish locks with the same resolved versions. Central transitive pinning remains disabled. Missing locks, content hashes, projects, or resolved provenance now fail compliance.
- Add central NuGet version management and shared test targets/properties; document lock/restore policy.
- **Done when:** Versions are declared once and a clean checkout restores/builds/tests reproducibly.

### DOC-001 — Consolidate canonical documentation

- **Status:** Complete (WP-30, 2026-09-06). `docs/documentation-map.md` assigns one owner to current product, architecture, development, testing, security, packaging, release, backlog, ledger, and manual-evidence facts. README, the capability matrix, system architecture, developer guide, release checklist, backlog, and ledger now route to those owners. Seven superseded plans are retained and explicitly labelled historical because they contain unique decision rationale. A deterministic policy and validator check the canonical set, sections, links, anchors, required paths/claims, manual PASS/FAIL evidence structure, and narrowly scoped retired claims with file/line diagnostics; CI and the quality suite run the gate.
- Keep the root README, product inventory, system architecture, developer guide, release checklist, and this backlog consistent; archive/delete superseded plans after extracting valid decisions; fail docs checks on retired provider/product claims.
- **Done when:** A new engineer or AI agent can identify scope, boundaries, invariants, build commands, and current priorities from README plus system map.

### SIZE-001 — Define source, asset, installer, and runtime budgets

- **Status:** Complete for the September 6 checkpoint; voice-preview decision superseded on 2026-09-22. `docs/release/size-budget-policy.json` owns Git-blob, asset, Release-publish, installer, installed-payload, and external-footprint rules. The 129-preview pack was present at that checkpoint; the current source candidate removes all 129 files and their manifest, sets bundled preview count to zero, and generates previews on demand in bounded per-user storage. Two locked Release/win-x64 WiX 5.0.2 runs produced identical 326,260,009-byte validated payloads and 112,861,561-byte MSIs; Burn containers differed by 1,988 bytes and the variation is retained. Current clean-candidate size validation remains pending.
- Historical: measure tracked size, 129-preview voice pack, installer variants, model/runtime downloads, and installed footprint. The owner selected on-demand previews; the September 22 source audit records that change.
- **Done when:** Packaging checks budgets and intentional exceptions.

### RELEASE-001 — Make release evidence reproducible

- Separate generated results from durable templates; attach commit/environment identity; keep machine-specific reports ignored.
- **Done when:** Another engineer can reproduce a release candidate and trace all evidence to one commit.

### API-001 — Guard public API and dependencies

- **Status:** Complete (WP-33, 2026-09-06). A deterministic compiled baseline covers 1,943 signatures (214 types) across 11 reusable production libraries and records the role, consumers, friendships, direct package identities, declared project references, and compiled references for all 17 source/tool assemblies. The validator rejects empty/malformed/missing evidence, public signature drift, inventory loss, dependency cycles, production-to-tool edges, and undeclared compiled repository references. Candidate generation is explicit and restricted to new ignored artifact paths. The audit also corrected VoicePreviewGenerator's previously implicit transitive Core dependency to a direct declared edge without changing package or runtime behavior.
- Generate API baselines for reusable assemblies and enforce project graph/forbidden dependencies without relying only on source text.
- **Done when:** Accidental API or dependency expansion fails CI with an actionable message.

## S4 — Low-priority cleanup

### CLEAN-001 — Remove proven unused residue

- **Status:** In progress. The unused `AppServiceFactory.SharedHttpClient` was removed with the service locator, eleven zero-caller `ModuleMarker` types were deleted, and redundant `.gitkeep` files were removed from every non-empty source/test/docs/installer directory. The intentionally empty `installer/msix` placeholder remains. Generated-report audit remains.
- Audit and remove `AppServiceFactory.SharedHttpClient`, unreferenced `ModuleMarker` types, redundant `.gitkeep` files, and superseded generated reports.
- **Done when:** Each deletion has a no-caller proof and clean build/test/package passes.

### NAME-001 — Align current names without breaking compatibility

- Rename the current settings DTO accurately; use Notype in product text; retain `DictateAnywhere.*` identifiers until an explicit compatibility migration exists.

### STYLE-001 — Apply narrow consistency automation

- Add only agreed low-noise formatter/analyzer rules; fix ownership instead of multiplying suppressions.
- **Done when:** Formatting is deterministic and exceptions have local rationale.

## Removal candidates requiring confirmation or measured proof

These are not approved for automatic deletion merely because they look unused:

| Candidate | Current recommendation | Required proof/decision |
| --- | --- | --- |
| Retired Whisper stack | Remove | No runtime/package caller; replacement and upgrade gates pass. |
| `ModelBenchmark` | Retain under `tools` as the owned WP-11/WP-12 performance tool | `run-performance-regression.ps1` and current WP-11/WP-12 evidence depend on its schema and behavior. |
| Profiles/snippets/app rules/experience modes | Likely remove if intentionally minimal | Product approval after PROD-001 behavior matrix. |
| 129 bundled voice previews | Make on-demand if installer size matters | UX/offline requirement and measured size/startup impact. |
| UIAccess helper | Retain as the operational production helper | Dynamic bridge caller, installer publish/signing contract, process tests, and elevated-target playbook are current. |
| TtsCli/Spikes/VoicePreviewGenerator | Retain under `tools` as explicit engineering tools | Current guide/script/asset-generation callers are recorded in the executable audit; solution filters declare their build role and packaging validation excludes their output. |
| Duplicate old plans/reports | Archive or delete | Extract still-valid decisions first. |

## Milestone gates

### Gate A — Safe, simplified history

- DATA-001, HISTORY-001, HISTORY-002, UI-SHELL-001, and UI-HISTORY-001 complete.
- Release tests pass and no open S0 remains.

### Gate B — Measured and owned architecture

- PERF-TRANSCRIBE-001, ARCH-001, LIFE-001, UI-001, UI-002, SET-001, PROVIDER-001, and CONCUR-001 complete.
- Primary workflows have one state/lifetime owner; no static construction from UI; no open S1 except a documented product-decision blocker.

### Gate C — Production hardening

- Applicable S2 tasks have performance, security, recovery, accessibility, packaging, and real-device evidence.
- Rubric score is at least 85 with no score cap.

### Gate D — High-quality system

- S3 tasks are complete or explicitly accepted with owner/expiry.
- Rubric score is at least 93 and clean-machine release rehearsal reproduces the evidence.

## Execution evidence — 2026-09-01

- **DATA-001:** `UnsupportedSettingsSchemaException` prevents a newer schema from loading as defaults or being overwritten; byte-preservation and save-refusal tests pass.
- **HISTORY-001:** current dictation/chat records use serialized versioned local JSONL, bounded newest-first reads, streaming atomic rewrites, stable IDs, and corruption preservation. Retired encrypted-history readers, decryption, startup recovery UI, and tests were deleted; old files are not touched.
- **HISTORY-002:** current settings/runtime/UI/tray no longer contain history enablement, password, transfer-password, retention policy, or compatibility recovery. The pipeline requires a real local history recorder; the disabled recorder was deleted.
- **UI-HISTORY-001:** Dictation History uses the continuous themed surface, shared caption styles, localized date/time-only detail metadata, search/editor/save/delete, and no repeated type/title labels. Automated behavior and markup checks pass; manual visual evidence is still required.
- **UI-SHELL-001:** the first-party inventory is documented; ten ordinary/specialized windows consume `WindowThemeBehavior`; Settings, History, Workbench, About, Reading Studio, and Reading Studio help consume one tested `WindowCaptionButtons` component with centralized commands, state glyphs, tooltips, and accessible names. Reading Studio and Workbench also consume the shared maximized-work-area behavior. Five custom headers consume `WindowDragRegionBehavior`; the transparent completion toast is the documented exception. Manual visual evidence remains.
- **REMOVE-001:** the command-line Whisper provider, manager, manifests, binaries, installer payload, setup script, spikes, tests, and obsolete dual-installer/offline-model release gates were deleted. The WP-13 executable audit retains App/UIAccess as production roles and ModelBenchmark/TtsCli/Spikes/VoicePreviewGenerator as currently owned engineering tools; packaging smoke now rejects developer executable projects in the production publish boundary. Physical tool relocation and production/developer solution separation remain WP-28 work.
  - **Current note (2026-09-06):** The preceding sentence is preserved as dated 2026-09-01 evidence. WP-28 subsequently moved the retained developer executables under `tools`, added production/developer solution filters, and made the installer payload boundary fail closed.
- **PROD-001:** the caller/behavior matrix is recorded in `docs/architecture/product-capability-matrix.md`. First run no longer advertises profile selection; the current schema and `AppSettings` omit profiles, snippets, app rules, and experience mode; obsolete profile resolvers/catalogs and snippet transformation code are deleted. Runtime dictation consumes the global language and spoken-command setting directly.
- **UI-001/UI-002/CONCUR-001:** chat-model readiness/setup and chat-send transactions have tested owners; dictation commands and successful-history writes have tested owners; mixed audio/document imports now run as one typed, cancellable transaction that owns composer/history/chat mutations; Workbench history queries and mutations have one typed boundary; chat message mutation, read-aloud lifetime, and request-progress timing have explicit owners. The Workbench navigation/history sidebar, header, operational status, compact quick-settings flyout, chat transcript, request-progress animation, compact composer, and expanded-prompt overlay own their presentation and forward intent with focused STA/guardrail tests. Reading Studio uses one typed operation session for preparation, import, export, publishing, cancellation, and shutdown instead of independent cancellation sources and busy flags, and its native caption behavior is shared rather than duplicated. Hotkey release monitoring, undo execution, autosave, prefetch, warm-up, capture disposal, and overlay timeouts now have shutdown-aware task ownership and focused race tests.
- **SET-001:** `CurrentSettingsDocument` is the sole current-schema DTO and `Migrations/LegacySettingsReader` is the sole historical reader. The current runtime contract contains no `ActiveModelId`, profiles, snippets, app rules, or experience mode; supported old schemas still map current values and rewrite once.
- **PROVIDER-001:** provider capabilities, descriptions, model descriptors, runtime construction, usage/license notices, and model-manager acquisition originate in `LocalTranscriptionProviderRegistry`; the composition root no longer repeats Hugging Face manifests, and selected-provider fallback/Workbench construction no longer creates both transcription implementations.
- **Verification:** the latest 2026-09-02 Release solution run passes with zero failures: 789 automated tests pass and two explicit hardware/model smoke tests are skipped. This includes encrypted-history compatibility removal, shell/work-area primitives, concise hotkey-failure plus loop/release-monitor/undo/productivity shutdown race coverage, background-operation ownership guardrails, Settings autosave and Reader prefetch disposal, dictation timing instrumentation, composition/lifecycle ownership tests, typed Workbench history mutations, Workbench chat/dictation/file-import/model-setup command ownership, Workbench workflow/view extraction, Reader operation arbitration/shared captions, current-settings migration isolation, retired-profile deletion, and provider-registration consolidation. Packaging smoke and security compliance gates passed at the preceding task boundary. Installer upgrade compatibility must be rerun for the final release candidate.
