using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class UndoHotkeyCoordinatorTests
{
  [Xunit.Fact]
  public async Task DisposeAsync_WaitsForActiveUndoAndRejectsOverlap()
  {
    await using FakeHotkeyService hotkeyService = new();
    DeferredUndoInsertionService undoService = new();
    RecordingDiagnostics diagnostics = new();
    UndoHotkeyCoordinator coordinator = new(
      hotkeyService,
      undoService,
      diagnostics,
      new HotkeyBinding(HotkeyModifiers.Control, 0x5A));

    await coordinator.StartAsync();
    hotkeyService.RaisePressed();
    await undoService.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

    hotkeyService.RaisePressed();
    Xunit.Assert.Equal(1, undoService.CallCount);

    Task disposal = coordinator.DisposeAsync().AsTask();
    Xunit.Assert.False(disposal.IsCompleted);

    undoService.Completion.SetResult(new UndoInsertionResult(true, null));
    await disposal.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.Equal(1, hotkeyService.UnregisterCallCount);
    Xunit.Assert.Contains("succeeded", diagnostics.InfoMessages[0], StringComparison.OrdinalIgnoreCase);
  }

  private sealed class DeferredUndoInsertionService : IUndoInsertionService
  {
    public TaskCompletionSource<bool> Started { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<UndoInsertionResult> Completion { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public Task<UndoInsertionResult> UndoLastInsertionAsync(CancellationToken cancellationToken = default)
    {
      CallCount++;
      Started.TrySetResult(true);
      return Completion.Task;
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

    public int UnregisterCallCount { get; private set; }

    public Task<HotkeyRegistrationResult> RegisterAsync(
      HotkeyBinding binding,
      CancellationToken cancellationToken = default)
    {
      return Task.FromResult(new HotkeyRegistrationResult(true, null));
    }

    public Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
      UnregisterCallCount++;
      return Task.CompletedTask;
    }

    public void RaisePressed()
    {
      HotkeyPressed?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));
    }

    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<string> InfoMessages { get; } = new();

    public void Info(string message)
    {
      InfoMessages.Add(message);
    }

    public void Warning(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
  }
}
