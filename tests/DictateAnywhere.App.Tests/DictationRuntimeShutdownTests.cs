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
  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public void StopClosesReadyDictationWhileUndoStartupIgnoresCancellation(bool disposing) => LifecycleTestContext.Run(async context =>
  {
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    context.ReleaseOnTimeout(() => release.TrySetResult());
    FakeService hotkey = new();
    FakeService undo = new() { RegistrationGate = release.Task };
    FakeService service = new();
    DictationRuntime.RuntimeServices services = new(hotkey, undo, service, service, service, service, service, service, service);
    DictationRuntime runtime = new(service, service, null, new DictationHistoryChangeNotifier(), (_, _, _, _) => services);
    Task start = runtime.StartAsync();
    Task? stop = null;
    try
    {
      await undo.RegistrationEntered.Task;
      Xunit.Assert.Equal(1, hotkey.PressedSubscribers);
      stop = disposing ? runtime.DisposeAsync().AsTask() : runtime.StopAsync();
      Xunit.Assert.False(stop.IsCompleted);
      Xunit.Assert.Equal(0, hotkey.PressedSubscribers);
      hotkey.Press();
      Xunit.Assert.Equal(0, service.CaptureStarts);
      Xunit.Assert.Equal(0, service.Disposals);
    }
    finally
    {
      release.TrySetResult();
      try { await start; } catch (OperationCanceledException) { }
      if (stop is not null) await stop;
      await runtime.DisposeAsync();
    }
    Xunit.Assert.True(start.IsCanceled);
    Xunit.Assert.False(runtime.IsRunning);
  });

  [Xunit.Fact]
  public void PendingCaptureIsRetainedAfterCanceledStopWait() => LifecycleTestContext.Run(async context =>
  {
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    context.ReleaseOnTimeout(() => release.TrySetResult());
    FakeService hotkey = new();
    FakeService service = new() { CaptureGate = release.Task };
    DictationRuntime.RuntimeServices services = new(hotkey, hotkey, service, service, service, service, service, null, service);
    DictationRuntime runtime = new(service, service, null, new DictationHistoryChangeNotifier(), (_, _, _, _) => services);
    Task? dispose = null;
    try
    {
      await runtime.StartAsync();
      hotkey.Press();
      await service.CaptureEntered.Task;
      using CancellationTokenSource wait = new();
      Task stopWait = runtime.StopAsync(wait.Token);
      wait.Cancel();
      await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopWait);
      dispose = runtime.DisposeAsync().AsTask();
      Xunit.Assert.False(dispose.IsCompleted);
      Xunit.Assert.Equal(DictationSessionState.Recording, runtime.CurrentState);
      Xunit.Assert.Equal(0, service.Disposals);
      Xunit.Assert.True(service.CaptureToken.IsCancellationRequested);
    }
    finally
    {
      release.TrySetResult();
      await (dispose ?? runtime.DisposeAsync().AsTask());
    }
    Xunit.Assert.Equal(0, service.Transcriptions);
    Xunit.Assert.False(service.IsCapturing);
    Xunit.Assert.Equal(DictationSessionState.Idle, runtime.CurrentState);
  });

  [Xunit.Fact]
  public void DisposingUncooperativeStartupPreventsLateFactoryAdmission() => LifecycleTestContext.Run(async context =>
  {
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    context.ReleaseOnTimeout(() => release.TrySetResult());
    FakeService service = new() { LoadGate = release.Task };
    int factories = 0;
    DictationRuntime runtime = new(service, service, null, new DictationHistoryChangeNotifier(), (_, _, _, _) =>
    {
      factories++;
      return new(service, service, service, service, service, service, service, null, service);
    });
    Task start = runtime.StartAsync();
    Task? dispose = null;
    try
    {
      await service.LoadEntered.Task;
      dispose = runtime.DisposeAsync().AsTask();
      Xunit.Assert.False(dispose.IsCompleted);
      Xunit.Assert.Equal(0, factories);
    }
    finally
    {
      release.TrySetResult();
      await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
      await (dispose ?? runtime.DisposeAsync().AsTask());
    }
    Xunit.Assert.Equal(0, factories);
    Xunit.Assert.False(runtime.IsRunning);
  });

  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public void CanceledStopWaitRetainsResourcesThroughUndoDrain(bool unregisterFails) => LifecycleTestContext.Run(async context =>
  {
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    context.ReleaseOnTimeout(() => release.TrySetResult());
    FakeService hotkey = new();
    FakeService undo = new() { UnregisterFailure = unregisterFails ? new IOException("controlled unregister failure") : null };
    FakeService service = new() { UndoGate = release.Task };
    DictationRuntime.RuntimeServices services = new(hotkey, undo, service, service, service, service, service, service, service);
    DictationRuntime runtime = new(service, service, null, new DictationHistoryChangeNotifier(), (_, _, _, _) => services);
    Task? stop = null;
    Task? disposal = null;
    try
    {
      await runtime.StartAsync();
      undo.Press();
      await service.UndoEntered.Task;
      using CancellationTokenSource wait = new();
      if (unregisterFails) wait.Cancel(); // Already-canceled wait still requests owned cleanup.
      Task canceledWait = runtime.StopAsync(wait.Token);
      wait.Cancel();
      await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
      stop = runtime.StopAsync();
      Xunit.Assert.Same(stop, runtime.StopAsync());
      disposal = runtime.DisposeAsync().AsTask();
      Xunit.Assert.Same(disposal, runtime.DisposeAsync().AsTask());
      Xunit.Assert.False(disposal.IsCompleted);
      Xunit.Assert.False(stop.IsCompleted);
      Xunit.Assert.Equal(0, service.Disposals);
      Xunit.Assert.Equal(0, hotkey.PressedSubscribers);
      hotkey.Press();
      Xunit.Assert.Equal(0, service.CaptureStarts);
      await Xunit.Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.StartAsync());
    }
    finally
    {
      release.TrySetResult();
      if (stop is not null)
      {
        if (unregisterFails) await Xunit.Assert.ThrowsAsync<AggregateException>(() => stop);
        else await stop;
      }
      disposal ??= runtime.DisposeAsync().AsTask();
      if (unregisterFails) await Xunit.Assert.ThrowsAsync<AggregateException>(() => disposal);
      else await disposal;
    }
    Xunit.Assert.Equal(4, service.Disposals);
    Xunit.Assert.Equal(1, hotkey.Disposals);
    Xunit.Assert.Equal(1, undo.Disposals);
  });

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
    internal Exception? UnregisterFailure { get; init; }
    internal Task UndoGate { get; init; } = Task.CompletedTask;
    internal TaskCompletionSource UndoEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task CaptureGate { get; init; } = Task.CompletedTask;
    internal Task LoadGate { get; init; } = Task.CompletedTask;
    internal TaskCompletionSource CaptureEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource LoadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken CaptureToken { get; private set; }
    internal int Transcriptions { get; private set; }
    internal Task RegistrationGate { get; init; } = Task.CompletedTask;
    internal TaskCompletionSource RegistrationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Exception? DisposeFailure { get; init; }
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed { add => pressed += value; remove => pressed -= value; }
    public event EventHandler<HotkeyEventArgs>? HotkeyReleased { add => released += value; remove => released -= value; }
    internal void Press() => pressed?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));
    public async Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default)
    {
      RegistrationEntered.TrySetResult();
      await RegistrationGate.ConfigureAwait(false);
      return new HotkeyRegistrationResult(true, null);
    }
    public Task UnregisterAsync(CancellationToken cancellationToken = default) =>
      UnregisterFailure is null ? UnregisterGate : Task.FromException(UnregisterFailure);
    public ValueTask DisposeAsync()
    {
      Disposals++;
      return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
    }
    public bool IsCapturing { get; private set; }
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      CaptureStarts++;
      CaptureToken = cancellationToken;
      CaptureEntered.TrySetResult();
      await CaptureGate.ConfigureAwait(false);
      IsCapturing = true;
    }
    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = false;
      return Task.FromResult(new AudioCaptureResult([], 16000, TimeSpan.Zero));
    }
    public Task<TranscriptionResult> TranscribeAsync(AudioCaptureResult audio, string modelId, CancellationToken cancellationToken = default)
    {
      Transcriptions++;
      return Task.FromResult(new TranscriptionResult("", modelId, TimeSpan.Zero));
    }
    public Task<InsertionResult> InsertAsync(string text, InsertionMethod preferredMethod, bool restoreClipboard, CancellationToken cancellationToken = default) =>
      Task.FromResult(new InsertionResult(true, preferredMethod, null));
    public Task<TextTransformationResult> TransformAsync(TextTransformationRequest request, CancellationToken cancellationToken = default) =>
      throw new NotSupportedException();
    public Task ShowStateAsync(DictationSessionState state, string? message = null, TimeSpan? elapsed = null,
      OverlayDisplayOptions? display = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public async Task<UndoInsertionResult> UndoLastInsertionAsync(CancellationToken cancellationToken = default)
    {
      UndoEntered.TrySetResult();
      await UndoGate.ConfigureAwait(false);
      return new UndoInsertionResult(false, null);
    }
    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
      LoadEntered.TrySetResult();
      await LoadGate.ConfigureAwait(false);
      return AppSettings.Default;
    }
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}
