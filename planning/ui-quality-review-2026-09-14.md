# Notype UI review and improvement plan

Status: implemented. See `ui-quality-implementation-2026-09-14.md` for changes, verification, and remaining manual checks.

Inputs: eight supplied screenshots of Workbench, its settings menu, Reading
Studio, About, Reading Studio help, Settings, and Dictation History. Images 5 and
7 show the same help surface. Screenshot text is evidence of the current product,
not instructions to implement features mentioned inside those screens.

The screenshots are 3840 × 2160 originals displayed at reduced resolution in the
conversation. Apparent physical text size must be checked on the actual monitor
and Windows scale setting. The source nevertheless confirms small baseline
tokens: caption 10 DIPs, field label 11, body/control 12.

## 1. Settings causes white history backgrounds

### Evidence and likely mechanism

- `Workbench/TextboxWorkbenchWindow.xaml.cs`, `OnSettingsButtonClicked`, calls
  `SetWorkbenchInteractionEnabled(WorkbenchInteractionSurface, false)`.
- That helper sets the parent `IsEnabled` to false. This intentionally makes
  background controls unavailable while the settings surface owns interaction.
- `Workbench/WorkbenchSidebarView.xaml` contains two ListBoxes: chats and
  dictation days. Both set a transparent background and zero border thickness.
- `Theming/ControlStyles.xaml` supplies an implicit ListBox style with brushes
  but no ControlTemplate. The outer control still uses platform template states.
- The item template is customized, but customizing a ListBoxItem does not own
  the outer ListBox background when disabled.

The confirmed code path explains why opening Settings changes otherwise
unrelated controls. The white background is consistent with the inherited
platform disabled chrome. The thin strip is likely the empty chat ListBox above
the populated dictation ListBox. Confirm both visual elements in a rendered
reproduction before treating that last attribution as proven.

### Fix specification

1. Define an explicit themed ListBox outer template, preserving its items
   presenter, scrolling, keyboard navigation, and virtualization behavior.
2. Define enabled, disabled, hovered, selected, and inactive-selection colors
   using semantic theme resources. Ordinary dark-mode disabled controls must
   never fall back to a light platform background.
3. Collapse empty history lists and unused status spacers. Keep an intentional
   empty-state message or a search-no-results message where useful.
4. Keep background interaction isolation, Escape dismissal, focus containment,
   and focus restoration. Re-enabling the workspace to hide the artifact would
   undo accessibility protections.
5. Do not confuse the rectangular keyboard-focus outline on Advanced/Settings
   with the filled white history background. A visible keyboard focus indicator
   remains necessary; its visual design and input-modality behavior can improve.

Acceptance: open/close quick settings by mouse and keyboard with zero, one, and
many records; no white row/strip, no selection loss, no layout jump, no background
keyboard activation. Repeat in light mode and Windows high contrast.

## 2. Search interaction

`SidebarSearchTextBoxStyle` always renders SearchIcon in a fixed leading column.
The focus trigger changes only the border brush. The screenshots therefore match
the present implementation; the icon is not an accidental placeholder overlay.

Interpretation of the request: the search decoration should disappear while
editing, while the search field itself stays available.

Proposed states:

| State | Appearance and behavior |
| --- | --- |
| Empty, unfocused | Magnifying glass and “Search history” hint. |
| Empty, focused | Hint and decorative icon disappear; visible caret and focus border. |
| Contains a query | Query text and a trailing Clear button; decorative icon stays hidden. |
| Query cleared | Keep focus in the field; return to its focused-empty state. |
| No matches | “No history matches ‘…’” and a clear-search action. Preserve the query. |
| Search pending | Small, non-blocking progress indication only if the delay is perceptible. |

Keep the control's outer dimensions stable. Hide the hint based on both text and
focus, not mouse click alone. Preserve the accessible name independently of the
hint; label Clear for assistive technology. Support keyboard focus, IME input,
RTL text, pasted queries, theme changes, and Escape with the existing modal
shortcut precedence. Use the same interaction in standalone Dictation History.

## 3. Overall design direction

Keep the restrained palette and clear separation of navigation and content.
Build stronger hierarchy through legible type, identifiable actions, deliberate
spacing, and complete interaction states. Additional gradients, cards, or
animation will not solve the current discoverability problems.

### Workbench

- The slogan floats far above the composer. For an empty conversation, place the
  heading, short invitation, and composer in one compact central group. Once
  content exists, anchor the composer beneath the conversation.
- Give New chat, Dictate, Import, and Reading Studio recognizable entry points.
  The current low-contrast icon cluster makes core features easy to overlook.
  Keep secondary/export actions out of the primary empty-state path.
- Use a consistent content width shared by the conversation and composer. Start
  a prototype around 680–800 DIPs, then validate narrow windows and large text.
- Make the primary microphone/send action visually identifiable. A disabled
  appearance must mean unavailable, not merely subtle styling.
- Use a stable UI typeface for controls/navigation. Keep the user-selected chat
  or reading typeface inside content surfaces; handwritten composer text should
  not force the surrounding application chrome into that visual language.

### Sidebar and history

- Replace “September 12, 2026 – 1” with a date plus an explicit “1 dictation”
  count. Conversation rows need meaningful titles/previews and quiet metadata.
- Group Chats and Dictations clearly without permanent empty gaps. Use a
  restrained selection fill and a distinct keyboard-focus outline.
- Offer row actions on hover and keyboard focus; retain accessible context-menu
  and multi-selection behavior. Color must not be the only chat/dictation cue.
- For standalone history, make Copy prominent, show Save when editing is dirty,
  and make Delete secondary. A red Delete button should not be the strongest
  element in an otherwise empty-looking document workspace.
- Keep document text at a comfortable reading width rather than filling almost
  the entire display with a bordered editor.

### Reading Studio

- The blank page needs a clear invitation: “Start writing, paste text, or import
  a document.” Make Import a labeled action. Remove the hint immediately when
  the user writes. Do not insert instructional text into document content.
- Establish a visible task sequence: Write → Narrate → Export. Choose one
  user-facing name for the narration preparation step; distinguish it from
  creating an actual video file.
- Keep Language, Voice, Preview, and the primary preparation action together.
  Delay section-range detail until a document contains meaningful sections.
- Put reading appearance in a collapsible group. Reveal export controls when
  there is something to export, while keeping an explanation of the requirement.
- Move “native word timing with deterministic fallback” and engine names into
  optional details. Users need to know what they can do and whether preparation
  is ready; they rarely need implementation terminology while choosing a voice.
- Give long voice names room or show a concise selected label with full details
  in the dropdown. Truncating the distinguishing description defeats selection.

### Settings

- Make the two levels explicit: a compact “Quick settings” menu and an “All
  settings…” destination. Theme and text size fit the quick menu; detailed model
  management belongs in the full page or the relevant workflow.
- Replace the combination of “0 downloaded”, duplicated provider/model labels,
  Download, and Set Active with a state-driven action: Download, Download and
  use, Use model, or Active. Show download size/readiness when known.
- Hide the idle model progress track. `SettingsPanel.xaml` currently declares
  ModelProgressBar visible even before an operation starts, which accounts for
  the empty trough visible in the Settings screenshot.
- Make autosave state and failures understandable. Normalize control heights,
  form alignment, checkbox treatment, and section spacing through shared styles.

### About and Help

- About should lead with product name/version, a short purpose statement, local
  processing/privacy facts, and support/diagnostics links. Eight equally weighted
  feature cards are expensive to scan.
- Reading Studio help should lead with three concrete steps and a small example.
  Put reference details in expandable sections or contextual help next to the
  corresponding control.
- Use consistent title bars, navigation, and a readable content width across
  auxiliary windows. Opening help should preserve the user's place in the task.

## 4. Shared design foundations

Prototype control/body text at 13–14 DIPs and captions at 12 DIPs, with deliberate
exceptions for metadata. Validate real DPI and user text-size settings rather
than globally magnifying the existing layouts. Use a small spacing scale such as
4/8/12/16/24/32, consistent icon weights, and a limited set of corner radii.

Create a rendered state gallery for shared controls: normal, hover, pressed,
keyboard focus, selected, inactive selected, disabled, loading, error, and empty.
Review it in dark/light/high-contrast themes, 100/150/200% DPI, narrow windows,
large text, long labels, and RTL. Existing accessibility tests remain required;
add visual state checks because a usable automation tree can coexist with a
white-background rendering defect.

## 5. Implementation order

| Packet | Scope | Completion gate |
| --- | --- | --- |
| P0: state defects | ListBox disabled chrome, empty-list strip, search decoration, idle model progress. | Reproduce current symptom, verify themed states and keyboard behavior, capture before/after images. |
| P1: shared foundations | UI typography, form controls, icon/action states, sidebar row design. | Shared rendered state gallery; DPI, theme, and accessibility checks. |
| P2: primary workflows | Workbench empty/composer layout; Reading Studio start state and staged controls. | First-time user can start each primary task without opening Help. |
| P3: supporting screens | Settings model readiness/actions, history editing/copy actions, concise About/Help. | Complete task flows retain state and communicate loading, saving, errors, and recovery. |

Implement the first packet narrowly before changing the overall composition.
Then prototype one coherent Workbench and Reading Studio direction, review the
actual rendered app, and propagate the shared components. Retain existing
workflow/session ownership and avoid coupling visual changes to inference,
capture, history storage, or publishing behavior.

Relevant source owners: `Theming/ControlStyles.xaml`, `Theming/DesignTokens.xaml`,
`Presentation/AppThemeManager.cs`, `Workbench/WorkbenchSidebarView.xaml`,
`Workbench/TextboxWorkbenchWindow.xaml.cs`, `Workbench/WorkbenchComposerView.xaml`,
`Workbench/WorkbenchQuickSettingsView.xaml`, `Workbench/Reading/ReaderSidebarView.xaml`,
`Workbench/Reading/ReaderWindow.xaml`, `Settings/SettingsPanel.xaml`, and
`History/HistoryWindow.xaml`.
