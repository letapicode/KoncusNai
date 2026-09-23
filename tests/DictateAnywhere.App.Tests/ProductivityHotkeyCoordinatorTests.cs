using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class ProductivityHotkeyCoordinatorTests
{
  [Xunit.Fact]
  public async Task StopAsync_RejectsOverlapAndWaitsForTheAcceptedAction()
  {
    await using FakeHotkeyService service = new();
    RecordingDiagnostics diagnostics = new();
    await using ProductivityHotkeyCoordinator coordinator = new(diagnostics, _ => service);
    TaskCompletionSource<bool> actionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Complete the controlled action inline so this synchronization test does not
    // depend on thread-pool availability while the full parallel suite is busy.
    TaskCompletionSource<bool> actionCompletion = new();
    int actionCount = 0;

    await coordinator.RestartAsync(
      CreateSettings(),
      async () =>
      {
        actionCount++;
        actionStarted.TrySetResult(true);
        await actionCompletion.Task;
      });

    service.RaisePressed();
    await actionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    service.RaisePressed();
    Xunit.Assert.Equal(1, actionCount);

    Task stop = coordinator.StopAsync();
    Xunit.Assert.False(stop.IsCompleted);

    actionCompletion.SetResult(true);
    await stop.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.Equal(1, service.UnregisterCallCount);
    Xunit.Assert.Equal(1, service.DisposeCallCount);
  }

  [Xunit.Fact]
  public async Task ActionFailure_IsObservedAndReportedWithoutFaultingShutdown()
  {
    await using FakeHotkeyService service = new();
    RecordingDiagnostics diagnostics = new();
    await using ProductivityHotkeyCoordinator coordinator = new(diagnostics, _ => service);

    await coordinator.RestartAsync(
      CreateSettings(),
      () => Task.FromException(new InvalidOperationException("retry unavailable")));

    service.RaisePressed();
    string warning = await diagnostics.WarningReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await coordinator.StopAsync();

    Xunit.Assert.Contains("retry last dictation", warning, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("retry unavailable", warning, StringComparison.Ordinal);
  }

  private static AppSettings CreateSettings() => AppSettings.Default with
  {
    RetryLastDictationHotkey = new HotkeyBinding(
      HotkeyModifiers.Alt | HotkeyModifiers.Shift,
      0x52),
  };

  private sealed class FakeHotkeyService : IHotkeyService
  {
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    public event EventHandler<HotkeyEventArgs>? HotkeyReleased
    {
      add { }
      remove { }
    }

    public int UnregisterCallCount { get; private set; }

    public int DisposeCallCount { get; private set; }

    public Task<HotkeyRegistrationResult> RegisterAsync(
      HotkeyBinding binding,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(new HotkeyRegistrationResult(true, null));

    public Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
      UnregisterCallCount++;
      return Task.CompletedTask;
    }

    public void RaisePressed() =>
      HotkeyPressed?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));

    public ValueTask DisposeAsync()
    {
      DisposeCallCount++;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public TaskCompletionSource<string> WarningReceived { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> InfoMessages { get; } = new();

    public void Info(string message) => InfoMessages.Add(message);

    public void Warning(string message) => WarningReceived.TrySetResult(message);

    public void Error(string message, Exception? exception = null)
    {
    }
  }
}
