using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class OllamaOwnershipBoundaryGuardTests
{
  [Fact]
  public void RuntimeAndOrdinaryHelperHaveNoMarkerOrDiscoveryAuthority()
  {
    string root = FindRoot();
    string owner = File.ReadAllText(Path.Combine(root, "src/DictateAnywhere.App/Runtime/OllamaProcessOwnership.cs"));
    string readiness = File.ReadAllText(Path.Combine(root, "src/DictateAnywhere.App/Runtime/LocalOllamaRuntimeReadiness.cs"));
    string app = File.ReadAllText(Path.Combine(root, "src/DictateAnywhere.App/App.xaml.cs"));
    string stop = File.ReadAllText(Path.Combine(root, "scripts/stop-notype.ps1"));
    Assert.DoesNotContain("File.", owner, StringComparison.Ordinal);
    Assert.DoesNotContain("GetProcessById", owner, StringComparison.Ordinal);
    Assert.DoesNotContain("GetProcessesByName", readiness, StringComparison.Ordinal);
    Assert.DoesNotContain("TrackNewProcess", readiness, StringComparison.Ordinal);
    Assert.DoesNotContain("OllamaStartupShortcutPolicy", app + readiness, StringComparison.Ordinal);
    Assert.DoesNotContain("CreateOllamaUpgradeMarker", readiness, StringComparison.Ordinal);
    Assert.DoesNotContain("owned-ollama.pid", stop, StringComparison.Ordinal);
    Assert.DoesNotContain("entireProcessTree", owner + readiness, StringComparison.Ordinal);
    Assert.DoesNotContain("Stop-ProcessTree", stop[stop.IndexOf("$notypeProcesses", StringComparison.Ordinal)..stop.IndexOf("if ($AllOllama)", StringComparison.Ordinal)], StringComparison.Ordinal);
    Assert.False(File.Exists(Path.Combine(root, "src/DictateAnywhere.App/Runtime/OllamaStartupShortcutPolicy.cs")));
  }

  private static string FindRoot()
  {
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "DictateAnywhere.sln"))) return current.FullName;
      current = current.Parent;
    }
    throw new InvalidOperationException("Repository unavailable.");
  }
}
