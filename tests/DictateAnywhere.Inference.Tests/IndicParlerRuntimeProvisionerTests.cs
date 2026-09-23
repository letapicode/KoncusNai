using System.Security.Cryptography;
using System.Text;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class IndicParlerRuntimeProvisionerTests
{
  [Xunit.Fact]
  public void IsRuntimeReady_RequiresPythonAndMatchingRequirementsStamp()
  {
    using TempDirectoryScope temp = new();
    string python = Path.Combine(temp.DirectoryPath, "python.exe");
    string requirements = Path.Combine(temp.DirectoryPath, "requirements.txt");
    string torchLock = Path.Combine(temp.DirectoryPath, "torch-lock.txt");
    string stamp = Path.Combine(temp.DirectoryPath, ".requirements.sha256");
    File.WriteAllText(python, string.Empty, Encoding.UTF8);
    File.WriteAllText(requirements, "example==1.0", Encoding.UTF8);
    File.WriteAllText(torchLock, "torch==2.13.0+cpu", Encoding.UTF8);
    string hashMaterial = string.Join(
      "\n",
      Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(requirements))),
      Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(torchLock))));
    File.WriteAllText(
      stamp,
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashMaterial))),
      Encoding.UTF8);

    Xunit.Assert.True(IndicParlerRuntimeProvisioner.IsRuntimeReady(python, stamp, requirements, torchLock));

    File.WriteAllText(torchLock, "torch==2.11.0+cu128", Encoding.UTF8);
    Xunit.Assert.False(IndicParlerRuntimeProvisioner.IsRuntimeReady(python, stamp, requirements, torchLock));
  }

  [Xunit.Fact]
  public void IsRuntimeReady_InvalidatesRuntimeWhenWorkerOrCompatibilitySourcesChange()
  {
    using TempDirectoryScope temp = new();
    string python = Path.Combine(temp.DirectoryPath, "python.exe");
    string requirements = Path.Combine(temp.DirectoryPath, "requirements.txt");
    string torchLock = Path.Combine(temp.DirectoryPath, "torch-lock.txt");
    string worker = Path.Combine(temp.DirectoryPath, "worker.py");
    string compatManifest = Path.Combine(temp.DirectoryPath, "MANIFEST.sha256");
    string stamp = Path.Combine(temp.DirectoryPath, ".requirements.sha256");
    File.WriteAllText(python, string.Empty, Encoding.UTF8);
    File.WriteAllText(requirements, "example==1.0", Encoding.UTF8);
    File.WriteAllText(torchLock, "torch==2.13.0+cpu", Encoding.UTF8);
    File.WriteAllText(worker, "print('ready')", Encoding.UTF8);
    File.WriteAllText(compatManifest, "abc  source.py", Encoding.UTF8);
    WriteFingerprintStamp(stamp, requirements, torchLock, worker, compatManifest);

    Xunit.Assert.True(IndicParlerRuntimeProvisioner.IsRuntimeReady(
      python,
      stamp,
      requirements,
      torchLock,
      worker,
      compatManifest));

    File.AppendAllText(worker, "\n# compatibility change", Encoding.UTF8);
    Xunit.Assert.False(IndicParlerRuntimeProvisioner.IsRuntimeReady(
      python,
      stamp,
      requirements,
      torchLock,
      worker,
      compatManifest));

    File.WriteAllText(worker, "print('ready')", Encoding.UTF8);
    File.AppendAllText(compatManifest, "\ndef  other.py", Encoding.UTF8);
    Xunit.Assert.False(IndicParlerRuntimeProvisioner.IsRuntimeReady(
      python,
      stamp,
      requirements,
      torchLock,
      worker,
      compatManifest));
  }

  [Xunit.Fact]
  public void SetupScript_BuildsPackagesFromVerifiedStagingCopy()
  {
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "scripts", "setup-indic-parler-runtime.ps1")))
    {
      current = current.Parent;
    }

    Xunit.Assert.NotNull(current);
    string setupScript = File.ReadAllText(Path.Combine(current!.FullName, "scripts", "setup-indic-parler-runtime.ps1"));
    Xunit.Assert.Contains("function Copy-VerifiedCompatSources", setupScript, StringComparison.Ordinal);
    Xunit.Assert.Contains("Copy-VerifiedCompatSources -SourceRoot $compatRoot -DestinationRoot $stagedCompatRoot", setupScript, StringComparison.Ordinal);
    Xunit.Assert.Contains("manifest includes generated build metadata", setupScript, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("(Join-Path $stagedCompatRoot 'parler-tts')", setupScript, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("(Join-Path $compatRoot 'parler-tts')", setupScript, StringComparison.Ordinal);
  }

  private static void WriteFingerprintStamp(string stampPath, params string[] inputs)
  {
    string hashMaterial = string.Join(
      "\n",
      inputs.Select(input => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)))));
    File.WriteAllText(
      stampPath,
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashMaterial))),
      Encoding.UTF8);
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.ParlerProvisionerTests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
      if (Directory.Exists(DirectoryPath))
      {
        Directory.Delete(DirectoryPath, recursive: true);
      }
    }
  }
}
