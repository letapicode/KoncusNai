using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Provides an app-owned FFmpeg executable only when video export is first requested.</summary>
internal static class ReaderFfmpegRuntime
{
  internal const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip";
  internal const string DownloadSha256 = "db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec";
  private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(30) };
  private static readonly SemaphoreSlim ProvisioningGate = new(1, 1);

  public static string RuntimeRoot { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DictateAnywhere",
    "media-runtime");

  public static string ExecutablePath => Path.Combine(RuntimeRoot, "ffmpeg.exe");

  public static async Task<string> EnsureAvailableAsync(
    IProgress<ReaderVideoExportProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    if (File.Exists(ExecutablePath))
    {
      return ExecutablePath;
    }

    await ProvisioningGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (File.Exists(ExecutablePath))
      {
        return ExecutablePath;
      }

      Directory.CreateDirectory(RuntimeRoot);
      string archivePath = Path.Combine(RuntimeRoot, $"ffmpeg-{Guid.NewGuid():N}.zip");
      string stagedPath = Path.Combine(RuntimeRoot, $"ffmpeg-{Guid.NewGuid():N}.exe");
      try
      {
        progress?.Report(new ReaderVideoExportProgress(ReaderVideoExportPhase.AcquiringRuntime, 0d, "Downloading the one-time high-quality video engine."));
        using HttpResponseMessage response = await HttpClient.GetAsync(
          DownloadUrl,
          HttpCompletionOption.ResponseHeadersRead,
          cancellationToken).ConfigureAwait(false);
        ValidateDownloadSource(response.RequestMessage?.RequestUri);
        response.EnsureSuccessStatusCode();
        long? totalBytes = response.Content.Headers.ContentLength;
        await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (FileStream destination = new(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
        {
          byte[] buffer = new byte[1024 * 128];
          long copied = 0;
          while (true)
          {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
              break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            double fraction = totalBytes > 0 ? Math.Clamp((double)copied / totalBytes.Value, 0d, 1d) : 0d;
            progress?.Report(new ReaderVideoExportProgress(ReaderVideoExportPhase.AcquiringRuntime, fraction, "Downloading the one-time high-quality video engine."));
          }
        }

        VerifySha256(archivePath, DownloadSha256);

        using (ZipArchive archive = ZipFile.OpenRead(archivePath))
        {
          ZipArchiveEntry[] executableEntries = archive.Entries.Where(entry =>
            entry.FullName.Replace('\\', '/').EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
          ZipArchiveEntry executable = executableEntries.Length == 1
            ? executableEntries[0]
            : throw new InvalidOperationException("The downloaded video engine must contain exactly one ffmpeg.exe.");
          string normalizedEntry = executable.FullName.Replace('\\', '/');
          if (normalizedEntry.StartsWith("/", StringComparison.Ordinal)
              || normalizedEntry.Split('/').Any(segment => segment is "" or "." or ".."))
          {
            throw new InvalidOperationException("The downloaded video engine contains an unsafe executable path.");
          }
          executable.ExtractToFile(stagedPath, overwrite: false);
        }

        File.Move(stagedPath, ExecutablePath, overwrite: true);
        progress?.Report(new ReaderVideoExportProgress(ReaderVideoExportPhase.AcquiringRuntime, 1d, "The video engine is ready."));
        return ExecutablePath;
      }
      finally
      {
        TryDelete(archivePath);
        TryDelete(stagedPath);
      }
    }
    finally
    {
      ProvisioningGate.Release();
    }
  }

  internal static void VerifySha256(string path, string expectedSha256)
  {
    using FileStream stream = File.OpenRead(path);
    string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidDataException("The downloaded video engine failed its integrity check.");
    }
  }

  internal static void ValidateDownloadSource(Uri? finalUri)
  {
    if (finalUri is null
        || !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(finalUri.IdnHost, "www.gyan.dev", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(finalUri.UserInfo))
    {
      throw new InvalidDataException("The video engine download was redirected to an unapproved source.");
    }
  }

  private static void TryDelete(string path)
  {
    try
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch (IOException)
    {
      // A stale staging file is harmless and can be replaced on the next export.
    }
    catch (UnauthorizedAccessException)
    {
      // Keep the original provisioning exception, if any, as the actionable failure.
    }
  }
}
