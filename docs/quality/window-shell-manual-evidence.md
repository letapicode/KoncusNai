# First-Party Window Shell Manual Evidence (WP-09)

## Executive Summary

- **Backlog Item:** `UI-SHELL-001` (Establish small reusable first-party window shell primitives)
- **Work Packet:** `WP-09` (Window shell manual evidence)
- **Status:** Closed by operator direction. Dated direct PASS/BLOCKED evidence is retained exactly as observed. On 2026-09-14 the operator declined the remaining shell, History, Narrator/version, validation/recovery, and conditional-surface cases; they are **SKIPPED / NOT TESTED**, not PASS. This is an explicit evidence waiver, not a claim of complete manual accessibility or screen-reader conformance.
- **Evidence Reconciled:** 2026-09-14
- **Source Checkpoint Reviewed:** `ef24d0d30d86f8bbed8184590ff60eba0d9a1992` (`ef24d0d`) for the final pre-closure evidence state. Earlier observations retain their own historical build identities below.
- **Manual Build:** The last prepared Narrator build was Release commit `ac305596e42edc63f2d423f7d2c51676e6616ae3`; no Narrator case was executed on it. Its locked restore and zero-warning Release build passed, and accessibility preflight passed 19/19 self-tests and 168/168 exact-commit automated tests with 26 scenarios, 131 control expectations, 11 shells, 51 states, and 12 operator cases. No executable hash was supplied, so none is inferred. The shared sidebar-glyph correction in this document's containing commit remains automated-only pending ordinary product use.

---

## 1. Fixed Build Identification

The earlier fixed-build record is retained only as preparation evidence; it is not evidence that any manual case ran. A new fixed build must be identified before manual execution.

Before that execution, build the selected commit and run:

```powershell
.\scripts\run-accessibility-preflight.ps1 -SelfTest
.\scripts\run-accessibility-preflight.ps1 -ExpectedCommit <40-character-commit>
```

The second command requires a clean tree and a Release executable whose product
version contains the exact commit. It compares the 11-window policy with
`docs/architecture/window-shell-inventory.md`, verifies XAML paths, classes,
dimensions, resize modes, caption boundaries, 51 declared states, and bounded
STA shell/focus tests. It writes a sanitized 12-case operator matrix under
`artifacts/accessibility-preflight/<run>/window-shell-operator-matrix.md`.
Every generated result starts as **NOT TESTED**. The generated file prepares an
operator session and is not durable manual evidence.

| Attribute | Verified Value |
| --- | --- |
| **Prepared Commit** | `7d5a9045c2fcfa7169a212a796bb25f6eb395757` (`7d5a904`); superseded for future manual execution |
| **Target Framework** | `.NET 8.0 Windows` (`net8.0-windows`) |
| **Configuration** | `Release` |
| **Executable Path** | `src\DictateAnywhere.App\bin\Release\net8.0-windows\DictateAnywhere.App.exe` |
| **Executable Size** | Must be captured for the selected manual build |
| **SHA-256 Hash** | Must be captured for the selected manual build |
| **Assembly DLL** | `src\DictateAnywhere.App\bin\Release\net8.0-windows\DictateAnywhere.App.dll` |
| **Dependencies** | Locked to committed project graph; no packages added or upgraded |

---

## 2. Test Environment & Baseline Configuration

The host environment details collected from the active system:

| Parameter | Host Specification | Notes |
| --- | --- | --- |
| **OS Edition** | Windows 10 Home (DisplayVersion `25H2`) | Internal platform: Windows NT 10.0.26200.9278 |
| **OS Build** | `26200.9278` | Windows 11 platform build |
| **GPU / Video** | Intel(R) Iris(R) Xe Graphics | Resolution 3840 x 2160 @ 59 Hz |
| **Display Arrangement** | Dual-display configuration | Primary: Dell 3840x2160 (60x34 cm); Secondary: Samsung 3840x2160 (34x19 cm) |
| **Baseline Scale** | **150%** (`AppliedDPI = 144`) | System applied DPI on primary display |
| **Target Scale Matrix** | **100%** (96 DPI), **150%** (144 DPI), **200%** (192 DPI) | Operator-assisted scale verification targets |
| **Baseline Windows Theme** | **Dark** (`AppsUseLightTheme = 0`, `SystemUsesLightTheme = 0`) | Configured in Windows Personalization |
| **Baseline App Theme** | **Dark** (`themePreference: 2` in `settings.json`) | `AppThemePreference.Dark` (enum: `System = 0`, `Light = 1`, `Dark = 2`). Setting in `%LOCALAPPDATA%\DictateAnywhere\settings.json` |
| **Baseline High Contrast** | **Disabled** (`Flags = 126`, bit `HCF_HIGHCONTRASTON` = 0) | Standard color profile active |

### Restoration Baseline
After completing theme, scaling, or contrast test variations, the operator must restore the system to:
1. Display Scaling: **150%** on Primary Display.
2. Windows Theme: **Dark** (`Settings > Personalization > Colors > Choose your mode: Dark`).
3. High Contrast: **Off** (`Left Alt + Left Shift + PrintScreen` or `Settings > Accessibility > Contrast themes: None`).
4. Notype Settings: `settings.json` preserved intact without resetting user data or history.

---

## 3. Reconciled Window Shell Inventory & Dimensions

Reconciled from `docs/architecture/window-shell-inventory.md` and complete code inspection of `DictateAnywhere.App` XAML definitions:

| Window Name | Class | Shell Ownership Category | Reachability & Invocation | Caption Control Type | Sizing Constraints (XAML) | Exempt / Notes |
| --- | --- | --- | --- | --- | --- | --- |
| **Workbench** | `TextboxWorkbenchWindow` | Specialized editor | Tray click, global hotkey (Ctrl+Alt+W), or startup | `WindowCaptionButtons` (Min, Max/Restore, Close) | `Width="1320" Height="780" MinWidth="1040" MinHeight="680"`, Resizable, Maximizable | First-party primary |
| **Settings** | `SettingsWindow` | Standard window | Workbench gear button or Tray > Settings | `WindowCaptionButtons` (Min, Max/Restore, Close) | `Width="1040" Height="760" MinWidth="900" MinHeight="640"`, Resizable, Maximizable | First-party primary |
| **Dictation History** | `HistoryWindow` | Specialized editor | Settings > Manage Dictation History or Tray > History | `WindowCaptionButtons` (Min, Max/Restore, Close) | `Width="980" Height="650" MinWidth="760" MinHeight="520"`, Resizable, Maximizable | First-party primary |
| **Reading Studio** | `ReaderWindow` | Specialized editor | Workbench > Reading Studio button or Tray > Reader | `WindowCaptionButtons` (Min, Max/Restore, Close) | `Width="1380" Height="860" MinWidth="1040" MinHeight="700"`, Resizable, Maximizable | First-party primary |
| **About Notype** | `AboutWindow` | Standard dialog | Workbench > Help (?) > About Notype | `WindowCaptionButtons` (Min, Max/Restore, Close) | `Width="920" Height="740" MinWidth="720" MinHeight="560"`, Resizable, Maximizable | First-party secondary |
| **Reading Studio Help** | `ReadingStudioHelpWindow` | Standard dialog | Reading Studio > Help (?) button | `WindowCaptionButtons` (Min, Max/Restore, Close) | `Width="920" Height="740" MinWidth="720" MinHeight="560"`, Resizable, Maximizable | First-party secondary |
| **YouTube Publishing** | `YouTubePublishingWindow` | Specialized tool | Reading Studio > Export > Publish to YouTube | Dedicated `ClosePublishingButton` | `Width="860" Height="820" MinWidth="760" MinHeight="680"`, Resizable modal | Embedded header workflow |
| **First-Run Setup** | `FirstRunWizardWindow` | Standard native window | Clean install when `!HasCompletedFirstRun` | Native DWM caption controls | `Width="760" Height="600"`, Fixed-size modal dialog | Standard native frame with `WindowThemeBehavior` |
| **Hotkey Settings** | `HotkeySettingsWindow` | Standard native tool | Standalone tool entry point | Native DWM caption controls | `Width="700" Height="360"`, Fixed-size modal tool | Standard native frame with `WindowThemeBehavior` |
| **Hotkey Test** | `HotkeyTestWindow` | Standard native tool | Settings > Hotkey test button / First-Run Wizard | Native DWM caption controls | `Width="760" Height="500"`, Resizable native tool | Standard native frame with `WindowThemeBehavior` |
| **Reader Completion Toast** | `ReaderCompletionToastWindow` | Toast | Reading Studio narration complete | None (non-activating) | `Width="390" Height="118"`, Non-resizable floating toast | **Criterion-exempt:** Deliberately non-activating popup (`WS_EX_NOACTIVATE`); caption and window sizing controls N/A; theme, scaling, and legibility checks apply |
| **Overlay Indicator** | `OverlayIndicatorWindow` | Floating indicator | Dictation active | None (non-activating) | `Size(84, 28)`, Non-resizable floating pill | **Criterion-exempt:** WinForms non-activating top-most pill (`WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW`); caption and window sizing controls N/A; theme, scaling, and legibility checks apply |

---

## 4. Test Matrix & Scope

Each non-exempt window is evaluated across the following dimensions:
- **T-DARK**: Dark Theme in-app and native DWM dark title bar.
- **T-LIGHT**: Light Theme in-app and native DWM light title bar.
- **S-100**: 100% Display Scaling (96 DPI) layout and text sharpness.
- **S-150**: 150% Display Scaling (144 DPI) default baseline.
- **S-200**: 200% Display Scaling (192 DPI) text wrapping, hit targets, and no clipping.
- **W-MAX**: Maximize and Restore state transitions, glyph toggling (`\uE922` to `\uE923`).
- **W-RESIZE**: Window edge resize behavior and min-size constraints.
- **K-NAV**: Caption button keyboard focus via Tab/Shift+Tab.
- **K-ACT**: Caption button activation via Space/Enter and Alt+Space window menu.
- **K-NAME**: Accessible automation names (`AutomationProperties.Name`) and tooltips.
- **HC**: Windows High Contrast mode compatibility and legibility.

### Matrix Overview Table

> **Note on Status:** All manual interaction cells are marked **Not tested**. No manual interaction passes or failures are claimed without dated operator-captured execution evidence. Automated primitive test results are tracked separately.

| Case Group | Window | Dark Theme | Light Theme | 100% Scale | 150% Scale | 200% Scale | Max / Restore | Resize Borders | Caption Kbd Focus | Caption Activation | Accessible Names | High Contrast | Manual Execution Status |
| --- | --- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **SHELL-WB** | Workbench | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-SET** | Settings | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-HIST** | Dictation History | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-RDR** | Reading Studio | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-ABT** | About Notype | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-RHLP** | Reading Studio Help | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-YTP** | YouTube Publishing | Not tested | Not tested | Not tested | Not tested | Not tested | N/A | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-FRW** | First-Run Setup | Not tested | Not tested | Not tested | Not tested | Not tested | N/A | N/A | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-HKT** | Hotkey Test | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-HKS** | Hotkey Settings | Not tested | Not tested | Not tested | Not tested | Not tested | N/A | N/A | Not tested | Not tested | Not tested | Not tested | Not tested |
| **SHELL-TST** | Toast Window | Not tested | Not tested | Not tested | Not tested | Not tested | N/A | N/A | N/A | N/A | N/A | Not tested | Not tested |
| **SHELL-OVL** | Overlay Window | Not tested | Not tested | Not tested | Not tested | Not tested | N/A | N/A | N/A | N/A | N/A | Not tested | Not tested |

---

## 5. Defect Validation & Source-Inspection Findings

Each proposed shell issue was validated against current supported product behavior, callers, settings normalization, and test suites:

### DEFECT-SHELL-001 (Proposed: System OS Theme Sync) — UNCONFIRMED / OUT OF SCOPE
- **Proposed Claim:** `AppThemeManager` does not track Windows OS theme transitions under "System" preference.
- **Validation Against Product Contract:**
  - `AppSettings.Default.ThemePreference` is `AppThemePreference.Dark` (value 2).
  - Settings UI (`SettingsPanel.xaml`) does not expose any theme selection surface.
  - Workbench UI (`TextboxWorkbenchWindow.xaml.cs` lines 1271–1278) exposes an explicit menu toggle that switches strictly between `Light` (1) and `Dark` (2).
  - In `src\DictateAnywhere.App\Presentation\AppThemeManager.cs` (lines 141–144), `SubscribeToSystemThemeChanges` explicitly documents:
    `// Theme choice is explicit (Dark or Light), so Windows theme changes are ignored.`
  - `AppThemePreference.System = 0` is an unexposed/unsupported enum value, not an active or supported product feature. The current product contract defines theme choice as an explicit user selection between Dark and Light.
- **Verdict:** **UNCONFIRMED / NOT A DEFECT**. Introducing dynamic OS system theme tracking would be a new feature expanding product scope, not a defect fix against current supported behavior.

### DEFECT-SHELL-002: WindowCaptionButtons lack visible keyboard focus cues — RESOLVED (AUTOMATED EVIDENCE VERIFIED / MANUAL VALIDATION PENDING)
- **Severity:** S2 (Accessibility ACCESS-001 / WCAG 2.4.7 Focus Visible)
- **Affected Windows:** All custom chrome windows using `WindowCaptionButtons` (Workbench, Settings, Dictation History, Reading Studio, About, Reading Studio Help).
- **Source Inspection Location:** `src\DictateAnywhere.App\Theming\ControlStyles.xaml` (lines 656–714, styles `AppChromeWindowButtonStyle` and `AppChromeCloseButtonStyle`).
- **Inspection Finding:**
  - `AppChromeWindowButtonStyle` explicitly sets `FocusVisualStyle="{x:Null}"`.
  - Its `ControlTemplate.Triggers` handled only `IsMouseOver` and `IsPressed`.
  - `AppChromeCloseButtonStyle` explicitly overrode the base template without focus visual triggers.
  - When navigating title bar controls via `Tab` or `Shift+Tab`, focus successfully landed on the button (`IsKeyboardFocused` becomes true), but there was zero visual feedback, violating `ACCESS-001` keyboard accessibility requirements.
- **Implementation:**
  - Added `IsKeyboardFocused = True` triggers to both `AppChromeWindowButtonStyle` and `AppChromeCloseButtonStyle` targeting `Chrome.BorderBrush = {DynamicResource Brush.Control.Primary}`.
  - Preserved 1px border thickness with `Transparent` default border brush on `Chrome` to eliminate any layout shift when focus arrives or leaves.
  - Retained mouse hover and press styling (`Brush.Control.Muted`, `Brush.Control.Pressed`, and Close red hover `#FFE81123` / press `#FFC50F1F`) without conflict, keeping focus indicators visible during hover and press.
  - Preserved button dimensions (44×34), hit targets, glyph visibility, accessible names, and tab order.
- **Automated Evidence:**
  - Added `WindowShellPrimitivesTests.CaptionButtons_ApplyKeyboardFocusIndicatorAndRestoreNormalRendering` on an STA thread verifying all caption styles (`MinimizeButton`, `MaximizeRestoreButton`, `CloseButton`):
    - Unfocused state renders transparent border (44×34).
    - Focused state applies `Brush.Control.Primary` border brush without changing dimensions or layout.
    - Focus removal restores transparent border rendering.
    - Sequential Tab focus traversal (`Minimize` -> `Maximize/Restore` -> `Close`) succeeds.
    - Maximize/restore state transitions preserve focus indicator, glyph switching, and accessible names.
- **Manual Verification Status:** **Pending interactive validation** (Awaiting dated operator execution in `SHELL-WB-03` and manual matrix; STA tests do not substitute for interactive manual confirmation).
- **Verdict:** **IMPLEMENTATION COMPLETE / MANUAL EVIDENCE PENDING**.

### DEFECT-SHELL-003: Custom chrome surfaces do not respond to Windows High Contrast mode — PALETTE RESOLVED; OUTER HWND BOUNDARY SUPERSEDED BY DEFECT-SHELL-004
- **Severity:** S2 (Accessibility ACCESS-001 / High Contrast Conformance)
- **Affected Windows:** All custom chrome windows (Workbench, Settings, Dictation History, Reading Studio, About, Reading Studio Help, YouTube Publishing).
- **Source Inspection Location:** `src\DictateAnywhere.App\Presentation\AppThemeManager.cs` & `ControlStyles.xaml`.
- **Inspection Finding:**
  - `AppThemeManager` previously applied hardcoded RGB palette colors (`DarkPalette`, `LightPalette`) and did not monitor or adapt to `SystemParameters.HighContrast`.
  - Custom chrome brushes (`Brush.Surface.Canvas`, `Brush.Border.Subtle`, `Brush.Text.Secondary`) remained bound to static dark/light palette colors rather than adapting to system high-contrast colors (`SystemColors.WindowBrushKey`, `SystemColors.WindowTextBrushKey`, `SystemColors.HighlightBrushKey`, etc.).
- **Implementation:**
  - Added `IsHighContrastActive()` detection via `SystemParameters.HighContrast` with safe exception handling.
  - Implemented `GetHighContrastPalette()` in `AppThemeManager` mapping all 38 theme brush keys to semantic system colors (`SystemColors.WindowColor`, `SystemColors.WindowTextColor`, `SystemColors.HighlightColor`, `SystemColors.HighlightTextColor`).
  - Added `isHighContrast` parameter to `ApplyPalette` and updated `ApplyThemeResources` to detect and apply high contrast dynamically.
  - Updated `SubscribeToSystemThemeChanges` to listen to `UserPreferenceCategory.Accessibility` and `UserPreferenceCategory.Color`, reapplying theme resources whenever high contrast is enabled, changed, or disabled while continuing to ignore standard OS theme changes for explicit Dark/Light preference.
  - Hardened STA test lifetime and bounded timeouts in `WindowShellPrimitivesTests.cs` (`CaptionButtons_ApplyKeyboardFocusIndicatorAndRestoreNormalRendering` and `CaptionButtons_OwnWindowCommandsStateAndAccessibleNames`) with `finally { window?.Close(); }` and 10s bounded joins.
- **Automated Evidence:**
  - `AppThemeManagerTests`: Added `ApplyPalette_WhenHighContrastEnabled_AppliesSystemColorMappings`, `ApplyPalette_WhenHighContrastDisabled_PreservesStandardPalettes`, and `GetHighContrastPalette_ContainsAllRequiredPaletteKeys`.
  - Full Release solution gate passes: 874 passed, 2 skipped, 0 failed.
- **Manual Verification Status:** **Pending interactive validation** (Awaiting dated operator execution in `SHELL-HC-01` and manual matrix; automated tests do not substitute for interactive manual confirmation).
- **Verdict:** **PALETTE IMPLEMENTATION COMPLETE**. The later direct
  `DEFECT-SHELL-004` observation showed that semantic client-area colors did not
  by themselves produce a visible outer HWND boundary.

### DEFECT-SHELL-004: Restored custom window loses its outer boundary in Windows High Contrast — SECOND CORRECTION IN CODE / MANUAL RERUN PENDING

- **Severity:** S1 accessibility regression because adjacent windows and the
  desktop cannot be reliably distinguished.
- **Direct observation:** On 2026-09-10, the operator used the Release build from
  commit `5cc97c701798270c2ed0a00bee66b8d3d8d56bda` on Windows 11 Version 25H2,
  build 26200.9278, at 1920 by 1080, one laptop display, 125% display scaling,
  100% Windows text size, Notype Light theme, and the Windows Dusk contrast
  theme. The restored Workbench had no visually distinguishable outside edge.
  The edge was visible again when Contrast themes was restored to None. A
  supplied screenshot showed readable internal controls and dividers but no
  reliable outside boundary.
- **Root cause:** `WindowThemeBehavior` applied only immersive Light/Dark DWM
  attributes. The implicit WPF `Window.BorderBrush` property and High Contrast
  client palette did not cause Windows to render the actual non-client boundary;
  the ordinary DWM shadow/border disappeared in the Dusk environment.
- **First correction and result:** WP-25-FIX-05 made `AppThemeManager` assign the
  Windows 11 DWM border-color attribute for custom-chrome windows. High Contrast
  uses the semantic `SystemColors.WindowTextColor`; leaving High Contrast restores
  the DWM default sentinel. On 2026-09-10 the operator reran commit
  `6518ec5529d07fa596dfb77e2c07d80bb1a4927f` in the same Dusk environment.
  About Notype and Reading Studio Help showed a thin, clearly distinguishable
  light client boundary (**PASS**), but restored Workbench and Reading Studio
  still had no reliable boundary against a similarly dark desktop (**FAIL**).
  Their surface remained distinguishable against a white desktop, which does not
  satisfy the matching-background case. History, Settings, and Publishing were
  not reported as directly observed and remain **Not tested**.
- **Second root cause:** DWM border color is a best-effort non-client hint and did
  not provide a visible edge for the affected custom-chrome HWNDs. About and Help
  differed because their root `Border` elements already rendered a one-pixel
  semantic boundary inside the client area. Workbench, Settings, History, Reading
  Studio, and Publishing had no equivalent client-rendered owner.
- **Second correction:** WP-25-FIX-06 retains the DWM hint as complementary
  native behavior and adds `AppHighContrastWindowBoundaryStyle`, driven by the
  inherited `WindowThemeBehavior.IsHighContrastActive` state. Exactly one
  noninteractive overlay boundary is present on Workbench, Settings, History,
  Reading Studio, and Publishing. About and Help retain their existing root
  boundaries and do not receive an overlay. Native-chrome windows retain Windows
  ownership, and the deliberately transparent non-activating Reader completion
  toast remains exempt. The overlay occupies the existing root grid, adds no
  padding or margin, does not participate in hit testing or focus, and suppresses
  its thickness while maximized or outside High Contrast.
- **Automated scope:** deterministic XAML inventory checks require all seven
  custom/specialized windows to have exactly one client-rendered boundary and
  reject DWM-only coverage or duplicate overlays. Bounded STA rendering checks
  cover semantic resource resolution, runtime High Contrast entry/exit,
  restored/maximized behavior, unchanged content/window measurements,
  noninteraction, z-order, and cleanup. Existing tests continue to protect DWM
  fallback, captions, focus, drag/resize ownership, and native-chrome exceptions.
- **Second-correction observation:** on 2026-09-10, after the exact-build
  preflight for commit `c2a12461073971d45a8fc8de7dc67546cb85cd7c`, the
  operator enabled the same Windows Dusk contrast environment and directly
  reported that the restored Workbench now showed the intended white boundary
  clearly and that the result looked correct. This is a direct **PASS** for the
  presence and visual distinguishability of the corrected restored-Workbench
  client boundary. It supersedes the earlier Workbench visual failure only for
  that observed state.
- **Remaining-boundary rerun:** on 2026-09-10, using the exact Release build from
  `c73d8fe4ae42f24191380ad73e38f363eea40a6d`, the operator reported completing
  the prescribed remaining High Contrast boundary checks and that every checked
  boundary, window-state, movement, resizing, and runtime-transition behavior
  looked correct. Workbench, Reading Studio, Settings, History, About, and
  Reading Studio Help boundary behavior is therefore **PASS** for the reachable
  prescribed states. Publishing reachability and restoration of the original
  Windows contrast setting were not itemized separately and are not inferred.
- **Verdict:** **CLIENT-BOUNDARY CORRECTION PASSED FOR REPORTED REACHABLE STATES**.

### DEFECT-SHELL-005: Reading Studio draft bounds expose asymmetric page gutters in High Contrast — CORRECTED IN CODE / MANUAL RERUN PENDING

- **Severity:** S2 accessibility/layout regression. The content remains usable,
  but the High Contrast focus/boundary treatment exposes an unbalanced document
  editing region that is not apparent in the ordinary contrast presentation.
- **Direct observation:** on 2026-09-10, in the same exact `c73d8fe` Release and
  Dusk contrast session, the operator reported that the distance from the page
  edge to the Reading Studio typing region was visibly different on the left and
  right. The operator reported ordinary contrast as visually correct. A supplied
  screenshot corroborated the asymmetric editor bounds, but the image and its
  test-owned text are not committed as evidence.
- **Measured baseline:** in a deterministic rendered Reader fixture with a
  1008-DIP page frame, the two-DIP frame padding plus `66/24` surface padding put
  the draft control 68 DIPs from the left frame edge and 26 DIPs from the right.
  The 914-DIP editor used `0,12,34,16` padding. Its content viewport was 876 DIPs
  without a scrollbar and 868 DIPs with the eight-DIP scrollbar. Consequently,
  the right-only text inset balanced long scrolling text but left both the
  visible editor outline and the no-scroll text region asymmetric.
- **Correction:** WP-25-FIX-07 makes `ReaderDocumentLayoutMetrics` the standard
  page/text geometry owner. The surface now uses `45,40,45,42`; the resulting
  page-to-control gutters are 47/47 DIPs before device-pixel rounding. Draft and
  read-only text reserve 21 DIPs on the left and 21 DIPs on the right when the
  scrollbar is hidden. When it is visible, only its actual width is subtracted
  from the right inset: 13 DIPs for the draft's eight-DIP scrollbar and 15 DIPs
  for the read-only surface's six-DIP scrollbar. Thus both effective sides remain
  68 DIPs, the draft content viewport remains 868 DIPs across the transition,
  and read-only text remains 872 DIPs wide. A rendered fractional-DPI fixture
  measured 47.333/46.667 outer gutters, within the one-DIP rounding tolerance.
- **Related Reader scrollbar defect:** rendering the read-only scrollbar exposed
  an invalid dynamic resource on `Style.BasedOn`. The six-DIP scrollbar style
  belonged only to the document view, so the correction moves it to that owner
  and uses a valid static base. No reading, editing, typography, focus, or
  workflow behavior changed.
- **Automated scope:** bounded rendered STA tests cover outer and effective
  gutters, hidden/visible scrollbar transitions, constant readable widths,
  read-only/draft alignment, minimum/intermediate/maximum page widths,
  Light/Dark/High Contrast geometry invariance, distraction-free geometry,
  Unicode/emoji/RTL/mixed text, long unbroken text, invalid geometry, focus
  layout neutrality, pending-dispatch cleanup, and deterministic closure. The
  shared preflight has a dedicated state-aware Reader gutter scenario. These
  objective checks do not establish human-perceived symmetry.
- **Manual classification:** **FAIL** for the Reading Studio High Contrast draft
  gutter symmetry at `c73d8fe`. Normal-contrast behavior is retained as a direct
  qualitative **PASS**, but exact normal-mode geometry was not measured manually.
- **Verdict:** **AUTOMATED CORRECTION COMPLETE; EXACT RELEASE VISUAL RERUN
  REQUIRED BEFORE THE 200% TEXT-SIZE BATCH**.

### DEFECT-SHELL-006: Reader High Contrast scrollbar and draft editing affordances — CORRECTED IN CODE / MANUAL RERUN PENDING

- **Direct observation:** on 2026-09-11, using the exact `2c9c5ee` Release at
  1920 by 1080, 125% display scaling, 100% text size, Notype Light, and Windows
  Dusk contrast, the operator reported the WP-25-FIX-07 gutter, scrollbar
  transition, resizing, content, focus, and theme checks otherwise working. On
  the light Reader page, however, the High Contrast scrollbar thumb was also
  light and difficult to distinguish. The draft hover tooltip displayed the
  long normal-help sentence with an oversized-looking popup, and after typing
  `hi` plus Tab, one Ctrl+Z removed the preceding text instead of only the Tab.
- **Correction:** WP-25-FIX-08 gives `ReaderDocumentView` local High Contrast
  scrollbar resources derived from the selected Reader page's ink color. Both
  read-only and draft scrollbars consume the same local keys; the
  keys are removed on High Contrast exit so normal Light/Dark application
  resources resume ownership. The six/eight-DIP scrollbar widths and text-lane
  reservation are unchanged. The normal tooltip/help is now `Edit narration
  text`; empty-draft validation remains actionable and separate from the stable
  `Reading draft` automation name. Plain Tab is inserted as a locked standalone
  WPF undo unit, including selection replacement, and programmatic draft loads
  clear stale undo history without changing Ctrl+Tab escape behavior.
- **Fractional-DPI hardening:** the symmetric Reader page frame opts out of the
  parent window's whole-DIP layout rounding. At the observed 125% scale this
  prevents two device pixels of one-sided rounding (46.4/48.0 DIPs) while
  retaining the same requested dimensions and symmetric layout metrics. The
  shared High Contrast client boundary likewise no longer rounds independently
  from its host, preventing a 0.4-DIP overlay mismatch without changing its
  brush, thickness, hit testing, or restored/maximized behavior.
- **Accepted platform behavior:** unmodified Home/End inside the multiline
  editor moves to the current visual line boundaries; Ctrl+Home/Ctrl+End are the
  document-boundary commands. Emoji glyphs may be monochrome when the selected
  Reader typeface or Windows fallback lacks color layers; the Unicode text was
  preserved, so no font or typography change is made.
- **Recording-indicator characterization:** the small object shown after
  Alt+Space is not draft content or a focus adorner. The existing overlay owner
  requests `AnchoredRecording`, and the non-activating presenter renders a
  bounded 84 by 28 pixel pill with a contrasting border, pulsing red recording
  dot, and elapsed time. Synthetic tests cover its size, state, no-taskbar/no-
  activation shell, and cleanup without starting capture. No product correction
  is justified absent a reproduction of a blank, clipped, focus-stealing, or
  stale indicator.
- **Automated scope:** rendered bounded-STA tests cover Reader-page-relative
  contrast for every built-in page palette, read-only and draft scrollbar
  consumption, runtime High Contrast entry/exit with invariant geometry,
  concise versus validation help, Tab undo isolation, selection replacement,
  programmatic-load history reset, Ctrl+Tab preservation, fractional-DPI gutter
  symmetry, and deterministic closure. Automation does not prove perceived
  scrollbar visibility or tooltip appearance.
- **Exact correction rerun:** on 2026-09-11 at `4c976ef`, the operator reported
  direct **PASS** for the Warm Paper, Midnight, and Abyss Black High Contrast
  scrollbar visibility/hover/drag states; repeated scrollbar transitions;
  Dusk-to-None-to-Dusk geometry; balanced gutters and outer boundary; isolated
  Tab undo including selection restoration; Ctrl+Tab/Ctrl+Shift+Tab escape;
  line/document Home/End contracts; and Unicode/RTL/emoji text preservation.
  The normal draft hover popup remained visually oversized, so that portion is
  direct **FAIL** rather than PASS.
- **Additional observations:** the Reader folder and help icon buttons have
  meaningful automation names but show no pointer-hover tooltip. A later exact
  `2a82ecb` clarification separated the accepted bottom-center recording pill
  from the unwanted inner Workbench Prompt focus rectangle exposed by Alt+Space.
- **Verdict:** **SCROLLBAR AND UNDO CORRECTIONS PASSED; TOOLTIP RENDERING,
  ICON HOVER HELP, AND THE SEPARATELY CHARACTERIZED COMPOSER FOCUS RECTANGLE
  REQUIRE CORRECTION AND RERUN BEFORE THE 200% TEXT-SIZE BATCH**.

### DEFECT-SHELL-007: Reader hover help — CORRECTED / MANUAL RERUN PASSED

- **Direct observation retained:** on 2026-09-11 at exact `4c976ef`, the
  operator found that the draft's concise help still rendered as an excessively
  tall popup and the Reading Studio folder/help icons had no pointer-hover help.
  These are direct **FAIL** observations only for those states; the preceding
  Reader scrollbar, gutter, undo, keyboard, navigation, and text-preservation
  results remain direct **PASS** evidence.
- **Tooltip root cause and correction:** `DraftTextBox` receives the selected
  Reader typeface, 23-point size, and explicit block line height. Its former
  string ToolTip inherited that placement-target typography through the popup,
  creating excess vertical space around one short line. `ReaderDocumentView`
  now owns one bounded ToolTip with explicit Segoe UI 12/16 metrics and wrapped
  content. Normal and validation help update that same content element, embedded
  whitespace is normalized, and the `Reading draft` automation name remains
  unchanged. The Reader folder and help buttons now pair their existing names
  with `Open document` and `Reading Studio help` tooltips.
- **Automated scope:** bounded WPF tests measure compact tooltip typography,
  normal/validation replacement, target-size neutrality, stable accessible
  names, and both icon tooltips. Automation does not prove perceived tooltip
  compactness.
- **Exact correction rerun:** on 2026-09-12 at exact `9912a66`, the operator
  reported direct **PASS** for the folder, help, and compact draft tooltips,
  including the prescribed Light, Dark, and Dusk variants. The stable Reader
  accessible name and unchanged Reader geometry were also reported as correct.
- **Manual classification:** the Reader tooltip correction is direct **PASS**.
  All remaining 200% text, display-scaling, reduced-motion, screen-reader, and
  safe-recovery states retain their actual **Not tested** status.
- **Verdict:** **CORRECTION AND FOCUSED MANUAL RERUN PASSED**.

### DEFECT-SHELL-008: Workbench Prompt inner focus rectangle — CORRECTED / MANUAL RERUN PASSED

- **Corrected direct observation:** at exact `2a82ecb`, the operator clarified
  that the floating red-dot/timer and transcribing animation near monitor
  bottom-center were acceptable direct **PASS** observations. The unwanted
  direct **FAIL** is a separate white rectangle inside the focused `Ask Notype`
  text area after Alt+Space. No manual result is inferred for the correction.
- **Root cause:** `ComposerTextBoxStyle` replaced the base TextBox focus trigger
  with the generic two-border `AppKeyboardFocusVisualStyle`. Alt keyboard-cue
  processing revealed that adorner around the transparent Prompt's own bounds;
  dictation state, `HotkeyStatus`, and the operational-status view do not inject
  a shape into the editor.
- **Correction:** the Prompt no longer owns the generic inner adorner. Pointer
  focus remains undecorated, while keyboard-origin focus and Alt cues change
  only the existing outer `CompactComposer` border brush to the semantic primary
  focus resource. Border thickness, padding, Prompt viewport, actions, and
  responsive measurements do not change, and the cue is cleared when focus or
  the compact surface leaves. Recording state remains independent.
- **Overlay restoration:** the focus anchor again selects a monitor only, and
  `OverlayIndicatorPlacementPolicy` places the existing non-activating,
  input-transparent pill at that work area's bounded bottom center. Caret-first
  anchor selection and cached post-recording state preserve the established
  state transition. The red dot, timer, transcribing animation, messages, and
  no-anchor fallback remain unchanged.
- **Exact correction rerun:** on 2026-09-12 at exact `9912a66`, the operator
  directly confirmed that the inner rectangle is absent, the complete rounded
  composer receives the intended focus indication, composer-button focus and
  cleanup remain correct, and the recording/transcribing indicator remains at
  monitor bottom-center. All other prescribed WP-25-MANUAL-11 behavior was
  reported as working.
- **Manual classification:** the Workbench composer correction and centered
  indicator are direct **PASS**. The separately observed Reader draft focus
  defect below is not attributed to this owner.
- **Verdict:** **CORRECTION AND FOCUSED MANUAL RERUN PASSED**.

### DEFECT-SHELL-009: Reading Studio draft nested focus rectangle — RESOLVED FOR REPORTED DUSK STATE

- **Direct observation retained:** on 2026-09-12 at exact `9912a66`, the
  operator reported direct **FAIL** for a second white rectangle inside the
  Reading Studio draft. The complete editor/page boundary was already visible,
  but Alt or Alt+Space exposed an inset rectangle around the transparent text
  editor; Windows Dusk High Contrast made that nested rectangle visible
  automatically or especially prominently. The operator reported all other
  WP-25-MANUAL-11 checks as working.
- **Root cause:** `ReaderDraftInput` explicitly consumed the generic two-border
  `AppKeyboardFocusVisualStyle`. Windows keyboard-cue processing rendered that
  adorner over the TextBox bounds inside the already bounded Reader page, so the
  Reader presented two competing focus boundaries.
- **Correction:** the TextBox no longer owns the generic inner adorner. One
  Reader-owned, noninteractive `DraftFocusBoundary` overlays the complete
  `ReaderPageFrame`, uses the semantic primary focus brush for keyboard/Alt
  focus, and remains transparent for pointer focus and after focus exits.
  Width, height, padding, gutters, scrollbar reservation, and text viewport do
  not change between focus states; runtime High Contrast resource changes flow
  through the semantic brush.
- **Automated scope:** bounded rendered-WPF coverage proves the nested adorner is
  absent, the replacement boundary is co-owned with the page frame and excluded
  from hit testing/focus traversal, pointer and keyboard origins remain distinct,
  Alt cues use the same owner, and focus entry/exit leaves Reader geometry
  invariant. Automation does not prove perceived focus quality.
- **Exact correction rerun:** on 2026-09-12 at exact `d833f91`, the operator
  supplied a Dusk High Contrast screenshot showing that the inner white
  rectangle remains around the complete draft TextBox even when Alt has not
  been pressed. With Windows Contrast themes set to None, the rectangle is not
  visible even after Alt. The intended rounded blue Reader boundary is visible
  separately outside it. This proves that removing `FocusVisualStyle` did not
  remove the High Contrast TextBox/template chrome.
- **Manual classification:** direct **FAIL** in Dusk High Contrast. Ordinary
  contrast without the rectangle is a direct **PASS** observation for that
  narrow condition. The remaining MANUAL-12 cases were stopped after this
  failure and retain **Not tested** status.
- **FIX-12 root cause:** `ReaderDraftInput` set zero border thickness and null
  focus style but did not own its `ControlTemplate`; Windows therefore supplied
  the TextBox theme template, whose High Contrast chrome remains independent of
  the removed focus adorner. The rectangle followed the TextBox bounds because
  that uncontrolled template was its remaining owner.
- **FIX-12 correction:** `ReaderDraftInput` now explicitly consumes a minimal
  Reader-owned borderless template. Its direct, nonfocusable
  `PART_ContentHost` binds the existing padding, background, and horizontal/
  vertical scrollbar policies. No internal Border exists, and
  `DraftFocusBoundary` remains the sole intentional editor-focus boundary.
  Padding rather than margin preserves the established viewport-width and
  single-scrollbar-lane geometry.
- **Automated scope:** rendered WPF coverage proves the resolved template is the
  Reader-owned template, `PART_ContentHost` is the editor's direct visual child,
  the host is nonfocusable, no template Border exists, and hidden/visible
  scrollbar, focus, gutter, and viewport measurements remain unchanged.
- **Exact correction observation:** on 2026-09-12 at exact Release commit
  `7b529e5`, the operator directly reported that the inset rectangle is no
  longer visible in Dusk and considered the defect fixed. This is direct
  **PASS** evidence for the corrected Dusk state that originally failed.
- **Evidence boundary:** the operator did not separately report the MANUAL-13
  Alt-only, focus-exit, scrollbar, resize, sidebar, or repeated runtime-contrast
  variants. Those variants remain **Not tested** and are not inferred from the
  Dusk result or automation.
- **Verdict:** **FIX-12 CORRECTED DUSK STATE PASSED; WP-25 REMAINS OPEN FOR THE
  UNREPORTED VARIANTS AND REMAINING ACCESSIBILITY MATRIX**.

### DEFECT-SHELL-010: Reading Studio slash-wrap editing flicker — DIRECT FAIL / CHARACTERIZATION REQUIRED

- **Direct observation:** on 2026-09-12 at exact Release commit `7b529e5`, the
  operator filled the Reading Studio draft with repeated copies of:
  `Resize continuously; test minimum size, just above minimum, maximize/restore, and sidebar hidden/shown. &#x20;`
  While editing earlier text, slash positions visibly flickered. The behavior
  occurred with Windows Contrast themes set to both None and Dusk. Removing the
  slash stopped the observed flicker; ordinary sentence text without the slash
  did not reproduce it.
- **Related pass retained:** pressing Alt did not restore the inset white
  TextBox rectangle. The operator reported the other executed MANUAL-14 Reader
  checks working.
- **Proven root cause:** each draft keystroke raised `DraftChanged`, and
  `ReaderWindow.OnDraftChanged` synchronously rendered the presentation.
  `RenderDistractionFree(false)` then unconditionally replaced the compensated
  visible-scrollbar padding `21,12,13,16` with the uncompensated standard value
  `21,12,21,16`. The queued inset pass restored `21,12,13,16`. This transient
  eight-DIP viewport-width excursion rewrapped and repainted the draft on every
  edit; slash positions made the movement especially visible because slash is
  a line-wrap opportunity. A rendered window-level regression failed on the
  baseline with the exact 13→21 DIP reset before the correction.
- **FIX-13 correction:** normal presentation no longer resets draft padding.
  Text, size, extent, and viewport changes retain the existing coalesced inset
  measurement, while scroll-offset-only events no longer schedule an inset
  operation. The visible-scrollbar padding and viewport therefore remain stable
  synchronously and after dispatcher settlement without timers, debouncing, or
  character-specific handling.
- **Automated scope:** the exact supplied sentence plus forward slash,
  backslash, combined URL/path/fraction/entity-like, ordinary, Unicode, emoji,
  Devanagari, Arabic, and mixed-direction fixtures retain text and stable
  visible-scrollbar padding/viewport geometry through insertion and deletion.
  The same rendered test proves offset-only scrolling leaves no pending inset
  operation. Existing hidden/visible scrollbar, effective-gutter, focus,
  template, High Contrast, and bounded-cleanup coverage remains active.
- **Corrected-build observation:** on 2026-09-12 at exact Release commit
  `d7add29d3785231dd023d2619b1563a374fbb2e8`, the operator repeated the bounded
  slash-wrap rerun with scrollbar-bearing content and reported that the issue
  was gone. The operator explicitly classified the remaining requested checks
  as passing. No flicker, jumping, text loss, inset-rectangle recurrence, or
  scrollbar-layout instability was reported.
- **Tooltip clarification:** the draft tooltip follows the Notype application
  theme rather than the selected Reader page theme. A light tooltip is therefore
  expected when Notype is Light even if the document uses Midnight; Notype Dark
  uses the dark application palette, and High Contrast uses Windows system
  colors. This clarification is not a new manual contrast claim.
- **Manual classification:** **PASS** for the exact FIX-13 slash-wrap and
  scrollbar-layout correction rerun in ordinary and Dusk contrast. The 200%
  text-size batch was not started.
- **Verdict:** **FIX-13 CORRECTED RELEASE PASSED; CONTINUE WP-25 WITH THE
  SEPARATELY BOUNDED 200% WINDOWS TEXT-SIZE BATCH**.

### DEFECT-SHELL-011: Inconsistent 200% Windows text scaling and fixed-geometry clipping — DIRECT FAIL

- **Direct observation:** on 2026-09-12 at exact Release commit
  `caaba869c4a17c10c87231cf01693cf61f85817a`, with Notype Light, Windows
  Contrast themes None, 125% display scaling, and Windows text size 200%, the
  operator supplied four screenshots and reported that some text became large
  while other text remained small.
- **Visible Workbench result:** the Workbench sidebar action was visibly clipped
  as `New cha`, while other nearby fixed-size title and navigation text remained
  substantially smaller. The Quick Settings surface mixed enlarged inherited
  control text with smaller explicit labels.
- **Visible Settings result:** ComboBox, button, CheckBox, and hotkey values grew,
  but section headings, row labels, captions, and descriptive text remained near
  their fixed baseline sizes. The resulting hierarchy is inconsistent rather
  than a coherent 200% presentation.
- **Visible About/Help result:** both scroll regions remained available, which is
  acceptable, but headings and card body/caption text used visibly different
  scaling behavior. Scrollability does not make unscaled body text a PASS.
- **Visible Reader result:** sidebar ComboBox values enlarged and were clipped or
  truncated within fixed-width controls while 10/11-DIP micro labels and hints
  remained small. The Reading Studio Help body/card text likewise remained much
  smaller than inherited control text.
- **Source characterization:** applicable views contain numerous literal
  `FontSize` values while other controls inherit WPF system typography. Windows
  text-size changes therefore affect the two groups differently. Fixed heights,
  widths, column definitions, and nonwrapping rows then constrain the enlarged
  inherited content. A correction must prove the complete rendered ownership
  and must not merely enlarge every literal independently.
- **Manual classification:** **FAIL** for 200% Windows text-size consistency and
  reachability. No FAIL is assigned merely because About or Help requires a
  scrollbar. Unreported History details, publishing, first-run, and Reader
  completion-toast states retain their prior status.
- **Verdict:** **A BOUNDED SHARED TEXT-SCALE AND RESPONSIVE-GEOMETRY CORRECTION IS
  REQUIRED BEFORE CONTINUING THE ACCESSIBILITY MATRIX**.
- **FIX-14 correction (2026-09-13):** `AppTextScaleManager` now owns semantic
  application-chrome typography from the Windows message-font accessibility
  metric, without multiplying display DPI or replacing user-selected chat and
  Reader document sizes. Text-bearing controls can grow, sidebars widen within
  bounded limits, and multi-column Settings/About/Help content stacks at large
  text scales. Runtime updates are coalesced and detached during shutdown.
  Automated WPF/resource/layout checks and the preflight cover the corrected
  state, but they do not establish human-perceived readability.
- **WP-25-MANUAL-17 direct rerun (2026-09-13):** at exact Release commit
  `c8c44fe325a596ff8973f3bd41dd39c7bb82955b`, with 125% display scaling,
  Windows text size 200%, Contrast themes None, and Notype Light, the operator
  reported all prescribed Workbench, Quick Settings, Advanced Settings, and
  Dictation History checks passing. This includes normal/minimum/maximized
  Workbench states, sidebar shown/hidden, composer actions, coherent typography,
  keyboard reachability, focus attachment, and the absence of clipping,
  overlap, inconsistency, or unreachable primary content.
- **Second-case direct rerun:** the operator likewise reported the prescribed
  About Notype, Reading Studio, Reading Studio Help, scrolling, responsive
  layout, and document-geometry checks passing. After an explicit follow-up,
  the operator confirmed the 200%-to-100%-to-200% runtime transition, Dark and
  Dusk variants at 200%, and restoration of the original Windows text size,
  Notype Light theme, and contrast setting all passed. Conditional Publishing,
  first-run, and Reader-completion-toast states receive no inferred result from
  this general report and retain their prior status.
- **Corrected-state classification:** **PASS** for the directly exercised
  WP-25-MANUAL-17 primary surfaces and variants. The original `caaba86` failure
  remains historical defect evidence. WP-25 remains open for the unexecuted
  display-scaling/DPI, reduced-motion, RTL/mixed-direction workflow, remaining
  Narrator, validation/recovery, and conditional-surface evidence; WP-32 remains
  blocked.

### WP-25-MANUAL-18: Additional display-scaling and DPI evidence — DIRECT PASS / BLOCKED

- **Environment:** on 2026-09-13, the operator used exact Release commit
  `04658a1dc2ed6503066fef756e4c8675643d011b` on Windows 11 Version 25H2,
  build 26200.9278, at 1920 by 1080 on one laptop display. Windows text size
  remained 100%; Notype Light and Contrast themes None were the ordinary
  baseline.
- **100% and 200% scaling:** after receiving the complete two-case checklist,
  the operator reported everything looked correct and explicitly classified
  the cases PASS. The exercised checks covered runtime scaling and fresh launch;
  Workbench restored/minimum/maximized and sidebar states; composer actions;
  Quick Settings placement and containment; Advanced Settings scrolling;
  Dictation History and About; Reading Studio restored/minimum/maximized,
  sidebar, editor gutters, scrollbar, and focus; Reading Studio Help; popups,
  captions, resizing, and keyboard reachability. The prescribed 200% Dark and
  Dusk boundary/focus checks were included in that reported PASS.
- **150% scaling:** after a separate, complete checklist, the operator reported
  all prescribed runtime/fresh-launch, window-state, minimum-size, sidebar,
  composer, Settings, About, Reader, Help, screen-edge popup, rapid-resize,
  focus, gutter, and scrollbar checks correct and explicitly classified the
  case PASS.
- **Restoration:** the final checklist required resolution to remain 1920 by
  1080, Windows text size to remain 100%, and display scaling, Notype theme, and
  contrast to return to 125%, Light, and None respectively. The operator's
  all-checks PASS is recorded as confirmation of those prescribed cleanup
  steps; no contradictory result was reported.
- **Unavailable environment:** **BLOCKED** — per-monitor DPI and monitor-transfer
  behavior requires a second physical monitor, but only one display is
  available. Synthetic coverage is not reported as manual evidence.
- **Classification:** **PASS** for the directly exercised 100%, 150%, and 200%
  single-display scaling cases; **BLOCKED** for per-monitor/monitor-transfer DPI.
  No new defect was reported. WP-25 remains open for reduced motion,
  RTL/mixed-direction workflow, remaining Narrator semantics,
  validation/recovery, and conditional-surface evidence; WP-32 remains blocked.

### WP-25-MANUAL-19: Windows reduced-motion evidence — DIRECT PASS

- **Environment:** on 2026-09-13, the operator used exact Release commit
  `1520e2ebf05381f307366d7c8ac7424b00488529` on Windows 11 Version 25H2,
  build 26200.9278, at 1920 by 1080 on one laptop display, 125% display
  scaling, 100% Windows text size, Notype Light, and Contrast themes None.
- **Setting:** after rechecking Windows Settings, the operator corrected the
  original Windows Animation effects value to On. The setting was turned Off
  for the reduced-motion observations, the prescribed runtime On/Off
  transitions were exercised, and the final all-checks PASS confirms
  restoration to On. Transparency effects and the other baseline settings were
  required to remain unchanged and were included in that PASS.
- **Workbench, Settings, and History:** after receiving the complete case, the
  operator explicitly reported everything PASS. The exercised checks covered
  visible focus, five repeated sidebar and Quick Settings transitions, popup
  cancellation, containment and focus restoration, restored/minimized/
  maximized/minimum layouts, Advanced Settings traversal, and repeated safe
  Dictation History open/close and no-match presentation without editing or
  deleting real entries.
- **Reading Studio and safe status presentation:** the operator explicitly
  classified the complete second case PASS. The checks covered repeated sidebar
  and Help transitions, keyboard focus, editor boundary, scrollbar/gutter
  stability, resizing/window states, About, tooltips, safely reachable
  persistent status presentation, runtime On-to-Off transitions, and a fresh
  launch with Animation effects Off. No duplicate, stale, flashing,
  animation-only, focus-stealing, or ambiguous state was reported.
- **Limit:** this does not infer a result for model-, microphone-, export-,
  publishing-, authentication-, or external-service-dependent progress states,
  which were excluded from the packet.
- **Classification:** **PASS** for the directly exercised reduced-motion cases.
  No new defect was reported. WP-25 remains open for RTL/mixed-direction
  workflow, remaining Narrator semantics, validation/recovery, and conditional
  surfaces; WP-32 remains blocked.

### WP-25-MANUAL-20: RTL and mixed-direction text evidence — DIRECT PASS / PARTIAL BLOCKED

- **Environment and preparation:** on 2026-09-13, the exact Release build from
  `9a3f8222e0c88fe0b79fff3efab388c1c0d02029` was exercised on Windows 11
  Version 25H2, build 26200.9278, at 1920 by 1080 on one laptop display, 125%
  display scaling, 100% Windows text size, Notype Light, Contrast themes None,
  and Animation effects On. Locked restore and the zero-warning Release build
  passed. Accessibility preflight passed 19/19 self-test cases and 168/168
  exact-build checks across 26 scenarios, 131 expectations, 11 shells, 51 shell
  states, and 12 operator cases; generated manual status remained NotTested.
- **Evidence method:** the operator explicitly requested and supervised direct
  Computer Use interaction with the exact Release executable, then classified
  the requested tests PASS. Screenshots and accessibility document/selection
  state were used only for direct observations; no Narrator speech was inferred
  from automation metadata.
- **Workbench composer:** Arabic, Arabic combining marks, mixed Arabic/Latin,
  parentheses, numbers, Arabic punctuation, em dashes, and the test-owned email
  address remained intact and visually coherent. Keyboard word selection was
  bounded to the composer; replacement followed by Ctrl+Z restored the exact
  original value. Ctrl+A selected only the composer fixture. Copy/paste produced
  an exact duplicate and Ctrl+Z removed only that paste. Tab moved focus to the
  next composer action, Shift+Tab returned a clean complete composer focus
  boundary, and no text was submitted. Light and Dark direct checks passed; the
  operator's final all-tests PASS covers the prescribed Dusk variant.
- **Reading Studio draft:** all four prescribed fixtures rendered without
  character reversal, loss, duplication, or punctuation displacement. A
  3,462-character wrapping fixture combined repeated mixed-direction text, an
  unbroken Latin token, and Arabic text. Its scrollbar remained separate from
  the text, page/editor gutters stayed balanced, the focus boundary remained
  attached, and no jump or flicker appeared. Page Up/Down moved incrementally;
  Home/End retained current-visual-line behavior; Ctrl+Home/Ctrl+End reached the
  document boundaries. Plain Tab inserted indentation and one Ctrl+Z removed
  only that indentation. Ctrl+Tab and Ctrl+Shift+Tab exited the editor without
  altering text. Sidebar hidden/shown, exact 1040-by-700 minimum, restored, and
  maximized layouts retained usable text, scrollbar, focus, and page geometry;
  the operator's final PASS covers the remaining prescribed one-unit-above-
  minimum and Dusk variants.
- **Cleanup:** all test-owned Workbench and Reader text was removed, nothing was
  submitted or prepared, Reading Studio was closed, Notype Light and Contrast
  themes None were restored, and Animation effects remained On. No microphone,
  model, voice, export, publishing, authentication, installer, or real History
  operation was used.
- **Unavailable cases:** **BLOCKED** — the prepared read-only Reader surface
  requires excluded model preparation. **BLOCKED** — no clearly harmless,
  nonpersistent Settings text field was available without touching hotkey,
  model, device, credential, or private data. **NOT TESTED** — Narrator speech
  and its displayed version were not observed; Computer Use accessibility names
  are not substituted for spoken screen-reader evidence.
- **Classification:** **PASS** for the Workbench and Reading Studio RTL/mixed-
  direction workflows, with the preceding conditional cases retained as
  BLOCKED/NOT TESTED. No new defect was reported. WP-25 remains open for the
  remaining Narrator-semantics, validation/recovery, and conditional-surface
  matrix; WP-32 remains blocked.

### WP-25-MANUAL-21: Primary Narrator semantics — NOT TESTED

- **Environment and preparation:** on 2026-09-13, exact Release commit
  `ac305596e42edc63f2d423f7d2c51676e6616ae3` passed the clean checkpoint,
  locked restore, zero-warning Release build, 19/19 accessibility-preflight
  self-tests, and 168/168 exact-build checks across 26 scenarios, 131 control
  expectations, 11 shells, 51 shell states, and 12 operator cases. Generated
  manual status remained NotTested.
- **Evidence limitation:** Computer Use stopped before Narrator testing and was
  subsequently excluded by the operator. The two case checklists were presented,
  but no exact spoken announcements, displayed Narrator version, or other direct
  observations were supplied. Requests to “consider” the cases passed do not
  satisfy the packet's direct-listening contract, so no PASS is inferred.
- **Classification:** **NOT TESTED**, not FAIL. No Narrator defect was reported.
  This result remains unchanged by the later operator waiver.

### 2026-09-14 operator closure and shared sidebar-glyph correction

- **Operator direction:** the operator explicitly declined further Narrator,
  shell, History, validation/recovery, and conditional-surface manual testing and
  requested that the remaining backlog evidence be closed as skipped.
- **Truthful classification:** every unexecuted case is **SKIPPED / NOT TESTED**.
  No PASS is inferred from automated tests, source inspection, silence, or the
  operator's decision to stop testing. Existing dated PASS and BLOCKED evidence
  above remains unchanged.
- **Unavailable evidence retained:** no displayed Narrator version or spoken
  output was observed; per-monitor transfer remained hardware-blocked; prepared
  read-only Reader and harmless Settings text-entry states retained their stated
  blockers; conditional Publishing, first-run, completion-toast, and unsafe or
  model-dependent recovery states were not exercised.
- **Administrative result:** `UI-SHELL-001`, `UI-HISTORY-001`, and `ACCESS-001`
  are closed by operator acceptance of these evidence gaps. This does not establish
  conformance for skipped cases and must not be represented as a complete manual
  accessibility certification.
- **Sidebar icon implementation:** source inspection found exactly two genuine
  hamburger sidebar toggles: Workbench and Reading Studio. Both now consume one
  shared, layout-neutral sidebar-panel vector that inherits the owning button's
  semantic foreground. The existing accessible action names, tooltips, commands,
  focus cues, button dimensions, custom-chrome hit testing, and sidebar behavior
  are unchanged. No third hamburger toggle was found. This visual change has
  automated coverage but no new manual PASS is claimed.

---

## 6. Detailed Test Cases & Execution Plans (Marked Not Tested)

The plans below are archived historical coverage definitions. They were not
executed as a complete matrix. On 2026-09-14 the operator declined further
execution, so their unresolved cases are **SKIPPED / NOT TESTED**, not pending
and not PASS.

### SHELL-WB-01: Workbench Surface Continuity & Native DWM Theming
- **Window:** `TextboxWorkbenchWindow`
- **Build:** Not yet selected; capture the exact Review 4 descendant commit and SHA-256 before execution.
- **Environment:** Windows 10 Home 25H2 (Build 26200.9278), 150% DPI, Dark Theme
- **Steps:**
  1. Launch `DictateAnywhere.App.exe`.
  2. Verify Workbench window presentation, title region, and body surface.
- **Expected Result:**
  - Continuous surface rendered in dark palette (`Brush.Surface.Canvas` = `#0A0F18`).
  - Native DWM title bar is suppressed via `WindowChrome` (`CaptionHeight="38"`, `GlassFrameThickness="0"`).
  - No unintended white title bar or window border seam.
- **Source Inspection Note:** XAML defines `WindowChrome` with `CaptionHeight="38"` and `WindowThemeBehavior.IsEnabled="True"`.
- **Manual Status:** **Skipped / Not tested** by operator direction on 2026-09-14 (the earlier unsupported pass claim remains withdrawn).

### SHELL-WB-02: Workbench Caption Button Interaction & State
- **Window:** `TextboxWorkbenchWindow`
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Environment:** Windows 10 Home 25H2, 150% DPI, Dark Theme
- **Steps:**
  1. Hover over Minimize, Maximize/Restore, and Close buttons.
  2. Click Maximize; observe glyph transition and window bounds.
  3. Click Restore; observe return to normal bounds.
  4. Double-click the drag region to maximize/restore.
- **Expected Result:**
  - Minimize tooltip "Minimize", glyph `\uE921`.
  - Maximize tooltip "Maximize", glyph `\uE922`. Upon maximize, glyph switches to `\uE923` and tooltip to "Restore".
  - Close button turns red (`#FFE81123`) on hover with white glyph `\uE8BB`.
  - Drag region double-click triggers maximize/restore without clipping taskbar.
- **Source Inspection Note:** Automated unit test `WindowShellPrimitivesTests` verifies glyph switching logic in memory; visual rendering on screen requires manual confirmation.
- **Manual Status:** **Skipped / Not tested** by operator direction on 2026-09-14 (the earlier unsupported pass claim remains withdrawn).

### SHELL-WB-03: Workbench Caption Keyboard & Accessibility
- **Window:** `TextboxWorkbenchWindow`
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Environment:** Windows 10 Home 25H2, 150% DPI
- **Steps:**
  1. In Notype, open **Settings** and read the configured dictation shortcut. Do not change it for this test.
  2. Inspect the accessibility tree using UI Automation / Accessibility Insights and verify `AutomationProperties.Name` on caption buttons.
  3. Press `Tab` and `Shift+Tab` into the caption controls.
  4. Test the Windows system menu only with a chord that is not the configured Notype hotkey. If `Alt+Space` is assigned to dictation, record the `Alt+Space` system-menu step as **Not applicable — hotkey conflict**; do not press it for this shell test and do not overwrite the user setting.
- **Expected Result:**
  - The caption actions are identifiable as "Minimize", "Maximize" (or "Restore" when maximized), and "Close". The UI Automation name may add the current window context; an assistive technology may announce the concise action.
  - Focused caption button displays visible `Brush.Control.Primary` focus outline (`DEFECT-SHELL-002`).
  - A nonconflicting Windows system-menu invocation displays the standard window command menu. A conflicting `Alt+Space` assignment is recorded, not treated as a Notype shell failure or pass.
- **Source Inspection Note:** DEFECT-SHELL-002 resolved in `ControlStyles.xaml` with visible border outline on `IsKeyboardFocused`.
- **Manual Status:** **Skipped / Not tested** by operator direction on 2026-09-14 (the earlier unsupported pass claim remains withdrawn).

### 2026-09-07/08 limited Workbench observation (not a completed matrix case)

- The operator used `Tab` in the Workbench and confirmed that focus reached
  controls including **New chat** and **Settings**; `Enter` activated each.
- **Observed failure:** neither **New chat** nor **Settings** had a visually
  discernible focus outline or other visible focus-state change. Caption buttons
  did show a clear rectangular focus indicator. This observation motivated
  `WP-25-AUTO-01`; the corrected shared focus cue still requires a fresh manual
  rerun and is not recorded as a pass here.
- Icon controls such as **Dictate**, **Add file**, and **Open Reading Studio**
  exposed meaningful labels/tooltips. Those labels do not prove visible focus.
- **Expand composer**, **Submit**, and the sidebar show/hide action are
  state-dependent. The operator did not treat their absence or disabled state in
  an inapplicable scenario as a failure.
- `Windows+Ctrl+Enter` did not start Windows Narrator. Narrator is the built-in
  Windows screen reader. For a future attempt, open **Windows Settings >
  Accessibility > Narrator** (Windows 11) or **Windows Settings > Ease of Access
  > Narrator** (Windows 10) and start it there. The shortcut attempt is
  **Blocked by the environment/tool launch** and is not an application result.
- The configured dictation shortcut was `Alt+Space`; the earlier instruction to
  use that chord for the Windows system menu was invalid for this environment.
- **Manual classification:** focused-button defect observed; Narrator launch
  blocked; all correction verification and remaining WP-25/WP-09 cases **Not
  tested**.

### 2026-09-09 limited Workbench observation (not a completed matrix case)

The operator reported Windows 11 Version 25H2, OS build `26200.9278`, one laptop
display at 1920 by 1080, 125% display scaling, 100% Windows text size, and the
Notype Light theme. The exact executable hash was not captured, so these are
retained as partial observations rather than a completed fixed-build case.

- **Observed passes:** `Tab` reached enabled Workbench controls; hidden and
  disabled controls were skipped; `Enter` activated only the focused control;
  every reached enabled control had a visible blue rectangular focus cue.
  Narrator was started and announced meaningful names including **New chat**,
  **Settings**, and **Open Reading Studio**. Caption controls retained visible
  focus. The maximize action was announced as **Maximize**, changed to
  **Restore**, and its glyph changed from one rectangle to overlapping
  rectangles. Requiring the spoken phrase "Maximize Notype" is not part of this
  observation.
- **Observed failure — Quick Settings:** activating **Settings** did not move the
  next keyboard focus into Quick Settings. `Tab` first continued through
  underlying Workbench controls before eventually reaching **Advanced** and the
  theme controls.
- **Observed failure — inline Advanced Settings:** after focus traversed the
  visible Settings controls and caption actions, `Tab` reached visually covered
  Workbench controls. Focus rectangles appeared over the Settings surface even
  though their Workbench targets were behind it.
- **Observed failure — narrow Workbench:** at the minimum restored width with
  the sidebar visible, the fixed-width composer clipped its right-side
  microphone/action area. Hiding the sidebar avoided the clipping. WP-25-FIX-02
  changes 720 from a fixed width to a preferred maximum and preserves all
  action columns; this correction still requires a manual rerun.
- **Scrolling characterization:** pointer scrolling is allowed to leave keyboard
  focus on **Manage Dictation History**, but its focus cue must move and clip
  with that control. A detached cue floating over unrelated content is a
  failure. The Settings content now owns a clipped adorner layer; a direct
  rerun is still required to determine whether the reported visual anomaly is
  resolved.
- **Manual classification:** the listed observations are retained as partial
  PASS/FAIL evidence. Quick Settings containment, inline Settings containment,
  scroll-cue clipping, and minimum-width composer layout remain **Not tested
  after correction**. All other WP-09/WP-25 cases remain **Not tested**.

### 2026-09-09 fixed-build correction rerun and Reading Studio observation (partial matrix evidence)

The operator ran the Release executable from commit
`1b2a2d798f13e08133d8c4c41a2ae149752eb03d` with SHA-256
`D20EB7A2BE2693AE25563BAC4F6FB2EDC9C9D3BE456B9BE9B5A9F4542C7E89F8` on
Windows 11 Version 25H2, OS build `26200.9278`, one 1920 by 1080 laptop display,
125% display scaling, 100% Windows text size, Light theme, and Narrator running.

- **WP-25-FIX-02 rerun — PASS:** Quick Settings initially focused **Advanced
  settings**, contained forward/reverse traversal, and returned visible focus to
  **Settings** on Escape. Inline Advanced Settings initially focused **Back**,
  contained traversal across Settings and permitted caption controls, and
  returned focus to **Settings**. The **Manage Dictation History** focus cue
  moved and clipped with Settings scrolling. At 1040 by 680 and the requested
  intermediate/maximized/restored/sidebar/text/action states, the composer kept
  Add file, Open Reading Studio, Dictate/Stop, and Submit visible and reachable.
- **About characterization:** With Narrator running, the version text was
  available through Narrator navigation. Ordinary Tab traversal otherwise
  reached the caption actions because the remaining body was static text.
  Closing About returned focus to the Workbench **Settings** invoker without
  reopening Quick Settings. That return is the intended fallback. WP-25-FIX-03
  adds one named focusable scroll region for keyboard scrolling without adding
  every static text block to the tab order.
- **Reading Studio help characterization:** Ordinary Tab traversal reached the
  caption actions while the body remained static. WP-25-FIX-03 likewise exposes
  one named keyboard-scrollable help-content region; individual static text
  remains outside the ordinary tab order.
- **Reading Studio failure — initial placement:** At 1920 by 1080 and 125%
  scaling, the initial top edge/title/caption area was partially above the work
  area. A subsequent restore/resize made it visible. WP-25-FIX-03 extends the
  existing custom-chrome work-area behavior to clamp initial normal bounds.
- **Reading Studio failure — sidebar scrolling:** Pointer scrolling moved the
  focused sidebar control away while its focus rectangle remained detached at
  a fixed viewport location. Sidebar content also scrolled underneath the fixed
  Reading Studio title/hamburger header. WP-25-FIX-03 gives the sidebar a fixed
  header row and a clipped, locally owned scroll/adorner viewport.
- **Reading editor characterization:** Plain `Tab` remains accepted as document
  text and therefore does not leave the multiline editor. WP-25-FIX-03 defines
  `Ctrl+Tab` and `Ctrl+Shift+Tab` as explicit forward/reverse escape routes while
  preserving plain-Tab insertion. The new chords require direct rerun.
- **Manual classification:** the four Workbench correction reruns above are
  partial fixed-build **PASS** evidence. The new Reader/sidebar/static-content
  corrections are **Not tested after correction**. DPI variants, 200% text,
  High Contrast legibility, RTL/mixed text, remaining screen-reader workflows,
  recovery states, and all other unexecuted matrix cells remain **Not tested**.

### 2026-09-09 fixed-build Reading Studio correction rerun (partial matrix evidence)

The operator ran the Release executable from commit
`7c686e88de0d523a1a0d5fe5e07c68d13afbf891` in the same sanitized environment:
Windows 11 Version 25H2, OS build `26200.9278`, one 1920 by 1080 laptop display,
125% display scaling, 100% Windows text size, Light theme, with Narrator used for
the applicable static-content checks.

- **Reading Studio placement — PASS:** the complete top edge, title, sidebar
  control, and caption actions were visible in the usable work area on the
  initial open and two reopen attempts; the taskbar did not obscure the window.
- **Reader sidebar header/focus — PASS:** the Reading Studio title and sidebar
  control remained fixed; sidebar content did not scroll underneath them; the
  focused-control outline moved and clipped with its target for the exercised
  pointer, scrollbar, and keyboard scrolling paths, and Tab navigation brought
  the next target into view.
- **Reading draft keyboard escape — PASS:** plain `Tab` inserted document
  indentation as designed. `Ctrl+Tab` and `Ctrl+Shift+Tab` left the editor in
  forward and reverse directions without changing or duplicating the harmless
  test text.
- **About Notype static content — partial PASS / FAIL:** the single named content
  region, caption traversal, Narrator-readable version, and focus restoration to
  the Workbench **Settings** invoker passed. `Page Up` and `Page Down` scrolled,
  but while that content region was focused, `End` did not reach the vertical
  bottom and `Home` did not return to the vertical top.
- **Reading Studio Help static content — partial PASS / FAIL:** the single named
  content region, caption traversal, card reachability, and focus restoration to
  the live help invoker passed. `Page Up` and `Page Down` scrolled, but `End` and
  `Home` did not reach the vertical bottom and top while the content region was
  focused.
- **WP-25-FIX-04 correction:** both named static-content regions now share a
  narrowly scoped direct-focus behavior for unmodified `Home` and `End` only.
  Automated evidence proves the vertical boundaries, retained focus, native
  ownership for Page Up/Down and other keys, descendant isolation, caption
  isolation, and zero-extent safety. The corrected Home/End behavior remains
  **Not tested manually after correction**; automation is not a manual PASS.
- **Manual classification:** the stated placement, Reader sidebar, editor escape,
  and non-Home/End portions of About/help are retained as direct partial **PASS**
  evidence. The two corrected Home/End paths and every other unexecuted
  environment/workflow matrix cell remain **Not tested**.

### 2026-09-10 fixed-build static-content correction rerun (partial matrix evidence)

After the locked restore, Release build, and exact-commit preflight passed, the
operator ran both prescribed static-content reruns against commit
`3c9189a82039e0d509457315e87a1d636910453c`. No environment change was reported
from the preceding sanitized Windows 11, 1920 by 1080, 125% display scaling,
100% text-size, Light-theme session.

- **About Notype Home/End — PASS:** while the named content region had keyboard
  focus, `Page Down` and `Page Up` moved incrementally, `End` reached the vertical
  bottom, and `Home` returned to the vertical top. Scrolling remained correct and
  no unexpected behavior was reported for the other prescribed checks.
- **Reading Studio Help Home/End — PASS:** the operator reported completing the
  corresponding named-content-region procedure with the same result: incremental
  Page Up/Down movement and correct Home-to-top and End-to-bottom boundaries,
  with all listed checks passing.
- **Manual classification:** both WP-25-FIX-04 correction reruns are direct
  **PASS** observations. This closes only the corrected static-content paths.
  High Contrast, 200% text size, additional available DPI/scaling, Dark theme,
  reduced motion, RTL/mixed-direction text, remaining Narrator workflows, and
  safe validation/cancellation/retry/recovery cases remain **Not tested** unless
  separately identified as observed elsewhere in this document.

### SHELL-SET-01: Settings Window Shell & Theming
- **Window:** `SettingsWindow`
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Environment:** Windows 10 Home 25H2, 150% DPI, Dark Theme
- **Steps:**
  1. Open Settings via Workbench gear button or tray.
  2. Verify window frame, header, and caption controls.
  3. Toggle theme from Dark to Light via Workbench menu; observe Settings response.
- **Expected Result:**
  - Continuous dark surface, no white title bar.
  - On switching to Light theme, palette updates to light brushes and DWM native attribute updates to light mode.
  - On switching back to Dark theme, dark palette and DWM dark attribute restore immediately.
  - Sizing respects minimum constraints (`900x640`).
- **Manual Status:** **Not tested** (the earlier unsupported pass claim is withdrawn).

### SHELL-HIST-01: Dictation History Shell Continuity & Sizing
- **Window:** `HistoryWindow`
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Environment:** Windows 10 Home 25H2, 150% DPI
- **Steps:**
  1. Open Dictation History (`Settings > Manage Dictation History`).
  2. Check title region, drag region, search box, list, and detail area.
  3. Resize window down to minimum constraints (`760x520`).
  4. Maximize window.
- **Expected Result:**
  - Header displays "Dictation History" with continuous surface.
  - Caption buttons have accessible names "Minimize Dictation History", "Maximize Dictation History", "Close Dictation History".
  - No white native title bar.
  - Window resizes smoothly; minimum width (760) and height (520) enforced without control overlapping.
- **Manual Status:** **Not tested** (Awaiting interactive execution)

### WP-10 History workflow operator matrix (automation prepared; manual Not tested)

This document also owns the eventual Dictation History workflow observations;
WP-10 does not create a second evidence owner. Before executing those cases on
an exact clean Release commit, run:

```powershell
.\scripts\run-history-preflight.ps1 -SelfTest
.\scripts\run-history-preflight.ps1 -ExpectedCommit <40-character-commit>
```

The preflight first requires the shared accessibility/shell preflight to pass,
then runs the declared History tests only against a unique test-owned scratch
store. It validates 8 owners, 21 workflow states, 15 required coverage tests,
and these 9 operator cases:

| Case | Required operator state | Manual Status |
| --- | --- | --- |
| `HISTORY-01` | Empty or no-selection state; Search remains reachable and editor/Save/Delete remain unavailable. | **NOT TESTED** |
| `HISTORY-02` | Select a non-sensitive test-owned entry; verify localized date/time and clean-action state. | **NOT TESTED** |
| `HISTORY-03` | Edit and save a test-owned entry; close/reopen and verify the saved state without recording its text. | **NOT TESTED** |
| `HISTORY-04` | Open Delete confirmation for a test-owned entry and cancel; verify selection/editor/focus retention. | **NOT TESTED** |
| `HISTORY-05` | Confirm deletion of an explicitly disposable test-owned entry and verify it remains absent after reopen. | **NOT TESTED** |
| `HISTORY-06` | Exercise search match, no-match, clear, and rapid latest-query behavior with test-owned identifiers. | **NOT TESTED** |
| `HISTORY-07` | Refresh a dirty selected test-owned entry, then filter it out; verify draft/caret preservation followed by stale-detail clearing. | **NOT TESTED** |
| `HISTORY-08` | Observe Dark, Light, available High Contrast/text-size/DPI variants, keyboard focus, and minimum-size layout; restore changed settings. | **NOT TESTED** |
| `HISTORY-09` | Exercise failure/retry/cancellation only with an approved reversible test profile; otherwise record BLOCKED. | **NOT TESTED** |

The generated file under ignored `artifacts/history-preflight/<run>/` supplies
the exact opening route, prerequisite state, action, expected result, and report
fields for each case. It is preparation output, not durable manual evidence.
Never point the preflight at `%LOCALAPPDATA%`, quote record text in evidence, or
delete a real History entry. On 2026-09-14 the operator declined the nine-case
matrix, so WP-10 / `UI-HISTORY-001` is administratively closed with every
unexecuted case retained as **SKIPPED / NOT TESTED**.

### SHELL-RDR-01: Reading Studio Specialized Layout & Drag Bar
- **Window:** `ReaderWindow`
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Environment:** Windows 10 Home 25H2, 150% DPI
- **Steps:**
  1. Open Reading Studio from Workbench.
  2. Confirm the full title, sidebar toggle, and caption controls initially appear inside the visible monitor work area.
  3. In the multiline draft, confirm plain `Tab` inserts indentation, `Ctrl+Tab` leaves the editor forward, and `Ctrl+Shift+Tab` leaves it backward.
  4. Focus a lower sidebar control and scroll with the pointer wheel, scrollbar, `Page Up`/`Page Down`, `Home`, and `End`; confirm the focus cue moves and clips with its control and content never scrolls under the fixed title/hamburger header.
  5. Maximize and restore.
- **Expected Result:**
  - Seamless surface blending into reading canvas and transport.
  - Custom drag region (`Height="42"`, `presentation:WindowDragRegionBehavior.IsEnabled="True"`).
  - Caption buttons (`WindowCaptionButtons`) positioned at top-right with `ContextTitle="Reading Studio"`.
  - Sidebar toggle button maintains hit-test visibility in chrome.
- **Manual Status:** **Skipped / Not tested** by operator direction on 2026-09-14.

### SHELL-ABT-01 & SHELL-RHLP-01: About Notype and Reading Studio Help Dialogs
- **Windows:** `AboutWindow`, `ReadingStudioHelpWindow`
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Environment:** Windows 10 Home 25H2, 150% DPI
- **Steps:**
  1. Open About Notype from Workbench help menu.
  2. Open Reading Studio Help from Reading Studio help icon.
  3. In each window, Tab to its single named content region. Use `Page Up` and `Page Down` for incremental vertical scrolling, `End` to reach the vertical bottom, and `Home` to return to the vertical top; verify focus remains on the region and individual static text blocks are not separate Tab stops.
  4. Verify chrome, drag region, caption controls, and cards layout.
- **Expected Result:**
  - Both windows use standard 54px header with drag region and `WindowCaptionButtons`.
  - Accessible names: "About Notype" and "Reading Studio help".
  - Scrollable content area with styled cards; no white title bar.
- **Manual Status:** **Skipped / Not tested** by operator direction on 2026-09-14.

### SHELL-YTP-01: YouTube Publishing Specialized Tool Frame
- **Window:** `YouTubePublishingWindow`
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Environment:** Windows 10 Home 25H2, 150% DPI
- **Steps:**
  1. Open Reading Studio > Export panel > Publish to YouTube.
  2. Verify modal window presentation and header.
- **Expected Result:**
  - Header has drag region and dedicated Close button (`ClosePublishingButton`, `AutomationProperties.Name="Close"`).
  - No maximize button (modal workflow tool).
  - Theme behavior enabled; continuous canvas.
- **Manual Status:** **Skipped / Not tested** by operator direction on 2026-09-14.

### SHELL-FRW-01, SHELL-HKT-01, SHELL-HKS-01: Standard Native Windows
- **Windows:** `FirstRunWizardWindow`, `HotkeyTestWindow`, `HotkeySettingsWindow`
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Environment:** Windows 10 Home 25H2, 150% DPI
- **Steps:**
  1. Inspect native title bar and caption controls on `HotkeyTestWindow` (accessible via Settings > Test hotkey).
  2. Verify native DWM dark title bar application.
- **Expected Result:**
  - Windows use native OS chrome with DWM immersive dark mode enabled via `WindowThemeBehavior`.
  - Native minimize/close controls adopt Windows 11 rounded style and dark title bar background.
- **Manual Status:** **Skipped / Not tested** by operator direction on 2026-09-14.

### SHELL-SCALE-01: Display Scaling Verification (100%, 150%, 200%)
- **Scope:** All reachable first-party windows.
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Steps:**
  1. Evaluate at baseline 150% DPI (144 DPI).
  2. Switch Windows display scaling to 100% (96 DPI); inspect text sharpness, button proportions, and hit targets.
  3. Switch Windows display scaling to 200% (192 DPI); inspect for text clipping, title bar overlap, or truncated action buttons.
- **Expected Result:**
  - Windows snap to device pixels (`SnapsToDevicePixels="True"`, `UseLayoutRounding="True"`).
  - Caption buttons remain accessible and click targets remain >= 44x34 px physical.
  - At 200%, header text and buttons wrap or space out without clipping or disappearing off-screen.
- **Manual Status:** **Skipped / Not tested** by operator direction on 2026-09-14.

### SHELL-HC-01: Windows High Contrast Mode
- **Scope:** All custom chrome windows.
- **Build:** Not yet selected; capture the exact Review 4 descendant commit before execution.
- **Steps:**
  1. Enable Windows High Contrast mode (`Left Alt + Left Shift + PrintScreen`).
  2. Inspect window borders, title bars, and text contrast.
- **Expected Result:** High contrast system brushes are applied to window chrome, borders, and text.
- **Source Inspection Note:** DEFECT-SHELL-003 high contrast palette mapping implemented in `AppThemeManager.cs`. Root outer window border configured via `ControlStyles.xaml` (`BorderBrush="{DynamicResource Brush.Border.Subtle}"`, `BorderThickness="1"` suppressed on `Maximized`).
- **Manual Status:** **Skipped / Not tested** by operator direction on 2026-09-14 (the earlier unsupported partial-pass/failure claim remains withdrawn; automation does not substitute for observation).

---

## 7. Gate Status & Summary

- **Automated Primitives & Theming Test Gate:** **PASS at Review 4** for caption wiring, accessible names, focus indicator application/restoration, glyph state, outer border rules, and high-contrast palette mapping. The full Release solution completed with exit 0: 1034 passed, 2 hardware/model smoke tests skipped, 0 failed.
- **Manual Interaction Gate:** **Closed by operator waiver**. Remaining cases are **SKIPPED / NOT TESTED**; no PASS is inferred.
- **Confirmed Defects:** 2 historical defects (`DEFECT-SHELL-002` and `DEFECT-SHELL-003`) are implementation/automated-verification complete; narrower corrected-state observations are retained above, and the unexecuted remainder is skipped rather than pending.
- **Unconfirmed Defects:** 1 (`DEFECT-SHELL-001` dismissed as System theme is unexposed/out of scope).
- **UI-SHELL-001 Status:** **Closed by operator direction**. Implementation and automated coverage are complete; skipped manual cases remain explicit evidence gaps.

---

## 8. Bounded Implementation Prompts for Confirmed Defects

### Fix Prompt 1: Caption Button Keyboard Focus Indicator (`DEFECT-SHELL-002`) — RESOLVED (MANUAL VALIDATION PENDING)

### Fix Prompt 2: Custom Shell High-Contrast Mode Support (`DEFECT-SHELL-003`) — RESOLVED (MANUAL VALIDATION PENDING)
