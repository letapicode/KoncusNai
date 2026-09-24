# Threat Model

## Scope
- Windows 10/11 local desktop application.
- Local audio capture, local offline transcription, global text insertion, local LLM chat assistance, Reading Studio / TTS synthesis, and user-initiated video publishing.

## Critical Assets
- Microphone audio samples in memory and transient capture buffers.
- Transcript text in memory and transient insertion buffers.
- Dictation history (`dictation-history.local.jsonl`) and chat history (`chat-history.local.jsonl`) stored on disk.
- Reading Studio generated media assets and synthesized audio cache (`%LOCALAPPDATA%\DictateAnywhere\`).
- Model binaries, checkpoints, runtime executables, and integrity manifests.
- User configuration settings (`settings.json`).
- Diagnostics logs (`%LOCALAPPDATA%\DictateAnywhere\logs\*.log`) and diagnostic export bundles (`.zip`).
- YouTube publishing credentials (OAuth client secrets and refresh tokens stored in user application data).

## History Storage & Local Persistence Guarantees
- **Local Plaintext Model**: Dictation and chat histories are stored as local plaintext JSON Lines (`.local.jsonl`) in `%LOCALAPPDATA%\DictateAnywhere\history\`.
- **Operating System Protection**: History is protected by Windows OS per-user filesystem Access Control Lists (DACLs), which restrict access to the current authenticated Windows user account and local administrators. The application does not use custom application-level encryption.
- **Rationale**: Plaintext storage provides transparent user ownership, allows standard Windows indexing/backup tools to function, and prevents vendor lock-in or data loss from lost application encryption keys.
- **User Control & Deletion**: Users can delete individual records, purge sessions, or clear history at any time. Mutations rewrite and truncate storage files immediately.
- **Diagnostic Exclusion**: Raw history files (`*.local.jsonl`) and audio recordings are strictly excluded from diagnostics logs and support export bundles.

## Attack Surfaces & Security Controls

### 1. Model & Runtime Tool Provisioning
- **Risk**: Tampered model payloads, corrupted runtimes, or unverified network endpoints.
- **Controls**:
  - Remote provisioning uses HTTPS and the hosts recorded in `docs/security/supply-chain-provenance.json`; URI user-info, insecure transport, mutable identities where hashes are required, and unapproved redirect hosts fail compliance.
  - Managed transcription snapshots use full repository commits and exact file membership, size, Git-LFS-pointer rejection, and SHA-256 verification before atomic promotion. Existing installed snapshots are restored if promotion fails.
  - GGUF, llama.cpp, and FFmpeg artifacts are verified against pinned SHA-256 values before activation or extraction. Archive membership is constrained and case-colliding/unsafe paths are rejected.
  - Ollama and the managed Python installer use explicit Authenticode publisher trust; the supported Ollama model tag must match its pinned registry digest.
  - NuGet uses complete checked-in locks in CI locked mode; Python provisioning uses exact hash-checked locks. CI actions use immutable upstream commit SHAs.
  - Downloads are strictly user-initiated; zero background network beacons or telemetry calls exist.

### 1a. Supply-chain credentials
- Provenance records credential names and allowed custody only, never values. `HF_TOKEN` may exist only in the Hugging Face user credential store or the provisioning process environment. OAuth and resumable publishing material remains Windows ProtectedData-bound. Signing and package-source credentials remain in operator/CI credential stores and are forbidden from manifests, command lines, logs, diagnostics, and evidence artifacts.

### 2. Injection APIs (`SendInput`, Clipboard Paste) & UIPI Boundaries
- **Risk**: Insertion into unintended or privileged windows (User Interface Privilege Isolation / UIPI bypass).
- **Controls**:
  - Foreground-window targeting checks before executing text insertion.
  - Standard application respects Windows UIPI boundaries; attempts to insert into elevated windows fail safely with user-facing guidance.
  - Optional UIAccess helper (`DictateAnywhere.UiAccessHelper.exe`) runs as a separately signed UIAccess process from a secure Program Files location.
  - The bridge launches only the configured child helper, passes non-sensitive method flags as arguments, sends Base64-encoded UTF-8 dictation text through redirected standard input, and receives one JSON response through redirected standard output. Raw text and its encoded form are not placed in the process command line.

### 3. Clipboard Handling
- **Risk**: Clipboard data loss, unauthorized clipboard disclosure, or clipboard logging.
- **Controls**:
  - Snapshot-and-restore path with bounded timeouts by default.
  - Robust fallback handling with explicit error classification.
  - Clipboard text contents are never logged or stored in diagnostics files.

### 4. Local Inference Loopback Communication
- **Risk**: A proxy, redirect, or different process listening on the local Ollama port could receive private chat and imported document text.
- **Controls**:
  - Ollama chat accepts only `http://127.0.0.1:11434/`; its HTTP client disables proxy use and redirects. Responses are limited to 16 MiB, and failed-response bodies are excluded from error messages.
  - Before sending chat, Koncus Nai checks the Windows owner of the IPv4 loopback listener. A process started directly by the app is approved for that process lifetime. An already running Ollama process requires an explicit, session-scoped approval showing its executable path and process ID. A model digest returned by `/api/tags` is model metadata, not proof of process identity.
  - Listener ownership is checked immediately before each chat request. There remains a check-to-connect race: a sufficiently privileged local process could replace the listener between the ownership check and the TCP connection. Approval of an external process does not authenticate its binary or control what that process does with text it receives.
  - The app's llama.cpp server uses `http://127.0.0.1:8090/`; review its separate process and transport controls before extending the Ollama trust claim to it.

### 4a. Document Import and OCR
- **Risk**: Tiny malformed PDFs or compressed images can cause excessive parsing, rendering, memory use, or persistent temporary files.
- **Controls**:
  - Input files are limited to 64 MiB before parsing or OCR. PDFs are limited to 2,000 pages, 2,000-point page dimensions, 5,000 words and 100,000 extracted characters per page, and 8 Mi characters of total extracted text. Exceeding a limit fails the import with an explicit error; Reading Studio does not silently shorten the document.
  - The OCR worker checks image headers for a 40-megapixel ceiling before raster decoding or model initialization. PDF extraction checks cancellation between pages and during word extraction, and removes rendered page files after OCR, failure, or cancellation.

### 5. Diagnostics & Support Bundling
- **Risk**: Transcript, prompt, audio sample, or credential leakage in diagnostic logs or export bundles.
- **Controls**:
  - Sensitive diagnostics redactor active by default; scrubs prompts, transcripts, audio markers, authorization tokens (`Bearer`), and credential values.
  - Local file storage only (`%LOCALAPPDATA%\DictateAnywhere\logs\`); no remote logging or telemetry sink.
  - Rolling log files with bounded file sizes and retention limits.
  - Support bundle exporter (`DiagnosticsBundleExporter`) redacts all included log lines, recursively strips credential fields (`password`, `secret`, `token`, `apiKey`, `api_key`, `api-key`, `credential`, `verifier`, `salt`, `privateKey`, `authKey`) from `settings.json`, and strictly excludes raw history files and audio records.

### 6. Video Publishing (YouTube)
- **Risk**: Credential compromise or accidental upload of private media.
- **Controls**:
  - Publishing is strictly user-initiated through an explicit interactive window.
  - OAuth credentials and tokens are managed via the Google API client library in local user profile storage.
  - HTTPS-only transmission directly to Google APIs (`https://www.googleapis.com/`, `https://oauth2.googleapis.com/`).
  - Rendered video output link points to `https://youtu.be/...`.

## Out of Scope
- Protection against physical machine access or elevated administrative compromise (mitigated by Windows BitLocker and OS security).
- Enterprise DLP (Data Loss Prevention) / inventory policy agent integration.
- Hardware security module (HSM) / secure enclave custody for client credentials.
