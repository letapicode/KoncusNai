using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.Workbench.Publishing;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "ReaderNarrationSession owns the alignment service and is disposed by the fixture.")]
public sealed class ReaderPublishingControllerTests : IAsyncDisposable
{
  private readonly string directory = Path.Combine(Path.GetTempPath(), $"notype-publishing-controller-{Guid.NewGuid():N}");
  private readonly ReaderOperationSession operations = new();
  private readonly ReaderNarrationSession narration;
  private readonly MemoryJobStore store = new();
  private readonly RecordingDiagnostics diagnostics = new();

  public ReaderPublishingControllerTests()
  {
    Directory.CreateDirectory(directory);
    narration = new ReaderNarrationSession(new UnusedSpeechService(), new UnusedAlignmentService());
  }

  [Xunit.Fact]
  public async Task ReceiptSaveFailureReportsRemoteSuccessAndRecoversWithoutUploadingAgain()
  {
    SuccessfulPublisher publisher = new();
    await using ReaderPublishingController controller = CreateController(publisher);
    ReaderPublishingSourceSnapshot source = Source("source");
    YouTubePublishingJob job = Job(source, Path.Combine(directory, "episode.mp4"));
    await File.WriteAllBytesAsync(job.Episodes[0].VideoPath, [1]);
    store.FailReceipts = true;
    ReaderPublishingCommand command = new(job, new("client", "secret"), source);

    ReaderPublishingResult result = await controller.PublishAsync(command);

    Xunit.Assert.Equal(ReaderPublishingStatus.ReceiptNotSaved, result.Status);
    Xunit.Assert.Equal("video", result.Job!.Episodes[0].YouTubeVideoId);
    ReaderPublishingRecoveryResult recovery = await controller.FindRecoverableJobAsync(source);
    Xunit.Assert.Equal("video", recovery.Job!.Episodes[0].YouTubeVideoId);
    store.FailReceipts = false;
    Xunit.Assert.Equal(ReaderPublishingOutcome.Succeeded, (await controller.PublishAsync(command)).Outcome);
    Xunit.Assert.Equal(1, publisher.Calls);
  }

  [Xunit.Fact]
  public async Task Recovery_AcceptsExactSourceAndRejectsMismatchedSource()
  {
    ReaderPublishingSourceSnapshot source = Source("source-a");
    YouTubePublishingJob job = Job(source, Path.Combine(directory, "episode.mp4"));
    await store.SaveAsync(job);
    await using ReaderPublishingController controller = CreateController(new SuccessfulPublisher());

    ReaderPublishingRecoveryResult match = await controller.FindRecoverableJobAsync(source);
    ReaderPublishingRecoveryResult mismatch = await controller.FindRecoverableJobAsync(Source("source-b"));

    Xunit.Assert.Equal(ReaderPublishingRecoveryStatus.Compatible, match.Status);
    Xunit.Assert.Equal(job.Id, match.Job?.Id);
    Xunit.Assert.Equal(ReaderPublishingRecoveryStatus.Incompatible, mismatch.Status);
    Xunit.Assert.Null(mismatch.Job);
  }

  [Xunit.Fact]
  public async Task CreateAndPublish_UsesTypedOwnerAndCompletesJournal()
  {
    SuccessfulPublisher publisher = new();
    await using ReaderPublishingController controller = CreateController(publisher);
    ReaderPublishingSourceSnapshot source = Source("source");
    ReaderPublishingJobCreationResult creation = controller.CreateJob(Plan(), source);
    YouTubePublishingJob job = Xunit.Assert.IsType<YouTubePublishingJob>(creation.Job);
    await File.WriteAllBytesAsync(job.Episodes[0].VideoPath, [1, 2, 3]);
    List<ReaderPublishingState> states = [];
    controller.StateChanged += (_, state) => states.Add(state);

    ReaderPublishingResult result = await controller.PublishAsync(new ReaderPublishingCommand(
      job,
      new YouTubeOAuthConfiguration("client", "secret"),
      source));

    Xunit.Assert.Equal(ReaderPublishingOutcome.Succeeded, result.Outcome);
    Xunit.Assert.True(result.Job?.IsComplete);
    Xunit.Assert.Equal(1, publisher.Calls);
    Xunit.Assert.Equal(ReaderPublishingPhase.Complete, states[^1].Phase);
    Xunit.Assert.Null(operations.ActiveKind);
  }

  [Xunit.Fact]
  public async Task Publish_ReturnsBusy_WhenAnotherReaderOperationOwnsTheLease()
  {
    await using ReaderPublishingController controller = CreateController(new SuccessfulPublisher());
    ReaderPublishingSourceSnapshot source = Source("source");
    using ReaderOperationSession.ReaderOperation preparation = operations.TryBegin(ReaderOperationKind.SectionPreparation)!;

    ReaderPublishingResult result = await controller.PublishAsync(new ReaderPublishingCommand(
      Job(source, Path.Combine(directory, "episode.mp4")),
      new YouTubeOAuthConfiguration("client", "secret"),
      source));

    Xunit.Assert.Equal(ReaderPublishingOutcome.Busy, result.Outcome);
  }

  [Xunit.Fact]
  public async Task PublishFailure_ReportsTechnicalDetailButReturnsSafeResult()
  {
    await using ReaderPublishingController controller = CreateController(new FailingPublisher());
    ReaderPublishingSourceSnapshot source = Source("source");
    YouTubePublishingJob job = Job(source, Path.Combine(directory, "episode.mp4"));
    await File.WriteAllBytesAsync(job.Episodes[0].VideoPath, [1]);

    ReaderPublishingResult result = await controller.PublishAsync(new ReaderPublishingCommand(
      job,
      new YouTubeOAuthConfiguration("client", "secret"),
      source));

    Xunit.Assert.Equal(ReaderPublishingOutcome.Failed, result.Outcome);
    Xunit.Assert.Equal(ReaderPublishingStatus.ReconciliationRequired, result.Status);
    Xunit.Assert.Contains(diagnostics.Warnings, item => item.Contains("publisher-technical-detail", StringComparison.Ordinal));
    Xunit.Assert.DoesNotContain("publisher-technical-detail", store.Saved[^1].Episodes[0].Error, StringComparison.Ordinal);
  }

  public async ValueTask DisposeAsync()
  {
    operations.Dispose();
    await narration.DisposeAsync();
    if (Directory.Exists(directory))
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  private ReaderPublishingController CreateController(IYouTubeVideoPublisher publisher) => new(
    operations,
    new ReaderExportPreparationService(narration),
    new UnusedExportEngine(),
    new YouTubePublishingCoordinator(publisher, store, (_, _) => Task.CompletedTask),
    store,
    diagnostics,
    Path.Combine(directory, "media"));

  private static ReaderPublishingSourceSnapshot Source(string sourceText)
  {
    IReadOnlyList<ReadingSection> sections =
    [
      new ReadingSection(0, UnicodeReadingTextSegmenter.Segment("A reading section.")),
    ];
    ReaderNarrationProfile profile = new(
      "en-us",
      "test",
      "voice",
      null,
      ReaderWordTimingStrategy.NativeWithDeterministicFallback);
    ReaderVideoVisualSettings visuals = new(
      "Segoe UI",
      ReadingHighlightMode.Word,
      ReaderHighlightVisualStyle.ReaderPage,
      "#D4A94F",
      ReaderThemeOption.Defaults[0],
      ReaderVideoFormat.Landscape,
      ReaderVideoCaptionStyle.ReaderPage,
      23d,
      ReaderLineMetricsProfile.Standard,
      ReaderTextDirection.LeftToRight);
    return ReaderPublishingSourceSnapshot.Create(sourceText, sections, [0], profile, "en", visuals);
  }

  private static YouTubePublishingJob Job(ReaderPublishingSourceSnapshot source, string videoPath) => new(
    Guid.NewGuid().ToString("N"),
    DateTimeOffset.UtcNow,
    Plan(),
    [new YouTubePublishingEpisode(1, 0, "Title", "Description", videoPath)],
    SourceFingerprint: source.SourceFingerprint);

  private static YouTubePublishingPlan Plan() => new(
    "Book",
    "Description",
    "reading",
    YouTubePrivacy.Private,
    null,
    24,
    MadeForKids: false,
    ContainsSyntheticMedia: true,
    RightsConfirmed: true,
    ReaderVideoFormat.Landscape,
    ReaderVideoCaptionStyle.ReaderPage);

  private sealed class SuccessfulPublisher : IYouTubeVideoPublisher
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
      Calls++;
      progress?.Report(new YouTubeUploadProgress(1, 1, "https://upload.example/session"));
      return Task.FromResult(new YouTubeUploadResult("video", "https://youtu.be/video"));
    }
  }

  private sealed class FailingPublisher : IYouTubeVideoPublisher
  {
    public Task ConnectAsync(YouTubeOAuthConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> HasStoredAuthorizationAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<YouTubeUploadResult> UploadAsync(
      YouTubeOAuthConfiguration configuration,
      YouTubeUploadRequest request,
      IProgress<YouTubeUploadProgress>? progress = null,
      CancellationToken cancellationToken = default) =>
      Task.FromException<YouTubeUploadResult>(new InvalidOperationException("publisher-technical-detail"));
  }

  private sealed class MemoryJobStore : IYouTubePublishingJobStore
  {
    public bool FailReceipts { get; set; }
    public List<YouTubePublishingJob> Saved { get; } = [];

    public Task SaveAsync(YouTubePublishingJob job, CancellationToken cancellationToken = default)
    {
      if (FailReceipts && job.Episodes.Any(episode => !string.IsNullOrWhiteSpace(episode.YouTubeVideoId)))
        throw new IOException("Receipt storage unavailable.");
      Saved.Add(job with { Episodes = job.Episodes.ToArray() });
      return Task.CompletedTask;
    }

    public Task<YouTubePublishingJob?> LoadLatestIncompleteAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(Saved.LastOrDefault(item => !item.IsComplete));
  }

  private sealed class UnusedExportEngine : IReaderExportEngine
  {
    public Task ExportAudioAsync(IReadOnlyList<string> sourcePaths, string outputPath, CancellationToken cancellationToken) =>
      throw new InvalidOperationException("Export is not expected.");

    public Task ExportVideoAsync(
      IReadOnlyList<ReaderVideoExportSection> sections,
      ReaderVideoVisualSettings settings,
      string outputPath,
      IProgress<ReaderVideoExportProgress>? progress,
      CancellationToken cancellationToken) =>
      throw new InvalidOperationException("Export is not expected.");
  }

  private sealed class UnusedSpeechService : ITextToSpeechService
  {
    public Task<TextToSpeechResult> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default) =>
      throw new InvalidOperationException("Speech is not expected.");
  }

  private sealed class UnusedAlignmentService : ISpeechAlignmentService
  {
    public Task<SpeechAlignmentResult> AlignAsync(SpeechAlignmentRequest request, CancellationToken cancellationToken = default) =>
      throw new InvalidOperationException("Alignment is not expected.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<string> Warnings { get; } = [];
    public void Info(string message) { }
    public void Warning(string message) => Warnings.Add(message);
    public void Error(string message, Exception? exception = null) { }
  }
}
