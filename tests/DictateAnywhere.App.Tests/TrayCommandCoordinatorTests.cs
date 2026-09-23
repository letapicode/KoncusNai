using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Tray;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using Forms = System.Windows.Forms;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "Each TrayCommandCoordinator owns and disposes the injected fake tray host.")]
public sealed class TrayCommandCoordinatorTests
{
  [Xunit.Fact]
  public async Task RunAsync_SerializesOverlappingCommands()
  {
    FakeTrayIconHost host = new();
    TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await using TrayCommandCoordinator coordinator = CreateCoordinator(host);

    Task first = coordinator.RunAsync(async () =>
    {
      firstStarted.SetResult();
      await releaseFirst.Task;
    });
    await firstStarted.Task;

    Task second = coordinator.RunAsync(() =>
    {
      secondStarted.SetResult();
      return Task.CompletedTask;
    });

    Xunit.Assert.False(secondStarted.Task.IsCompleted);
    releaseFirst.SetResult();
    await Task.WhenAll(first, second);
    Xunit.Assert.True(secondStarted.Task.IsCompletedSuccessfully);
  }

  [Xunit.Fact]
  public async Task RunAsync_ReportsUnexpectedFailure_AndReleasesCommandSlot()
  {
    FakeTrayIconHost host = new();
    Exception? reported = null;
    TrayCommandHandlers handlers = CreateHandlers((_, _, exception) => reported = exception);
    await using TrayCommandCoordinator coordinator = new(host, handlers, () => DictationSessionState.Idle);

    await coordinator.RunAsync(() => throw new ArithmeticException("boom"));
    bool followUpRan = false;
    await coordinator.RunAsync(() =>
    {
      followUpRan = true;
      return Task.CompletedTask;
    });

    Xunit.Assert.IsType<ArithmeticException>(reported);
    Xunit.Assert.True(followUpRan);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_DetachesEventsAndDisposesTrayHost()
  {
    FakeTrayIconHost host = new();
    int commandCount = 0;
    TrayCommandHandlers handlers = CreateHandlers((_, _, _) => { }, () =>
    {
      commandCount++;
      return Task.CompletedTask;
    });
    TrayCommandCoordinator coordinator = new(host, handlers, () => DictationSessionState.Idle);

    host.RaiseOpenSettings();
    await WaitUntilAsync(() => commandCount == 1);
    await coordinator.DisposeAsync();
    host.RaiseOpenSettings();

    await Task.Delay(20);
    Xunit.Assert.Equal(1, commandCount);
    Xunit.Assert.True(host.Disposed);
  }

  private static TrayCommandCoordinator CreateCoordinator(FakeTrayIconHost host) => new(
    host,
    CreateHandlers((_, _, _) => { }),
    () => DictationSessionState.Idle);

  private static TrayCommandHandlers CreateHandlers(
    Action<string, string, Exception> reportFailure,
    Func<Task>? openSettings = null) => new(
    openSettings ?? (() => Task.CompletedTask),
    () => Task.CompletedTask,
    () => Task.CompletedTask,
    _ => Task.CompletedTask,
    () => Task.CompletedTask,
    _ => Task.CompletedTask,
    () => Task.CompletedTask,
    () => { },
    reportFailure);

  private static async Task WaitUntilAsync(Func<bool> condition)
  {
    DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
    while (!condition() && DateTimeOffset.UtcNow < deadline)
    {
      await Task.Delay(10);
    }

    Xunit.Assert.True(condition());
  }

  private sealed class FakeTrayIconHost : ITrayIconHost
  {
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenWorkbenchRequested { add { } remove { } }
    public event EventHandler? OpenHistoryRequested { add { } remove { } }
    public event EventHandler? QuitRequested { add { } remove { } }
    public event EventHandler<bool>? StartupToggleRequested { add { } remove { } }
    public event EventHandler? RetryLastDictationRequested { add { } remove { } }
    public event EventHandler<TranscriptionModelSelection>? QuickModelSwitchRequested { add { } remove { } }
    public event EventHandler? ExportDiagnosticsRequested { add { } remove { } }

    public bool Disposed { get; private set; }

    public void RaiseOpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    public void SetStatus(DictationSessionState state) { }
    public void SetModelReadiness(ModelReadinessSnapshot snapshot) { }
    public void SetStartOnLoginEnabled(bool enabled) { }
    public void SetModelMenu(IReadOnlyList<ModelInfo> models, TranscriptionModelSelection activeSelection) { }
    public void ShowNotification(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info, int timeoutMilliseconds = 5000) { }
    public void Dispose() => Disposed = true;
  }
}
