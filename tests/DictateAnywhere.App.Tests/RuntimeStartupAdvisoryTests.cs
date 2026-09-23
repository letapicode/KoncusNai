using DictateAnywhere.App.Experience;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class RuntimeStartupAdvisoryTests
{
  [Xunit.Fact]
  public void DescribeOverlayVisibilityRisk_ReturnsWarning_WhenOverlayDisabledInDictateAnywhereMode()
  {
    AppSettings settings = AppSettings.Default with
    {
      OverlayEnabled = false,
    };

    string? warning = RuntimeStartupAdvisory.DescribeOverlayVisibilityRisk(settings);

    Xunit.Assert.NotNull(warning);
    Xunit.Assert.Contains("Overlay is disabled", warning);
  }

  [Xunit.Fact]
  public void IsRecordingFeedbackVisible_ReturnsFalse_WhenIndicatorAndFallbackAreBothDisabled()
  {
    AppSettings settings = AppSettings.Default with
    {
      OverlayEnabled = true,
      CaretIndicatorEnabled = false,
      FallbackToCornerOverlay = false,
    };

    bool visible = RuntimeStartupAdvisory.IsRecordingFeedbackVisible(settings);

    Xunit.Assert.False(visible);
  }

  [Xunit.Fact]
  public void BuildNotice_CombinesHotkeyFallbackAndOverlayWarning_WhenBothApply()
  {
    AppSettings settings = AppSettings.Default with
    {
      OverlayEnabled = false,
    };
    GlobalHotkeyRegistrationOutcome hotkeyOutcome = new(
      Success: true,
      ActiveBinding: GlobalToggleSettingsPolicy.FallbackHotkey,
      UsedFallbackBinding: true,
      StatusMessage: "Alt + Space unavailable. Fallback active: Win + Alt + Space.",
      DiagnosticsMessage: "fallback");

    RuntimeStartupNotice? notice = RuntimeStartupAdvisory.BuildNotice(settings, hotkeyOutcome);

    Xunit.Assert.NotNull(notice);
    Xunit.Assert.Equal("Runtime Attention Required", notice!.Title);
    Xunit.Assert.Contains("Fallback active", notice.Message);
    Xunit.Assert.Contains("Overlay is disabled", notice.Message);
  }
}
