# Koncus Nai production-readiness implementation report

Date: 2026-09-15

This report records the implementation and evidence for the [chat polish and first public release plan](production-readiness-plan-2026-09-15.md). It does not declare a production release: signing and clean-machine install lifecycle tests still require release infrastructure.

## Delivered behavior

### Chat presentation and date handling

- Replaced the partial Markdown parser with Markdig at a bounded native-WPF rendering boundary. HTML and remote content remain disabled. Headings, paragraphs, emphasis, lists, quotes, rules, inline code, fenced code, and line breaks render as formatting while stored/exported text remains unchanged and code-copy content remains exact.
- Added a 100,000-character formatting limit with literal-text fallback for unusually large or malformed replies.
- Added fresh device-local date, weekday, timezone, and UTC-offset context to every model request without writing that transient context to history.
- Added deterministic handling for narrow standalone today/tomorrow/yesterday date questions, including calendar-day arithmetic across month, year, leap-day, and daylight-saving boundaries.
- Removed the instruction that encouraged routine introductions, closing offers, and model-authored Continue prompts. App failure notices and app-added continuation notices are excluded from later model history.

### Workbench layout and zoom

- Moved the transcript scrollbar to the chat-pane edge while keeping a readable centered text column.
- Preserved selection and reading position during re-render; new content follows automatically only when the reader was already near the bottom.
- Put the upper-left controls in the requested order: logo, Koncus Nai, sidebar toggle.
- Added visible 80–150% workbench zoom controls and `Ctrl++`, `Ctrl+-`, and `Ctrl+0`, including numpad handling. The setting is validated and persisted. Native caption buttons stay at Windows sizing.

### `run-kn`

- Added `scripts/run-kn.ps1` as the canonical source-checkout launcher and changed public documentation to use `run-kn`.
- Added an ownership-aware launcher installer. It removes known generated aliases and shortcuts while preserving customized or foreign files.
- Installed the user launcher at `%LOCALAPPDATA%\Notype\bin\run-kn.cmd`; fresh CMD and PowerShell command discovery resolves it.
- Broadcast the Windows environment-change notification after launcher installation so Explorer and Windows Run can refresh command discovery; already-open terminals still need to be reopened.
- Added an installed `Launchers\run-kn.cmd`, WiX App Paths registration, and a managed PATH entry. The compiled MSI database test confirms both records.
- Added same-user activation signaling so another launch restores and activates the existing workbench instead of showing the old already-running dialog.
- Removed eager read-aloud runtime preparation from ordinary source launch.

### First-run experience

- Added a reversible **Dictation only** / **Full assistant** choice to first-run setup and Settings.
- Existing settings files default to Full assistant. Dictation-only launch remains in the tray and does not open the workbench; an explicit attempt to open assistant surfaces routes to Settings so the feature can be enabled without deleting history, models, or preferences.

### Local model boundary

- Added a bounded outgoing chat context that always preserves system instructions and the latest complete exchange, with a clear failure when even the required content cannot fit.
- Added model-identity verification against llama.cpp `/v1/models` before sending chat or imported file content to an automatically managed endpoint. A healthy process serving an unexpected model is rejected and is never terminated as if it were owned by the app.
- Preserved Gemma-compatible prompt formatting and Qwen system-role behavior.

## Verification completed

- Release solution build: passed with 0 warnings and 0 errors.
- Full solution tests, run sequentially to avoid UI-test resource contention: 1,402 passed; 6 skipped environment/model tests; 0 failed.
- Real installed Gemma 3 smoke test through `LlamaCppChatService`: “what is an algorithm” and one follow-up passed against an isolated local server process.
- Focused chat, layout, and visual regressions: passed.
- Launcher migration/idempotency/custom-file preservation tests: passed.
- Packaging source contract: passed.
- Compiled MSI database validation: product/shortcut/icon, `run-kn`, upgrade family, and all 18 startup-preference cases passed without installing it.
- Security, privacy, dependency provenance, and license compliance script: passed.
- Candidate and Git-history scans found no tracked sensitive filenames and no common private-key/token signatures. This is a pattern scan and still requires a human review of publication content.
- `git diff --check`: passed after removing the reported whitespace defect.

## Built release candidate

The isolated unsigned candidate is under `artifacts/production-readiness/package/installer`:

- `KoncusNai-1.4.2-x64.msi` — 113,150,412 bytes; SHA-256 `a00d418755f3133b4d6aa8a2e4b02266835f6dd9db4c1caee7d50764c702d8b2`
- `KoncusNai-Setup-Small-1.4.2-x64.exe` — 113,914,895 bytes; SHA-256 `10b77d9aeb3e67f69bc53e06ca93d1e6e6a4a778d9e7622f460fd52da591c7bc`

The build also exposed and fixed a release-only dependency issue: the `win-x64` locked restore did not yet contain Markdig even though the normal lock file did.

## Remaining release gates

1. The test-source size gate reports 1,427,181 bytes against a 1,400,000-byte policy cap. The failure predates staging the new untracked tests, so the candidate must be measured again after the intended release files are staged. Do not increase the cap merely to make the check pass; audit and classify the full test payload first.
2. The MSI and setup EXE are unsigned. A trusted Windows code-signing certificate and timestamped signing run are required.
3. Run clean-VM install, repeated launch, upgrade, repair, uninstall, startup, offline, standard-user, and `Win+R`/CMD/PowerShell checks. Static source and MSI-database checks cannot prove Windows Installer lifecycle behavior.
4. Review the more-than-260-entry dirty working tree and split or intentionally include earlier work before the first public commit. No files were staged, committed, pushed, or published by this implementation.
5. Complete the release checklist with the signed artifact hashes and the clean-machine evidence.

The running app must be restarted to load these binaries and UI changes.
