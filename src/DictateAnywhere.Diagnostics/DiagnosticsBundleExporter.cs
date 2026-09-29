using System;
using System.Diagnostics.CodeAnalysis;
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
        "koncus-nai-diagnostics-",
        DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
        "-",
        Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8],
        ".zip"));

    bool created = false;
    try
    {
      using FileStream zipStream = new(bundlePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
      created = true;
      using ZipArchive archive = new(zipStream, ZipArchiveMode.Create, leaveOpen: false);

      int logCount = AddLogFiles(archive, logsDirectoryPath, options);
      bool includedSettings = options.IncludeSettings && AddOptionalSettings(archive, settingsFilePath, options.AllowlistedSettings);
      WriteManifest(archive, logCount, includedSettings, options);
    }
    catch
    {
      if (created)
      {
        try
        {
          File.Delete(bundlePath);
        }
        catch (IOException)
        {
          // Preserve the original export failure.
        }
        catch (UnauthorizedAccessException)
        {
          // Preserve the original export failure.
        }
      }

      throw;
    }

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

  [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
    Justification = "The log stream is disposed in the finally block; the redacting reader explicitly leaves it open.")]
  private static int AddLogFiles(ZipArchive archive, string logsDirectoryPath, DiagnosticsBundleExportOptions options)
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
      if (IsExcludedDiagnosticFile(fileName)
          || (!string.IsNullOrWhiteSpace(options.LogFilePrefix)
              && !fileName.StartsWith(options.LogFilePrefix + "-", StringComparison.OrdinalIgnoreCase)
              && !string.Equals(fileName, options.LogFilePrefix + ".log", StringComparison.OrdinalIgnoreCase)))
      {
        continue;
      }

      string entryName = Path.Combine("logs", fileName).Replace('\\', '/');
      Stream? source = null;
      try
      {
        try
        {
          source = OpenLogForRead(file);
        }
        catch (FileNotFoundException)
        {
          // A rotated log can disappear after enumeration.
          continue;
        }
        if (options.IncludeSensitiveData)
        {
          using Stream destination = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open();
          source.CopyTo(destination);
        }
        else
        {
          AddRedactedTextFile(archive, source, entryName, options.MetadataOnlyLogs);
        }
      }
      finally
      {
        source?.Dispose();
      }

      count++;
    }

    return count;
  }

  private static Stream OpenLogForRead(string path) =>
    new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

  private static bool IsExcludedDiagnosticFile(string fileName)
  {
    if (string.IsNullOrWhiteSpace(fileName))
    {
      return true;
    }

    // Reader worker logs are free-form and may contain prompt or path fragments
    // that marker-based redaction cannot reliably recognize.
    if (fileName.StartsWith("reader-", StringComparison.OrdinalIgnoreCase))
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

  private static bool AddOptionalSettings(ZipArchive archive, string? settingsFilePath, bool allowlistedOnly)
  {
    if (string.IsNullOrWhiteSpace(settingsFilePath) || !File.Exists(settingsFilePath))
    {
      return false;
    }

    string sanitized;
    try
    {
      JsonNode? node = JsonNode.Parse(File.ReadAllText(settingsFilePath, Encoding.UTF8));
      if (node is not JsonObject) return false;
      if (allowlistedOnly)
      {
        node = SafeSettingsExportProjector.Project((JsonObject)node);
      }
      else
      {
        SanitizeSettingsNode(node);
      }
      sanitized = node.ToJsonString(new JsonSerializerOptions
      {
        WriteIndented = true,
      });
    }
    catch (JsonException)
    {
      return false;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }

    ZipArchiveEntry entry = archive.CreateEntry("settings/settings.json", CompressionLevel.Optimal);
    using Stream entryStream = entry.Open();
    using StreamWriter writer = new(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.Write(sanitized);
    return true;
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
      "authKey", "auth_key", "client_secret", "clientsecret",
      "path", "directory", "folder", "fileName", "deviceId", "processName", "url"
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

  private static void AddRedactedTextFile(ZipArchive archive, Stream source, string entryName, bool metadataOnly)
  {
    ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
    using Stream entryStream = entry.Open();
    using StreamWriter writer = new(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    using StreamReader reader = new(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
    while (reader.ReadLine() is { } line)
    {
      if (metadataOnly)
      {
        string? projected = SafeLogExportProjector.Project(line);
        if (projected is not null) writer.WriteLine(projected);
      }
      else
      {
        writer.WriteLine(SensitiveDiagnosticsRedactor.Redact(line));
      }
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
      ContentsPolicy: options.MetadataOnlyLogs
        ? "app logs contain structured metadata and fixed retry outcomes only; raw history, audio, message text, paths, and credentials are excluded; settings contain allowlisted non-identifying fields"
        : "logs are redacted unless sensitive data is explicitly included; raw history and audio are strictly excluded; settings exclude credential material",
      ApplicationVersion: options.ApplicationVersion ?? "unknown",
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
    string ApplicationVersion,
    string RuntimeVersion);
}
