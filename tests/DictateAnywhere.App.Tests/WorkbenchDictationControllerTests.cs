using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "WorkbenchDictationController owns and disposes every service produced by the test factories.")]
public sealed class WorkbenchDictationControllerTests
{
  [Xunit.Fact]
  public async Task StartAndStop_TransitionsStateAndReturnsTrimmedTranscript()
  {
    FakeAudioCaptureService audio = new()
    {
      Capture = new AudioCaptureResult([1, 2], 16_000, TimeSpan.FromSeconds(2)),
    };
    FakeTranscriptionService transcription = new("  hello world  ");
    await using WorkbenchDictationController controller = CreateController(audio, transcription);
    await controller.ConfigureAsync(AppSettings.Default, registerHotkey: false);

    bool started = await controller.StartRecordingAsync();
    WorkbenchTranscriptionOutcome outcome = await controller.StopAndTranscribeAsync();

    Xunit.Assert.True(started);
    Xunit.Assert.Equal(WorkbenchSessionState.Idle, controller.State);
    Xunit.Assert.Equal(WorkbenchTranscriptionStatus.Completed, outcome.Status);
    Xunit.Assert.Equal("hello world", outcome.Text);
    Xunit.Assert.Equal(TimeSpan.FromSeconds(2), outcome.AudioDuration);
    Xunit.Assert.Equal(AppSettings.Default.GetConfiguredTranscriptionModelId(), transcription.ModelIds[0]);
  }

  [Xunit.Fact]
  public async Task ConfigureAsync_ReplacesAndDisposesPreviousServices()
  {
    List<FakeAudioCaptureService> audioServices = [];
    List<FakeTranscriptionService> transcriptionServices = [];
    await using WorkbenchDictationController controller = new(
      new NoOpDiagnostics(),
      _ => Add(audioServices, new FakeAudioCaptureService()),
      (_, _) => Add(transcriptionServices, new FakeTranscriptionService("text")),
      () => new FakeHotkeyService());

    await controller.ConfigureAsync(AppSettings.Default, registerHotkey: false);
    await controller.ConfigureAsync(AppSettings.Default with { OverlayEnabled = false }, registerHotkey: false);

    Xunit.Assert.Equal(2, audioServices.Count);
    Xunit.Assert.Equal(2, transcriptionServices.Count);
    Xunit.Assert.True(audioServices[0].Disposed);
    Xunit.Assert.True(transcriptionServices[0].Disposed);
    Xunit.Assert.False(audioServices[1].Disposed);
    Xunit.Assert.False(transcriptionServices[1].Disposed);
  }

  [Xunit.Fact]
  public async Task ConfigureAsync_PartialConstructionFailure_DisposesCreatedService()
  {
    FakeAudioCaptureService audio = new();
    await using WorkbenchDictationController controller = new(
      new NoOpDiagnostics(),
      _ => audio,
      (_, _) => throw new InvalidOperationException("simulated transcription construction failure"),
      () => new FakeHotkeyService());

    await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
      controller.ConfigureAsync(AppSettings.Default, registerHotkey: false));

    Xunit.Assert.True(audio.Disposed);
    Xunit.Assert.False(controller.IsConfigured);
    Xunit.Assert.Equal(WorkbenchSessionState.Idle, controller.State);
  }

  [Xunit.Fact]
  public async Task ConfigureAsync_RegisteredHotkey_ForwardsToggleAndDetachesOnDispose()
  {
    FakeHotkeyService hotkey = new();
    WorkbenchDictationController controller = new(
      new NoOpDiagnostics(),
      _ => new FakeAudioCaptureService(),
      (_, _) => new FakeTranscriptionService("text"),
      () => hotkey);
    int toggles = 0;
    controller.ToggleRequested += (_, _) => toggles++;
    WorkbenchHotkeyRegistrationOutcome? outcome = await controller.ConfigureAsync(
      AppSettings.Default,
      registerHotkey: true);

    hotkey.RaisePressed();
    await controller.DisposeAsync();
    hotkey.RaisePressed();

    Xunit.Assert.NotNull(outcome);
    Xunit.Assert.True(outcome!.Success);
    Xunit.Assert.Equal(1, toggles);
    Xunit.Assert.True(hotkey.Disposed);
  }

  private static WorkbenchDictationController CreateController(
    FakeAudioCaptureService audio,
    FakeTranscriptionService transcription) => new(
      new NoOpDiagnostics(),
      _ => audio,
      (_, _) => transcription,
      () => new FakeHotkeyService());

  private static T Add<T>(ICollection<T> collection, T item)
  {
    collection.Add(item);
    return item;
  }

  private sealed class FakeAudioCaptureService : IAudioCaptureService, IAsyncDisposable
  {
    public bool IsCapturing { get; private set; }
    public bool Disposed { get; private set; }
    public AudioCaptureResult Capture { get; set; } = new([1], 16_000, TimeSpan.FromSeconds(1));

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = true;
      return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = false;
      return Task.FromResult(Capture);
    }

    public ValueTask DisposeAsync()
    {
      Disposed = true;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class FakeTranscriptionService : ITranscriptionService, IAsyncDisposable
  {
    private readonly string text;

    public FakeTranscriptionService(string text)
    {
      this.text = text;
    }

    public List<string> ModelIds { get; } = new();
    public bool Disposed { get; private set; }

    public Task<TranscriptionResult> TranscribeAsync(
      AudioCaptureResult audio,
      string modelId,
      CancellationToken cancellationToken = default)
    {
      ModelIds.Add(modelId);
      return Task.FromResult(new TranscriptionResult(text, modelId, TimeSpan.Zero));
    }

    public ValueTask DisposeAsync()
    {
      Disposed = true;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class FakeHotkeyService : IHotkeyService
  {
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;
    public event EventHandler<HotkeyEventArgs>? HotkeyReleased
    {
      add { }
      remove { }
    }
    public bool Disposed { get; private set; }

    public Task<HotkeyRegistrationResult> RegisterAsync(
      HotkeyBinding binding,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(new HotkeyRegistrationResult(true, null));

    public Task UnregisterAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void RaisePressed() => HotkeyPressed?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));

    public ValueTask DisposeAsync()
    {
      Disposed = true;
      HotkeyPressed = null;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class NoOpDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}
