using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Tests;

public sealed class CurrentSettingsPolicyTests
{
  [Xunit.Fact]
  public void AppSettings_CurrentContract_OmitsRetiredProductFields()
  {
    string[] retiredProperties =
    [
      "ActiveModelId",
      "ActiveProfileId",
      "ExperienceMode",
      "Profiles",
      "AppProfileRules",
    ];

    foreach (string propertyName in retiredProperties)
    {
      Xunit.Assert.Null(typeof(AppSettings).GetProperty(propertyName));
    }
  }

  [Xunit.Fact]
  public void Normalize_EnforcesCurrentProductConfiguration()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
      OverlayEnabled = false,
      CaretIndicatorEnabled = false,
      FallbackToCornerOverlay = false,
      EnableDictationCommands = true,
      InsertionBlockedProcessNames = new[] { "code" },
    };

    AppSettings normalized = CurrentSettingsPolicy.Normalize(settings);

    Xunit.Assert.Equal(RecordingMode.ToggleToTalk, normalized.RecordingMode);
    Xunit.Assert.True(normalized.OverlayEnabled);
    Xunit.Assert.True(normalized.CaretIndicatorEnabled);
    Xunit.Assert.True(normalized.FallbackToCornerOverlay);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, normalized.PreferredInsertionMethod);
    Xunit.Assert.True(normalized.RestoreClipboard);
    Xunit.Assert.Empty(normalized.InsertionBlockedProcessNames);
    Xunit.Assert.True(normalized.EnableDictationCommands);
  }

  [Xunit.Fact]
  public void Normalize_ProducesStableBehaviorWhenAppliedMoreThanOnce()
  {
    AppSettings once = CurrentSettingsPolicy.Normalize(AppSettings.Default);
    AppSettings twice = CurrentSettingsPolicy.Normalize(once);

    Xunit.Assert.Equal(once.RecordingMode, twice.RecordingMode);
    Xunit.Assert.True(twice.OverlayEnabled);
    Xunit.Assert.True(twice.CaretIndicatorEnabled);
    Xunit.Assert.True(twice.FallbackToCornerOverlay);
    Xunit.Assert.True(twice.RestoreClipboard);
    Xunit.Assert.Equal(once.PreferredInsertionMethod, twice.PreferredInsertionMethod);
    Xunit.Assert.Empty(twice.InsertionBlockedProcessNames);
  }

  [Xunit.Fact]
  public void Normalize_PreservesSpokenFormattingCommandPreference()
  {
    AppSettings settings = AppSettings.Default with
    {
      RestoreClipboard = false,
      EnableDictationCommands = true,
    };

    AppSettings normalized = CurrentSettingsPolicy.Normalize(settings);

    Xunit.Assert.True(normalized.RestoreClipboard);
    Xunit.Assert.True(normalized.EnableDictationCommands);
  }

  [Xunit.Fact]
  public void Normalize_ReplacesUnknownChatTypefaceWithSystemDefault()
  {
    AppSettings normalized = CurrentSettingsPolicy.Normalize(AppSettings.Default with
    {
      ChatTypefaceId = "missing-font",
    });

    Xunit.Assert.Equal(ChatTypefaceIds.System, normalized.ChatTypefaceId);
  }
}
