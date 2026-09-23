using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Settings;

internal sealed class CurrentSettingsDocument
{
  public int SchemaVersion { get; init; } = SettingsSchema.CurrentVersion;
  public int HotkeyModifiers { get; init; } = (int)AppSettings.Default.Hotkey.Modifiers;
  public int HotkeyVirtualKey { get; init; } = AppSettings.Default.Hotkey.VirtualKey;
  public int UndoHotkeyModifiers { get; init; } = (int)AppSettings.Default.UndoHotkey.Modifiers;
  public int UndoHotkeyVirtualKey { get; init; } = AppSettings.Default.UndoHotkey.VirtualKey;
  public string TranscriptionProviderId { get; init; } = TranscriptionProviderIds.CohereLocal;
  public string TranscriptionModelId { get; init; } = AppSettings.Default.TranscriptionModelId;
  public string TranscriptionLanguage { get; init; } = TranscriptionLanguageSettings.DefaultLanguage;
  public string? PreferredAudioInputDeviceId { get; init; }
  public bool HasCompletedFirstRun { get; init; }
  public bool EnableSecureFieldDetection { get; init; } = AppSettings.Default.EnableSecureFieldDetection;
  public bool EnableElevatedInsertion { get; init; }
  public bool EnableDictationCommands { get; init; }
  public int RetryLastDictationHotkeyModifiers { get; init; } = (int)AppSettings.Default.RetryLastDictationHotkey!.Modifiers;
  public int RetryLastDictationHotkeyVirtualKey { get; init; } = AppSettings.Default.RetryLastDictationHotkey!.VirtualKey;
  public int LastDictationRetryWindowSeconds { get; init; } = AppSettings.Default.LastDictationRetryWindowSeconds;
  public int ThemePreference { get; init; } = (int)AppSettings.Default.ThemePreference;
  public int ChatOutputFontSize { get; init; } = AppSettings.Default.ChatOutputFontSize;
  public string ChatTypefaceId { get; init; } = ChatTypefaceIds.System;
  public int WorkbenchZoomPercent { get; init; } = 100;
  public bool AssistantFeaturesEnabled { get; init; } = true;
  public string? CrisperWhisperLicenseAcceptanceVersion { get; init; }
  public string? LegalAcceptanceVersion { get; init; }
  public DateTimeOffset? LegalAcceptanceAcceptedAtUtc { get; init; }
  public bool EnableAutomaticPunctuation { get; init; } = true;

  public static CurrentSettingsDocument FromSettings(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    return new CurrentSettingsDocument
    {
      HotkeyModifiers = (int)settings.Hotkey.Modifiers,
      HotkeyVirtualKey = settings.Hotkey.VirtualKey,
      UndoHotkeyModifiers = (int)settings.UndoHotkey.Modifiers,
      UndoHotkeyVirtualKey = settings.UndoHotkey.VirtualKey,
      TranscriptionProviderId = settings.GetConfiguredTranscriptionProviderId(),
      TranscriptionModelId = settings.GetConfiguredTranscriptionModelId(),
      TranscriptionLanguage = TranscriptionLanguageSettings.NormalizeGlobal(settings.TranscriptionLanguage),
      PreferredAudioInputDeviceId = settings.PreferredAudioInputDeviceId,
      HasCompletedFirstRun = settings.HasCompletedFirstRun,
      EnableSecureFieldDetection = settings.EnableSecureFieldDetection,
      EnableElevatedInsertion = settings.EnableElevatedInsertion,
      EnableDictationCommands = settings.EnableDictationCommands,
      RetryLastDictationHotkeyModifiers = (int)(settings.RetryLastDictationHotkey ?? AppSettings.Default.RetryLastDictationHotkey!).Modifiers,
      RetryLastDictationHotkeyVirtualKey = (settings.RetryLastDictationHotkey ?? AppSettings.Default.RetryLastDictationHotkey!).VirtualKey,
      LastDictationRetryWindowSeconds = Math.Clamp(settings.LastDictationRetryWindowSeconds, 60, 86_400),
      ThemePreference = (int)settings.ThemePreference,
      ChatOutputFontSize = Math.Clamp(settings.ChatOutputFontSize, 12, 30),
      ChatTypefaceId = ChatTypefaceSettings.Normalize(settings.ChatTypefaceId),
      EnableAutomaticPunctuation = settings.EnableAutomaticPunctuation,
      WorkbenchZoomPercent = Math.Clamp(settings.WorkbenchZoomPercent, 80, 150),
      AssistantFeaturesEnabled = settings.AssistantFeaturesEnabled,
      CrisperWhisperLicenseAcceptanceVersion = settings.CrisperWhisperLicenseAcceptanceVersion,
      LegalAcceptanceVersion = settings.LegalAcceptanceVersion,
      LegalAcceptanceAcceptedAtUtc = settings.LegalAcceptanceAcceptedAtUtc,
    };
  }

  public AppSettings ToSettings()
  {
    AppSettings defaults = AppSettings.Default;
    return defaults with
    {
      Hotkey = new HotkeyBinding((HotkeyModifiers)HotkeyModifiers, HotkeyVirtualKey),
      UndoHotkey = new HotkeyBinding((HotkeyModifiers)UndoHotkeyModifiers, UndoHotkeyVirtualKey),
      TranscriptionProviderId = TranscriptionProviderId,
      TranscriptionModelId = TranscriptionModelId,
      TranscriptionLanguage = TranscriptionLanguageSettings.NormalizeGlobal(TranscriptionLanguage),
      PreferredAudioInputDeviceId = PreferredAudioInputDeviceId,
      HasCompletedFirstRun = HasCompletedFirstRun,
      EnableSecureFieldDetection = EnableSecureFieldDetection,
      EnableElevatedInsertion = EnableElevatedInsertion,
      EnableDictationCommands = EnableDictationCommands,
      RetryLastDictationHotkey = new HotkeyBinding(
        (HotkeyModifiers)RetryLastDictationHotkeyModifiers,
        RetryLastDictationHotkeyVirtualKey),
      LastDictationRetryWindowSeconds = Math.Clamp(LastDictationRetryWindowSeconds, 60, 86_400),
      ThemePreference = Enum.IsDefined(typeof(AppThemePreference), ThemePreference)
        ? (AppThemePreference)ThemePreference
        : defaults.ThemePreference,
      ChatOutputFontSize = Math.Clamp(ChatOutputFontSize, 12, 30),
      ChatTypefaceId = ChatTypefaceSettings.Normalize(ChatTypefaceId),
      EnableAutomaticPunctuation = EnableAutomaticPunctuation,
      WorkbenchZoomPercent = Math.Clamp(WorkbenchZoomPercent, 80, 150),
      AssistantFeaturesEnabled = AssistantFeaturesEnabled,
      CrisperWhisperLicenseAcceptanceVersion = CrisperWhisperLicenseAcceptanceVersion,
      LegalAcceptanceVersion = LegalAcceptanceVersion,
      LegalAcceptanceAcceptedAtUtc = LegalAcceptanceAcceptedAtUtc,
    };
  }

}
