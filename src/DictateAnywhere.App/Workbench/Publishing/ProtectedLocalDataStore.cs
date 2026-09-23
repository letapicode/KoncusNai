using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Util.Store;

namespace DictateAnywhere.App.Workbench.Publishing;

/// <summary>Google token store encrypted for the current Windows user with DPAPI.</summary>
internal sealed class ProtectedLocalDataStore : IDataStore
{
  private static readonly byte[] Entropy = "Notype.YouTube.OAuth.v1"u8.ToArray();
  private readonly string directoryPath;

  public ProtectedLocalDataStore(string? directoryPath = null)
  {
    this.directoryPath = directoryPath ?? Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "youtube-auth");
  }

  public Task StoreAsync<T>(string key, T value)
  {
    ArgumentNullException.ThrowIfNull(value);
    Directory.CreateDirectory(directoryPath);
    byte[] json = JsonSerializer.SerializeToUtf8Bytes(value);
    byte[] encrypted = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
    WriteAtomically(GetPath(key), encrypted);
    return Task.CompletedTask;
  }

  public Task DeleteAsync<T>(string key)
  {
    string path = GetPath(key);
    if (File.Exists(path))
    {
      File.Delete(path);
    }

    return Task.CompletedTask;
  }

  public Task<T?> GetAsync<T>(string key)
  {
    string path = GetPath(key);
    if (!File.Exists(path))
    {
      return Task.FromResult<T?>(default);
    }

    byte[] encrypted = File.ReadAllBytes(path);
    byte[] json = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
    return Task.FromResult(JsonSerializer.Deserialize<T>(json));
  }

  public Task ClearAsync()
  {
    if (Directory.Exists(directoryPath))
    {
      foreach (string path in Directory.EnumerateFiles(directoryPath, "*.bin", SearchOption.TopDirectoryOnly))
      {
        File.Delete(path);
      }
    }

    return Task.CompletedTask;
  }

  internal bool HasAnyData() => Directory.Exists(directoryPath)
    && Directory.EnumerateFiles(directoryPath, "*.bin", SearchOption.TopDirectoryOnly).Any();

  internal static void WriteAtomically(string destination, byte[] encrypted)
  {
    string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
      using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
      {
        stream.Write(encrypted);
        stream.Flush(flushToDisk: true);
      }
      File.Move(temporary, destination, overwrite: true);
    }
    finally
    {
      if (File.Exists(temporary)) File.Delete(temporary);
    }
  }

  private string GetPath(string key)
  {
    byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key ?? string.Empty));
    return Path.Combine(directoryPath, $"{Convert.ToHexString(hash)}.bin");
  }
}
