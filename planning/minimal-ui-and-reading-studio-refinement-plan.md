# Minimal UI and Reading Studio refinement plan

> **Historical record:** This dated implementation record preserves the evidence
> available at that time; it is not current-head evidence or a current product
> contract. Use [`docs/documentation-map.md`](../docs/documentation-map.md) for
> current owners.

**Status:** Implemented and verified on 2026-08-28. The Release solution builds with zero warnings, 523 tests pass, two hardware-dependent smoke tests are skipped, and the updated Workbench, Settings, About, and Reading Studio were visually inspected in the running WPF application.

## Goal

Make Notype quieter when idle, reduce redundant labels and disclosure in the main settings menu, turn Reading Studio into a write-first experience, and make About a useful product overview. Preserve the local-first architecture, existing model/runtime boundaries, keyboard accessibility, and testability.

## Confirmed findings

1. Notype does not currently start Ollama from a passive readiness check. `CheckAsync` only calls the local HTTP endpoint.
2. The official Ollama installer creates `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Ollama.lnk`. That shortcut launches Ollama at Windows sign-in independently of Notype. It is the reason Ollama can appear while Notype and the Ollama-backed chat model are idle.
3. The Settings gear menu currently repeats a product header (`Notype` / `Local workspace`), nests a `Settings` item under the Settings gear, expands a two-choice Theme submenu, and exposes the full Cohere model release name.
4. Reading Studio currently opens with instructional placeholder content and requires a separate Paste or type button. Its sidebar also spends vertical space on redundant section headings, divider lines, combo boxes for binary or visual choices, and provider names on voices after the provider is already implied by the selected language.
5. The About dialog is built programmatically inside the workbench code-behind, is fixed at 560 × 520, and no longer describes the current feature set.

## Implementation sequence

### 1. Keep Ollama on demand

- Introduce a narrowly scoped Windows startup-shortcut policy for the Notype-managed Ollama runtime.
- Resolve only the current user's exact `Ollama.lnk` startup entry and verify its shortcut target is an Ollama executable before removing it. Never remove unrelated startup entries and never stop an Ollama process Notype did not start.
- Apply the policy after Notype installs/updates Ollama and during Notype startup so existing managed installations are repaired.
- Preserve the existing process-ownership marker: Notype may stop only the Ollama process it explicitly launched.
- Add unit coverage for path/target classification and the on-demand policy boundary.
- Remove the currently confirmed Ollama startup shortcut on this development machine after the code and tests are in place.

Acceptance:

- Launching or signing into Windows does not start Ollama merely because Notype once installed it.
- Opening Notype, opening the model menu, or polling readiness does not start Ollama.
- Explicitly downloading/selecting the Ollama-backed model can start Ollama.
- Quitting Notype still stops only a Notype-owned Ollama process.

### 2. Simplify the main Settings gear menu

- Remove the `Notype / Local workspace` header.
- Rename the nested `Settings` action to `Advanced`, retaining the gear icon.
- Replace the expandable Theme submenu with one direct light/dark toggle button using a sun/moon glyph, an accessible name, and a tooltip that explains the next action.
- Rename `Speech model` to `Transcribe` and present the active Cohere selection as `Cohere`, while retaining detailed model IDs internally for persistence and runtime selection.
- Change the welcome line to `Let’s get it done already.`

Acceptance:

- The menu contains no redundant workspace subtitle or nested `Settings` label.
- Theme changes with one click and remains keyboard/automation accessible.
- The selected transcription provider is readable without displaying the release-stamped model name.

### 3. Align the full Settings surface

- Replace the General > Appearance theme combo box with the same explicit light/dark switch semantics used by the gear menu.
- Rename the transcription card heading to `Transcribe` and use concise provider/model presentation where there is only one Cohere choice.
- Keep provider/model IDs and model-management operations unchanged.

Acceptance:

- Both Settings entry points use the same light/dark mental model.
- Existing saved theme and transcription settings continue to load and save without schema changes.

### 4. Make Reading Studio write first

- Add an explicit new-reading entry path that opens the existing scrollable editor immediately, maximized, with an empty body and focused caret.
- Remove the Paste or type source button. Keep document import as the alternate source path.
- Change the editor completion action to transition into the reading workspace; do not synthesize or prepare audio merely because text was entered.
- Keep the editor document-like: multiline, wrapping, vertically scrollable, generous page typography, and keyboard focus on open.

Acceptance:

- Clicking Reading Studio lands in an empty, focused writing surface.
- Pasting or typing long text scrolls naturally.
- Confirming text builds reading sections and reveals the reading view.
- Opening/importing an existing document still works.

### 5. Simplify Reading Studio controls

- Move `Reading Studio` into the top chrome row so it aligns vertically with the hamburger button and uses title case.
- Remove redundant headings: `Reading range`, `Narration`, `Preparation`, and `Reading experience`.
- Rename the range fields to `Start section` and `End section` (accurate to the internal document model).
- Keep only `Language` and `Voice` for speech selection.
- Replace the two-option processing combo box with a toggle/segmented switch and explain the modes through hover text. Remove the persistent small-print preparation description.
- Rename `Prepare selected range` to `Create videobook`.
- Replace Focus color with six directly clickable swatches in two rows, each with a visible selection state, tooltip, and accessible name.
- Replace Reading theme with directly clickable visual theme swatches, each previewing page/ink colors and exposing its name on hover.
- Remove category divider lines and use spacing/group proximity instead.
- Change `EXPORT & PUBLISH` to `Export & Publish`.

Acceptance:

- The sidebar is materially shorter without losing functionality.
- Binary and visual options no longer require opening combo boxes.
- Every swatch and toggle remains keyboard focusable and has an automation name.
- Existing preparation, playback, export, and publishing behavior remains intact.

### 6. Improve language and voice presentation/defaults

- Default Reading Studio to `English · United States · Kokoro` and `Bella` (female).
- Keep the provider on the language label because selecting a language/provider pair determines the compatible voices.
- Remove `Kokoro` and `Indic Parler` suffixes from voice labels.
- Add concise voice-character descriptors such as warm, expressive, calm, bright, or grounded directly to voice choices; retain deeper notes for tooltips where useful.
- Keep comfortable defaults for long-form reading: book serif, warm paper, word-on-page focus, reader-page highlight style, warm-gold focus, 23 pt text, and 1.00× speed.

Acceptance:

- A new Reading Studio opens with US English and Bella selected.
- Voice choices describe how they sound without repeating the engine name.
- Language/provider compatibility and request provider IDs remain unchanged.

### 7. Replace and expand About

- Move About into a dedicated XAML window so content/layout are maintainable and testable.
- Increase the default and minimum size, allow resizing, and provide a scrollable body.
- Cover: local-first privacy, global dictation, Workbench chat/history/import, transcription providers, profiles and cleanup, Reading Studio sources/OCR, Kokoro and Indic narration, focus modes and accessibility, audio/video export, YouTube publishing, runtime behavior, and where settings/data live.
- Display the running assembly version rather than hard-coding a version string.
- Use concise sections/cards so “thorough” does not become a wall of text.

Acceptance:

- About opens at a useful reading size and can be resized.
- Its content matches implemented features and avoids claims not backed by the repository.
- Workbench code-behind no longer owns the About layout.

### 8. Verification and maintainability gates

- Update focused WPF initialization/presentation tests for renamed/replaced controls and defaults.
- Add tests around the Ollama startup shortcut policy and voice catalog presentation.
- Run the App and Core/Inference tests affected by the changes, then run the full solution test suite if focused tests pass.
- Build in Release to catch XAML compiler, analyzer, and warnings-as-errors failures.
- Launch the built WPF app and visually inspect the gear menu, full Settings, About, new-reading editor, and populated Reading Studio at normal desktop scale.
- Search for removed copy (`Local workspace`, `READING STUDIO`, `READING EXPERIENCE`, `PREPARATION`, `Prepare selected range`, provider suffixes on voice labels) to catch stale UI strings.

## Maintainability constraints

- Do not change persisted settings schema for presentation-only work.
- Do not encode provider display rules in model IDs at call sites; centralize concise labels.
- Use reusable XAML styles/templates for theme toggles and swatches rather than creating controls repeatedly in code-behind.
- Keep Ollama startup cleanup exact-target and independently testable.
- Keep draft/edit transition logic separate from narration preparation logic.
- Preserve unrelated files and avoid broad mechanical rewrites.
