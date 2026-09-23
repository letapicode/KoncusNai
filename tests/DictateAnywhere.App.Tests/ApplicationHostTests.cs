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
      disposalOrder?.Add("runtime");
      return ValueTask.CompletedTask;
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

    public Task RefreshAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
      RefreshCount++;
      return Task.CompletedTask;
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
