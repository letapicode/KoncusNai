using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Workbench.Publishing;

internal interface IYouTubePublishingJobStore
{
  Task SaveAsync(YouTubePublishingJob job, CancellationToken cancellationToken = default);
  Task<YouTubePublishingJob?> LoadLatestIncompleteAsync(CancellationToken cancellationToken = default);
}

/// <summary>Atomic local publishing journal used to recover rendered and uploading episodes.</summary>
internal sealed class YouTubePublishingJobStore : IYouTubePublishingJobStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  private readonly string directoryPath;

  public YouTubePublishingJobStore(string? directoryPath = null)
  {
    this.directoryPath = directoryPath ?? Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "publishing-jobs");
  }

  public async Task SaveAsync(YouTubePublishingJob job, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(job);
    Directory.CreateDirectory(directoryPath);
    string path = GetPath(job.Id);
    string temporaryPath = path + ".tmp";
    try
    {
      await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
      {
        await JsonSerializer.SerializeAsync(stream, job, JsonOptions, cancellationToken).ConfigureAwait(false);
      }

      File.Move(temporaryPath, path, overwrite: true);
    }
    finally
    {
      TryDelete(temporaryPath);
    }
  }

  public async Task<YouTubePublishingJob?> LoadLatestIncompleteAsync(CancellationToken cancellationToken = default)
  {
    if (!Directory.Exists(directoryPath))
    {
      return null;
    }

    foreach (string path in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly)
      .OrderByDescending(File.GetLastWriteTimeUtc))
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        YouTubePublishingJob? job = await JsonSerializer.DeserializeAsync<YouTubePublishingJob>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (job is { IsComplete: false })
        {
          return job;
        }
      }
      catch (JsonException)
      {
        // A damaged journal is skipped; another recoverable job may still exist.
      }
      catch (IOException)
      {
        // A file still being finalized by another instance is skipped.
      }
    }

    return null;
  }

  private string GetPath(string id) => Path.Combine(directoryPath, $"{id}.json");

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
    }
    catch (UnauthorizedAccessException)
    {
    }
  }
}
