using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Workbench.Publishing;

internal delegate Task<string> RenderPublishingEpisodeAsync(
  YouTubePublishingEpisode episode,
  IProgress<double> progress,
  CancellationToken cancellationToken);

/// <summary>Runs and journals a complete render/upload series after one user approval.</summary>
internal sealed class YouTubePublishingCoordinator
{
  private readonly IYouTubeVideoPublisher publisher;
  private readonly IYouTubePublishingJobStore jobStore;
  private readonly Func<TimeSpan, CancellationToken, Task> delay;
  public YouTubePublishingJob? LastKnownJob { get; private set; }

  public YouTubePublishingCoordinator(
    IYouTubeVideoPublisher publisher,
    IYouTubePublishingJobStore? jobStore = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
  {
    this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    this.jobStore = jobStore ?? new YouTubePublishingJobStore();
    this.delay = delay ?? Task.Delay;
  }

  public async Task<YouTubePublishingJob> RunAsync(
    YouTubePublishingJob job,
    YouTubeOAuthConfiguration configuration,
    string language,
    RenderPublishingEpisodeAsync renderEpisode,
    IProgress<YouTubePublishingProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(job);
    ArgumentNullException.ThrowIfNull(renderEpisode);
    if (!job.Plan.RightsConfirmed)
    {
      throw new InvalidOperationException("Publishing requires confirmation that you own or are authorized to use this content.");
    }

    // A retry may still hold the original request object after a receipt save failed.
    // Retain the newer in-memory transaction instead of overwriting it with that stale request.
    if (LastKnownJob is { } known && string.Equals(known.Id, job.Id, StringComparison.Ordinal)) job = known;
    List<YouTubePublishingEpisode> episodes = job.Episodes.ToList();
    YouTubePublishingJob current = job with { Episodes = episodes };
    LastKnownJob = current with { Episodes = episodes.ToArray() };
    await jobStore.SaveAsync(current, cancellationToken).ConfigureAwait(true);

    for (int index = 0; index < episodes.Count; index++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      YouTubePublishingEpisode episode = episodes[index];
      if (episode.State == YouTubeEpisodeState.Uploaded || !string.IsNullOrWhiteSpace(episode.YouTubeVideoId))
      {
        continue;
      }

      try
      {
        if ((episode.UploadAttempted || episode.State == YouTubeEpisodeState.Uploading)
            && string.IsNullOrWhiteSpace(episode.UploadSessionUri))
        {
          throw new InvalidOperationException("A previous upload has no recoverable session. Check YouTube before starting a new publishing job.");
        }
        if (!File.Exists(episode.VideoPath))
        {
          episode = episode with { State = YouTubeEpisodeState.Rendering, Error = null };
          ReplaceEpisode(index, episode);
          await PersistAsync().ConfigureAwait(true);
          IProgress<double> renderProgress = new Progress<double>(fraction => progress?.Report(new YouTubePublishingProgress(
            episode.EpisodeNumber,
            episodes.Count,
            YouTubeEpisodeState.Rendering,
            Math.Clamp(fraction, 0d, 1d) * 0.72d,
            $"Creating episode {episode.EpisodeNumber:N0} of {episodes.Count:N0}.")));
          string renderedPath = await renderEpisode(episode, renderProgress, cancellationToken).ConfigureAwait(true);
          episode = episode with { VideoPath = renderedPath, State = YouTubeEpisodeState.ReadyToUpload };
          ReplaceEpisode(index, episode);
          await PersistAsync().ConfigureAwait(true);
        }

        episode = episode with { State = YouTubeEpisodeState.Uploading, Error = null, UploadAttempted = true };
        ReplaceEpisode(index, episode);
        await PersistAsync().ConfigureAwait(true);

        DateTimeOffset? publishAt = job.Plan.Privacy == YouTubePrivacy.Scheduled && job.Plan.FirstPublishAt.HasValue
          ? job.Plan.FirstPublishAt.Value.AddHours((episode.EpisodeNumber - 1) * job.Plan.HoursBetweenEpisodes)
          : null;
        YouTubeUploadRequest uploadRequest = new(
          episode.VideoPath,
          episode.Title,
          episode.Description,
          YouTubeMetadataPolicy.ParseTags(job.Plan.Tags),
          language,
          job.Plan.Privacy,
          publishAt,
          job.Plan.MadeForKids,
          job.Plan.ContainsSyntheticMedia,
          episode.UploadSessionUri);

        string? resumeSessionUri = episode.UploadSessionUri;
        PublishingCheckpointQueue checkpoints = new(jobStore);
        object checkpointSync = new();
        bool acceptingProgress = true;
        IProgress<YouTubeUploadProgress> uploadProgress = new InlineProgress<YouTubeUploadProgress>(update =>
        {
          lock (checkpointSync)
          {
            if (!acceptingProgress) return;
            if (!string.IsNullOrWhiteSpace(update.SessionUri)
                && !string.Equals(resumeSessionUri, update.SessionUri, StringComparison.Ordinal))
            {
              resumeSessionUri = update.SessionUri;
              episode = episode with { UploadSessionUri = update.SessionUri };
              ReplaceEpisode(index, episode);
              checkpoints.Enqueue(current with { Episodes = episodes.ToArray() });
            }
          }

          progress?.Report(new YouTubePublishingProgress(
            episode.EpisodeNumber,
            episodes.Count,
            YouTubeEpisodeState.Uploading,
            0.72d + (update.Fraction * 0.28d),
            $"Uploading episode {episode.EpisodeNumber:N0} of {episodes.Count:N0}."));
        });

        YouTubeUploadResult upload;
        try
        {
          upload = await UploadWithRetryAsync(
            configuration,
            CreateCurrentUploadRequest,
            uploadProgress,
            cancellationToken).ConfigureAwait(true);
          // Retain the irreversible result before observing fallible progress I/O.
          lock (checkpointSync)
          {
            acceptingProgress = false;
            episode = episode with
            {
              State = YouTubeEpisodeState.Uploaded,
              YouTubeVideoId = upload.VideoId,
              UploadSessionUri = null,
              Error = null,
            };
            ReplaceEpisode(index, episode);
          }
        }
        finally
        {
          lock (checkpointSync) acceptingProgress = false;
          try { await checkpoints.DrainAsync().ConfigureAwait(true); }
          catch (Exception ex) when (!string.IsNullOrWhiteSpace(episode.YouTubeVideoId)
            && ex is IOException or UnauthorizedAccessException)
          {
            // The completion receipt below supersedes a failed progress checkpoint.
          }
        }
        try { await jobStore.SaveAsync(current, CancellationToken.None).ConfigureAwait(true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
          throw new PublishingReceiptPersistenceException(current, ex);
        }
        progress?.Report(new YouTubePublishingProgress(
          episode.EpisodeNumber,
          episodes.Count,
          YouTubeEpisodeState.Uploaded,
          1d,
          $"Episode {episode.EpisodeNumber:N0} uploaded."));

        YouTubeUploadRequest CreateCurrentUploadRequest()
        {
          lock (checkpointSync)
          {
            return uploadRequest with { ResumeSessionUri = resumeSessionUri };
          }
        }
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (PublishingReceiptPersistenceException) { throw; }
      catch (Exception ex)
      {
        _ = ex;
        episode = episode with
        {
          State = string.IsNullOrWhiteSpace(episode.YouTubeVideoId) ? YouTubeEpisodeState.Failed : YouTubeEpisodeState.Uploaded,
          Error = episode.UploadAttempted && string.IsNullOrWhiteSpace(episode.UploadSessionUri)
            ? "Publishing stopped. Check YouTube before starting a new job; no recoverable upload session was saved."
            : "Publishing stopped. This episode can be resumed.",
        };
        ReplaceEpisode(index, episode);
        await jobStore.SaveAsync(current, CancellationToken.None).ConfigureAwait(true);
        throw;
      }
    }

    current = current with { IsComplete = true, Episodes = episodes };
    await jobStore.SaveAsync(current, cancellationToken).ConfigureAwait(true);
    LastKnownJob = current;
    return current;

    void ReplaceEpisode(int index, YouTubePublishingEpisode episode)
    {
      episodes[index] = episode;
      current = current with { Episodes = episodes };
      LastKnownJob = current with { Episodes = episodes.ToArray() };
    }

    Task PersistAsync() => jobStore.SaveAsync(current, cancellationToken);
  }

  private async Task<YouTubeUploadResult> UploadWithRetryAsync(
    YouTubeOAuthConfiguration configuration,
    Func<YouTubeUploadRequest> requestFactory,
    IProgress<YouTubeUploadProgress> progress,
    CancellationToken cancellationToken)
  {
    for (int attempt = 1; ; attempt++)
    {
      try
      {
        return await publisher.UploadAsync(configuration, requestFactory(), progress, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (attempt < 3 && !string.IsNullOrWhiteSpace(requestFactory().ResumeSessionUri)
        && ex is IOException or HttpRequestException or TimeoutException)
      {
        await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
      }
    }
  }

  private sealed class PublishingCheckpointQueue(IYouTubePublishingJobStore jobStore)
  {
    private readonly object sync = new();
    private Task tail = Task.CompletedTask;

    public void Enqueue(YouTubePublishingJob snapshot)
    {
      lock (sync)
      {
        tail = SaveAfterAsync(tail, snapshot);
      }
    }

    public Task DrainAsync()
    {
      lock (sync)
      {
        return tail;
      }
    }

    private async Task SaveAfterAsync(Task predecessor, YouTubePublishingJob snapshot)
    {
      try { await predecessor.ConfigureAwait(false); }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        // Each newer snapshot is a complete checkpoint and can repair a failed predecessor.
      }
      await jobStore.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
    }
  }

  private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
  {
    public void Report(T value) => report(value);
  }
}
