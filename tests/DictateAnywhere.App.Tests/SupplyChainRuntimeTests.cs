using System.Text.Json;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Workbench.Reading;

namespace DictateAnywhere.App.Tests;

public sealed class SupplyChainRuntimeTests
{
  [Xunit.Fact]
  public void OllamaModelReadiness_RequiresThePinnedRegistryDigest()
  {
    using JsonDocument matching = JsonDocument.Parse(
      $$"""{"models":[{"name":"gemma4:e4b","digest":"{{LocalOllamaRuntimeReadiness.Gemma4E4BDigest}}"}]}""");
    using JsonDocument wrongDigest = JsonDocument.Parse(
      """{"models":[{"name":"gemma4:e4b","digest":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"}]}""");
    using JsonDocument missingDigest = JsonDocument.Parse("""{"models":[{"name":"gemma4:e4b"}]}""");

    Xunit.Assert.True(LocalOllamaRuntimeReadiness.IsPinnedModelPresent(matching.RootElement, "gemma4:e4b"));
    Xunit.Assert.False(LocalOllamaRuntimeReadiness.IsPinnedModelPresent(wrongDigest.RootElement, "gemma4:e4b"));
    Xunit.Assert.False(LocalOllamaRuntimeReadiness.IsPinnedModelPresent(missingDigest.RootElement, "gemma4:e4b"));
    Xunit.Assert.False(LocalOllamaRuntimeReadiness.IsPinnedModelPresent(matching.RootElement, "unknown:latest"));
  }

  [Xunit.Theory]
  [Xunit.InlineData("https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip", true)]
  [Xunit.InlineData("https://WWW.GYAN.DEV/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip", true)]
  [Xunit.InlineData("http://www.gyan.dev/ffmpeg.zip", false)]
  [Xunit.InlineData("https://example.invalid/ffmpeg.zip", false)]
  [Xunit.InlineData("https://user@example.invalid/ffmpeg.zip", false)]
  public void ReaderFfmpegRuntime_RejectsRedirectsOutsideThePinnedSource(string source, bool accepted)
  {
    if (accepted)
    {
      ReaderFfmpegRuntime.ValidateDownloadSource(new Uri(source));
      return;
    }

    Xunit.Assert.Throws<InvalidDataException>(() =>
      ReaderFfmpegRuntime.ValidateDownloadSource(new Uri(source)));
  }

  [Xunit.Fact]
  public void LocalPythonRepair_UsesTheHashLockedRequirementsWithoutMutablePipUpgrade()
  {
    string requirementsPath = LocalPythonRuntimeDependencyProbe.ResolveRequirementsPath();
    IReadOnlyList<string> arguments = LocalPythonRuntimeDependencyProbe.BuildLockedInstallArguments(requirementsPath);

    Xunit.Assert.EndsWith("requirements-lock.txt", requirementsPath, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(
      ["-m", "pip", "install", "--require-hashes", "--no-deps", "--no-build-isolation", "-r", requirementsPath],
      arguments);
    Xunit.Assert.DoesNotContain("--upgrade", arguments);
  }

  [Xunit.Theory]
  [Xunit.InlineData("https://ollama.com/download/OllamaSetup.exe", true)]
  [Xunit.InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/file", true)]
  [Xunit.InlineData("http://ollama.com/download/OllamaSetup.exe", false)]
  [Xunit.InlineData("https://example.invalid/OllamaSetup.exe", false)]
  [Xunit.InlineData("https://user@ollama.com/download/OllamaSetup.exe", false)]
  public void OllamaInstaller_RejectsRedirectsOutsideTheApprovedSignedChannel(string source, bool accepted)
  {
    if (accepted)
    {
      LocalOllamaRuntimeReadiness.ValidateInstallerDownloadSource(new Uri(source));
      return;
    }

    Xunit.Assert.Throws<InvalidDataException>(() =>
      LocalOllamaRuntimeReadiness.ValidateInstallerDownloadSource(new Uri(source)));
  }
}
