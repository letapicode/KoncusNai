using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "The dictation controller owns services produced by the test factories.")]
[Xunit.Collection(LastDictationSessionCacheCollection.Name)]
public sealed class WorkbenchDictationCommandControllerTests : IDisposable
{
  [Xunit.Fact]
  public async Task StartThenStop_OwnsArbitrationComposerMergeAndHistoryWrite()
  {
    FakeAudioCaptureService audio = new(new AudioCaptureResult([1, 2], 16_000, TimeSpan.FromSeconds(2)));
    await using WorkbenchDictationController dictation = CreateDictation(audio, "transcribed text");
    await dictation.ConfigureAsync(AppSettings.Default, registerHotkey: false);
    await using WorkbenchOperationSession operations = new();
    DictationHistoryRecord? persisted = null;
    WorkbenchDictationHistoryRecorder history = new(
      (_, record, _) =>
      {
        persisted = record;
        return Task.FromResult(HistoryCommandResult.Succeeded(1));
      },
      new NoOpDiagnostics());
    WorkbenchDictationCommandController controller = new(operations, dictation, history, new NoOpDiagnostics());

    WorkbenchDictationCommandResult started = await controller.StartAsync("microphone");
    WorkbenchDictationCommandResult completed = await controller.StopAndTranscribeAsync(
      "button",
      "existing prompt",
      "session-1",
      AppSettings.Default,
      composerRevision: 42);

    Xunit.Assert.Equal(WorkbenchDictationCommandStatus.RecordingStarted, started.Status);
    Xunit.Assert.Equal(WorkbenchDictationCommandStatus.TranscriptionCompleted, completed.Status);
    Xunit.Assert.Equal($"existing prompt{Environment.NewLine}{Environment.NewLine}transcribed text", completed.ComposerText);
    Xunit.Assert.True(completed.ShouldRefreshHistory);
    Xunit.Assert.False(completed.NeedsAttention);
    Xunit.Assert.Empty(completed.StatusMessage);
    Xunit.Assert.NotNull(persisted);
    Xunit.Assert.Equal("transcribed text", persisted!.FinalText);
    Xunit.Assert.Equal("session-1", persisted.SessionId);
    Xunit.Assert.Equal("session-1", completed.SourceSessionId);
    Xunit.Assert.Equal(42, completed.SourceComposerRevision);
    Xunit.Assert.Equal("transcribed text", completed.TranscribedText);
    Xunit.Assert.False(operations.IsBusy);
  }

  [Xunit.Fact]
  public void CompletedResult_ReconcilesEditsButNeverCrossesSessionBoundary()
  {
    WorkbenchDictationCommandResult result = new(
      WorkbenchDictationCommandStatus.TranscriptionCompleted,
      "Done.",
      OperationAccepted: true,
      ComposerText: "original\r\n\r\ntranscribed",
      TranscribedText: "transcribed",
      SourceSessionId: "session-1",
      SourceComposerRevision: 7);

    Xunit.Assert.Equal(
      "new user edit\r\n\r\ntranscribed",
      result.ReconcileComposerText("new user edit", "session-1", currentRevision: 8));
    Xunit.Assert.Equal(
      "original\r\n\r\ntranscribed",
      result.ReconcileComposerText("original", "session-1", currentRevision: 7));
    Xunit.Assert.Null(result.ReconcileComposerText("new session text", "session-2", currentRevision: 8));
  }

  [Xunit.Fact]
  public async Task StopAndTranscribe_NoAudio_ReturnsConciseNoticeWithoutHistoryWrite()
  {
    FakeAudioCaptureService audio = new(new AudioCaptureResult([], 16_000, TimeSpan.FromMilliseconds(400)));
    await using WorkbenchDictationController dictation = CreateDictation(audio, string.Empty);
    await dictation.ConfigureAsync(AppSettings.Default, registerHotkey: false);
    await using WorkbenchOperationSession operations = new();
    int historyWrites = 0;
    WorkbenchDictationHistoryRecorder history = new(
      (_, _, _) =>
      {
        historyWrites++;
        return Task.FromResult(HistoryCommandResult.Succeeded(1));
      },
      new NoOpDiagnostics());
    WorkbenchDictationCommandController controller = new(operations, dictation, history, new NoOpDiagnostics());

    _ = await controller.StartAsync("microphone");
    WorkbenchDictationCommandResult result = await controller.StopAndTranscribeAsync(
      "button",
      string.Empty,
      "session-2",
      AppSettings.Default);

    Xunit.Assert.Equal(WorkbenchDictationCommandStatus.NoAudibleSpeech, result.Status);
    Xunit.Assert.Equal(DictationStatusMessages.NoAudibleSpeechDetected, result.StatusMessage);
    Xunit.Assert.Equal(0, historyWrites);
    Xunit.Assert.False(operations.IsBusy);
  }

  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public async Task StartupFeedbackFailure_StopsAcquiredMicrophone(bool cancellation)
  {
    using CancellationTokenSource startup = new();
    FakeAudioCaptureService audio = new(new AudioCaptureResult([1], 16000, TimeSpan.Zero));
    await using WorkbenchDictationController dictation = new(new NoOpDiagnostics(), _ => audio,
      (_, _) => new FakeTranscriptionService("hello"), () => new FakeHotkeyService(),
      _ => new FailingOverlay(DictateAnywhere.Core.Domain.DictationSessionState.Recording, cancellation ? startup : null));
    await dictation.ConfigureAsync(AppSettings.Default, false);
    if (cancellation) await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => dictation.StartRecordingAsync(startup.Token));
    else await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => dictation.StartRecordingAsync());
    Xunit.Assert.False(audio.IsCapturing);
    Xunit.Assert.Equal(WorkbenchSessionState.Idle, dictation.State);
  }

  [Xunit.Fact]
  public async Task CompletionFeedbackFailure_DoesNotDiscardInsertionOrPersistence()
  {
    FakeAudioCaptureService audio = new(new AudioCaptureResult([1], 16000, TimeSpan.Zero));
    await using WorkbenchDictationController dictation = new(new NoOpDiagnostics(), _ => audio,
      (_, _) => new FakeTranscriptionService("hello"), () => new FakeHotkeyService(),
      _ => new FailingOverlay(DictateAnywhere.Core.Domain.DictationSessionState.Completed));
    await dictation.ConfigureAsync(AppSettings.Default, false);
    await using WorkbenchOperationSession operations = new();
    int writes = 0;
    WorkbenchDictationHistoryRecorder history = new((_, _, _) =>
    { writes++; return Task.FromResult(HistoryCommandResult.Succeeded(1)); }, new NoOpDiagnostics());
    WorkbenchDictationCommandController controller = new(operations, dictation, history, new NoOpDiagnostics());
    await controller.StartAsync("microphone");
    WorkbenchDictationCommandResult result = await controller.StopAndTranscribeAsync("button", "", "session", AppSettings.Default);
    Xunit.Assert.Equal(WorkbenchDictationCommandStatus.TranscriptionCompleted, result.Status);
    Xunit.Assert.Equal(1, writes);
    Xunit.Assert.False(result.NeedsAttention);
  }

  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public async Task FailedErrorOverlayPreservesTheActualCaptureOrTranscriptionOutcome(bool transcriptionFailure)
  {
    FakeAudioCaptureService audio = new(new AudioCaptureResult(transcriptionFailure ? [1] : [], 16000, TimeSpan.Zero));
    await using WorkbenchDictationController dictation = new(new NoOpDiagnostics(), _ => audio,
      (_, _) => new FailedTranscription(), () => new FakeHotkeyService(),
      _ => new FailingOverlay(DictateAnywhere.Core.Domain.DictationSessionState.Error));
    await dictation.ConfigureAsync(AppSettings.Default, false);
    await using WorkbenchOperationSession operations = new();
    int writes = 0;
    WorkbenchDictationHistoryRecorder history = new((_, _, _) =>
    { writes++; return Task.FromResult(HistoryCommandResult.Succeeded(1)); }, new NoOpDiagnostics());
    WorkbenchDictationCommandController controller = new(operations, dictation, history, new NoOpDiagnostics());
    await controller.StartAsync("microphone");
    WorkbenchDictationCommandResult result = await controller.StopAndTranscribeAsync("button", "", "session", AppSettings.Default);
    Xunit.Assert.Equal(transcriptionFailure ? WorkbenchDictationCommandStatus.Failed : WorkbenchDictationCommandStatus.NoAudibleSpeech, result.Status);
    Xunit.Assert.Equal(0, writes);
  }

  private sealed class FailedTranscription : ITranscriptionService
  {
    public Task<TranscriptionResult> TranscribeAsync(AudioCaptureResult audio, string modelId,
      CancellationToken cancellationToken = default) => Task.FromException<TranscriptionResult>(new InvalidOperationException("inference failed"));
  }

  private sealed class FailingOverlay(DictateAnywhere.Core.Domain.DictationSessionState failedState,
    CancellationTokenSource? cancellation = null) : IOverlayService
  {
    public Task ShowStateAsync(DictateAnywhere.Core.Domain.DictationSessionState state, string? message = null,
      TimeSpan? elapsed = null, OverlayDisplayOptions? display = null, CancellationToken cancellationToken = default)
    {
      if (state != failedState) return Task.CompletedTask;
      if (cancellation is null) return Task.FromException(new InvalidOperationException("presentation failed"));
      cancellation.Cancel();
      return Task.FromCanceled(cancellationToken);
    }
    public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  [Xunit.Fact]
  public async Task TranscriptIsPublishedBeforeSlowStorageAndShutdownStillWaits()
  {
    FakeAudioCaptureService audio = new(new AudioCaptureResult([1, 2], 16_000, TimeSpan.FromSeconds(1)));
    await using WorkbenchDictationController dictation = CreateDictation(audio, "hello");
    await dictation.ConfigureAsync(AppSettings.Default, registerHotkey: false);
    await using WorkbenchOperationSession operations = new();
    TaskCompletionSource<HistoryCommandResult> storage = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool visible = false;
    WorkbenchDictationHistoryRecorder history = new((_, record, token) =>
    {
      Xunit.Assert.True(visible);
      Xunit.Assert.False(token.CanBeCanceled);
      Xunit.Assert.Equal("hello", record.FinalText);
      return storage.Task;
    }, new NoOpDiagnostics());
    WorkbenchDictationCommandController controller = new(operations, dictation, history, new NoOpDiagnostics());
    await controller.StartAsync("microphone");
    Task<WorkbenchDictationCommandResult> pending = controller.StopAndTranscribeAsync("button", "draft", "session",
      AppSettings.Default, insertTranscript: text => visible = text == "hello");
    Xunit.Assert.True(visible);
    Xunit.Assert.False(pending.IsCompleted);
    Task shutdown = operations.DisposeAsync().AsTask();
    Xunit.Assert.False(shutdown.IsCompleted);
    LastDictationSessionCache.Store(new DictationHistoryRecord(DateTimeOffset.UtcNow, "default", "provider", "model",
      "overlapping hotkey", "overlapping hotkey", TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
      SessionId: "other").Normalize());
    storage.SetResult(HistoryCommandResult.Succeeded(1));
    WorkbenchDictationCommandResult result = await pending;
    Xunit.Assert.Equal(WorkbenchDictationCommandStatus.TranscriptionCompleted, result.Status);
    Xunit.Assert.Equal("hello", result.TranscribedText);
    Xunit.Assert.Equal("hello", result.HistoryWrite!.Record.FinalText);
    await shutdown;
  }

  [Xunit.Fact]
  public async Task FailedEditorCallback_PreservesTranscriptInsteadOfReportingTranscriptionFailure()
  {
    FakeAudioCaptureService audio = new(new AudioCaptureResult([1], 16_000, TimeSpan.FromSeconds(1)));
    await using WorkbenchDictationController dictation = CreateDictation(audio, "recover me");
    await dictation.ConfigureAsync(AppSettings.Default, registerHotkey: false);
    await using WorkbenchOperationSession operations = new();
    WorkbenchDictationHistoryRecorder history = new((_, _, _) => Task.FromResult(HistoryCommandResult.Succeeded(1)), new NoOpDiagnostics());
    WorkbenchDictationCommandController controller = new(operations, dictation, history, new NoOpDiagnostics());
    await controller.StartAsync("microphone");
    WorkbenchDictationCommandResult result = await controller.StopAndTranscribeAsync("button", "", "session",
      AppSettings.Default, insertTranscript: _ => throw new InvalidOperationException("editor closed"));
    Xunit.Assert.Equal(WorkbenchDictationCommandStatus.TranscriptionCompleted, result.Status);
    Xunit.Assert.Contains("Saved to history", result.StatusMessage);
    Xunit.Assert.Equal(WorkbenchInsertionOutcome.Failed, result.InsertionOutcome);
    Xunit.Assert.DoesNotContain("draft changed", result.StatusMessage);
    Xunit.Assert.Equal("recover me", result.HistoryWrite!.Record.FinalText);
  }

  public void Dispose() => LastDictationSessionCache.Clear();

  [Xunit.Theory]
  [Xunit.InlineData((int)WorkbenchInsertionOutcome.DraftChanged, true)]
  [Xunit.InlineData((int)WorkbenchInsertionOutcome.FocusLost, false)]
  [Xunit.InlineData((int)WorkbenchInsertionOutcome.TargetUnavailable, true)]
  [Xunit.InlineData((int)WorkbenchInsertionOutcome.ConversationChanged, false)]
  public async Task RejectionReasonAndSaveOutcomeStayIndependent(int reason, bool saved)
  {
    WorkbenchInsertionOutcome rejection = (WorkbenchInsertionOutcome)reason;
    await using WorkbenchDictationController dictation = CreateDictation(
      new FakeAudioCaptureService(new AudioCaptureResult([1], 16000, TimeSpan.FromSeconds(1))), "recover");
    await dictation.ConfigureAsync(AppSettings.Default, registerHotkey: false);
    await using WorkbenchOperationSession operations = new();
    WorkbenchDictationHistoryRecorder history = new((_, _, _) => Task.FromResult(saved
      ? HistoryCommandResult.Succeeded(1) : HistoryCommandResult.Unavailable(new System.IO.IOException("Storage unavailable"))), new NoOpDiagnostics());
    WorkbenchDictationCommandController controller = new(operations, dictation, history, new NoOpDiagnostics());
    await controller.StartAsync("microphone");
    WorkbenchDictationCommandResult result = await controller.StopAndTranscribeAsync("microphone", "", "session",
      AppSettings.Default, insertOutcome: _ => rejection);
    Xunit.Assert.Equal(rejection, result.InsertionOutcome);
    Xunit.Assert.Equal(rejection == WorkbenchInsertionOutcome.DraftChanged, result.StatusMessage.Contains("draft changed", StringComparison.Ordinal));
    Xunit.Assert.Equal(saved, result.StatusMessage.Contains("Saved to history", StringComparison.Ordinal));
    Xunit.Assert.Equal("recover", result.TranscribedText);
    Xunit.Assert.True(result.NeedsAttention);
  }

  private static WorkbenchDictationController CreateDictation(
    FakeAudioCaptureService audio,
    string transcript) => new(
    new NoOpDiagnostics(),
    _ => audio,
    (_, _) => new FakeTranscriptionService(transcript),
    () => new FakeHotkeyService());

  private sealed class FakeAudioCaptureService(AudioCaptureResult capture) : IAudioCaptureService, IAsyncDisposable
  {
    public bool IsCapturing { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = true;
      return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = false;
      return Task.FromResult(capture);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FakeTranscriptionService(string transcript) : ITranscriptionService, IAsyncDisposable
  {
    public Task<TranscriptionResult> TranscribeAsync(
      AudioCaptureResult audio,
      string modelId,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(new TranscriptionResult(transcript, modelId, TimeSpan.Zero));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FakeHotkeyService : IHotkeyService
  {
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed
    {
      add { }
      remove { }
    }

    public event EventHandler<HotkeyEventArgs>? HotkeyReleased
    {
      add { }
      remove { }
    }

    public Task<HotkeyRegistrationResult> RegisterAsync(
      HotkeyBinding binding,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(new HotkeyRegistrationResult(true, null));

    public Task UnregisterAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class NoOpDiagnostics : IDiagnostics
  {
    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
  }
}
