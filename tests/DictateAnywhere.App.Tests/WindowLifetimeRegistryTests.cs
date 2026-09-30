using DictateAnywhere.App.Lifecycle;

namespace DictateAnywhere.App.Tests;

public sealed class WindowLifetimeRegistryTests
{
  [Xunit.Fact]
  public async Task QuitRetainsAlreadyClosingWindowAlongsideReplacementAndIndependentReaders()
  {
    WindowLifetimeRegistry windows = new();
    object oldWorkbench = new();
    object newWorkbench = new();
    object firstReader = new();
    object secondReader = new();
    TaskCompletionSource releaseOld = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int oldCleanupCount = 0;
    List<object> closed = [];
    windows.Register(oldWorkbench, () => closed.Add(oldWorkbench), () =>
    {
      oldCleanupCount++;
      return releaseOld.Task;
    });
    Task closing = windows.CloseAsync(oldWorkbench);
    foreach (object window in new[] { newWorkbench, firstReader, secondReader })
      windows.Register(window, () => closed.Add(window), () => Task.CompletedTask);
    Task quit = windows.DisposeAsync();
    Xunit.Assert.Same(quit, windows.DisposeAsync());
    Xunit.Assert.Same(closing, windows.CloseAsync(oldWorkbench));
    Xunit.Assert.False(quit.IsCompleted);
    Xunit.Assert.Equal(4, closed.Count);
    Xunit.Assert.Throws<ObjectDisposedException>(() => windows.Register(new object(), () => { }, () => Task.CompletedTask));
    releaseOld.SetResult();
    await quit.WaitAsync(TimeSpan.FromSeconds(2));
    Xunit.Assert.Equal(1, oldCleanupCount);
  }

  [Xunit.Fact]
  public async Task CloseFailureStillDisposesThatWindowAndOtherWindows()
  {
    WindowLifetimeRegistry windows = new();
    bool failedWindowDisposed = false;
    bool otherWindowDisposed = false;
    windows.Register(new object(), () => throw new IOException("Close failed"), () =>
    {
      failedWindowDisposed = true;
      return Task.CompletedTask;
    });
    windows.Register(new object(), () => { }, () =>
    {
      otherWindowDisposed = true;
      return Task.CompletedTask;
    });
    await Xunit.Assert.ThrowsAsync<AggregateException>(() => windows.DisposeAsync());
    Xunit.Assert.True(failedWindowDisposed);
    Xunit.Assert.True(otherWindowDisposed);
  }

  [Xunit.Fact]
  public async Task CloseCallbackCanReenterWithoutStartingAnotherDisposal()
  {
    WindowLifetimeRegistry windows = new();
    object window = new();
    Task? reentrant = null;
    int disposals = 0;
    windows.Register(window, () => reentrant = windows.CloseAsync(window), () =>
    {
      disposals++;
      return Task.CompletedTask;
    });
    Task close = windows.CloseAsync(window);
    Xunit.Assert.Same(close, reentrant);
    await close;
    await windows.DisposeAsync();
    Xunit.Assert.Equal(1, disposals);
  }
}
