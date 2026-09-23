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
    Xunit.Assert.NotNull(persisted);
    Xunit.Assert.Equal(completed.ComposerText, persisted!.FinalText);
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

  public void Dispose() => LastDictationSessionCache.Clear();

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
