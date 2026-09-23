# UI Redesign Compatibility

## Goal
Allow future visual redesign work in `DictateAnywhere.App` without reworking runtime orchestration, settings policy, or Workbench behavior.

## Redesign-Safe Seams
- `src/DictateAnywhere.App/Presentation`
  - Holds small presentation contracts that convert runtime/settings data into UI-ready labels and status state.
  - Current contracts include:
    - `ExperienceModeOptionViewModel`
    - `RecordingModeOptionViewModel`
    - `InsertionMethodOptionViewModel`
    - `ModelOptionViewModel`
    - `ProfileOptionViewModel`
    - `VoiceSnippetOptionViewModel`
    - `AudioDeviceOptionViewModel`
    - `WorkbenchViewModel`
    - `ProfilePresentationFormatter`
    - `ThemeResourceResolver`
- `src/DictateAnywhere.App/Theming`
  - Holds WPF resource dictionaries for colors, spacing, and shared control styles.
  - Current dictionaries:
    - `DesignTokens.xaml`
    - `ControlStyles.xaml`

## Rules
- Window code-behind may compose runtime services, but label-building and status-color selection must go through `Presentation/*`.
- Runtime/service modules must not depend on `Presentation/*` or `Theming/*`.
- Visual redesign work should prefer replacing XAML layout and resource dictionaries before changing window behavior code.
- Shared UI status semantics are expressed with `UiStatusKind`, not per-window brush constants.

## Current Coverage
- `SettingsWindow`, `FirstRunWizardWindow`, and `TextboxWorkbenchWindow` consume the shared presentation contracts.
- App-level smoke tests verify both flows:
  - Dictate Anywhere label/summary contracts
  - Workbench state-to-view-model mapping

## Follow-On Work
- Move remaining UI-specific selection state out of code-behind where it still directly binds to settings records.
- Introduce adapter seams before attempting the larger `DictateAnywhere.App` dependency inversion.
