using System;
using System.IO;

namespace DictateAnywhere.App.Tests;

public sealed class HistoryViewRefreshArchitectureGuardrailTests
{
  [Xunit.Fact]
  public void WindowCoordinator_DelegatesHistoryNotificationsAndOwnsRefreshShutdown()
  {
    string repoRoot = FindRepoRoot();
    string coordinatorSource = File.ReadAllText(Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Lifecycle",
      "WindowCoordinator.cs"));

    Xunit.Assert.Contains("CoalescingRefreshSession historyRefreshSession", coordinatorSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("historyRefreshSession.TryRequestRefresh", coordinatorSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("historyRefreshSession.DisposeAsync()", coordinatorSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("RefreshPersistedDictationHistoryAsync(cancellationToken)", coordinatorSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("RefreshPersistedHistoryAsync(cancellationToken)", coordinatorSource, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("historyRefreshQueued", coordinatorSource, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("historyRefreshRunning", coordinatorSource, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void WindowCoordinator_PushesSavedSettingsAndAwaitsWindowShutdown()
  {
    string repoRoot = FindRepoRoot();
    string appSource = File.ReadAllText(Path.Combine(repoRoot, "src", "DictateAnywhere.App", "App.xaml.cs"));
    string coordinatorSource = File.ReadAllText(Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Lifecycle",
      "WindowCoordinator.cs"));

    Xunit.Assert.Contains("windowCoordinator.ApplySettingsToOpenHistoryAsync(settings)", appSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("historyWindow.ApplySettingsAsync(settings)", coordinatorSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("await window.DisposeAsync()", coordinatorSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("await CloseWorkbenchAsync()", coordinatorSource, StringComparison.Ordinal);
  }

  private static string FindRepoRoot()
  {
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "DictateAnywhere.sln")))
      {
        return current.FullName;
      }

      current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
  }
}
