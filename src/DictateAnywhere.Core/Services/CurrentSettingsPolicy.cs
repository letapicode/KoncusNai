using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Services;

/// <summary>
/// Defines the intentionally small current product configuration. Historical
/// profile and experience-mode fields remain readable only by historical
/// persistence readers and never enter the current runtime contract.
/// </summary>
public static class CurrentSettingsPolicy
{
  public static AppSettings Normalize(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    string chatTypefaceId = ChatTypefaceSettings.Normalize(settings.ChatTypefaceId);
    if (IsCurrent(settings)
        && string.Equals(settings.ChatTypefaceId, chatTypefaceId, StringComparison.Ordinal))
    {
      return settings;
    }

    return settings with
    {
      RecordingMode = RecordingMode.ToggleToTalk,
      OverlayEnabled = true,
      CaretIndicatorEnabled = true,
      FallbackToCornerOverlay = true,
      PreferredInsertionMethod = InsertionMethod.ClipboardPaste,
      RestoreClipboard = true,
      InsertionBlockedProcessNames = Array.Empty<string>(),
      ChatTypefaceId = chatTypefaceId,
      WorkbenchZoomPercent = Math.Clamp(settings.WorkbenchZoomPercent, 80, 150),
    };
  }

  private static bool IsCurrent(AppSettings settings)
  {
    return settings.RecordingMode == RecordingMode.ToggleToTalk
        && settings.OverlayEnabled
        && settings.CaretIndicatorEnabled
        && settings.FallbackToCornerOverlay
        && settings.PreferredInsertionMethod == InsertionMethod.ClipboardPaste
        && settings.RestoreClipboard
        && settings.InsertionBlockedProcessNames.Count == 0
        && settings.WorkbenchZoomPercent is >= 80 and <= 150;
  }
}
