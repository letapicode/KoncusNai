# Current product capability matrix

This document owns the supported Koncus Nai capability boundary. Historical
settings and dated plans may describe retired behavior, but do not expand this
current contract.

## Supported capabilities

| Capability | User surface | Persistence or external boundary | Runtime owner |
| --- | --- | --- | --- |
| Global toggle dictation | First run, Settings, tray | Hotkey and selected local speech provider/model | `DictationRuntime` and `DictationPipelineCoordinator` |
| Global transcription language | First run and Settings | Current scalar setting | transcription compatibility policy |
| Spoken formatting commands | Settings | `enableDictationCommands` | text transformation service |
| Workbench dictation, local chat, imports, and history | Main workspace and tray command | Local app state and always-on local plaintext JSONL history | Workbench operation/session owners and history coordinators |
| Reading Studio narration, editing, export, and publishing preparation | Reading Studio | Local document/session state; network only for explicit acquisition or publish actions | Reader session and export owners |
| Explicit Dark/Light theme selection | Settings and first-party windows | Current theme preference; high contrast maps semantic system colors | `AppThemeManager` and shared shell primitives |
| Elevated-target insertion | Global dictation | One-shot, signed UIAccess helper in a secure installed location | `UiAccessHelperProcessBridge` owns the child process |

History is always-on local plaintext JSONL protected by the signed-in Windows
account and filesystem permissions. It has no application encryption, password,
retention switch, or enable switch. Raw microphone audio is not stored in
history. Dictation and local chat processing stay on-device; network use is
limited to the allowlisted, explicit acquisition and publishing boundaries in
the [local-processing guarantees](../security/local-processing-guarantees.md).

## Explicit non-capabilities

- No current dictation profiles, per-profile languages, voice snippets,
  per-application routing rules, or experience-mode selection.
- No restored legacy Whisper provider or encrypted-history compatibility stack.
- No Windows System-theme tracking. The presence of a compatibility enum member
  does not change the supported explicit Dark/Light product behavior.
- No cloud/off-device dictation or local-chat inference.
- No automatic or password-protected history retention policy.
- No developer benchmark, CLI, spike, or preview-generator executable in the
  production installer. App and UIAccessHelper are the only production
  executable roles.

## Compatibility boundary

Settings schemas 1 through 17 remain readable so an existing installation can
upgrade without losing supported settings. `CurrentSettingsDocument` is the
sole schema-19 DTO, and `Migrations/LegacySettingsReader` is the sole historical
reader. Legacy profile, snippet, application-rule, and experience-mode values
are normalized to the current product and omitted when schema 20 is written. A
future schema is rejected without modifying the file.

Non-object settings roots, schema values that cannot be represented as integers,
and duplicate schema declarations are also preserved and opened read-only.
Historical negative integer schema versions retain the existing best-effort
legacy migration behavior.

## Failure boundaries and resource limits

- Insertion requires a matching process lifetime and focused field identity.
  Browser targets without a usable UI Automation identity are refused. Blacklist
  and secure-field policy run before elevated routing and again at dispatch;
  typing checks the target between batches. These checks reduce focus races but
  cannot make Win32 `SendInput` atomic with another application's focus changes.
- Dispatch without visible verification is reported as dispatched. It never
  triggers automatic replay by typing. Clipboard restoration requires continued
  sequence ownership; a newer copy wins. Rich/custom clipboard content is left
  intact and typing is attempted instead, including when typing subsequently
  fails. Timeout can abandon only pre-mutation clipboard work; a committed native
  write remains owned until completion. One outstanding STA operation is allowed.
- Generic automatic undo in external applications is unavailable: Windows does
  not expose the edit-transaction identity needed to prove which action Ctrl+Z
  would reverse. Users retain the target application's own Undo command.
- Streaming dictation does not keep a full-session PCM buffer. Pending audio is
  limited to 4 MiB and 32 queued chunks with one transcription worker; completed
  output is limited to one million characters and 10,000 chunks. Crossing a limit
  reports failure instead of inserting an incomplete transcript. Cleanup cancels
  and awaits the worker; a provider that ignores cancellation must finish before
  the session releases its resources.
- Local history repairs missing final line boundaries before append, skips
  structurally/semantically invalid records, and preserves their original bytes
  through unrelated rewrites. Records are limited to 16 MiB when encoded; reads
  retain at most 32 MiB of encoded record data as well as their requested count
  limit. Search scans the entire file before limiting matching records. Presentation
  still limits a result page to 100 Workbench or 200 standalone history records.
- Worker stdout messages are limited to 16 Mi characters including any trailing
  CR. Oversize output terminates that worker; buffered read-ahead is preserved for
  the next message. Stderr is read in fixed chunks and retains only its last
  65,536 characters, including for output without a newline.
- Non-PDF document imports accept at most 64 MiB on disk and eight Mi characters
  of expanded input across all consumed archive entries, including repeat reads.
  Cancellation is checked during reads and between extraction stages. XML DTDs
  are refused and HTML/RTF regex work has a two-second timeout per operation.
- Publishing retains a returned video ID before fallible checkpoint drainage,
  writes the receipt independently of user cancellation, and never re-uploads
  a known completed episode. A receipt-save failure retains the job in memory and
  reports that the upload succeeded remotely. An attempted upload without a
  recoverable session requires checking YouTube before creating a new job.
  Abrupt process loss plus failed local storage can still require manual remote
  reconciliation; no atomic transaction spans the local journal and YouTube.

## Verification

- First run does not offer profile or experience-mode choices.
- Current settings serialization contains none of `activeProfileId`,
  `experienceMode`, `profiles`, `voiceSnippets`, or `appProfileRules`.
- Migration tests prove supported old files rewrite once to schema 20 and future
  files remain byte-for-byte untouched.
- Pipeline tests prove spoken-command enablement comes from the current scalar
  setting and voice snippets remain unavailable.
- Packaging validation proves only App and UIAccessHelper occupy production
  executable roles.
- Privacy guardrails protect the local plaintext history boundary and exclude
  raw audio, history, credentials, and diagnostics from release payloads.
