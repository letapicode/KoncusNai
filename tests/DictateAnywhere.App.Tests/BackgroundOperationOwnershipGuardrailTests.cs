using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DictateAnywhere.App.Tests;

public sealed partial class BackgroundOperationOwnershipGuardrailTests
{
  [Xunit.Fact]
  public void ProductionCode_DoesNotDiscardCommonTaskProducingOperations()
  {
    string repoRoot = FindRepoRoot();
    string sourceRoot = Path.Combine(repoRoot, "src");
    List<string> violations = [];

    foreach (string path in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
    {
      if (IsGeneratedPath(path))
      {
        continue;
      }

      string[] lines = File.ReadAllLines(path);
      for (int index = 0; index < lines.Length; index++)
      {
        if (DiscardedTaskPattern().IsMatch(lines[index])
            && !IsObservedLifecycleBridge(Path.GetRelativePath(repoRoot, path), lines[index]))
        {
          violations.Add($"{Path.GetRelativePath(repoRoot, path)}:{index + 1}: {lines[index].Trim()}");
        }
      }
    }

    Xunit.Assert.True(
      violations.Count == 0,
      "Background tasks must be returned, awaited, or stored by a lifecycle owner."
      + Environment.NewLine
      + string.Join(Environment.NewLine, violations));
  }

  // Terminal observers catch/report every fault; the original operation remains
  // owned by the window, watchdog, or shutdown task. The two TCS-based owners
  // publish their shared completion before invoking reentrant callbacks.
  private static bool IsObservedLifecycleBridge(string path, string line)
  {
    string statement = line.Trim();
    if (statement.StartsWith("_ = LifecycleCleanup.ObserveAsync(", StringComparison.Ordinal)) return true;
    string normalized = path.Replace('\\', '/');
    return (normalized == "src/DictateAnywhere.App/Lifecycle/ApplicationShutdown.cs"
        && statement == "_ = RunCoreAsync(source, steps);")
      || (normalized == "src/DictateAnywhere.App/Lifecycle/WindowLifetimeRegistry.cs"
        && (statement == "_ = CloseCoreAsync(window, entry, source, alreadyClosed);"
          || statement == "_ = DisposeCoreAsync(source);"))
      || (normalized == "src/DictateAnywhere.App/Lifecycle/ApplicationHost.cs"
        && statement == "_ = StopCoreAsync(source);")
      || (normalized == "src/DictateAnywhere.App/App.xaml.cs"
        && statement == "_ = StopAdmissionAsync(admission);");
  }

  [Xunit.Fact]
  public void LifecycleExceptionsDoNotPermitUnownedWorkOrOtherTcsBridges()
  {
    Xunit.Assert.False(IsObservedLifecycleBridge("src/Other.cs", "_ = Task.Run(Work);"));
    Xunit.Assert.False(IsObservedLifecycleBridge("src/Other.cs", "_ = WorkAsync();"));
    Xunit.Assert.False(IsObservedLifecycleBridge("src/Other.cs", "_ = RunCoreAsync(source, steps);"));
    Xunit.Assert.False(IsObservedLifecycleBridge("src/DictateAnywhere.App/Lifecycle/ApplicationShutdown.cs", "_ = WorkAsync();"));
  }

  private static bool IsGeneratedPath(string path) =>
    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
    || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

  private static string FindRepoRoot()
  {
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
      if (File.Exists(Path.Combine(directory.FullName, "DictateAnywhere.sln")))
      {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    throw new InvalidOperationException("Unable to locate repository root from test base directory.");
  }

  [GeneratedRegex(
    @"^\s*_\s*=\s*(?:(?:Task|ValueTask)\.(?:Run|Factory\.StartNew)\s*\(|[A-Za-z_][A-Za-z0-9_.]*Async\s*\(|[A-Za-z_][A-Za-z0-9_.]*\.ContinueWith\s*\()",
    RegexOptions.CultureInvariant)]
  private static partial Regex DiscardedTaskPattern();
}
