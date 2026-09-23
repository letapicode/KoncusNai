using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Experience;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchSettingsPolicyTests
{
  [Xunit.Fact]
  public void Normalize_UsesRequiredDictationModeAndOverlay()
  {
    AppSettings input = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
      OverlayEnabled = true,
    };

    AppSettings normalized = WorkbenchSettingsPolicy.Normalize(input);

    Xunit.Assert.Equal(RecordingMode.ToggleToTalk, normalized.RecordingMode);
    Xunit.Assert.True(normalized.OverlayEnabled);
    Xunit.Assert.True(normalized.CaretIndicatorEnabled);
    Xunit.Assert.True(normalized.FallbackToCornerOverlay);
  }

  [Xunit.Fact]
  public void Normalize_RewritesDefaultHotkeyToPreferredWorkbenchHotkey()
  {
    AppSettings input = AppSettings.Default with
    {
      Hotkey = AppSettings.Default.Hotkey,
    };

    AppSettings normalized = WorkbenchSettingsPolicy.Normalize(input);

    Xunit.Assert.Equal(WorkbenchSettingsPolicy.PreferredHotkey, normalized.Hotkey);
  }

  [Xunit.Fact]
  public void ShouldAutoFallback_ReturnsTrue_ForPreferredHotkeyRegistrationFailure()
  {
    bool shouldFallback = WorkbenchSettingsPolicy.ShouldAutoFallback(
      WorkbenchSettingsPolicy.PreferredHotkey,
      new HotkeyRegistrationResult(false, "already registered"));

    Xunit.Assert.True(shouldFallback);
  }

  [Xunit.Fact]
  public void ShouldAutoFallback_ReturnsFalse_ForCustomHotkeyRegistrationFailure()
  {
    bool shouldFallback = WorkbenchSettingsPolicy.ShouldAutoFallback(
      new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41),
      new HotkeyRegistrationResult(false, "already registered"));

    Xunit.Assert.False(shouldFallback);
  }

  [Xunit.Fact]
  public void Normalize_AfterGlobalTogglePreset_ReappliesWorkbenchConstraints()
  {
    AppSettings globallyPreset = GlobalToggleSettingsPolicy.ApplyPreset(AppSettings.Default);

    AppSettings normalized = WorkbenchSettingsPolicy.Normalize(globallyPreset);

    Xunit.Assert.Equal(RecordingMode.ToggleToTalk, normalized.RecordingMode);
    Xunit.Assert.True(normalized.OverlayEnabled);
    Xunit.Assert.Equal(WorkbenchSettingsPolicy.PreferredHotkey, normalized.Hotkey);
  }
}
