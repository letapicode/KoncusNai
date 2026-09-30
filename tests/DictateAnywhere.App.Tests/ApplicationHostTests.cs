using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "Each ApplicationHost owns and disposes the injected fake runtime and readiness sessions.")]
public sealed class ApplicationHostTests
{
  [Xunit.Fact]
  public async Task ApplyRuntimeSettingsAsync_EquivalentRuntimeSettings_DoesNotRestart()
  {
    FakeRuntimeSession runtime = new();
    FakeReadinessSession readiness = new();
    await using ApplicationHost host = new(runtime, readiness, new RecordingDiagnostics());

    RuntimeSettingsApplyResult first = await host.ApplyRuntimeSettingsAsync(AppSettings.Default);
    RuntimeSettingsApplyResult second = await host.ApplyRuntimeSettingsAsync(
      AppSettings.Default with { ThemePreference = AppThemePreference.Light });

    Xunit.Assert.True(first.IsRunning);
    Xunit.Assert.True(second.IsRunning);
    Xunit.Assert.Equal(1, runtime.StartCount);
    Xunit.Assert.Equal(0, runtime.RestartCount);
    Xunit.Assert.Equal(1, readiness.RefreshCount);
  }

  [Xunit.Fact]
  public async Task ApplyRuntimeSettingsAsync_BusyRuntime_DefersAndAppliesWhenIdle()
  {
    FakeRuntimeSession runtime = new();
    RecordingDiagnostics diagnostics = new();
    await using ApplicationHost host = new(runtime, new FakeReadinessSession(), diagnostics);
    await host.ApplyRuntimeSettingsAsync(AppSettings.Default);
    runtime.CurrentState = DictationSessionState.Recording;
    AppSettings updated = AppSettings.Default with { OverlayEnabled = false };

    RuntimeSettingsApplyResult deferred = await host.ApplyRuntimeSettingsAsync(updated);

    Xunit.Assert.True(deferred.Deferred);
    Xunit.Assert.True(host.HasPendingSettings);
    Xunit.Assert.Equal(0, runtime.RestartCount);
    Xunit.Assert.Contains(diagnostics.InfoMessages, message => message.Contains("deferred", StringComparison.OrdinalIgnoreCase));

    runtime.CurrentState = DictationSessionState.Idle;
    RuntimeSettingsApplyResult? applied = await host.TryApplyPendingSettingsAsync();

    Xunit.Assert.NotNull(applied);
    Xunit.Assert.True(applied!.IsRunning);
    Xunit.Assert.False(applied.Deferred);
    Xunit.Assert.False(host.HasPendingSettings);
    Xunit.Assert.Equal(1, runtime.RestartCount);
  }

  [Xunit.Fact]
  public async Task ApplyRuntimeSettingsAsync_StartFailure_PreservesPendingSettingsForRetry()
  {
    FakeRuntimeSession runtime = new()
    {
      StartFailure = new IOException("simulated startup failure"),
    };
    await using ApplicationHost host = new(runtime, new FakeReadinessSession(), new RecordingDiagnostics());

    RuntimeSettingsApplyResult failed = await host.ApplyRuntimeSettingsAsync(AppSettings.Default);

    Xunit.Assert.False(failed.IsRunning);
    Xunit.Assert.IsType<IOException>(failed.Failure);
    Xunit.Assert.True(host.HasPendingSettings);

    runtime.StartFailure = null;
    RuntimeSettingsApplyResult? recovered = await host.TryApplyPendingSettingsAsync();

    Xunit.Assert.NotNull(recovered);
    Xunit.Assert.True(recovered!.IsRunning);
    Xunit.Assert.False(host.HasPendingSettings);
    Xunit.Assert.Equal(2, runtime.StartCount);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_DetachesReadinessEventAndDisposesOwnedSessionsInOrder()
  {
    List<string> disposalOrder = new();
    FakeRuntimeSession runtime = new(disposalOrder);
    FakeReadinessSession readiness = new(disposalOrder);
    ApplicationHost host = new(runtime, readiness, new RecordingDiagnostics());
    int eventCount = 0;
    host.ModelReadinessChanged += (_, _) => eventCount++;
    readiness.PublishSnapshot();

    await host.DisposeAsync();
    readiness.PublishSnapshot();

    Xunit.Assert.Equal(1, eventCount);
    Xunit.Assert.Equal(["runtime", "readiness"], disposalOrder);
  }

  [Xunit.Fact]
  public async Task ShutdownCancelsActiveAndQueuedReadinessAndDoesNotStartRuntimeAfterRelease()
  {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    FakeRuntimeSession runtime = new();
    FakeReadinessSession readiness = new()
    {
      Refresh = async _ => { started.TrySetResult(); await release.Task; },
    };
    ApplicationHost host = new(runtime, readiness, new RecordingDiagnostics());
    Task active = host.ApplyRuntimeSettingsAsync(AppSettings.Default);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Task queued = host.ApplyRuntimeSettingsAsync(AppSettings.Default);
    host.BeginShutdown();
    Xunit.Assert.Equal(1, runtime.StopCount);
    Task firstDisposal = host.DisposeAsync().AsTask();
    Task secondDisposal = host.DisposeAsync().AsTask();
    Xunit.Assert.Same(firstDisposal, secondDisposal);
    Xunit.Assert.False(firstDisposal.IsCompleted);
    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
    release.SetResult();
    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
    await firstDisposal.WaitAsync(TimeSpan.FromSeconds(2));
    Xunit.Assert.Equal(0, runtime.StartCount);
    Xunit.Assert.Equal(1, runtime.DisposeCount);
    Xunit.Assert.Equal(1, readiness.DisposeCount);
    await Xunit.Assert.ThrowsAsync<ObjectDisposedException>(() => host.StartAsync());
  }

  [Xunit.Fact]
  public async Task DirectDisposalClosesAdmissionBeforeReturning()
  {
    FakeRuntimeSession runtime = new();
    ApplicationHost host = new(runtime, new FakeReadinessSession(), new RecordingDiagnostics());
    Task disposal = host.DisposeAsync().AsTask();
    Xunit.Assert.Equal(1, runtime.StopCount);
    await Xunit.Assert.ThrowsAsync<ObjectDisposedException>(() => host.StartAsync());
    await disposal;
    Xunit.Assert.Equal(0, runtime.StartCount);
  }

  [Xunit.Fact]
  public async Task RuntimeDisposalFailureDoesNotSkipReadinessDisposal()
  {
    FakeRuntimeSession runtime = new() { DisposeFailure = new IOException("Worker exit failed") };
    FakeReadinessSession readiness = new();
    ApplicationHost host = new(runtime, readiness, new RecordingDiagnostics());
    await Xunit.Assert.ThrowsAsync<AggregateException>(() => host.DisposeAsync().AsTask());
    Xunit.Assert.Equal(1, runtime.DisposeCount);
    Xunit.Assert.Equal(1, readiness.DisposeCount);
  }

  private sealed class FakeRuntimeSession : IApplicationRuntimeSession
  {
    private readonly List<string>? disposalOrder;

    public FakeRuntimeSession(List<string>? disposalOrder = null)
    {
      this.disposalOrder = disposalOrder;
    }

    public bool IsRunning { get; private set; }
    public DictationSessionState CurrentState { get; set; } = DictationSessionState.Idle;
    public int StartCount { get; private set; }
    public int RestartCount { get; private set; }
    public Exception? StartFailure { get; set; }
    public Exception? DisposeFailure { get; set; }
    public int DisposeCount { get; private set; }
    public int StopCount { get; private set; }
    public RuntimeStartupNotice? StartupNotice { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      StartCount++;
      if (StartFailure is not null)
      {
        throw StartFailure;
      }

      IsRunning = true;
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      StopCount++;
      IsRunning = false;
      return Task.CompletedTask;
    }

    public Task<bool> TryRestartWhenIdleAsync(CancellationToken cancellationToken = default)
    {
      RestartCount++;
      IsRunning = true;
      return Task.FromResult(true);
    }

    public RuntimeStartupNotice? ConsumeStartupNotice()
    {
      RuntimeStartupNotice? result = StartupNotice;
      StartupNotice = null;
      return result;
    }

    public ValueTask DisposeAsync()
    {
      DisposeCount++;
      disposalOrder?.Add("runtime");
      return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
    }
  }

  private sealed class FakeReadinessSession : IModelReadinessSession
  {
    private readonly List<string>? disposalOrder;

    public FakeReadinessSession(List<string>? disposalOrder = null)
    {
      this.disposalOrder = disposalOrder;
    }

    public event EventHandler<ModelReadinessSnapshot>? SnapshotChanged;
    public ModelReadinessSnapshot CurrentSnapshot { get; private set; } = ModelReadinessSnapshot.Empty;
    public int RefreshCount { get; private set; }
    public int DisposeCount { get; private set; }
    public Func<CancellationToken, Task>? Refresh { get; init; }

    public Task RefreshAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
      RefreshCount++;
      return Refresh?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    public ITranscriptionService CreateTranscriptionService(AppSettings settings, IDiagnostics diagnostics) =>
      throw new NotSupportedException("Service creation is outside these lifecycle tests.");

    public void PublishSnapshot()
    {
      CurrentSnapshot = new ModelReadinessSnapshot(Array.Empty<ModelReadinessEntry>(), DateTimeOffset.UtcNow);
      SnapshotChanged?.Invoke(this, CurrentSnapshot);
    }

    public ValueTask DisposeAsync()
    {
      DisposeCount++;
      disposalOrder?.Add("readiness");
      return ValueTask.CompletedTask;
    }
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<string> InfoMessages { get; } = new();

    public void Info(string message) => InfoMessages.Add(message);
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}
