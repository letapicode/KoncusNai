using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace DictateAnywhere.Diagnostics.Tests;

public sealed class DiagnosticsBundleExporterTests
{
  [Xunit.Fact]
  public void ExportToDirectory_CreatesBundleWithManifestAndLogs()
  {
    using TempDirectoryScope root = new();
    string logsDirectory = Path.Combine(root.DirectoryPath, "logs");
    string outputDirectory = Path.Combine(root.DirectoryPath, "output");
    string settingsFile = Path.Combine(root.DirectoryPath, "settings.json");

    Directory.CreateDirectory(logsDirectory);
    File.WriteAllText(Path.Combine(logsDirectory, "app-20260214-100000-0001.log"), "{\"message\":\"ok\"}");
    File.WriteAllText(settingsFile, "{\"schemaVersion\":2}");

    DiagnosticsBundleExporter exporter = new();
    string bundlePath = exporter.ExportToDirectory(outputDirectory, logsDirectory, settingsFile);

    Xunit.Assert.True(File.Exists(bundlePath));

    using ZipArchive archive = ZipFile.OpenRead(bundlePath);
    Xunit.Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "manifest.json", StringComparison.Ordinal));
    Xunit.Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
    Xunit.Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "settings/settings.json", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public void ExportToDirectory_RemovesSensitiveSettingsRecursively()
  {
    using TempDirectoryScope root = new();
    string outputDirectory = Path.Combine(root.DirectoryPath, "output");
    string settingsFile = Path.Combine(root.DirectoryPath, "settings.json");
    File.WriteAllText(
      settingsFile,
      "{\"schemaVersion\":13,\"apiToken\":\"secret-value\",\"provider\":{\"clientSecret\":\"nested-secret\",\"model\":\"local\"}}");

    DiagnosticsBundleExporter exporter = new();
    string bundlePath = exporter.ExportToDirectory(outputDirectory, logsDirectoryPath: string.Empty, settingsFilePath: settingsFile);

    using ZipArchive archive = ZipFile.OpenRead(bundlePath);
    ZipArchiveEntry settingsEntry = Xunit.Assert.Single(archive.Entries, entry => string.Equals(entry.FullName, "settings/settings.json", StringComparison.Ordinal));
    using StreamReader reader = new(settingsEntry.Open());
    string exportedSettings = reader.ReadToEnd();

    Xunit.Assert.DoesNotContain("apiToken", exportedSettings, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("clientSecret", exportedSettings, StringComparison.Ordinal);
    Xunit.Assert.Contains("model", exportedSettings, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void ExportToDirectory_RedactsSensitiveLogPayloadsByDefault()
  {
    using TempDirectoryScope root = new();
    string logsDirectory = Path.Combine(root.DirectoryPath, "logs");
    string outputDirectory = Path.Combine(root.DirectoryPath, "output");

    Directory.CreateDirectory(logsDirectory);
    File.WriteAllText(
      Path.Combine(logsDirectory, "app-20260214-100000-0001.log"),
      "recognized text: private words");

    DiagnosticsBundleExporter exporter = new();
    string bundlePath = exporter.ExportToDirectory(outputDirectory, logsDirectory);

    using ZipArchive archive = ZipFile.OpenRead(bundlePath);
    ZipArchiveEntry entry = Xunit.Assert.Single(archive.Entries, candidate => candidate.FullName.StartsWith("logs/", StringComparison.Ordinal));
    using StreamReader reader = new(entry.Open());
    string exportedLog = reader.ReadToEnd();

    Xunit.Assert.Contains("[REDACTED]", exportedLog, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("private words", exportedLog, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void ExportToDirectory_ManifestDeclaresSensitiveDataExcludedByDefault()
  {
    using TempDirectoryScope root = new();
    string outputDirectory = Path.Combine(root.DirectoryPath, "output");

    DiagnosticsBundleExporter exporter = new();
    string bundlePath = exporter.ExportToDirectory(outputDirectory, logsDirectoryPath: string.Empty, settingsFilePath: null);

    using ZipArchive archive = ZipFile.OpenRead(bundlePath);
    ZipArchiveEntry manifestEntry = Xunit.Assert.Single(archive.Entries, entry => string.Equals(entry.FullName, "manifest.json", StringComparison.Ordinal));
    using JsonDocument document = JsonDocument.Parse(manifestEntry.Open());

    JsonElement rootElement = document.RootElement;
    Xunit.Assert.False(rootElement.GetProperty("includedSensitiveData").GetBoolean());
    Xunit.Assert.False(rootElement.GetProperty("includedHistory").GetBoolean());
    Xunit.Assert.False(rootElement.GetProperty("includedAudio").GetBoolean());
  }

  [Xunit.Fact]
  public void ExportToDirectory_RejectsUncDestination()
  {
    DiagnosticsBundleExporter exporter = new();
    InvalidOperationException exception = Xunit.Assert.Throws<InvalidOperationException>(() =>
      exporter.ExportToDirectory(@"\\server\share", logsDirectoryPath: string.Empty));

    Xunit.Assert.Contains("local", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public void ExportToDirectory_WorksWithoutLogsDirectory()
  {
    using TempDirectoryScope root = new();
    string outputDirectory = Path.Combine(root.DirectoryPath, "output");

    DiagnosticsBundleExporter exporter = new();
    string bundlePath = exporter.ExportToDirectory(outputDirectory, logsDirectoryPath: string.Empty, settingsFilePath: null);

    using ZipArchive archive = ZipFile.OpenRead(bundlePath);
    Xunit.Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "manifest.json", StringComparison.Ordinal));
    Xunit.Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public void ExportToDirectory_ThrowsIfHistoryOrAudioRequested()
  {
    DiagnosticsBundleExporter exporter = new();
    using TempDirectoryScope root = new();

    Xunit.Assert.Throws<InvalidOperationException>(() =>
      exporter.ExportToDirectory(
        root.DirectoryPath,
        logsDirectoryPath: string.Empty,
        settingsFilePath: null,
        new DiagnosticsBundleExportOptions(IncludeHistory: true)));

    Xunit.Assert.Throws<InvalidOperationException>(() =>
      exporter.ExportToDirectory(
        root.DirectoryPath,
        logsDirectoryPath: string.Empty,
        settingsFilePath: null,
        new DiagnosticsBundleExportOptions(IncludeAudio: true)));
  }

  [Xunit.Fact]
  public void ExportToDirectory_StrictlyExcludesHistoryAndAudioFilesEvenIfInLogsFolder()
  {
    using TempDirectoryScope root = new();
    string logsDirectory = Path.Combine(root.DirectoryPath, "logs");
    string outputDirectory = Path.Combine(root.DirectoryPath, "output");
    Directory.CreateDirectory(logsDirectory);

    File.WriteAllText(Path.Combine(logsDirectory, "app-main.log"), "{\"message\":\"main app log\"}");
    File.WriteAllText(Path.Combine(logsDirectory, "dictation-history.local.jsonl"), "{\"text\":\"private dictation\"}");
    File.WriteAllText(Path.Combine(logsDirectory, "chat-history.local.jsonl"), "{\"text\":\"private chat\"}");
    File.WriteAllText(Path.Combine(logsDirectory, "audio-sample.wav"), "RIFF0000WAVE");
    File.WriteAllText(Path.Combine(logsDirectory, "recording.mp3"), "ID3000000");
    File.WriteAllText(Path.Combine(logsDirectory, "history-export.log"), "{\"text\":\"history log\"}");
    File.WriteAllText(Path.Combine(logsDirectory, "audio-debug.log"), "{\"text\":\"audio log\"}");

    DiagnosticsBundleExporter exporter = new();
    string bundlePath = exporter.ExportToDirectory(outputDirectory, logsDirectory);

    using ZipArchive archive = ZipFile.OpenRead(bundlePath);
    var entryNames = archive.Entries.Select(e => e.FullName).ToList();

    Xunit.Assert.Contains("logs/app-main.log", entryNames);
    Xunit.Assert.DoesNotContain(entryNames, name => name.Contains("history", StringComparison.OrdinalIgnoreCase));
    Xunit.Assert.DoesNotContain(entryNames, name => name.Contains("audio", StringComparison.OrdinalIgnoreCase));
    Xunit.Assert.DoesNotContain(entryNames, name => name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase));
    Xunit.Assert.DoesNotContain(entryNames, name => name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
    Xunit.Assert.DoesNotContain(entryNames, name => name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
  }

  [Xunit.Fact]
  public void ExportToDirectory_RemovesUnderscoredAndHyphenatedSensitiveSettings()
  {
    using TempDirectoryScope root = new();
    string outputDirectory = Path.Combine(root.DirectoryPath, "output");
    string settingsFile = Path.Combine(root.DirectoryPath, "settings.json");
    File.WriteAllText(
      settingsFile,
      "{\"schemaVersion\":14,\"api_key\":\"secret1\",\"api-key\":\"secret2\",\"private_key\":\"key123\",\"client_secret\":\"csec\",\"normalSetting\":\"keepMe\"}");

    DiagnosticsBundleExporter exporter = new();
    string bundlePath = exporter.ExportToDirectory(outputDirectory, logsDirectoryPath: string.Empty, settingsFilePath: settingsFile);

    using ZipArchive archive = ZipFile.OpenRead(bundlePath);
    ZipArchiveEntry settingsEntry = Xunit.Assert.Single(archive.Entries, entry => string.Equals(entry.FullName, "settings/settings.json", StringComparison.Ordinal));
    using StreamReader reader = new(settingsEntry.Open());
    string exportedSettings = reader.ReadToEnd();

    Xunit.Assert.DoesNotContain("api_key", exportedSettings, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("api-key", exportedSettings, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("private_key", exportedSettings, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("client_secret", exportedSettings, StringComparison.Ordinal);
    Xunit.Assert.Contains("normalSetting", exportedSettings, StringComparison.Ordinal);
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.Diagnostics.Tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
      if (Directory.Exists(DirectoryPath))
      {
        Directory.Delete(DirectoryPath, recursive: true);
      }
    }
  }
}
