using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

public sealed class RuntimeWatchdog
{
  private readonly IRuntimeSupervisor runtime;
  private readonly IDiagnostics diagnostics;
  private readonly TimeSpan retryInterval;

  private DateTimeOffset nextRetryNotBeforeUtc = DateTimeOffset.MinValue;
  private int failedRecoveryAttempts;

  public RuntimeWatchdog(
    IRuntimeSupervisor runtime,
    IDiagnostics diagnostics,
    TimeSpan retryInterval)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.retryInterval = retryInterval > TimeSpan.Zero
      ? retryInterval
      : throw new ArgumentOutOfRangeException(nameof(retryInterval), "Retry interval must be positive.");
  }

  public DateTimeOffset NextRetryNotBeforeUtc => nextRetryNotBeforeUtc;

  public int FailedRecoveryAttempts => failedRecoveryAttempts;

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Watchdog should keep retrying and log failures instead of crashing the host process.")]
  public async Task TickAsync(CancellationToken cancellationToken = default)
  {
    if (runtime.IsRunning)
    {
      nextRetryNotBeforeUtc = DateTimeOffset.MinValue;
      failedRecoveryAttempts = 0;
      return;
    }

    DateTimeOffset now = DateTimeOffset.UtcNow;
    if (nextRetryNotBeforeUtc > now)
    {
      return;
    }

    try
    {
      diagnostics.Warning("Runtime watchdog detected a stopped runtime. Attempting restart.");
      await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
      diagnostics.Info("Runtime watchdog restart succeeded.");
      nextRetryNotBeforeUtc = DateTimeOffset.MinValue;
      failedRecoveryAttempts = 0;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      failedRecoveryAttempts++;
      nextRetryNotBeforeUtc = DateTimeOffset.UtcNow + retryInterval;
      diagnostics.Warning(
        $"Runtime watchdog restart failed (attempt {failedRecoveryAttempts}). Next retry at {nextRetryNotBeforeUtc:O}. Error: {ex.Message}");
    }
  }
}
