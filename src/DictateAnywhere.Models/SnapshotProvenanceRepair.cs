using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Models;

/// <summary>Recovers an older local snapshot only after every file matches the pinned manifest.</summary>
internal static class SnapshotProvenanceRepair
{
  internal static async Task<bool> TryRepairAsync(
    string modelPath,
    string artifactId,
    string repository,
    string revision,
    string manifestPath,
    CancellationToken cancellationToken = default)
  {
    if (!Directory.Exists(modelPath)) return false;

    using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken)
      .ConfigureAwait(false));
    JsonElement[] matches = manifest.RootElement.GetProperty("artifacts").EnumerateArray()
      .Where(item => string.Equals(item.GetProperty("id").GetString(), artifactId, StringComparison.OrdinalIgnoreCase))
      .ToArray();
    if (matches.Length != 1) return false;
    JsonElement artifact = matches[0];
    if (!string.Equals(artifact.GetProperty("repository").GetString(), repository, StringComparison.Ordinal)
        || !string.Equals(artifact.GetProperty("revision").GetString(), revision, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    Dictionary<string, (long Size, string Hash)> expected = new(StringComparer.OrdinalIgnoreCase);
    foreach (JsonElement file in artifact.GetProperty("files").EnumerateArray())
    {
      string path = file.GetProperty("path").GetString() ?? string.Empty;
      string hash = file.GetProperty("sha256").GetString() ?? string.Empty;
      if (!IsSafeRelativePath(path) || hash.Length != 64 || !hash.All(Uri.IsHexDigit)
          || !expected.TryAdd(path.Replace('/', Path.DirectorySeparatorChar),
            (file.GetProperty("size").GetInt64(), hash)))
      {
        return false;
      }
    }
    if (expected.Count == 0) return false;

    string[] actual = Directory.EnumerateFiles(modelPath, "*", new EnumerationOptions
    {
      RecurseSubdirectories = true,
      AttributesToSkip = FileAttributes.ReparsePoint,
    })
      .Select(path => Path.GetRelativePath(modelPath, path))
      .Where(path => !path.StartsWith(".cache" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(path, HuggingFaceSnapshotModelManager.ProvenanceMarkerFileName,
          StringComparison.OrdinalIgnoreCase))
      .ToArray();
    if (actual.Length != expected.Count
        || actual.Distinct(StringComparer.OrdinalIgnoreCase).Count() != actual.Length
        || actual.Any(path => !expected.ContainsKey(path)))
    {
      return false;
    }

    foreach ((string relativePath, (long size, string hash)) in expected)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string fullPath = Path.Combine(modelPath, relativePath);
      FileInfo info = new(fullPath);
      if (!info.Exists || info.Length != size || size < 1) return false;
      await using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
      string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)
        .ConfigureAwait(false));
      if (!string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase)) return false;
    }

    string markerPath = Path.Combine(modelPath, HuggingFaceSnapshotModelManager.ProvenanceMarkerFileName);
    string temporaryMarkerPath = markerPath + ".tmp-" + Guid.NewGuid().ToString("N");
    try
    {
      await File.WriteAllTextAsync(temporaryMarkerPath, JsonSerializer.Serialize(new
      {
        schemaVersion = 1,
        artifactId,
        repository,
        revision,
      }), cancellationToken).ConfigureAwait(false);
      File.Move(temporaryMarkerPath, markerPath, overwrite: true);
    }
    finally
    {
      if (File.Exists(temporaryMarkerPath)) File.Delete(temporaryMarkerPath);
    }

    return true;
  }

  private static bool IsSafeRelativePath(string path) =>
    !string.IsNullOrWhiteSpace(path)
    && !Path.IsPathRooted(path)
    && !path.StartsWith("/", StringComparison.Ordinal)
    && !path.Contains('\\')
    && path.Split('/').All(part => part.Length > 0 && part != "." && part != ".."
      && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
}
