# Code scrolling and Paper follow-up

Implemented September 26, 2026 following approval of the follow-up plan.

## Changes

- Fixed the shared horizontal scrollbar style: responsive width, horizontal track orientation, 12-unit height and left/right paging commands. Vertical bars retain their width, orientation and vertical paging behavior.
- Added an accessible horizontal control for an overflowing code block whose own bottom scrollbar is below the visible transcript. Its rail reserves space below the transcript, so it does not cover code text. It hides when the native scrollbar is available, code fits, the block is offscreen or the document is cleared.
- Retained horizontal position when switching Paper appearance and appending messages. Each code block has independent scroll state.
- Measured ordinary code with the actual rendering font/text engine, including tabs and Unicode fallback, rather than relying on character count alone. Oversized source uses a bounded width estimate. Copy still returns the unchanged source.
- Increased the responsive reading/composer column from 780 to 900 units, and Paper's maximum page width from 940 to 1060 units.
- Changed Paper's page and controls to warmer tones, increased the existing local texture opacity from 18% to 32%, and added a thin page edge and subtle shadow. Light/Dark shell colors and code highlighting colors remain unchanged from the preceding implementation. High contrast still suppresses the Paper surface.
- No settings schema, chat persistence, model/provider or export changes.

## Code-based verification

- Broad Release solution run excluding `UiQualityRenderingTests`: **1,491 passed**, **6 optional live-model/video checks skipped**.
- Final focused run after the rail refinement and additional Unicode test: **69 passed**.
- Tests verify actual horizontal movement in both directions, full-width track layout, the last character becoming visible, exact code copy, 12/15/30-unit code, tabs/Unicode, tall-block access, resize/font changes, selection preservation during scrolling, Paper/append scroll-state retention, clearing stale controls, short-block behavior and unchanged vertical bars.
- Existing settings/history/export/workflow, accessibility, About/Help and Reading Studio code regression checks passed in the broad run. Paper/token contrast checks passed in the focused run.
- Release publish succeeded: `artifacts/publish-code-scroll/DictateAnywhere.App.exe`.
- `git diff --check` passed. Prior uncommitted work was retained.

No screenshots were generated and no visual validation was performed. Appearance review is left to the user.

## Limits and user review

- WPF document widths remain bounded at 1,000,000 units. Pathologically large individual lines can exceed that bound; source copy/export remains intact. Expensive font measurement is limited to source up to 100,000 characters.
- Physical mouse/keyboard, display DPI and live model/read-aloud behavior were not manually exercised. The optional checks above remain skipped as configured.
- Use the new published executable to test the long red-black-tree condition, scrolling through the middle and bottom of long code blocks, and Paper appearance. An already running process continues to use its previously loaded binaries.
