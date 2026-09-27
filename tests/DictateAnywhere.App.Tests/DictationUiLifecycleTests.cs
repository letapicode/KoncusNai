using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage("Reliability", "CA2000", Justification = "Controller owns the test service factories.")]
[Xunit.Collection(LastDictationSessionCacheCollection.Name)]
[Xunit.Trait("Category", "WindowsWpf")]
public sealed class DictationUiLifecycleTests : IDisposable
{
  public void Dispose() => LastDictationSessionCache.Clear();

  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public void MicrophoneLifecycleKeepsOwnedEditorEnabledUntilInsertionAndPersistence(bool expanded)
  {
    WpfTestSta.Run(() =>
    {
      WpfTestSta.Cleanup(LastDictationSessionCache.Clear);
      FakeAudioCaptureService audio = new(new AudioCaptureResult([1], 16000, TimeSpan.FromSeconds(1)));
      WorkbenchDictationController dictation = CreateDictation(audio, "new");
      WpfTestSta.Cleanup(() => dictation.DisposeAsync().AsTask().GetAwaiter().GetResult());
      WorkbenchOperationSession operations = new();
      WpfTestSta.Cleanup(() => operations.DisposeAsync().AsTask().GetAwaiter().GetResult());
      WorkbenchComposerView composer = new();
      WpfTestSta.Cleanup(composer.DisposePresentation);
      WorkbenchExpandedPromptView large = new();
      System.Windows.Controls.Grid panel = new();
      panel.Children.Add(composer);
      panel.Children.Add(large);
      large.SetOpen(expanded);
      System.Windows.Window host = new() { Content = panel, Width = 500, Height = 300,
        Left = -10000, Top = -10000, ShowInTaskbar = false };
      WpfTestSta.Cleanup(host.Close);
      dictation.ConfigureAsync(AppSettings.Default, registerHotkey: false).GetAwaiter().GetResult();
      composer.PromptText = "Hello old world";
      large.SetText(composer.PromptText);
      System.Windows.Controls.TextBox editor = expanded ? large.PromptElement : composer.PromptElement;
      host.Show();
      host.UpdateLayout();
      editor.Select(6, 3);
      WorkbenchDictationTarget target = new(editor, () => true);
      bool persisted = false;
      WorkbenchDictationHistoryRecorder history = new((_, _, _) =>
      {
        Xunit.Assert.True(operations.IsBusy);
        Xunit.Assert.Equal("Hello new world", editor.Text);
        persisted = true;
        return Task.FromResult(HistoryCommandResult.Succeeded(1));
      }, new NoOpDiagnostics());
      WorkbenchDictationCommandController controller = new(operations, dictation, history, new NoOpDiagnostics());
      void Render(WorkbenchDictationCommandProgress phase)
      {
        WorkbenchPresentationState state = WorkbenchPresentationReducer.Reduce(new WorkbenchPresentationSnapshot(
          dictation.State, operations.IsBusy, operations.IsImportingFiles, false, false, false, false,
          false, true, false, false, false, false, true, expanded, false, false, "", "",
          IsDictationOperation: operations.IsDictationOperation));
        composer.Render(state.Composer, state.Dictation);
        if (phase == WorkbenchDictationCommandProgress.Transcribing)
        {
          Xunit.Assert.Equal(WorkbenchSessionState.Transcribing, dictation.State);
          Xunit.Assert.Same(System.Windows.Media.Brushes.Transparent,
            Xunit.Assert.IsType<System.Windows.Controls.Button>(composer.FindName("Record")).Background);
        }
        large.PromptElement.IsEnabled = state.Composer.CanEditPrompt;
        Xunit.Assert.True(editor.IsEnabled);
        Xunit.Assert.False(state.Composer.CanSend);
      }
      controller.StartAsync("microphone", Render).GetAwaiter().GetResult();
      WorkbenchDictationCommandResult result = controller.StopAndTranscribeAsync("microphone", composer.PromptText,
        "session", AppSettings.Default, progress: Render, insertOutcome: target.Insert).GetAwaiter().GetResult();
      Xunit.Assert.Equal(WorkbenchInsertionOutcome.Inserted, result.InsertionOutcome);
      Xunit.Assert.False(result.NeedsAttention);
      Xunit.Assert.True(persisted);
      Xunit.Assert.False(operations.IsBusy);
      Xunit.Assert.Equal(9, editor.CaretIndex);
      Xunit.Assert.True(editor.Undo());
      Xunit.Assert.Equal("Hello old world", editor.Text);

    });
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
