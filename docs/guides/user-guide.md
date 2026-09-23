# Koncus Nai User Guide

## Scope
This guide covers installation, first use, daily dictation, Reading Studio narration, model choices, and troubleshooting for Windows 11 users.

## Requirements
- Windows 11 x64.
- Microphone input available and permitted in Windows privacy settings.
- A supported local transcription model prepared through Settings.

## Install
No supported public installer has been validated or published. The installer steps below describe the intended flow for a future release; they are not current public download instructions.

The installer candidate requires administrator approval on Windows 11 x64. Dictation only and Full assistant are first-run choices within the same installation, not separate packages. Settings can still open the workbench in Dictation only mode.

Model weights are downloaded separately when prepared. Current pinned dictation downloads are approximately 4.13 GB for Cohere, 1.62 GB for CrisperWhisper Turbo, or 3.09 GB for CrisperWhisper Large, plus runtimes and temporary storage. Prepared local models can run offline. Voice previews are generated and cached locally; user-generated narration and recordings stay local and should never be uploaded as release assets.

See the [current public-source audit](../../planning/public-source-pre-release-audit-2026-09-17.md) for the release decisions and installation limits. Source archives from GitHub require a build; use a tested installer from GitHub Releases when one is published.

When a validated installer is published, use its setup package:
- `KoncusNai-Setup-Small-<version>-x64.exe`
  - Includes the application and local runtime dependencies.
  - A model may be downloaded the first time its provider is prepared.

1. Run your selected setup EXE and complete installation.
2. Launch Koncus Nai from Start Menu.
3. Confirm the tray icon appears.

## First-Run Setup
1. Select a dictation hotkey.
2. Use **Hotkey Test** to confirm Windows detects your key combo.
3. Run benchmark and accept the recommended model.
4. Download the selected model and finish setup.

## Daily Dictation
The normal workflow uses toggle dictation:
1. Press hotkey once to start.
2. Press hotkey again to stop and insert.
3. Follow the bottom-center recording, transcribing, and completion indicator.

The first few dictations after opening the app may take longer than later ones, even for a short phrase. Koncus Nai checks readiness in the background, and a model or worker may need time to load. Open the tray menu to see the selected model's **Warming**, **Ready**, or **Failed** state. Do not judge steady-state speed from the first attempt alone: use the same model and similar speech length for several attempts after it reports **Ready**. A faster third attempt is possible but is not guaranteed; persistent delays should be investigated with the privacy-safe stage timings described in the [README](../../README.md#first-use-dictation-speed-and-timing).

Koncus Nai remembers the application where recording began and attempts to restore it before insertion. If an ordinary focus change prevents insertion, the transcript is copied to the clipboard and is also saved to local history. Secure or blocked destinations remain protected and are reported as errors.

## Koncus Nai Global Toggle Preset
Use this when you want the rollout configuration for cross-app dictation.

The default hotkey target is `Alt + Space` and the recording mode is toggle.

If `Alt + Space` is blocked by Windows or another app:
- Koncus Nai falls back to `Win + Alt + Space`.
- A tray notification reports the active fallback.

## Main Workspace
The main Koncus Nai window provides local chat, saved chat and dictation history, unified file import, and Reading Studio. Use the single plus button in the **Just Ask** composer: audio/video is transcribed into the composer, while supported documents and images are read locally and attached to your next message. The compact Settings menu contains theme, chat text size, chat model, and speech choices. The selected chat typeface applies to replies, the normal and expanded composer, and the animated request status. Text size uses a live 12-30 px slider and saves automatically.

## Settings Quick Reference
- Compact menu: theme, chat text size, chat model, and speech model.
- Advanced settings: dictation shortcut, microphone, transcription provider/model/language, spoken formatting commands, insertion safety, and chat typeface. **Manage Dictation History** opens the local history workspace directly.
- Settings save automatically. The recording/status overlay and its indicator are part of the standard dictation workflow rather than optional appearance settings.
- Chat typeface choices are System, Book Serif, Literary Serif, Modern Serif, Excalifont, and Kalam.

## Model Choice Guidance
- `Cohere Transcribe`:
  - Multilingual local transcription.
  - First download requires accepted Hugging Face access for `CohereLabs/cohere-transcribe-03-2026`.
  - Sign in for the Windows user with `hf auth login` or set `HF_TOKEN` before downloading. Koncus Nai never stores the token in its settings.
- `CrisperWhisper`:
  - Local transcription and Reading Studio forced alignment through a persistent Python worker.
  - Model preparation may download runtime/model assets once; later work uses the local cache.

Use benchmark recommendation when available.

## Reading Studio

Open Reading Studio from the tray or Textbox Workbench. A new session opens as an empty writing page with the controls visible on the left. Type or paste directly into the page, choose the language, voice, processing mode, and appearance, then select **Create Videobook**. Use the pencil icon beside playback to edit the source again. The document icon imports a supported file and the help icon explains every Reading Studio control.

The **Typeface** menu shows fonts compatible with the selected writing system. Latin languages keep the full style list, Devanagari languages include the bundled literary and hand-drawn fonts, and other scripts use Windows' multilingual font system. Changing language may therefore select a different safe default typeface. The same typeface and script spacing are used for the page, focused reading, and exported video.

- Use the top-left menu button to hide or restore the settings panel. The reading text stays aligned with the button row.
- Previous, Play/Pause, Next, and Fullscreen Reading Mode are grouped below the reading surface.
- The word count and section count share the same footer row as Previous, Play/Pause, and Next. The document title is not repeated in the reader chrome.
- Click or drag the timeline to seek directly. You can also click a displayed word or sentence.
- Word and sentence follow-along both honor the selected highlight style and focus color.
- When a DOCX or searchable PDF uses formatted story/chapter titles, Reading Studio preserves those titles as one scrollable section each. Maximizing or resizing the window changes line wrapping, not the number of sections.
- Audio, Video, and YouTube publishing actions are grouped at the bottom of the settings panel and use icon tooltips.
- Video export uses the selected theme, typeface, text size, focus color, follow-along mode, and highlight style.
- Clicking the pencil does not discard prepared narration by itself. Playback remains available until the text changes, and undoing all text changes restores the prepared controls.
- Story titles detected from DOCX/PDF formatting are narrated with a clear pause before the opening sentence.
- Sanskrit and other Devanagari reading layouts reserve extra height above each line so vowel signs remain visible at larger Text Size settings. Long Sanskrit passages are split at smaller sentence-aware boundaries for more reliable pronunciation.

Narrators remain provider-specific. The selected **Language** identifies its compatible engine, while the **Voice** list shows concise speaker names and qualities such as warm or expressive without repeating the engine name. English and Hindi choices from different engines do not overwrite one another. Use the small play button beside the narrator to hear its language-appropriate preview.

Indic Parler provides 21 official languages plus 3 clearly marked experimental languages, including Nepali with Amrita and Sanskrit with Aryan. Its first preview or narration automatically prepares the private runtime and downloads the model cache when necessary—there is no setup command for you to run. Because the upstream model is gated, you must first accept its Hugging Face access conditions and sign in for the current Windows account. Later narration works locally from the cache.

The default device mode is `auto`: Koncus Nai uses compatible CUDA/ROCm or Apple MPS acceleration when available and otherwise uses CPU. A 64-bit system with 16 GB RAM and no GPU receives the complete feature set.

## Privacy and Offline Behavior
- Dictation audio and transcription stay local.
- No cloud upload during dictation sessions.
- Network use is limited to model download/update actions.
- Narration and voice previews stay local. The first use of a model may use the network to provision its runtime and cache.
- Installation works offline. Dictation requires the selected provider and model to have been prepared on that Windows account before the machine goes offline.

## Troubleshooting
Hotkey does not trigger:
- Open Settings -> Hotkeys -> Hotkey Test.
- Choose a different combo if conflict is reported.
- Avoid relying on `Fn` if Windows does not detect it.

Text does not appear in elevated/admin app:
- This is expected without configured UIAccess path.
- Use non-admin target app or configure elevated insertion requirements.

Insertion blocked in password/secure field:
- Expected when secure-field detection is enabled.
- Use a non-secure input field.

High latency:
- Run benchmark again.
- Compare a cold run with repeated warm runs and select the faster supported provider/model only if its accuracy remains acceptable.

Text was transcribed but could not be inserted:
- Look for the bottom-center recovery message.
- Paste the recovery copy from the clipboard, or open the current day in Dictation History.
- Koncus Nai does not bypass secure fields, blocked applications, or Windows privilege boundaries.

Cohere model download fails with gated-repo or `HF_TOKEN` error:
- Open the model page for `CohereLabs/cohere-transcribe-03-2026` in Hugging Face and accept the access terms.
- Sign in for this Windows user with `hf auth login`, or set `HF_TOKEN` before launching Koncus Nai. The model downloader uses that user's Hugging Face credential store and keeps credentials outside the application data and source tree.
- Retry the download from Settings -> `Models`.

Indic Parler preview or narration reports a gated-model access error:
- Accept the access conditions for `ai4bharat/indic-parler-tts` on Hugging Face.
- Sign in to Hugging Face for this Windows account, then retry the same narrator preview or preparation. Koncus Nai handles the runtime and model setup automatically.

Koncus Nai opens in the tray instead of showing a main window:
- This is expected for the tray host. Click the tray icon to open the configured window.
- Use the tray menu's **Quit** command to close the app. If Koncus Nai started Ollama, Quit also stops that owned process; it does not stop an Ollama instance that was already running.

## Useful Paths
- Settings and local app data:
  - `%LocalAppData%\DictateAnywhere`
- Logs:
  - `%LocalAppData%\DictateAnywhere\logs`
- Models:
  - `%LocalAppData%\DictateAnywhere\models`
- Indic Parler isolated runtime:
  - `%LocalAppData%\DictateAnywhere\indic-parler-runtime`
- Indic Parler model cache:
  - `%LocalAppData%\DictateAnywhere\models\indic-parler-cache`
