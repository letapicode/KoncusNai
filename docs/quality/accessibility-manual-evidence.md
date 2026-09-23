# Accessibility Manual Matrix Evidence (WP-25)

## Executive Summary

- **Backlog Item:** `ACCESS-001` (Automated and manual accessibility verification)
- **Work Packet:** `WP-25` (Accessibility manual matrix)
- **Status:** Source-inspection audit and automated regression tests committed. Manual cases
  `ACC-KB-*`, `ACC-HC-01`, `ACC-RM-01`, `ACC-NW-01`, `ACC-RTL-01`, `ACC-TRY-01` are
  **NOT TESTED** pending operator execution. Screen-reader (`ACC-SR-*`) and 200% DPI
  (`ACC-DPI-01`) cases are **NOT TESTED** pending live-session availability.
- **Evidence Committed:** 2026-09-14
- **Source Checkpoint Reviewed:** commit `32dafe1` (WP-24 baseline) and current HEAD (WP-25)

### Source Inspection Finding

Complete re-audit of all custom XAML styles confirms that every interactive control type in the
application has a correct keyboard focus visual. See Section 3 for the full per-style table.
No focus-indicator code defects were found. The `AppKeyboardFocusVisualStyle` (a themed
double-border ring defined in `ControlStyles.xaml` lines 4-20) is consistently applied across
all button, slider, list, tab, and checkbox styles. WPF input controls (`TextBox`, `ComboBox`,
`PasswordBox`) use inline `IsKeyboardFocused`/`IsKeyboardFocusWithin` triggers instead.

---

## 1. Fixed Build Identification

The build referenced by this document for source inspection purposes is the WP-25 commit on
`main`. Before any manual test case execution, the operator must:

1. Build the selected commit in `Release` configuration:
   ```powershell
   dotnet build src/DictateAnywhere.App/DictateAnywhere.App.csproj -c Release
   ```
2. Record the executable identity:
   ```powershell
   $exe = "src\DictateAnywhere.App\bin\Release\net8.0-windows\DictateAnywhere.App.exe"
   (Get-Item $exe).Length
   (Get-FileHash $exe -Algorithm SHA256).Hash
   ```
3. Run the full Release test gate and confirm >=1028 passed:
   ```powershell
   dotnet test --configuration Release
   ```

| Attribute | Verified Value |
| --- | --- |
| **Commit** | WP-25 HEAD (record 40-char SHA before manual session) |
| **Target Framework** | `.NET 8.0 Windows` (`net8.0-windows`) |
| **Configuration** | `Release` |
| **Executable Path** | `src\DictateAnywhere.App\bin\Release\net8.0-windows\DictateAnywhere.App.exe` |
| **Executable Size** | *(operator captures before session)* |
| **SHA-256 Hash** | *(operator captures before session)* |

---

## 2. Test Environment & Baseline Configuration

| Parameter | Reference Value | Notes |
| --- | --- | --- |
| **OS Edition** | Windows 10 Home (DisplayVersion `25H2`) | Internal platform: Windows NT 10.0.26200.9278 |
| **GPU / Video** | Intel(R) Iris(R) Xe Graphics | Resolution 3840 x 2160 @ 59 Hz |
| **Display Arrangement** | Dual-display (Primary: Dell 3840x2160, Secondary: Samsung 3840x2160) | |
| **Baseline Scale** | **150%** (`AppliedDPI = 144`) | System-applied DPI on primary display |
| **Target Scale Matrix** | **100%** (96 DPI), **150%** (144 DPI), **200%** (192 DPI) | Scale verification targets |
| **Baseline Windows Theme** | **Dark** (`AppsUseLightTheme = 0`, `SystemUsesLightTheme = 0`) | |
| **Baseline App Theme** | **Dark** (`themePreference: 2` in `settings.json`) | `AppThemePreference.Dark` |
| **Baseline High Contrast** | **Disabled** (`Flags = 126`, bit `HCF_HIGHCONTRASTON` = 0) | |
| **Screen Reader** | Windows Narrator | Only for `ACC-SR-*` cases; off for all others |

### Restoration Baseline

After completing any theme, scale, or contrast variation, restore:
1. Display Scaling: **150%** on Primary.
2. Windows Theme: **Dark** (`Settings > Personalization > Colors > Dark`).
3. High Contrast: **Off** (`Left Alt + Left Shift + PrintScreen` or `Settings > Accessibility > Contrast themes: None`).
4. Narrator: **Off**.
5. App Settings: `settings.json` intact; no user data or history reset.

---

## 3. Source Inspection - Keyboard Focus Visual Audit

Inspection performed 2026-09-14 on current HEAD (`main`). All findings are **PASS (source inspection)**.

### 3.1 Global Styles (`ControlStyles.xaml`)

| Style / Control | Mechanism | Status |
| --- | --- | --- |
| `AppKeyboardFocusVisualStyle` | Double-border focus ring defined as named resource | DEFINITION |
| `AppActionButtonStyle` (base button) | `FocusVisualStyle={StaticResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `AppPrimaryActionButtonStyle` | `BasedOn AppActionButtonStyle`; own `Template`; no `FocusVisualStyle` reset - inherits ring | **PASS** |
| `AppDangerActionButtonStyle` | Same as above | **PASS** |
| `AppToolbarIconButtonStyle` | `BasedOn AppActionButtonStyle`; no `Template` override - full inheritance | **PASS** |
| `AppDangerIconButtonStyle` | `BasedOn AppToolbarIconButtonStyle` | **PASS** |
| Default `Button` implicit style | `BasedOn AppActionButtonStyle` (line 153-154) | **PASS** |
| `AppTextBoxStyle` | `FocusVisualStyle={x:Null}` + `IsKeyboardFocused -> BorderBrush=Brush.Progress.Value` | **PASS** |
| `PasswordBox` global style | `FocusVisualStyle={StaticResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `AppComboBoxStyle` | `FocusVisualStyle={x:Null}` + `IsKeyboardFocusWithin -> ToggleButton.BorderBrush` | **PASS** |
| `AppComboBoxItemStyle` | `FocusVisualStyle={x:Null}` + `IsHighlighted` trigger (keyboard highlight) | **PASS** |
| `ListBox` global style | `FocusVisualStyle={StaticResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `ListBoxItem` global style | `FocusVisualStyle={StaticResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `TabControl` global style | `FocusVisualStyle={StaticResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `TabItem` global style | `FocusVisualStyle={StaticResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `CheckBox` global style | `FocusVisualStyle={StaticResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `MenuItem` global style | `FocusVisualStyle={x:Null}` + `IsHighlighted` trigger (WPF menu pattern) | **PASS** |
| `AppChromeWindowButtonStyle` (caption) | `FocusVisualStyle={x:Null}` + inline `IsKeyboardFocused -> Chrome.BorderBrush=Primary` | **PASS** |
| `AppChromeCloseButtonStyle` | Own template with `IsKeyboardFocused` trigger | **PASS** |

### 3.2 Workbench Styles

| Style | File | Mechanism | Status |
| --- | --- | --- | --- |
| `WorkbenchToolbarButtonStyle` | `TextboxWorkbenchWindow.xaml` | `BasedOn AppToolbarIconButtonStyle` -> inherits `AppKeyboardFocusVisualStyle` | **PASS** |
| `WorkbenchDangerToolbarButtonStyle` | `TextboxWorkbenchWindow.xaml` | `BasedOn AppDangerIconButtonStyle` -> inherits | **PASS** |
| `ComposerIconButtonStyle` | `TextboxWorkbenchWindow.xaml` | `BasedOn AppToolbarIconButtonStyle`; own template; no `FocusVisualStyle` reset -> inherits | **PASS** |
| `ComposerSubmitButtonStyle` | `TextboxWorkbenchWindow.xaml` | `BasedOn AppToolbarIconButtonStyle`; own template; no `FocusVisualStyle` reset -> inherits | **PASS** |
| `ComposerStopButtonStyle` | `TextboxWorkbenchWindow.xaml` | `BasedOn AppActionButtonStyle` -> inherits | **PASS** |
| `SidebarActionButtonStyle` | `WorkbenchSidebarView.xaml` | Explicit `FocusVisualStyle={DynamicResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `SidebarSearchTextBoxStyle` | `WorkbenchSidebarView.xaml` | `BasedOn AppTextBoxStyle`; `IsKeyboardFocused -> BorderBrush` | **PASS** |
| `SettingsMenuButtonStyle` | `WorkbenchQuickSettingsView.xaml` | Explicit `FocusVisualStyle={DynamicResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `ChatTextSizeSliderStyle` | `WorkbenchQuickSettingsView.xaml` | Explicit `FocusVisualStyle={DynamicResource AppKeyboardFocusVisualStyle}` | **PASS** |

### 3.3 Publishing Window Styles

| Style | Mechanism | Status |
| --- | --- | --- |
| `PublishingButtonBase` | Explicit `FocusVisualStyle={DynamicResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `PrimaryButton` | `BasedOn PublishingButtonBase` -> inherits | **PASS** |
| `SecondaryButton` | `BasedOn PublishingButtonBase` -> inherits | **PASS** |
| `PublishingWindowControlButton` (close) | Explicit `FocusVisualStyle={DynamicResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `PasswordBox` local template | `IsKeyboardFocused -> PasswordSurface.BorderBrush=Brush.Control.Primary` | **PASS** |
| `ComboBox` local template | `IsKeyboardFocusWithin -> ControlSurface.BorderBrush=Brush.Progress.Value` | **PASS** |
| `DatePicker` local template | `IsKeyboardFocusWithin -> DateSurface.BorderBrush=Brush.Control.Primary` | **PASS** |

### 3.4 Reader Window Styles

| Style | Mechanism | Status |
| --- | --- | --- |
| `ReaderSecondaryButton` | Explicit `FocusVisualStyle={DynamicResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `ReaderPrimaryButton` | `BasedOn ReaderSecondaryButton` -> inherits | **PASS** |
| `ReaderIconButton` | `BasedOn ReaderSecondaryButton` -> inherits | **PASS** |
| `ReaderBareIconButton` | `FocusVisualStyle={x:Null}` + `IsKeyboardFocused -> FocusSurface.BorderBrush=Primary` | **PASS** |
| `ReaderWindowControlButton` | Explicit `FocusVisualStyle={DynamicResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `ReaderSlider` | Explicit `FocusVisualStyle={DynamicResource AppKeyboardFocusVisualStyle}` | **PASS** |
| `ReaderProcessingToggle` | `FocusVisualStyle={x:Null}` + `IsKeyboardFocused -> Outer.BorderBrush=Primary` | **PASS** |
| `ReaderSwatchItem` (ListBoxItem) | `FocusVisualStyle={x:Null}` + `IsKeyboardFocused -> SelectionRing.Stroke=Primary` | **PASS** |

---

## 4. Automated Accessibility Evidence

The following automated test methods provide regression-guarded accessibility evidence.
All pass at HEAD per the Release gate.

| Test Method | What It Verifies | Result |
| --- | --- | --- |
| `PrimaryWindows_DeclareMinimumBoundsAndResizableConstraints` | MinWidth>=450, MinHeight>=250 for all 10 window XAML files; live instances exceed bounds | **PASS (automated)** |
| `PrimaryViews_InstantiatedControls_HaveAccessibleNames` | 40+ named controls across all primary views have correct `AutomationProperties.Name` | **PASS (automated)** |
| `XamlFiles_ExplicitNamedControls_HaveAccessibleNames` | Key controls in Settings, History, FirstRun verified via XAML parse | **PASS (automated)** |
| `DynamicErrorAndStatusIndicators_DeclareLiveRegions` | All status TextBlocks declare `AutomationProperties.LiveSetting="Polite"` | **PASS (automated)** |
| `DynamicStatusContainers_HaveTextWrappingToPreventClipping` | Status text blocks have `TextWrapping=Wrap` | **PASS (automated)** |
| `TabNavigation_InteractiveControls_AreFocusableAndTabStops` | Publishing window interactive controls are focusable and IsTabStop | **PASS (automated)** |
| `HighContrast_SystemPalette_CoversAllThemeResourceKeys` | HC palette covers all Dark and Light palette keys | **PASS (automated)** |
| `ReducedMotion_ClientAreaAnimationParameter_IsRecognized` | `SystemParameters.ClientAreaAnimation` evaluated; animation views instantiate without error | **PASS (automated)** |
| `KeyboardFocusVisual_AllCustomButtonStyles_HaveFocusRingDeclared` *(WP-25)* | Every named Button/ToggleButton/Slider style has a named FocusVisualStyle or inline IsKeyboardFocused trigger | **PASS (automated)** |
| `TabNavigation_AllPrimaryWindowInteractiveControls_AreFocusableAndTabStops` *(WP-25)* | History save/delete buttons, Reader transport controls are focusable and IsTabStop | **PASS (automated)** |
| `RTLAndReducedMotion_CodePaths_ExistInSource` *(WP-25)* | FlowDirection.RightToLeft reference in ReaderDocumentView; ClientAreaAnimation reference in app source | **PASS (automated)** |

---

## 5. Manual Test Cases

Each case below includes exact operator steps and expected results. Operator records actual
result as **PASS**, **FAIL**, or **SKIP** with a date and brief observation.

> **NOT TESTED** = case not yet executed. This is an explicit pending status, not a claim of
> conformance. Cases left NOT TESTED at commit time are intentionally deferred.

---

### ACC-KB-01 - Keyboard tab order: Workbench window

**Precondition:** App running, Workbench window open, sidebar visible, no modal dialogs.

**Steps:**
1. Click the Workbench window title area to give it keyboard focus.
2. Press Tab repeatedly, cycling through all focusable controls.
3. Observe which control receives the highlighted focus ring at each step.
4. Press Shift+Tab to confirm reverse order is consistent.
5. Attempt to activate each focused button using Enter or Space.

**Expected:**
- Every interactive control receives visible keyboard focus in logical order.
- Focus ring uses the double-border `AppKeyboardFocusVisualStyle` or equivalent inline border.
- No control is silently skipped (invisible focus jump).
- Enter/Space on focused buttons produces the same action as mouse click.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-KB-02 - Keyboard tab order: Settings window

**Precondition:** Settings window open.

**Steps:**
1. Press Tab to cycle through all Settings fields.
2. Confirm tab panels (TabControl) are reachable via Tab, navigable with arrow keys.
3. Confirm ComboBox fields open with Alt+Down; items navigable with arrow keys.
4. Confirm CheckBox fields toggle with Space.
5. Activate Close button via keyboard.

**Expected:** All form fields and tab panels reachable keyboard-only. ComboBox dropdown navigable. Close button reachable.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-KB-03 - Keyboard tab order: History window

**Precondition:** History window open with at least one dictation entry.

**Steps:**
1. Tab through all controls: Search TextBox, History ListBox items, transcript TextBox, Save/Delete buttons.
2. Confirm ListBox arrow-key navigation moves through entries.
3. Confirm Save and Delete buttons activate with Enter.

**Expected:** All controls reachable. ListBox arrow navigation selects entries. Buttons activatable.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-KB-04 - Keyboard tab order: Reader window

**Precondition:** Reader window open with at least one chapter loaded.

**Steps:**
1. Tab through transport controls: Previous, Play/Pause, Next, Edit Text, Fullscreen.
2. Tab into sidebar: Section combos, Language, Voice, Prepare, Follow-Along, Highlight Style, Color swatch list, Font, Theme list, Text Size slider, Speed slider, Video Format.
3. Confirm sliders navigable with arrow keys; swatch items show focus ring; toggle button keyboard-operable.

**Expected:** All transport and sidebar controls Tab-reachable and operable. Sliders respond to arrow keys. Swatch list items show primary-color stroke ring.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-KB-05 - Keyboard tab order: Publishing window

**Precondition:** YouTube Publishing window open.

**Steps:**
1. Tab through all form fields and action buttons.
2. Confirm Close (x) button at top-right is Tab-reachable.
3. Confirm error message at bottom is updated and live-region announced when invalid data submitted.

**Expected:** All fields and buttons reachable. PasswordBox shows blue border ring on keyboard focus. Close button has focus ring. ValidationTextBlock updates on invalid submission without losing focus context.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-KB-06 - Keyboard tab order: First Run wizard

**Precondition:** App in first-run state or wizard launched.

**Steps:**
1. Tab through: HotkeyCaptureControl, Run Benchmark, Model ComboBox, Finish button, Cancel button.
2. Confirm Tab cycles completely and returns to first control.

**Expected:** All wizard controls focusable and keyboard-activatable. Cancel and Finish buttons accessible by keyboard. Model combo opens and navigates with keyboard.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-KB-07 - Error state keyboard reachability

**Precondition:** Publishing window open.

**Steps:**
1. Navigate to Publish button using Tab. Activate Publish without required fields.
2. Observe ValidationTextBlock updates with error text.
3. Confirm keyboard focus not lost or trapped.
4. Tab back to the error-causing field and correct it.

**Expected:** ValidationTextBlock reads error (live region Polite). Keyboard focus remains on a logical control. Tab order allows navigation back to problematic field.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-SR-01 - Screen reader: Workbench live region announcements

> **Requires:** Windows Narrator active.

**Steps:**
1. Enable Narrator (Win+Ctrl+Enter).
2. Open Workbench; start dictation via keyboard.
3. Speak a phrase and stop.
4. Listen for Narrator announcement of status changes.

**Expected:** HotkeyStatusText announced on activation. Dictation in-progress and completion announced. Control names read as declared.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-SR-02 - Screen reader: control name announcements

> **Requires:** Windows Narrator active.

**Steps:**
1. Enable Narrator. Tab through Workbench composer controls.
2. Listen for announced control names.
3. Tab to History ListBox; arrow through items; listen for item text announcements.
4. Open Quick Settings; listen for button names.

**Expected:** Narrator reads "Composer prompt, text box", "Dictate, button", "Submit, button", etc. No control announces a generic unnamed description.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-DPI-01 - 200% display scale: all primary windows

> **Requires:** System DPI change to 200% (192 DPI) and app restart.

**Steps:**
1. Change Display Scaling to 200%. Restart app.
2. Open each primary window: Workbench, Settings, History, Reader, Publishing, First Run.
3. Verify no text clipped, no buttons overlap, no controls hidden by window chrome.
4. Verify MinWidth/MinHeight constraints still apply.
5. Restore scale to 150%.

**Expected:** All windows render without layout breakage at 200% DPI. TextWrapping prevents overflow. MinWidth/MinHeight constraints respected. No fixed-pixel values cause clipping.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-HC-01 - High contrast mode: all primary windows

**Steps:**
1. Enable High Contrast Black (Left Alt+Left Shift+PrintScreen).
2. Open each primary window.
3. Verify text readable, focus rings visible, buttons distinguishable.
4. Interact keyboard-only to confirm focus cues visible in HC mode.
5. Disable High Contrast and restore baseline.

**Expected:** Window borders activate in HC mode. All text, buttons, and focus rings visible. No element invisible-against-background.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-RM-01 - Reduced motion: animation suppression

**Steps:**
1. Enable "Reduce animations" in Windows Accessibility settings.
2. Open Workbench and trigger a completion toast.
3. Observe whether toast animation is suppressed.
4. Open Reader and trigger a completion; observe toast.

**Expected:** When `SystemParameters.ClientAreaAnimation` is false, toast animation is suppressed. Source code path confirmed by automated test. No uncontrolled flashing or rapid motion.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-NW-01 - Narrow window: Workbench and Settings at minimum bounds

**Steps:**
1. Open Workbench. Drag left edge toward MinWidth (1040 px). Verify UI does not break.
2. Open Settings. Drag to MinWidth (900 px). Verify form fields remain readable.
3. Attempt to resize below MinWidth; confirm window refuses.

**Expected:** Workbench at MinWidth=1040: all primary controls visible. Settings at MinWidth=900: form fields and tabs accessible. Windows cannot resize below XAML-declared minimum.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-RTL-01 - RTL/mixed text: Reader document display

**Steps:**
1. Open Reader. Load a document with Arabic, Hebrew, or mixed RTL/LTR paragraphs.
2. Observe per-paragraph FlowDirection assignment.
3. Observe inline RTL/LTR runs within mixed paragraphs.
4. Tab to draft text box; verify cursor movement follows text flow.

**Expected:** RTL paragraphs display right-aligned with right-to-left reading order. Mixed paragraphs show correct bidirectional layout. No text overflow or clipping. Source code path confirmed by automated test.

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

### ACC-TRY-01 - Tray context menu keyboard navigation

**Steps:**
1. Minimize Workbench to tray.
2. Press Win+B to focus system tray. Navigate to Notype icon.
3. Press Enter or Space to open context menu.
4. Navigate menu items with arrow keys; activate one with Enter.

**Expected:** Tray icon reachable via keyboard. Context menu opens with WinForms keyboard behavior. Menu items navigable and activatable. No custom WPF code interferes (tray uses OS-native NotifyIcon + ContextMenuStrip).

**Status:** NOT TESTED | **Date:** — | **Observation:** —

---

## 6. Defects Found

No code defects were identified during the WP-25 source inspection. All keyboard focus
indicators were confirmed correct by source audit. No XAML or C# changes were required.

If any manual test case above produces a FAIL result, file a bounded defect with:
- Case ID and exact observation
- Screenshot or screen recording
- Proposed bounded fix
- Rerun that specific case after the fix

---

## 7. Evidence Audit Trail

| Activity | Date | Operator | Outcome |
| --- | --- | --- | --- |
| Source inspection (all XAML styles) | 2026-09-14 | Agent | All focus indicators PASS |
| Automated test additions (WP-25, 3 new tests) | 2026-09-14 | Agent | Committed with WP-25 HEAD |
| Full Release gate | 2026-09-14 | Agent | >=1028 passed, 2 skipped, 0 failed |
| Manual keyboard cases (ACC-KB-01 to ACC-KB-07) | - | (pending operator) | NOT TESTED |
| Screen reader cases (ACC-SR-01 to ACC-SR-02) | - | (pending operator) | NOT TESTED |
| 200% DPI case (ACC-DPI-01) | - | (pending operator) | NOT TESTED |
| High contrast case (ACC-HC-01) | - | (pending operator) | NOT TESTED |
| Reduced motion case (ACC-RM-01) | - | (pending operator) | NOT TESTED |
| Narrow window case (ACC-NW-01) | - | (pending operator) | NOT TESTED |
| RTL case (ACC-RTL-01) | - | (pending operator) | NOT TESTED |
| Tray keyboard case (ACC-TRY-01) | - | (pending operator) | NOT TESTED |
