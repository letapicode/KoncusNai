# Reading Studio draft, help, and identity refinement plan

> **Historical record:** This dated implementation record preserves the evidence
> available at that time; it is not current-head evidence or a current product
> contract. Use [`docs/documentation-map.md`](../docs/documentation-map.md) for
> current owners.

**Status:** Implemented and verified on 2026-08-28. The Release solution builds with zero warnings, 525 tests pass, two hardware-dependent smoke tests are skipped, removed UI copy is absent, and the generated minimalist mark was inspected at 16, 32, and 256 pixels and confirmed in Windows' task switcher. Automated WPF state coverage verifies the inline draft, persistent sidebar, transport metadata, compact actions, circular visual choices, help window, and edit workflow; further live input was stopped when concurrent desktop activity was detected.

## Goal

Make Reading Studio open as a calm writing workspace inside its normal shell, reduce the remaining visual weight in the settings sidebar, add durable in-product guidance, keep product documentation current, and replace the detailed book artwork with a crisp minimalist Notype mark.

## Confirmed findings

1. New Reading Studio sessions currently use a full-window `SourceEditorOverlay`. The overlay hides the normal sidebar and contains its own `Start writing` title, editable title field, Restore/Maximize button, Cancel button, privacy caption, and Start reading button.
2. The document title, word count, section count, current section, and overall progress occupy a large block at the top of the sidebar. These are reading-status details and fit more naturally near the transport controls.
3. Document import is presented as a labeled `Source` section with a full-width `Open document` button even though it is a single familiar action.
4. Focus colors use six choices in a three-column grid and each color sits inside a rectangular selection container. Rose and coral are insufficiently distinct.
5. Reading themes use bordered miniature page cards in a two-column grid. Four direct circular swatches can communicate the choices with less visual noise.
6. Reading Studio has no dedicated help surface explaining its workflow or controls.
7. `docs/guides/implemented-features.md` still describes the previous paste/import-first workflow, provider labels on voices, and the old preparation wording.
8. The current application icon is a detailed open-book/audio illustration. The build, About page, tray/launcher assets, and installer icon all ultimately consume the existing `Notype.ico` and sized PNG assets.

## Interaction model

Reading Studio will have one explicit workspace state:

- **Draft:** the left settings panel remains visible and the main reading page becomes an empty, scrollable multiline editor with the caret focused. No separate title field, modal panel, Restore, Cancel, Start writing, or Start reading controls are shown. Range and export controls remain unavailable until text exists and a reading document has been created.
- **Reading:** pressing **Create Videobook** validates the draft, creates the reading document, selects its full section range, switches the main page to the formatted reading surface, and begins the existing explicit preparation workflow. A pencil icon beside the transport/status area switches back to Draft with the full source text preserved.
- Imported documents enter Reading state because they already have a source title and parsed content. The pencil action can still expose the imported text for correction.

The draft-to-reading transition will be implemented in one state method so visibility, focus, button availability, document reconstruction, playback invalidation, and accessible labels cannot drift across event handlers.

## Implementation sequence

### 1. Replace the editor overlay with an inline draft state

- Remove the `SourceEditorOverlay`, compact/maximized editor panel, title box, Restore/Maximize, Cancel, privacy caption, and Start reading button.
- Add a draft `TextBox` inside the existing reader page frame. Reuse the page typography, padding, theme colors, wrapping, and scroll behavior so writing feels like editing a document rather than opening a dialog.
- Keep the sidebar visible for new drafts.
- Focus the draft editor after layout using the dispatcher and place the caret at the end without synthesizing or preparing anything.
- Add a single pencil icon in the footer for returning to Draft state. Supply a tooltip, automation name, and keyboard focus treatment.
- Preserve imported titles internally. New drafts use `Untitled reading` internally without exposing a separate title field.

### 2. Make Create Videobook the state transition

- Capitalize the action as `Create Videobook` in idle and progress/status copy.
- When Draft is active, disable the button for whitespace-only input and enable it immediately after meaningful text is entered.
- On click, build the document from the current editor text, select the complete generated section range, switch to Reading state, and run the existing selected preparation mode.
- When Reading is active, preserve the current behavior for re-creating narration after language, voice, or range changes.
- Keep playback, preparation, cancellation, export, and ownership behavior unchanged.

### 3. Move document metadata to the transport area

- Remove the document title/metadata/progress block from the top of the sidebar.
- Place the title, word/section count, current section, and compact document progress close to Previous / Play / Next.
- Keep the status text readable without competing with the centered transport controls.
- Hide reading-only metadata and playback controls while an untouched draft is open; show the pencil edit action only after a real document exists.

### 4. Reduce document import to one icon

- Remove the `Source` heading and the full-width labeled button.
- Place a compact document/folder icon at the top of the sidebar controls beside a new help icon.
- Use `Open document` only as tooltip and automation text.
- Keep the existing local import/OCR behavior and file dialog unchanged.

### 5. Simplify visual choices

- Expand focus colors to eight visually separated premium hues arranged four per row: gold, sky, violet, emerald, coral, rose, aqua, and silver. Adjust rose/coral so hue and luminance are clearly different.
- Render each focus choice as a standalone circle. Selection uses a restrained outer ring around the circle; hover/focus does not add a rectangular tile.
- Render all four reading themes in one row as standalone circles based on their page color. Use the same restrained circular selection ring and hover tooltip.
- Retain keyboard selection, automation names, and high-contrast visible focus behavior.

### 6. Add evolving Reading Studio help

- Add a dedicated, resizable `ReadingStudioHelpWindow` that visually follows the About window but owns its own content and code-behind.
- Explain the Draft → Create Videobook → Reading → Edit workflow, document import and OCR, range selection, language/voice, processing modes, focus and theme controls, playback, export, publishing, privacy, and first-use model preparation.
- Open it from a compact help icon beside the document-import icon.
- Keep the copy in XAML so future feature changes are easy to review and update.

### 7. Replace the product icon

- Introduce a source-controlled SVG master containing a geometric single-letter `N` mark: a midnight rounded square, warm-gold continuous monogram, and no book/microphone detail.
- Generate the existing 16, 24, 32, 48, 64, 128, and 256 PNG assets plus the multi-resolution `Notype.ico`, preserving current resource paths so build, launcher, About, tray, and installer integrations do not need parallel branding logic.
- Keep a deterministic icon-generation script alongside the assets.
- Inspect the mark at 16, 32, and 256 pixels to ensure it remains recognizable and does not blur into a generic square.

### 8. Documentation and tests

- Update `docs/guides/implemented-features.md` for the write-first workflow, on-demand Ollama startup policy, simplified voice labels, visual swatches, help window, and current `Create Videobook` action.
- Update Reader initialization tests to require an inline focused editor, visible sidebar, absent legacy editor controls, eight focus colors, four single-row themes, document/help icons, metadata in the footer, and the pencil edit action.
- Add help-window construction/content coverage and icon asset integrity checks.
- Keep UI construction tests in the shared non-parallel WPF collection.

## Maintainability guardrails

- Represent Draft/Reading explicitly rather than inferring state from several visibility properties.
- Centralize workspace-state application, draft validation, and document control enablement.
- Do not duplicate source text between separate modal editor controls.
- Keep help content out of `ReaderWindow.xaml.cs` and keep icon generation deterministic.
- Reuse existing button, chrome, and typography resources instead of introducing near-duplicate styles.
- Preserve all local-first, OCR, narration, export, publishing, accessibility, and Ollama ownership boundaries.

## Verification

- Build the full solution in Release with zero warnings and errors.
- Run the full automated test suite.
- Audit source for removed UI copy: `Start writing`, `Restore`, editor `Cancel`, `Source`, lowercase `Create videobook`, and stale `Prepare selected range` wording.
- Launch the Release app and inspect a new Reading Studio session, draft typing, Create Videobook transition, edit transition, import/help icons, metadata placement, focus colors, reading themes, and help window at normal desktop scale.
- Confirm the app icon in the executable/window, About page, and small PNG sizes.

## Acceptance criteria

- A new Reading Studio window shows its normal sidebar and an immediately editable blank page with no overlay chrome or title field.
- Create Videobook is the only visible draft-to-reading action and it switches to the formatted reader while invoking the existing preparation workflow.
- A pencil icon safely returns to the full draft text.
- Sidebar document import and help are compact icons with useful hover and automation text.
- Reading metadata lives near playback, not at the top of the sidebar.
- Focus colors are eight distinct circles in two rows of four; themes are four circles in one row; neither appears inside rectangular option cards.
- Reading Studio help is thorough, resizable, and reachable from the sidebar.
- Product documentation describes the implemented behavior.
- The application uses a legible minimalist `N` mark at every existing icon size.
