# Reading Studio quality implementation plan

> **Historical record:** This completed quality plan preserves implementation
> rationale; it is not current-head evidence or a current status owner. Use
> [`docs/documentation-map.md`](../docs/documentation-map.md) for current owners.

## Goal

Make the main application open predictably, simplify Reading Studio into one coherent reading model, preserve imported text faithfully, and make playback/export controls behave like polished desktop controls. Changes should be implemented through small policies and reusable helpers rather than event-handler-only patches.

## Implementation status

Stages 1–6 are implemented. Stage 7 is complete for compilation, automated behavior, and the normal-launch smoke test: the full Release solution passes 475 tests, and launching the executable opens the main Notype window. Reading Studio's custom WPF layout compiles and its control/state wiring is covered by focused initialization tests; the live in-app click-through was stopped when Windows reported concurrent user input, to avoid taking control of the user's active window.

## Confirmed causes

1. **Interactive launch is treated like background launch.** `App.StartSelectedExperienceAsync` only creates the workbench window when the saved experience is `TextboxWorkbench`. Windows startup and an explicit command launch currently use the same command line, so there is no intentional tray-only path.
2. **The tray's primary action opens Settings.** Double-click raises `OpenSettingsRequested`, even though the workbench is the application's primary surface.
3. **PDF selection is too trusting of `Page.Text`.** The current code only chooses geometric words when native text has almost no spaces. A native text layer can have normal spacing and still omit glyphs or words. The geometric fallback also flattens every page to one line, which prevents reliable heading/title inference.
4. **Document title always comes from the filename.** There is no content-title policy.
5. **Reading view and follow-along are separate representations of the same state.** Combinations can conflict, and highlight-style selection silently changes reading view.
6. **Sentence rendering bypasses the reusable word-style path.** It uses a `Span` with the theme's sentence color, ignoring the chosen focus color and most highlight styles.
7. **Sentence boundaries stop before trailing quotation marks.** Closing quotation/bracket tokens can become the first token of the next sentence.
8. **Timeline track clicks use WPF repeat-button commands.** Those commands apply a large-change step instead of mapping the pointer directly to time.
9. **Per-word layout reserves excess width.** A bold-width safety buffer, border padding, and a literal separator are all added, making Latin text particularly loose.
10. **Export controls and their canvas setting are separated.** The output actions are at the top while the video canvas is lower in the scrolling panel.
11. **Video rendering uses fixed font sizes.** Theme, typeface, highlight style, and focus color are passed already; selected reader text size is not.
12. **Voice choice has no audition path.** All samples can be generated lazily and cached per language/voice, avoiding a large bundled preview library.
13. **YouTube publishing uses a visually separate shell.** It needs the same chrome, surfaces, spacing, and theme context as Reading Studio.

## Implementation stages and acceptance criteria

### 1. Launch and progress animation

- Add an explicit `--background` launch option and register that option for Windows login startup.
- Open the workbench for normal interactive launches regardless of dictation experience mode.
- Make tray double-click open the workbench; keep Settings in the context menu.
- Slow the thinking-character cadence and rotation with named timing policy constants.
- Verify startup command persistence and animation timing with unit tests.

### 2. Text integrity, headings, and sentence boundaries

- Reconstruct geometric PDF words into lines using coordinates and compare native/geometric candidates by character and token coverage.
- Prefer the more complete credible candidate, while retaining native text when geometric extraction is materially worse.
- Keep RapidOCR as the page fallback for genuinely sparse/image-only pages; do not OCR every searchable page.
- Add a conservative content-title resolver that examines early meaningful lines and falls back to the filename.
- Replace punctuation-only sentence termination with ranges that consume closing quotes/brackets.
- Add regression tests for partial native PDF text, title candidates, and quoted sentence endings.

### 3. One reading model and responsive sidebar

- Replace `Reading view` plus `Follow along` with one `Reading focus` selector: Off, Word on page, Sentence on page, Focused sentence, Focused word.
- Keep internal view/highlight enums for rendering/export, but derive both from the single option.
- Add a persistent hamburger control that collapses/restores the left panel without entering fullscreen.
- Make fullscreen icon-only with an accessible tooltip and preserve the pre-fullscreen sidebar state.
- Convert source and output actions to icon-only controls with accessible names and tooltips.

### 4. Highlighting, typography, and seeking

- Render sentence follow-along through the same word visuals as word follow-along.
- Apply every highlight style and the selected focus color in both word and sentence modes.
- Use a translucent selected-color band where a filled sentence treatment is appropriate so text contrast remains safe.
- Reduce Latin word safety width while preserving a slightly larger Devanagari buffer; validate that bold highlighting never reflows.
- Handle timeline pointer input by mapping the click coordinate directly to slider time; retain thumb dragging.

### 5. Export and publishing cohesion

- Move Canvas, Audio, Video, and YouTube actions into one bottom `Export & publish` group.
- Use recognizable audio, camera, and YouTube glyphs with tooltips.
- Add selected text size to `ReaderVideoExportRequest` and scale landscape/vertical typography from it within safe bounds.
- Keep theme, typeface, focus color, and highlight style immutable in a captured render-settings value for the entire export/publish operation.
- Restyle YouTube publishing with Reading Studio's chrome, shell, cards, footer, and owner-selected theme.

### 6. Narrator previews and Sanskrit evaluation

- Add a play/stop button adjacent to narrator selection.
- Provide punctuation-rich, language-appropriate preview sentences, prioritizing English, Hindi, and Nepali.
- Generate on first play with the selected local voice, copy to a stable per-user cache, and reuse it thereafter. This makes preview storage proportional to voices actually auditioned and supports the other languages without increasing installer size.
- Stop preview audio and cancel generation when language/voice changes or the reader closes.
- Document Sanskrit options separately. Do not label a model production-ready until license, language support, runtime footprint, and Windows integration are verified from primary sources.

### 7. Verification

- Run the focused App test project after each stage.
- Run the full solution test suite after compilation is clean.
- Render/inspect Reading Studio and YouTube Publishing at minimum size and a typical desktop size.
- Confirm no generated binaries, caches, or preview audio are added to source control.
