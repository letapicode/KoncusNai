using System;
using System.IO;

namespace DictateAnywhere.App.Tests;

public sealed class HistoryPersistenceArchitectureGuardrailTests
{
  [Xunit.Fact]
  public void LocalStores_DelegateJsonlMechanicsToTheSharedRecordFile()
  {
    string historyRoot = Path.Combine(
      FindRepoRoot(),
      "src",
      "DictateAnywhere.App",
      "History");
    string[] storeFiles =
    [
      "LocalChatHistoryStore.cs",
      "LocalDictationHistoryStore.cs",
    ];

    foreach (string storeFile in storeFiles)
    {
      string source = File.ReadAllText(Path.Combine(historyRoot, storeFile));
      Xunit.Assert.Contains("VersionedJsonLinesFile", source, StringComparison.Ordinal);
      Xunit.Assert.DoesNotContain("SemaphoreSlim ioLock", source, StringComparison.Ordinal);
    }
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
