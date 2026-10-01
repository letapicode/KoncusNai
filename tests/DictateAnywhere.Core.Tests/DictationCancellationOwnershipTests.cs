using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Core.Services;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class DictationCancellationOwnershipTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public Task SuccessfulDictationStillCommitsWithThrowingReporting(bool queued) => RunAsync("completed", queued, async rig =>
  {
    rig.Services.ThrowOnInfo = true;
    rig.Services.ThrowOnError = true;
    await rig.StartPipelineAsync();
    Assert.Contains("insert", rig.Services.Tokens.Keys);
    Assert.Contains("history", rig.Services.Tokens.Keys);
    rig.Services.Release.TrySetResult();
    await rig.Coordinator.StopAsync();
    Assert.Equal(DictationSessionState.Idle, rig.Coordinator.CurrentState);
  });

  [Fact]
  public Task ReportingOnlyFailureDoesNotFaultHealthyStopOrRestart() => RunAsync("unused", true, async rig =>
  {
    await rig.Coordinator.StartAsync();
    rig.Services.ThrowOnInfo = true;
    await rig.Coordinator.StopAsync();
    await rig.Coordinator.StartAsync();
    rig.Hotkey.Press();
    Assert.True(rig.Services.IsCapturing);
  });

  [Fact]
  public Task ThrowingErrorSinkCannotSkipCaptureRecovery() => RunAsync("unused", false, async rig =>
  {
    await rig.Coordinator.StartAsync();
    rig.Services.CaptureFailure = new IOException("controlled capture failure");
    rig.Services.ThrowOnError = true;
    rig.Hotkey.Press(); // Every fake await completes inline; the callback has finished on return.
    Assert.False(rig.Services.IsCapturing);
    Assert.Equal(DictationSessionState.Idle, rig.Coordinator.CurrentState);
    Assert.Contains(rig.Services.CaptureFailure, rig.Services.ReportedErrors);
  });
  [Theory]
  [InlineData("recording", false)]
  [InlineData("capture", true)]
  [InlineData("transcribing", false)]
  [InlineData("capture-stop", true)]
  [InlineData("transcribe", false)]
  [InlineData("transform", true)]
  [InlineData("insert", false)]
  [InlineData("history", true)]
  public Task StopDoesNotAdmitTheNextStageAfterUncooperativeSuccess(string stage, bool queued) => RunAsync(stage, queued, async rig =>
  {
    await rig.StartPipelineAsync().ConfigureAwait(false);
    CancellationToken accepted = rig.Services.Tokens[stage];
    Task stop = rig.Track(rig.Coordinator.StopAsync());
    Assert.True(accepted.IsCancellationRequested);
    Assert.False(stop.IsCompleted);
    rig.Services.Release.TrySetResult();
    await stop.ConfigureAwait(false);
    string[] order = ["recording", "capture", "transcribing", "capture-stop", "transcribe", "transform", "insert", "history", "completed"];
    foreach (string next in order.Skip(Array.IndexOf(order, stage) + 1))
      Assert.DoesNotContain(next, rig.Services.Tokens.Keys);
    Assert.Equal(DictationSessionState.Idle, rig.Coordinator.CurrentState);
  });

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public Task LateProviderFaultDoesNotReviveErrorOrSuccessAndSinkFaultStillDrains(bool canceledProvider) => RunAsync("transcribe", true, async rig =>
  {
    await rig.StartPipelineAsync().ConfigureAwait(false);
    rig.Services.ProviderFailure = canceledProvider
      ? new OperationCanceledException("controlled canceled provider")
      : new IOException("controlled late provider fault");
    rig.Services.ThrowOnError = true;
    Task stop = rig.Track(rig.Coordinator.StopAsync());
    rig.Services.Release.TrySetResult();
    await stop.ConfigureAwait(false);
    Assert.DoesNotContain("error", rig.Services.Tokens.Keys);
    Assert.DoesNotContain("history", rig.Services.Tokens.Keys);
    Assert.Equal(canceledProvider ? 0 : 1, rig.Services.ErrorReports);
  });

  [Fact]
  public Task CanceledWaitDoesNotOrphanStopAndDisposeDrainsQueuedRelease() => RunAsync("capture", true, async rig =>
  {
    await rig.StartPipelineAsync().ConfigureAwait(false);
    rig.Hotkey.Release(); // Queued behind recording startup's signal semaphore.
    using CancellationTokenSource wait = new();
    Task first = rig.Track(rig.Coordinator.StopAsync(wait.Token));
    wait.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first).ConfigureAwait(false);
    Task stop = rig.Track(rig.Coordinator.StopAsync());
    Task dispose = rig.Track(rig.Coordinator.DisposeAsync().AsTask());
    Assert.Same(dispose, rig.Coordinator.DisposeAsync().AsTask());
    Assert.False(stop.IsCompleted);
    Assert.False(dispose.IsCompleted);
    // The canceled source still supports late registration while its provider owns it.
    bool notified = false;
    using CancellationTokenRegistration registration = rig.Services.Tokens["capture"].Register(() => notified = true);
    Assert.True(notified);
    rig.Services.Release.TrySetResult();
    await Task.WhenAll(stop, dispose).ConfigureAwait(false);
    Assert.DoesNotContain("transcribe", rig.Services.Tokens.Keys);
    Assert.Contains("capture-discard", rig.Services.Tokens.Keys);
  });

  [Fact]
  public Task ReentrantThrowingCancellationStillDrainsAndAttemptsIndependentCleanup() => RunAsync("transcribe", false, async rig =>
  {
    await rig.StartPipelineAsync().ConfigureAwait(false);
    Task? reentrant = null;
    using CancellationTokenRegistration registration = rig.Services.Tokens["transcribe"].Register(() =>
    {
      reentrant = rig.Coordinator.StopAsync(); // Request, never await the operation containing this callback.
      throw new InvalidOperationException("controlled cancellation failure");
    });
    rig.ExpectedCleanupFailure = true;
    Task stop = rig.Track(rig.Coordinator.StopAsync());
    Assert.Same(stop, reentrant);
    Assert.False(stop.IsCompleted);
    rig.Services.Release.TrySetResult();
    await Assert.ThrowsAsync<AggregateException>(() => stop).ConfigureAwait(false);
    Assert.Equal(1, rig.Hotkey.Unregistrations);
    Assert.Equal(1, rig.Services.Hides);
    await Assert.ThrowsAsync<AggregateException>(() => rig.Coordinator.DisposeAsync().AsTask()).ConfigureAwait(false);
  });

  [Fact]
  public Task NormalReleaseKeepsOneTokenAcrossAllStages() => RunAsync("completed", true, async rig =>
  {
    await rig.StartPipelineAsync().ConfigureAwait(false);
    CancellationToken token = rig.Services.Tokens["capture"];
    foreach (string stage in new[] { "recording", "transcribing", "capture-stop", "transcribe", "transform", "insert", "history", "completed" })
      Assert.Equal(token, rig.Services.Tokens[stage]);
    Assert.False(token.IsCancellationRequested);
    Task stop = rig.Track(rig.Coordinator.StopAsync());
    rig.Services.Release.TrySetResult();
    await stop.ConfigureAwait(false);
  });

  [Fact]
  public Task SavedOldCallbackCannotAdoptRestartedRun() => RunAsync("transcribe", true, async rig =>
  {
    await rig.StartPipelineAsync().ConfigureAwait(false);
    Action oldCallback = rig.Hotkey.SnapshotPress();
    CancellationToken oldToken = rig.Services.Tokens["transcribe"];
    Task stop = rig.Track(rig.Coordinator.StopAsync());
    Task restart = rig.Track(rig.Coordinator.StartAsync());
    Assert.False(restart.IsCompleted);
    rig.Services.Release.TrySetResult();
    await Task.WhenAll(stop, restart).ConfigureAwait(false);
    oldCallback();
    Assert.Equal(1, rig.Services.Captures);
    rig.Hotkey.Press();
    Assert.Equal(2, rig.Services.Captures);
    Assert.NotEqual(oldToken, rig.Services.Tokens["capture"]);
    Assert.False(rig.Services.Tokens["capture"].IsCancellationRequested);
  });

  [Fact]
  public Task RepeatedStopAndSavedCallbackRetainTheAcceptedOwner() => RunAsync("transcribe", true, async rig =>
  {
    await rig.StartPipelineAsync().ConfigureAwait(false);
    Action oldCallback = rig.Hotkey.SnapshotPress();
    Task first = rig.Track(rig.Coordinator.StopAsync());
    Task second = rig.Track(rig.Coordinator.StopAsync());
    Assert.False(second.IsCompleted);
    oldCallback();
    Assert.Equal(1, rig.Services.Captures);
    rig.Services.Release.TrySetResult();
    await Task.WhenAll(first, second).ConfigureAwait(false);
    Assert.Equal(0, rig.Hotkey.Subscribers);
  });

  [Fact]
  public Task StopDuringUncooperativeStartupDoesNotRegisterLateHotkeys() => RunAsync("load", true, async rig =>
  {
    Task start = rig.Track(rig.Coordinator.StartAsync());
    await rig.Services.Entered.Task.ConfigureAwait(false);
    Task stop = rig.Track(rig.Coordinator.StopAsync());
    Assert.False(stop.IsCompleted);
    rig.Services.Release.TrySetResult();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start).ConfigureAwait(false);
    await stop.ConfigureAwait(false);
    Assert.Equal(0, rig.Hotkey.Subscribers);
  });

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Observe the same completed test fault during recovery without replacing its original assertion.")]
  private static async Task RunAsync(string stage, bool queued, Func<Rig, Task> body)
  {
    Rig rig = new(stage, queued);
    Task test = Task.Run(() => body(rig)); // No shared xUnit context inside behavioral deadlines.
    try { await test.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
    finally
    {
      rig.Services.Release.TrySetResult();
      try { await test.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
      catch (Exception) when (test.IsCompleted) { /* Original test failure remains the result. */ }
      foreach (Task tracked in rig.Tasks)
      {
        try { await tracked.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (AggregateException) when (rig.ExpectedCleanupFailure) { }
      }
      try { await rig.Coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
      catch (AggregateException) when (rig.ExpectedCleanupFailure) { }
    }
  }

  private sealed class Rig
  {
    internal Services Services { get; }
    internal Hotkey Hotkey { get; } = new();
    internal DictationPipelineCoordinator Coordinator { get; }
    internal List<Task> Tasks { get; } = [];
    internal bool ExpectedCleanupFailure { get; set; }
    internal Task Track(Task task) { Tasks.Add(task); return task; }
    internal Rig(string stage, bool queued)
    {
      Services = new(stage, queued);
      Coordinator = new(Hotkey, Services, Services, Services, Services, Services, Services, Services, Services);
    }
    internal async Task StartPipelineAsync()
    {
      await Coordinator.StartAsync().ConfigureAwait(false);
      Hotkey.Press();
      if (Services.BlockedStage is not ("recording" or "capture"))
      {
        await Services.Captured.Task.ConfigureAwait(false);
        Hotkey.Release();
      }
      await Services.Entered.Task.ConfigureAwait(false);
    }
  }

  private sealed class Hotkey : IHotkeyService
  {
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;
    public event EventHandler<HotkeyEventArgs>? HotkeyReleased;
    internal int Subscribers => HotkeyPressed?.GetInvocationList().Length ?? 0;
    internal int Unregistrations { get; private set; }
    internal void Press() => HotkeyPressed?.Invoke(this, new(DateTimeOffset.UtcNow));
    internal void Release() => HotkeyReleased?.Invoke(this, new(DateTimeOffset.UtcNow));
    internal Action SnapshotPress()
    {
      EventHandler<HotkeyEventArgs>? snapshot = HotkeyPressed;
      return () => snapshot?.Invoke(this, new(DateTimeOffset.UtcNow));
    }
    public Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default) =>
      Task.FromResult(new HotkeyRegistrationResult(true, null));
    public Task UnregisterAsync(CancellationToken cancellationToken = default) { Unregistrations++; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class Services : IAudioCaptureService, ITranscriptionService, ITextInsertionService,
    ITextTransformationService, IOverlayService, ISettingsStore, IDiagnostics, IDictationHistoryRecorder
  {
    internal string BlockedStage { get; }
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; }
    internal Dictionary<string, CancellationToken> Tokens { get; } = [];
    internal int Captures { get; private set; }
    internal int Hides { get; private set; }
    internal Exception? ProviderFailure { get; set; }
    internal bool ThrowOnError { get; set; }
    internal bool ThrowOnInfo { get; set; }
    internal Exception? CaptureFailure { get; set; }
    internal List<Exception?> ReportedErrors { get; } = [];
    internal int ErrorReports { get; private set; }
    public bool IsCapturing { get; private set; }
    internal Services(string stage, bool queued)
    {
      BlockedStage = stage;
      Release = new(queued ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);
    }
    private async Task StepAsync(string stage, CancellationToken token)
    {
      Tokens[stage] = token;
      if (stage != BlockedStage) return;
      Entered.TrySetResult();
      await Release.Task.ConfigureAwait(false); // Intentionally ignores cancellation.
    }
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      Captures++;
      await StepAsync("capture", cancellationToken).ConfigureAwait(false);
      IsCapturing = true;
      if (CaptureFailure is not null) throw CaptureFailure;
      Captured.TrySetResult();
    }
    public async Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
    {
      await StepAsync(cancellationToken.CanBeCanceled ? "capture-stop" : "capture-discard", cancellationToken).ConfigureAwait(false);
      IsCapturing = false;
      return new([1, 0], 16000, TimeSpan.FromMilliseconds(1));
    }
    public async Task<TranscriptionResult> TranscribeAsync(AudioCaptureResult audio, string modelId, CancellationToken cancellationToken = default)
    {
      await StepAsync("transcribe", cancellationToken).ConfigureAwait(false);
      if (ProviderFailure is not null) throw ProviderFailure;
      return new("test", modelId, TimeSpan.Zero);
    }
    public async Task<TextTransformationResult> TransformAsync(TextTransformationRequest request, CancellationToken cancellationToken = default)
    {
      await StepAsync("transform", cancellationToken).ConfigureAwait(false);
      return new(request.Text);
    }
    public async Task<InsertionResult> InsertAsync(string text, InsertionMethod preferredMethod, bool restoreClipboard, CancellationToken cancellationToken = default)
    {
      await StepAsync("insert", cancellationToken).ConfigureAwait(false);
      return InsertionResult.Verified(preferredMethod);
    }
    public Task ShowStateAsync(DictationSessionState state, string? message = null, TimeSpan? elapsed = null,
      OverlayDisplayOptions? display = null, CancellationToken cancellationToken = default) =>
      StepAsync(state switch
      {
        DictationSessionState.Recording => "recording",
        DictationSessionState.Transcribing => "transcribing",
        DictationSessionState.Completed => "completed",
        _ => "error"
      }, cancellationToken);
    public Task HideAsync(CancellationToken cancellationToken = default) { Hides++; return Task.CompletedTask; }
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
      await StepAsync("load", cancellationToken).ConfigureAwait(false);
      return AppSettings.Default with { RecordingMode = RecordingMode.HoldToTalk };
    }
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) => StepAsync("history", cancellationToken);
    public void Info(string message) { if (ThrowOnInfo) throw new IOException("controlled info sink failure"); }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null)
    {
      ErrorReports++;
      ReportedErrors.Add(exception);
      if (ThrowOnError) throw new IOException("controlled diagnostic sink fault");
    }
  }
}
