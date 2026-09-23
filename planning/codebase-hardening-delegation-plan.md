# Notype hardening delegation plan

## Purpose

This is the agent-execution companion to `planning/codebase-hardening-backlog.md`. The backlog remains the source of truth for scope, severity, and completion. This document breaks the 25 not-fully-closed backlog items into bounded work packets that can be delegated without replaying the original multi-million-token task history.

Use one new Codex task per packet unless a packet explicitly says it may be combined. Execute packets in dependency order. Never run two code-changing packets against the same checkout at the same time.

## Non-negotiable engineering contract

Every delegated agent must:

1. Read this document, the named backlog item, `README.md`, `docs/architecture/system-architecture.md`, and every directly affected production file before editing.
2. Start from a committed checkpoint and inspect `git status --short`. Preserve unrelated changes and stop if they overlap the packet.
3. Preserve one owner for state, lifetime, cancellation, persistence, and failure policy. Do not create a wrapper that merely renames window code.
4. Establish or confirm characterization tests before behavior-preserving extraction.
5. Prefer deletion and direct code. Add an abstraction only for two real consumers or a documented state/lifetime boundary.
6. Keep user-facing statuses short and actionable. Put exception detail in `IDiagnostics`; never place raw `Exception.Message` in a status bar.
7. Use `apply_patch` for edits. Do not perform broad formatting, unrelated cleanup, or destructive Git operations.
8. Run focused tests while iterating, then `dotnet test DictateAnywhere.sln --configuration Release --no-restore --nologo` at the packet boundary.
9. Run `git diff --check`, update the canonical backlog/evidence if the packet changes status, and make one cohesive commit.
10. Report changed ownership, tests, remaining risks, manual evidence, and the commit hash. Do not claim a manual or hardware gate passed without evidence.

## Model-selection policy

These recommendations use the capability descriptions exposed by the current Codex host. Exact account pricing and token multipliers are not assumed.

| Model | Use it for | Avoid using it for | Relative token strategy |
| --- | --- | --- | --- |
| **GPT-5.6 Sol, high** | Cross-cutting architecture decisions, performance diagnosis, public API boundaries, final integration review | Mechanical file moves, repetitive tests, documentation cleanup | Use sparingly where a wrong decision would create expensive rework. |
| **GPT-5.6 Terra, high** | Primary implementation model for stateful refactors, WPF workflow extraction, worker lifecycle, Settings redesign | Simple deletions or rote configuration edits | Best default for difficult code packets with a precise plan. |
| **GPT-5.6 Luna, medium/high** | Bounded implementation, audits, test additions, packaging, accessibility automation, evidence collection | Ambiguous ownership redesign or performance conclusions | Preferred cost-efficient model once architecture and done criteria are fixed. Start at medium; use high only for multi-file reasoning. |
| **GPT-5.5, high** | Fallback for difficult work if a 5.6 model is unavailable; independent review of a risky patch | First choice when Sol/Terra are available | Proven but usually not the most efficient default for this plan. |
| **GPT-5.4, high** | Fallback for straightforward established-pattern work | New architectural boundaries and complex concurrency | Use only when 5.6 access is constrained. |
| **GPT-5.4-mini, medium/high** | Mechanical cleanup, central package files, documentation claim checks, deterministic guardrails | WPF ownership extraction, process lifecycle, performance optimization | Lowest-cost lane for narrowly specified, easily verified packets. |

Escalation rule: begin with the recommended model. If the agent cannot explain the current owner, intended owner, preserved invariants, and test strategy before editing, stop that task and rerun it with Terra high or Sol high. Do not spend repeated small-model attempts on an architectural packet.

## Task and checkout strategy

- Prefer a **new Codex task for every packet**. The repository plan supplies the context; old conversation history is unnecessary.
- Start each task from the commit produced by the preceding packet. For this repository, sequential local-checkout work is simpler than parallel worktrees.
- Parallel work is safe only for read-only audits or evidence packets explicitly marked parallel-safe. Integrate their conclusions before starting dependent code.
- Keep the current long task as the program-management/audit task. Use new tasks for implementation, then return here only for integration review or plan updates.
- Do not ask a new task to “finish the backlog.” Give it exactly one packet ID.

## Dependency sequence

```text
Checkpoint
  -> WP-01..04 Workbench
  -> WP-05..08 Reading Studio
  -> WP-09..11 manual S1 evidence
  -> WP-12 measured performance decision
  -> WP-13 removal audit
  -> WP-14..16 Settings
  -> WP-17..21 contracts/inference/observability/reliability
  -> WP-22..28 security/accessibility/tests/packaging
  -> WP-29..36 build/docs/size/release/API/cleanup/naming/style
```

`WP-11` may be executed alongside `WP-01..08` only as a read-only/manual measurement run from a fixed commit. `WP-09`, `WP-10`, and `WP-25` must wait until XAML changes are complete. `WP-12` cannot start before `WP-11` has valid baseline evidence.

## S1 work packets

### WP-01 — Workbench settings-application owner

- **Backlog:** UI-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** checkpoint commit
- **Primary files:** `TextboxWorkbenchWindow.xaml.cs`, `WorkbenchOperationSession.cs`, `WorkbenchQuickSettingsController.cs`, `WorkbenchDependencies.cs`, `ApplicationComposition.cs`
- **Objective:** Move settings normalization, deferred-apply state, operation arbitration, runtime reset, dictation reconfiguration, and hotkey outcome policy into one UI-independent controller.
- **Implementation:**
  1. Characterize immediate apply, defer-during-recording, defer-during-chat, replacement by the latest pending settings, shutdown cancellation, hotkey failure, and fallback binding.
  2. Introduce one controller/state contract owning current settings, the latest pending settings, registration preference, and the `SettingsApply` lease.
  3. Move `quickSettingsController.CancelLatestQueryAndWaitAsync`, chat/read-aloud runtime reset, and dictation configuration into that transaction.
  4. Return typed progress/result state; keep theme resource application and control rendering in the window.
  5. Remove `pendingSettings`, `shouldRegisterWorkbenchHotkey`, duplicated deferred-apply methods, and raw settings exceptions from the window.
- **Done:** The window forwards settings intent and renders typed state; no settings transaction state is duplicated; focused non-WPF tests cover races and failure recovery.
- **Prompt:**
  > Implement WP-01 from `planning/codebase-hardening-delegation-plan.md`. Follow the engineering contract exactly. Do not work on any other packet. Run focused tests, the full Release solution test, `git diff --check`, update UI-001 evidence without marking it complete, and commit the cohesive change.

### WP-02 — Workbench history interaction state

- **Backlog:** UI-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-01
- **Primary files:** `TextboxWorkbenchWindow.xaml.cs`, `WorkbenchHistoryController.cs`, `WorkbenchSidebarView.xaml.cs`, history command/query types
- **Objective:** Give selection, edit/rename/delete results, cache updates, and chat-history loading one explicit interaction owner while leaving dialog presentation in WPF.
- **Implementation:**
  1. Characterize dictation group loading, current-record editing, rename/delete cancellation, multi-delete, chat loading, unsaved chat rename, persisted chat rename, and selection preservation.
  2. Define a typed history interaction state/result. The controller may accept confirmed intent, but must not create WPF dialogs.
  3. Own `selectedHistoryRecord`, `LastDictationSessionCache` updates, chat mutation/mark-saved actions, and post-mutation refresh directives.
  4. Keep localized date formatting, confirmation dialogs, focus, and view rendering in WPF.
  5. Remove mutation/state branching from the window and strengthen compiled behavior tests.
- **Done:** Persistence remains in `WorkbenchHistoryController`; interaction state has one owner; window handlers are thin confirm/forward/render adapters.
- **Prompt:**
  > Implement WP-02 from `planning/codebase-hardening-delegation-plan.md` starting from the latest committed checkpoint. Preserve all history behavior and avoid a generic repository or WPF-aware controller. Run the required gates, update UI-001 evidence, and commit only this packet.

### WP-03 — Workbench presentation-state reducer and remaining views

- **Backlog:** UI-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-02
- **Primary files:** `TextboxWorkbenchWindow.xaml(.cs)`, existing `Workbench*View` controls
- **Objective:** Replace scattered enablement/status calculations with one immutable presentation state and move cohesive remaining view behavior into existing or narrowly justified controls.
- **Implementation:**
  1. Inventory every remaining window field and method by owner; do not split by line count.
  2. Define one derived `WorkbenchPresentationState` from controller states for action enablement, operational-status visibility, composer/chat state, and selection metadata.
  3. Move quick-settings text-size interaction/debounce, prompt expansion synchronization, sidebar selection mechanics, and transcript/export presentation only where the relevant view genuinely owns them.
  4. Keep the window as composition/event routing plus top-level rendering.
  5. Delete obsolete helper methods and update guardrails to enforce direction without brittle line-string assertions where a compiled test is possible.
- **Done:** No duplicated busy flags; view controls own presentation; controller tests avoid controls; window code-behind is cohesive and materially closer to the below-500 guideline.
- **Prompt:**
  > Implement WP-03 exactly as specified in `planning/codebase-hardening-delegation-plan.md`. Begin with an ownership inventory and characterization tests. Do not create partial classes or a base window to satisfy line count. Run all gates and commit this packet.

### WP-04 — Close UI-001

- **Backlog:** UI-001
- **Model:** GPT-5.6 Sol, high
- **Depends on:** WP-03
- **Objective:** Perform an architectural review rather than another rewrite.
- **Implementation:**
  1. Map every Workbench state, operation, disposable, persistence call, and error policy to exactly one owner.
  2. Identify and fix only concrete remaining violations.
  3. Verify workflow tests do not reach controls and UI only forwards intent/renders state.
  4. Record final code-behind/XAML size as evidence, not as the goal.
- **Done:** Every UI-001 criterion is demonstrably met; backlog may be marked complete only with test evidence.
- **Prompt:**
  > Audit and close WP-04 from `planning/codebase-hardening-delegation-plan.md`. Treat this as a senior architecture review. Make only evidence-backed fixes, run the full Release gate, update UI-001 truthfully, and commit.

### WP-05 — Reading preparation command/state owner

- **Backlog:** UI-002
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-04
- **Primary files:** `ReaderWindow.xaml.cs`, `ReaderDocumentSession.cs`, `ReaderPlaybackSession.cs`, narration sessions, `ReaderOperationSession.cs`
- **Objective:** Extract full-range/section preparation, narration selection, prefetch, timing, and cancellation into a UI-independent command/state boundary.
- **Implementation:** Characterize section preparation, full-range preparation, cancellation, stale completion, preview conflicts, cached preparation, and shutdown; define typed progress/state; retain `MediaPlayer` in a WPF adapter; remove preparation timers/flags from the window when their ownership moves.
- **Done:** Preparation behavior is testable without WPF; only the media adapter touches WPF playback; no duplicated cancellation/busy state remains.
- **Prompt:**
  > Implement WP-05 from the delegation plan. Preserve live playback, highlighting, cache, and cancellation behavior. Do not mix export or visual-control extraction into this packet. Run the mandated gates and commit.

### WP-06 — Reading export and publishing command owners

- **Backlog:** UI-002
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-05
- **Primary files:** Reader export preparation/exporters, publishing coordinator/window, `ReaderWindow.xaml.cs`
- **Objective:** Move audiobook/video export and publishing workspace/job orchestration behind typed commands with one operation owner and concise errors.
- **Implementation:** Characterize cancellation, partial files, retry/resume, progress ordering, and shutdown; keep file dialogs and publishing modal rendering in WPF; ensure no raw exception messages reach UI; preserve reuse of live timing/highlight policy.
- **Done:** Export/publish behavior tests do not access controls; partial artifact ownership and cleanup are explicit.
- **Prompt:**
  > Implement WP-06 from `planning/codebase-hardening-delegation-plan.md`. Limit scope to export/publishing command ownership and error policy. Run focused fault/cancellation tests plus the full Release gate and commit.

### WP-07 — Reading Studio cohesive views and presentation state

- **Backlog:** UI-002
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-06
- **Primary files:** `ReaderWindow.xaml(.cs)` and new/existing Reader view controls
- **Objective:** Split sidebar, document page/editor, and transport into intent-forwarding views after state ownership has moved.
- **Implementation:** Define immutable presentation state; move control-specific focus/selection/typography/rendering into views; retain top-level coordination in the window; centralize accessibility metadata and avoid a base-window hierarchy.
- **Done:** Window is a thin coordinator, code-behind is normally below 500 cohesive lines, and live/export timing policy is shared.
- **Prompt:**
  > Implement WP-07 from the delegation plan. Extract views only after confirming their controller/state owner. Do not use partial code-behind files as a line-count workaround. Run STA, markup, and full Release tests and commit.

### WP-08 — Close UI-002

- **Backlog:** UI-002
- **Model:** GPT-5.6 Sol, high
- **Depends on:** WP-07
- **Objective:** Audit Reading Studio ownership, lifetime, shared timing policy, and shutdown; correct only demonstrated gaps.
- **Done:** UI-002 criteria and focused tests are complete; backlog evidence names each owner and remaining manual checks.
- **Prompt:**
  > Perform WP-08 as the final UI-002 architectural audit. Use the backlog done criteria as the acceptance test, run the full Release suite, update canonical evidence, and commit only necessary fixes.

### WP-09 — Window shell manual evidence

- **Backlog:** UI-SHELL-001
- **Model:** GPT-5.6 Luna, medium, plus user-operated UI
- **Depends on:** WP-07
- **Parallel-safe:** Read-only/manual against a fixed commit
- **Objective:** Execute and record explicit Dark/Light theme, maximize/restore, 100/150/200% DPI, keyboard caption buttons, high contrast, and accessible-name checks for every first-party window. Windows System-theme tracking is not a supported product behavior.
- **Done:** Dated screenshots/results identify OS build, scale, theme, commit, pass/fail, and issue links. An agent must not infer visual success from XAML.
- **Prompt:**
  > Execute WP-09 from the delegation plan against the current commit. Do not change product code unless a reproducible defect is found; if found, document it and stop for a separate fix packet. Record dated manual evidence and update UI-SHELL-001 truthfully.

### WP-10 — Dictation History manual evidence

- **Backlog:** UI-HISTORY-001
- **Model:** GPT-5.6 Luna, medium, plus user-operated UI
- **Depends on:** WP-09
- **Objective:** Verify search, no-results, selection, caret preservation, save, delete, empty/loading/error states, localized time, keyboard use, 200% scale, dark/light/high contrast, and absence of repeated type/title labels.
- **Prompt:**
  > Execute WP-10 from the delegation plan against the fixed tested commit. Capture real manual evidence; do not claim visual checks from source inspection. Update UI-HISTORY-001 only when every criterion is evidenced.

### WP-11 — Complete transcription baseline evidence

- **Backlog:** PERF-TRANSCRIBE-001
- **Model:** GPT-5.6 Luna, high for harness/evidence; user supplies microphone/device run
- **Depends on:** fixed Release build; may overlap code refactors only as read-only measurement
- **Objective:** Produce controlled end-to-end stop-to-visible-text and GPU/VRAM evidence for cold/warm 3/7/11-second runs.
- **Implementation:** Pin commit/config/model/runtime/device; run repeated samples sufficient for p50/p95; correlate pipeline stages; sample parent/child process and worker count; verify cancel/restart/orphan behavior; commit transcript-free raw data and report.
- **Done:** Named-stage latency and memory budgets are reproducible; missing GPU telemetry is explicitly marked unsupported rather than guessed.
- **Prompt:**
  > Execute WP-11 from the delegation plan. Use only the existing benchmark/instrumentation boundary unless a measurement defect blocks valid evidence. Keep user audio/text out of committed artifacts, run the required matrix, update PERF-TRANSCRIBE-001, and commit reproducible evidence.

## S2 work packets

### WP-12 — Measured transcription optimization decision

- **Backlog:** PERF-TRANSCRIBE-002
- **Model:** GPT-5.6 Sol, high
- **Depends on:** WP-11
- **Objective:** Rank measured bottlenecks, test the least-complex high-impact change, and either implement a >=20% material improvement within accuracy budget or document that inference/model choice dominates.
- **Implementation:** Verify lazy selected-provider startup, persistent worker reuse, one model instance, cancellation cleanup, and protocol/file overhead before evaluating runtime alternatives. Never reintroduce command-line Whisper or propose a WPF/Rust rewrite.
- **Prompt:**
  > Execute WP-12 using the committed PERF-TRANSCRIBE-001 evidence. Make no optimization claim without comparable cold/warm data and accuracy results. Implement only the smallest qualifying change, run all gates, update the report/backlog, and commit.

### WP-13 — Remaining unreachable-capability audit

- **Backlog:** REMOVE-001
- **Model:** GPT-5.6 Luna, high
- **Depends on:** WP-11; coordinate with WP-28
- **Objective:** Classify ModelBenchmark and auxiliary executables by owner, caller, build role, and distribution role; delete or move only with proof.
- **Implementation:** Use `rg`, project graph, scripts, installer inputs, and release docs; preserve the benchmark only if WP-11 owns it; remove tests/docs/packaging in the same change as a deletion.
- **Prompt:**
  > Implement WP-13 from the delegation plan. Produce a caller/owner/distribution table before deleting anything. Preserve PERF-TRANSCRIBE tooling with a real owner, run clean build/test/package gates, update REMOVE-001, and commit.

### WP-14 — SettingsDraft and validation model

- **Backlog:** UI-003
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-04; SET-001 already complete
- **Objective:** Replace scattered control-to-`AppSettings` mutation with one typed draft, validation result, and immutable editing state.
- **Implementation:** Characterize every setting; distinguish immediate preview from persisted value; include unsupported-schema read-only state; remove history configuration entirely; keep Manage Dictation History as navigation intent.
- **Prompt:**
  > Implement WP-14 from the delegation plan. Limit scope to the Settings draft/validation state and characterization tests; do not yet split all controllers or views. Run full Release tests and commit.

### WP-15 — Settings operation controllers and autosave revision owner

- **Backlog:** UI-003
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-14
- **Objective:** Give model/device refresh, benchmark, download/activate/delete, import/export, and save revision/cancellation deterministic owners.
- **Implementation:** Preserve one autosave coordinator; latest refresh wins; import replaces the draft atomically; failures retain unsaved changes; all raw exception details go to Diagnostics.
- **Prompt:**
  > Implement WP-15 exactly as specified. Add non-WPF race/failure tests before deleting SettingsPanel flags. Do not redesign XAML in this packet. Run all gates and commit.

### WP-16 — Settings view decomposition and UI-003 closure

- **Backlog:** UI-003
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-15
- **Objective:** Bind Settings controls to the editing state, split cohesive sections only where useful, remove mutation/busy/initialization flags, and close UI-003.
- **Done:** Concurrent save/refresh/import is deterministic; behavior tests avoid WPF; code-behind is cohesive and normally below 400–500 lines.
- **Prompt:**
  > Implement and close WP-16 from the delegation plan. Do not add a generic MVVM framework or base panel. Run markup/STA/controller/full Release tests, update UI-003 evidence, and commit.

### WP-17 — Core public-surface inventory and reduction

- **Backlog:** CORE-001
- **Model:** GPT-5.6 Sol, high
- **Depends on:** UI-001/UI-002/UI-003 complete
- **Objective:** Generate a consumer inventory for every public Core type, define permitted project dependencies, then internalize or move only proven implementation details.
- **Implementation:** Establish an API baseline first; separate domain contracts from provider metadata; avoid circular dependencies and mass namespace churn.
- **Prompt:**
  > Execute WP-17 from the delegation plan. Produce the public-type consumer inventory before editing. Make API changes in small groups with compiled consumer tests, run the full gate, update CORE-001, and commit.

### WP-18 — Inference capability organization and worker contract

- **Backlog:** INFRA-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-17, PROVIDER-001
- **Objective:** Organize transcription/chat/TTS/alignment/OCR/worker types by capability and establish shared process/protocol mechanics without generic provider policy.
- **Implementation:** Characterize worker framing/start/stop/errors first; prefer namespace/folder moves over project splits; split an assembly only with measured deployment or dependency benefit; add one worker contract-test suite.
- **Prompt:**
  > Implement WP-18 from the delegation plan. Keep provider policy concrete and share only proven process/protocol mechanics. Preserve binary/runtime behavior, run worker contracts and full Release tests, and commit.

### WP-19 — Cache identity and recovery

- **Backlog:** CACHE-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-08, WP-18
- **Objective:** Inventory narration/timing/preview/OCR/model/prepared-export caches and define versioned typed keys containing every output-affecting input.
- **Implementation:** Specify eviction, corruption recovery, atomic writes, and cancellation; add parameterized hit/miss tests distinguishing content-affecting from appearance-only changes.
- **Prompt:**
  > Implement WP-19 from the delegation plan. Start with a cache inventory and key matrix. Do not create one generic cache framework; update each real owner, add identity/corruption tests, run all gates, and commit.

### WP-20 — Correlated operation diagnostics

- **Backlog:** OBS-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** UI command owners and WP-18
- **Objective:** Standardize operation IDs, stages, durations, provider/model/runtime identity, typed outcomes, retry/fallback, remediation code, and redaction.
- **Implementation:** Define the smallest shared event contract; instrument primary dictation/chat/reader/settings workflows; prohibit raw text/audio; add nesting/cancellation/redaction tests and bundle reconstruction test.
- **Prompt:**
  > Implement WP-20 from the delegation plan. Preserve existing diagnostics consumers and do not introduce a logging framework migration. Add correlation/redaction tests, run full Release tests, update OBS-001, and commit.

### WP-21 — Worker/process fault matrix

- **Backlog:** REL-001
- **Model:** GPT-5.6 Terra, high; Sol high review if ownership is ambiguous
- **Depends on:** WP-18, WP-20, CONCUR-001
- **Objective:** Contract-test every owned Python/server/OCR/alignment/ffmpeg/UIAccess/publishing process for timeout, missing dependency, malformed protocol, stderr flood, crash, cancellation, restart, orphan cleanup, and shutdown.
- **Implementation:** Inventory processes and ownership first; add injectable launch/clock/stream boundaries only where needed; never kill a pre-existing third-party process.
- **Prompt:**
  > Execute WP-21 from the delegation plan. Build the process/owner/fault matrix before code changes, then close one process family at a time. Run fault tests plus full Release gates, update REL-001, and commit.

### WP-22 — Privacy-boundary verification

- **Backlog:** SEC-001
- **Model:** GPT-5.6 Luna, high
- **Depends on:** HISTORY-002, WP-20
- **Objective:** Verify local plaintext history protected by OS account/filesystem access, diagnostic exclusion of raw history/audio, network behavior, and secret redaction; align threat model and product text.
- **Prompt:**
  > Implement WP-22 from the delegation plan. Compare actual storage/network/logging behavior with the threat model, add automated privacy/redaction checks, correct mismatches, run security/full Release gates, and commit.

### WP-23 — Supply-chain provenance enforcement

- **Backlog:** SEC-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-13, WP-22, WP-29
- **Objective:** Produce machine-readable provenance, integrity, license, credential-handling, and CI-action records for NuGet/Python/models/runtimes/binaries/fonts.
- **Done:** Compliance fails on missing required provenance/hash/license entries; docs match packaging.
- **Prompt:**
  > Implement WP-23 from the delegation plan. Inventory shipped artifacts from actual project/installer inputs, add deterministic compliance validation, run security/package/full Release gates, update SEC-001, and commit.

### WP-24 — Accessibility automation

- **Backlog:** ACCESS-001
- **Model:** GPT-5.6 Luna, high
- **Depends on:** WP-08, WP-16
- **Objective:** Add semantic/markup tests for names, labels, focus order, keyboard intents, reduced-motion policy, scaling constraints, and error-state reachability across primary windows.
- **Prompt:**
  > Implement WP-24 from the delegation plan. Add low-brittleness automated accessibility checks after all major XAML refactors. Do not claim manual screen-reader/DPI results. Run all tests and commit.

### WP-25 — Accessibility manual matrix

- **Backlog:** ACCESS-001
- **Model:** GPT-5.6 Luna, medium, plus user-operated UI
- **Depends on:** WP-24
- **Objective:** Execute keyboard-only, screen reader, focus, high contrast, 200% text/DPI, reduced motion, narrow window, RTL/mixed text, and error recovery across Workbench, Settings, History, Reader, Publishing, first run, and tray alternatives.
- **Prompt:**
  > Execute WP-25 against the fixed Release commit. Record dated environment and per-case evidence; do not infer results. File concrete defects as separate bounded fixes, rerun affected cases, update ACCESS-001, and commit evidence.

### WP-26 — Test taxonomy and focused-suite rebalance

- **Backlog:** TEST-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** UI and infrastructure refactors complete
- **Objective:** Label unit/integration/model/hardware/manual suites, split giant WPF tests by behavior, and replace brittle source-string assertions with compiled/project checks where practical.
- **Prompt:**
  > Implement WP-26 from the delegation plan. Preserve coverage while improving failure locality; do not rewrite stable tests solely for style. Run the entire Release suite and commit.

### WP-27 — Coverage, mutation, and fault-evidence thresholds

- **Backlog:** TEST-001
- **Model:** GPT-5.6 Sol, high
- **Depends on:** WP-26
- **Objective:** Collect CI coverage, select explicit thresholds, and apply targeted mutation/fault testing to migration, state, and policy code rather than chasing global percentage.
- **Prompt:**
  > Implement WP-27 from the delegation plan. Establish a measured baseline before thresholds, target critical policy/state code, document exclusions and runtime cost, run CI-equivalent gates, update TEST-001, and commit.

### WP-28 — Executable and packaging-role separation

- **Backlog:** PKG-001
- **Model:** GPT-5.6 Luna, high
- **Depends on:** WP-13
- **Objective:** Classify App, UIAccess, TtsCli, ModelBenchmark, Spikes, and VoicePreviewGenerator; move retained developer utilities under `tools`; make production installer inputs explicit.
- **Prompt:**
  > Implement WP-28 from the delegation plan using the WP-13 caller/owner audit. Ensure normal production packaging cannot include tools, run clean build/package/upgrade gates, update PKG-001, and commit.

## S3 work packets

### WP-29 — Central package and shared test configuration

- **Backlog:** BUILD-001
- **Model:** GPT-5.4-mini, high (Luna medium fallback)
- **Depends on:** major project moves complete
- **Objective:** Add central NuGet version management and shared test properties/targets without changing resolved dependencies unexpectedly; document lock/restore policy.
- **Prompt:**
  > Implement WP-29 from the delegation plan. Capture the pre-change resolved package graph, centralize versions mechanically, prove clean restore/build/test equivalence, update BUILD-001, and commit.

### WP-30 — Canonical documentation consolidation

- **Backlog:** DOC-001
- **Model:** GPT-5.4-mini, high
- **Depends on:** architecture stable; may audit earlier read-only
- **Objective:** Keep README, product inventory, system architecture, developer guide, release checklist, and backlog consistent; delete/archive superseded plans only after extracting valid decisions; add retired-claim checks.
- **Prompt:**
  > Implement WP-30 from the delegation plan. Verify claims against current code and packaging, do not preserve contradictory plans, add deterministic retired-claim checks, run docs/full Release gates, update DOC-001, and commit.

### WP-31 — Size budgets

- **Backlog:** SIZE-001
- **Model:** GPT-5.6 Luna, medium
- **Depends on:** WP-28
- **Objective:** Measure tracked source/assets, 129 voice previews, installer variants, model/runtime downloads, and installed footprint; define enforceable budgets and explicit exceptions.
- **Prompt:**
  > Execute WP-31 from the delegation plan. Measure actual artifacts rather than repository guesses, document the voice-preview decision as pending if product input is required, add deterministic budget checks, update SIZE-001, and commit.

### WP-32 — Reproducible release evidence

- **Backlog:** RELEASE-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-23, WP-25, WP-28, WP-29, WP-31
- **Objective:** Separate durable templates from generated machine results; attach commit/environment identity; ignore machine-specific outputs; make release rehearsal reproducible.
- **Prompt:**
  > Implement WP-32 from the delegation plan. Run the release workflow from a clean commit, preserve only durable templates and redacted evidence, prove traceability to the commit, update RELEASE-001, and commit.

### WP-33 — Public API and dependency guards

- **Backlog:** API-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** WP-17, WP-18, WP-29
- **Objective:** Generate API baselines for reusable assemblies and enforce project graph/forbidden dependencies with actionable CI failures.
- **Prompt:**
  > Implement WP-33 from the delegation plan. Baseline intentional APIs after CORE/INFRA changes, add compiled dependency guards, run clean restore/build/test, update API-001, and commit.

## S4 work packets

### WP-34 — Generated residue and proven-unused cleanup

- **Backlog:** CLEAN-001
- **Model:** GPT-5.4-mini, high
- **Depends on:** WP-30, WP-32
- **Objective:** Finish generated-report audit and remove only files with no caller/current evidence role; preserve the intentional empty MSIX placeholder.
- **Prompt:**
  > Implement WP-34 from the delegation plan. Produce no-caller/no-owner proof for every deletion, preserve release templates, run clean build/test/package gates, update CLEAN-001, and commit.

### WP-35 — Current naming alignment

- **Backlog:** NAME-001
- **Model:** GPT-5.6 Terra, high
- **Depends on:** structural work complete
- **Objective:** Use Notype consistently in current product text and accurately name current settings/types without breaking persisted schemas, namespaces, install identity, automation, or upgrade compatibility.
- **Prompt:**
  > Implement WP-35 from the delegation plan. Start with a compatibility matrix of user-visible names, persisted names, assembly namespaces, installer identity, and automation. Rename only safe current surfaces, run upgrade/full Release gates, and commit.

### WP-36 — Narrow consistency automation and final review

- **Backlog:** STYLE-001
- **Model:** GPT-5.4-mini, high; GPT-5.6 Sol high for final architecture review
- **Depends on:** all code-changing packets
- **Objective:** Add only low-noise formatter/analyzer rules, fix resulting issues by ownership rather than suppressions, and perform the final rubric/release-gate review.
- **Prompt:**
  > Implement WP-36 from the delegation plan. Measure analyzer noise before enabling rules, avoid repository-wide cosmetic churn, require local rationale for suppressions, run every release/package/security/manual gate that applies, update STYLE-001 and the rubric, and commit.

## Integration review after every five packets

Use GPT-5.6 Sol high for a read-mostly integration audit after WP-04, WP-08, WP-16, WP-21, WP-28, and WP-36. The reviewer should inspect ownership, dependency direction, unobserved tasks, raw UI exception messages, tests, backlog truthfulness, and commit boundaries. It should fix only clear regressions; new improvements go into a future packet rather than expanding the review.

Copy-ready review prompt:

> Review the completed packet range ending at the current HEAD using `planning/codebase-hardening-delegation-plan.md`, `planning/codebase-hardening-backlog.md`, and the engineering rubric. Verify ownership, behavior preservation, tests, documentation truthfulness, and release gates. Make only concrete regression fixes, run the full Release suite and `git diff --check`, then commit the review fixes or report that no commit was needed.

## Completion accounting

The 25 open backlog items map to the packets as follows:

| Backlog item | Packets |
| --- | --- |
| UI-SHELL-001 | WP-09 |
| UI-HISTORY-001 | WP-10 |
| PERF-TRANSCRIBE-001 | WP-11 |
| UI-001 | WP-01–WP-04 |
| UI-002 | WP-05–WP-08 |
| PERF-TRANSCRIBE-002 | WP-12 |
| REMOVE-001 | WP-13 |
| UI-003 | WP-14–WP-16 |
| CORE-001 | WP-17 |
| INFRA-001 | WP-18 |
| CACHE-001 | WP-19 |
| OBS-001 | WP-20 |
| REL-001 | WP-21 |
| SEC-001 | WP-22–WP-23 |
| ACCESS-001 | WP-24–WP-25 |
| TEST-001 | WP-26–WP-27 |
| PKG-001 | WP-28 |
| BUILD-001 | WP-29 |
| DOC-001 | WP-30 |
| SIZE-001 | WP-31 |
| RELEASE-001 | WP-32 |
| API-001 | WP-33 |
| CLEAN-001 | WP-34 |
| NAME-001 | WP-35 |
| STYLE-001 | WP-36 |

No packet may mark a backlog item complete unless every mapped packet and its manual evidence are complete.
