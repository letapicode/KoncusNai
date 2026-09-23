# Notype Cache Inventory & Key Identity Matrix (CACHE-001)

## Purpose & Scope
This document provides the canonical inventory and typed identity matrix for all local caching systems in Notype. Under backlog item `CACHE-001` (Work Package `WP-19`), each real owner maintains concrete domain models and explicit lifecycles without an artificial generic caching framework.

---

## 1. Cache Inventory & Identity Matrix

| # | Cache Name | Concrete Owner | Typed Cache Key | Output-Affecting Inputs (Must Miss) | Non-Affecting / Appearance-Only Inputs (Must Hit) | Storage Medium & Location | Eviction & Corruption Recovery Policy |
| :- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **1** | **Narration Audio** | `ReadingAudioCache` (`ReaderNarrationSession`) | `ReaderSpeechCacheKey` | `SectionIndex`, `NarrationText`, `LanguageCode`, `ProviderId`, `VoiceId`, `Description` | Font family, font size, theme, highlight mode, highlight style, highlight color, text direction presentation, playback speed | In-memory dictionary (`Dictionary<ReaderSpeechCacheKey, TextToSpeechResult>`); referenced WAV files on local disk | **Detection**: Missing file or length <= 44 bytes (truncated WAV).<br>**Recovery**: Evict key from dictionary and re-synthesize.<br>**Eviction**: Session reset / document reload / text edit. Preserved across range adjustments. |
| **2** | **Word Timing** | `ReaderNarrationSession` | `ReaderTimingCacheKey` | `Speech` (`ReaderSpeechCacheKey`), `AudioPath`, `Duration`, `ReaderWordTimingStrategy` | UI highlight rendering state, viewer theme, current playback position | In-memory dictionary (`Dictionary<ReaderTimingCacheKey, ReaderWordTimingMap>`) | **Detection**: Underlying audio file deleted or truncated (length <= 44 bytes).<br>**Recovery**: Evict timing key and re-align / re-resolve.<br>**Eviction**: Cleared in lockstep with narration cache via `Clear()`. |
| **3** | **Voice Preview** | `ReaderVoicePreviewCache` / `ReaderVoicePreviewAssetStore` | Versioned file identity: `ProviderId`, `LanguageCode`, `VoiceId`, `SampleTextHash`, `SchemaVersion` | `ProviderId`, `LanguageCode`, `VoiceId`, `SampleText`, `SchemaVersion` | UI preview button state, player volume, window focus | Local disk in `%LOCALAPPDATA%\DictateAnywhere\voice-previews` | **Detection**: Missing file, length < 44 bytes, or invalid `RIFF...WAVE` header.<br>**Recovery**: Delete corrupted file and re-synthesize into atomic `.tmp` file before renaming.<br>**Eviction**: Automatic cache-miss on sample text change. |
| **4** | **Scanned Page / OCR** | `ReadableDocumentTextExtractor` | Per-page processing context | `PdfStream` content, `PageIndex`, `LanguageCode` | Viewer typography, zoom, font | Transient local disk: `%TEMP%\notype-ocr-<guid>` | **Policy**: Strictly transient. Privacy requirement: no documents or images are retained.<br>**Recovery**: Ensured cleanup in `try ... finally { TryDeleteDirectory(...) }`. |
| **5** | **Model Snapshot** | `HuggingFaceSnapshotModelManager` | Repository model definition: `providerId`, `entry.ModelId`, `entry.RepositoryId`, `Revision`, `AllowPatterns` | Model ID, repository ID, revision, required file patterns | Active model selection, UI download progress | Local disk in `%LOCALAPPDATA%\DictateAnywhere\models\<provider>\<modelId>` | **Detection**: Missing directory, missing `config.json`, or 0-byte `config.json` / auxiliary files.<br>**Recovery**: Returns `IsInstalled == false`. Download uses a `.partial` directory; promotion temporarily renames an installed snapshot and restores it if the new directory move fails. Stale partials are deleted on failure or retry. |
| **6** | **Prepared Playback** | `ReaderPreparationController` / `ReaderPlaybackSession` | `ReaderPreparationRequest` | `Kind`, `SelectedSections`, `CurrentSectionOffset`, `NarrationProfile` | Theme, typography, font family, font size, highlight style, highlight color | In-memory session state | **Detection**: Stale request generation or cancelled token.<br>**Recovery**: Stale results discarded; UI remains in previous consistent state.<br>**Eviction**: Invalidate on range change or profile change. |
| **7** | **Last Dictation Session** | `LastDictationSessionCache` | `SessionId` / `EntryId` | `SessionId`, `EntryId`, `CreatedUtc` | Overlay display mode, window active state | In-memory static cache with synchronization lock | **Detection**: Expiry past `maxAge`.<br>**Recovery**: Returns `false` and yields null.<br>**Eviction**: Explicit `Clear()`, confirmed deletion match via `ClearIfMatches(sessionIds)`. |

---

## 2. Atomic Writes & Fault Invariants

1. **Voice Preview Audio**:
   - Written to a temporary file (`{path}.{Guid:N}.tmp`).
   - Atomically moved to final destination via `File.Move(temporaryPath, path, overwrite: true)`.
   - Temporary file deleted in `finally` block if an error or cancellation occurs.
2. **Model Snapshot Storage**:
   - Downloaded to `{destinationPath}.partial`.
   - The download process must complete before promotion; installed-state checks reject missing or empty required files.
   - An existing destination is renamed to a unique sibling backup, the partial directory is moved into place, and the prior snapshot is restored if that move fails.
   - Partial directories are deleted on failure; successful promotion removes the prior backup.
3. **Active Model State Pointer**:
   - Written to `{statePath}.tmp`.
   - Atomically replaced via `File.Move(tempPath, statePath, overwrite: true)`.

---

## 3. Parameterized Test Verification Matrix

1. **Content-Affecting Miss**:
   - Modifying section text, narration profile (language, voice), or timing strategy causes a cache MISS and triggers generation.
2. **Appearance-Only Hit**:
   - Changing font, font size, theme, highlight style, highlight color, or text direction presentation preserves cached speech and timing without re-synthesizing.
3. **Range Adjustment Preservation**:
   - Changing the selected section range in Reading Studio invalidates playback preparation bounds, but preserves already generated section audio in `ReadingAudioCache`.
4. **Corruption Recovery**:
   - 0-byte or truncated audio files in `ReadingAudioCache` and `ReaderVoicePreviewCache` are evicted and safely re-synthesized.
   - Deleted audio referenced by `timingCache` is detected and evicted.
   - 0-byte `config.json` in model directories is detected as not installed.
   - Failed snapshot promotion restores the previously installed directory; successful replacement removes the backup.
