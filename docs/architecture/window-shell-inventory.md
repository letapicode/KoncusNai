# First-party window shell inventory

This inventory defines which shared shell mechanics each first-party window consumes. Window workflow code must not reimplement native-theme initialization or ordinary caption-button behavior.

| Window | Class | Shell ownership |
| --- | --- | --- |
| Workbench | Specialized editor | Custom layout and Win32 sizing hook; shared theme behavior and caption buttons |
| Settings | Standard | Shared theme behavior, window chrome tokens, and caption buttons |
| Dictation History | Specialized editor | Shared theme behavior, window chrome tokens, and caption buttons |
| About | Standard | Shared theme behavior, window chrome tokens, and caption buttons |
| Reading Studio help | Standard | Shared theme behavior, window chrome tokens, and caption buttons |
| Reading Studio | Specialized editor | Shared theme behavior; feature-owned command bar and layout |
| YouTube publishing | Specialized tool | Shared theme behavior; feature-owned title/actions because publishing state is embedded in its header |
| First-run setup | Standard native window | Shared theme behavior; native caption controls |
| Hotkey settings | Standard native tool | Shared theme behavior; native caption controls |
| Hotkey test | Standard native tool | Shared theme behavior; native caption controls |
| Reader completion toast | Toast | Deliberately transparent, non-activating, and exempt from native DWM/caption behavior |

## Shared primitives

- `WindowThemeBehavior` owns native DWM theme application when a window handle is created. `AppThemeManager` reapplies the selected palette and native theme to open windows when the preference changes.
- `WindowCaptionButtons` owns minimize, maximize/restore, close, glyph state, tooltips, and accessible names for ordinary custom-chrome windows.
- `WindowDragRegionBehavior` owns drag and double-click maximize behavior for custom title regions.
- `WindowShellOperations` is the narrow command boundary used by the caption and drag-region components.
- `ControlStyles.xaml` owns caption sizes, colors, typography, hover/pressed states, and glyph font.

The remaining shell work is visual verification at dark/light theme, 100% and 200% scaling, maximized/restored state, keyboard navigation, and high-contrast/accessibility review; see `docs/quality/window-shell-manual-evidence.md` for the test matrix and evidence. A single base window is intentionally not part of the design.
