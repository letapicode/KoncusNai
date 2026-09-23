using System;
using System.Collections.Generic;

namespace DictateAnywhere.Core.Contracts;

public sealed record AppSettings(
  HotkeyBinding Hotkey,
  RecordingMode RecordingMode,
  string TranscriptionProviderId,
  string TranscriptionModelId,
  string TranscriptionLanguage,
  InsertionMethod PreferredInsertionMethod,
  bool RestoreClipboard,
  bool OverlayEnabled,
  bool CaretIndicatorEnabled,
  bool FallbackToCornerOverlay,
  string? PreferredAudioInputDeviceId,
  bool HasCompletedFirstRun,
  HotkeyBinding UndoHotkey,
  bool EnableSecureFieldDetection,
  IReadOnlyList<string> InsertionBlockedProcessNames,
  bool EnableElevatedInsertion,
  bool EnableDictationCommands,
  HotkeyBinding? RetryLastDictationHotkey = null,
  int LastDictationRetryWindowSeconds = 900,
  AppThemePreference ThemePreference = AppThemePreference.Dark,
  int ChatOutputFontSize = 15,
  string ChatTypefaceId = ChatTypefaceIds.System,
  bool EnableAutomaticPunctuation = true,
  int WorkbenchZoomPercent = 100,
  bool AssistantFeaturesEnabled = true,
  string? CrisperWhisperLicenseAcceptanceVersion = null,
  string? LegalAcceptanceVersion = null,
  DateTimeOffset? LegalAcceptanceAcceptedAtUtc = null)
{
  public const int MinChatOutputFontSize = 10;
  public const int MaxChatOutputFontSize = 36;
  public const int MinRetryWindowSeconds = 10;
  public const int MaxRetryWindowSeconds = 86400;

  public static HotkeyBinding AltSpaceDefaultHotkey { get; } =
    new(HotkeyModifiers.Alt, 0x20);

  public static HotkeyBinding WorkbenchDefaultHotkey { get; } =
    AltSpaceDefaultHotkey;

  public static HotkeyBinding GlobalToggleDefaultHotkey { get; } =
    AltSpaceDefaultHotkey;

  public static HotkeyBinding AutomaticFallbackHotkey { get; } =
    new(HotkeyModifiers.Control, 0x20);

  public static AppSettings Default { get; } = new(
    Hotkey: AltSpaceDefaultHotkey,
    RecordingMode: RecordingMode.ToggleToTalk,
    TranscriptionProviderId: TranscriptionProviderIds.CohereLocal,
    TranscriptionModelId: "cohere-transcribe-03-2026",
    TranscriptionLanguage: TranscriptionLanguageSettings.DefaultLanguage,
    PreferredInsertionMethod: InsertionMethod.ClipboardPaste,
    RestoreClipboard: true,
    OverlayEnabled: true,
    CaretIndicatorEnabled: true,
    FallbackToCornerOverlay: true,
    PreferredAudioInputDeviceId: null,
    HasCompletedFirstRun: false,
    UndoHotkey: new HotkeyBinding(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x5A),
    EnableSecureFieldDetection: true,
    InsertionBlockedProcessNames: Array.Empty<string>(),
    EnableElevatedInsertion: false,
    EnableDictationCommands: false,
    RetryLastDictationHotkey: new HotkeyBinding(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x59),
    LastDictationRetryWindowSeconds: 900,
    ThemePreference: AppThemePreference.Dark,
    ChatOutputFontSize: 15,
    ChatTypefaceId: ChatTypefaceIds.System,
    EnableAutomaticPunctuation: true);
}
