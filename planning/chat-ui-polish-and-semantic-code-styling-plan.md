# Chat UI polish and semantic code styling

## Status and scope

Implemented following the user's approval. Verification results and remaining manual checks are recorded in `planning/chat-ui-polish-verification.md`.

This follow-up refines the existing chat readability implementation. Light and Dark remain application themes. Paper remains a separate optional chat appearance, with its existing persisted setting. Preserve saved typeface, text size, zoom, model selection, history, exports, and unsent input.

## References

- Styling brief: `C:/Users/RA/.codex/attachments/4c7fb586-53de-4be9-bb55-8dfb4400d025/Pasted text.txt`. Use the token hierarchy and dark palette as the design specification; surrounding advice is reference material rather than a separate instruction to generate images.
- Current Dark screenshot: `C:/Users/RA/AppData/Local/Temp/codex-clipboard-d20766a3-e680-4b03-aa0f-25e3d91b37a6.png`.
- ChatGPT comparison: `C:/Users/RA/AppData/Local/Temp/codex-clipboard-bbb1c67a-1cae-45d2-b541-707efb7ea0d6.png`.
- Current Paper screenshot: `C:/Users/RA/AppData/Local/Temp/codex-clipboard-95d38ed0-0068-4a5d-8e2e-065447fc1209.png`.
- Original blank texture: `C:/Users/RA/Downloads/Downloaded/ChatGPT Image Sep 25, 2026, 01_48_04 PM.png`. Keep the bundled local asset as the texture source.
- Earlier plan: `planning/chat-ui-readability-and-paper-view-plan.md`. This document replaces its visual treatment decisions where they differ, while retaining its content/persistence boundaries.

## Findings from the current connected code

1. `ChatCodeHighlighter` has only Plain, Keyword, Identifier, String, Comment, and Number categories. Every non-keyword identifier receives the same accent. Its shared, case-insensitive keyword set also cannot express Java's distinct primitive types and literals accurately.
2. `ChatMarkdownRenderer` gives user-message Sections a fixed 210-unit left margin, rectangular background, and right-aligned text. That does not produce a bubble that fits a short message.
3. The code renderer appends a Paragraph to a newly created RichTextBox's default document. Inspect the document's initial blocks as a likely contributor to the header gap, along with document/control padding, line metrics, and Grid row measurements. Confirm the cause before changing spacing.
4. Paper code explicitly draws a bottom border. The visible line is part of the current treatment and should be removed.
5. Reply action buttons use the global secondary-text brush, which becomes too light over Paper in the Dark shell. Paper composer and user-message surfaces also need better coordination with the page.
6. Appearance switching currently interacts with transcript caching, TextPointer selection offsets, nested code typography, and deferred scrolling. Changes to message containers must account for all four.

## Visual specification

### Code typography and spacing

- Keep the default at 15 WPF device-independent units and respect saved 12–30 text-size choices. Apply the same size to code; do not silently force a separate 14-unit size.
- Prefer JetBrains Mono if available, followed by Cascadia Code, Consolas, and a system monospace fallback. If bundling JetBrains Mono, verify its redistribution license, include the license, and verify the packaged font. Use normal weight, with ligatures disabled where they obscure literal operators.
- Target a 1.55 line-height multiplier, with exact indentation and source characters preserved. Check height and scrolling at larger sizes.
- Use a compact header with a 12–13-unit semibold language label and copy action. Target roughly 12 units between the header's bottom and the first code line, without an empty leading paragraph.
- Hide horizontal scrolling controls when lines fit; show a usable scrollbar inside the code region when they do not. Never widen the transcript to fit a long line.
- No new line-number gutter or current-line highlight in this pass. The brief lists editor treatments, but this component is a selectable chat code viewer.

### Dark code palette

| Category | Color | Rule |
| --- | --- | --- |
| Code surface | `#171A21` | Neutral charcoal; retain the surrounding Dark application theme. |
| Header surface | `#242A34` | Compact, integrated with rounded outer corners. |
| Border | `#303744` | Subtle single outline. |
| Default text, parameters, local variables | `#D8DEE9` | Most identifiers remain neutral. |
| Language keywords | `#F0719B` | Includes public, class, static, while, if, else, return. |
| Primitive types | `#B69DF8` | Includes int, boolean, double. |
| Class/type names | `#C4A7FF` | Known built-ins and recognizable type contexts. |
| Method declarations and calls | `#82AAFF` | No extra bolding. |
| Properties/members | `#8ABEB7` | For example, length in nums.length. |
| Numbers and boolean/null literals | `#F2A65A` | Distinguish unary signs from arithmetic operators. |
| Strings and characters | `#A8C97F` | Preserve escapes. |
| Comments | `#737D8C` | Quiet but readable; measure contrast and lighten minimally if required. |
| Operators | `#BCC3CE` | Neutral. |
| Brackets and braces | `#AEB6C2` | Neutral. |
| Commas, semicolons, dots | `#89929F` | Quiet punctuation. |
| Language label | `#C9D1DC` | Semibold. |
| Copy icon | `#8E98A8` | Brightens to `#D8DEE9` on hover/focus. |

The acceptance principle is that variables stay neutral and colors communicate categories. The brief's 60–70% neutral guidance is a visual target for typical code, not a quota that changes token classification.

Light and Paper use the same category distinctions with colors appropriate for light surfaces. Keep Paper's successful magenta/violet/warm ink direction, but stop accenting ordinary variables. Measure small-text contrast against the actual surfaces; do not reuse Dark hex colors directly on Paper. High contrast uses system colors and suppresses texture.

### User messages

- Right-anchor the bubble; left-align text inside it.
- Short messages size to their content. Long messages wrap at approximately 75% of the available conversation width, with responsive bounds at narrow sizes.
- Start with an 18-unit corner radius and 14–16 units of horizontal padding, 10–12 vertically. Use consistent spacing between messages.
- Preserve the user's saved prose typeface. Code retains its monospace typeface.
- Use the existing blue direction in Standard Dark, a restrained blue tint in Standard Light, and a muted parchment/blue-gray tint in Paper. Avoid large white strips.
- Support multiline text, lists, long URLs, Unicode, and user messages containing code. Keep selection, keyboard navigation, and accessible text intact.

### Paper and composition

- Remove the decorative line beneath Paper code. Code reads directly on the page.
- Reduce texture prominence with a stable, subtle treatment; avoid stretching grain into a dominant background.
- Use a bounded page and shared reading column. Start with approximately 740–820 units for content, centered in the available chat region, and generous responsive page margins. Review narrow layouts before fixing these dimensions.
- Coordinate the composer with the paper tone, using a soft tint and thin border rather than stark white. Restyle composer actions, placeholder, attachments, disabled states, focus, and expanded input consistently.
- Use dark Paper action icons with readable hover/focus states for code copy, reply copy, and read-aloud.
- Keep the existing sidebar alignment, Quick Settings controls, and Light/Dark/Paper separation. Recheck them after layout and scaling changes.

## Implementation stages

### 1. Establish real-app baselines and confirm layout causes

Review the renderer, transcript view, Workbench layout, theme resources, composer/expanded prompt, and settings-application paths. Capture the user's Java examples in the actual app at saved settings and at 15 units/100% zoom. Measure the code header gap and inspect default document blocks. Record message widths, scroll extent, selection, and unsent input before changing them.

### 2. Improve token classification and palettes

Extend token categories for primitive types, class/types, methods, members, literals, operators, and punctuation. Use language-specific keyword rules. Java is the detailed reference fixture; retain bounded, conservative rules for the other currently supported languages. Recognize declarations, calls, member access, and type contexts where local syntax supports them. Uncertain identifiers stay neutral.

These are display-oriented lexical/contextual rules, not compiler-backed symbol resolution. Do not claim exact semantic analysis for arbitrary fragments. Keep unknown/oversized input readable and preserve token concatenation exactly. Use one shared renderer across providers; avoid modifying model prompts or stored replies.

### 3. Correct the code component

Fix the confirmed source of leading blank space, then apply font, line metrics, compact header, surface colors, and action states. Preserve code copy, selection, tabs, Unicode, and scrolling. Verify short blocks have no spurious scrollbar and long blocks scroll horizontally within the available width. Remove Paper's bottom rule.

### 4. Implement rounded user bubbles safely

Prototype the rounded background and content-fitting measurement before replacing the current Section treatment. Check WPF FlowDocument selection across messages and embedded content. Choose the presentation structure that preserves existing selection/copy behavior; nested editors per message must not silently break cross-message selection. Maintain append optimization and restore selection/scroll after appearance changes. Cover short, wrapped, multiline, and Markdown user messages.

### 5. Coordinate Paper page, composer, and actions

Apply the restrained texture, page width/margins, tinted bubbles and composer, visible icons, and complete interactive states. Review both Light and Dark shells. Preserve pending attachments, expanded-input contents, composer focus, and existing persisted Paper/zoom settings. No new settings schema is expected for these presentation refinements.

### 6. Complete regression and visual verification

Run focused tests after each stage, then the relevant full Release suite and publish check. Review the actual application in addition to synthetic WPF captures. Record any unavailable physical DPI/keyboard/model checks explicitly rather than marking them passed.

## Acceptance and test matrix

- Java fixture: public/static/class/control-flow keywords pink; int violet; BinarySearch/String type color; binarySearch blue; nums/target/left/right/mid neutral; length teal; numbers orange; operators/brackets neutral. Include strings, annotations, generics, constructors, comments, method calls, incomplete code, and escaped literals.
- Other supported languages: representative fixtures preserve source, avoid indiscriminate identifier coloring, and use safe fallback for uncertain categories. Test unknown languages, untagged blocks, oversized input, and malformed/incomplete Markdown.
- Layout: short/long/multiline user bubbles, no fixed-width colored strip, left-aligned text, rounded corners, no artificial code-leading paragraph, no Paper separator, no unnecessary scrollbar.
- Content: exact Copy code and Copy reply; selection within code and across messages; read-aloud; Markdown/RTF exports; history restore; model switching; append behavior; unsent text and attachments preserved through appearance changes.
- Settings: existing saved size/typeface/zoom/Paper choice survive Quick Settings and full Settings saves. Confirm 15-unit defaults and existing keyboard zoom behavior.
- Visual matrix: Standard Light, Standard Dark, Paper in both shells, high contrast; 80/100/150% Workbench zoom; 12/15/30-unit chat sizes; narrow and large windows; available 100/150/200% display scaling. Include focus/hover/disabled states and selected text.
- Connected features: sidebar collapse/expand and header alignment; Quick Settings placement/focus; Advanced Settings hotkey capture; About/Help zoom; Reading Studio appearance remains unaffected.
- Packaging: verify any bundled font/license and the local texture resolve in the published app. Check the public API/dependency baseline only if an intentional contract change occurs.
- Delivery: list changed areas, test counts, before/after real-app captures, and remaining limitations. Readability and visual consistency must be reviewed before considering the work complete.
