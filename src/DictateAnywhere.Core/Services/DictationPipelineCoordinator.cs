using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.Core.Services;

public sealed class DictationPipelineCoordinator : IAsyncDisposable
{
  private readonly IHotkeyService hotkeyService;
  private readonly IAudioCaptureService audioCaptureService;
  private readonly ITranscriptionService transcriptionService;
  private readonly ITextInsertionService textInsertionService;
  private readonly ITextTransformationService textTransformationService;
  private readonly IOverlayService overlayService;
  private readonly ISettingsStore settingsStore;
  private readonly IDiagnostics diagnostics;
  private readonly IDictationHistoryRecorder historyRecorder;
  private readonly ITextInsertionTargetSession? insertionTargetSession;

  private readonly DictationSessionStateMachine stateMachine = new();
  private readonly SemaphoreSlim signalLock = new(1, 1);
  private readonly object runtimeSync = new();
  private CancellationTokenSource? runtimeCts;
  private ChunkedTranscriptionSession? chunkedTranscriptionSession;

  private int pipelineBusy;
  private int started;
  private string? activeOperationId;
  private AppSettings settings = AppSettings.Default;
  private AppSettings? activeSessionSettings;
  private bool disposed;

  public DictationPipelineCoordinator(
    IHotkeyService hotkeyService,
    IAudioCaptureService audioCaptureService,
    ITranscriptionService transcriptionService,
    ITextInsertionService textInsertionService,
    ITextTransformationService textTransformationService,
    IOverlayService overlayService,
    ISettingsStore settingsStore,
    IDiagnostics diagnostics,
    IDictationHistoryRecorder historyRecorder)
  {
    this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
    this.audioCaptureService = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
    this.transcriptionService = transcriptionService ?? throw new ArgumentNullException(nameof(transcriptionService));
    this.textInsertionService = textInsertionService ?? throw new ArgumentNullException(nameof(textInsertionService));
    this.textTransformationService = textTransformationService ?? throw new ArgumentNullException(nameof(textTransformationService));
    this.overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));
    this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.historyRecorder = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
    insertionTargetSession = textInsertionService as ITextInsertionTargetSession;
  }

  public DictationSessionState CurrentState => stateMachine.CurrentState;

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);

    if (Interlocked.Exchange(ref started, 1) == 1)
    {
      return;
    }

    lock (runtimeSync)
    {
      runtimeCts = new CancellationTokenSource();
    }

    settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);

    HotkeyRegistrationResult registrationResult = await hotkeyService
      .RegisterAsync(settings.Hotkey, cancellationToken)
      .ConfigureAwait(false);

    if (!registrationResult.Success)
    {
      Interlocked.Exchange(ref started, 0);
      CancelAndDisposeRuntimeTokenSource();
      string message = registrationResult.ErrorMessage ?? "Unable to register configured hotkey.";
      diagnostics.Error(message);
      await overlayService
        .ShowStateAsync(
          DictationSessionState.Error,
          DictationStatusMessages.HotkeyUnavailable,
          display: OverlayDisplayOptions.StatusPanelDefault,
          cancellationToken: cancellationToken)
        .ConfigureAwait(false);
      throw new InvalidOperationException(message);
    }

    hotkeyService.HotkeyPressed += OnHotkeyPressed;
    hotkeyService.HotkeyReleased += OnHotkeyReleased;

    diagnostics.Info($"Dictation coordinator started in {settings.RecordingMode} mode.");
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    await StopCoreAsync(cancellationToken).ConfigureAwait(false);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;

    await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
    signalLock.Dispose();
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Shutdown path must tolerate and isolate downstream service failures.")]
  private async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    if (Interlocked.Exchange(ref started, 0) == 0)
    {
      return;
    }

    hotkeyService.HotkeyPressed -= OnHotkeyPressed;
    hotkeyService.HotkeyReleased -= OnHotkeyReleased;

    CancelAndDisposeRuntimeTokenSource();

    await signalLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (audioCaptureService.IsCapturing)
      {
        try
        {
          if (audioCaptureService is IChunkedAudioCaptureService chunkedAudioCaptureService)
          {
            _ = await chunkedAudioCaptureService.StopAndFlushChunkAsync(cancellationToken).ConfigureAwait(false);
          }
          else
          {
            _ = await audioCaptureService.StopAsync(cancellationToken).ConfigureAwait(false);
          }
        }
        catch (OperationCanceledException)
        {
          throw;
        }
        catch (Exception ex)
        {
          diagnostics.Warning($"Ignoring capture stop failure during shutdown: {ex.Message}");
        }
      }

      try
      {
        await hotkeyService.UnregisterAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        diagnostics.Warning($"Ignoring hotkey unregister failure during shutdown: {ex.Message}");
      }

      try
      {
        await overlayService.HideAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        diagnostics.Warning($"Ignoring overlay hide failure during shutdown: {ex.Message}");
      }

      stateMachine.Reset();
      await DisposeChunkedTranscriptionSessionAsync().ConfigureAwait(false);
      insertionTargetSession?.ClearCapturedTarget();
      diagnostics.Info("Dictation coordinator stopped.");
    }
    finally
    {
      Volatile.Write(ref pipelineBusy, 0);
      signalLock.Release();
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Hotkey callbacks are genuine event boundaries; all accepted pipeline work is awaited and final failures are reported.")]
  private async void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
  {
    try
    {
      await HandleHotkeySignalAsync(HotkeySignal.Pressed, e.ObservedAtUtc).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected failure while handling the dictation hotkey press.", ex);
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Hotkey callbacks are genuine event boundaries; all accepted pipeline work is awaited and final failures are reported.")]
  private async void OnHotkeyReleased(object? sender, HotkeyEventArgs e)
  {
    try
    {
      await HandleHotkeySignalAsync(HotkeySignal.Released, e.ObservedAtUtc).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected failure while handling the dictation hotkey release.", ex);
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Coordinator boundary catches unexpected pipeline exceptions and transitions to a safe state.")]
  private async Task HandleHotkeySignalAsync(HotkeySignal signal, DateTimeOffset observedAtUtc)
  {
    if (Interlocked.CompareExchange(ref started, 1, 1) == 0 || disposed)
    {
      return;
    }

    if (Volatile.Read(ref pipelineBusy) == 1)
    {
      diagnostics.Info($"Dropped reentrant {signal} signal at {observedAtUtc:O} while pipeline is busy.");
      return;
    }

    try
    {
      await signalLock.WaitAsync(GetRuntimeToken()).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return;
    }

    try
    {
      await ProcessHotkeySignalAsync(signal).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      await HandlePipelineErrorAsync("Dictation operation was canceled.", null).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      await HandlePipelineErrorAsync("Dictation failed. Check logs for details.", ex).ConfigureAwait(false);
    }
    finally
    {
      signalLock.Release();
    }
  }

  private Task ProcessHotkeySignalAsync(HotkeySignal signal)
  {
    return settings.RecordingMode switch
    {
      RecordingMode.HoldToTalk => ProcessHoldToTalkSignalAsync(signal),
      RecordingMode.ToggleToTalk => ProcessToggleToTalkSignalAsync(signal),
      _ => Task.CompletedTask,
    };
  }

  private Task ProcessHoldToTalkSignalAsync(HotkeySignal signal)
  {
    return signal switch
    {
      HotkeySignal.Pressed => StartRecordingIfIdleAsync(),
      HotkeySignal.Released => StopTranscribeAndInsertIfRecordingAsync(),
      _ => Task.CompletedTask,
    };
  }

  private Task ProcessToggleToTalkSignalAsync(HotkeySignal signal)
  {
    if (signal != HotkeySignal.Pressed)
    {
      return Task.CompletedTask;
    }

    return stateMachine.CurrentState switch
    {
      DictationSessionState.Idle => StartRecordingIfIdleAsync(),
      DictationSessionState.Recording => StopTranscribeAndInsertIfRecordingAsync(),
      _ => Task.CompletedTask,
    };
  }

  private async Task StartRecordingIfIdleAsync()
  {
    if (stateMachine.CurrentState != DictationSessionState.Idle)
    {
      diagnostics.Info($"Ignoring start request while state is {stateMachine.CurrentState}.");
      return;
    }

    if (!stateMachine.TryTransition(DictationSessionState.Recording))
    {
      diagnostics.Warning("Could not transition to Recording state.");
      return;
    }

    insertionTargetSession?.CaptureCurrentTarget();
    await DisposeChunkedTranscriptionSessionAsync().ConfigureAwait(false);
    AppSettings effectiveSettings = settings;
    activeSessionSettings = effectiveSettings;

    if (audioCaptureService is IChunkedAudioCaptureService chunkedAudioCaptureService)
    {
      chunkedTranscriptionSession = new ChunkedTranscriptionSession(
        chunkedAudioCaptureService,
        transcriptionService,
        effectiveSettings.GetConfiguredTranscriptionModelId(),
        diagnostics,
        GetRuntimeToken());
      chunkedTranscriptionSession.Start();
      diagnostics.Info("Chunked transcription session started.");
    }

    await overlayService
      .ShowStateAsync(
        DictationSessionState.Recording,
        elapsed: TimeSpan.Zero,
        display: OverlayDisplayOptions.AnchoredRecording,
        cancellationToken: GetRuntimeToken())
      .ConfigureAwait(false);

    await (audioCaptureService is IChunkedAudioCaptureService chunkedCapture
      ? chunkedCapture.StartChunkedAsync(GetRuntimeToken())
      : audioCaptureService.StartAsync(GetRuntimeToken())).ConfigureAwait(false);
    activeOperationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    Dictionary<string, object?> startProperties = new(StringComparer.Ordinal)
    {
      ["operationId"] = activeOperationId,
      ["stage"] = "capture",
      ["outcome"] = "Started",
      ["provider"] = effectiveSettings.GetConfiguredTranscriptionProviderId(),
      ["model"] = effectiveSettings.GetConfiguredTranscriptionModelId(),
    };

    if (diagnostics is IStructuredDiagnostics structuredDiagnostics)
    {
      structuredDiagnostics.Info("Recording started.", startProperties);
    }
    else
    {
      diagnostics.Info("Recording started.");
    }
  }

  private async Task StopTranscribeAndInsertIfRecordingAsync()
  {
    if (stateMachine.CurrentState != DictationSessionState.Recording)
    {
      return;
    }

    Volatile.Write(ref pipelineBusy, 1);
    Stopwatch pipelineStopwatch = Stopwatch.StartNew();
    try
    {
      if (!stateMachine.TryTransition(DictationSessionState.Transcribing))
      {
        throw new InvalidOperationException("Failed to transition to transcribing state.");
      }

      await overlayService
        .ShowStateAsync(
          DictationSessionState.Transcribing,
          display: OverlayDisplayOptions.AnchoredTranscribing,
          cancellationToken: GetRuntimeToken())
        .ConfigureAwait(false);

      AppSettings effectiveSettings = activeSessionSettings ?? settings;
      TranscriptionModelSelection transcriptionSelection = effectiveSettings.GetConfiguredTranscriptionSelection();
      StopTranscriptionOutcome stopOutcome = await StopAndTranscribeAsync(effectiveSettings, transcriptionSelection)
        .ConfigureAwait(false);
      TranscriptionResult transcription = stopOutcome.Transcription;
      diagnostics.Info(
        string.Format(
          CultureInfo.InvariantCulture,
          "Transcription completed with provider '{0}' model '{1}' in {2:F0} ms ({3} chars).",
          transcriptionSelection.ProviderId,
          transcription.ModelId,
          transcription.Duration.TotalMilliseconds,
          transcription.Text.Length));

      if (string.IsNullOrWhiteSpace(transcription.Text))
      {
        diagnostics.Warning("Dictation produced no audible speech.");
        if (!stateMachine.TryTransition(DictationSessionState.Completed))
        {
          throw new InvalidOperationException("Failed to complete a dictation with no audible speech.");
        }

        await overlayService
          .ShowStateAsync(
            DictationSessionState.Completed,
            DictationStatusMessages.NoAudibleSpeechDetected,
            display: OverlayDisplayOptions.AnchoredNotice,
            cancellationToken: GetRuntimeToken())
          .ConfigureAwait(false);

        await ResetToIdleAsync(hideOverlay: false).ConfigureAwait(false);
        return;
      }

      TextTransformationOptions transformationOptions = new(
        EnableDictationCommands: effectiveSettings.EnableDictationCommands);
      Stopwatch transformationStopwatch = Stopwatch.StartNew();
      TextTransformationResult transformation = await textTransformationService
        .TransformAsync(
          new TextTransformationRequest(transcription.Text, transformationOptions),
          GetRuntimeToken())
        .ConfigureAwait(false);
      transformationStopwatch.Stop();
      string transformedText = transformation.Text;

      diagnostics.Info(
        string.Format(
          CultureInfo.InvariantCulture,
          "Spoken-command transformation completed in {0:F0} ms ({1}->{2} chars).",
          transformationStopwatch.Elapsed.TotalMilliseconds,
          transcription.Text.Length,
          transformedText.Length));

      if (transformedText.Length == 0)
      {
        diagnostics.Warning("Dictation produced no insertable text.");
        await ResetToIdleAsync(hideOverlay: true).ConfigureAwait(false);
        return;
      }

      if (!stateMachine.TryTransition(DictationSessionState.Inserting))
      {
        throw new InvalidOperationException("Failed to transition to inserting state.");
      }

      diagnostics.Info(
        $"Preparing insertion ({transformedText.Length} chars) using preferred method {effectiveSettings.PreferredInsertionMethod}.");

      Stopwatch insertionStopwatch = Stopwatch.StartNew();
      InsertionResult insertion = await textInsertionService
        .InsertAsync(
          transformedText,
          effectiveSettings.PreferredInsertionMethod,
          effectiveSettings.RestoreClipboard,
          GetRuntimeToken())
        .ConfigureAwait(false);
      insertionStopwatch.Stop();

      if (insertion.Outcome == InsertionOutcome.Blocked && !insertion.CanRecoverTranscript)
      {
        throw new InvalidOperationException(
          insertion.ErrorMessage
          ?? $"Text insertion returned {insertion.Outcome} using {insertion.MethodUsed}.");
      }

      bool recoveryRequired = insertion.CanRecoverTranscript;

      if (!stateMachine.TryTransition(DictationSessionState.Completed))
      {
        throw new InvalidOperationException("Failed to transition to completed state.");
      }

      if (insertion.Outcome == InsertionOutcome.VerifiedInserted)
      {
        diagnostics.Info($"Insertion verified using {insertion.MethodUsed}.");
      }
      else if (recoveryRequired)
      {
        diagnostics.Warning($"Insertion was not accepted by the target. Dictation was preserved for recovery. {insertion.ErrorMessage ?? string.Empty}".TrimEnd());
      }
      else
      {
        diagnostics.Warning(
          $"Insertion dispatched using {insertion.MethodUsed}, but visible text could not be verified. {insertion.ErrorMessage ?? string.Empty}".TrimEnd());
      }

      pipelineStopwatch.Stop();
      LogPipelineTiming(
        transcriptionSelection,
        stopOutcome,
        transformationStopwatch.Elapsed,
        insertionStopwatch.Elapsed,
        pipelineStopwatch.Elapsed,
        insertion);
      bool historyRecorded = await RecordHistoryAsync(
          transcriptionSelection,
          transcription,
          transformation,
          transformationStopwatch.Elapsed,
          pipelineStopwatch.Elapsed,
          recoveryRequired ? "global-hotkey-recovery" : "global-hotkey")
        .ConfigureAwait(false);

      await overlayService
        .ShowStateAsync(
          DictationSessionState.Completed,
          recoveryRequired
            ? BuildRecoveryMessage(insertion.RecoveryCopyAvailable, historyRecorded)
            : null,
          display: recoveryRequired
            ? OverlayDisplayOptions.AnchoredStatus
            : OverlayDisplayOptions.AnchoredCompletion,
          cancellationToken: GetRuntimeToken())
        .ConfigureAwait(false);

      await ResetToIdleAsync(hideOverlay: false).ConfigureAwait(false);
    }
    finally
    {
      await DisposeChunkedTranscriptionSessionAsync().ConfigureAwait(false);
      Volatile.Write(ref pipelineBusy, 0);
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "History persistence must not make an otherwise successful dictation fail.")]
  private async Task<bool> RecordHistoryAsync(
    TranscriptionModelSelection transcriptionSelection,
    TranscriptionResult transcription,
    TextTransformationResult transformation,
    TimeSpan transformationDuration,
    TimeSpan totalPipelineDuration,
    string source)
  {
    try
    {
      await historyRecorder
        .RecordAsync(
          new DictationHistoryRecord(
            CreatedUtc: DateTimeOffset.UtcNow,
            ProfileId: DictationHistoryRecord.DefaultProfileId,
            TranscriptionProviderId: transcriptionSelection.ProviderId,
            TranscriptionModelId: transcription.ModelId,
            RawTranscript: transcription.Text,
            FinalText: transformation.Text,
            TranscriptionDuration: transcription.Duration,
            TextTransformationDuration: transformationDuration,
            TotalPipelineDuration: totalPipelineDuration,
            Source: source).Normalize(),
          GetRuntimeToken())
        .ConfigureAwait(false);
      return true;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      diagnostics.Warning($"Local history write failed: {ex.Message}");
      return false;
    }
  }

  private static string BuildRecoveryMessage(bool clipboardAvailable, bool historyAvailable)
  {
    return (clipboardAvailable, historyAvailable) switch
    {
      (true, true) => DictationStatusMessages.InsertFailedCopiedAndSaved,
      (true, false) => DictationStatusMessages.InsertFailedCopied,
      (false, true) => DictationStatusMessages.InsertFailedSaved,
      _ => DictationStatusMessages.InsertFailedNotPreserved,
    };
  }

  private async Task<StopTranscriptionOutcome> StopAndTranscribeAsync(
    AppSettings effectiveSettings,
    TranscriptionModelSelection transcriptionSelection)
  {
    string modelId = effectiveSettings.GetConfiguredTranscriptionModelId();

    if (audioCaptureService is IChunkedAudioCaptureService chunkedAudioCaptureService
        && chunkedTranscriptionSession is not null)
    {
      Stopwatch captureFinalizationStopwatch = Stopwatch.StartNew();
      AudioCaptureChunk? finalChunk = await chunkedAudioCaptureService
        .StopAndFlushChunkAsync(GetRuntimeToken())
        .ConfigureAwait(false);
      captureFinalizationStopwatch.Stop();
      chunkedTranscriptionSession.QueueChunk(finalChunk);
      Stopwatch chunkCompletionStopwatch = Stopwatch.StartNew();
      IReadOnlyList<TranscriptionChunkResult> chunks = await chunkedTranscriptionSession
        .CompleteAsync(GetRuntimeToken())
        .ConfigureAwait(false);
      TranscriptionResult combined = TranscriptChunkCombiner.Combine(chunks, modelId);
      chunkCompletionStopwatch.Stop();
      diagnostics.Info(
        string.Format(
          CultureInfo.InvariantCulture,
          "Chunked transcription completed with provider '{0}' across {1} chunk(s) ({2} chars).",
          transcriptionSelection.ProviderId,
          chunks.Count,
          combined.Text.Length));
      return new StopTranscriptionOutcome(
        combined,
        captureFinalizationStopwatch.Elapsed,
        chunkCompletionStopwatch.Elapsed,
        IsChunked: true);
    }

    Stopwatch captureStopwatch = Stopwatch.StartNew();
    AudioCaptureResult audio = await audioCaptureService.StopAsync(GetRuntimeToken()).ConfigureAwait(false);
    captureStopwatch.Stop();
    if (audio.Pcm16Mono.Length == 0)
    {
      diagnostics.Warning("No microphone input was captured; skipping transcription.");
      return new StopTranscriptionOutcome(
        new TranscriptionResult(string.Empty, modelId, TimeSpan.Zero),
        captureStopwatch.Elapsed,
        TimeSpan.Zero,
        IsChunked: false);
    }

    Stopwatch transcriptionWallStopwatch = Stopwatch.StartNew();
    TranscriptionResult transcription = await transcriptionService
      .TranscribeAsync(audio, modelId, GetRuntimeToken())
      .ConfigureAwait(false);
    transcriptionWallStopwatch.Stop();
    return new StopTranscriptionOutcome(
      transcription,
      captureStopwatch.Elapsed,
      transcriptionWallStopwatch.Elapsed,
      IsChunked: false);
  }

  private void LogPipelineTiming(
    TranscriptionModelSelection selection,
    StopTranscriptionOutcome stopOutcome,
    TimeSpan transformationDuration,
    TimeSpan insertionDuration,
    TimeSpan stopToVisibleDuration,
    InsertionResult insertion)
  {
    IReadOnlyDictionary<string, object?> properties = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
      ["operationId"] = activeOperationId ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
      ["stage"] = "dictation",
      ["outcome"] = "Completed",
      ["durationMs"] = Math.Round(stopToVisibleDuration.TotalMilliseconds, 2),
      ["providerId"] = selection.ProviderId,
      ["provider"] = selection.ProviderId,
      ["modelId"] = stopOutcome.Transcription.ModelId,
      ["model"] = stopOutcome.Transcription.ModelId,
      ["chunked"] = stopOutcome.IsChunked,
      ["captureFinalizationMs"] = Math.Round(stopOutcome.CaptureFinalizationDuration.TotalMilliseconds, 2),
      ["transcriptionWallMs"] = Math.Round(stopOutcome.TranscriptionWallDuration.TotalMilliseconds, 2),
      ["modelReportedMs"] = Math.Round(stopOutcome.Transcription.Duration.TotalMilliseconds, 2),
      ["transformationMs"] = Math.Round(transformationDuration.TotalMilliseconds, 2),
      ["insertionMs"] = Math.Round(insertionDuration.TotalMilliseconds, 2),
      ["stopToVisibleMs"] = Math.Round(stopToVisibleDuration.TotalMilliseconds, 2),
      ["insertionMethod"] = insertion.MethodUsed.ToString(),
      ["insertionOutcome"] = insertion.Outcome.ToString(),
    };

    if (diagnostics is IStructuredDiagnostics structuredDiagnostics)
    {
      structuredDiagnostics.Info("Dictation stop-to-visible timing completed.", properties);
      return;
    }

    diagnostics.Info(
      string.Format(
        CultureInfo.InvariantCulture,
        "Dictation stop-to-visible timing completed: capture={0:F0} ms, transcription={1:F0} ms, transform={2:F0} ms, insertion={3:F0} ms, total={4:F0} ms.",
        stopOutcome.CaptureFinalizationDuration.TotalMilliseconds,
        stopOutcome.TranscriptionWallDuration.TotalMilliseconds,
        transformationDuration.TotalMilliseconds,
        insertionDuration.TotalMilliseconds,
        stopToVisibleDuration.TotalMilliseconds));
  }

  private sealed record StopTranscriptionOutcome(
    TranscriptionResult Transcription,
    TimeSpan CaptureFinalizationDuration,
    TimeSpan TranscriptionWallDuration,
    bool IsChunked);

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Error recovery must swallow secondary failures while attempting safe reset.")]
  private async Task HandlePipelineErrorAsync(string message, Exception? exception)
  {
    Dictionary<string, object?> errorProps = new(StringComparer.Ordinal)
    {
      ["operationId"] = activeOperationId ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
      ["stage"] = "pipeline",
      ["outcome"] = exception is OperationCanceledException ? "Cancelled" : "Failed",
      ["provider"] = (activeSessionSettings ?? settings).GetConfiguredTranscriptionProviderId(),
      ["model"] = (activeSessionSettings ?? settings).GetConfiguredTranscriptionModelId(),
    };

    if (diagnostics is IStructuredDiagnostics structuredDiagnostics)
    {
      structuredDiagnostics.Error(message, exception, errorProps);
    }
    else
    {
      diagnostics.Error(message, exception);
    }

    DictationSessionState state = stateMachine.CurrentState;
    if (state is DictationSessionState.Recording or DictationSessionState.Transcribing or DictationSessionState.Inserting)
    {
      _ = stateMachine.TryTransition(DictationSessionState.Error);
    }

    try
    {
      await overlayService
        .ShowStateAsync(
          DictationSessionState.Error,
          message,
          display: OverlayDisplayOptions.AnchoredStatus,
          cancellationToken: CancellationToken.None)
        .ConfigureAwait(false);
    }
    catch (Exception overlayException)
    {
      diagnostics.Warning($"Failed to show error overlay: {overlayException.Message}");
    }

    if (audioCaptureService.IsCapturing)
    {
      try
      {
        if (audioCaptureService is IChunkedAudioCaptureService chunkedAudioCaptureService)
        {
          _ = await chunkedAudioCaptureService.StopAndFlushChunkAsync(CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
          _ = await audioCaptureService.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
      }
      catch (Exception stopException)
      {
        diagnostics.Warning($"Failed to stop capture during error recovery: {stopException.Message}");
      }
    }

    await DisposeChunkedTranscriptionSessionAsync().ConfigureAwait(false);
    await ResetToIdleAsync(hideOverlay: false).ConfigureAwait(false);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Reset should never throw while cleaning up overlay failures.")]
  private async Task ResetToIdleAsync(bool hideOverlay)
  {
    if (stateMachine.CurrentState is DictationSessionState.Completed or DictationSessionState.Error)
    {
      _ = stateMachine.TryTransition(DictationSessionState.Idle);
    }
    else
    {
      stateMachine.Reset();
    }

    if (hideOverlay)
    {
      try
      {
        await overlayService.HideAsync(CancellationToken.None).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        diagnostics.Warning($"Failed to hide overlay while resetting state: {ex.Message}");
      }
    }

    insertionTargetSession?.ClearCapturedTarget();
    activeSessionSettings = null;
    activeOperationId = null;
  }

  private CancellationToken GetRuntimeToken()
  {
    lock (runtimeSync)
    {
      return runtimeCts?.Token ?? CancellationToken.None;
    }
  }

  private void CancelAndDisposeRuntimeTokenSource()
  {
    CancellationTokenSource? ctsToDispose = null;
    lock (runtimeSync)
    {
      if (runtimeCts is not null)
      {
        ctsToDispose = runtimeCts;
        runtimeCts = null;
      }
    }

    if (ctsToDispose is not null)
    {
      ctsToDispose.Cancel();
      ctsToDispose.Dispose();
    }
  }

  private async Task DisposeChunkedTranscriptionSessionAsync()
  {
    ChunkedTranscriptionSession? session = chunkedTranscriptionSession;
    chunkedTranscriptionSession = null;
    if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
  }

  private enum HotkeySignal
  {
    Pressed = 0,
    Released = 1,
  }
}
