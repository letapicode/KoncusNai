using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using DictateAnywhere.App.Diagnostics;
using DictateAnywhere.App.Productivity;
using DictateAnywhere.Diagnostics;

namespace DictateAnywhere.App.Tests;

public sealed class LocalDiagnosticsExportTests
{
  [Xunit.Fact]
  public void LocalFileDiagnostics_ExportsReadablePrivacyFilteredBundle()
  {
    string root = Path.Combine(Path.GetTempPath(), "KoncusNai.Diagnostics.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      string settingsPath = Path.Combine(root, "settings.json");
      File.WriteAllText(settingsPath, "{\"schemaVersion\":20,\"themePreference\":\"Dark\",\"apiToken\":\"do-not-export\",\"preferredAudioInputDeviceId\":\"private-device\",\"insertionBlockedProcessNames\":[\"private-process\"],\"futureSetting\":\"private-unknown\"}");
      StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
      {
        LogsDirectoryPath = Path.Combine(root, "logs"),
      };
      using LocalFileDiagnostics diagnostics = new(options, new DiagnosticsBundleExporter(), settingsPath);
      diagnostics.Info("Export flow check");
      diagnostics.Warning("File transcription failed for 'C:\\private\\dictation.wav': private dictated words");
      diagnostics.Error("Workbench failure", new InvalidOperationException("credential=private-secret"));
      File.WriteAllText(Path.Combine(options.LogsDirectoryPath, "worker.log"), "unstructured private input");

      string path = diagnostics.ExportBundle(Path.Combine(root, "support"));
      DiagnosticsBundleVerifier.Verify(path);
      using ZipArchive archive = ZipFile.OpenRead(path);
      Xunit.Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
      Xunit.Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "logs/worker.log");
      ZipArchiveEntry appLog = Xunit.Assert.Single(archive.Entries,
        entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
      using (StreamReader logReader = new(appLog.Open()))
      {
        string exportedLog = logReader.ReadToEnd();
        Xunit.Assert.DoesNotContain("private", exportedLog, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.DoesNotContain("dictation.wav", exportedLog, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.DoesNotContain("credential", exportedLog, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.Contains("timestampUtc", exportedLog, StringComparison.Ordinal);
        Xunit.Assert.Contains("category", exportedLog, StringComparison.Ordinal);
      }
      ZipArchiveEntry settings = Xunit.Assert.Single(archive.Entries,
        entry => entry.FullName == "settings/settings.json");
      using StreamReader reader = new(settings.Open());
      string exportedSettings = reader.ReadToEnd();
      Xunit.Assert.DoesNotContain("do-not-export", exportedSettings, StringComparison.Ordinal);
      Xunit.Assert.DoesNotContain("private-device", exportedSettings, StringComparison.Ordinal);
      Xunit.Assert.DoesNotContain("private-process", exportedSettings, StringComparison.Ordinal);
      Xunit.Assert.DoesNotContain("private-unknown", exportedSettings, StringComparison.Ordinal);
      Xunit.Assert.Contains("themePreference", exportedSettings, StringComparison.Ordinal);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Xunit.Fact]
  public void RetryEmptyState_ExportsFixedOutcomeAndApplicationVersionWithoutMessage()
  {
    string root = Path.Combine(Path.GetTempPath(), "KoncusNai.RetryDiagnostics.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
      {
        LogsDirectoryPath = Path.Combine(root, "logs"),
      };
      using LocalFileDiagnostics diagnostics = new(options, new DiagnosticsBundleExporter(),
        Path.Combine(root, "missing-settings.json"));
      RetryActionDiagnostics.Write(diagnostics,
        ProductivityActionResult.Failed("private dictated words", RetryOutcomeCode.NoHistory));

      string path = diagnostics.ExportBundle(Path.Combine(root, "support"));
      DiagnosticsBundleVerifier.Verify(path);
      using ZipArchive archive = ZipFile.OpenRead(path);
      using JsonDocument manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
      Xunit.Assert.Equal(typeof(LocalFileDiagnostics).Assembly
          .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        manifest.RootElement.GetProperty("applicationVersion").GetString());
      ZipArchiveEntry logEntry = Xunit.Assert.Single(archive.Entries,
        entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
      using StreamReader reader = new(logEntry.Open());
      string log = reader.ReadToEnd();
      Xunit.Assert.DoesNotContain("private dictated words", log, StringComparison.Ordinal);
      using JsonDocument eventDocument = JsonDocument.Parse(log);
      JsonElement entry = eventDocument.RootElement;
      Xunit.Assert.Equal("NoHistory", entry.GetProperty("properties").GetProperty("outcome").GetString());
      Xunit.Assert.False(entry.GetProperty("properties").GetProperty("succeeded").GetBoolean());
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }
}
