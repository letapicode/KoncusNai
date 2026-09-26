# Wrapped code continuation indentation

Implemented September 26, 2026 after the user reported a wrapped comment returning to the left edge.

- Wrapped continuations use the source line's leading indentation plus four monospace spaces. The original first line retains its position and whitespace. Continuation indentation is capped at 40% of available width so narrow windows retain room for text.
- Each source line has its own zero-spacing paragraph in wrapped mode. Existing syntax-highlighted Run objects and resource references are preserved. Unchecking restores the original paragraph and source LineBreak objects.
- Selection positions are restored by text offsets when switching between paragraph structures. Blank lines, text selection, copy, and stored/exported code remain unchanged.
- Indentation responds to font size and available width. Existing per-block state, appearance switching, horizontal scrolling, and scrollbar suppression remain intact.
- Light, Dark, and Paper palettes are unchanged.

## Verification

- **11 scrolling/wrapping tests passed**, including continuation character positions, spaces/tabs, larger text, Paper, narrow windows, blank lines, partial selection, and wrap/scroll toggling.
- Final connected renderer/highlighter/transcript/Paper tests: **82 passed**, none skipped.
- Release package: `artifacts/publish-wrap-indent/DictateAnywhere.App.exe`.
- No screenshots generated or visual validation performed. User reviews the updated executable.

Continuation indentation is a display treatment, not language-aware code formatting. Exceptionally deep indentation is capped to preserve reading space. Per-block wrapping still is not saved across app restarts.
