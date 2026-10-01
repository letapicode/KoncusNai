using System;
using System.Linq;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Services;

/// <summary>Copies mutable settings input and applies only fields edited relative to an owner's baseline.</summary>
public static class SettingsSnapshot
{
  public static AppSettings Capture(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    string[] names = settings.InsertionBlockedProcessNames.ToArray();
    return settings with { InsertionBlockedProcessNames = names.Length == 0 ? Array.Empty<string>() : Array.AsReadOnly(names) };
  }

  // Whole-document replacement remains explicit; stale owners supply their original
  // baseline. Storage checks conflicting edits before applying this pure projection.
  public static AppSettings Merge(AppSettings baseline, AppSettings edited, AppSettings current)
  {
    ArgumentNullException.ThrowIfNull(baseline);
    ArgumentNullException.ThrowIfNull(edited);
    ArgumentNullException.ThrowIfNull(current);
    bool modelChanged = edited.TranscriptionProviderId != baseline.TranscriptionProviderId
      || edited.TranscriptionModelId != baseline.TranscriptionModelId;
    bool runtimeChanged = edited.DictationRuntimePreference != baseline.DictationRuntimePreference
      || edited.DictationRuntimeDevice != baseline.DictationRuntimeDevice;
    bool legalChanged = edited.LegalAcceptanceVersion != baseline.LegalAcceptanceVersion
      || edited.LegalAcceptanceAcceptedAtUtc != baseline.LegalAcceptanceAcceptedAtUtc;
    return Capture(current with
    {
      Hotkey = edited.Hotkey != baseline.Hotkey ? edited.Hotkey : current.Hotkey,
      RecordingMode = edited.RecordingMode != baseline.RecordingMode ? edited.RecordingMode : current.RecordingMode,
      TranscriptionProviderId = modelChanged ? edited.TranscriptionProviderId : current.TranscriptionProviderId,
      TranscriptionModelId = modelChanged ? edited.TranscriptionModelId : current.TranscriptionModelId,
      TranscriptionLanguage = edited.TranscriptionLanguage != baseline.TranscriptionLanguage ? edited.TranscriptionLanguage : current.TranscriptionLanguage,
      PreferredInsertionMethod = edited.PreferredInsertionMethod != baseline.PreferredInsertionMethod ? edited.PreferredInsertionMethod : current.PreferredInsertionMethod,
      RestoreClipboard = edited.RestoreClipboard != baseline.RestoreClipboard ? edited.RestoreClipboard : current.RestoreClipboard,
      OverlayEnabled = edited.OverlayEnabled != baseline.OverlayEnabled ? edited.OverlayEnabled : current.OverlayEnabled,
      CaretIndicatorEnabled = edited.CaretIndicatorEnabled != baseline.CaretIndicatorEnabled ? edited.CaretIndicatorEnabled : current.CaretIndicatorEnabled,
      FallbackToCornerOverlay = edited.FallbackToCornerOverlay != baseline.FallbackToCornerOverlay ? edited.FallbackToCornerOverlay : current.FallbackToCornerOverlay,
      PreferredAudioInputDeviceId = edited.PreferredAudioInputDeviceId != baseline.PreferredAudioInputDeviceId ? edited.PreferredAudioInputDeviceId : current.PreferredAudioInputDeviceId,
      HasCompletedFirstRun = edited.HasCompletedFirstRun != baseline.HasCompletedFirstRun ? edited.HasCompletedFirstRun : current.HasCompletedFirstRun,
      UndoHotkey = edited.UndoHotkey != baseline.UndoHotkey ? edited.UndoHotkey : current.UndoHotkey,
      EnableSecureFieldDetection = edited.EnableSecureFieldDetection != baseline.EnableSecureFieldDetection ? edited.EnableSecureFieldDetection : current.EnableSecureFieldDetection,
      InsertionBlockedProcessNames = !edited.InsertionBlockedProcessNames.SequenceEqual(baseline.InsertionBlockedProcessNames) ? edited.InsertionBlockedProcessNames : current.InsertionBlockedProcessNames,
      EnableElevatedInsertion = edited.EnableElevatedInsertion != baseline.EnableElevatedInsertion ? edited.EnableElevatedInsertion : current.EnableElevatedInsertion,
      EnableDictationCommands = edited.EnableDictationCommands != baseline.EnableDictationCommands ? edited.EnableDictationCommands : current.EnableDictationCommands,
      RetryLastDictationHotkey = edited.RetryLastDictationHotkey != baseline.RetryLastDictationHotkey ? edited.RetryLastDictationHotkey : current.RetryLastDictationHotkey,
      LastDictationRetryWindowSeconds = edited.LastDictationRetryWindowSeconds != baseline.LastDictationRetryWindowSeconds ? edited.LastDictationRetryWindowSeconds : current.LastDictationRetryWindowSeconds,
      ThemePreference = edited.ThemePreference != baseline.ThemePreference ? edited.ThemePreference : current.ThemePreference,
      ChatOutputFontSize = edited.ChatOutputFontSize != baseline.ChatOutputFontSize ? edited.ChatOutputFontSize : current.ChatOutputFontSize,
      ChatTypefaceId = edited.ChatTypefaceId != baseline.ChatTypefaceId ? edited.ChatTypefaceId : current.ChatTypefaceId,
      EnableAutomaticPunctuation = edited.EnableAutomaticPunctuation != baseline.EnableAutomaticPunctuation ? edited.EnableAutomaticPunctuation : current.EnableAutomaticPunctuation,
      WorkbenchZoomPercent = edited.WorkbenchZoomPercent != baseline.WorkbenchZoomPercent ? edited.WorkbenchZoomPercent : current.WorkbenchZoomPercent,
      AssistantFeaturesEnabled = edited.AssistantFeaturesEnabled != baseline.AssistantFeaturesEnabled ? edited.AssistantFeaturesEnabled : current.AssistantFeaturesEnabled,
      CrisperWhisperLicenseAcceptanceVersion = edited.CrisperWhisperLicenseAcceptanceVersion != baseline.CrisperWhisperLicenseAcceptanceVersion ? edited.CrisperWhisperLicenseAcceptanceVersion : current.CrisperWhisperLicenseAcceptanceVersion,
      LegalAcceptanceVersion = legalChanged ? edited.LegalAcceptanceVersion : current.LegalAcceptanceVersion,
      LegalAcceptanceAcceptedAtUtc = legalChanged ? edited.LegalAcceptanceAcceptedAtUtc : current.LegalAcceptanceAcceptedAtUtc,
      ChatPaperViewEnabled = edited.ChatPaperViewEnabled != baseline.ChatPaperViewEnabled ? edited.ChatPaperViewEnabled : current.ChatPaperViewEnabled,
      DictationRuntimePreference = runtimeChanged ? edited.DictationRuntimePreference : current.DictationRuntimePreference,
      DictationRuntimeDevice = runtimeChanged ? edited.DictationRuntimeDevice : current.DictationRuntimeDevice,
    });
  }

  /// <summary>True when both owners changed the same field/group to different values.</summary>
  public static bool HasConflict(AppSettings baseline, AppSettings edited, AppSettings current)
  {
    // Disjoint updates commute. Reuse the typed field/group policy rather than
    // introducing a second property catalogue or reflection-based mutations.
    AppSettings left = Merge(baseline, edited, current);
    AppSettings right = Merge(baseline, current, edited);
    return (left with { InsertionBlockedProcessNames = right.InsertionBlockedProcessNames }) != right
      || !left.InsertionBlockedProcessNames.SequenceEqual(right.InsertionBlockedProcessNames);
  }
}
