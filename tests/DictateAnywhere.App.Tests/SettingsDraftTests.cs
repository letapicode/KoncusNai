using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DictateAnywhere.App.Settings;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class SettingsDraftTests
{
  [Fact]
  public void FromSettings_DefaultSettings_RoundTripsAccurately()
  {
    AppSettings original = AppSettings.Default;

    SettingsDraft draft = SettingsDraft.FromSettings(original);
    AppSettings converted = draft.ToSettings();

    Assert.Equal(original, converted);
    Assert.False(draft.IsDirty(original));
    Assert.False(draft.HasAppearanceChanges(original));
    Assert.False(draft.HasBehavioralChanges(original));
    Assert.False(draft.IsReadOnly);
    Assert.Null(draft.ReadOnlyReason);
  }

  [Fact]
  public void FromSettings_CustomSettings_RoundTripsAllPropertiesAccurately()
  {
    AppSettings custom = new(
      Hotkey: new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x44), // Ctrl+Shift+D
      RecordingMode: RecordingMode.ToggleToTalk,
      TranscriptionProviderId: "custom-provider",
      TranscriptionModelId: "custom-model",
      TranscriptionLanguage: "es",
      PreferredInsertionMethod: InsertionMethod.ClipboardPaste,
      RestoreClipboard: true,
      OverlayEnabled: true,
      CaretIndicatorEnabled: true,
      FallbackToCornerOverlay: true,
      PreferredAudioInputDeviceId: "microphone-device-123",
      HasCompletedFirstRun: true,
      UndoHotkey: new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x5A), // Ctrl+Shift+Z
      EnableSecureFieldDetection: false,
      InsertionBlockedProcessNames: Array.Empty<string>(),
      EnableElevatedInsertion: true,
      EnableDictationCommands: true,
      RetryLastDictationHotkey: new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x52), // Ctrl+Shift+R
      LastDictationRetryWindowSeconds: 1200,
      ThemePreference: AppThemePreference.Light,
      ChatOutputFontSize: 20,
      ChatTypefaceId: ChatTypefaceIds.Kalam,
      EnableAutomaticPunctuation: false);

    SettingsDraft draft = SettingsDraft.FromSettings(custom);
    AppSettings converted = draft.ToSettings();

    Assert.Equal(custom, converted);
    Assert.False(draft.IsDirty(custom));
  }

  [Fact]
  public void EphemeralAppearancePreview_DistinguishesPreviewFromPersistedSettings()
  {
    AppSettings baseline = AppSettings.Default; // Dark theme, font size 15
    SettingsDraft draft = SettingsDraft.FromSettings(baseline);

    Assert.Equal(AppThemePreference.Dark, draft.EffectiveThemePreference);
    Assert.Equal(15, draft.EffectiveChatOutputFontSize);

    // Apply preview
    SettingsDraft previewDraft = draft
      .WithThemePreview(AppThemePreference.Light)
      .WithFontPreview(24, ChatTypefaceIds.BookSerif);

    // Effective values reflect preview
    Assert.Equal(AppThemePreference.Light, previewDraft.EffectiveThemePreference);
    Assert.Equal(24, previewDraft.EffectiveChatOutputFontSize);
    Assert.Equal(ChatTypefaceIds.BookSerif, previewDraft.EffectiveChatTypefaceId);

    // Underlying persisted values remain unchanged
    Assert.Equal(AppThemePreference.Dark, previewDraft.ThemePreference);
    Assert.Equal(15, previewDraft.ChatOutputFontSize);
    Assert.Equal(ChatTypefaceIds.System, previewDraft.ChatTypefaceId);

    // ToSettings ignores preview and persists the real settings
    AppSettings converted = previewDraft.ToSettings();
    Assert.Equal(AppThemePreference.Dark, converted.ThemePreference);
    Assert.Equal(15, converted.ChatOutputFontSize);
    Assert.Equal(ChatTypefaceIds.System, converted.ChatTypefaceId);

    // Clearing previews returns effective to persisted
    SettingsDraft clearedDraft = previewDraft.ClearPreviews();
    Assert.Equal(AppThemePreference.Dark, clearedDraft.EffectiveThemePreference);
    Assert.Equal(15, clearedDraft.EffectiveChatOutputFontSize);
  }

  [Fact]
  public void IsDirty_DetectsPropertyChangesAndCategorizesThem()
  {
    AppSettings baseline = AppSettings.Default;
    SettingsDraft draft = SettingsDraft.FromSettings(baseline);

    Assert.False(draft.IsDirty(baseline));

    // Appearance change
    SettingsDraft appearanceDraft = draft with { ThemePreference = AppThemePreference.Light };
    Assert.True(appearanceDraft.IsDirty(baseline));
    Assert.True(appearanceDraft.HasAppearanceChanges(baseline));
    Assert.False(appearanceDraft.HasBehavioralChanges(baseline));

    // Behavioral change
    SettingsDraft behavioralDraft = draft with { PreferredAudioInputDeviceId = "new-mic" };
    Assert.True(behavioralDraft.IsDirty(baseline));
    Assert.False(behavioralDraft.HasAppearanceChanges(baseline));
    Assert.True(behavioralDraft.HasBehavioralChanges(baseline));
  }

  [Fact]
  public void Validate_ValidDraft_ReturnsSuccess()
  {
    SettingsDraft draft = SettingsDraft.FromSettings(AppSettings.Default);
    SettingsValidationResult result = draft.Validate();

    Assert.True(result.IsValid);
    Assert.Empty(result.Errors);
  }

  [Fact]
  public void Validate_ConflictingHotkeys_ProducesValidationErrors()
  {
    HotkeyBinding conflictBinding = new(HotkeyModifiers.Alt, 0x20);
    SettingsDraft draft = SettingsDraft.FromSettings(AppSettings.Default) with
    {
      Hotkey = conflictBinding,
      UndoHotkey = conflictBinding,
    };

    SettingsValidationResult result = draft.Validate();

    Assert.False(result.IsValid);
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.UndoHotkey)));
  }

  [Fact]
  public void Validate_InvalidOrEmptyHotkeys_ProducesValidationErrors()
  {
    SettingsDraft draft = SettingsDraft.FromSettings(AppSettings.Default) with
    {
      Hotkey = new HotkeyBinding(HotkeyModifiers.None, 0),
      UndoHotkey = new HotkeyBinding(HotkeyModifiers.None, 0),
    };

    SettingsValidationResult result = draft.Validate();

    Assert.False(result.IsValid);
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.Hotkey)));
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.UndoHotkey)));
  }

  [Fact]
  public void Validate_RetryHotkeyConflict_ProducesValidationErrors()
  {
    HotkeyBinding dictationBinding = new(HotkeyModifiers.Alt, 0x20);
    SettingsDraft draft = SettingsDraft.FromSettings(AppSettings.Default) with
    {
      Hotkey = dictationBinding,
      RetryLastDictationHotkey = dictationBinding,
    };

    SettingsValidationResult result = draft.Validate();

    Assert.False(result.IsValid);
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.RetryLastDictationHotkey)));
  }

  [Fact]
  public void Validate_ReservedSystemHotkey_ProducesValidationErrors()
  {
    HotkeyBinding reserved = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x44);
    SettingsDraft draft = SettingsDraft.FromSettings(AppSettings.Default) with
    {
      Hotkey = reserved,
    };

    SettingsValidationResult result = draft.Validate(new[] { reserved });

    Assert.False(result.IsValid);
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.Hotkey)));
  }

  [Fact]
  public void Validate_MissingRequiredModelAndLanguage_ProducesErrors()
  {
    SettingsDraft draft = SettingsDraft.FromSettings(AppSettings.Default) with
    {
      TranscriptionProviderId = "  ",
      TranscriptionModelId = "",
      TranscriptionLanguage = "",
    };

    SettingsValidationResult result = draft.Validate();

    Assert.False(result.IsValid);
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.TranscriptionProviderId)));
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.TranscriptionModelId)));
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.TranscriptionLanguage)));
  }

  [Theory]
  [InlineData(5)]
  [InlineData(50)]
  public void Validate_InvalidFontSize_ProducesErrors(int invalidFontSize)
  {
    SettingsDraft draft = SettingsDraft.FromSettings(AppSettings.Default) with
    {
      ChatOutputFontSize = invalidFontSize,
    };

    SettingsValidationResult result = draft.Validate();

    Assert.False(result.IsValid);
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.ChatOutputFontSize)));
  }

  [Theory]
  [InlineData(2)]
  [InlineData(100_000)]
  public void Validate_InvalidRetryWindow_ProducesErrors(int invalidRetryWindow)
  {
    SettingsDraft draft = SettingsDraft.FromSettings(AppSettings.Default) with
    {
      LastDictationRetryWindowSeconds = invalidRetryWindow,
    };

    SettingsValidationResult result = draft.Validate();

    Assert.False(result.IsValid);
    Assert.True(result.HasErrorFor(nameof(SettingsDraft.LastDictationRetryWindowSeconds)));
  }

  [Fact]
  public void CreateReadOnly_ProtectsUnsupportedFutureSchemaAndPreventsSaving()
  {
    string reason = "Unsupported schema version 99 detected. Settings cannot be overwritten.";
    SettingsDraft draft = SettingsDraft.CreateReadOnly(reason);

    Assert.True(draft.IsReadOnly);
    Assert.Equal(reason, draft.ReadOnlyReason);

    SettingsValidationResult validation = draft.Validate();
    Assert.False(validation.IsValid);
    Assert.True(validation.HasErrorFor(nameof(SettingsDraft.IsReadOnly)));

    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => draft.ToSettings());
    Assert.Contains(reason, ex.Message);
  }

  [Fact]
  public void HistoryConfiguration_IsCompletelyAbsentFromSettingsDraft()
  {
    PropertyInfo[] properties = typeof(SettingsDraft).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    foreach (PropertyInfo prop in properties)
    {
      Assert.DoesNotContain("history", prop.Name, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("retention", prop.Name, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("encryption", prop.Name, StringComparison.OrdinalIgnoreCase);
    }
  }
}
