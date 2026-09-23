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
        if (DiscardedTaskPattern().IsMatch(lines[index]))
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
