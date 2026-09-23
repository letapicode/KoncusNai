using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class RuntimeSettingsRestartPolicyTests
{
  [Xunit.Fact]
  public void RequiresRestart_ReturnsFalse_ForPresentationAndMetadataChanges()
  {
    AppSettings active = AppSettings.Default;
    AppSettings updated = active with
    {
      HasCompletedFirstRun = !active.HasCompletedFirstRun,
      ThemePreference = AppThemePreference.Light,
      ChatOutputFontSize = 24,
      ChatTypefaceId = ChatTypefaceIds.BookSerif,
      LastDictationRetryWindowSeconds = active.LastDictationRetryWindowSeconds + 30,
    };

    Xunit.Assert.False(RuntimeSettingsRestartPolicy.RequiresRestart(active, updated));
  }

  [Xunit.Fact]
  public void RequiresRestart_ReturnsTrue_WhenTranscriptionModelChanges()
  {
    AppSettings updated = AppSettings.Default with
    {
      TranscriptionModelId = "another-model",
    };

    Xunit.Assert.True(RuntimeSettingsRestartPolicy.RequiresRestart(AppSettings.Default, updated));
  }

  [Xunit.Fact]
  public void RequiresRestart_UsesSupportedCollectionValuesInsteadOfCollectionIdentity()
  {
    AppSettings equivalent = AppSettings.Default with
    {
      InsertionBlockedProcessNames = AppSettings.Default.InsertionBlockedProcessNames.ToArray(),
    };

    Xunit.Assert.False(RuntimeSettingsRestartPolicy.RequiresRestart(AppSettings.Default, equivalent));
  }
}
