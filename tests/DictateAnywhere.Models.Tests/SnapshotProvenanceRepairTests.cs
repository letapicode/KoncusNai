using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DictateAnywhere.Models;

namespace DictateAnywhere.Models.Tests;

public sealed class SnapshotProvenanceRepairTests
{
  [Xunit.Fact]
  public async Task ValidPinnedSnapshot_IsRecoveredWithoutDownloading()
  {
    string root = CreateFixture(out string modelPath, out string manifestPath);
    try
    {
      bool repaired = await SnapshotProvenanceRepair.TryRepairAsync(modelPath,
        "model-a", "org/model-a", new string('1', 40), manifestPath);
      Xunit.Assert.True(repaired);
      string marker = Path.Combine(modelPath, HuggingFaceSnapshotModelManager.ProvenanceMarkerFileName);
      Xunit.Assert.True(File.Exists(marker));
      using JsonDocument document = JsonDocument.Parse(File.ReadAllText(marker));
      Xunit.Assert.Equal("model-a", document.RootElement.GetProperty("artifactId").GetString());
    }
    finally { Directory.Delete(root, recursive: true); }
  }

  [Xunit.Fact]
  public async Task TamperedSnapshot_DoesNotGainProvenance()
  {
    string root = CreateFixture(out string modelPath, out string manifestPath);
    try
    {
      File.WriteAllText(Path.Combine(modelPath, "config.json"), "tampered");
      bool repaired = await SnapshotProvenanceRepair.TryRepairAsync(modelPath,
        "model-a", "org/model-a", new string('1', 40), manifestPath);
      Xunit.Assert.False(repaired);
      Xunit.Assert.False(File.Exists(Path.Combine(modelPath,
        HuggingFaceSnapshotModelManager.ProvenanceMarkerFileName)));
    }
    finally { Directory.Delete(root, recursive: true); }
  }

  private static string CreateFixture(out string modelPath, out string manifestPath)
  {
    string root = Path.Combine(Path.GetTempPath(), "KoncusNai.ModelRepair.Tests", Guid.NewGuid().ToString("N"));
    modelPath = Path.Combine(root, "model");
    Directory.CreateDirectory(modelPath);
    byte[] content = Encoding.UTF8.GetBytes("trusted model config");
    File.WriteAllBytes(Path.Combine(modelPath, "config.json"), content);
    manifestPath = Path.Combine(root, "manifest.json");
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
    {
      artifacts = new[]
      {
        new
        {
          id = "model-a",
          repository = "org/model-a",
          revision = new string('1', 40),
          files = new[] { new { path = "config.json", size = content.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(content)) } },
        },
      },
    }));
    return root;
  }
}
