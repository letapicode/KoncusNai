using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.App.Composition;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class SettingsTransferOwnershipTests
{
  [Fact]
  public async Task Transfer_RejectsMissingCorruptCanceledAndActiveDestinationInputs()
  {
    string root = Path.Combine(Path.GetTempPath(), "kn-settings-transfer-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      string active = Path.Combine(root, "settings.json");
      JsonSettingsFileTransferService service = new(active);
      await Assert.ThrowsAsync<FileNotFoundException>(() => service.ImportAsync(active));
      await File.WriteAllTextAsync(active, "broken JSON");
      await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => service.ImportAsync(active));
      await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(active.ToUpperInvariant(), AppSettings.Default));
      using CancellationTokenSource canceled = new();
      canceled.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportAsync(active, canceled.Token));
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(Path.Combine(root, "export.json"), AppSettings.Default, canceled.Token));
      Assert.Equal("broken JSON", await File.ReadAllTextAsync(active));
      Assert.False(File.Exists(Path.Combine(root, "export.json")));
    }
    finally { Directory.Delete(root, recursive: true); }
  }

  [Fact]
  public async Task Import_DoesNotRewriteLegacySource()
  {
    string root = Path.Combine(Path.GetTempPath(), "kn-settings-transfer-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      string path = Path.Combine(root, "source.json");
      const string json = "{\"schemaVersion\":18,\"chatOutputFontSize\":19}";
      await File.WriteAllTextAsync(path, json);
      var result = await new JsonSettingsFileTransferService().ImportAsync(path);
      Assert.Equal(19, result.ChatOutputFontSize);
      Assert.Equal(json, await File.ReadAllTextAsync(path));
    }
    finally { Directory.Delete(root, recursive: true); }
  }
}
