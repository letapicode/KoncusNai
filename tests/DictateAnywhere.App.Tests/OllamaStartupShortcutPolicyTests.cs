using DictateAnywhere.App.Runtime;

namespace DictateAnywhere.App.Tests;

public sealed class OllamaStartupShortcutPolicyTests
{
  [Xunit.Theory]
  [Xunit.InlineData(@"C:\Users\Person\AppData\Local\Programs\Ollama\ollama.exe")]
  [Xunit.InlineData(@"C:\Users\Person\AppData\Local\Programs\Ollama\ollama app.exe")]
  [Xunit.InlineData(@"C:\Users\Person\AppData\Local\Programs\Ollama\OLLAMA.EXE")]
  public void IsExpectedOllamaTarget_AcceptsOnlyOllamaExecutables(string targetPath)
  {
    Xunit.Assert.True(OllamaStartupShortcutPolicy.IsExpectedOllamaTarget(targetPath));
  }

  [Xunit.Theory]
  [Xunit.InlineData(null)]
  [Xunit.InlineData("")]
  [Xunit.InlineData(@"C:\Tools\Other\ollama-helper.exe")]
  [Xunit.InlineData(@"C:\Windows\System32\cmd.exe")]
  public void IsExpectedOllamaTarget_RejectsUnrelatedTargets(string? targetPath)
  {
    Xunit.Assert.False(OllamaStartupShortcutPolicy.IsExpectedOllamaTarget(targetPath));
  }

  [Xunit.Fact]
  public void TryDisableAutomaticStartup_DeletesOnlyAVerifiedShortcut()
  {
    string directory = Path.Combine(Path.GetTempPath(), "Notype.OllamaStartup.Tests", Guid.NewGuid().ToString("N"));
    string shortcutPath = Path.Combine(directory, "Ollama.lnk");
    Directory.CreateDirectory(directory);
    File.WriteAllText(shortcutPath, "fixture");
    try
    {
      bool removed = OllamaStartupShortcutPolicy.TryDisableAutomaticStartup(
        shortcutPath,
        _ => @"C:\Users\Person\AppData\Local\Programs\Ollama\ollama app.exe",
        File.Delete);

      Xunit.Assert.True(removed);
      Xunit.Assert.False(File.Exists(shortcutPath));
    }
    finally
    {
      if (Directory.Exists(directory))
      {
        Directory.Delete(directory, recursive: true);
      }
    }
  }

  [Xunit.Fact]
  public void TryDisableAutomaticStartup_PreservesAnUnrelatedShortcut()
  {
    string directory = Path.Combine(Path.GetTempPath(), "Notype.OllamaStartup.Tests", Guid.NewGuid().ToString("N"));
    string shortcutPath = Path.Combine(directory, "Ollama.lnk");
    Directory.CreateDirectory(directory);
    File.WriteAllText(shortcutPath, "fixture");
    try
    {
      bool removed = OllamaStartupShortcutPolicy.TryDisableAutomaticStartup(
        shortcutPath,
        _ => @"C:\Windows\System32\notepad.exe",
        File.Delete);

      Xunit.Assert.False(removed);
      Xunit.Assert.True(File.Exists(shortcutPath));
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }
}
