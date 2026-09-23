using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DictateAnywhere.Diagnostics.Tests;

public sealed class StructuredLocalDiagnosticsTests
{
  [Xunit.Fact]
  public void Error_WritesStructuredJsonWithCategory()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Error("Unable to register configured hotkey.", new InvalidOperationException("Hotkey is already in use."));
    }

    string logFile = Directory.GetFiles(scope.DirectoryPath, "diag-*.log", SearchOption.TopDirectoryOnly).Single();
    string line = File.ReadLines(logFile).Single();
    using JsonDocument json = JsonDocument.Parse(line);

    Xunit.Assert.Equal("ERROR", json.RootElement.GetProperty("level").GetString());
    Xunit.Assert.Equal(
      DiagnosticFailureCategory.HotkeyRegistrationFailure.ToString(),
      json.RootElement.GetProperty("category").GetString());
  }

  [Xunit.Fact]
  public void Warning_WritesReservedHotkeyCategory_WhenConflictMessageDetected()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Warning("Workbench hotkey registration failed: already registered by another application.");
    }

    string logFile = Directory.GetFiles(scope.DirectoryPath, "diag-*.log", SearchOption.TopDirectoryOnly).Single();
    string line = File.ReadLines(logFile).Single();
    using JsonDocument json = JsonDocument.Parse(line);

    Xunit.Assert.Equal("WARN", json.RootElement.GetProperty("level").GetString());
    Xunit.Assert.Equal(
      DiagnosticFailureCategory.HotkeyReservedBySystemOrApp.ToString(),
      json.RootElement.GetProperty("category").GetString());
  }

  [Xunit.Fact]
  public void Info_RedactsTranscript_ByDefault()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Info("transcript: hello this should never be logged verbatim");
    }

    string line = File.ReadLines(Directory.GetFiles(scope.DirectoryPath, "diag-*.log").Single()).Single();
    using JsonDocument json = JsonDocument.Parse(line);
    string? message = json.RootElement.GetProperty("message").GetString();

    Xunit.Assert.Equal("transcript: [REDACTED]", message);
  }

  [Xunit.Fact]
  public void Info_IncludesTranscript_WhenOptedIn()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = true,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Info("transcript: hello this is expected because opt-in is true");
    }

    string line = File.ReadLines(Directory.GetFiles(scope.DirectoryPath, "diag-*.log").Single()).Single();
    using JsonDocument json = JsonDocument.Parse(line);
    string? message = json.RootElement.GetProperty("message").GetString();

    Xunit.Assert.Equal("transcript: hello this is expected because opt-in is true", message);
  }

  [Xunit.Fact]
  public void Error_WritesSanitizedInferenceSummary_WhenStructuredExceptionProvided()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Error(
        "Dictation failed. Check logs for details.",
        new InferenceException(
          "Input WAV file does not exist: 'C:\\Temp\\sensitive.wav'.",
          reason: "InputFileMissing",
          diagnosticSummary: "The transcription input file was unavailable before processing started."));
    }

    string line = File.ReadLines(Directory.GetFiles(scope.DirectoryPath, "diag-*.log").Single()).Single();
    using JsonDocument json = JsonDocument.Parse(line);

    Xunit.Assert.Equal(
      DiagnosticFailureCategory.InferenceFailure.ToString(),
      json.RootElement.GetProperty("category").GetString());
    Xunit.Assert.Equal(
      "The transcription input file was unavailable before processing started.",
      json.RootElement.GetProperty("exceptionMessage").GetString());
  }

  [Xunit.Fact]
  public void Info_WritesStructuredProperties_WhenProvided()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Info(
        "Cohere transcription completed.",
        new Dictionary<string, object?>
        {
          ["correlationId"] = "abc123",
          ["stage"] = "completed",
          ["totalMs"] = 1234.5,
        });
    }

    string line = File.ReadLines(Directory.GetFiles(scope.DirectoryPath, "diag-*.log").Single()).Single();
    using JsonDocument json = JsonDocument.Parse(line);
    JsonElement properties = json.RootElement.GetProperty("properties");

    Xunit.Assert.Equal("abc123", properties.GetProperty("correlationId").GetString());
    Xunit.Assert.Equal("completed", properties.GetProperty("stage").GetString());
    Xunit.Assert.Equal(1234.5, properties.GetProperty("totalMs").GetDouble());
  }

  [Xunit.Fact]
  public void Info_PreservesNumericTranscriptionWallTimingWithoutExposingSensitiveText()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Info(
        "Dictation stop-to-visible timing completed.",
        new Dictionary<string, object?>
        {
          ["transcriptionWallMs"] = 2058.42,
          ["transcript"] = "private words",
          ["transcriptionWallMsAsText"] = "2058.42",
        });
    }

    string line = File.ReadLines(Directory.GetFiles(scope.DirectoryPath, "diag-*.log").Single()).Single();
    using JsonDocument json = JsonDocument.Parse(line);
    JsonElement properties = json.RootElement.GetProperty("properties");

    Xunit.Assert.Equal(2058.42, properties.GetProperty("transcriptionWallMs").GetDouble());
    Xunit.Assert.Equal("[REDACTED]", properties.GetProperty("transcript").GetString());
    Xunit.Assert.Equal("[REDACTED]", properties.GetProperty("transcriptionWallMsAsText").GetString());
    Xunit.Assert.DoesNotContain("private words", line, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void Error_WritesExceptionStackTrace_WhenAvailable()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = true,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      try
      {
        throw new InvalidOperationException("boom");
      }
      catch (InvalidOperationException ex)
      {
        diagnostics.Error("Structured failure.", ex);
      }
    }

    string line = File.ReadLines(Directory.GetFiles(scope.DirectoryPath, "diag-*.log").Single()).Single();
    using JsonDocument json = JsonDocument.Parse(line);

    Xunit.Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("exceptionStackTrace").GetString()));
  }

  [Xunit.Fact]
  public void Info_DoesNotRedactBenignTranscriptionStatusMessage()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Info("Transcription completed with provider 'cohere-local' model 'transcribe-v2' in 2058 ms (69 chars).");
    }

    string line = File.ReadLines(Directory.GetFiles(scope.DirectoryPath, "diag-*.log").Single()).Single();
    using JsonDocument json = JsonDocument.Parse(line);
    string? message = json.RootElement.GetProperty("message").GetString();

    Xunit.Assert.Equal("Transcription completed with provider 'cohere-local' model 'transcribe-v2' in 2058 ms (69 chars).", message);
  }

  [Xunit.Fact]
  public void Info_RedactsLongHexRun_ByDefault()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 16 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Info("payload=0123456789abcdef0123456789abcdef");
    }

    string line = File.ReadLines(Directory.GetFiles(scope.DirectoryPath, "diag-*.log").Single()).Single();
    using JsonDocument json = JsonDocument.Parse(line);
    string? message = json.RootElement.GetProperty("message").GetString();

    Xunit.Assert.Equal("[REDACTED SENSITIVE PAYLOAD]", message);
  }

  [Xunit.Fact]
  public void Write_RotatesAndRetainsConfiguredCount()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 1024,
      RetainedFileCount = 2,
      IncludeSensitiveData = false,
    };

    using StructuredLocalDiagnostics diagnostics = new(options);
    string payload = new('A', 300);
    for (int i = 0; i < 20; i++)
    {
      diagnostics.Info($"entry {i}: {payload}");
    }

    string[] files = Directory.GetFiles(scope.DirectoryPath, "diag-*.log", SearchOption.TopDirectoryOnly);
    Xunit.Assert.InRange(files.Length, 1, 2);
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

  private sealed class InferenceException : InvalidOperationException
  {
    public InferenceException(string message, string reason, string diagnosticSummary)
      : base(message)
    {
      Reason = reason;
      DiagnosticSummary = diagnosticSummary;
    }

    public string Reason { get; }

    public string DiagnosticSummary { get; }
  }
}
