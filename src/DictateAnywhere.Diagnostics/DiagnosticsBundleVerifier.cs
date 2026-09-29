using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace DictateAnywhere.Diagnostics;

/// <summary>Checks the finished support artifact before the app reports success.</summary>
public static class DiagnosticsBundleVerifier
{
  public static void Verify(string bundlePath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
    if (!File.Exists(bundlePath))
    {
      throw new FileNotFoundException("The diagnostics bundle was not created.", bundlePath);
    }

    try
    {
      using ZipArchive archive = ZipFile.OpenRead(bundlePath);
      ZipArchiveEntry manifest = archive.GetEntry("manifest.json")
        ?? throw new InvalidDataException("The diagnostics bundle has no manifest.");
      using Stream manifestStream = manifest.Open();
      using JsonDocument document = JsonDocument.Parse(manifestStream);
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object
          || !root.TryGetProperty("logFileCount", out JsonElement logCountElement)
          || !logCountElement.TryGetInt32(out int expectedLogCount)
          || !root.TryGetProperty("includedSettings", out JsonElement settingsElement)
          || settingsElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
          || !HasFalse(root, "includedSensitiveData")
          || !HasFalse(root, "includedHistory")
          || !HasFalse(root, "includedAudio"))
      {
        throw new InvalidDataException("The diagnostics bundle manifest is invalid or includes prohibited data.");
      }

      HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
      int actualLogCount = 0;
      bool hasSettings = false;
      foreach (ZipArchiveEntry entry in archive.Entries)
      {
        if (!names.Add(entry.FullName))
        {
          throw new InvalidDataException("The diagnostics bundle contains duplicate entries.");
        }

        if (entry.FullName == "manifest.json") continue;
        if (entry.FullName == "settings/settings.json")
        {
          hasSettings = true;
        }
        else if (entry.FullName.StartsWith("logs/", StringComparison.Ordinal)
                 && entry.FullName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                 && entry.FullName.AsSpan(5).IndexOf('/') < 0
                 && !ContainsProhibitedName(entry.FullName))
        {
          actualLogCount++;
        }
        else
        {
          throw new InvalidDataException("The diagnostics bundle contains an unexpected file.");
        }

        using Stream entryStream = entry.Open();
        entryStream.CopyTo(Stream.Null);
      }

      if (actualLogCount != expectedLogCount || hasSettings != settingsElement.GetBoolean())
      {
        throw new InvalidDataException("The diagnostics bundle contents do not match its manifest.");
      }
    }
    catch (JsonException exception)
    {
      throw new InvalidDataException("The diagnostics bundle manifest cannot be read.", exception);
    }
  }

  private static bool HasFalse(JsonElement root, string name) =>
    root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.False;

  private static bool ContainsProhibitedName(string name) =>
    name.Contains("history", StringComparison.OrdinalIgnoreCase)
    || name.Contains("chat", StringComparison.OrdinalIgnoreCase)
    || name.Contains("dictation", StringComparison.OrdinalIgnoreCase)
    || name.Contains("audio", StringComparison.OrdinalIgnoreCase)
    || name.Contains("recording", StringComparison.OrdinalIgnoreCase);
}
