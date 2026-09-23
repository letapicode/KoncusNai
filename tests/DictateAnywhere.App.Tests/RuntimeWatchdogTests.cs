using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class RuntimeWatchdogTests
{
  [Xunit.Fact]
  public async Task TickAsync_RuntimeAlreadyRunning_DoesNotStartAgain()
  {
    FakeRuntimeSupervisor runtime = new()
    {
      IsRunning = true,
    };
    FakeDiagnostics diagnostics = new();
    RuntimeWatchdog watchdog = new(runtime, diagnostics, TimeSpan.FromSeconds(1));

    await watchdog.TickAsync();

    Xunit.Assert.Equal(0, runtime.StartCallCount);
    Xunit.Assert.Empty(diagnostics.WarningMessages);
  }

  [Xunit.Fact]
  public async Task TickAsync_RuntimeStopped_StartsAndClearsFailureCounters()
  {
    FakeRuntimeSupervisor runtime = new();
    FakeDiagnostics diagnostics = new();
    RuntimeWatchdog watchdog = new(runtime, diagnostics, TimeSpan.FromSeconds(1));

    await watchdog.TickAsync();

    Xunit.Assert.Equal(1, runtime.StartCallCount);
    Xunit.Assert.True(runtime.IsRunning);
    Xunit.Assert.Equal(0, watchdog.FailedRecoveryAttempts);
    Xunit.Assert.Contains(diagnostics.InfoMessages, message => message.Contains("restart succeeded", StringComparison.OrdinalIgnoreCase));
  }

  [Xunit.Fact]
  public async Task TickAsync_WhenRecoveryFails_BacksOffBeforeRetry()
  {
    FakeRuntimeSupervisor runtime = new()
    {
      StartException = new InvalidOperationException("hotkey conflict"),
    };
    FakeDiagnostics diagnostics = new();
    RuntimeWatchdog watchdog = new(runtime, diagnostics, TimeSpan.FromMinutes(5));

    await watchdog.TickAsync();
    await watchdog.TickAsync();

    Xunit.Assert.Equal(1, runtime.StartCallCount);
    Xunit.Assert.Equal(1, watchdog.FailedRecoveryAttempts);
    Xunit.Assert.True(watchdog.NextRetryNotBeforeUtc > DateTimeOffset.UtcNow);
    Xunit.Assert.Contains(diagnostics.WarningMessages, message => message.Contains("failed", StringComparison.OrdinalIgnoreCase));
  }

  private sealed class FakeRuntimeSupervisor : IRuntimeSupervisor
  {
    public bool IsRunning { get; set; }

    public int StartCallCount { get; private set; }

    public Exception? StartException { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      StartCallCount++;
      if (StartException is not null)
      {
        throw StartException;
      }

      IsRunning = true;
      return Task.CompletedTask;
    }
  }

  private sealed class FakeDiagnostics : IDiagnostics
  {
    public List<string> InfoMessages { get; } = new();

    public List<string> WarningMessages { get; } = new();

    public void Info(string message)
    {
      InfoMessages.Add(message);
    }

    public void Warning(string message)
    {
      WarningMessages.Add(message);
    }

    public void Error(string message, Exception? exception = null)
    {
      WarningMessages.Add(message);
    }
  }
}
