using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace DictateAnywhere.App.Workbench.Reading;

internal sealed record ReaderVoicePreviewAsset(
  string ProviderId,
  string LanguageCode,
  string VoiceId,
  string FileName,
  double DurationSeconds,
  string Sha256);

internal sealed record ReaderVoicePreviewManifest(
  int SchemaVersion,
  DateTimeOffset GeneratedAtUtc,
  IReadOnlyList<ReaderVoicePreviewAsset> Previews)
{
  internal const int CurrentSchemaVersion = 1;
}

internal sealed class ReaderVoicePreviewAssetStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  private readonly string assetDirectory;
  private readonly Lazy<IReadOnlyDictionary<string, ReaderVoicePreviewAsset>> assetsByKey;
  private readonly HashSet<string> verifiedPaths = new(StringComparer.OrdinalIgnoreCase);
  private readonly object verificationSync = new();

  internal ReaderVoicePreviewAssetStore(string? assetDirectory = null)
  {
    this.assetDirectory = assetDirectory ?? Path.Combine(AppContext.BaseDirectory, "Assets", "VoicePreviews");
    assetsByKey = new Lazy<IReadOnlyDictionary<string, ReaderVoicePreviewAsset>>(LoadManifest, isThreadSafe: true);
  }

  internal bool TryResolve(ReaderLanguageOption language, ReaderVoiceOption voice, out string path)
  {
    ArgumentNullException.ThrowIfNull(language);
    ArgumentNullException.ThrowIfNull(voice);

    path = string.Empty;
    if (!assetsByKey.Value.TryGetValue(CreateKey(language.ProviderId, language.Code, voice.Id), out ReaderVoicePreviewAsset? asset))
    {
      return false;
    }

    string candidate = Path.GetFullPath(Path.Combine(assetDirectory, asset.FileName));
    string root = Path.GetFullPath(assetDirectory) + Path.DirectorySeparatorChar;
    if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
    {
      return false;
    }

    lock (verificationSync)
    {
      if (!verifiedPaths.Contains(candidate))
      {
        string actualHash;
        try
        {
          actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(candidate)));
        }
        catch (IOException)
        {
          return false;
        }
        catch (UnauthorizedAccessException)
        {
          return false;
        }

        if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
          return false;
        }

        verifiedPaths.Add(candidate);
      }
    }

    path = candidate;
    return true;
  }

  internal static string CreateFileName(ReaderLanguageOption language, ReaderVoiceOption voice) =>
    $"{Sanitize(language.ProviderId)}-{Sanitize(language.Code)}-{Sanitize(voice.Id)}.wav";

  internal static string CreateKey(string providerId, string languageCode, string voiceId) =>
    $"{providerId.Trim().ToLowerInvariant()}|{languageCode.Trim().ToLowerInvariant()}|{voiceId.Trim().ToLowerInvariant()}";

  private IReadOnlyDictionary<string, ReaderVoicePreviewAsset> LoadManifest()
  {
    string manifestPath = Path.Combine(assetDirectory, "manifest.json");
    if (!File.Exists(manifestPath))
    {
      return new Dictionary<string, ReaderVoicePreviewAsset>(StringComparer.Ordinal);
    }

    try
    {
      ReaderVoicePreviewManifest? manifest = JsonSerializer.Deserialize<ReaderVoicePreviewManifest>(
        File.ReadAllText(manifestPath),
        JsonOptions);
      if (manifest?.SchemaVersion != ReaderVoicePreviewManifest.CurrentSchemaVersion)
      {
        return new Dictionary<string, ReaderVoicePreviewAsset>(StringComparer.Ordinal);
      }

      return manifest.Previews
        .GroupBy(asset => CreateKey(asset.ProviderId, asset.LanguageCode, asset.VoiceId), StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }
    catch (JsonException)
    {
      return new Dictionary<string, ReaderVoicePreviewAsset>(StringComparer.Ordinal);
    }
    catch (IOException)
    {
      return new Dictionary<string, ReaderVoicePreviewAsset>(StringComparer.Ordinal);
    }
    catch (UnauthorizedAccessException)
    {
      return new Dictionary<string, ReaderVoicePreviewAsset>(StringComparer.Ordinal);
    }
  }

  internal static string Sanitize(string value) => string.Concat(
    value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
}
