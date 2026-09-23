using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.Diagnostics.Tests;

public sealed class OperationDiagnosticsTests
{
  [Fact]
  public void Scope_EmitsCorrelatedEventsWithStagesAndOutcome()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 64 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    string opId = "op-root-123";
    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      using (OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
               diagnostics,
               operationName: "DictationSession",
               operationId: opId,
               provider: "whisper",
               model: "base",
               runtime: "python"))
      {
        opScope.Stage("capture");
        opScope.Stage("transcription");
        opScope.Complete();
      }
    }

    IReadOnlyList<ReconstructedOperationTimeline> timelines =
      DiagnosticTimelineReconstructor.ReconstructFromDirectory(scope.DirectoryPath);

    ReconstructedOperationTimeline timeline = Assert.Single(timelines);
    Assert.Equal(opId, timeline.OperationId);
    Assert.Equal("whisper", timeline.Provider);
    Assert.Equal("base", timeline.Model);
    Assert.Equal("python", timeline.Runtime);
    Assert.Equal(OperationOutcome.Completed, timeline.FinalOutcome);
    Assert.Null(timeline.FinalRemediationCode);
    Assert.NotNull(timeline.TotalDurationMs);

    Assert.Contains(timeline.Stages, s => s.Stage == "init" && s.Outcome == OperationOutcome.Started);
    Assert.Contains(timeline.Stages, s => s.Stage == "capture");
    Assert.Contains(timeline.Stages, s => s.Stage == "transcription");
    Assert.Contains(timeline.Stages, s => s.Outcome == OperationOutcome.Completed);
  }

  [Fact]
  public void Scope_SupportsNestedChildOperationsWithParentCorrelation()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 64 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    string parentOpId = "parent-workflow-1";
    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      using OperationDiagnosticScope parentScope = OperationDiagnosticScope.Begin(
        diagnostics,
        operationName: "BatchPreparation",
        operationId: parentOpId,
        provider: "kokoro",
        model: "af_heart");

      using (OperationDiagnosticScope child1 = parentScope.BeginChild("Section-0"))
      {
        child1.Stage("synthesis");
        child1.Complete();
      }

      using (OperationDiagnosticScope child2 = parentScope.BeginChild("Section-1"))
      {
        child2.Stage("synthesis");
        child2.Complete();
      }

      parentScope.Complete();
    }

    IReadOnlyList<ReconstructedOperationTimeline> timelines =
      DiagnosticTimelineReconstructor.ReconstructFromDirectory(scope.DirectoryPath);

    Assert.Equal(3, timelines.Count);

    ReconstructedOperationTimeline parent = Assert.Single(timelines, t => t.OperationId == parentOpId);
    Assert.Null(parent.ParentOperationId);
    Assert.Equal(OperationOutcome.Completed, parent.FinalOutcome);

    List<ReconstructedOperationTimeline> children = timelines.Where(t => t.ParentOperationId == parentOpId).ToList();
    Assert.Equal(2, children.Count);
    Assert.All(children, c =>
    {
      Assert.Equal(parentOpId, c.ParentOperationId);
      Assert.Equal("kokoro", c.Provider);
      Assert.Equal("af_heart", c.Model);
      Assert.Equal(OperationOutcome.Completed, c.FinalOutcome);
    });
  }

  [Fact]
  public void Scope_RecordsCancellationWithDurationAndRemediationCode()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 64 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    string opId = "op-cancel-999";
    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
        diagnostics,
        operationName: "ChatGeneration",
        operationId: opId,
        provider: "gemma",
        model: "gemma-2b");

      opScope.Stage("streaming");
      opScope.Cancel("User clicked stop");
    }

    IReadOnlyList<ReconstructedOperationTimeline> timelines =
      DiagnosticTimelineReconstructor.ReconstructFromDirectory(scope.DirectoryPath);

    ReconstructedOperationTimeline timeline = Assert.Single(timelines);
    Assert.Equal(opId, timeline.OperationId);
    Assert.Equal(OperationOutcome.Cancelled, timeline.FinalOutcome);
    Assert.Equal(DiagnosticRemediationCodes.OperationCancelled, timeline.FinalRemediationCode);
    Assert.NotNull(timeline.TotalDurationMs);

    StageTimelineEvent lastStage = timeline.Stages[^1];
    Assert.Equal(OperationOutcome.Cancelled, lastStage.Outcome);
    Assert.Equal(DiagnosticRemediationCodes.OperationCancelled, lastStage.RemediationCode);
  }

  [Fact]
  public void Scope_RecordsFailureWithClassifiedRemediationCode()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 64 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    string opId = "op-fail-456";
    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
        diagnostics,
        operationName: "DictationPipeline",
        operationId: opId,
        provider: "whisper",
        model: "base");

      opScope.Stage("capture");
      opScope.Fail(
        new InvalidOperationException("Microphone input device failure: WASAPI device unplugged."),
        "Audio capture failed.");
    }

    IReadOnlyList<ReconstructedOperationTimeline> timelines =
      DiagnosticTimelineReconstructor.ReconstructFromDirectory(scope.DirectoryPath);

    ReconstructedOperationTimeline timeline = Assert.Single(timelines);
    Assert.Equal(opId, timeline.OperationId);
    Assert.Equal(OperationOutcome.Failed, timeline.FinalOutcome);
    Assert.Equal(DiagnosticRemediationCodes.AudioCaptureFailure, timeline.FinalRemediationCode);
    Assert.NotNull(timeline.TotalDurationMs);

    StageTimelineEvent failedStage = Assert.Single(timeline.Stages, s => s.Outcome == OperationOutcome.Failed);
    Assert.Equal("ERROR", failedStage.Level);
    Assert.Equal(DiagnosticRemediationCodes.AudioCaptureFailure, failedStage.RemediationCode);
  }

  [Fact]
  public void Redactor_StripsTranscriptsPromptsAndAudioBytesFromLogs()
  {
    using TempDirectoryScope scope = new();
    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = scope.DirectoryPath,
      FileNamePrefix = "diag",
      MaxFileSizeBytes = 64 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      diagnostics.Info("transcript: This is confidential patient speech data");
      diagnostics.Info("prompt: Write a python script using password123");
      diagnostics.Info("audio chunk: 0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20");
      diagnostics.Info("Authorization: Bearer secret_access_token_12345");
    }

    string logFile = Directory.GetFiles(scope.DirectoryPath, "diag-*.log", SearchOption.TopDirectoryOnly).Single();
    string logContent = File.ReadAllText(logFile);

    Assert.DoesNotContain("confidential patient speech data", logContent, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("password123", logContent, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20", logContent, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("secret_access_token_12345", logContent, StringComparison.OrdinalIgnoreCase);

    Assert.Contains("[REDACTED]", logContent, StringComparison.Ordinal);
  }

  [Fact]
  public void BundleReconstruction_ReconstructsFailedPrimaryWorkflowWithoutSensitiveData()
  {
    using TempDirectoryScope testScope = new();
    string logsDir = Path.Combine(testScope.DirectoryPath, "logs");
    string outDir = Path.Combine(testScope.DirectoryPath, "bundle-out");
    string settingsPath = Path.Combine(testScope.DirectoryPath, "settings.json");

    Directory.CreateDirectory(logsDir);
    Directory.CreateDirectory(outDir);

    File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
    {
      schemaVersion = 13,
      apiKey = "super-secret-key",
      transcriptionProviderId = "whisper-local",
    }));

    StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = logsDir,
      FileNamePrefix = "notype",
      MaxFileSizeBytes = 64 * 1024,
      RetainedFileCount = 5,
      IncludeSensitiveData = false,
    };

    string failedOperationId = "workflow-dictation-failure-789";
    string sensitiveTranscript = "My social security number is 000-11-2222";
    string sensitivePrompt = "Classify this credit card: 4111222233334444";

    using (StructuredLocalDiagnostics diagnostics = new(options))
    {
      using (OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
               diagnostics,
               operationName: "DictationWorkflow",
               operationId: failedOperationId,
               provider: "whisper",
               model: "whisper-base-en",
               runtime: "python"))
      {
        opScope.Stage("capture");

        // Simulating attempt to log raw text that gets redacted
        diagnostics.Info($"transcript: {sensitiveTranscript}");
        diagnostics.Info($"user prompt: {sensitivePrompt}");

        opScope.Stage("transcription");

        // Fails during inference
        opScope.Fail(
          new InvalidOperationException("InferenceException: local transcription runtime process exited unexpectedly."),
          "Speech-to-text processing failed.",
          remediationCode: DiagnosticRemediationCodes.InferenceFailed);
      }
    }

    DiagnosticsBundleExporter exporter = new();
    string zipBundlePath = exporter.ExportToDirectory(outDir, logsDir, settingsPath);

    Assert.True(File.Exists(zipBundlePath));

    // 1. Reconstruct workflow from the bundle
    IReadOnlyList<ReconstructedOperationTimeline> timelines =
      DiagnosticTimelineReconstructor.ReconstructFromBundle(zipBundlePath);

    ReconstructedOperationTimeline workflow = Assert.Single(timelines, t => t.OperationId == failedOperationId);
    Assert.Equal("whisper", workflow.Provider);
    Assert.Equal("whisper-base-en", workflow.Model);
    Assert.Equal("python", workflow.Runtime);
    Assert.Equal(OperationOutcome.Failed, workflow.FinalOutcome);
    Assert.Equal(DiagnosticRemediationCodes.InferenceFailed, workflow.FinalRemediationCode);
    Assert.NotNull(workflow.TotalDurationMs);
    Assert.True(workflow.TotalDurationMs.Value >= 0);

    Assert.Contains(workflow.Stages, s => s.Stage == "capture");
    Assert.Contains(workflow.Stages, s => s.Stage == "transcription");
    Assert.Contains(workflow.Stages, s => s.Outcome == OperationOutcome.Failed && s.RemediationCode == DiagnosticRemediationCodes.InferenceFailed);

    // 2. Verify zero unredacted user text, transcript, prompt, or credentials exist in the bundle
    string[] forbiddenTokens =
    {
      sensitiveTranscript,
      sensitivePrompt,
      "000-11-2222",
      "4111222233334444",
      "super-secret-key",
    };

    DiagnosticTimelineReconstructor.AssertNoSensitiveData(zipBundlePath, forbiddenTokens);
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.OperationDiagTests", Guid.NewGuid().ToString("N"));
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
