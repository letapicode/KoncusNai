using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "The production controllers own services produced by these test factories.")]
[Xunit.Collection(LastDictationSessionCacheCollection.Name)]
public sealed class WorkbenchFileImportCommandControllerTests : IDisposable
{
  [Xunit.Fact]
  public async Task ImportAsync_MixedBatchOwnsComposerHistoryAndChatAttachmentTransaction()
  {
    string audioPath = CreateTempFile(".wav");
    string imagePath = CreateTempFile(".png");
    try
    {
      await using WorkbenchDictationController dictation = CreateConfiguredDictation();
      await dictation.ConfigureAsync(AppSettings.Default, registerHotkey: false);
      await using WorkbenchOperationSession operations = new();
      await using WorkbenchChatController chat = new(_ => new UnusedChatService());
      DictationHistoryRecord? persisted = null;
      WorkbenchDictationHistoryRecorder history = new(
        (_, record, _) =>
        {
          persisted = record;
          return Task.FromResult(HistoryCommandResult.Succeeded(1));
        },
        new NoOpDiagnostics());
      WorkbenchAudioImportController audio = new(
        new NoOpDiagnostics(),
        (_, _) => Task.FromResult(new AudioCaptureResult([1], 16_000, TimeSpan.FromSeconds(1))),
        (_, _) => Task.FromResult(new TranscriptionResult("audio transcript", "fake", TimeSpan.Zero)));
      WorkbenchDocumentImportController documents = new(() => new FakeOcrService(), new NoOpDiagnostics());
      await using WorkbenchFileImportCommandController controller = new(
        operations,
        dictation,
        audio,
        documents,
        history,
        chat,
        new NoOpDiagnostics());
      List<WorkbenchFileImportProgressKind> progress = [];

      WorkbenchFileImportResult result = await controller.ImportAsync(
        [audioPath, imagePath],
        "file-picker",
        "existing",
        "session-1",
        AppSettings.Default,
        update => progress.Add(update.Kind));

      Xunit.Assert.True(result.OperationAccepted);
      Xunit.Assert.True(result.ShouldRefreshHistory);
      Xunit.Assert.True(result.PendingFilesChanged);
      Xunit.Assert.Equal($"existing{Environment.NewLine}{Environment.NewLine}audio transcript", result.ComposerText);
      Xunit.Assert.NotNull(persisted);
      Xunit.Assert.Equal("file-picker", persisted!.Source);
      Xunit.Assert.Single(chat.PendingFiles);
      Xunit.Assert.Equal(
        [
          WorkbenchFileImportProgressKind.AudioBatchStarted,
          WorkbenchFileImportProgressKind.AudioItemCompleted,
          WorkbenchFileImportProgressKind.DocumentItemStarted,
        ],
        progress);
      Xunit.Assert.False(operations.IsBusy);
    }
    finally
    {
      File.Delete(audioPath);
      File.Delete(imagePath);
    }
  }

  [Xunit.Fact]
  public async Task ImportAsync_WhenAnotherOperationOwnsTheSession_RejectsWithoutMutation()
  {
    string audioPath = CreateTempFile(".wav");
    try
    {
      await using WorkbenchDictationController dictation = CreateConfiguredDictation();
      await dictation.ConfigureAsync(AppSettings.Default, registerHotkey: false);
      await using WorkbenchOperationSession operations = new();
      using WorkbenchOperation active = Xunit.Assert.IsType<WorkbenchOperation>(
        operations.TryBegin(WorkbenchOperationKind.SettingsApply));
      await using WorkbenchChatController chat = new(_ => new UnusedChatService());
      WorkbenchDictationHistoryRecorder history = new(
        (_, _, _) => throw new InvalidOperationException("History must not be called."),
        new NoOpDiagnostics());
      WorkbenchAudioImportController audio = new(
        new NoOpDiagnostics(),
        (_, _) => throw new InvalidOperationException("Decoder must not be called."),
        (_, _) => throw new InvalidOperationException("Transcriber must not be called."));
      WorkbenchDocumentImportController documents = new(() => new FakeOcrService(), new NoOpDiagnostics());
      await using WorkbenchFileImportCommandController controller = new(
        operations,
        dictation,
        audio,
        documents,
        history,
        chat,
        new NoOpDiagnostics());

      WorkbenchFileImportResult result = await controller.ImportAsync(
        [audioPath],
        "file-drop",
        string.Empty,
        "session-2",
        AppSettings.Default);

      Xunit.Assert.False(result.OperationAccepted);
      Xunit.Assert.Empty(chat.PendingFiles);
    }
    finally
    {
      File.Delete(audioPath);
    }
  }

  [Xunit.Fact]
  public async Task ImportAsync_WhenOperationSessionIsDisposed_StopsWithoutReportingFailure()
  {
    string imagePath = CreateTempFile(".png");
    try
    {
      await using WorkbenchDictationController dictation = CreateConfiguredDictation();
      await using WorkbenchOperationSession operations = new();
      await using WorkbenchChatController chat = new(_ => new UnusedChatService());
      WorkbenchDictationHistoryRecorder history = new(
        (_, _, _) => throw new InvalidOperationException("History must not be called."),
        new NoOpDiagnostics());
      WorkbenchAudioImportController audio = new(
        new NoOpDiagnostics(),
        (_, _) => throw new InvalidOperationException("Decoder must not be called."),
        (_, _) => throw new InvalidOperationException("Transcriber must not be called."));
      ControlledOcrService ocr = new();
      WorkbenchDocumentImportController documents = new(() => ocr, new NoOpDiagnostics());
      await using WorkbenchFileImportCommandController controller = new(
        operations,
        dictation,
        audio,
        documents,
        history,
        chat,
        new NoOpDiagnostics());

      Task<WorkbenchFileImportResult> import = controller.ImportAsync(
        [imagePath],
        "file-picker",
        string.Empty,
        "session-3",
        AppSettings.Default);
      await ocr.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

      ValueTask disposal = operations.DisposeAsync();
      WorkbenchFileImportResult result = await import;
      await disposal;

      Xunit.Assert.True(result.OperationAccepted);
      Xunit.Assert.Equal("File import stopped.", result.StatusMessage);
      Xunit.Assert.False(result.PendingFilesChanged);
      Xunit.Assert.Empty(chat.PendingFiles);
      Xunit.Assert.False(operations.IsBusy);
    }
    finally
    {
      File.Delete(imagePath);
    }
  }

  public void Dispose() => LastDictationSessionCache.Clear();

  private static WorkbenchDictationController CreateConfiguredDictation() => new(
    new NoOpDiagnostics(),
    _ => new FakeAudioCaptureService(),
    (_, _) => new FakeTranscriptionService(),
    () => new FakeHotkeyService());

  private static string CreateTempFile(string extension)
  {
    string path = Path.Combine(Path.GetTempPath(), $"notype-command-{Guid.NewGuid():N}{extension}");
    File.WriteAllBytes(path, [0]);
    return path;
  }

  private sealed class FakeAudioCaptureService : IAudioCaptureService, IAsyncDisposable
  {
    public bool IsCapturing { get; private set; }
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = true;
      return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(new AudioCaptureResult([], 16_000, TimeSpan.Zero));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FakeTranscriptionService : ITranscriptionService, IAsyncDisposable
  {
    public Task<TranscriptionResult> TranscribeAsync(
      AudioCaptureResult audio,
      string modelId,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(new TranscriptionResult(string.Empty, modelId, TimeSpan.Zero));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FakeHotkeyService : IHotkeyService
  {
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed { add { } remove { } }
    public event EventHandler<HotkeyEventArgs>? HotkeyReleased { add { } remove { } }

    public Task<HotkeyRegistrationResult> RegisterAsync(
      HotkeyBinding binding,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(new HotkeyRegistrationResult(true, null));

    public Task UnregisterAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FakeOcrService : IDocumentOcrService
  {
    public Task<DocumentOcrResult> RecognizeAsync(
      DocumentOcrRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(new DocumentOcrResult([new DocumentOcrLine("recognized", 1)], "fake"));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class ControlledOcrService : IDocumentOcrService
  {
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<DocumentOcrResult> RecognizeAsync(
      DocumentOcrRequest request,
      CancellationToken cancellationToken = default)
    {
      Started.TrySetResult();
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      throw new InvalidOperationException("Unreachable after cancellation.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class UnusedChatService : IChatCompletionService
  {
    public Task<ChatCompletionResult> CompleteAsync(
      ChatCompletionRequest request,
      CancellationToken cancellationToken = default) =>
      throw new InvalidOperationException("Chat completion must not be called.");
  }

  private sealed class NoOpDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}
