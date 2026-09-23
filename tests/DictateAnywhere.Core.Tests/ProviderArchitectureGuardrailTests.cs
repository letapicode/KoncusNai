using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class ProviderArchitectureGuardrailTests
{
  [Fact]
  public void CoreContracts_DoNotReferenceProviderClientStacks()
  {
    string repoRoot = FindRepoRoot();
    string coreRoot = Path.Combine(repoRoot, "src", "DictateAnywhere.Core");
    string[] forbiddenTokens =
    [
      "using System.Net.Http",
      "HttpClient",
      "Google.Apis",
      "Google.Cloud",
      "Google.GenAI",
      "OpenAI.",
      "Cohere.",
    ];

    List<string> violations = [];
    foreach (string path in Directory.GetFiles(coreRoot, "*.cs", SearchOption.AllDirectories))
    {
      if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
          || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string[] lines = File.ReadAllLines(path);
      for (int index = 0; index < lines.Length; index++)
      {
        foreach (string token in forbiddenTokens)
        {
          if (lines[index].Contains(token, StringComparison.Ordinal))
          {
            string relativePath = Path.GetRelativePath(repoRoot, path);
            violations.Add($"{relativePath}:{index + 1} references provider client token '{token}'.");
          }
        }
      }
    }

    Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
  }

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
}
