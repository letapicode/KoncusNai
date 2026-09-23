using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using System.Diagnostics.CodeAnalysis;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "ReaderNarrationSession owns the alignment service and is disposed by the fixture.")]
public sealed class ReaderExportControllerTests : IAsyncDisposable
{
  private readonly string directory = Path.Combine(Path.GetTempPath(), $"notype-export-controller-{Guid.NewGuid():N}");
  private readonly ReaderOperationSession operations = new();
  private readonly RecordingDiagnostics diagnostics = new();
  private readonly ReaderNarrationSession narration;

  public ReaderExportControllerTests()
  {
    Directory.CreateDirectory(directory);
    narration = new ReaderNarrationSession(new SpeechService(directory), new AlignmentService());
  }

  [Xunit.Fact]
  public async Task AudioExport_PreparesInOrder_AndCommitsDestinationAtomically()
  {
    FakeExportEngine engine = new();
    await using ReaderExportController controller = CreateController(engine);
    string output = Path.Combine(directory, "book.wav");
    await File.WriteAllTextAsync(output, "existing");
    List<ReaderExportState> states = [];
    controller.StateChanged += (_, state) => states.Add(state);

    ReaderExportResult result = await controller.ExportAudioAsync(new ReaderAudioExportCommand(
      Sections("First section.", "Second section."),
      Profile(),
      output));

    Xunit.Assert.Equal(ReaderExportOutcome.Succeeded, result.Outcome);
    Xunit.Assert.Equal("audio", await File.ReadAllTextAsync(output));
    Xunit.Assert.Equal(ReaderExportPhase.WritingAudio, states[^2].Phase);
    Xunit.Assert.Equal(ReaderExportPhase.Complete, states[^1].Phase);
    Xunit.Assert.DoesNotContain(Directory.EnumerateFiles(directory), path => path.Contains(".partial", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task FailedExport_PreservesExistingDestination_CleansPartial_AndCanRetry()
  {
    FakeExportEngine engine = new() { Failure = new IOException("technical-export-detail") };
    await using ReaderExportController controller = CreateController(engine);
    string output = Path.Combine(directory, "book.wav");
    await File.WriteAllTextAsync(output, "existing");
    ReaderAudioExportCommand command = new(Sections("One section."), Profile(), output);

    ReaderExportResult failed = await controller.ExportAudioAsync(command);

    Xunit.Assert.Equal(ReaderExportOutcome.Failed, failed.Outcome);
    Xunit.Assert.Equal("existing", await File.ReadAllTextAsync(output));
    Xunit.Assert.Contains(diagnostics.Warnings, item => item.Contains("technical-export-detail", StringComparison.Ordinal));
    Xunit.Assert.DoesNotContain(Directory.EnumerateFiles(directory), path => path.Contains(".partial", StringComparison.Ordinal));

    engine.Failure = null;
    ReaderExportResult retried = await controller.ExportAudioAsync(command);
    Xunit.Assert.Equal(ReaderExportOutcome.Succeeded, retried.Outcome);
  }

  [Xunit.Fact]
  public async Task CancellationDuringWrite_PreservesDestination_AndReturnsCanceled()
  {
    FakeExportEngine engine = new() { Block = true };
    await using ReaderExportController controller = CreateController(engine);
    string output = Path.Combine(directory, "book.wav");
    await File.WriteAllTextAsync(output, "existing");

    Task<ReaderExportResult> task = controller.ExportAudioAsync(new ReaderAudioExportCommand(
      Sections("One section."),
      Profile(),
      output));
    await engine.Started;
    controller.Cancel();
    ReaderExportResult result = await task;

    Xunit.Assert.Equal(ReaderExportOutcome.Canceled, result.Outcome);
    Xunit.Assert.Equal("existing", await File.ReadAllTextAsync(output));
    Xunit.Assert.DoesNotContain(Directory.EnumerateFiles(directory), path => path.Contains(".partial", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task SharedOperationOwner_ReturnsBusyWithoutStartingExport()
  {
    FakeExportEngine engine = new();
    await using ReaderExportController controller = CreateController(engine);
    using ReaderOperationSession.ReaderOperation import = operations.TryBegin(ReaderOperationKind.DocumentImport)!;

    ReaderExportResult result = await controller.ExportAudioAsync(new ReaderAudioExportCommand(
      Sections("One section."),
      Profile(),
      Path.Combine(directory, "book.wav")));

    Xunit.Assert.Equal(ReaderExportOutcome.Busy, result.Outcome);
    Xunit.Assert.Equal(0, engine.AudioCalls);
  }

  [Xunit.Fact]
  public async Task VideoExport_MapsTypedEnginePhases_WithoutWpfControls()
  {
    FakeExportEngine engine = new();
    await using ReaderExportController controller = CreateController(engine);
    List<ReaderExportPhase> phases = [];
    controller.StateChanged += (_, state) => phases.Add(state.Phase);

    ReaderExportResult result = await controller.ExportVideoAsync(new ReaderVideoExportCommand(
      Sections("Hello world."),
      Profile(ReaderWordTimingStrategy.LocalForcedAlignment),
      Visuals(),
      Path.Combine(directory, "reading.mp4")));

    Xunit.Assert.Equal(ReaderExportOutcome.Succeeded, result.Outcome);
    Xunit.Assert.Contains(ReaderExportPhase.AcquiringVideoRuntime, phases);
    Xunit.Assert.Contains(ReaderExportPhase.RenderingFrames, phases);
    Xunit.Assert.Contains(ReaderExportPhase.Encoding, phases);
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

  private ReaderExportController CreateController(IReaderExportEngine engine) => new(
    operations,
    new ReaderExportPreparationService(narration),
    engine,
    diagnostics);

  private static IReadOnlyList<ReadingSection> Sections(params string[] text) => text
    .Select((value, index) => new ReadingSection(index, UnicodeReadingTextSegmenter.Segment(value)))
    .ToArray();

  private static ReaderNarrationProfile Profile(
    ReaderWordTimingStrategy strategy = ReaderWordTimingStrategy.NativeWithDeterministicFallback) => new(
      "en-us",
      "test",
      "voice",
      null,
      strategy);

  private static ReaderVideoVisualSettings Visuals() => new(
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

  private sealed class FakeExportEngine : IReaderExportEngine
  {
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? Failure { get; set; }
    public bool Block { get; set; }
    public int AudioCalls { get; private set; }
    public Task Started => started.Task;

    public async Task ExportAudioAsync(
      IReadOnlyList<string> sourcePaths,
      string outputPath,
      CancellationToken cancellationToken)
    {
      AudioCalls++;
      await File.WriteAllTextAsync(outputPath, "audio", cancellationToken);
      started.TrySetResult();
      if (Block)
      {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      }

      if (Failure is not null)
      {
        throw Failure;
      }
    }

    public async Task ExportVideoAsync(
      IReadOnlyList<ReaderVideoExportSection> sections,
      ReaderVideoVisualSettings settings,
      string outputPath,
      IProgress<ReaderVideoExportProgress>? progress,
      CancellationToken cancellationToken)
    {
      progress?.Report(new ReaderVideoExportProgress(ReaderVideoExportPhase.AcquiringRuntime, 1d, string.Empty));
      progress?.Report(new ReaderVideoExportProgress(ReaderVideoExportPhase.RenderingFrames, 1d, string.Empty));
      progress?.Report(new ReaderVideoExportProgress(ReaderVideoExportPhase.Encoding, 1d, string.Empty));
      await File.WriteAllTextAsync(outputPath, "video", cancellationToken);
    }
  }

  private sealed class SpeechService(string directory) : ITextToSpeechService
  {
    private int calls;

    public async Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      string path = Path.Combine(directory, $"speech-{++calls}.wav");
      await File.WriteAllBytesAsync(path, [], cancellationToken);
      return new TextToSpeechResult(path, TimeSpan.FromSeconds(1), 1, "test", "test");
    }
  }

  private sealed class AlignmentService : ISpeechAlignmentService
  {
    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default)
    {
      string[] words = request.Transcript.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      IReadOnlyList<SpeechWordTiming> timings = words
        .Select((word, index) => new SpeechWordTiming(
          word.TrimEnd('.', ',', '!', '?'),
          TimeSpan.FromMilliseconds(index * 400),
          TimeSpan.FromMilliseconds((index + 1) * 400)))
        .ToArray();
      return Task.FromResult(new SpeechAlignmentResult(timings, "test", TimeSpan.Zero));
    }

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
