# Koncus Nai: chat polish and first public release plan

Date: 2026-09-15. Status: batches 1–4 implemented and verified; final signed-release and clean-machine gates remain. See [the implementation report](production-readiness-implementation-2026-09-15.md).

## Scope and review limits

This plan covers the reported chat formatting, incorrect dates, maximized layout, zoom, header order, `run-kn`, optional feature setup, and first public release. Read-aloud improvements are deferred in [TODO.md](../TODO.md).

This was a targeted static review of the affected implementation, launch/installer configuration, CI, security boundaries, architecture, and existing release evidence. It is not a line-by-line audit of every file, a completed secret scan, or a production security certification. Findings below distinguish observed code from risks that still require testing. No app code, installed commands, settings, histories, running processes, or Git history were changed. No build, installation, or release gate was run for this documentation pass.

The working tree already contained 221 changed/untracked entries. Preserve and classify that work before implementation; do not reset, blanket-stage, or mix it into unrelated fixes. HEAD already has 104 commits: this is a first public publication, not a repository with no history.

Use this as the current user-requested work sequence. Retain the engineering rules and completed work in [the existing hardening backlog](codebase-hardening-backlog.md); do not restart its completed refactors.

## Confirmed findings

Paths below are relative to the repository root.

| Area | Evidence | Consequence |
| --- | --- | --- |
| Markdown | `src/DictateAnywhere.App/Presentation/ChatMarkdownRenderer.cs` recognizes bold, inline code, links, and fences, but not single-star emphasis or horizontal rules. | The screenshot's `*how*` and `---` are valid Markdown left visible by an incomplete renderer. Do not remove punctuation from model output globally. |
| Paragraph spacing | The renderer creates a paragraph for every input line, including blank lines, with fixed margins and line height. | Extra gaps and poor behavior with large fonts; Markdown soft line breaks are treated as paragraphs. |
| Scrollbar | `Workbench/WorkbenchChatTranscriptView.xaml` centers a `RichTextBox` with `MaxWidth="720"`. Its code-behind clears/rebuilds the document and calls `ScrollToEnd()` on render. | The scroll track sits beside the narrow text column. Re-rendering also risks losing selection and the reader's position. |
| Date grounding | `WorkbenchChatController.BeginCompletion` supplies brand/file context but no readable current date/timezone. The llama.cpp payload sends roles/content, not message timestamps. | The model has no dependable supplied clock context. The guessed 2023 dates do not establish a clock or Windows bug. |
| Continue wording | `LlamaCppChatService.ResponsePolicy` encourages Continue for long solutions; `AddCompletion` separately appends a notice when truncated. | Separate model verbosity from genuine truncation notices; the essay's exact outro cannot be attributed solely to the prompt. |
| Zoom | `AppTextScaleManager` adapts semantic chrome text to Windows settings; chat typography is separate. No workbench Ctrl-plus zoom command was found in the reviewed handlers. | Whole-workbench zoom needs its own coherent policy, not just another chat font increment. |
| Header | The sidebar toggle is an overlay before the logo/name; sidebar branding reserves space for it. | Move these together so their layout and title-bar hit testing stay consistent. |
| Launcher | `scripts/install-koncus-nai-launcher.ps1` creates three aliases targeting source `scripts/run-notype.ps1`. That script may build the project and eagerly prepares Kokoro. | This is a developer launcher, not a customer installation command. Dictation-only startup should not require read-aloud preparation. |
| Installer | `installer/wix/DictateAnywhere.wxs` has one main feature, a Start menu shortcut, and no `run-kn` registration. | An installed customer needs explicit command registration and first-run feature setup. |
| Read aloud | `WorkbenchSpeechSession.ReadAsync` calls `SynthesizeAsync` on each request; its workbench API exposes stop, not pause/resume. | There is no workbench-level prepared-reply replay path. Lower-level synthesis cache behavior still needs inspection before claiming all audio is regenerated. |
| Release evidence | The rebrand report records an unsigned installer and a failed test-source size budget. The later Gemma fix report says no installer was regenerated. | The old installer is not evidence for a fixed, signed release. Rebuild from the final candidate. |

The pasted `&#x20;` is an encoded space. Determine whether it originated in app rendering, clipboard/export, or the paste transport before changing entity handling. Never decode arbitrary content repeatedly or modify code examples to hide this symptom.

## Batch 1 — Chat rendering and truthful date answers

### Rendering

- Extend or replace the small parser behind the existing WPF rendering boundary. Prefer a maintained Markdown parser with a deliberately limited native rendering surface if extending the regex approach becomes fragile; review its dependency/license before adoption.
- Support paragraphs, soft/hard breaks, headings, emphasis, strong text, lists, quotes, rules, inline code, and fenced code. Define a readable fallback for tables and unsupported syntax.
- Keep paragraph spacing and line height relative to typography. Preserve the user's font choice; do not silently replace it because the screenshot uses a serif font.
- Preserve raw stored messages. Rendering, accessible text, clipboard text, code copy, export, and speech projection must have explicit behavior. Keep existing raw Markdown copy available; speech should read prose without formatting markers.
- Bound work on large/malformed input. Current regexes have no explicit timeout. Test long unmatched markers, backticks within code, escaped punctuation, Unicode/Hindi/Nepali, literal asterisks, math, unclosed fences, and very long lines.
- Treat generated text as untrusted: do not execute HTML/scripts or fetch remote images during rendering. Existing links are styled text; if adding activation, allow only intended schemes and require a user click. Do not inadvertently enable file, command, or custom-protocol execution.

### Dates and response policy

- Introduce a testable clock/timezone source. Sample it for every outgoing request, including retry and resumed chats; inject an unambiguous local date, weekday, timezone, and offset as transient application context.
- Preserve the brand and file context. Pass instructions through the existing provider formatter so Gemma still receives compatible alternating turns and Qwen keeps its system role. Never persist stale clock instructions in history.
- Answer narrowly recognized standalone today/tomorrow/yesterday questions with deterministic local calendar arithmetic. Accept a simple greeting alongside the question. Do not intercept quoted text, fiction, document questions, or an explicit foreign timezone using a loose substring match.
- Broader requests can use the supplied clock context; this does not make the model a reliable live-news source. Say when a requested external fact or timezone cannot be established. The device clock remains the authority, not a guarantee that the computer's clock is correct.
- Use calendar-day addition in the selected timezone, not a fixed UTC 24-hour shift. Test midnight, month/year rollover, leap day, DST, timezone changes, and old conversations containing incorrect dates.
- Request direct answers without habitual introductions or closing offers. Reserve app continuation UI for actual truncation and keep app-added metadata out of subsequent model answers. Do not strip legitimate content after generation with broad text replacements.

**Acceptance:** the supplied essay renders italics/rules and normal paragraph gaps; code copy stays exact. Fake-clock tests prove calendar behavior. Run the installed Gemma model through the actual service with date context plus a follow-up, and retain Qwen formatting regression coverage. Do not rerun expensive unrelated model benchmarks.

## Batch 2 — Workbench layout, header, and zoom

- Give the transcript a scroll viewport spanning the chat pane; center a readable content column inside it. Put the vertical track at the chat pane's right edge, accounting for the outer grid margin. Keep the composer visible and aligned with the text.
- Use one vertical scroll owner. Check code-block horizontal scrolling, wheel/Page Up/Down, keyboard selection, drag selection, overlays, and touchpad behavior.
- Follow new responses only when the reader was already at the bottom. Preserve an anchor when reading older content, resizing, zooming, or toggling the sidebar; provide a return-to-latest affordance when useful.
- Header order: **[KN logo] [Koncus Nai] [sidebar toggle]** at the upper left. This means moving the toggle after the name, not moving the whole sidebar to the right. Keep a visible reopen button when collapsed, tooltips, accessible names, and correct window-drag/caption hit testing.
- Add workbench zoom: `Ctrl++` zooms in, `Ctrl+-` zooms out, `Ctrl+0` restores 100%. Provide visible menu controls and the current percentage. Initial proposed range: 80–150%, subject to minimum-window accessibility checks.
- Scale the workbench's text, controls, and spacing coherently; leave native window controls at normal Windows sizing. Existing chat font preference remains the base document setting. Compose zoom with Windows DPI/text scaling without applying either twice.
- Persist a validated finite value with a safe default. Handle main keyboard plus/Shift+equals and numpad variants. Respect IME, AltGr, hotkey-capture controls, and existing editor shortcuts. No global hotkey registration; scope to the active workbench.
- Reuse `AppTextScaleManager`/design tokens where appropriate; do not apply an ad hoc transform that clips popup menus or breaks hit testing. Reader zoom can be a separate later decision.

**Acceptance:** inspect maximized and minimum-size windows, sidebar open/closed, light/dark/high contrast, keyboard-only navigation, and 100/150/200% Windows scale. Check moving between monitors, high text size combined with zoom, long history titles, code overflow, and settings overlays. Preserve composer drafts and transcript selection.

## Batch 3 — Make `run-kn` the only supported launch command

### Developer checkout

- Add the canonical `scripts/run-kn.ps1`, update its installer/wrappers, README and current guides, and install `run-kn.cmd` into the existing managed launcher directory.
- Retire generated `run-notype`, `run-nilo`, and `run-koncus-nai` aliases after retargeting owned shortcuts and checking references. Remove only files whose content/target proves ownership; preserve foreign/customized commands and report conflicts.
- Update active callers/tests before removing old source scripts. Historical evidence can retain old names. Do not globally rename `DictateAnywhere` storage paths, DPAPI entropy, mutexes, executable identity, or MSI UpgradeCodes.
- Remove mandatory read-aloud preparation from ordinary launch. Audit build freshness too: lock/runtime/config changes should not leave the launcher running stale outputs merely because C#/XAML timestamps did not change.

### Installed customer

- Ship a small installed launcher or equivalent executable entry point for `run-kn`, targeting installed files without a checkout, SDK, or development PowerShell script.
- Register a Shell App Paths entry for `run-kn.exe` for Windows Run, and provide command discovery for CMD/PowerShell through a narrowly scoped managed PATH directory. These are different discovery mechanisms; verify both. See [Microsoft application registration](https://learn.microsoft.com/en-us/windows/win32/shell/app-registration).
- Integrate registration/removal into WiX with the correct per-machine/per-user scope. Refresh environment notifications; document that already-open terminals may retain old PATH values.
- Repeated launches should activate the existing instance rather than silently exit or create a second model worker. Decide activation behavior for dictation-only mode explicitly.
- Handle paths with spaces/non-ASCII characters, argument quoting, command-name collisions, stale developer PATH entries, multiple Windows users, upgrade/repair, and uninstall ownership. Never terminate a same-named foreign process.

**Acceptance:** after a clean installation, `Win+R` → `run-kn`, a fresh CMD, and a fresh PowerShell session all launch the installed app. Repeating the command activates the existing instance. Upgrade preserves settings/history/startup preference; uninstall removes only owned command entries. Test with the source folder unavailable.

## Batch 4 — Simple setup with optional capabilities

For v1, prefer a small shared installation with a first-run choice: **Dictation only** or **Full assistant**. This is a new product feature; keep it focused and reversible in Settings. True independently installable MSI modules can follow if payload measurements justify them.

- Dictation-only setup offers the hotkey, microphone test, language/model choice, download size, and readiness feedback. Do not initialize chat, TTS, Reading Studio, or publishing dependencies until requested.
- Full assistant may expose the additional surfaces, but still prepares each provider on explicit use. Clearly distinguish installed app size, runtime download size, model download size, and disk space needed during extraction.
- Existing users retain their enabled capabilities. Mode changes must not delete their histories/models or reset voice/hotkey preferences. Preserve the existing always-local-history product policy; document that histories are plaintext protected by Windows account permissions.
- Audit startup through both the source launcher and `ApplicationComposition`/runtime registration. Hiding controls alone is not optional dependency initialization.
- Verify offline startup, interrupted download/resume, proxy failures, low disk, corrupted cache, denied permissions, missing microphone/audio device, and canceled setup. A canceled optional download must leave dictation usable.

## Release risks requiring closure or evidence

These are targeted investigation items, not claims of demonstrated exploits.

| Priority | Finding/risk | Required evidence or action |
| --- | --- | --- |
| Before public source | Secrets and private data in current files or 104-commit history | Scan candidate files plus all refs intended for publication, with redacted reports. Review screenshots, fixtures, local paths, logs, OAuth files, signing material, environment files, model caches, binaries, and archives. `.gitignore` does not remove already tracked history. |
| Before production | Wrong local server/model | `LlamaCppChatService` accepts any successful `/health` at its configured address before checking ownership/model identity. Validate the selected model and managed endpoint ownership before sending chat/file content. Test occupied port, other process, model switching, service replacement, and cancellation during startup. Never kill an unowned server. |
| Before production | Context overflow and memory growth | The reviewed llama.cpp path sends the conversation without an explicit token budget; default context is 8192 and output allowance 1536. Establish budgeting after formatting, preserving required instructions/latest question and complete turns. Define clear behavior when one attachment is too large; never silently imply all omitted content was read. Test long chats, multilingual input, file context, and concurrent render memory. |
| Before production | Lifecycle races | Exercise new chat/model switch/history deletion/window close while completion, import, speech, or download is pending. Late work must not resurrect deleted data, overwrite newer settings, speak unexpectedly, or appear in another chat. |
| Before production | Privacy and privilege boundaries | Preserve current-user DPAPI for OAuth and stable entropy. Inspect log/error-body redaction, clipboard restoration, foreground target changes, password fields, UIAccess helper trust/IPC, and local server bind/auth behavior. Imported documents are data, not instructions to gain capabilities. |
| Before production | Supply-chain and file boundaries | Re-run pinned hash/provenance checks and inspect archive traversal, case collisions, symlinks/reparse points, extraction limits, partial download promotion, worker input/output limits, and process-tree cleanup. Reuse existing controls rather than replacing them speculatively. |
| Before production | Existing failing gate and stale installer | Re-measure test-source size (prior report: 1,529,661 versus 1,400,000 bytes); resolve through reviewed policy or appropriate consolidation, not hidden weakening or deleting useful coverage. Rebuild installer after all fixes; previous unsigned candidate predates Gemma fix. |

### Publication checklist

1. Inventory and review the intended source changes. Run a dedicated secrets scan locally before uploading anything. Do not print matched credentials. If a secret was exposed, revoke/rotate it first; cleaning history alone is insufficient. Any necessary history rewrite is a separate reviewed operation. See [GitHub's sensitive-data guidance](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository).
2. Choose the repository license and verify third-party/runtime/model redistribution obligations against the exact versions shipped. Include notices and accurate privacy/setup documentation. Keep runtime credentials and signing keys out of the repo and package.
3. Validate a clean checkout with locked restore/build/test and existing quality/security/documentation/size gates. Current CI already uses read-only contents permission, pinned action revisions, and checkout without persisted credentials; preserve these controls. Keep privileged release secrets inaccessible to untrusted PR code.
4. Follow [the canonical release checklist](../docs/release/release-checklist.md), using fresh evidence from the final candidate. Generate signed application/helper/installer artifacts as required, checksums and a versioned manifest. Choose an unused release version; do not call it 1.0 merely because this is the first public upload.
5. Test a clean Windows machine without the repo or developer dependencies: install, first run, both modes, `run-kn`, hotkey dictation into representative apps, model chat, reboot/startup, upgrade, repair, uninstall, and recovery after interrupted setup. Include a non-admin user and verify the supported elevation path separately.
6. Inspect final package contents and sanitized diagnostics, publish known limitations, and retain rollback artifacts. Publishing source and shipping a production installer are separate milestones; neither happens during this planning task.

## Efficient implementation order

Use four focused implementation batches above, with a small reviewable diff and evidence at each boundary. Then close release blockers and execute the final release checklist once on the unchanged candidate. Run focused tests during work and the repository-required broader gates at task boundaries; repeat broader checks only after meaningful changes or failures. Preserve the already verified Gemma fix and its real-model evidence, while adding a real-model check when modifying its outgoing context.

No claim that all hidden edge cases are eliminated is justified by static inspection. Completion means the named acceptance cases and release gates have evidence, with remaining limitations stated explicitly.
