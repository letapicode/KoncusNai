using System;
using System.Text.Json;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Settings.Migrations;

/// <summary>Maps supported historical documents into the current runtime contract.</summary>
internal static class LegacySettingsReader
{
  internal const int OldestSupportedSchemaVersion = 0;

  internal static AppSettings Read(JsonElement root, int schemaVersion)
  {
    if (schemaVersion is < OldestSupportedSchemaVersion or >= SettingsSchema.CurrentVersion)
    {
      throw new ArgumentOutOfRangeException(nameof(schemaVersion));
    }

    AppSettings defaults = AppSettings.Default;
    if (schemaVersion == 0)
    {
      return defaults with
      {
        Hotkey = new HotkeyBinding(
          (HotkeyModifiers)ReadInt(root, "modifiers", (int)defaults.Hotkey.Modifiers),
          ReadInt(root, "virtualKey", defaults.Hotkey.VirtualKey)),
        HasCompletedFirstRun = true,
      };
    }

    string activeModelId = ReadString(root, "activeModelId", defaults.TranscriptionModelId);
    string providerId = schemaVersion >= 10
      ? ReadString(root, "transcriptionProviderId", defaults.TranscriptionProviderId)
      : defaults.TranscriptionProviderId;
    string modelId = schemaVersion >= 10
      ? ReadString(root, "transcriptionModelId", activeModelId)
      : activeModelId;

    AppSettings migrated = defaults with
    {
      Hotkey = new HotkeyBinding(
        (HotkeyModifiers)ReadInt(root, "hotkeyModifiers", (int)defaults.Hotkey.Modifiers),
        ReadInt(root, "hotkeyVirtualKey", defaults.Hotkey.VirtualKey)),
      UndoHotkey = schemaVersion >= 3
        ? new HotkeyBinding(
          (HotkeyModifiers)ReadInt(root, "undoHotkeyModifiers", (int)defaults.UndoHotkey.Modifiers),
          ReadInt(root, "undoHotkeyVirtualKey", defaults.UndoHotkey.VirtualKey))
        : defaults.UndoHotkey,
      TranscriptionProviderId = providerId,
      TranscriptionModelId = modelId,
      TranscriptionLanguage = schemaVersion >= 8
        ? TranscriptionLanguageSettings.NormalizeGlobal(
          ReadString(root, "transcriptionLanguage", defaults.TranscriptionLanguage))
        : defaults.TranscriptionLanguage,
      PreferredAudioInputDeviceId = ReadNullableString(
        root,
        "preferredAudioInputDeviceId",
        defaults.PreferredAudioInputDeviceId),
      HasCompletedFirstRun = schemaVersion == 1
        || ReadBool(root, "hasCompletedFirstRun", defaults.HasCompletedFirstRun),
      EnableSecureFieldDetection = schemaVersion >= 3
        ? ReadBool(root, "enableSecureFieldDetection", defaults.EnableSecureFieldDetection)
        : defaults.EnableSecureFieldDetection,
      EnableElevatedInsertion = schemaVersion >= 4
        ? ReadBool(root, "enableElevatedInsertion", defaults.EnableElevatedInsertion)
        : defaults.EnableElevatedInsertion,
      EnableDictationCommands = schemaVersion >= 5
        ? ReadBool(root, "enableDictationCommands", defaults.EnableDictationCommands)
        : defaults.EnableDictationCommands,
    };

    if (schemaVersion >= 13)
    {
      migrated = migrated with
      {
        RetryLastDictationHotkey = new HotkeyBinding(
          (HotkeyModifiers)ReadInt(
            root,
            "retryLastDictationHotkeyModifiers",
            (int)defaults.RetryLastDictationHotkey!.Modifiers),
          ReadInt(
            root,
            "retryLastDictationHotkeyVirtualKey",
            defaults.RetryLastDictationHotkey!.VirtualKey)),
        LastDictationRetryWindowSeconds = Math.Clamp(
          ReadInt(root, "lastDictationRetryWindowSeconds", defaults.LastDictationRetryWindowSeconds),
          60,
          86_400),
      };
    }

    if (schemaVersion >= 14)
    {
      int theme = ReadInt(root, "themePreference", (int)defaults.ThemePreference);
      migrated = migrated with
      {
        ThemePreference = Enum.IsDefined(typeof(AppThemePreference), theme)
          ? (AppThemePreference)theme
          : defaults.ThemePreference,
      };
    }

    if (schemaVersion >= 15)
    {
      migrated = migrated with
      {
        ChatOutputFontSize = Math.Clamp(
          ReadInt(root, "chatOutputFontSize", defaults.ChatOutputFontSize),
          12,
          30),
      };
    }

    if (schemaVersion >= 16)
    {
      migrated = migrated with
      {
        ChatTypefaceId = ChatTypefaceSettings.Normalize(
          ReadString(root, "chatTypefaceId", defaults.ChatTypefaceId)),
      };
    }

    if (schemaVersion >= 17)
    {
      migrated = migrated with
      {
        EnableAutomaticPunctuation = ReadBool(
          root,
          "enableAutomaticPunctuation",
          defaults.EnableAutomaticPunctuation),
      };
    }

    if (schemaVersion >= 18)
    {
      migrated = migrated with
      {
        WorkbenchZoomPercent = Math.Clamp(
          ReadInt(root, "workbenchZoomPercent", defaults.WorkbenchZoomPercent),
          80,
          150),
        AssistantFeaturesEnabled = ReadBool(
          root,
          "assistantFeaturesEnabled",
          defaults.AssistantFeaturesEnabled),
      };
    }

    if (schemaVersion >= 19)
    {
      migrated = migrated with
      {
        CrisperWhisperLicenseAcceptanceVersion = ReadNullableString(
          root,
          "crisperWhisperLicenseAcceptanceVersion",
          defaults.CrisperWhisperLicenseAcceptanceVersion),
      };
    }

    return migrated;
  }

  private static int ReadInt(JsonElement element, string propertyName, int defaultValue) =>
    TryGetPropertyIgnoreCase(element, propertyName, out JsonElement value)
      && value.ValueKind == JsonValueKind.Number
      && value.TryGetInt32(out int parsed)
        ? parsed
        : defaultValue;

  private static bool ReadBool(JsonElement element, string propertyName, bool defaultValue) =>
    TryGetPropertyIgnoreCase(element, propertyName, out JsonElement value)
      && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? value.GetBoolean()
        : defaultValue;

  private static string ReadString(JsonElement element, string propertyName, string defaultValue) =>
    TryGetPropertyIgnoreCase(element, propertyName, out JsonElement value)
      && value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? defaultValue
        : defaultValue;

  private static string? ReadNullableString(JsonElement element, string propertyName, string? defaultValue)
  {
    if (!TryGetPropertyIgnoreCase(element, propertyName, out JsonElement value))
    {
      return defaultValue;
    }

    return value.ValueKind switch
    {
      JsonValueKind.Null => null,
      JsonValueKind.String => value.GetString(),
      _ => defaultValue,
    };
  }

  private static bool TryGetPropertyIgnoreCase(
    JsonElement element,
    string propertyName,
    out JsonElement value)
  {
    if (element.TryGetProperty(propertyName, out value))
    {
      return true;
    }

    foreach (JsonProperty property in element.EnumerateObject())
    {
      if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
      {
        value = property.Value;
        return true;
      }
    }

    value = default;
    return false;
  }
}
