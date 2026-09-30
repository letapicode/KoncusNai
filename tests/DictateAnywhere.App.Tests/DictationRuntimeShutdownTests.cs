using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.App.Tests;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
  Justification = "The runtime owns fake services; every barrier is released before its bounded cleanup is awaited.")]
public sealed class DictationRuntimeShutdownTests
{
  [Xunit.Fact]
  public async Task SlowUndoUnregisterDoesNotLeaveDictationAcceptingHotkeys()
  {
    FakeService hotkey = new();
    TaskCompletionSource releaseUndo = new(TaskCreationOptions.RunContinuationsAsynchronously);
    FakeService undo = new() { UnregisterGate = releaseUndo.Task };
    FakeService service = new();
    DictationRuntime.RuntimeServices services = new(hotkey, undo, service, service, service, service, service, service, service);
    DictationRuntime runtime = new(service, service, null, new DictationHistoryChangeNotifier(), (_, _, _, _) => services);
    await runtime.StartAsync();
    Task stop = runtime.StopAsync();
    try
    {
      Xunit.Assert.False(stop.IsCompleted);
      Xunit.Assert.Equal(0, hotkey.PressedSubscribers);
      Xunit.Assert.Equal(0, hotkey.ReleasedSubscribers);
      hotkey.Press();
      Xunit.Assert.Equal(0, service.CaptureStarts);
      Xunit.Assert.False(runtime.IsRunning);
    }
    finally
    {
      releaseUndo.TrySetResult();
      await stop.WaitAsync(TimeSpan.FromSeconds(2));
      await runtime.DisposeAsync();
    }
  }

  [Xunit.Fact]
  public async Task RuntimeServiceFailureStillAttemptsEveryIndependentService()
  {
    FakeService failingHotkey = new() { DisposeFailure = new IOException("Hotkey cleanup failed") };
    FakeService others = new();
    DictationRuntime.RuntimeServices services = new(failingHotkey, others, others, others, others, others, others, null, others);
    await Xunit.Assert.ThrowsAsync<AggregateException>(() => services.DisposeAsync().AsTask());
    Xunit.Assert.Equal(1, failingHotkey.Disposals);
    Xunit.Assert.Equal(5, others.Disposals);
  }

  private sealed class FakeService : IHotkeyService, IAudioCaptureService, ITranscriptionService,
    ITextInsertionService, ITextTransformationService, IOverlayService, IUndoInsertionService,
    IDictationHistoryRecorder, ISettingsStore, IDiagnostics
  {
    private EventHandler<HotkeyEventArgs>? pressed;
    private EventHandler<HotkeyEventArgs>? released;
    internal int PressedSubscribers => pressed?.GetInvocationList().Length ?? 0;
    internal int ReleasedSubscribers => released?.GetInvocationList().Length ?? 0;
    internal int CaptureStarts { get; private set; }
    internal int Disposals { get; private set; }
    internal Task UnregisterGate { get; init; } = Task.CompletedTask;
    internal Exception? DisposeFailure { get; init; }
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed { add => pressed += value; remove => pressed -= value; }
    public event EventHandler<HotkeyEventArgs>? HotkeyReleased { add => released += value; remove => released -= value; }
    internal void Press() => pressed?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));
    public Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default) =>
      Task.FromResult(new HotkeyRegistrationResult(true, null));
    public Task UnregisterAsync(CancellationToken cancellationToken = default) => UnregisterGate;
    public ValueTask DisposeAsync()
    {
      Disposals++;
      return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
    }
    public bool IsCapturing => false;
    public Task StartAsync(CancellationToken cancellationToken = default) { CaptureStarts++; return Task.CompletedTask; }
    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(new AudioCaptureResult([], 16000, TimeSpan.Zero));
    public Task<TranscriptionResult> TranscribeAsync(AudioCaptureResult audio, string modelId, CancellationToken cancellationToken = default) =>
      Task.FromResult(new TranscriptionResult("", modelId, TimeSpan.Zero));
    public Task<InsertionResult> InsertAsync(string text, InsertionMethod preferredMethod, bool restoreClipboard, CancellationToken cancellationToken = default) =>
      Task.FromResult(new InsertionResult(true, preferredMethod, null));
    public Task<TextTransformationResult> TransformAsync(TextTransformationRequest request, CancellationToken cancellationToken = default) =>
      throw new NotSupportedException();
    public Task ShowStateAsync(DictationSessionState state, string? message = null, TimeSpan? elapsed = null,
      OverlayDisplayOptions? display = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<UndoInsertionResult> UndoLastInsertionAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(new UndoInsertionResult(false, null));
    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(AppSettings.Default);
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}
