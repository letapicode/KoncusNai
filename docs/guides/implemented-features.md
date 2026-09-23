# Implemented Feature Guide

## Purpose

This is the product-facing inventory of functionality currently implemented in Koncus Nai (also called Dictate Anywhere in older project and installer names). It is intended to let a new developer, tester, or product owner understand what is available, how it behaves, and the main boundaries to preserve when making changes.

Update this document whenever a user-visible feature, supported model, workflow, or material behavior changes. Do not list an idea or an unfinished experiment as implemented.

## Product principles

- **Local first.** Dictation, transcription, read-aloud, document extraction, and local chat run on the device. Network access is for downloading or updating models and optional publishing only.
- **Explicit work.** Narration preparation and video/publishing exports are initiated by the person using the app and show progress. An Indic narrator selection can automatically provision its isolated runtime and model on first use, without asking the reader to run commands.
- **Safe insertion.** The app avoids inserting into detected secure fields and uses a clipboard-first strategy with a Unicode typing fallback.
- **Readable recovery.** Long local operations report their phase, preserve useful prepared work when safe, and expose user-readable errors rather than failing silently.

## Dictation

### Global dictation

- Records microphone audio with toggle-to-talk behavior.
- Supports a global hotkey. The global-toggle preset prefers `Alt + Space` and falls back to `Win + Alt + Space` if Windows or another application blocks the preferred binding.
- Shows recording time, a letter-animated **Transcribing** state, and a brief green completion check in a compact non-focus-stealing pill at the bottom center of the monitor where dictation began. Recovery and error states remain readable text because they require action.
- Inserts completed text into the focused application. Clipboard paste is the default; Unicode typing is available as a fallback.
- Captures the insertion target when recording starts and tries to restore it after transcription. If an ordinary target change prevents insertion, the completed transcript is copied for recovery and saved to local history. Secure fields, blocked applications, and privilege boundaries remain separate protected failures.
- Can restore the clipboard after insertion and can block insertion into detected password or secure fields.
- Provides an optional UIAccess/elevated-insertion path for compatible elevated applications; normal Windows UIPI restrictions still apply where that path is unavailable.

### Textbox Workbench

- Provides a local textbox workflow for recording and transcription without global insertion.
- Includes recording controls, transcript editing, one composer-level file action, local status/diagnostics, and a dedicated chat workspace. Audio/video files are transcribed into the composer; supported documents and images are extracted locally and attached as bounded context for the next chat message.
- Supports saved local chat history, chat rename/delete, copy, and export actions. Selecting the current dictation-history day again reloads it after the composer has been cleared, and Advanced settings includes **Manage Dictation History**.
- Applies the selected System, Book Serif, Literary Serif, Modern Serif, Excalifont, or Kalam chat typeface to replies, the composer, its expanded editor, and request-progress text.
- Sentence-cases automatically generated chat-history titles while preserving deliberate mixed-case names and manually entered titles.
- Uses a continuous 12-30 px chat text-size slider with live preview and persisted integer values.
- Refreshes the open dictation-history sidebar and standalone History window immediately after a global dictation is stored.
- Uses a compact settings menu with one-click light/dark mode, an Advanced settings entry, model readiness, and the current transcription provider shown without internal release identifiers.

### Dictation formatting

- Supports optional deterministic spoken formatting commands.
- Does not expose dictation profiles, voice snippets, per-application routing, experience modes, or local Gemma transcript rewriting.

## Speech transcription and models

- Supports local Cohere Transcribe and CrisperWhisper models, including provider-isolated download, activation, deletion, readiness, benchmarking, and recommendation.
- Supports the local Cohere Transcribe model for multilingual transcription when its Hugging Face access requirements are met.
- Stores model language support and resolves a compatible language deterministically when the selected model does not support a requested language.
- Uses speech chunking and silence-aware boundaries for long recordings, then combines transcript chunks in order.
- Includes local diagnostics, error categorization, compatibility checks, and offline acceptance coverage.

## Reading Studio

### Sources and import

- Opens new sessions as a blank, focused, scrollable writing page inside the normal Reading Studio shell. The left settings panel remains visible while the user types or pastes directly into the page.
- Uses **Create Videobook** as the explicit draft-to-reading transition. It builds the reading document, selects the complete generated range, switches to the formatted reader, and starts the selected preparation mode. A pencil icon returns the complete source to the editable page.
- Imports EPUB, DOCX, TXT/Markdown/CSV/JSON/XML, HTML, RTF, supported image formats, and PDF files through a borderless document icon with hover and accessibility text. The adjacent help action uses the same direct-on-surface treatment.
- Extracts EPUB chapters in spine order, DOCX paragraphs and heading structure, plain text, HTML text, and RTF text locally.
- Extracts searchable PDF text locally. It reconstructs readable lines from word geometry, normalizes PDF ligatures, and uses bold-font metadata to preserve story or chapter titles instead of flattening them into body text. If a text layer has collapsed word boundaries, geometric extraction restores readable spacing.
- Renders scanned PDF pages and uses local RapidOCR only for pages without meaningful searchable text.
- Uses local OCR for imported images.

### Reading and navigation

- Uses detected DOCX headings or strongly emphasized title paragraphs and PDF bold-title lines as semantic sections. Documents without trustworthy headings fall back to fixed text-based sections that preserve paragraph rhythm and sentence boundaries.
- Keeps imported section counts independent of window size, DPI, maximize/restore state, reader font size, and page dimensions. Resizing changes only visual wrapping; it never silently rebuilds the document.
- Lets the user choose a start and end section, then prepare either the current section or the entire selected range.
- Supports full-page, one-sentence, and one-word reading views.
- Provides previous/next section navigation, direct seeking by timeline or by clicking a word/sentence, document progress, and distraction-free full-screen reading.
- Keeps the word and section count on the same centered footer row as the transport controls. The document title is intentionally omitted from the reader chrome.
- The settings panel can be hidden with the top-left menu button. A dedicated title-bar row keeps the editor and reader clear of the window controls at restored, narrow, and maximized sizes; distraction-free mode removes that reserved space. The fullscreen control sits with the Previous, Play/Pause, and Next transport controls.
- Uses the native Windows maximize and restore glyphs with layout rounding so the custom title-bar controls remain crisp across DPI and window-state changes.
- Prefetches the next section when appropriate so the next prepared section can start sooner.
- Includes a dedicated, resizable Reading Studio help window explaining writing, import/OCR, language and voice, processing modes, focus controls, playback, export, publishing, privacy, and first-use model setup.

### Narration and timing

- Creates local text-to-speech narration and aligns displayed words to the generated audio.
- Uses trusted native word timings for Kokoro and clearly labels its deterministic fallback when native tokens cannot cover the display text. Indic Parler uses local, audio-grounded forced alignment rather than its provider-generated duration estimate.
- Keeps narrator providers explicit on language choices so same-language engines do not conflict. Once a language is selected, the compatible Voice list uses concise names and tone descriptions rather than repeating the provider on every voice.
- Includes the complete Kokoro-82M catalog of 54 voices across its 9 language variants, including distinct Kokoro English and Hindi choices.
- Includes AI4Bharat Indic Parler narration for 21 officially supported languages and 3 clearly labeled experimental languages. Nepali defaults to Amrita and Sanskrit defaults to Aryan.
- Provides a play/stop preview beside the selected narrator. Preview text is language-appropriate, exercises varied speech sounds and punctuation, and generated previews are cached by provider, language, and voice.
- Indic Parler is CPU-first and runs fully on the 16 GB CPU baseline. In `auto` mode it uses a compatible CUDA/ROCm or Apple MPS accelerator when available, with conservative precision selection and one safe CPU retry after an accelerator out-of-memory failure.
- Indic Parler keeps one persistent model instance per process and serializes generation. It never maintains simultaneous CPU and GPU model copies.
- Synthesis status and metadata report device, backend, data type, GPU name when applicable, fallback details, generation time, audio duration, real-time factor, and reliable peak-memory measurements when available.
- On first Indic Parler use, Koncus Nai automatically provisions an isolated signed Python runtime and pinned dependencies when needed, then downloads the gated model into its local cache. Hugging Face access acceptance and sign-in are still required by the model owner.
- Shows preparation phases, progress, elapsed time, completion notification, and cancellation/error states.
- Replays a completed prepared section reliably by resetting the media player before replay.
- Uses Kokoro's model-native token timestamps directly when they cover the display text. This avoids a separate long-running alignment pass and improves word-following accuracy.
- Gives a detected story or chapter title an explicit narration boundary so the title and opening sentence do not run together.
- Opening the pencil editor is non-destructive: prepared playback remains available until the source text actually changes. Returning the text to its original value restores that prepared state, and an unchanged draft never asks for another videobook build.
- Rebuilds draft section choices after a short typing pause, including known and newly detected heading paragraphs, so Start Section and End Section do not retain stale titles.
- Uses smaller sentence-aware synthesis chunks for Sanskrit and other Devanagari Indic narration. Provider duration estimates may be retained in the speech cache, but Reading Studio does not present them as audio-grounded word alignment.

### Reader appearance

- Supports reader themes, typefaces, text size, playback speed, focus colors, and word/sentence/off follow-along modes.
- Presents eight distinct premium focus colors as direct circular choices in two rows of four, and four reading themes as direct circular choices in one row. Hover and automation text expose each color or theme name without surrounding card-like buttons.
- Includes highlight styles: Reader page, Focus type, Soft underline, Focus fill, Kinetic bold, Focus ring, and Spotlight.
- Highlight style and focus color apply in both word and sentence follow-along modes.
- Focused-word mode renders the word through the same highlight pipeline as the full-page and focused-sentence modes, so every focus style and color remains visible while paused and during playback.
- Selecting **Reader page** uses a complete page layout. Selecting **Kinetic bold** or **Focus fill** selects a focused-sentence layout; the user can still choose a different reading view afterward.
- Appearance-only controls (theme, font, text size, reading view, highlighting, and speed) do not invalidate prepared narration or playback.
- Changing narration language or voice, reading range, or document content invalidates prepared narration. The user must use **Create Videobook** again; narration is never silently regenerated just because a setting changed.
- Devanagari layouts include additional top padding and line height in the editor, full-page reader, focused sentence, and focused word views so upper vowel signs and marks remain visible at large text sizes.

### Reader export and publishing

- Exports prepared narration as audio.
- Exports synchronized MP4 reading videos in 16:9 landscape and 9:16 YouTube Short formats.
- Video export reuses the selected reader theme, typeface, text size, focus color, follow-along mode, and highlight style. Highlight updates change paint without changing glyph metrics, so inactive words stay fixed from frame to frame. Reader page uses page video, Kinetic Bold uses stable phrase captions, and Focus Fill uses the rounded focus treatment.
- Supports creating/publishing a YouTube reading series through the dedicated publishing workflow. See `docs/youtube-publishing.md` for setup and operational details.
- Reading Studio chrome and the YouTube publishing workflow use the shared application light/dark palette for surfaces, labels, inputs, dropdowns, buttons, window controls, and scrollbars. Reading themes remain scoped to the book page and exported reading media.
- The application light theme uses a warm neutral surface scale instead of pure white. Reading Studio and YouTube Publishing use rounded window chrome, and publishing text, password, date, and selection fields share consistent rounded templates.

## Local chat

- Provides local-chat model selection, readiness status, setup/download controls, request timing, history, and friendly failure messages.
- Supports an Ollama-backed Gemma 4 E4B model and llama.cpp-backed local GGUF models, including the bundled Gemma 3 4B option.
- Does **not** start Ollama when Koncus Nai opens or when Reading Studio is used.
- Removes only a verified Ollama shortcut from the current user's Windows Startup folder so the installer cannot silently restore sign-in launch. Unrelated startup entries are never touched.
- The **Start / set up Ollama** action starts an installed Ollama service only after the user asks to use it. It downloads Gemma 4 only when the model is missing. Ollama loads the model for the first chat request, so that first response can take longer.
- Choosing **Quit** from the tray closes Koncus Nai and stops an Ollama process only when that process was started and ownership-tracked by Koncus Nai. An Ollama instance that was already running is left alone.
- Local Gemma chat and refinement can require Python runtime dependencies and installed model files; the UI provides readiness and repair/update guidance.

## Settings, privacy, diagnostics, and packaging

- Persists current application settings with migrations, including hotkeys, model selection, global transcription language, spoken formatting commands, local provider choices, chat typeface, and chat text size. Changes save automatically. Retired profile and experience fields are read only during upgrades and are not written to the current schema.
- Saves dictation and chat history locally without an application password or automatic entry limit; destructive confirmations retain the shared red danger treatment.
- Keeps audio, document text, OCR, local narration, and local model inference on-device. It does not upload dictated or imported text as part of normal local workflows.
- Keeps structured local diagnostics and avoids persisting raw audio/transcript content in logs by default.
- Provides a Windows MSI and setup EXE. Provider models are prepared separately and are not bundled into the installer.
- Uses a deterministic minimalist `N` application mark across the executable, window/About resources, launcher, tray, installer, and size-specific PNG assets.
- Enforces a native minimum tracking size for the main chat window, including custom Windows maximize handling, so it cannot be collapsed into an unusable strip.
- Includes build, release, compatibility, performance, fault-injection, offline, multilingual, and browser-surface acceptance scripts and reports.

## Important operating boundaries

| Situation | Expected behavior |
| --- | --- |
| User changes a visual reader setting after preparing narration | Prepared audio remains playable; no new narration is created. |
| User changes reader language or voice after preparing narration | Prepared audio is discarded because it no longer matches the narration choice; the user explicitly prepares again. |
| User imports a scan/image or non-searchable PDF page | Local OCR is used for that page. |
| User imports a searchable PDF with words run together | Geometric PDF word extraction is used to restore word spacing when reliable. |
| User imports a DOCX/PDF story collection with detectable formatted titles | Each title becomes one stable, scrollable reader section; resizing the window does not change the section count. |
| User opens Koncus Nai for dictation or reading | Ollama is not started or loaded. |
| User chooses to set up Ollama/Gemma 4 | Ollama is started only then; the model downloads only if absent and loads on the first chat request. |
| User quits Koncus Nai from the tray | Koncus Nai closes and stops only an ownership-tracked Ollama process that Koncus Nai started; a pre-existing Ollama process remains running. |
| User selects an Indic Parler narrator for the first time | Koncus Nai provisions the isolated runtime and model cache automatically, reports progress, and continues the requested preview or narration. Gated-model access may still require the user to accept terms or sign in. |
| User forces an unavailable TTS device or unsafe precision | The request fails with a clear error. Automatic CPU fallback is reserved for `TTS_DEVICE=auto`. |
| User selects another local model that is not ready | The UI reports readiness/setup rather than performing an unrelated invisible download or runtime change. |

## Related documentation

- User setup and troubleshooting: `docs/guides/user-guide.md`
- Developer workflow: `docs/guides/developer-guide.md`
- System architecture: `docs/architecture/system-architecture.md`
- Local provider architecture: `docs/architecture/local-model-provider-stack.md`
- Reader model notices: `docs/THIRD_PARTY_READER_MODELS.md`
- Indic Parler setup, devices, languages, CLI, and validation: `docs/guides/indic-parler-tts.md`
- Sanskrit evaluation boundaries: `docs/sanskrit-tts-evaluation.md`
- YouTube publishing: `docs/youtube-publishing.md`
- Known limitations: `docs/known-limitations/known-issues-and-mitigations.md`
