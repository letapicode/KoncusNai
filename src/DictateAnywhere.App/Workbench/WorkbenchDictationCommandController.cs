using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchDictationCommandStatus
{
  Ignored,
  RecordingStarted,
  NoAudibleSpeech,
  TranscriptionCompleted,
  Canceled,
  Failed,
}

internal enum WorkbenchDictationCommandProgress
{
  RecordingStarting,
  Transcribing,
}

internal sealed record WorkbenchDictationCommandResult(
  WorkbenchDictationCommandStatus Status,
  string StatusMessage,
  bool OperationAccepted,
  string? ComposerText = null,
  WorkbenchDictationHistoryWriteResult? HistoryWrite = null,
  string? TranscribedText = null,
  string? SourceSessionId = null,
  long? SourceComposerRevision = null)
{
  public bool ShouldRefreshHistory => HistoryWrite?.ShouldRefresh == true;

  public string? ReconcileComposerText(string currentText, string currentSessionId, long currentRevision)
  {
    if (Status != WorkbenchDictationCommandStatus.TranscriptionCompleted
        || !string.Equals(SourceSessionId, currentSessionId, StringComparison.Ordinal))
    {
      return null;
    }

    return SourceComposerRevision == currentRevision
      ? ComposerText ?? currentText
      : WorkbenchComposerText.Append(currentText, TranscribedText ?? string.Empty);
  }
}

/// <summary>Owns Workbench microphone command arbitration, status mapping, and successful-transcription persistence.</summary>
[SuppressMessage(
  "Design",
  "CA1031:Do not catch general exception types",
  Justification = "This UI command boundary records unexpected device/runtime failures and returns bounded presentation state.")]
internal sealed class WorkbenchDictationCommandController
{
  private readonly WorkbenchOperationSession operationSession;
  private readonly WorkbenchDictationController dictationController;
  private readonly WorkbenchDictationHistoryRecorder historyRecorder;
  private readonly IDiagnostics diagnostics;

  public WorkbenchDictationCommandController(
    WorkbenchOperationSession operationSession,
    WorkbenchDictationController dictationController,
    WorkbenchDictationHistoryRecorder historyRecorder,
    IDiagnostics diagnostics)
  {
    this.operationSession = operationSession ?? throw new ArgumentNullException(nameof(operationSession));
    this.dictationController = dictationController ?? throw new ArgumentNullException(nameof(dictationController));
    this.historyRecorder = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  public async Task<WorkbenchDictationCommandResult> StartAsync(
    string source,
    Action<WorkbenchDictationCommandProgress>? progress = null)
  {
    if (!dictationController.IsConfigured)
    {
      return Failed("Error: audio service unavailable.", accepted: false);
    }

    using WorkbenchOperation? operation = operationSession.TryBegin(WorkbenchOperationKind.RecordingStart);
    if (operation is null)
    {
      return Ignored();
    }

    progress?.Invoke(WorkbenchDictationCommandProgress.RecordingStarting);
    try
    {
      if (!await dictationController.StartRecordingAsync(operation.CancellationToken).ConfigureAwait(true))
      {
        return Ignored(accepted: true);
      }

      diagnostics.Info($"Workbench recording started via {source}.");
      return new WorkbenchDictationCommandResult(
        WorkbenchDictationCommandStatus.RecordingStarted,
        $"Recording ({source})...",
        OperationAccepted: true);
    }
    catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
    {
      return new WorkbenchDictationCommandResult(
        WorkbenchDictationCommandStatus.Canceled,
        "Recording start cancelled.",
        OperationAccepted: true);
    }
    catch (Exception ex) when (IsExpectedFailure(ex))
    {
      diagnostics.Error("Workbench recording start failed.", ex);
      return Failed("Couldn’t start recording. See Diagnostics.", accepted: true);
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected Workbench recording start failure.", ex);
      return Failed("Couldn’t start recording. See Diagnostics.", accepted: true);
    }
  }

  public async Task<WorkbenchDictationCommandResult> StopAndTranscribeAsync(
    string source,
    string existingComposerText,
    string sessionId,
    AppSettings settings,
    long composerRevision = 0,
    Action<WorkbenchDictationCommandProgress>? progress = null)
  {
    ArgumentNullException.ThrowIfNull(settings);
    if (!dictationController.IsConfigured)
    {
      return Failed("Error: workbench services unavailable.", accepted: false);
    }

    if (dictationController.State != WorkbenchSessionState.Recording)
    {
      return Ignored();
    }

    using WorkbenchOperation? operation = operationSession.TryBegin(WorkbenchOperationKind.Transcription);
    if (operation is null)
    {
      return Ignored();
    }

    progress?.Invoke(WorkbenchDictationCommandProgress.Transcribing);
    try
    {
      WorkbenchTranscriptionOutcome outcome = await dictationController
        .StopAndTranscribeAsync(operation.CancellationToken)
        .ConfigureAwait(true);
      if (outcome.Status == WorkbenchTranscriptionStatus.Ignored)
      {
        return Ignored(accepted: true);
      }

      if (outcome.Status == WorkbenchTranscriptionStatus.NoAudibleSpeech)
      {
        diagnostics.Warning("Workbench recording stopped with no usable microphone audio.");
        return new WorkbenchDictationCommandResult(
          WorkbenchDictationCommandStatus.NoAudibleSpeech,
          DictationStatusMessages.NoAudibleSpeechDetected,
          OperationAccepted: true);
      }

      diagnostics.Info($"Workbench transcription completed for {outcome.AudioDuration.TotalSeconds:F1}s audio.");
      string composerText = WorkbenchComposerText.Append(existingComposerText, outcome.Text);
      WorkbenchDictationHistoryWriteResult history = await historyRecorder
        .RecordAsync(settings, sessionId, outcome.Text, composerText, "workbench", operation.CancellationToken)
        .ConfigureAwait(true);
      return new WorkbenchDictationCommandResult(
        WorkbenchDictationCommandStatus.TranscriptionCompleted,
        $"Done: {outcome.AudioDuration.TotalSeconds:F1}s audio transcribed.",
        OperationAccepted: true,
        composerText,
        history,
        outcome.Text,
        sessionId,
        composerRevision);
    }
    catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
    {
      return new WorkbenchDictationCommandResult(
        WorkbenchDictationCommandStatus.Canceled,
        "Transcription cancelled.",
        OperationAccepted: true);
    }
    catch (Exception ex) when (IsExpectedFailure(ex))
    {
      diagnostics.Error("Workbench stop/transcribe failed.", ex);
      return Failed("Transcription failed. See Diagnostics.", accepted: true);
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected Workbench stop/transcribe failure.", ex);
      return Failed("Transcription failed. See Diagnostics.", accepted: true);
    }
  }

  private static WorkbenchDictationCommandResult Ignored(bool accepted = false) => new(
    WorkbenchDictationCommandStatus.Ignored,
    string.Empty,
    accepted);

  private static WorkbenchDictationCommandResult Failed(string message, bool accepted) => new(
    WorkbenchDictationCommandStatus.Failed,
    message,
    accepted);

  private static bool IsExpectedFailure(Exception exception) =>
    exception is IOException or InvalidOperationException or UnauthorizedAccessException;
}
