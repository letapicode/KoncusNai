using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DictateAnywhere.App.Runtime;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class CohereConvertedCacheTests
{
  [Fact]
  public void DeletingConversionRemovesOnlyThatModelsCache()
  {
    string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "cohere-cache-test-" + Guid.NewGuid().ToString("N")));
    string model = Path.Combine(root, "source-model");
    string cache = Path.Combine(root, "cache");
    string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(model.ToLowerInvariant()))).ToLowerInvariant()[..24];
    string converted = Path.Combine(cache, key);
    Directory.CreateDirectory(model);
    Directory.CreateDirectory(converted);
    Directory.CreateDirectory(Path.Combine(cache, "other-model"));
    try
    {
      File.WriteAllText(Path.Combine(model, "source"), "synthetic source marker");
      File.WriteAllText(Path.Combine(converted, "q8"), "synthetic conversion marker");
      File.WriteAllText(Path.Combine(cache, "other-model", "keep"), "synthetic other-model marker");
      CohereRuntimePreparation.DeleteConvertedCache(cache, model);
      Assert.False(Directory.Exists(converted));
      Assert.True(File.Exists(Path.Combine(model, "source")));
      Assert.True(File.Exists(Path.Combine(cache, "other-model", "keep")));
      CohereRuntimePreparation.DeleteConvertedCache(cache, model);
    }
    finally { Directory.Delete(root, recursive: true); }
  }
}
