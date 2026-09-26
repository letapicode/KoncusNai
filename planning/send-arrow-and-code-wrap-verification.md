# Send arrow and code wrapping

Implemented September 26, 2026.

- The submit affordance now enables for a nonempty draft while chat is idle, including when cached model readiness is false. The existing submission controller performs readiness checks and reports missing model/runtime setup. Busy chat, recording, other operations, and file imports still block the affordance. Empty and whitespace-only drafts do not enable it.
- Added a keyboard-accessible `Wrap lines` checkbox next to the language in every code block, across models and appearances. Default remains horizontal scrolling. Checked mode uses WPF soft wrapping at the available width; it does not insert source line breaks, reformat code, or change copy/export content.
- Wrapping adapts to resize and font size. A wrapped block suppresses its native horizontal bar and the auxiliary rail. Unchecking restores measured horizontal scrolling. The checkbox and language/copy controls use separate header columns.
- Each block owns its wrapping state. Appending messages preserves it; appearance rebuilds restore it for matching code blocks. Clearing the transcript resets it. Wrapping is a temporary display preference and is not saved to chat content or settings.
- Existing Light/Dark/Paper palettes are unchanged.

## Code verification

- Focused renderer, scrolling, presentation reducer, and send controller checks: **37 passed**.
- Final broader Release application tests excluding screenshot/render-gallery tests: **999 passed**, **2 optional video tests skipped**.
- New tests exercise typing-driven send enablement and click forwarding, whitespace handling, stale readiness, import blocking, actual long-line reflow in standard/Paper modes, source copy and selection retention, resizing/font changes, appearance/append state retention, rail suppression, and restoration of scrolling.
- Release package: `artifacts/publish-send-wrap/DictateAnywhere.App.exe`.

## Review limits

No screenshots were generated or visual validation performed. User reviews appearance with the new executable. Soft wrapping displays continuations at the available width; it does not perform language-aware reindentation. Per-block wrapping is not persisted across app restarts. Live model and physical mouse/keyboard behavior were not manually tested.
