# Local Processing Guarantees

## Product Guarantees
- **Local Audio Capture & Processing**: Dictation audio is captured and processed locally on the device.
- **Offline Transcription**: Speech-to-text transcription is executed exclusively by selected local CrisperWhisper or Cohere workers.
- **Offline Chat & Rewriting**: Koncus Nai sends chat and rewriting requests to local inference backends (Ollama or llama.cpp over localhost loopback).
- **Offline Reading Studio**: TTS synthesis is executed locally through the Kokoro or Indic Parler worker selected for the narration profile.
- **Zero Telemetry**: No telemetry, analytics, or background crash reporting beacons are included or transmitted.
- **Zero Cloud Leakage from Koncus Nai**: The app does not upload dictation audio, transcript text, prompts, or completions to a remote or cloud service. An approved external Ollama process is outside the app's control; see the trust limits below.
- **Offline Reliability**: Dictation, chat, and editing sessions do not require internet access and function fully offline once models are downloaded.

## History Storage Guarantees
- **Local Plaintext Storage**: Dictation history (`dictation-history.local.jsonl`) and chat history (`chat-history.local.jsonl`) are stored as local plaintext JSON Lines in `%LOCALAPPDATA%\DictateAnywhere\history\`.
- **Operating System Protection**: Access is protected by Windows OS per-user filesystem Access Control Lists (DACLs), granting access only to the current Windows user and administrators. The application does not use custom application-level encryption.
- **User Ownership & Control**: Users have full control over history records. Individual entries or entire sessions can be permanently deleted at any time, immediately purging them from disk.
- **No Cloud Replication**: History records are never synchronized or backed up to cloud servers.

## Allowed Network Usage
Network activity is strictly restricted to user-initiated flows on an explicit allowlist:
1. **Model Downloads (`huggingface.co`)**:
   - Triggered when the user initiates a model download or update in Settings, or asks Reading Studio to use an optional narration model that is not installed yet. The UI must disclose that first-use download before the request continues.
   - Managed transcription downloads use immutable revisions and exact file hashes before staged atomic promotion. Other local worker model requests are bound to full immutable revisions and the Hugging Face content-addressed cache.
   - Gated models use the current Windows user's standard Hugging Face credential store or a process-provided `HF_TOKEN`; tokens are not stored in Koncus Nai settings.
2. **Llama.cpp Runtime Setup (`github.com`)**:
   - Triggered when the user configures or installs the llama.cpp chat provider runtime.
   - Fetches one pinned Windows x64 release archive and verifies its SHA-256 before safe staged extraction and promotion.
3. **Ollama Runtime Installer (`ollama.com`)**:
   - Triggered when the user initiates Ollama installation from the runtime readiness panel.
   - Requires a valid Ollama Inc. Authenticode signature; the supported model tag must match the recorded registry digest.
4. **Media Engine Runtime (`www.gyan.dev`)**:
   - Triggered when the user downloads the FFmpeg essentials package for Reading Studio video rendering.
   - Uses an exact versioned archive and verifies SHA-256 before extracting the single expected executable.
5. **Video Publishing (`googleapis.com`, `youtu.be`)**:
   - Triggered only when the user explicitly clicks Publish to YouTube in the Reading Studio.
   - Communicates with Google OAuth (`oauth2.googleapis.com`) and YouTube Data API v3 (`www.googleapis.com`).
6. **Localhost Loopback HTTP (`127.0.0.1`)**:
   - Inter-process communication with Ollama daemon (`http://127.0.0.1:11434/`) and llama.cpp server (`http://127.0.0.1:8090/`).
   - Koncus Nai connects to the literal loopback endpoint. Ollama chat disables HTTP redirects and proxies, and requires review of an externally started listener before private chat text is sent. A locally running process may have its own networking behavior; see `docs/security/threat-model.md` for the process-identity race and approval limits.

## Logging & Diagnostic Bundle Guarantees
- **Local Logging Only**: Diagnostic logs are written to `%LOCALAPPDATA%\DictateAnywhere\logs\` and are never transmitted automatically.
- **Automated Redaction**: Prompts, transcripts, audio payloads, and authorization credentials (`Bearer`, API keys, passwords, secrets) are redacted by default.
- **Support Export Bundles**:
  - Raw history files (`*.local.jsonl`) and audio recordings (`*.wav`, `*.mp3`, `*.mp4`) are strictly excluded from diagnostics bundles.
  - All log entries included in bundles are scrubbed of sensitive data.
  - Exported settings files recursively strip all credential material (`password`, `secret`, `token`, `apiKey`, `api_key`, `api-key`, `credential`, `verifier`, `salt`, `privateKey`, `authKey`).

## UI Copy Requirements
- First-run wizard and Settings UI must explicitly state that dictation, transcription, and chat run locally and offline.
- Provider selection UI must show operational status badges and privacy summaries (`local-offline`).
- Insertion settings must clarify that elevated/admin targets require UIAccess or manual elevated execution due to Windows security policy.
