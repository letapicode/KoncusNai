using System;
using System.IO;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Settings;

namespace DictateAnywhere.Settings.Tests;

public sealed class JsonSettingsStoreTests
{
  [Xunit.Theory]
  [Xunit.InlineData("[]")]
  [Xunit.InlineData("null")]
  [Xunit.InlineData("{\"schemaVersion\":2147483648}")]
  [Xunit.InlineData("{\"schemaVersion\":\"99\"}")]
  [Xunit.InlineData("{\"schemaVersion\":null}")]
  [Xunit.InlineData("{\"schemaVersion\":1,\"SchemaVersion\":99}")]
  public async Task UnrecognizedSchema_CannotBeMigratedOrOverwritten(string json)
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, json);
    JsonSettingsStore store = new(path);
    await Xunit.Assert.ThrowsAsync<UnrecognizedSettingsSchemaException>(() => store.LoadAsync());
    await Xunit.Assert.ThrowsAsync<UnrecognizedSettingsSchemaException>(() => store.SaveAsync(AppSettings.Default));
    Xunit.Assert.Equal(json, await File.ReadAllTextAsync(path));
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReturnsDefault_WhenFileMissing()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    JsonSettingsStore store = new(path);

    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal(AppSettings.Default, settings);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ThrowsAndPreservesFile_WhenSchemaIsNewer()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    const string futureSettings = """
{
  "schemaVersion": 999,
  "futureSetting": "must survive"
}
""";
    await File.WriteAllTextAsync(path, futureSettings);
    JsonSettingsStore store = new(path);

    UnsupportedSettingsSchemaException exception = await Xunit.Assert.ThrowsAsync<UnsupportedSettingsSchemaException>(
      () => store.LoadAsync());

    Xunit.Assert.Equal(999, exception.FoundVersion);
    Xunit.Assert.Equal(path, exception.SettingsFilePath);
    Xunit.Assert.Equal(futureSettings, await File.ReadAllTextAsync(path));
  }

  [Xunit.Fact]
  public async Task SaveAsync_ThrowsAndPreservesFile_WhenSchemaIsNewer()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    const string futureSettings = """
{
  "schemaVersion": 999,
  "futureSetting": "must survive"
}
""";
    await File.WriteAllTextAsync(path, futureSettings);
    JsonSettingsStore store = new(path);

    _ = await Xunit.Assert.ThrowsAsync<UnsupportedSettingsSchemaException>(
      () => store.SaveAsync(AppSettings.Default));

    Xunit.Assert.Equal(futureSettings, await File.ReadAllTextAsync(path));
  }

  [Xunit.Theory]
  [Xunit.InlineData(27, 27)]
  [Xunit.InlineData(99, 30)]
  public async Task LoadAsync_PreservesContinuousChatSizeWithinSupportedRange(int storedSize, int expectedSize)
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, $$"""
{
  "schemaVersion": 16,
  "chatOutputFontSize": {{storedSize}}
}
""");

    JsonSettingsStore store = new(path);

    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal(expectedSize, settings.ChatOutputFontSize);
  }

  [Xunit.Fact]
  public async Task SaveAsync_ThenLoadAsync_PreservesSupportedSettings_AndAppliesCurrentProductPolicy()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    JsonSettingsStore store = new(path);

    AppSettings expected = new(
      Hotkey: new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41),
      RecordingMode: RecordingMode.ToggleToTalk,
      TranscriptionProviderId: TranscriptionProviderIds.CrisperWhisperLocal,
      TranscriptionModelId: "crisperwhisper-2-turbo",
      TranscriptionLanguage: "en-gb",
      PreferredInsertionMethod: InsertionMethod.SendInputUnicodeTyping,
      RestoreClipboard: false,
      OverlayEnabled: false,
      CaretIndicatorEnabled: false,
      FallbackToCornerOverlay: false,
      PreferredAudioInputDeviceId: "mic-1",
      HasCompletedFirstRun: true,
      UndoHotkey: new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x55),
      EnableSecureFieldDetection: false,
      InsertionBlockedProcessNames: new[] { "code", "chrome" },
      EnableElevatedInsertion: true,
      EnableDictationCommands: true,
      RetryLastDictationHotkey: new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x59),
      LastDictationRetryWindowSeconds: 120,
      ThemePreference: AppThemePreference.Dark,
      ChatOutputFontSize: 18,
      ChatTypefaceId: ChatTypefaceIds.Excalifont,
      EnableAutomaticPunctuation: false,
      WorkbenchZoomPercent: 125,
      AssistantFeaturesEnabled: false,
      CrisperWhisperLicenseAcceptanceVersion: CrisperWhisperLicensePolicy.AcceptanceVersion,
      LegalAcceptanceVersion: AppLegalAcceptancePolicy.AcceptanceVersion,
      LegalAcceptanceAcceptedAtUtc: new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.Zero));

    await store.SaveAsync(expected);
    AppSettings actual = await store.LoadAsync();

    Xunit.Assert.Equal(expected.Hotkey, actual.Hotkey);
    Xunit.Assert.Equal(RecordingMode.ToggleToTalk, actual.RecordingMode);
    Xunit.Assert.Equal(expected.TranscriptionProviderId, actual.TranscriptionProviderId);
    Xunit.Assert.Equal(expected.TranscriptionModelId, actual.TranscriptionModelId);
    Xunit.Assert.Equal(expected.TranscriptionLanguage, actual.TranscriptionLanguage);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, actual.PreferredInsertionMethod);
    Xunit.Assert.True(actual.RestoreClipboard);
    Xunit.Assert.True(actual.OverlayEnabled);
    Xunit.Assert.True(actual.CaretIndicatorEnabled);
    Xunit.Assert.True(actual.FallbackToCornerOverlay);
    Xunit.Assert.Equal(expected.PreferredAudioInputDeviceId, actual.PreferredAudioInputDeviceId);
    Xunit.Assert.Equal(expected.HasCompletedFirstRun, actual.HasCompletedFirstRun);
    Xunit.Assert.Equal(expected.UndoHotkey, actual.UndoHotkey);
    Xunit.Assert.Equal(expected.EnableSecureFieldDetection, actual.EnableSecureFieldDetection);
    Xunit.Assert.Empty(actual.InsertionBlockedProcessNames);
    Xunit.Assert.Equal(expected.EnableElevatedInsertion, actual.EnableElevatedInsertion);
    Xunit.Assert.Equal(expected.EnableDictationCommands, actual.EnableDictationCommands);
    Xunit.Assert.Equal(expected.RetryLastDictationHotkey, actual.RetryLastDictationHotkey);
    Xunit.Assert.Equal(expected.LastDictationRetryWindowSeconds, actual.LastDictationRetryWindowSeconds);
    Xunit.Assert.Equal(expected.ThemePreference, actual.ThemePreference);
    Xunit.Assert.Equal(expected.ChatOutputFontSize, actual.ChatOutputFontSize);
    Xunit.Assert.Equal(expected.ChatTypefaceId, actual.ChatTypefaceId);
    Xunit.Assert.Equal(expected.EnableAutomaticPunctuation, actual.EnableAutomaticPunctuation);
    Xunit.Assert.Equal(expected.WorkbenchZoomPercent, actual.WorkbenchZoomPercent);
    Xunit.Assert.Equal(expected.AssistantFeaturesEnabled, actual.AssistantFeaturesEnabled);
    Xunit.Assert.Equal(expected.CrisperWhisperLicenseAcceptanceVersion, actual.CrisperWhisperLicenseAcceptanceVersion);
    Xunit.Assert.Equal(expected.LegalAcceptanceVersion, actual.LegalAcceptanceVersion);
    Xunit.Assert.Equal(expected.LegalAcceptanceAcceptedAtUtc, actual.LegalAcceptanceAcceptedAtUtc);

    string json = await File.ReadAllTextAsync(path);
    Xunit.Assert.Contains("\"schemaVersion\": 20", json, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("activeProfileId", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("experienceMode", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("profiles", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("appProfileRules", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("voiceSnippets", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("activeModelId", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("recordingMode", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("preferredInsertionMethod", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("restoreClipboard", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("overlayEnabled", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("caretIndicatorEnabled", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("fallbackToCornerOverlay", json, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("insertionBlockedProcessNames", json, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReadsLegacySchemaWithoutVersion()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "modifiers": 6,
  "virtualKey": 32
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal((HotkeyModifiers.Control | HotkeyModifiers.Shift), settings.Hotkey.Modifiers);
    Xunit.Assert.Equal(0x20, settings.Hotkey.VirtualKey);
    Xunit.Assert.True(settings.HasCompletedFirstRun);
  }

  [Xunit.Fact]
  public async Task LoadAsync_TreatsNegativeSchemaVersionAsBestEffortPreVersionInput()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": -1,
  "modifiers": 6,
  "virtualKey": 32
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();
    string rewrittenJson = await File.ReadAllTextAsync(path);

    Xunit.Assert.Equal(
      new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x20),
      settings.Hotkey);
    Xunit.Assert.True(settings.HasCompletedFirstRun);
    Xunit.Assert.Contains("\"schemaVersion\": 20", rewrittenJson, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task LoadAsync_MigratesSchemaV1AndMarksOnboardingComplete()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 1,
  "hotkeyModifiers": 9,
  "hotkeyVirtualKey": 68,
  "recordingMode": 1,
  "activeModelId": "small.en",
  "preferredInsertionMethod": 1,
  "restoreClipboard": false,
  "overlayEnabled": false,
  "preferredAudioInputDeviceId": "usb-mic"
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal((HotkeyModifiers.Alt | HotkeyModifiers.Windows), settings.Hotkey.Modifiers);
    Xunit.Assert.Equal(0x44, settings.Hotkey.VirtualKey);
    Xunit.Assert.Equal(RecordingMode.ToggleToTalk, settings.RecordingMode);
    Xunit.Assert.Equal(AppSettings.Default.TranscriptionProviderId, settings.TranscriptionProviderId);
    Xunit.Assert.Equal(AppSettings.Default.TranscriptionModelId, settings.TranscriptionModelId);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, settings.PreferredInsertionMethod);
    Xunit.Assert.True(settings.RestoreClipboard);
    Xunit.Assert.True(settings.OverlayEnabled);
    Xunit.Assert.Equal("usb-mic", settings.PreferredAudioInputDeviceId);
    Xunit.Assert.True(settings.HasCompletedFirstRun);
    Xunit.Assert.Equal(AppSettings.Default.UndoHotkey, settings.UndoHotkey);
    Xunit.Assert.Equal(AppSettings.Default.EnableSecureFieldDetection, settings.EnableSecureFieldDetection);
    Xunit.Assert.Equal(AppSettings.Default.InsertionBlockedProcessNames, settings.InsertionBlockedProcessNames);
    Xunit.Assert.Equal(AppSettings.Default.EnableElevatedInsertion, settings.EnableElevatedInsertion);
    Xunit.Assert.Equal(AppSettings.Default.EnableDictationCommands, settings.EnableDictationCommands);
    Xunit.Assert.Equal(AppSettings.Default.TranscriptionLanguage, settings.TranscriptionLanguage);
    Xunit.Assert.Equal(AppSettings.Default.TranscriptionLanguage, settings.TranscriptionLanguage);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReadsSchemaV3_SecureFieldAndBlacklistSettings()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 3,
  "hotkeyModifiers": 8,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 6,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 0,
  "activeModelId": "base.en",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": true,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": false,
  "insertionBlockedProcessNames": [ "Code.exe", "chrome", "code", "  ", null ]
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal((HotkeyModifiers.Control | HotkeyModifiers.Shift), settings.UndoHotkey.Modifiers);
    Xunit.Assert.Equal(0x5A, settings.UndoHotkey.VirtualKey);
    Xunit.Assert.False(settings.EnableSecureFieldDetection);
    Xunit.Assert.Empty(settings.InsertionBlockedProcessNames);
    Xunit.Assert.False(settings.EnableElevatedInsertion);
    Xunit.Assert.False(settings.EnableDictationCommands);
    Xunit.Assert.Equal(AppSettings.Default.TranscriptionLanguage, settings.TranscriptionLanguage);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReadsSchemaV4_ElevatedInsertionToggle()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 4,
  "hotkeyModifiers": 9,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 9,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 0,
  "activeModelId": "base.en",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": true,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": true,
  "insertionBlockedProcessNames": [ "code" ],
  "enableElevatedInsertion": true
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.True(settings.EnableElevatedInsertion);
    Xunit.Assert.False(settings.EnableDictationCommands);
    Xunit.Assert.Equal(AppSettings.Default.TranscriptionLanguage, settings.TranscriptionLanguage);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReadsSchemaV5_DictationCommandsAndIgnoresRemovedCleanupToggles()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 5,
  "hotkeyModifiers": 9,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 9,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 0,
  "activeModelId": "base.en",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": true,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": true,
  "insertionBlockedProcessNames": [ "code" ],
  "enableElevatedInsertion": false,
  "enableFillerRemoval": true,
  "enableSentenceFormattingCleanup": true,
  "enableDictationCommands": true
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.True(settings.EnableDictationCommands);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReadsSchemaV6_ThenRetiresProfilesAndSnippets()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 6,
  "hotkeyModifiers": 9,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 9,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 0,
  "activeModelId": "base.en",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": true,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": true,
  "insertionBlockedProcessNames": [ "code" ],
  "enableElevatedInsertion": false,
  "enableFillerRemoval": true,
  "enableSentenceFormattingCleanup": true,
  "enableDictationCommands": true,
  "activeProfileId": "email",
  "profiles": [
    {
      "profileId": "default",
      "displayName": "Default",
      "preferredInsertionMethod": 0,
      "restoreClipboard": true,
      "enableFillerRemoval": false,
      "enableSentenceFormattingCleanup": false,
      "enableDictationCommands": false,
      "voiceSnippets": []
    },
    {
      "profileId": "email",
      "displayName": "Email",
      "preferredInsertionMethod": 0,
      "restoreClipboard": true,
      "enableFillerRemoval": true,
      "enableSentenceFormattingCleanup": true,
      "enableDictationCommands": true,
      "voiceSnippets": [
        { "triggerPhrase": "greeting line", "templateText": "Hi team," }
      ]
    }
  ]
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal(AppSettings.Default.TranscriptionLanguage, settings.TranscriptionLanguage);
    Xunit.Assert.True(settings.EnableDictationCommands);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReadsSchemaV7_AndIgnoresRetiredExperienceMode()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 7,
  "hotkeyModifiers": 1,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 9,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 1,
  "activeModelId": "base.en",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": false,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": true,
  "insertionBlockedProcessNames": [],
  "enableElevatedInsertion": false,
  "enableFillerRemoval": false,
  "enableSentenceFormattingCleanup": false,
  "enableDictationCommands": false,
  "activeProfileId": "default",
  "experienceMode": 1,
  "profiles": [
    {
      "profileId": "default",
      "displayName": "Default",
      "preferredInsertionMethod": 0,
      "restoreClipboard": true,
      "enableFillerRemoval": false,
      "enableSentenceFormattingCleanup": false,
      "enableDictationCommands": false,
      "voiceSnippets": []
    }
  ]
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal(new HotkeyBinding(HotkeyModifiers.Alt, 0x20), settings.Hotkey);
    Xunit.Assert.Equal(RecordingMode.ToggleToTalk, settings.RecordingMode);
    Xunit.Assert.Equal(AppSettings.Default.TranscriptionLanguage, settings.TranscriptionLanguage);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReadsSchemaV8_TranscriptionLanguageSettings()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 8,
  "hotkeyModifiers": 1,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 9,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 1,
  "activeModelId": "base.en",
  "transcriptionLanguage": "EN_us",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": false,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": true,
  "insertionBlockedProcessNames": [],
  "enableElevatedInsertion": false,
  "enableFillerRemoval": false,
  "enableSentenceFormattingCleanup": false,
  "enableDictationCommands": false,
  "activeProfileId": "default",
  "experienceMode": 1,
  "profiles": [
    {
      "profileId": "default",
      "displayName": "Default",
      "preferredInsertionMethod": 0,
      "restoreClipboard": true,
      "enableFillerRemoval": false,
      "enableSentenceFormattingCleanup": false,
      "enableDictationCommands": false,
      "transcriptionLanguageOverride": "FR_ca",
      "voiceSnippets": []
    }
  ]
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal("en-us", settings.TranscriptionLanguage);
  }

  [Xunit.Fact]
  public async Task LoadAsync_RewritesOlderSchemaFiles_ToCurrentSchemaVersion()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 8,
  "hotkeyModifiers": 1,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 9,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 1,
  "activeModelId": "base.en",
  "transcriptionLanguage": "en-us",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": false,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": true,
  "insertionBlockedProcessNames": [],
  "enableElevatedInsertion": false,
  "enableFillerRemoval": false,
  "enableSentenceFormattingCleanup": false,
  "enableDictationCommands": false,
  "activeProfileId": "default",
  "experienceMode": 0,
  "profiles": [
    {
      "profileId": "default",
      "displayName": "Default",
      "preferredInsertionMethod": 0,
      "restoreClipboard": true,
      "enableFillerRemoval": false,
      "enableSentenceFormattingCleanup": false,
      "enableDictationCommands": false,
      "voiceSnippets": []
    }
  ]
}
""");

    JsonSettingsStore store = new(path);

    AppSettings settings = await store.LoadAsync();
    string rewrittenJson = await File.ReadAllTextAsync(path);

    Xunit.Assert.Equal("en-us", settings.TranscriptionLanguage);
    Xunit.Assert.Contains("\"schemaVersion\": 20", rewrittenJson, StringComparison.Ordinal);
    Xunit.Assert.Contains("\"enableAutomaticPunctuation\": true", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("\"chatOutputFontSize\": 15", rewrittenJson, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("caretIndicatorEnabled", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("fallbackToCornerOverlay", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("\"transcriptionProviderId\": \"cohere-local\"", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("\"transcriptionModelId\": \"cohere-transcribe-03-2026\"", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("rewriteProviderId", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("enableFillerRemoval", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("enableSentenceFormattingCleanup", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("activeProfileId", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("experienceMode", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("profiles", rewrittenJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("appProfileRules", rewrittenJson, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReadsSchemaV9_OverlayAnchorSettings()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 9,
  "hotkeyModifiers": 1,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 9,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 1,
  "activeModelId": "base.en",
  "transcriptionLanguage": "en",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": true,
  "caretIndicatorEnabled": false,
  "fallbackToCornerOverlay": false,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": true,
  "insertionBlockedProcessNames": [],
  "enableElevatedInsertion": false,
  "enableFillerRemoval": false,
  "enableSentenceFormattingCleanup": false,
  "enableDictationCommands": false,
  "activeProfileId": "default",
  "experienceMode": 0,
  "profiles": [
    {
      "profileId": "default",
      "displayName": "Default",
      "preferredInsertionMethod": 0,
      "restoreClipboard": true,
      "enableFillerRemoval": false,
      "enableSentenceFormattingCleanup": false,
      "enableDictationCommands": false,
      "voiceSnippets": []
    }
  ]
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.True(settings.OverlayEnabled);
    Xunit.Assert.True(settings.CaretIndicatorEnabled);
    Xunit.Assert.True(settings.FallbackToCornerOverlay);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReadsSchemaV10_ProviderNeutralTranscriptionSelection()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 10,
  "hotkeyModifiers": 1,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 9,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 1,
  "activeModelId": "base.en",
  "transcriptionProviderId": "cohere-local",
  "transcriptionModelId": "cohere-transcribe-03-2026",
  "transcriptionLanguage": "en",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": true,
  "caretIndicatorEnabled": true,
  "fallbackToCornerOverlay": true,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": true,
  "insertionBlockedProcessNames": [],
  "enableElevatedInsertion": false,
  "enableFillerRemoval": false,
  "enableSentenceFormattingCleanup": false,
  "enableDictationCommands": false,
  "activeProfileId": "default",
  "experienceMode": 0,
  "profiles": [
    {
      "profileId": "default",
      "displayName": "Default",
      "preferredInsertionMethod": 0,
      "restoreClipboard": true,
      "enableFillerRemoval": false,
      "enableSentenceFormattingCleanup": false,
      "enableDictationCommands": false,
      "voiceSnippets": []
    }
  ]
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal("cohere-local", settings.TranscriptionProviderId);
    Xunit.Assert.Equal("cohere-transcribe-03-2026", settings.TranscriptionModelId);
    Xunit.Assert.Equal("cohere-transcribe-03-2026", settings.GetConfiguredTranscriptionModelId());
    Xunit.Assert.False(settings.EnableDictationCommands);
  }

  [Xunit.Fact]
  public async Task LoadAsync_MigratesSchemaV11_WithoutPersistingRemovedRewriteSettings()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 11,
  "hotkeyModifiers": 1,
  "hotkeyVirtualKey": 32,
  "undoHotkeyModifiers": 9,
  "undoHotkeyVirtualKey": 90,
  "recordingMode": 1,
  "activeModelId": "base.en",
  "transcriptionProviderId": "whisper-local",
  "transcriptionModelId": "base.en",
  "transcriptionLanguage": "en",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": true,
  "caretIndicatorEnabled": true,
  "fallbackToCornerOverlay": true,
  "preferredAudioInputDeviceId": null,
  "hasCompletedFirstRun": true,
  "enableSecureFieldDetection": true,
  "insertionBlockedProcessNames": [],
  "enableElevatedInsertion": false,
  "enableFillerRemoval": false,
  "enableSentenceFormattingCleanup": false,
  "enableDictationCommands": false,
  "activeProfileId": "default",
  "experienceMode": 0,
  "rewriteProviderId": "gemma-local",
  "rewriteModelId": "gemma-4-E4B",
  "rewriteMode": 2,
  "profiles": [
    {
      "profileId": "default",
      "displayName": "Default",
      "preferredInsertionMethod": 0,
      "restoreClipboard": true,
      "enableFillerRemoval": false,
      "enableSentenceFormattingCleanup": false,
      "enableDictationCommands": false,
      "rewriteProviderIdOverride": "rule-based",
      "rewriteModelIdOverride": "",
      "rewriteModeOverride": 1,
      "voiceSnippets": []
    }
  ]
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    string migratedJson = await File.ReadAllTextAsync(path);
    Xunit.Assert.Contains("\"schemaVersion\": 20", migratedJson, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("rewriteProviderId", migratedJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("rewriteModelId", migratedJson, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("rewriteMode", migratedJson, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task LoadAsync_MigratesLegacySchemaV1_ToEnglishBaseline_WhenLanguageMissing()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, """
{
  "schemaVersion": 1,
  "hotkeyModifiers": 9,
  "hotkeyVirtualKey": 32,
  "recordingMode": 0,
  "activeModelId": "base.en",
  "preferredInsertionMethod": 0,
  "restoreClipboard": true,
  "overlayEnabled": true,
  "preferredAudioInputDeviceId": null
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal("en", settings.TranscriptionLanguage);
  }

  [Xunit.Fact]
  public async Task LoadAsync_ReturnsDefault_WhenJsonCorrupt()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, "{invalid-json");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal(AppSettings.Default, settings);
  }

  [Xunit.Fact]
  public async Task LoadAsync_MigratesSchema19_PreservingCrisperAcceptanceAndRequiringProjectAcknowledgement()
  {
    using TemporaryDirectoryScope scope = new();
    string path = Path.Combine(scope.DirectoryPath, "settings.json");
    await File.WriteAllTextAsync(path, $$"""
{
  "schemaVersion": 19,
  "hasCompletedFirstRun": true,
  "crisperWhisperLicenseAcceptanceVersion": "{{CrisperWhisperLicensePolicy.AcceptanceVersion}}"
}
""");

    JsonSettingsStore store = new(path);
    AppSettings settings = await store.LoadAsync();

    Xunit.Assert.Equal(CrisperWhisperLicensePolicy.AcceptanceVersion, settings.CrisperWhisperLicenseAcceptanceVersion);
    Xunit.Assert.Null(settings.LegalAcceptanceVersion);
    Xunit.Assert.Null(settings.LegalAcceptanceAcceptedAtUtc);
    Xunit.Assert.Contains("\"schemaVersion\": 20", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
  }

  private sealed class TemporaryDirectoryScope : IDisposable
  {
    public TemporaryDirectoryScope()
    {
      DirectoryPath = Path.Combine(
        Path.GetTempPath(),
        "DictateAnywhere.Tests.Settings",
        Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
      try
      {
        if (Directory.Exists(DirectoryPath))
        {
          Directory.Delete(DirectoryPath, recursive: true);
        }
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }
  }
}


