using System;
using System.Globalization;

namespace DictateAnywhere.Settings;

public sealed class UnsupportedSettingsSchemaException : InvalidOperationException
{
  public UnsupportedSettingsSchemaException(string settingsFilePath, int foundVersion, int currentVersion)
    : base(string.Format(
      CultureInfo.InvariantCulture,
      "Settings were created by a newer Koncus Nai version (schema {0}; this version supports through {1}). The file was not changed. Update Koncus Nai, or back up and reset the settings file explicitly.",
      foundVersion,
      currentVersion))
  {
    SettingsFilePath = settingsFilePath ?? throw new ArgumentNullException(nameof(settingsFilePath));
    FoundVersion = foundVersion;
    CurrentVersion = currentVersion;
  }

  public string SettingsFilePath { get; }

  public int FoundVersion { get; }

  public int CurrentVersion { get; }
}
