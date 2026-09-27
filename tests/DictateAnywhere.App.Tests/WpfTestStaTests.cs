using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class WpfTestStaTests
{
  [Fact]
  public void RepeatedHostedLifetimesCloseNativeWindowsAndAbortPendingCallbacks()
  {
    Application? application = Application.Current;
    for (int iteration = 0; iteration < 4; iteration++)
    {
      Dispatcher? owned = null;
      DispatcherOperation? queued = null;
      bool closed = false, cleaned = false, called = false;
      int ownerThread = 0;
      WpfTestSta.Run(() =>
      {
        owned = Dispatcher.CurrentDispatcher;
        ownerThread = Environment.CurrentManagedThreadId;
        Window window = new() { Width = 200, Height = 100, Left = -10000, Top = -10000, ShowInTaskbar = false };
        WpfTestSta.Cleanup(() =>
        {
          Assert.Equal(ownerThread, Environment.CurrentManagedThreadId);
          window.Close(); cleaned = true;
        });
        window.Closed += (_, _) => closed = true;
        window.Show();
        Popup popup = new() { Child = new TextBlock { Text = "Test popup" }, PlacementTarget = window };
        WpfTestSta.Cleanup(() => popup.IsOpen = false);
        popup.IsOpen = true;
        queued = owned.BeginInvoke(DispatcherPriority.ApplicationIdle, () => called = true);
      });
      Assert.True(closed && cleaned);
      Assert.True(owned!.HasShutdownFinished);
      Assert.Equal(DispatcherOperationStatus.Aborted, queued!.Status);
      Assert.False(called);
      Assert.Same(application, Application.Current);
    }
  }

  [Fact]
  public void AssertionAndCleanupFailuresAreBothReportedAndDispatcherStillShutsDown()
  {
    Dispatcher? owned = null;
    bool lastCleanup = false;
    InvalidOperationException assertion = new("Original action failure");
    AggregateException error = Assert.Throws<AggregateException>(() => WpfTestSta.Run(() =>
    {
      owned = Dispatcher.CurrentDispatcher;
      WpfTestSta.Cleanup(() => lastCleanup = true);
      WpfTestSta.Cleanup(() => throw new InvalidOperationException("Cleanup failure"));
      throw assertion;
    }));
    Assert.Same(assertion, error.InnerExceptions[0]);
    Assert.Equal("Cleanup failure", error.InnerExceptions[1].Message);
    Assert.True(lastCleanup);
    Assert.True(owned!.HasShutdownFinished);
  }

  [Fact]
  public void PartialSetupAndDispatcherExceptionsRemainVisibleAfterCleanup()
  {
    Dispatcher? owned = null;
    bool cleaned = false;
    InvalidOperationException failure = new("Queued callback failed");
    Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => WpfTestSta.Run(() =>
    {
      owned = Dispatcher.CurrentDispatcher;
      WpfTestSta.Cleanup(() => cleaned = true);
      owned.BeginInvoke(DispatcherPriority.Normal, new Action(() => throw failure));
      owned.Invoke(() => { }, DispatcherPriority.Background);
    })));
    Assert.True(cleaned);
    Assert.True(owned!.HasShutdownFinished);
    Assert.Throws<InvalidOperationException>(() => WpfTestSta.Cleanup(() => { }));
  }
}
