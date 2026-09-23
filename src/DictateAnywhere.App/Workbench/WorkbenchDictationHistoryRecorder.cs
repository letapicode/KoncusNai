using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Productivity;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal sealed record WorkbenchDictationHistoryWriteResult(
  DictationHistoryRecord Record,
  HistoryCommandStatus Status,
  string? StatusMessage)
{
  public bool ShouldRefresh => Status == HistoryCommandStatus.Succeeded;
}

/// <summary>Creates, caches, persists, and maps one Workbench dictation-history record.</summary>
internal sealed class WorkbenchDictationHistoryRecorder
{
  private readonly Func<AppSettings, DictationHistoryRecord, CancellationToken, Task<HistoryCommandResult>> recordAsync;
  private readonly IDiagnostics diagnostics;

  public WorkbenchDictationHistoryRecorder(
    Func<AppSettings, DictationHistoryRecord, CancellationToken, Task<HistoryCommandResult>> recordAsync,
    IDiagnostics diagnostics)
  {
    this.recordAsync = recordAsync ?? throw new ArgumentNullException(nameof(recordAsync));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "History persistence must not discard an otherwise successful transcription result.")]
  public async Task<WorkbenchDictationHistoryWriteResult> RecordAsync(
    AppSettings settings,
    string sessionId,
    string rawTranscript,
    string finalText,
    string source,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    DictationHistoryRecord record = new(
      CreatedUtc: DateTimeOffset.UtcNow,
      ProfileId: DictationHistoryRecord.DefaultProfileId,
      TranscriptionProviderId: settings.GetConfiguredTranscriptionProviderId(),
      TranscriptionModelId: settings.GetConfiguredTranscriptionModelId(),
      RawTranscript: rawTranscript,
      FinalText: finalText,
      TranscriptionDuration: TimeSpan.Zero,
      TextTransformationDuration: TimeSpan.Zero,
      TotalPipelineDuration: TimeSpan.Zero,
      SessionId: sessionId,
      Source: source);
    record = record.Normalize();
    LastDictationSessionCache.Store(record);

    try
    {
      HistoryCommandResult result = await recordAsync(settings, record, cancellationToken).ConfigureAwait(true);
      return result.Status switch
      {
        HistoryCommandStatus.Succeeded => new WorkbenchDictationHistoryWriteResult(record, result.Status, null),
        HistoryCommandStatus.Canceled or HistoryCommandStatus.Unavailable => Unavailable(record, result),
        _ => throw new InvalidOperationException($"Unexpected dictation-history record status: {result.Status}."),
      };
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected Workbench dictation-history write failure.", ex);
      return new WorkbenchDictationHistoryWriteResult(
        record,
        HistoryCommandStatus.Unavailable,
        "History write failed unexpectedly.");
    }
  }

  private WorkbenchDictationHistoryWriteResult Unavailable(
    DictationHistoryRecord record,
    HistoryCommandResult result)
  {
    diagnostics.Error("Workbench dictation-history write failed.", result.Failure);
    return new WorkbenchDictationHistoryWriteResult(record, result.Status, "History is unavailable.");
  }
}
