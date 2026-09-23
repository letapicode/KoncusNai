using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DictateAnywhere.Diagnostics;

public sealed class DiagnosticsBundleExporter
{
  public string ExportToDirectory(string destinationDirectory, string logsDirectoryPath, string? settingsFilePath = null)
  {
    return ExportToDirectory(
      destinationDirectory,
      logsDirectoryPath,
      settingsFilePath,
      DiagnosticsBundleExportOptions.Default);
  }

  public string ExportToDirectory(
    string destinationDirectory,
    string logsDirectoryPath,
    string? settingsFilePath,
    DiagnosticsBundleExportOptions options)
  {
    ArgumentNullException.ThrowIfNull(options);
    if (options.IncludeHistory || options.IncludeAudio)
    {
      throw new InvalidOperationException("Diagnostic bundles strictly exclude raw history and audio records.");
    }

    string resolvedDestination = ResolveLocalPath(destinationDirectory);
    Directory.CreateDirectory(resolvedDestination);

    string bundlePath = Path.Combine(
      resolvedDestination,
      string.Concat(
        "dictate-anywhere-diagnostics-",
        DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
        ".zip"));

    using FileStream zipStream = new(bundlePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    using ZipArchive archive = new(zipStream, ZipArchiveMode.Create, leaveOpen: false);

    int logCount = AddLogFiles(archive, logsDirectoryPath, options.IncludeSensitiveData);
    bool includedSettings = options.IncludeSettings && AddOptionalSettings(archive, settingsFilePath);
    WriteManifest(archive, logCount, includedSettings, options);

    return bundlePath;
  }

  private static string ResolveLocalPath(string destinationDirectory)
  {
    if (string.IsNullOrWhiteSpace(destinationDirectory))
    {
      throw new ArgumentException("Destination directory must not be empty.", nameof(destinationDirectory));
    }

    string resolved = Path.GetFullPath(destinationDirectory);
    if (resolved.StartsWith(@"\\", StringComparison.Ordinal))
    {
      throw new InvalidOperationException("Diagnostics export only supports local file system paths.");
    }

    return resolved;
  }

  private static int AddLogFiles(ZipArchive archive, string logsDirectoryPath, bool includeSensitiveData)
  {
    if (string.IsNullOrWhiteSpace(logsDirectoryPath) || !Directory.Exists(logsDirectoryPath))
    {
      return 0;
    }

    string[] files = Directory.GetFiles(logsDirectoryPath, "*.log", SearchOption.TopDirectoryOnly);
    Array.Sort(files, StringComparer.OrdinalIgnoreCase);

    int count = 0;
    foreach (string file in files)
    {
      string fileName = Path.GetFileName(file);
      if (IsExcludedDiagnosticFile(fileName))
      {
        continue;
      }

      string entryName = Path.Combine("logs", fileName).Replace('\\', '/');
      if (includeSensitiveData)
      {
        archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
      }
      else
      {
        AddRedactedTextFile(archive, file, entryName);
      }

      count++;
    }

    return count;
  }

  private static bool IsExcludedDiagnosticFile(string fileName)
  {
    if (string.IsNullOrWhiteSpace(fileName))
    {
      return true;
    }

    ReadOnlySpan<string> forbidden =
    [
      "history", "chat", "dictation", "audio", "recording",
      ".jsonl", ".wav", ".mp3", ".mp4", ".pcm"
    ];

    foreach (string pattern in forbidden)
    {
      if (fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  private static bool AddOptionalSettings(ZipArchive archive, string? settingsFilePath)
  {
    if (string.IsNullOrWhiteSpace(settingsFilePath) || !File.Exists(settingsFilePath))
    {
      return false;
    }

    AddSanitizedSettings(archive, settingsFilePath);
    return true;
  }

  private static void AddSanitizedSettings(ZipArchive archive, string settingsFilePath)
  {
    ZipArchiveEntry entry = archive.CreateEntry("settings/settings.json", CompressionLevel.Optimal);
    using Stream entryStream = entry.Open();
    using StreamWriter writer = new(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    try
    {
      JsonNode? node = JsonNode.Parse(File.ReadAllText(settingsFilePath, Encoding.UTF8));
      SanitizeSettingsNode(node);

      writer.Write(node?.ToJsonString(new JsonSerializerOptions
      {
        WriteIndented = true,
      }) ?? "{}");
    }
    catch (JsonException)
    {
      writer.Write("{}");
    }
    catch (IOException)
    {
      writer.Write("{}");
    }
    catch (UnauthorizedAccessException)
    {
      writer.Write("{}");
    }
  }

  private static void SanitizeSettingsNode(JsonNode? node)
  {
    if (node is JsonObject jsonObject)
    {
      foreach ((string key, JsonNode? value) in jsonObject.ToArray())
      {
        if (IsSensitiveSettingName(key))
        {
          jsonObject.Remove(key);
          continue;
        }

        SanitizeSettingsNode(value);
      }

      return;
    }

    if (node is JsonArray jsonArray)
    {
      foreach (JsonNode? item in jsonArray)
      {
        SanitizeSettingsNode(item);
      }
    }
  }

  private static bool IsSensitiveSettingName(string name)
  {
    ReadOnlySpan<string> markers =
    [
      "password", "secret", "token", "apiKey", "api_key", "api-key",
      "credential", "verifier", "salt", "privateKey", "private_key",
      "authKey", "auth_key", "client_secret", "clientsecret"
    ];
    foreach (string marker in markers)
    {
      if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  private static void AddRedactedTextFile(ZipArchive archive, string sourcePath, string entryName)
  {
    ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
    using Stream entryStream = entry.Open();
    using StreamWriter writer = new(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    foreach (string line in File.ReadLines(sourcePath, Encoding.UTF8))
    {
      writer.WriteLine(SensitiveDiagnosticsRedactor.Redact(line));
    }
  }

  private static void WriteManifest(
    ZipArchive archive,
    int logCount,
    bool includedSettings,
    DiagnosticsBundleExportOptions options)
  {
    BundleManifest manifest = new(
      CreatedUtc: DateTimeOffset.UtcNow,
      LogFileCount: logCount,
      IncludedSettings: includedSettings,
      IncludedSensitiveData: options.IncludeSensitiveData,
      IncludedHistory: false,
      IncludedAudio: false,
      ContentsPolicy: "logs are redacted unless sensitive data is explicitly included; raw history and audio are strictly excluded; settings exclude credential material",
      RuntimeVersion: Environment.Version.ToString());

    ZipArchiveEntry entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
    using Stream stream = entry.Open();
    JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });
  }

  private sealed record BundleManifest(
    DateTimeOffset CreatedUtc,
    int LogFileCount,
    bool IncludedSettings,
    bool IncludedSensitiveData,
    bool IncludedHistory,
    bool IncludedAudio,
    string ContentsPolicy,
    string RuntimeVersion);
}
