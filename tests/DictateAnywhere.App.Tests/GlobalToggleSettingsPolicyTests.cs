using DictateAnywhere.App.Experience;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class GlobalToggleSettingsPolicyTests
{
  [Xunit.Fact]
  public void ApplyPreset_EnforcesToggleAndAltSpace()
  {
    AppSettings input = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
      Hotkey = new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41),
    };

    AppSettings normalized = GlobalToggleSettingsPolicy.ApplyPreset(input);

    Xunit.Assert.Equal(RecordingMode.ToggleToTalk, normalized.RecordingMode);
    Xunit.Assert.Equal(GlobalToggleSettingsPolicy.PreferredHotkey, normalized.Hotkey);
  }

  [Xunit.Fact]
  public void ApplyPreset_PreservesCommands_ButRemovesRetiredBlockedProcessBehavior()
  {
    AppSettings input = AppSettings.Default with
    {
      EnableDictationCommands = true,
      InsertionBlockedProcessNames = new[] { "windsurf", "antigravity" },
    };

    AppSettings normalized = GlobalToggleSettingsPolicy.ApplyPreset(input);

    Xunit.Assert.Empty(normalized.InsertionBlockedProcessNames);
    Xunit.Assert.True(normalized.EnableDictationCommands);
  }

  [Xunit.Fact]
  public void ShouldAutoFallback_ReturnsTrue_ForPreferredHotkeyRegistrationFailure()
  {
    bool shouldFallback = GlobalToggleSettingsPolicy.ShouldAutoFallback(
      GlobalToggleSettingsPolicy.PreferredHotkey,
      new HotkeyRegistrationResult(false, "already registered"));

    Xunit.Assert.True(shouldFallback);
  }

  [Xunit.Fact]
  public void ShouldAutoFallback_ReturnsFalse_ForCustomHotkeyRegistrationFailure()
  {
    bool shouldFallback = GlobalToggleSettingsPolicy.ShouldAutoFallback(
      new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41),
      new HotkeyRegistrationResult(false, "already registered"));

    Xunit.Assert.False(shouldFallback);
  }

  [Xunit.Fact]
  public void WorkbenchPolicy_PreservesRequiredModeAndOverlay()
  {
    AppSettings globallyPreset = GlobalToggleSettingsPolicy.ApplyPreset(AppSettings.Default);
    AppSettings workbench = Workbench.WorkbenchSettingsPolicy.Normalize(globallyPreset);

    Xunit.Assert.True(workbench.OverlayEnabled);
    Xunit.Assert.True(workbench.CaretIndicatorEnabled);
    Xunit.Assert.True(workbench.FallbackToCornerOverlay);
    Xunit.Assert.Equal(RecordingMode.ToggleToTalk, workbench.RecordingMode);
  }
}
