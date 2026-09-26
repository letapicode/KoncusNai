using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Settings;

/// <summary>
/// Immutable, typed draft representing active uncommitted settings edits across all domains.
/// Distinguishes ephemeral preview appearance from persisted configuration, represents
/// unsupported schema states as read-only, and enforces the complete absence of legacy history configuration.
/// </summary>
public sealed record SettingsDraft
{
  // Hotkeys Domain
  public HotkeyBinding Hotkey { get; init; } = AppSettings.AltSpaceDefaultHotkey;
  public HotkeyBinding UndoHotkey { get; init; } = new(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x5A);
  public HotkeyBinding? RetryLastDictationHotkey { get; init; } = new(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x59);
  public int LastDictationRetryWindowSeconds { get; init; } = 900;

  // Transcription & Models Domain
  public string TranscriptionProviderId { get; init; } = TranscriptionProviderIds.CohereLocal;
  public string TranscriptionModelId { get; init; } = "cohere-transcribe-03-2026";
  public string TranscriptionLanguage { get; init; } = TranscriptionLanguageSettings.DefaultLanguage;
  public bool EnableAutomaticPunctuation { get; init; } = true;

  // Audio / Hardware Domain
  public string? PreferredAudioInputDeviceId { get; init; }

  // Dictation Behavior Domain
  public RecordingMode RecordingMode { get; init; } = RecordingMode.ToggleToTalk;
  public InsertionMethod PreferredInsertionMethod { get; init; } = InsertionMethod.ClipboardPaste;
  public bool RestoreClipboard { get; init; } = true;
  public bool OverlayEnabled { get; init; } = true;
  public bool CaretIndicatorEnabled { get; init; } = true;
  public bool FallbackToCornerOverlay { get; init; } = true;
  public bool EnableSecureFieldDetection { get; init; } = true;
  public IReadOnlyList<string> InsertionBlockedProcessNames { get; init; } = Array.Empty<string>();
  public bool EnableElevatedInsertion { get; init; }
  public bool EnableDictationCommands { get; init; }

  // Appearance & Chat Domain (Persisted)
  public AppThemePreference ThemePreference { get; init; } = AppThemePreference.Dark;
  public int ChatOutputFontSize { get; init; } = 15;
  public string ChatTypefaceId { get; init; } = ChatTypefaceIds.System;
  public int WorkbenchZoomPercent { get; init; } = 100;
  public bool ChatPaperViewEnabled { get; init; }
  public bool AssistantFeaturesEnabled { get; init; } = true;
  public string? CrisperWhisperLicenseAcceptanceVersion { get; init; }
  public string? LegalAcceptanceVersion { get; init; }
  public DateTimeOffset? LegalAcceptanceAcceptedAtUtc { get; init; }

  // Ephemeral Previews (Not persisted to AppSettings)
  public AppThemePreference? PreviewThemePreference { get; init; }
  public int? PreviewChatOutputFontSize { get; init; }
  public string? PreviewChatTypefaceId { get; init; }

  // Effective Values (Preview takes precedence when active)
  public AppThemePreference EffectiveThemePreference => PreviewThemePreference ?? ThemePreference;
  public int EffectiveChatOutputFontSize => PreviewChatOutputFontSize ?? ChatOutputFontSize;
  public string EffectiveChatTypefaceId => PreviewChatTypefaceId ?? ChatTypefaceId;

  // Lifecycle
  public bool HasCompletedFirstRun { get; init; }

  // Read-Only / Schema Protection
  public bool IsReadOnly { get; init; }
  public string? ReadOnlyReason { get; init; }

  /// <summary>
  /// Factory to initialize a draft from a persisted AppSettings instance.
  /// </summary>
  public static SettingsDraft FromSettings(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    AppSettings normalized = CurrentSettingsPolicy.Normalize(settings);
    return new SettingsDraft
    {
      Hotkey = normalized.Hotkey,
      UndoHotkey = normalized.UndoHotkey,
      RetryLastDictationHotkey = normalized.RetryLastDictationHotkey,
      LastDictationRetryWindowSeconds = normalized.LastDictationRetryWindowSeconds,
      TranscriptionProviderId = normalized.TranscriptionProviderId,
      TranscriptionModelId = normalized.TranscriptionModelId,
      TranscriptionLanguage = normalized.TranscriptionLanguage,
      EnableAutomaticPunctuation = normalized.EnableAutomaticPunctuation,
      PreferredAudioInputDeviceId = normalized.PreferredAudioInputDeviceId,
      RecordingMode = normalized.RecordingMode,
      PreferredInsertionMethod = normalized.PreferredInsertionMethod,
      RestoreClipboard = normalized.RestoreClipboard,
      OverlayEnabled = normalized.OverlayEnabled,
      CaretIndicatorEnabled = normalized.CaretIndicatorEnabled,
      FallbackToCornerOverlay = normalized.FallbackToCornerOverlay,
      EnableSecureFieldDetection = normalized.EnableSecureFieldDetection,
      InsertionBlockedProcessNames = normalized.InsertionBlockedProcessNames,
      EnableElevatedInsertion = normalized.EnableElevatedInsertion,
      EnableDictationCommands = normalized.EnableDictationCommands,
      ThemePreference = normalized.ThemePreference,
      ChatOutputFontSize = normalized.ChatOutputFontSize,
      ChatTypefaceId = normalized.ChatTypefaceId,
      WorkbenchZoomPercent = normalized.WorkbenchZoomPercent,
      ChatPaperViewEnabled = normalized.ChatPaperViewEnabled,
      AssistantFeaturesEnabled = normalized.AssistantFeaturesEnabled,
      CrisperWhisperLicenseAcceptanceVersion = normalized.CrisperWhisperLicenseAcceptanceVersion,
      LegalAcceptanceVersion = normalized.LegalAcceptanceVersion,
      LegalAcceptanceAcceptedAtUtc = normalized.LegalAcceptanceAcceptedAtUtc,
      HasCompletedFirstRun = normalized.HasCompletedFirstRun,
      IsReadOnly = false,
      ReadOnlyReason = null,
      PreviewThemePreference = null,
      PreviewChatOutputFontSize = null,
      PreviewChatTypefaceId = null,
    };
  }

  /// <summary>
  /// Creates a read-only draft representing an unsupported schema or locked settings state.
  /// </summary>
  public static SettingsDraft CreateReadOnly(string reason) =>
    CreateReadOnly(AppSettings.Default, reason);

  /// <summary>
  /// Creates a read-only draft representing an unsupported schema or locked settings state with specific fallback settings.
  /// </summary>
  public static SettingsDraft CreateReadOnly(AppSettings fallbackSettings, string reason)
  {
    ArgumentNullException.ThrowIfNull(fallbackSettings);
    if (string.IsNullOrWhiteSpace(reason))
    {
      throw new ArgumentException("Read-only reason must not be empty.", nameof(reason));
    }

    SettingsDraft draft = FromSettings(fallbackSettings);
    return draft with
    {
      IsReadOnly = true,
      ReadOnlyReason = reason,
    };
  }

  /// <summary>
  /// Converts the draft to an AppSettings instance for persistence.
  /// Ephemeral previews are ignored in favor of persisted values.
  /// Throws InvalidOperationException if the draft is marked read-only.
  /// </summary>
  public AppSettings ToSettings()
  {
    if (IsReadOnly)
    {
      throw new InvalidOperationException($"Cannot convert read-only settings draft to AppSettings: {ReadOnlyReason ?? "Settings are read-only."}");
    }

    return new AppSettings(
      Hotkey: Hotkey,
      RecordingMode: RecordingMode,
      TranscriptionProviderId: TranscriptionProviderId,
      TranscriptionModelId: TranscriptionModelId,
      TranscriptionLanguage: TranscriptionLanguage,
      PreferredInsertionMethod: PreferredInsertionMethod,
      RestoreClipboard: RestoreClipboard,
      OverlayEnabled: OverlayEnabled,
      CaretIndicatorEnabled: CaretIndicatorEnabled,
      FallbackToCornerOverlay: FallbackToCornerOverlay,
      PreferredAudioInputDeviceId: PreferredAudioInputDeviceId,
      HasCompletedFirstRun: HasCompletedFirstRun,
      UndoHotkey: UndoHotkey,
      EnableSecureFieldDetection: EnableSecureFieldDetection,
      InsertionBlockedProcessNames: InsertionBlockedProcessNames,
      EnableElevatedInsertion: EnableElevatedInsertion,
      EnableDictationCommands: EnableDictationCommands,
      RetryLastDictationHotkey: RetryLastDictationHotkey,
      LastDictationRetryWindowSeconds: LastDictationRetryWindowSeconds,
      ThemePreference: ThemePreference,
      ChatOutputFontSize: ChatOutputFontSize,
      ChatTypefaceId: ChatTypefaceId,
      EnableAutomaticPunctuation: EnableAutomaticPunctuation,
      WorkbenchZoomPercent: WorkbenchZoomPercent,
      ChatPaperViewEnabled: ChatPaperViewEnabled,
      AssistantFeaturesEnabled: AssistantFeaturesEnabled,
      CrisperWhisperLicenseAcceptanceVersion: CrisperWhisperLicenseAcceptanceVersion,
      LegalAcceptanceVersion: LegalAcceptanceVersion,
      LegalAcceptanceAcceptedAtUtc: LegalAcceptanceAcceptedAtUtc);
  }

  /// <summary>
  /// Ephemeral preview helpers.
  /// </summary>
  public SettingsDraft WithThemePreview(AppThemePreference? previewTheme) =>
    this with { PreviewThemePreference = previewTheme };

  public SettingsDraft WithFontPreview(int? previewFontSize, string? previewTypefaceId = null) =>
    this with
    {
      PreviewChatOutputFontSize = previewFontSize,
      PreviewChatTypefaceId = previewTypefaceId ?? PreviewChatTypefaceId,
    };

  public SettingsDraft ClearPreviews() =>
    this with
    {
      PreviewThemePreference = null,
      PreviewChatOutputFontSize = null,
      PreviewChatTypefaceId = null,
    };

  /// <summary>
  /// Determines if any persisted configuration differs from the given baseline AppSettings.
  /// </summary>
  public bool IsDirty(AppSettings baseline)
  {
    ArgumentNullException.ThrowIfNull(baseline);

    return !Hotkey.Equals(baseline.Hotkey)
      || !UndoHotkey.Equals(baseline.UndoHotkey)
      || !Equals(RetryLastDictationHotkey, baseline.RetryLastDictationHotkey)
      || LastDictationRetryWindowSeconds != baseline.LastDictationRetryWindowSeconds
      || !string.Equals(TranscriptionProviderId, baseline.TranscriptionProviderId, StringComparison.Ordinal)
      || !string.Equals(TranscriptionModelId, baseline.TranscriptionModelId, StringComparison.Ordinal)
      || !string.Equals(TranscriptionLanguage, baseline.TranscriptionLanguage, StringComparison.Ordinal)
      || EnableAutomaticPunctuation != baseline.EnableAutomaticPunctuation
      || !string.Equals(PreferredAudioInputDeviceId, baseline.PreferredAudioInputDeviceId, StringComparison.Ordinal)
      || RecordingMode != baseline.RecordingMode
      || PreferredInsertionMethod != baseline.PreferredInsertionMethod
      || RestoreClipboard != baseline.RestoreClipboard
      || OverlayEnabled != baseline.OverlayEnabled
      || CaretIndicatorEnabled != baseline.CaretIndicatorEnabled
      || FallbackToCornerOverlay != baseline.FallbackToCornerOverlay
      || EnableSecureFieldDetection != baseline.EnableSecureFieldDetection
      || EnableElevatedInsertion != baseline.EnableElevatedInsertion
      || EnableDictationCommands != baseline.EnableDictationCommands
      || ThemePreference != baseline.ThemePreference
      || ChatOutputFontSize != baseline.ChatOutputFontSize
      || !string.Equals(ChatTypefaceId, baseline.ChatTypefaceId, StringComparison.Ordinal)
      || WorkbenchZoomPercent != baseline.WorkbenchZoomPercent
      || ChatPaperViewEnabled != baseline.ChatPaperViewEnabled
      || AssistantFeaturesEnabled != baseline.AssistantFeaturesEnabled
      || !string.Equals(CrisperWhisperLicenseAcceptanceVersion, baseline.CrisperWhisperLicenseAcceptanceVersion, StringComparison.Ordinal)
      || !string.Equals(LegalAcceptanceVersion, baseline.LegalAcceptanceVersion, StringComparison.Ordinal)
      || LegalAcceptanceAcceptedAtUtc != baseline.LegalAcceptanceAcceptedAtUtc
      || HasCompletedFirstRun != baseline.HasCompletedFirstRun
      || !InsertionBlockedProcessNames.SequenceEqual(baseline.InsertionBlockedProcessNames);
  }

  /// <summary>
  /// Checks whether appearance-specific settings differ from the baseline.
  /// </summary>
  public bool HasAppearanceChanges(AppSettings baseline)
  {
    ArgumentNullException.ThrowIfNull(baseline);

    return ThemePreference != baseline.ThemePreference
      || ChatOutputFontSize != baseline.ChatOutputFontSize
      || WorkbenchZoomPercent != baseline.WorkbenchZoomPercent
      || ChatPaperViewEnabled != baseline.ChatPaperViewEnabled
      || !string.Equals(ChatTypefaceId, baseline.ChatTypefaceId, StringComparison.Ordinal);
  }

  /// <summary>
  /// Checks whether behavioral/runtime settings differ from the baseline.
  /// </summary>
  public bool HasBehavioralChanges(AppSettings baseline)
  {
    ArgumentNullException.ThrowIfNull(baseline);

    return !Hotkey.Equals(baseline.Hotkey)
      || !UndoHotkey.Equals(baseline.UndoHotkey)
      || !Equals(RetryLastDictationHotkey, baseline.RetryLastDictationHotkey)
      || !string.Equals(TranscriptionProviderId, baseline.TranscriptionProviderId, StringComparison.Ordinal)
      || !string.Equals(TranscriptionModelId, baseline.TranscriptionModelId, StringComparison.Ordinal)
      || !string.Equals(TranscriptionLanguage, baseline.TranscriptionLanguage, StringComparison.Ordinal)
      || EnableAutomaticPunctuation != baseline.EnableAutomaticPunctuation
      || !string.Equals(PreferredAudioInputDeviceId, baseline.PreferredAudioInputDeviceId, StringComparison.Ordinal)
      || AssistantFeaturesEnabled != baseline.AssistantFeaturesEnabled
      || !string.Equals(CrisperWhisperLicenseAcceptanceVersion, baseline.CrisperWhisperLicenseAcceptanceVersion, StringComparison.Ordinal);
  }

  /// <summary>
  /// Validates the current draft settings against product invariants and conflict rules.
  /// </summary>
  public SettingsValidationResult Validate(IReadOnlyCollection<HotkeyBinding>? reservedHotkeys = null)
  {
    List<SettingsValidationError> errors = new();

    if (IsReadOnly)
    {
      errors.Add(new SettingsValidationError(
        nameof(IsReadOnly),
        ReadOnlyReason ?? "Settings draft is read-only and cannot be modified."));
      return SettingsValidationResult.Failure(errors);
    }

    // Hotkey validation
    if (!IsValidBinding(Hotkey))
    {
      errors.Add(new SettingsValidationError(nameof(Hotkey), "Dictation hotkey must be configured with a modifier and valid key."));
    }

    if (!IsValidBinding(UndoHotkey))
    {
      errors.Add(new SettingsValidationError(nameof(UndoHotkey), "Undo hotkey must be configured with a modifier and valid key."));
    }

    if (IsValidBinding(Hotkey) && IsValidBinding(UndoHotkey) && Hotkey.Equals(UndoHotkey))
    {
      errors.Add(new SettingsValidationError(
        nameof(UndoHotkey),
        "Dictation hotkey and Undo hotkey cannot use the same key combination."));
    }

    if (IsValidBinding(RetryLastDictationHotkey))
    {
      if (RetryLastDictationHotkey!.Equals(Hotkey))
      {
        errors.Add(new SettingsValidationError(
          nameof(RetryLastDictationHotkey),
          "Retry last dictation hotkey conflicts with the Dictation hotkey."));
      }

      if (RetryLastDictationHotkey.Equals(UndoHotkey))
      {
        errors.Add(new SettingsValidationError(
          nameof(RetryLastDictationHotkey),
          "Retry last dictation hotkey conflicts with the Undo hotkey."));
      }
    }

    if (reservedHotkeys is not null)
    {
      if (IsValidBinding(Hotkey) && reservedHotkeys.Contains(Hotkey))
      {
        errors.Add(new SettingsValidationError(nameof(Hotkey), "Dictation hotkey conflicts with a reserved system hotkey."));
      }

      if (IsValidBinding(UndoHotkey) && reservedHotkeys.Contains(UndoHotkey))
      {
        errors.Add(new SettingsValidationError(nameof(UndoHotkey), "Undo hotkey conflicts with a reserved system hotkey."));
      }

      if (IsValidBinding(RetryLastDictationHotkey) && reservedHotkeys.Contains(RetryLastDictationHotkey!))
      {
        errors.Add(new SettingsValidationError(nameof(RetryLastDictationHotkey), "Retry last dictation hotkey conflicts with a reserved system hotkey."));
      }
    }

    // Transcription & Model validation
    if (string.IsNullOrWhiteSpace(TranscriptionProviderId))
    {
      errors.Add(new SettingsValidationError(nameof(TranscriptionProviderId), "Transcription provider must be selected."));
    }

    if (string.IsNullOrWhiteSpace(TranscriptionModelId))
    {
      errors.Add(new SettingsValidationError(nameof(TranscriptionModelId), "Transcription model must be selected."));
    }

    if (string.IsNullOrWhiteSpace(TranscriptionLanguage))
    {
      errors.Add(new SettingsValidationError(nameof(TranscriptionLanguage), "Transcription language must be specified."));
    }

    // Appearance limits
    if (ChatOutputFontSize < AppSettings.MinChatOutputFontSize || ChatOutputFontSize > AppSettings.MaxChatOutputFontSize)
    {
      errors.Add(new SettingsValidationError(
        nameof(ChatOutputFontSize),
        $"Chat font size must be between {AppSettings.MinChatOutputFontSize} and {AppSettings.MaxChatOutputFontSize}."));
    }

    if (string.IsNullOrWhiteSpace(ChatTypefaceId))
    {
      errors.Add(new SettingsValidationError(nameof(ChatTypefaceId), "Chat typeface must be selected."));
    }

    if (WorkbenchZoomPercent is < 80 or > 150)
    {
      errors.Add(new SettingsValidationError(
        nameof(WorkbenchZoomPercent),
        "Workbench zoom must be between 80 and 150 percent."));
    }

    // Retry window limits
    if (LastDictationRetryWindowSeconds < AppSettings.MinRetryWindowSeconds || LastDictationRetryWindowSeconds > AppSettings.MaxRetryWindowSeconds)
    {
      errors.Add(new SettingsValidationError(
        nameof(LastDictationRetryWindowSeconds),
        $"Retry window must be between {AppSettings.MinRetryWindowSeconds} and {AppSettings.MaxRetryWindowSeconds} seconds."));
    }

    return errors.Count == 0 ? SettingsValidationResult.Success : SettingsValidationResult.Failure(errors);
  }

  private static bool IsValidBinding(HotkeyBinding? binding) =>
    binding is not null && binding.Modifiers != HotkeyModifiers.None && binding.VirtualKey is > 0 and <= 0xFE;
}
