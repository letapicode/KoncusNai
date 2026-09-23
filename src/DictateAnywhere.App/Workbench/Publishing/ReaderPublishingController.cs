using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Publishing;

internal enum ReaderPublishingPhase
{
  Idle,
  WaitingForOperation,
  RecoveringJob,
  RenderingEpisode,
  Uploading,
  Checkpointing,
  Complete,
  Canceled,
  Failed,
}

internal enum ReaderPublishingOutcome
{
  Succeeded,
  Busy,
  Canceled,
  Failed,
  Stale,
}

internal enum ReaderPublishingStatus
{
  None,
  Starting,
  Rendering,
  Uploading,
  Complete,
  Canceled,
  Failed,
  ReceiptNotSaved,
  ReconciliationRequired,
}

[Flags]
internal enum ReaderPublishingDirective
{
  None = 0,
  StopVoicePreview = 1,
  NotifyCompletion = 2,
  ShowResumableWork = 4,
}

internal enum ReaderPublishingRecoveryStatus
{
  None,
  Compatible,
  Incompatible,
  Busy,
  Failed,
}

internal sealed record ReaderPublishingSourceSnapshot(
  string SourceFingerprint,
  IReadOnlyList<ReadingSection> DocumentSections,
  IReadOnlyList<int> SelectedSectionIndices,
  ReaderNarrationProfile NarrationProfile,
  string Language,
  ReaderVideoVisualSettings Visuals)
{
  public static ReaderPublishingSourceSnapshot Create(
    string sourceText,
    IReadOnlyList<ReadingSection> documentSections,
    IReadOnlyList<int> selectedSectionIndices,
    ReaderNarrationProfile narrationProfile,
    string language,
    ReaderVideoVisualSettings visuals) => new(
      ComputeFingerprint(sourceText),
      documentSections.ToArray(),
      selectedSectionIndices.ToArray(),
      narrationProfile,
      language,
      visuals);

  private static string ComputeFingerprint(string text)
  {
    byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
    return Convert.ToHexString(digest);
  }
}

internal sealed record ReaderPublishingCommand(
  YouTubePublishingJob Job,
  YouTubeOAuthConfiguration OAuthConfiguration,
  ReaderPublishingSourceSnapshot Source);

internal sealed record ReaderPublishingState(
  ReaderPublishingPhase Phase,
  ReaderPublishingStatus Status,
  int EpisodeNumber,
  int EpisodeCount,
  double Progress,
  ReaderPublishingDirective Directives)
{
  public static ReaderPublishingState Idle { get; } = new(
    ReaderPublishingPhase.Idle,
    ReaderPublishingStatus.None,
    0,
    0,
    0d,
    ReaderPublishingDirective.None);
}

internal sealed record ReaderPublishingResult(
  ReaderPublishingOutcome Outcome,
  ReaderPublishingStatus Status,
  ReaderPublishingDirective Directives,
  YouTubePublishingJob? Job = null);

internal sealed record ReaderPublishingRecoveryResult(
  ReaderPublishingRecoveryStatus Status,
  YouTubePublishingJob? Job = null);

internal sealed record ReaderPublishingJobCreationResult(
  bool Succeeded,
  YouTubePublishingJob? Job = null);

/// <summary>
/// Owns publishing recovery, workspace construction, shared-operation arbitration, episode rendering,
/// cancellation, and safe outcomes. The coordinator owns the ordered upload/checkpoint transaction.
/// </summary>
internal sealed class ReaderPublishingController : IAsyncDisposable
{
  private readonly object sync = new();
  private readonly ReaderOperationSession operationSession;
  private readonly ReaderExportPreparationService preparationService;
  private readonly IReaderExportEngine exportEngine;
  private readonly YouTubePublishingCoordinator coordinator;
  private readonly IYouTubePublishingJobStore jobStore;
  private readonly IDiagnostics diagnostics;
  private readonly string mediaRoot;
  private readonly CancellationTokenSource lifetimeSource = new();
  private ReaderOperationSession.ReaderOperation? activeOperation;
  private Task<ReaderPublishingResult>? activeTask;
  private Task<ReaderPublishingRecoveryResult>? recoveryTask;
  private ReaderPublishingState state = ReaderPublishingState.Idle;
  private long generation;
  private bool disposed;

  public ReaderPublishingController(
    ReaderOperationSession operationSession,
    ReaderExportPreparationService preparationService,
    IReaderExportEngine exportEngine,
    YouTubePublishingCoordinator coordinator,
    IYouTubePublishingJobStore jobStore,
    IDiagnostics diagnostics,
    string? mediaRoot = null)
  {
    this.operationSession = operationSession ?? throw new ArgumentNullException(nameof(operationSession));
    this.preparationService = preparationService ?? throw new ArgumentNullException(nameof(preparationService));
    this.exportEngine = exportEngine ?? throw new ArgumentNullException(nameof(exportEngine));
    this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    this.jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.mediaRoot = mediaRoot ?? Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "publishing-media");
  }

  public event EventHandler<ReaderPublishingState>? StateChanged;

  public ReaderPublishingState State
  {
    get
    {
      lock (sync)
      {
        return state;
      }
    }
  }

  public Task<ReaderPublishingRecoveryResult> FindRecoverableJobAsync(
    ReaderPublishingSourceSnapshot source,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(source);
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (recoveryTask is { IsCompleted: false })
      {
        return Task.FromResult(new ReaderPublishingRecoveryResult(ReaderPublishingRecoveryStatus.Busy));
      }

      recoveryTask = FindRecoverableJobCoreAsync(source, cancellationToken);
      return recoveryTask;
    }
  }

  private async Task<ReaderPublishingRecoveryResult> FindRecoverableJobCoreAsync(
    ReaderPublishingSourceSnapshot source,
    CancellationToken cancellationToken)
  {
    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken,
      lifetimeSource.Token);
    try
    {
      YouTubePublishingJob? job = coordinator.LastKnownJob is { IsComplete: false } known
        ? known : await jobStore.LoadLatestIncompleteAsync(linked.Token).ConfigureAwait(false);
      if (job is null)
      {
        return new ReaderPublishingRecoveryResult(ReaderPublishingRecoveryStatus.None);
      }

      bool compatible = string.Equals(job.SourceFingerprint, source.SourceFingerprint, StringComparison.Ordinal)
        && job.Episodes.Count == source.SelectedSectionIndices.Count
        && job.Episodes.Select(episode => episode.SourceSectionIndex).SequenceEqual(source.SelectedSectionIndices)
        && job.Episodes.All(episode => episode.SourceSectionIndex >= 0
          && episode.SourceSectionIndex < source.DocumentSections.Count);
      return compatible
        ? new ReaderPublishingRecoveryResult(ReaderPublishingRecoveryStatus.Compatible, job)
        : new ReaderPublishingRecoveryResult(ReaderPublishingRecoveryStatus.Incompatible);
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
    {
      diagnostics.Warning($"Reader publishing recovery failed: {ex}");
      return new ReaderPublishingRecoveryResult(ReaderPublishingRecoveryStatus.Failed);
    }
  }

  public ReaderPublishingJobCreationResult CreateJob(
    YouTubePublishingPlan plan,
    ReaderPublishingSourceSnapshot source)
  {
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(source);
    try
    {
      string jobId = Guid.NewGuid().ToString("N");
      string directory = Path.Combine(mediaRoot, jobId);
      Directory.CreateDirectory(directory);
      int count = source.SelectedSectionIndices.Count;
      List<YouTubePublishingEpisode> episodes = new(count);
      for (int offset = 0; offset < count; offset++)
      {
        int number = offset + 1;
        string description = YouTubeMetadataPolicy.NormalizeDescription(string.IsNullOrWhiteSpace(plan.Description)
          ? $"Part {number:N0} of {count:N0} in {plan.SeriesTitle}."
          : $"{plan.Description}\n\nPart {number:N0} of {count:N0}.");
        episodes.Add(new YouTubePublishingEpisode(
          number,
          source.SelectedSectionIndices[offset],
          YouTubeMetadataPolicy.CreateEpisodeTitle(plan.SeriesTitle, number, count),
          description,
          Path.Combine(directory, $"episode-{number:D4}.mp4")));
      }

      return new ReaderPublishingJobCreationResult(true, new YouTubePublishingJob(
        jobId,
        DateTimeOffset.UtcNow,
        plan.Normalize(),
        episodes,
        SourceFingerprint: source.SourceFingerprint));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      diagnostics.Warning($"Reader publishing workspace creation failed: {ex}");
      return new ReaderPublishingJobCreationResult(false);
    }
  }

  public Task<ReaderPublishingResult> PublishAsync(ReaderPublishingCommand request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Job);
    ArgumentNullException.ThrowIfNull(request.Source);
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (activeTask is { IsCompleted: false })
      {
        return Task.FromResult(Busy());
      }

      ReaderOperationSession.ReaderOperation? operation = operationSession.TryBegin(ReaderOperationKind.YouTubePublish);
      if (operation is null)
      {
        return Task.FromResult(Busy());
      }

      long requestGeneration = ++generation;
      activeOperation = operation;
      activeTask = ExecuteAndReleaseAsync(request, requestGeneration, operation);
      return activeTask;
    }
  }

  public void Cancel()
  {
    ReaderOperationSession.ReaderOperation? operation;
    lock (sync)
    {
      operation = activeOperation;
    }

    operation?.Cancel();
  }

  public async ValueTask DisposeAsync()
  {
    Task<ReaderPublishingResult>? task;
    Task<ReaderPublishingRecoveryResult>? recovery;
    ReaderOperationSession.ReaderOperation? operation;
    lock (sync)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      generation++;
      operation = activeOperation;
      task = activeTask;
      recovery = recoveryTask;
    }

    lifetimeSource.Cancel();
    operation?.Cancel();
    if (task is not null)
    {
      _ = await task.ConfigureAwait(false);
    }

    if (recovery is not null)
    {
      try
      {
        _ = await recovery.ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
      }
    }

    lifetimeSource.Dispose();
  }

  private async Task<ReaderPublishingResult> ExecuteAndReleaseAsync(
    ReaderPublishingCommand request,
    long requestGeneration,
    ReaderOperationSession.ReaderOperation operation)
  {
    await Task.Yield();
    try
    {
      Publish(new ReaderPublishingState(
        ReaderPublishingPhase.WaitingForOperation,
        ReaderPublishingStatus.Starting,
        0,
        request.Job.Episodes.Count,
        0d,
        ReaderPublishingDirective.StopVoicePreview), requestGeneration);
      IProgress<YouTubePublishingProgress> progress = new InlineProgress<YouTubePublishingProgress>(update =>
      {
        ReaderPublishingPhase phase = update.State switch
        {
          YouTubeEpisodeState.Rendering => ReaderPublishingPhase.RenderingEpisode,
          YouTubeEpisodeState.Uploading => ReaderPublishingPhase.Uploading,
          YouTubeEpisodeState.Uploaded => ReaderPublishingPhase.Checkpointing,
          _ => ReaderPublishingPhase.WaitingForOperation,
        };
        ReaderPublishingStatus status = update.State == YouTubeEpisodeState.Uploading
          ? ReaderPublishingStatus.Uploading
          : ReaderPublishingStatus.Rendering;
        Publish(new ReaderPublishingState(
          phase,
          status,
          update.EpisodeNumber,
          update.EpisodeCount,
          update.OverallFraction,
          ReaderPublishingDirective.None), requestGeneration);
      });
      YouTubePublishingJob completed = await coordinator.RunAsync(
        request.Job,
        request.OAuthConfiguration,
        request.Source.Language,
        (episode, episodeProgress, token) => RenderEpisodeAsync(
          request.Source,
          episode,
          request.Job.Plan,
          episodeProgress,
          token),
        progress,
        operation.CancellationToken).ConfigureAwait(false);
      ThrowIfStale(requestGeneration, operation.CancellationToken);
      Publish(new ReaderPublishingState(
        ReaderPublishingPhase.Complete,
        ReaderPublishingStatus.Complete,
        completed.Episodes.Count,
        completed.Episodes.Count,
        1d,
        ReaderPublishingDirective.NotifyCompletion), requestGeneration);
      return new ReaderPublishingResult(
        ReaderPublishingOutcome.Succeeded,
        ReaderPublishingStatus.Complete,
        ReaderPublishingDirective.NotifyCompletion,
        completed);
    }
    catch (OperationCanceledException)
    {
      bool current = IsCurrent(requestGeneration);
      if (current)
      {
        Publish(new ReaderPublishingState(
          ReaderPublishingPhase.Canceled,
          ReaderPublishingStatus.Canceled,
          0,
          request.Job.Episodes.Count,
          0d,
          ReaderPublishingDirective.ShowResumableWork), requestGeneration);
      }

      return new ReaderPublishingResult(
        current ? ReaderPublishingOutcome.Canceled : ReaderPublishingOutcome.Stale,
        ReaderPublishingStatus.Canceled,
        ReaderPublishingDirective.ShowResumableWork);
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException
      or System.Net.Http.HttpRequestException or Google.GoogleApiException or TimeoutException)
    {
      diagnostics.Warning($"Reader publishing failed: {ex}");
      YouTubePublishingJob? recovery = coordinator.LastKnownJob;
      ReaderPublishingStatus status = ex is PublishingReceiptPersistenceException
        ? ReaderPublishingStatus.ReceiptNotSaved
        : recovery?.Episodes.Any(episode => episode.UploadAttempted
          && string.IsNullOrWhiteSpace(episode.YouTubeVideoId) && string.IsNullOrWhiteSpace(episode.UploadSessionUri)) == true
          ? ReaderPublishingStatus.ReconciliationRequired : ReaderPublishingStatus.Failed;
      Publish(new ReaderPublishingState(
        ReaderPublishingPhase.Failed,
        status,
        0,
        request.Job.Episodes.Count,
        0d,
        ReaderPublishingDirective.ShowResumableWork), requestGeneration);
      return new ReaderPublishingResult(
        ReaderPublishingOutcome.Failed,
        status,
        ReaderPublishingDirective.ShowResumableWork,
        recovery);
    }
    finally
    {
      operation.Dispose();
      lock (sync)
      {
        if (ReferenceEquals(activeOperation, operation))
        {
          activeOperation = null;
        }
      }
    }
  }

  private async Task<string> RenderEpisodeAsync(
    ReaderPublishingSourceSnapshot source,
    YouTubePublishingEpisode episode,
    YouTubePublishingPlan plan,
    IProgress<double> progress,
    CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    ReadingSection section = source.DocumentSections[episode.SourceSectionIndex];
    IProgress<ReaderExportPreparationProgress> preparationProgress = new InlineProgress<ReaderExportPreparationProgress>(update =>
    {
      if (update.Phase == ReaderExportPreparationPhase.Timing)
      {
        progress.Report(0.20d);
      }
      else if (update.Phase == ReaderExportPreparationPhase.Complete)
      {
        progress.Report(0.35d);
      }
    });
    ReaderVideoExportSection prepared = await preparationService.PrepareVideoSectionAsync(
      section,
      source.NarrationProfile,
      source.Visuals.TextDirection,
      preparationProgress,
      cancellationToken).ConfigureAwait(false);
    ReaderVideoVisualSettings visuals = source.Visuals with
    {
      Format = plan.VideoFormat,
      CaptionStyle = plan.CaptionStyle,
    };
    IProgress<ReaderVideoExportProgress> videoProgress = new InlineProgress<ReaderVideoExportProgress>(update =>
    {
      (double start, double span) = update.Phase switch
      {
        ReaderVideoExportPhase.AcquiringRuntime => (0.35d, 0.08d),
        ReaderVideoExportPhase.RenderingFrames => (0.43d, 0.39d),
        ReaderVideoExportPhase.Encoding => (0.82d, 0.18d),
        _ => (0.35d, 0d),
      };
      progress.Report(start + (Math.Clamp(update.Fraction, 0d, 1d) * span));
    });
    await ReaderExportController.WriteAtomicallyAsync(
      episode.VideoPath,
      ".mp4",
      (temporaryPath, token) => exportEngine.ExportVideoAsync(
        [prepared],
        visuals,
        temporaryPath,
        videoProgress,
        token),
      cancellationToken).ConfigureAwait(false);
    progress.Report(1d);
    return episode.VideoPath;
  }

  private void Publish(ReaderPublishingState next, long requestGeneration)
  {
    EventHandler<ReaderPublishingState>? handler;
    lock (sync)
    {
      if (disposed || generation != requestGeneration)
      {
        return;
      }

      state = next;
      handler = StateChanged;
    }

    handler?.Invoke(this, next);
  }

  private bool IsCurrent(long requestGeneration)
  {
    lock (sync)
    {
      return !disposed && generation == requestGeneration;
    }
  }

  private void ThrowIfStale(long requestGeneration, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!IsCurrent(requestGeneration))
    {
      throw new OperationCanceledException("The publishing request was superseded.", cancellationToken);
    }
  }

  private static ReaderPublishingResult Busy() => new(
    ReaderPublishingOutcome.Busy,
    ReaderPublishingStatus.None,
    ReaderPublishingDirective.None);

  private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
  {
    public void Report(T value) => report(value);
  }
}
