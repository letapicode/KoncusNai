using DictateAnywhere.App.Workbench.Publishing;
using DictateAnywhere.App.Workbench.Reading;

namespace DictateAnywhere.App.Tests;

public sealed class YouTubePublishingTests
{
  [Xunit.Fact]
  public async Task CancellationAfterRemoteSuccessStillPersistsReceiptAndIgnoresLateProgress()
  {
    string path = Path.GetTempFileName();
    try
    {
      using CancellationTokenSource cancellation = new();
      YouTubePublishingJob job = YouTubePublishingJob.Create(CreatePlan(), [new(1, 0, "title", "description", path)]);
      MemoryJobStore store = new();
      IProgress<YouTubeUploadProgress>? lateProgress = null;
      ScriptedPublisher publisher = new((_, _, progress) =>
      {
        lateProgress = progress;
        cancellation.Cancel();
        return new YouTubeUploadResult("remote-id", "https://youtu.be/remote-id");
      });
      YouTubePublishingCoordinator coordinator = new(publisher, store);
      await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(
        job, new("client", "secret"), "en", (_, _, _) => Task.FromResult(path), cancellationToken: cancellation.Token));
      int saves = store.Saved.Count;
      lateProgress!.Report(new YouTubeUploadProgress(1, 2, "https://upload.example/stale"));
      Xunit.Assert.Equal(saves, store.Saved.Count);
      Xunit.Assert.Equal("remote-id", store.Saved[^1].Episodes[0].YouTubeVideoId);
      Xunit.Assert.Null(coordinator.LastKnownJob!.Episodes[0].UploadSessionUri);
    }
    finally { File.Delete(path); }
  }

  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public async Task SuccessfulUpload_RetainsReceiptDespiteCheckpointOrReceiptSaveFailure(bool failReceipt)
  {
    string path = Path.GetTempFileName();
    try
    {
      await File.WriteAllBytesAsync(path, [1]);
      YouTubePublishingJob job = YouTubePublishingJob.Create(CreatePlan(), [new(1, 0, "title", "description", path)]);
      MemoryJobStore store = new()
      {
        Failure = snapshot => snapshot.Episodes.Any(episode => failReceipt
          ? episode.YouTubeVideoId is not null : episode.UploadSessionUri is not null),
      };
      ScriptedPublisher publisher = new((_, _, progress) =>
      {
        progress?.Report(new YouTubeUploadProgress(1, 1, "https://upload.example/session"));
        return new YouTubeUploadResult("remote-id", "https://youtu.be/remote-id");
      });
      YouTubePublishingCoordinator coordinator = new(publisher, store);
      Task<YouTubePublishingJob> run = coordinator.RunAsync(job, new("client", "secret"), "en", (_, _, _) => Task.FromResult(path));
      if (failReceipt)
      {
        PublishingReceiptPersistenceException failure = await Xunit.Assert.ThrowsAsync<PublishingReceiptPersistenceException>(() => run);
        Xunit.Assert.Equal("remote-id", failure.Job.Episodes[0].YouTubeVideoId);
        Xunit.Assert.Equal("remote-id", coordinator.LastKnownJob!.Episodes[0].YouTubeVideoId);
        store.Failure = null;
        await coordinator.RunAsync(job, new("client", "secret"), "en", (_, _, _) => Task.FromResult(path));
      }
      else Xunit.Assert.Equal("remote-id", (await run).Episodes[0].YouTubeVideoId);
      Xunit.Assert.Equal(1, publisher.Calls);
      Xunit.Assert.Equal("remote-id", store.Saved[^1].Episodes[0].YouTubeVideoId);
    }
    finally { File.Delete(path); }
  }

  [Xunit.Fact]
  public async Task UncertainUploadWithoutSession_IsNotRetriedOrRestarted()
  {
    string path = Path.GetTempFileName();
    try
    {
      YouTubePublishingJob job = YouTubePublishingJob.Create(CreatePlan(), [new(1, 0, "title", "description", path)]);
      MemoryJobStore store = new();
      ScriptedPublisher publisher = new((_, _, _) => throw new IOException("response lost"));
      YouTubePublishingCoordinator coordinator = new(publisher, store);
      await Xunit.Assert.ThrowsAsync<IOException>(() => coordinator.RunAsync(job, new("client", "secret"), "en", (_, _, _) => Task.FromResult(path)));
      await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(store.Saved[^1], new("client", "secret"), "en", (_, _, _) => Task.FromResult(path)));
      Xunit.Assert.Equal(1, publisher.Calls);
    }
    finally { File.Delete(path); }
  }

  [Xunit.Fact]
  public void MetadataPolicy_ProducesSafeSeriesMetadata()
  {
    string title = YouTubeMetadataPolicy.CreateEpisodeTitle(new string('A', 130), 4, 12);
    IReadOnlyList<string> tags = YouTubeMetadataPolicy.ParseTags(" books, reading;Books, audio ");

    Xunit.Assert.True(title.Length <= 100);
    Xunit.Assert.EndsWith("Part 04", title, StringComparison.Ordinal);
    Xunit.Assert.Equal(["books", "reading", "audio"], tags);
  }

  [Xunit.Fact]
  public async Task Coordinator_RendersMissingMedia_ResumesExistingUpload_AndPersistsCompletion()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-publishing-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      string firstPath = Path.Combine(directory, "episode-0001.mp4");
      string secondPath = Path.Combine(directory, "episode-0002.mp4");
      await File.WriteAllBytesAsync(secondPath, [1, 2, 3]);
      DateTimeOffset firstPublication = DateTimeOffset.UtcNow.AddDays(2);
      YouTubePublishingPlan plan = CreatePlan() with
      {
        Privacy = YouTubePrivacy.Scheduled,
        FirstPublishAt = firstPublication,
        HoursBetweenEpisodes = 12,
      };
      YouTubePublishingJob job = YouTubePublishingJob.Create(plan,
      [
        new YouTubePublishingEpisode(1, 0, "Book · Part 01", "Part one", firstPath),
        new YouTubePublishingEpisode(2, 1, "Book · Part 02", "Part two", secondPath, YouTubeEpisodeState.Uploading, UploadSessionUri: "https://upload.example/session"),
      ]);
      FakeYouTubePublisher publisher = new();
      MemoryJobStore store = new();
      YouTubePublishingCoordinator coordinator = new(publisher, store);
      int renderCount = 0;

      YouTubePublishingJob completed = await coordinator.RunAsync(
        job,
        new YouTubeOAuthConfiguration("client-id", "secret"),
        "hi",
        async (episode, progress, cancellationToken) =>
        {
          renderCount++;
          await File.WriteAllBytesAsync(episode.VideoPath, [4, 5, 6], cancellationToken);
          progress.Report(1d);
          return episode.VideoPath;
        });

      Xunit.Assert.True(completed.IsComplete);
      Xunit.Assert.Equal(1, renderCount);
      Xunit.Assert.Equal(2, publisher.Requests.Count);
      Xunit.Assert.Null(publisher.Requests[0].ResumeSessionUri);
      Xunit.Assert.Equal("https://upload.example/session", publisher.Requests[1].ResumeSessionUri);
      Xunit.Assert.Equal(firstPublication, publisher.Requests[0].PublishAt);
      Xunit.Assert.Equal(firstPublication.AddHours(12), publisher.Requests[1].PublishAt);
      Xunit.Assert.All(completed.Episodes, episode => Xunit.Assert.Equal(YouTubeEpisodeState.Uploaded, episode.State));
      Xunit.Assert.True(store.Saved.Count >= 6);
      Xunit.Assert.True(store.Saved[^1].IsComplete);
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Xunit.Fact]
  public async Task Coordinator_RejectsPublishingWithoutRightsConfirmation()
  {
    YouTubePublishingPlan plan = CreatePlan() with { RightsConfirmed = false };
    YouTubePublishingJob job = YouTubePublishingJob.Create(plan,
    [
      new YouTubePublishingEpisode(1, 0, "Title", "Description", "missing.mp4"),
    ]);
    YouTubePublishingCoordinator coordinator = new(new FakeYouTubePublisher(), new MemoryJobStore());

    InvalidOperationException error = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(
      job,
      new YouTubeOAuthConfiguration("client-id", "secret"),
      "en",
      (_, _, _) => Task.FromResult("video.mp4")));

    Xunit.Assert.Contains("authorized", error.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task JobStore_ReloadsTheLatestIncompletePublishingJournal()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-publishing-store-{Guid.NewGuid():N}");
    try
    {
      YouTubePublishingJobStore store = new(directory);
      YouTubePublishingJob job = YouTubePublishingJob.Create(CreatePlan(),
      [
        new YouTubePublishingEpisode(1, 3, "Title", "Description", "episode.mp4", YouTubeEpisodeState.Uploading, UploadSessionUri: "https://upload.example/session"),
      ]) with { SourceFingerprint = "ABC123" };

      await store.SaveAsync(job);
      YouTubePublishingJob? loaded = await store.LoadLatestIncompleteAsync();

      Xunit.Assert.NotNull(loaded);
      Xunit.Assert.Equal(job.Id, loaded.Id);
      Xunit.Assert.Equal("ABC123", loaded.SourceFingerprint);
      Xunit.Assert.Equal("https://upload.example/session", loaded.Episodes[0].UploadSessionUri);
    }
    finally
    {
      if (Directory.Exists(directory))
      {
        Directory.Delete(directory, recursive: true);
      }
    }
  }

  [Xunit.Fact]
  public async Task JobStore_CleansTemporaryJournalWhenSaveIsCanceled()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-publishing-store-cancel-{Guid.NewGuid():N}");
    try
    {
      YouTubePublishingJobStore store = new(directory);
      YouTubePublishingJob job = YouTubePublishingJob.Create(CreatePlan(),
      [
        new YouTubePublishingEpisode(1, 0, "Title", "Description", "episode.mp4"),
      ]);
      using CancellationTokenSource cancellation = new();
      cancellation.Cancel();

      await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(job, cancellation.Token));

      Xunit.Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
    }
    finally
    {
      if (Directory.Exists(directory))
      {
        Directory.Delete(directory, recursive: true);
      }
    }
  }

  [Xunit.Fact]
  public async Task Coordinator_PersistsUploadCheckpointInOrder_AndReusesItForTransientRetry()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-publishing-retry-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      string videoPath = Path.Combine(directory, "episode.mp4");
      await File.WriteAllBytesAsync(videoPath, [1, 2, 3]);
      YouTubePublishingJob job = YouTubePublishingJob.Create(CreatePlan(),
      [
        new YouTubePublishingEpisode(1, 0, "Title", "Description", videoPath),
      ]);
      MemoryJobStore store = new();
      ScriptedPublisher publisher = new((call, request, progress) =>
      {
        if (call == 1)
        {
          progress?.Report(new YouTubeUploadProgress(1, 3, "https://upload.example/checkpoint"));
          throw new IOException("transient-upload-detail");
        }

        Xunit.Assert.Equal("https://upload.example/checkpoint", request.ResumeSessionUri);
        progress?.Report(new YouTubeUploadProgress(3, 3, request.ResumeSessionUri));
        return new YouTubeUploadResult("video-id", "https://youtu.be/video-id");
      });
      List<TimeSpan> delays = [];
      YouTubePublishingCoordinator coordinator = new(
        publisher,
        store,
        (delay, _) =>
        {
          delays.Add(delay);
          return Task.CompletedTask;
        });

      YouTubePublishingJob completed = await coordinator.RunAsync(
        job,
        new YouTubeOAuthConfiguration("client", "secret"),
        "en",
        (_, _, _) => Task.FromResult(videoPath));

      Xunit.Assert.True(completed.IsComplete);
      Xunit.Assert.Equal(2, publisher.Calls);
      Xunit.Assert.Equal([TimeSpan.FromSeconds(2)], delays);
      Xunit.Assert.Contains(store.Saved, saved => saved.Episodes[0].UploadSessionUri == "https://upload.example/checkpoint");
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Xunit.Fact]
  public async Task Coordinator_DoesNotRetryNonTransientFailure_AndPersistsSafeError()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-publishing-failure-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      string videoPath = Path.Combine(directory, "episode.mp4");
      await File.WriteAllBytesAsync(videoPath, [1]);
      YouTubePublishingJob job = YouTubePublishingJob.Create(CreatePlan(),
      [
        new YouTubePublishingEpisode(1, 0, "Title", "Description", videoPath),
      ]);
      MemoryJobStore store = new();
      ScriptedPublisher publisher = new((_, _, _) => throw new InvalidOperationException("secret-technical-detail"));
      YouTubePublishingCoordinator coordinator = new(
        publisher,
        store,
        (_, _) => Task.CompletedTask);

      InvalidOperationException failure = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(
        job,
        new YouTubeOAuthConfiguration("client", "secret"),
        "en",
        (_, _, _) => Task.FromResult(videoPath)));

      Xunit.Assert.Contains("secret-technical-detail", failure.Message, StringComparison.Ordinal);
      Xunit.Assert.Equal(1, publisher.Calls);
      YouTubePublishingEpisode persisted = store.Saved[^1].Episodes[0];
      Xunit.Assert.Equal(YouTubeEpisodeState.Failed, persisted.State);
      Xunit.Assert.Equal("Publishing stopped. Check YouTube before starting a new job; no recoverable upload session was saved.", persisted.Error);
      Xunit.Assert.DoesNotContain("secret-technical-detail", persisted.Error, StringComparison.Ordinal);
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  private static YouTubePublishingPlan CreatePlan() => new(
    "Book",
    "A reading series.",
    "books, reading",
    YouTubePrivacy.Private,
    null,
    24,
    MadeForKids: false,
    ContainsSyntheticMedia: true,
    RightsConfirmed: true,
    ReaderVideoFormat.YouTubeShort,
    ReaderVideoCaptionStyle.KineticBold);

  private sealed class FakeYouTubePublisher : IYouTubeVideoPublisher
  {
    public List<YouTubeUploadRequest> Requests { get; } = [];

    public Task ConnectAsync(YouTubeOAuthConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> HasStoredAuthorizationAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<YouTubeUploadResult> UploadAsync(
      YouTubeOAuthConfiguration configuration,
      YouTubeUploadRequest request,
      IProgress<YouTubeUploadProgress>? progress = null,
      CancellationToken cancellationToken = default)
    {
      Requests.Add(request);
      long length = new FileInfo(request.VideoPath).Length;
      progress?.Report(new YouTubeUploadProgress(length, length, request.ResumeSessionUri ?? $"https://upload.example/{Requests.Count}"));
      return Task.FromResult(new YouTubeUploadResult($"video-{Requests.Count}", $"https://youtu.be/video-{Requests.Count}"));
    }
  }

  private sealed class MemoryJobStore : IYouTubePublishingJobStore
  {
    public Func<YouTubePublishingJob, bool>? Failure { get; set; }
    public List<YouTubePublishingJob> Saved { get; } = [];

    public Task SaveAsync(YouTubePublishingJob job, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (Failure?.Invoke(job) == true) throw new IOException("injected persistence failure");
      Saved.Add(job with { Episodes = job.Episodes.ToArray() });
      return Task.CompletedTask;
    }

    public Task<YouTubePublishingJob?> LoadLatestIncompleteAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(Saved.LastOrDefault(job => !job.IsComplete));
  }

  private sealed class ScriptedPublisher(
    Func<int, YouTubeUploadRequest, IProgress<YouTubeUploadProgress>?, YouTubeUploadResult> upload) : IYouTubeVideoPublisher
  {
    public int Calls { get; private set; }

    public Task ConnectAsync(YouTubeOAuthConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> HasStoredAuthorizationAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<YouTubeUploadResult> UploadAsync(
      YouTubeOAuthConfiguration configuration,
      YouTubeUploadRequest request,
      IProgress<YouTubeUploadProgress>? progress = null,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Calls++;
      return Task.FromResult(upload(Calls, request, progress));
    }
  }
}
