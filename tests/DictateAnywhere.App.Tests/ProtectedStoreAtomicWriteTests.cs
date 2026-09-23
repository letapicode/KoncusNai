using DictateAnywhere.App.Workbench.Publishing;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class ProtectedStoreAtomicWriteTests
{
  [Fact]
  public async Task FailedReplacement_PreservesExistingToken_AndRemovesTemporaryFile()
  {
    string root = Path.Combine(Path.GetTempPath(), "kn-token-test-" + Guid.NewGuid().ToString("N"));
    try
    {
      ProtectedLocalDataStore store = new(root);
      await store.StoreAsync("fixture", "original synthetic token");
      string path = Assert.Single(Directory.GetFiles(root));
      byte[] original = File.ReadAllBytes(path);
      using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
      {
        Exception? error = await Record.ExceptionAsync(() => store.StoreAsync("fixture", "replacement synthetic token"));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(root));
      }
      Assert.Equal("original synthetic token", await store.GetAsync<string>("fixture"));
      await store.StoreAsync("fixture", "replacement synthetic token");
      Assert.Equal("replacement synthetic token", await store.GetAsync<string>("fixture"));
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
  }
}
