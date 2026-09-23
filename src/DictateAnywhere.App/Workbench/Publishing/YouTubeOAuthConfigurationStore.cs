using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace DictateAnywhere.App.Workbench.Publishing;

/// <summary>Stores the developer's desktop OAuth configuration encrypted for the current Windows user.</summary>
internal sealed class YouTubeOAuthConfigurationStore
{
  private static readonly byte[] Entropy = "Notype.YouTube.Client.v1"u8.ToArray();
  private readonly string path;

  public YouTubeOAuthConfigurationStore(string? path = null)
  {
    this.path = path ?? Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "youtube-client.bin");
  }

  public YouTubeOAuthConfiguration Load()
  {
    if (!File.Exists(path))
    {
      return new YouTubeOAuthConfiguration(
        Environment.GetEnvironmentVariable("NOTYPE_YOUTUBE_CLIENT_ID") ?? string.Empty,
        Environment.GetEnvironmentVariable("NOTYPE_YOUTUBE_CLIENT_SECRET") ?? string.Empty);
    }

    try
    {
      byte[] json = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
      return JsonSerializer.Deserialize<YouTubeOAuthConfiguration>(json)?.Normalize()
        ?? new YouTubeOAuthConfiguration(string.Empty, string.Empty);
    }
    catch (CryptographicException)
    {
      return new YouTubeOAuthConfiguration(string.Empty, string.Empty);
    }
    catch (JsonException)
    {
      return new YouTubeOAuthConfiguration(string.Empty, string.Empty);
    }
  }

  public void Save(YouTubeOAuthConfiguration configuration)
  {
    YouTubeOAuthConfiguration normalized = configuration.Normalize();
    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("OAuth configuration path has no directory."));
    byte[] json = JsonSerializer.SerializeToUtf8Bytes(normalized);
    ProtectedLocalDataStore.WriteAtomically(path, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
  }
}
