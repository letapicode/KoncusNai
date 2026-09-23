using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Inference;

/// <summary>Downloads the free Windows llama.cpp runtime and a selected public GGUF chat model.</summary>
public sealed class LlamaCppProvisioningService
{
  internal const string RuntimeVersion = "b10823";
  internal const string RuntimeAssetName = "llama-b10823-bin-win-cpu-x64.zip";
  internal const string RuntimeDownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b10823/llama-b10823-bin-win-cpu-x64.zip";
  internal const string RuntimeSha256 = "fa3d7a84302fddfc669e6187aeffc85503a1172372eb86905f0529a83d99e1dc";
  internal const string RuntimeMarkerFileName = ".notype-runtime-provenance";
  private static readonly SemaphoreSlim ProvisionLock = new(1, 1);
  private readonly HttpClient httpClient;

  public LlamaCppProvisioningService(HttpClient? httpClient = null)
  {
    this.httpClient = httpClient ?? new HttpClient();
    if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
    {
      this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Koncus Nai/1.0");
    }
  }

  public async Task<LlamaCppRuntimeAvailability> ProvisionAsync(
    string modelId,
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default)
  {
    await ProvisionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      LlamaCppChatOptions options = LlamaCppChatOptions.ForModel(modelId);
      LlamaCppModelDefinition model = LlamaCppModelCatalog.GetRequired(modelId);
      Directory.CreateDirectory(Path.GetDirectoryName(LlamaCppChatOptions.DefaultRuntimeDirectory)!);
      Directory.CreateDirectory(Path.GetDirectoryName(options.ModelPath)!);
      if (!IsVerifiedRuntime(options))
      {
        progress?.Report(0.02);
        await DownloadRuntimeAsync(options, cancellationToken).ConfigureAwait(false);
      }
      progress?.Report(0.30);
      if (!File.Exists(options.ModelPath) || !MatchesSha256(options.ModelPath, model.Sha256))
      {
        await DownloadFileAsync(model.DownloadUrl, options.ModelPath, model.Sha256, 0.30, 0.98, progress, cancellationToken).ConfigureAwait(false);
      }
      WriteProvenanceMarker(GetModelMarkerPath(options.ModelPath), model.Sha256);
      progress?.Report(1.0);
      return LlamaCppRuntimeAvailability.Check(options);
    }
    finally
    {
      ProvisionLock.Release();
    }
  }

  public Task<LlamaCppRuntimeAvailability> ProvisionAsync(
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default)
    => ProvisionAsync(LlamaCppModelCatalog.Gemma3FourBModelId, progress, cancellationToken);

  private async Task DownloadRuntimeAsync(LlamaCppChatOptions options, CancellationToken cancellationToken)
  {
    string archivePath = Path.Combine(Path.GetTempPath(), $"notype-llama-{Guid.NewGuid():N}.zip");
    string stagedDirectory = LlamaCppChatOptions.DefaultRuntimeDirectory + $".staged-{Guid.NewGuid():N}";
    try
    {
      await DownloadFileAsync(RuntimeDownloadUrl, archivePath, RuntimeSha256, 0.02, 0.28, progress: null, cancellationToken).ConfigureAwait(false);
      using ZipArchive archive = ZipFile.OpenRead(archivePath);
      ZipArchiveEntry[] servers = archive.Entries.Where(entry => entry.Name.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
      ZipArchiveEntry server = servers.Length == 1
        ? servers[0]
        : throw new InvalidOperationException("The downloaded llama.cpp release must contain exactly one llama-server.exe.");
      string sourceDirectory = Path.GetDirectoryName(server.FullName)?.Replace('\\', '/') ?? string.Empty;
      ZipArchiveEntry[] runtimeEntries = archive.Entries
        .Where(entry => !string.IsNullOrEmpty(entry.Name) && Path.GetDirectoryName(entry.FullName)?.Replace('\\', '/') == sourceDirectory)
        .ToArray();
      if (runtimeEntries.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != runtimeEntries.Length)
      {
        throw new InvalidOperationException("The downloaded llama.cpp release contains duplicate runtime filenames.");
      }

      Directory.CreateDirectory(stagedDirectory);
      foreach (ZipArchiveEntry entry in runtimeEntries)
      {
        ValidateArchiveEntry(entry, sourceDirectory);
        string destination = Path.Combine(stagedDirectory, entry.Name);
        entry.ExtractToFile(destination, overwrite: false);
      }

      if (!File.Exists(Path.Combine(stagedDirectory, "llama-server.exe")))
      {
        throw new InvalidOperationException("The staged llama.cpp runtime is incomplete.");
      }

      File.WriteAllText(
        Path.Combine(stagedDirectory, RuntimeMarkerFileName),
        $"{RuntimeVersion}\n{RuntimeSha256}\n");

      PromoteDirectory(stagedDirectory, LlamaCppChatOptions.DefaultRuntimeDirectory);
    }
    finally
    {
      if (File.Exists(archivePath)) File.Delete(archivePath);
      if (Directory.Exists(stagedDirectory)) Directory.Delete(stagedDirectory, recursive: true);
    }
  }

  private static void ValidateArchiveEntry(ZipArchiveEntry entry, string sourceDirectory)
  {
    string normalized = entry.FullName.Replace('\\', '/');
    string expected = string.IsNullOrEmpty(sourceDirectory) ? entry.Name : $"{sourceDirectory}/{entry.Name}";
    if (!string.Equals(normalized, expected, StringComparison.Ordinal)
        || normalized.StartsWith("/", StringComparison.Ordinal)
        || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
    {
      throw new InvalidOperationException("The downloaded llama.cpp release contains an unsafe runtime path.");
    }
  }

  private async Task DownloadFileAsync(string url, string targetPath, string expectedSha256, double start, double end, IProgress<double>? progress, CancellationToken cancellationToken)
  {
    string temporaryPath = targetPath + ".download";
    try
    {
      using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();
      ValidateDownloadHost(url, response.RequestMessage?.RequestUri);
      long total = response.Content.Headers.ContentLength ?? -1;
      {
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream output = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, useAsync: true);
        byte[] buffer = new byte[131072]; long copied = 0; int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
          await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
          copied += read;
          if (total > 0) progress?.Report(start + ((end - start) * copied / total));
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
      }
      VerifySha256(temporaryPath, expectedSha256);
      // The stream must be disposed before moving on Windows; otherwise the
      // temporary download remains locked by this process.
      File.Move(temporaryPath, targetPath, overwrite: true);
    }
    finally
    {
      if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
    }
  }

  internal static void VerifySha256(string path, string expectedSha256)
  {
    using FileStream stream = File.OpenRead(path);
    string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidDataException("The downloaded llama.cpp artifact failed its integrity check.");
    }
  }

  internal static bool MatchesSha256(string path, string expectedSha256)
  {
    try
    {
      using FileStream stream = File.OpenRead(path);
      string actual = Convert.ToHexString(SHA256.HashData(stream));
      return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return false;
    }
  }

  internal static bool IsVerifiedRuntime(LlamaCppChatOptions options)
  {
    string markerPath = Path.Combine(Path.GetDirectoryName(options.ServerExecutablePath)!, RuntimeMarkerFileName);
    return File.Exists(options.ServerExecutablePath)
      && HasMatchingProvenanceMarker(markerPath, $"{RuntimeVersion}\n{RuntimeSha256}\n");
  }

  internal static bool HasVerifiedModelMarker(string modelPath)
  {
    return LlamaCppModelCatalog.TryGet(Path.GetFileName(modelPath), out LlamaCppModelDefinition? model)
      && model is not null
      && HasMatchingProvenanceMarker(GetModelMarkerPath(modelPath), model.Sha256 + "\n");
  }

  internal static string GetModelMarkerPath(string modelPath) => modelPath + ".sha256";

  private static bool HasMatchingProvenanceMarker(string markerPath, string expected)
  {
    try
    {
      string actual = File.ReadAllText(markerPath).Replace("\r\n", "\n", StringComparison.Ordinal);
      return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return false;
    }
  }

  private static void WriteProvenanceMarker(string markerPath, string value)
  {
    string temporaryPath = markerPath + $".tmp-{Guid.NewGuid():N}";
    try
    {
      File.WriteAllText(temporaryPath, value + "\n");
      File.Move(temporaryPath, markerPath, overwrite: true);
    }
    finally
    {
      if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
    }
  }

  internal static void ValidateDownloadHost(string requestedUrl, Uri? responseUri)
  {
    Uri requested = new(requestedUrl, UriKind.Absolute);
    string[] approvedHosts =
    [
      "github.com",
      "release-assets.githubusercontent.com",
      "objects.githubusercontent.com",
      "huggingface.co",
      "cdn-lfs.hf.co",
      "cas-bridge.xethub.hf.co",
    ];
    if (requested.Scheme != Uri.UriSchemeHttps
        || responseUri is null
        || responseUri.Scheme != Uri.UriSchemeHttps
        || !approvedHosts.Contains(requested.IdnHost, StringComparer.OrdinalIgnoreCase)
        || !approvedHosts.Contains(responseUri.IdnHost, StringComparer.OrdinalIgnoreCase))
    {
      throw new InvalidDataException("The download redirected to an unapproved source host.");
    }
  }

  internal static void PromoteDirectory(string stagedDirectory, string destinationDirectory)
  {
    string backupDirectory = destinationDirectory + $".backup-{Guid.NewGuid():N}";
    bool movedExisting = false;
    try
    {
      if (Directory.Exists(destinationDirectory))
      {
        Directory.Move(destinationDirectory, backupDirectory);
        movedExisting = true;
      }
      Directory.Move(stagedDirectory, destinationDirectory);
      if (movedExisting) Directory.Delete(backupDirectory, recursive: true);
    }
    catch
    {
      if (movedExisting && Directory.Exists(backupDirectory))
      {
        if (Directory.Exists(destinationDirectory)) Directory.Delete(destinationDirectory, recursive: true);
        Directory.Move(backupDirectory, destinationDirectory);
      }
      throw;
    }
  }
}
