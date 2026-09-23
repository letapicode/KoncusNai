using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

internal enum ReaderExportKind
{
  Audio,
  Video,
}

internal enum ReaderExportPhase
{
  Idle,
  WaitingForOperation,
  PreparingNarration,
  PreparingTiming,
  WritingAudio,
  AcquiringVideoRuntime,
  RenderingFrames,
  Encoding,
  Complete,
  Canceled,
  Failed,
}

internal enum ReaderExportOutcome
{
  Succeeded,
  Busy,
  Canceled,
  Failed,
  Stale,
}

internal enum ReaderExportStatus
{
  None,
  PreparingAudio,
  PreparingVideo,
  WritingAudio,
  AcquiringVideoRuntime,
  RenderingVideo,
  EncodingVideo,
  AudioComplete,
  VideoComplete,
  AudioCanceled,
  VideoCanceled,
  AudioFailed,
  VideoFailed,
}

[Flags]
internal enum ReaderExportDirective
{
  None = 0,
  StopVoicePreview = 1,
  NotifyCompletion = 2,
}

internal sealed record ReaderVideoVisualSettings(
  string FontFamilyName,
  ReadingHighlightMode HighlightMode,
  ReaderHighlightVisualStyle HighlightStyle,
  string HighlightColor,
  ReaderThemeOption Theme,
  ReaderVideoFormat Format,
  ReaderVideoCaptionStyle CaptionStyle,
  double FontSize,
  ReaderLineMetricsProfile LineMetricsProfile,
  ReaderTextDirection TextDirection);

internal sealed record ReaderAudioExportCommand(
  IReadOnlyList<ReadingSection> Sections,
  ReaderNarrationProfile NarrationProfile,
  string OutputPath);

internal sealed record ReaderVideoExportCommand(
  IReadOnlyList<ReadingSection> Sections,
  ReaderNarrationProfile NarrationProfile,
  ReaderVideoVisualSettings Visuals,
  string OutputPath);

internal sealed record ReaderExportState(
  ReaderExportKind Kind,
  ReaderExportPhase Phase,
  ReaderExportStatus Status,
  int CompletedSections,
  int TotalSections,
  double Progress,
  bool IsCached,
  ReaderExportDirective Directives)
{
  public static ReaderExportState Idle { get; } = new(
    ReaderExportKind.Audio,
    ReaderExportPhase.Idle,
    ReaderExportStatus.None,
    0,
    0,
    0d,
    false,
    ReaderExportDirective.None);
}

internal sealed record ReaderExportResult(
  ReaderExportOutcome Outcome,
  ReaderExportStatus Status,
  ReaderExportDirective Directives,
  string? OutputPath = null);

internal interface IReaderExportEngine
{
  Task ExportAudioAsync(
    IReadOnlyList<string> sourcePaths,
    string outputPath,
    CancellationToken cancellationToken);

  Task ExportVideoAsync(
    IReadOnlyList<ReaderVideoExportSection> sections,
    ReaderVideoVisualSettings settings,
    string outputPath,
    IProgress<ReaderVideoExportProgress>? progress,
    CancellationToken cancellationToken);
}

/// <summary>
/// Owns audio/video export arbitration, cancellation, progress policy, safe failure handling,
/// and same-directory temporary artifact publication. The operation and preparation services are borrowed.
/// </summary>
internal sealed class ReaderExportController : IAsyncDisposable
{
  private readonly object sync = new();
  private readonly ReaderOperationSession operationSession;
  private readonly ReaderExportPreparationService preparationService;
  private readonly IReaderExportEngine exportEngine;
  private readonly IDiagnostics diagnostics;
  private ReaderOperationSession.ReaderOperation? activeOperation;
  private Task<ReaderExportResult>? activeTask;
  private ReaderExportState state = ReaderExportState.Idle;
  private long generation;
  private bool disposed;

  public ReaderExportController(
    ReaderOperationSession operationSession,
    ReaderExportPreparationService preparationService,
    IReaderExportEngine exportEngine,
    IDiagnostics diagnostics)
  {
    this.operationSession = operationSession ?? throw new ArgumentNullException(nameof(operationSession));
    this.preparationService = preparationService ?? throw new ArgumentNullException(nameof(preparationService));
    this.exportEngine = exportEngine ?? throw new ArgumentNullException(nameof(exportEngine));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  public event EventHandler<ReaderExportState>? StateChanged;

  public ReaderExportState State
  {
    get
    {
      lock (sync)
      {
        return state;
      }
    }
  }

  public Task<ReaderExportResult> ExportAudioAsync(ReaderAudioExportCommand request)
  {
    ArgumentNullException.ThrowIfNull(request);
    Validate(request.Sections, request.NarrationProfile, request.OutputPath, ".wav");
    return Begin(
      ReaderExportKind.Audio,
      ReaderOperationKind.AudioExport,
      (generation, operation) => ExecuteAudioAsync(request, generation, operation));
  }

  public Task<ReaderExportResult> ExportVideoAsync(ReaderVideoExportCommand request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Visuals);
    Validate(request.Sections, request.NarrationProfile, request.OutputPath, ".mp4");
    return Begin(
      ReaderExportKind.Video,
      ReaderOperationKind.VideoExport,
      (generation, operation) => ExecuteVideoAsync(request, generation, operation));
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
    Task<ReaderExportResult>? task;
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
    }

    operation?.Cancel();
    if (task is not null)
    {
      _ = await task.ConfigureAwait(false);
    }
  }

  private Task<ReaderExportResult> Begin(
    ReaderExportKind kind,
    ReaderOperationKind operationKind,
    Func<long, ReaderOperationSession.ReaderOperation, Task<ReaderExportResult>> execute)
  {
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (activeTask is { IsCompleted: false })
      {
        return Task.FromResult(Busy());
      }

      ReaderOperationSession.ReaderOperation? operation = operationSession.TryBegin(operationKind);
      if (operation is null)
      {
        return Task.FromResult(Busy());
      }

      long requestGeneration = ++generation;
      activeOperation = operation;
      activeTask = ExecuteAndReleaseAsync(kind, requestGeneration, operation, execute);
      return activeTask;
    }
  }

  private async Task<ReaderExportResult> ExecuteAndReleaseAsync(
    ReaderExportKind kind,
    long requestGeneration,
    ReaderOperationSession.ReaderOperation operation,
    Func<long, ReaderOperationSession.ReaderOperation, Task<ReaderExportResult>> execute)
  {
    await Task.Yield();
    try
    {
      return await execute(requestGeneration, operation).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      bool current = IsCurrent(requestGeneration);
      ReaderExportStatus status = kind == ReaderExportKind.Audio
        ? ReaderExportStatus.AudioCanceled
        : ReaderExportStatus.VideoCanceled;
      if (current)
      {
        Publish(new ReaderExportState(
          kind,
          ReaderExportPhase.Canceled,
          status,
          0,
          0,
          0d,
          false,
          ReaderExportDirective.None), requestGeneration);
      }

      return new ReaderExportResult(
        current ? ReaderExportOutcome.Canceled : ReaderExportOutcome.Stale,
        status,
        ReaderExportDirective.None);
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
    {
      diagnostics.Warning($"Reader {kind.ToString().ToLowerInvariant()} export failed: {ex}");
      ReaderExportStatus status = kind == ReaderExportKind.Audio
        ? ReaderExportStatus.AudioFailed
        : ReaderExportStatus.VideoFailed;
      Publish(new ReaderExportState(
        kind,
        ReaderExportPhase.Failed,
        status,
        0,
        0,
        0d,
        false,
        ReaderExportDirective.None), requestGeneration);
      return new ReaderExportResult(ReaderExportOutcome.Failed, status, ReaderExportDirective.None);
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

  private async Task<ReaderExportResult> ExecuteAudioAsync(
    ReaderAudioExportCommand request,
    long requestGeneration,
    ReaderOperationSession.ReaderOperation operation)
  {
    Publish(new ReaderExportState(
      ReaderExportKind.Audio,
      ReaderExportPhase.WaitingForOperation,
      ReaderExportStatus.PreparingAudio,
      0,
      request.Sections.Count,
      0d,
      false,
      ReaderExportDirective.StopVoicePreview), requestGeneration);
    IProgress<ReaderExportPreparationProgress> progress = new InlineProgress<ReaderExportPreparationProgress>(update =>
    {
      double sectionEnd = 0.90d * update.SectionNumber / update.SectionCount;
      Publish(new ReaderExportState(
        ReaderExportKind.Audio,
        ReaderExportPhase.PreparingNarration,
        ReaderExportStatus.PreparingAudio,
        update.SectionIndex,
        update.SectionCount,
        sectionEnd,
        update.IsCached,
        ReaderExportDirective.None), requestGeneration);
    });
    IReadOnlyList<TextToSpeechResult> prepared = await preparationService.PrepareAudioAsync(
      request.Sections,
      request.NarrationProfile,
      progress,
      operation.CancellationToken).ConfigureAwait(false);
    ThrowIfStale(requestGeneration, operation.CancellationToken);
    Publish(new ReaderExportState(
      ReaderExportKind.Audio,
      ReaderExportPhase.WritingAudio,
      ReaderExportStatus.WritingAudio,
      request.Sections.Count,
      request.Sections.Count,
      0.94d,
      false,
      ReaderExportDirective.None), requestGeneration);
    await WriteAtomicallyAsync(
      request.OutputPath,
      ".wav",
      (temporaryPath, token) => exportEngine.ExportAudioAsync(
        prepared.Select(item => item.AudioPath).ToArray(),
        temporaryPath,
        token),
      operation.CancellationToken).ConfigureAwait(false);
    ThrowIfStale(requestGeneration, operation.CancellationToken);
    return Complete(ReaderExportKind.Audio, ReaderExportStatus.AudioComplete, request.OutputPath, request.Sections.Count, requestGeneration);
  }

  private async Task<ReaderExportResult> ExecuteVideoAsync(
    ReaderVideoExportCommand request,
    long requestGeneration,
    ReaderOperationSession.ReaderOperation operation)
  {
    Publish(new ReaderExportState(
      ReaderExportKind.Video,
      ReaderExportPhase.WaitingForOperation,
      ReaderExportStatus.PreparingVideo,
      0,
      request.Sections.Count,
      0d,
      false,
      ReaderExportDirective.StopVoicePreview), requestGeneration);
    IProgress<ReaderExportPreparationProgress> preparationProgress = new InlineProgress<ReaderExportPreparationProgress>(update =>
    {
      double sectionStart = 0.42d * update.SectionIndex / update.SectionCount;
      double narrationEnd = 0.42d * (update.SectionIndex + 0.58d) / update.SectionCount;
      double sectionEnd = 0.42d * update.SectionNumber / update.SectionCount;
      ReaderExportPhase phase = update.Phase == ReaderExportPreparationPhase.Timing
        ? ReaderExportPhase.PreparingTiming
        : ReaderExportPhase.PreparingNarration;
      double mapped = update.Phase switch
      {
        ReaderExportPreparationPhase.Narration => narrationEnd,
        ReaderExportPreparationPhase.Timing => sectionEnd,
        ReaderExportPreparationPhase.Complete => sectionEnd,
        _ => sectionStart,
      };
      Publish(new ReaderExportState(
        ReaderExportKind.Video,
        phase,
        ReaderExportStatus.PreparingVideo,
        update.SectionIndex,
        update.SectionCount,
        mapped,
        update.IsCached,
        ReaderExportDirective.None), requestGeneration);
    });
    IReadOnlyList<ReaderVideoExportSection> prepared = await preparationService.PrepareVideoAsync(
      request.Sections,
      request.NarrationProfile,
      request.Visuals.TextDirection,
      preparationProgress,
      operation.CancellationToken).ConfigureAwait(false);
    ThrowIfStale(requestGeneration, operation.CancellationToken);
    IProgress<ReaderVideoExportProgress> videoProgress = new InlineProgress<ReaderVideoExportProgress>(update =>
    {
      (ReaderExportPhase phase, ReaderExportStatus status, double start, double span) = update.Phase switch
      {
        ReaderVideoExportPhase.AcquiringRuntime => (ReaderExportPhase.AcquiringVideoRuntime, ReaderExportStatus.AcquiringVideoRuntime, 0.42d, 0.10d),
        ReaderVideoExportPhase.RenderingFrames => (ReaderExportPhase.RenderingFrames, ReaderExportStatus.RenderingVideo, 0.52d, 0.30d),
        ReaderVideoExportPhase.Encoding => (ReaderExportPhase.Encoding, ReaderExportStatus.EncodingVideo, 0.82d, 0.18d),
        _ => throw new ArgumentOutOfRangeException(nameof(update), update.Phase, "Unknown video export phase."),
      };
      Publish(new ReaderExportState(
        ReaderExportKind.Video,
        phase,
        status,
        request.Sections.Count,
        request.Sections.Count,
        start + (Math.Clamp(update.Fraction, 0d, 1d) * span),
        false,
        ReaderExportDirective.None), requestGeneration);
    });
    await WriteAtomicallyAsync(
      request.OutputPath,
      ".mp4",
      (temporaryPath, token) => exportEngine.ExportVideoAsync(
        prepared,
        request.Visuals,
        temporaryPath,
        videoProgress,
        token),
      operation.CancellationToken).ConfigureAwait(false);
    ThrowIfStale(requestGeneration, operation.CancellationToken);
    return Complete(ReaderExportKind.Video, ReaderExportStatus.VideoComplete, request.OutputPath, request.Sections.Count, requestGeneration);
  }

  internal static async Task WriteAtomicallyAsync(
    string outputPath,
    string extension,
    Func<string, CancellationToken, Task> write,
    CancellationToken cancellationToken)
  {
    string fullPath = Path.GetFullPath(outputPath);
    string directory = Path.GetDirectoryName(fullPath)
      ?? throw new InvalidOperationException("The export destination has no directory.");
    Directory.CreateDirectory(directory);
    string temporaryPath = Path.Combine(
      directory,
      $".{Path.GetFileNameWithoutExtension(fullPath)}.{Guid.NewGuid():N}.partial{extension}");
    try
    {
      await write(temporaryPath, cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      File.Move(temporaryPath, fullPath, overwrite: true);
    }
    finally
    {
      TryDelete(temporaryPath);
    }
  }

  private ReaderExportResult Complete(
    ReaderExportKind kind,
    ReaderExportStatus status,
    string outputPath,
    int sectionCount,
    long requestGeneration)
  {
    Publish(new ReaderExportState(
      kind,
      ReaderExportPhase.Complete,
      status,
      sectionCount,
      sectionCount,
      1d,
      false,
      ReaderExportDirective.NotifyCompletion), requestGeneration);
    return new ReaderExportResult(
      ReaderExportOutcome.Succeeded,
      status,
      ReaderExportDirective.NotifyCompletion,
      outputPath);
  }

  private void Publish(ReaderExportState next, long requestGeneration)
  {
    EventHandler<ReaderExportState>? handler;
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
      throw new OperationCanceledException("The export request was superseded.", cancellationToken);
    }
  }

  private static ReaderExportResult Busy() => new(
    ReaderExportOutcome.Busy,
    ReaderExportStatus.None,
    ReaderExportDirective.None);

  private static void Validate(
    IReadOnlyList<ReadingSection> sections,
    ReaderNarrationProfile profile,
    string outputPath,
    string extension)
  {
    ArgumentNullException.ThrowIfNull(sections);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
    if (sections.Count == 0)
    {
      throw new ArgumentException("At least one reading section is required.", nameof(sections));
    }

    if (!string.Equals(Path.GetExtension(outputPath), extension, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException($"The export destination must use the {extension} extension.");
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
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
  {
    public void Report(T value) => report(value);
  }
}
