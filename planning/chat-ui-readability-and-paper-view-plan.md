# Chat readability and paper view plan

## Design decision from the references

- `C:/Users/RA/Downloads/Downloaded/ChatGPT Image Sep 25, 2026, 01_48_04 PM.png` is the **blank texture source**: a subtle, cool-white paper surface with visible grain and no writing.
- `C:/Users/RA/Downloads/Downloaded/ChatGPT Image Sep 25, 2026, 01_47_27 PM.png` is the **appearance reference**: live code should appear directly on the paper, with dark punctuation, magenta keywords, violet names/types, and warm-colored numbers. Its code is part of the picture and must not be used as rendered UI text.
- Add **Paper** as an optional chat-workspace appearance alongside **Standard**. The paper treatment covers the conversation page and a visually coordinated composer; the sidebar and window chrome retain the selected application theme. Standard remains the default for existing users. The paper mode is independent of the existing Reading Studio `Warm Paper` document theme.
- Use the blank texture only as a restrained background. Code, prose, message actions, and controls remain live, selectable, accessible WPF elements. In Windows high contrast, replace the image with solid system colors. Ensure the light page and all syntax colors meet readable contrast in both light and dark app shells.

## Connected-code review and impact map

| Concern | Current owner | Required boundary |
| --- | --- | --- |
| Model responses | `WorkbenchChatController` and `ChatMessage` | Keep provider output, prompts, and message storage unchanged; style every model through the shared transcript renderer. |
| Markdown and code | `ChatMarkdownRenderer` | Add bounded highlighting and paper/standard code presentation without changing source text, copy, or speech text. |
| Conversation layout | `WorkbenchChatTranscriptView` | Preserve message order, selection, scroll position, append optimization, and re-render correctly when appearance changes. |
| User/assistant distinction | Transcript view and theme brushes | Add a readable user-message surface and alignment; keep assistant prose and actions clear in both appearances. |
| Typography | `AppSettings`, `ChatTextSizePolicy`, Workbench typography application | Keep the 15 px default, respect saved 12–30 px choices, and make code follow the chat size without overflow. |
| Zoom and Quick Settings | Workbench zoom handler, `WorkbenchRoot`, Quick Settings view and placement | Use one Workbench zoom value; reproduce the reported Ctrl+Plus issue with focus inside Quick Settings and verify its size, scrolling, and placement. |
| Sidebar toggle | Workbench XAML, sidebar width resource, toggle accessibility state | Anchor the open-state control near the sidebar divider while keeping it available when collapsed and at large text scale. |
| Theme and high contrast | `AppThemeManager`, `WindowThemeBehavior`, design tokens | Scope paper-specific brushes to the chat workspace; avoid changing global palette or Reading Studio themes. |
| Appearance persistence | `AppSettings`, `CurrentSettingsDocument`, `SettingsSchema`, legacy reader, app/window settings events | Persist Standard/Paper with a safe default and migration; preserve existing settings and future-schema protection. |
| History and export | `LocalChatHistoryStore`, `ChatTranscriptExporter` | Keep stored messages and Markdown/RTF exports content based; appearance must not alter history or export text. |
| Packaging | App project resources and published installer payload | Bundle an optimized local copy of the blank texture and verify it loads from a published build. |

## Implementation sequence

### 1. Establish the baseline

1. Capture the same conversation at the current saved text size and at 15 px, at 100% Workbench zoom. Check light/dark themes, minimum/maximized window sizes, and Windows display scaling.
2. Reproduce long Java lines, unfenced and fenced code, mixed prose/code, and a conversation restored from history. Confirm where wrapping or clipping originates before changing layout.
3. Keep saved user text-size preferences. Ensure new installs start at 15 px and that the Quick Settings label, transcript, composer, inline code, and fenced code agree at that baseline.

### 2. Build one code-rendering path for all models

1. Add a bounded language-aware tokenizer behind `ChatMarkdownRenderer`. Use Markdown fence language tags and common aliases; show a readable plain-code fallback for unknown, malformed, or oversized input. Avoid changing provider prompts to achieve styling.
2. Render tokens as live WPF runs using semantic color resources for base text, keywords, strings, comments, and numbers. Keep source text and Copy code output unchanged by highlighting.
3. Use a clear monospace font, consistent line height, and a visible language/copy header. Give long lines horizontal scrolling inside the code region so they do not widen the transcript or hide the composer.
4. Supply two visual styles from the same tokens: a contained Standard code block and a transparent, lightly separated Paper code region that reads as ink on the sheet.

### 3. Improve message and shell alignment

1. Give user messages a modest tinted, right-aligned surface in Standard and a paper-compatible surface in Paper. Keep assistant prose on the page and preserve copy/read-aloud actions.
2. Place the sidebar toggle against the sidebar grid boundary with a measured inset, and define its collapsed position. Verify hit target, tooltips, accessible name, window drag behavior, and responsiveness.

### 4. Repair and verify Workbench zoom

1. Reproduce Ctrl+Plus with Quick Settings open and focus on a button, slider, and model selector; test Ctrl+Shift+=, numpad Plus, Ctrl+Minus, and Ctrl+0.
2. Make keyboard shortcuts and the panel's zoom buttons update the same saved Workbench percentage. Verify text, controls, surface width, internal scrolling, overlay, and placement at 80–150%.
3. Keep About and Reading Studio Help's document zoom behavior. Review the Advanced settings hotkey-capture boundary before changing its shortcut handling.

### 5. Add the Paper appearance

1. Optimize a local copy of the blank texture and add it as a WPF resource. Use stable scaling or tiling so its grain remains subtle at different DPI and zoom levels; provide a solid-color fallback if loading fails.
2. Build a bounded paper page within the chat workspace with generous but responsive margins. Apply dark prose ink and the reference's magenta/violet/warm code palette at accessible contrast. Coordinate the composer and its focus state with the page.
3. Add a Standard/Paper control in Quick Settings. Persist the selection with an explicit settings default and migration, and ensure switching modes preserves the active conversation, selection, scroll position, model, and unsent composer text.
4. In high contrast, suppress texture and use system text/background/selection colors. Keep the paper presentation independent of stored conversations and exports.

## Verification gates

- **Focused tests:** settings default/round-trip/migration; tokenizer aliases/fallback/bounds; code copy fidelity; theme/high-contrast resources; appearance switching and transcript cache invalidation; role surfaces; sidebar state; Quick Settings zoom with focus inside the panel.
- **Connected regression:** chat send and local greeting, model switching, history restore, copy/read-aloud, Markdown and RTF export, settings autosave, Reading Studio theme behavior, app theme switching, keyboard navigation, and accessibility names.
- **Visual review:** compare the two references with live Java code and prose at 15 px; inspect Standard and Paper in light/dark shells, high contrast, 80–150% zoom, narrow/maximized windows, and common DPI settings. Check code line scrolling, texture sharpness, selection visibility, and readable contrast.
- **Packaging check:** publish the app and confirm the bundled paper asset resolves without the original image path or a network request.
- **Sequence:** complete and review each numbered stage before stacking the next one. The final change is ready when the full relevant test slice and manual visual matrix pass with no regressions in the connected features above.
