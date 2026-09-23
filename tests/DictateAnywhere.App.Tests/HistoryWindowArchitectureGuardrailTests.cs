using System;
using System.IO;

namespace DictateAnywhere.App.Tests;

public sealed class HistoryWindowArchitectureGuardrailTests
{
  [Xunit.Fact]
  public void HistoryWindow_DelegatesReplaceableQueriesAndOwnsAsyncShutdown()
  {
    string historyRoot = Path.Combine(FindRepoRoot(), "src", "DictateAnywhere.App", "History");
    string windowSource = File.ReadAllText(Path.Combine(historyRoot, "HistoryWindow.xaml.cs"));
    string windowMarkup = File.ReadAllText(Path.Combine(historyRoot, "HistoryWindow.xaml"));
    string coordinatorSource = File.ReadAllText(Path.Combine(historyRoot, "HistoryQueryCoordinator.cs"));

    Xunit.Assert.Contains("HistoryQueryCoordinator queryCoordinator", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("HistoryCommandCoordinator commandCoordinator", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains(".QueryLatestAsync", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("queryCoordinator.DisposeAsync()", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("commandCoordinator.DisposeAsync()", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("internal ValueTask DisposeAsync()", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("internal Task ApplySettingsAsync", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("CurrentSettingsPolicy.Normalize(settings)", windowSource, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("ISettingsStore settingsStore", windowSource, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("LoadAndRefreshAsync", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains(".UpdateDictationAsync", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains(".DeleteDictationSessionsAsync", windowSource, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("ProductivityTextActions.CreateHistoryStore", windowSource, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("ReadRecentAsync(200", windowSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("Closed=\"OnClosed\"", windowMarkup, StringComparison.Ordinal);
    Xunit.Assert.Contains("LatestOperationSession querySession", coordinatorSource, StringComparison.Ordinal);
    Xunit.Assert.Contains("HistoryQueryStatus", coordinatorSource, StringComparison.Ordinal);
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
