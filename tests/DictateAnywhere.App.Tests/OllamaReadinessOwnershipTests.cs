using DictateAnywhere.App.Runtime;
using Xunit;
using Child = DictateAnywhere.App.Tests.OllamaProcessOwnershipTests.Child;

namespace DictateAnywhere.App.Tests;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Each isolated owner is explicitly stopped and its shared cleanup is drained, including expected faults.")]
public sealed class OllamaReadinessOwnershipTests
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ExistingOrNewlyObservedListenerNeverLaunchesOrApprovesOwnedProcess(bool alreadyRunning)
  {
    await using OllamaProcessOwnership owner = new();
    int launches = 0;
    int approvals = 0;
    bool owned = await Ensure(owner, _ => { launches++; throw new InvalidOperationException("must not launch"); },
      _ => approvals++, reachable: alreadyRunning, external: true);
    Assert.False(owned);
    Assert.Equal(0, launches);
    Assert.Equal(0, approvals);
  }

  [Fact]
  public async Task ReentrantStopInsideLaunchRetainsChildAndRejectsLateApproval()
  {
    OllamaProcessOwnership owner = new();
    using Child child = new();
    Task? stop = null;
    int approved = 0;
    Task<bool> start = Ensure(owner, _ => { stop = owner.RequestStop(); return child; }, _ => approved++);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
    await stop!;
    Assert.Equal(0, approved);
    Assert.Equal(1, child.Stops);
    Assert.Equal(1, child.Disposals);
  }

  [Fact]
  public async Task CallerCancellationImmediatelyAfterLaunchCannotApproveOrOrphanChild()
  {
    OllamaProcessOwnership owner = new();
    using Child child = new();
    using CancellationTokenSource caller = new();
    int approved = 0;
    Task<bool> start = Ensure(owner, _ => { caller.Cancel(); return child; }, _ => approved++, token: caller.Token);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
    await owner.StopAsync();
    Assert.Equal(0, approved);
    Assert.Equal(1, child.Stops);
    Assert.Equal(1, child.Disposals);
  }

  [Fact]
  public async Task TrustUpdateFailurePreservesOriginalAndStopsOnlyDirectLaunch()
  {
    OllamaProcessOwnership owner = new();
    InvalidOperationException original = new("identity unavailable");
    using Child child = new() { StopFailure = new IOException("termination failure") };
    Exception actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(owner, _ => child, _ => throw original));
    Assert.Same(original, actual);
    await Assert.ThrowsAsync<AggregateException>(() => owner.StopAsync());
    Assert.Equal(1, child.Stops);
    Assert.Equal(1, child.Disposals);
  }

  [Fact]
  public async Task ReadinessFailureCleansOnlyItsLaunchAndRetainsPreviousRuntime()
  {
    OllamaProcessOwnership owner = new();
    using Child previous = new();
    using Child candidate = new();
    owner.TrackProcess(previous);
    await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(owner, _ => candidate, _ => { }, ready: false));
    Assert.Equal(0, previous.Stops);
    Assert.Equal(1, candidate.Stops);
    await owner.StopAsync();
    Assert.Equal(1, previous.Stops);
    Assert.Equal(1, candidate.Stops);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(9)]
  public async Task ExitedLaunchCannotCertifyDifferentServingProcess(int exitCode)
  {
    await using OllamaProcessOwnership owner = new();
    using Child child = new() { HasExited = true, ExitCode = exitCode };
    int approvals = 0;
    InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(owner, _ => child, _ => approvals++));
    Assert.Contains($"exited with code {exitCode}", error.Message, StringComparison.Ordinal);
    Assert.Equal(0, approvals);
    Assert.Equal(1, child.Stops);
  }

  [Fact]
  public async Task LaunchFailureDoesNotTargetPreviouslyOwnedRuntime()
  {
    await using OllamaProcessOwnership owner = new();
    using Child previous = new();
    owner.TrackProcess(previous);
    IOException failure = new("launch denied");
    Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Ensure(owner, _ => throw failure, _ => { })));
    Assert.Equal(0, previous.Stops);
  }

  [Fact]
  public async Task OverlappingSetupsSerializeAndRetainAllDirectLaunches()
  {
    await using OllamaProcessOwnership owner = new();
    using Child first = new();
    using Child second = new();
    TaskCompletionSource entered = OllamaProcessOwnershipTests.Signal();
    TaskCompletionSource release = OllamaProcessOwnershipTests.Signal();
    int launches = 0;
    Task<bool> a = LocalOllamaRuntimeReadiness.EnsureRunningAsync(owner, _ => Task.FromResult(false),
      (_, _) => Task.FromResult("fake"), _ => { launches++; return first; }, _ => Task.FromResult(false),
      async _ => { entered.TrySetResult(); await release.Task; return true; }, _ => { }, null, CancellationToken.None);
    await entered.Task;
    Task<bool> b = Ensure(owner, _ => { Interlocked.Increment(ref launches); return second; }, _ => { });
    try { Assert.Equal(1, launches); Assert.False(b.IsCompleted); }
    finally { release.TrySetResult(); await Task.WhenAll(a, b); }
    await owner.StopAsync();
    Assert.Equal(2, launches);
    Assert.Equal(1, first.Stops);
    Assert.Equal(1, second.Stops);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(8)]
  public async Task HelperSuccessfulAndFailedExitAreDistinctAndHandlesDrain(int code)
  {
    await using OllamaProcessOwnership owner = new();
    using Child child = new() { HasExited = true, ExitCode = code };
    Task<int> helper = LocalOllamaRuntimeReadiness.RunHelperAsync(owner, () => child, TimeSpan.FromMinutes(1), "fixture", CancellationToken.None);
    if (code == 0) Assert.Equal(0, await helper);
    else Assert.Contains("exit code 8", (await Assert.ThrowsAsync<InvalidOperationException>(() => helper)).Message, StringComparison.Ordinal);
    Assert.Equal(1, child.Disposals);
  }

  [Fact]
  public async Task HelperTimeoutIsDistinctAndStopsOnlyItsDirectHandle()
  {
    await using OllamaProcessOwnership owner = new();
    using Child child = new();
    InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      LocalOllamaRuntimeReadiness.RunHelperAsync(owner, () => child, TimeSpan.FromMinutes(1), "fixture", CancellationToken.None,
        (_, _, _) => Task.FromException(new TimeoutException("controlled deadline"))));
    Assert.Contains("timed out", error.Message, StringComparison.Ordinal);
    Assert.IsType<TimeoutException>(error.InnerException);
    Assert.Equal(1, child.Stops);
  }

  [Fact]
  public async Task HelperCancellationAfterLaunchRetainsAndStopsItsHandle()
  {
    await using OllamaProcessOwnership owner = new();
    using Child child = new();
    using CancellationTokenSource cancellation = new();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LocalOllamaRuntimeReadiness.RunHelperAsync(owner,
      () => { cancellation.Cancel(); return child; }, TimeSpan.FromMinutes(1), "fixture", cancellation.Token));
    Assert.Equal(1, child.Stops);
  }

  private static Task<bool> Ensure(OllamaProcessOwnership owner, Func<string, OllamaProcessOwnership.IProcess> launch,
    Action<OllamaProcessOwnership.IProcess> approve, bool reachable = false, bool external = false,
    bool ready = true, CancellationToken token = default) => LocalOllamaRuntimeReadiness.EnsureRunningAsync(owner,
      _ => Task.FromResult(reachable), (_, _) => Task.FromResult("fake"), launch,
      _ => Task.FromResult(external), _ => Task.FromResult(ready), approve, null, token);

  [Fact]
  public void TemporaryCleanupContinuesIndependentlyAndPreservesOriginalFailure()
  {
    int second = 0;
    IOException original = new("installer launch failure");
    LocalOllamaRuntimeReadiness.RemoveInstallerFiles(() => throw new UnauthorizedAccessException(), () => second++, original);
    Assert.Equal(1, second);
    AggregateException error = Assert.Throws<AggregateException>(() => LocalOllamaRuntimeReadiness.RemoveInstallerFiles(
      () => throw new IOException("delete"), () => throw new UnauthorizedAccessException("directory"), null));
    Assert.Equal(2, error.InnerExceptions.Count);
  }
}
