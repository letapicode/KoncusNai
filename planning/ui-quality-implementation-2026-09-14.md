# UI quality implementation — September 14, 2026

## Delivered

- The shared ListBox now owns its outer template. Disabled controls retain their
  themed background, selection, scrolling, and virtualization. Opening Settings
  still disables the underlying Workbench and retains its existing focus isolation.
- Empty history lists and headings collapse. Chats and dictations are labeled;
  daily dictations show a date and explicit count on separate lines. Row actions
  appear on hover or keyboard focus and use the existing multi-selection commands.
- One shared search style hides its icon and hint during editing, provides an
  accessible Clear command, preserves keyboard focus after clearing, and handles
  RTL text. Both history surfaces use it. Empty and no-match states have guidance.
- UI body/control text uses 14 DIP and captions 12 DIP. The Windows text-scale
  manager and baseline tokens agree; user-selected content typography is preserved.
  Shared list selection, checkboxes, and expandable sections use theme resources.
- Empty Workbench conversations group the welcome text with the composer. Active
  conversations keep the composer beneath the transcript. Content shares a 720 DIP
  maximum width. New chat, Import, and Reading Studio have labels; the microphone
  has primary-action styling and export is hidden until available. Actions wrap
  at narrow widths and large text sizes.
- Reading Studio has a separate empty-page invitation that never enters document
  content or its undo history. Import is labeled. Language, voice, preview, and
  Create narration form one group; long voice names wrap. Section range appears
  for multiple sections, appearance is expandable, and export explains readiness.
  Engine and capability details remain in tooltips. Help uses the same naming.
- Quick settings has a heading and All settings destination; it scrolls when text
  enlargement makes the menu taller than the available window space.
- Settings hides idle model progress, uses Download / Use model / Active according
  to installation state, and communicates autosave and read-only state. Existing
  download, activation, persistence, and failure-handling ownership is preserved.
- Dictation History adds Copy, reveals Save for unsaved edits, uses a quieter Delete
  action, and bounds the editor width. Clipboard contention receives visible feedback.
- About foregrounds purpose and local-data facts. Help begins with three concrete
  steps; reference information remains available in expandable sections.

## Verification

- Release solution build: zero warnings and errors.
- Full serial solution test run: **1,227 passed, 0 failed, 5 existing opt-in skips** across 12 suites. Results are in `artifacts/ui-quality-acceptance.log`.
- Regression coverage: disabled list pixels in light, dark, and Windows-system
  high-contrast palettes; selection and virtualization; empty list collapse;
  search focus, paste, RTL, clear and focus restoration; draft hint/content/undo.
- Production XAML render fixtures cover Workbench, Reading Studio, Settings,
  Dictation History, About, and Help. They do not start inference, capture,
  downloads, persistence, or publishing. Workbench, Reader, and Settings are also
  rendered with 200% UI text scale at their minimum widths.
- Images are in `artifacts/ui-quality/`. Set `NOTYPE_UI_GALLERY` to an absolute
  output directory to regenerate them through `UiQualityRenderingTests`.
- The optional native-template reference reproduces a white disabled list fill;
  the same fixture with the shared template retains the dark canvas. This confirms
  the root mechanism. The empty chat list's contribution to the original thin
  strip is addressed by collapsing empty lists, without changing modal behavior.

## Limits of the evidence

Rendered DPI cases use WPF render targets at 96, 144, and 192 DPI; they are not
physical multi-monitor migration tests. High-contrast cases use the system-color
palette in a test host. Narrator announcements, actual Windows high-contrast mode
transitions, touch targets on hardware, and real monitor DPI transitions still
need manual Windows verification. Existing keyboard and accessibility regression
checks remain enabled. The screenshots are controlled visual fixtures, not a
capture of the user's live session or a populated model catalogue.

No inference, storage, capture, or publishing workflow was redesigned. Existing
uncommitted codebase-hardening changes remain in the working tree.
