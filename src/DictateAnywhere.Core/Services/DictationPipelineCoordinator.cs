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
  private RunLifetime? lifetime;
  private TaskCompletionSource? disposal;
  private Task? disposalDriver;
  private ChunkedTranscriptionSession? chunkedTranscriptionSession;

  private int pipelineBusy;
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
    this.diagnostics = DiagnosticBoundary.Wrap(diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
    this.historyRecorder = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
    insertionTargetSession = textInsertionService as ITextInsertionTargetSession;
  }

  public DictationSessionState CurrentState => stateMachine.CurrentState;

  public Task StartAsync(CancellationToken cancellationToken = default)
  {
    RunLifetime owner;
    lock (runtimeSync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      cancellationToken.ThrowIfCancellationRequested();
      if (lifetime?.Stop is not null)
      {
        if (!lifetime.Stop.Task.IsCompletedSuccessfully)
          return StartAfterStopAsync(lifetime.Stop.Task, cancellationToken);
        lifetime = null;
      }
      if (lifetime is not null) return lifetime.Start.Task.WaitAsync(cancellationToken);
      owner = new RunLifetime();
      lifetime = owner;
    }
    owner.StartDriver = StartCoreAsync(owner, cancellationToken);
    return owner.Start.Task.WaitAsync(cancellationToken);
  }

  private async Task StartAfterStopAsync(Task stop, CancellationToken cancellationToken)
  {
    await stop.WaitAsync(cancellationToken).ConfigureAwait(false);
    await StartAsync(cancellationToken).ConfigureAwait(false);
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Startup completion owns its fault; failed startup requests teardown without awaiting itself.")]
  private async Task StartCoreAsync(RunLifetime owner, CancellationToken cancellationToken)
  {
    Exception? failure = null;
    try
    {
      using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken);
      settings = await settingsStore.LoadAsync(startup.Token).ConfigureAwait(false);
      startup.Token.ThrowIfCancellationRequested();
      _ = GetRuntimeToken(owner);
      HotkeyRegistrationResult registrationResult = await hotkeyService
        .RegisterAsync(settings.Hotkey, startup.Token).ConfigureAwait(false);
      startup.Token.ThrowIfCancellationRequested();
      if (!registrationResult.Success)
      {
        string message = registrationResult.ErrorMessage ?? "Unable to register configured hotkey.";
        diagnostics.Error(message);
        await overlayService.ShowStateAsync(DictationSessionState.Error,
          DictationStatusMessages.HotkeyUnavailable, display: OverlayDisplayOptions.StatusPanelDefault,
          cancellationToken: startup.Token).ConfigureAwait(false);
        throw new InvalidOperationException(message);
      }
      lock (runtimeSync)
      {
        _ = GetRuntimeToken(owner);
        owner.Pressed = (sender, args) => OnHotkeyPressed(owner, sender, args);
        owner.Released = (sender, args) => OnHotkeyReleased(owner, sender, args);
        hotkeyService.HotkeyPressed += owner.Pressed;
        hotkeyService.HotkeyReleased += owner.Released;
        owner.Ready = true;
      }
      diagnostics.Info($"Dictation coordinator started in {settings.RecordingMode} mode.");
    }
    catch (Exception exception)
    {
      failure = exception;
      // Publish teardown while startup still owns its lease; never self-await.
      _ = RequestStop(owner);
    }
    finally { ReleaseOperation(owner); }
    if (failure is null) owner.Start.TrySetResult();
    else if (failure is OperationCanceledException) owner.Start.TrySetCanceled();
    else
    {
      owner.Start.TrySetException(failure);
      _ = owner.Start.Task.Exception; // A canceled caller wait must not leave an unobserved owned fault.
    }
  }

  public Task StopAsync(CancellationToken cancellationToken = default)
  {
    RunLifetime? owner;
    lock (runtimeSync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      owner = lifetime;
    }
    Task stop = owner is null ? Task.CompletedTask : RequestStop(owner);
    return cancellationToken.CanBeCanceled ? stop.WaitAsync(cancellationToken) : stop;
  }

  public ValueTask DisposeAsync()
  {
    TaskCompletionSource source;
    RunLifetime? owner;
    lock (runtimeSync)
    {
      if (disposal is not null) return new ValueTask(disposal.Task);
      disposed = true;
      source = disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
      owner = lifetime;
    }
    disposalDriver = DisposeCoreAsync(source, owner);
    return new ValueTask(source.Task);
  }

  private Task RequestStop(RunLifetime owner)
  {
    lock (runtimeSync)
    {
      if (owner.Stop is not null) return owner.Stop.Task;
      owner.Accepting = false;
      owner.Stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
      if (owner.Operations == 0) owner.Drained.TrySetResult();
    }
    owner.StopDriver = StopCoreAsync(owner);
    return owner.Stop.Task;
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Every independent teardown stage is attempted; the shared stop reports aggregate failure after draining.")]
  private async Task StopCoreAsync(RunLifetime owner)
  {
    List<Exception> failures = [];
    async Task Attempt(Func<Task> cleanup)
    {
      try { await cleanup().ConfigureAwait(false); }
      catch (Exception exception) { failures.Add(exception); }
    }
    // No user cancellation callback runs under runtimeSync. Caller wait tokens
    // never cancel this owned teardown operation.
    await Attempt(() =>
    {
      hotkeyService.HotkeyPressed -= owner.Pressed;
      hotkeyService.HotkeyReleased -= owner.Released;
      return Task.CompletedTask;
    }).ConfigureAwait(false);
    await Attempt(() =>
    {
      owner.Cancellation.Cancel();
      return Task.CompletedTask;
    }).ConfigureAwait(false);
    await owner.Drained.Task.ConfigureAwait(false);
    await Attempt(async () =>
    {
      if (!audioCaptureService.IsCapturing) return;
      if (audioCaptureService is IChunkedAudioCaptureService chunked)
        _ = await chunked.StopAndFlushChunkAsync(CancellationToken.None).ConfigureAwait(false);
      else _ = await audioCaptureService.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }).ConfigureAwait(false);
    await Attempt(() => hotkeyService.UnregisterAsync(CancellationToken.None)).ConfigureAwait(false);
    await Attempt(() => overlayService.HideAsync(CancellationToken.None)).ConfigureAwait(false);
    await Attempt(DisposeChunkedTranscriptionSessionAsync).ConfigureAwait(false);
    await Attempt(() =>
    {
      stateMachine.Reset();
      insertionTargetSession?.ClearCapturedTarget();
      activeSessionSettings = null;
      activeOperationId = null;
      Volatile.Write(ref pipelineBusy, 0);
      diagnostics.Info("Dictation coordinator stopped.");
      return Task.CompletedTask;
    }).ConfigureAwait(false);
    owner.Cancellation.Dispose();
    if (failures.Count == 0) owner.Stop!.TrySetResult();
    else
    {
      owner.Stop!.TrySetException(new AggregateException(failures));
      _ = owner.Stop.Task.Exception;
    }
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Dispose retains the shared stop failure while releasing the semaphore only after its users drain.")]
  private async Task DisposeCoreAsync(TaskCompletionSource source, RunLifetime? owner)
  {
    Exception? failure = null;
    try
    {
      if (owner is not null) await RequestStop(owner).ConfigureAwait(false);
    }
    catch (Exception exception) { failure = exception; }
    signalLock.Dispose();
    if (failure is null) source.TrySetResult();
    else { source.TrySetException(failure); _ = source.Task.Exception; }
  }

  private bool AcquireOperation(RunLifetime owner)
  {
    lock (runtimeSync)
    {
      if (disposed || !ReferenceEquals(lifetime, owner) || !owner.Accepting || !owner.Ready) return false;
      owner.Operations++;
      return true;
    }
  }

  private void ReleaseOperation(RunLifetime owner)
  {
    lock (runtimeSync)
    {
      owner.Operations--;
      if (!owner.Accepting && owner.Operations == 0) owner.Drained.TrySetResult();
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Hotkey callbacks are genuine event boundaries; all accepted pipeline work is awaited and final failures are reported.")]
  private async void OnHotkeyPressed(RunLifetime owner, object? sender, HotkeyEventArgs e)
  {
    if (!AcquireOperation(owner)) return;
    try
    {
      await HandleHotkeySignalAsync(owner, HotkeySignal.Pressed, e.ObservedAtUtc).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      ReportCallbackFailure("Unexpected failure while handling the dictation hotkey press.", ex);
    }
    finally { ReleaseOperation(owner); }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Hotkey callbacks are genuine event boundaries; all accepted pipeline work is awaited and final failures are reported.")]
  private async void OnHotkeyReleased(RunLifetime owner, object? sender, HotkeyEventArgs e)
  {
    if (!AcquireOperation(owner)) return;
    try
    {
      await HandleHotkeySignalAsync(owner, HotkeySignal.Released, e.ObservedAtUtc).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      ReportCallbackFailure("Unexpected failure while handling the dictation hotkey release.", ex);
    }
    finally { ReleaseOperation(owner); }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Coordinator boundary catches unexpected pipeline exceptions and transitions to a safe state.")]
  private async Task HandleHotkeySignalAsync(RunLifetime owner, HotkeySignal signal, DateTimeOffset observedAtUtc)
  {
    if (Volatile.Read(ref pipelineBusy) == 1)
    {
      diagnostics.Info($"Dropped reentrant {signal} signal at {observedAtUtc:O} while pipeline is busy.");
      return;
    }

    try
    {
      await signalLock.WaitAsync(GetRuntimeToken(owner)).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return;
    }

    try
    {
      _ = GetRuntimeToken(owner);
      await ProcessHotkeySignalAsync(owner, signal).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!owner.Accepting || owner.Token.IsCancellationRequested) { }
    catch (Exception ex) when (!owner.Accepting || owner.Token.IsCancellationRequested)
    {
      ReportCallbackFailure("Stopped dictation work failed while draining.", ex);
    }
    catch (OperationCanceledException)
    {
      await HandlePipelineErrorAsync(owner, "Dictation operation was canceled.", null).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      await HandlePipelineErrorAsync(owner, "Dictation failed. Check logs for details.", ex).ConfigureAwait(false);
    }
    finally
    {
      signalLock.Release();
    }
  }

  private void ReportCallbackFailure(string message, Exception failure)
  {
    diagnostics.Error(message, failure);
  }

  private Task ProcessHotkeySignalAsync(RunLifetime owner, HotkeySignal signal)
  {
    return settings.RecordingMode switch
    {
      RecordingMode.HoldToTalk => ProcessHoldToTalkSignalAsync(owner, signal),
      RecordingMode.ToggleToTalk => ProcessToggleToTalkSignalAsync(owner, signal),
      _ => Task.CompletedTask,
    };
  }

  private Task ProcessHoldToTalkSignalAsync(RunLifetime owner, HotkeySignal signal)
  {
    return signal switch
    {
      HotkeySignal.Pressed => StartRecordingIfIdleAsync(owner),
      HotkeySignal.Released => StopTranscribeAndInsertIfRecordingAsync(owner),
      _ => Task.CompletedTask,
    };
  }

  private Task ProcessToggleToTalkSignalAsync(RunLifetime owner, HotkeySignal signal)
  {
    if (signal != HotkeySignal.Pressed)
    {
      return Task.CompletedTask;
    }

    return stateMachine.CurrentState switch
    {
      DictationSessionState.Idle => StartRecordingIfIdleAsync(owner),
      DictationSessionState.Recording => StopTranscribeAndInsertIfRecordingAsync(owner),
      _ => Task.CompletedTask,
    };
  }

  private async Task StartRecordingIfIdleAsync(RunLifetime owner)
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
        GetRuntimeToken(owner));
      chunkedTranscriptionSession.Start();
      diagnostics.Info("Chunked transcription session started.");
    }

    Stopwatch recordingOverlayStopwatch = Stopwatch.StartNew();
    await overlayService
      .ShowStateAsync(
        DictationSessionState.Recording,
        elapsed: TimeSpan.Zero,
        display: OverlayDisplayOptions.AnchoredRecording,
        cancellationToken: GetRuntimeToken(owner))
      .ConfigureAwait(false);
    recordingOverlayStopwatch.Stop();

    Stopwatch captureStartStopwatch = Stopwatch.StartNew();
    await (audioCaptureService is IChunkedAudioCaptureService chunkedCapture
      ? chunkedCapture.StartChunkedAsync(GetRuntimeToken(owner))
      : audioCaptureService.StartAsync(GetRuntimeToken(owner))).ConfigureAwait(false);
    _ = GetRuntimeToken(owner);
    captureStartStopwatch.Stop();
    activeOperationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    Dictionary<string, object?> startProperties = new(StringComparer.Ordinal)
    {
      ["operationId"] = activeOperationId,
      ["stage"] = "capture",
      ["outcome"] = "Started",
      ["provider"] = effectiveSettings.GetConfiguredTranscriptionProviderId(),
      ["model"] = effectiveSettings.GetConfiguredTranscriptionModelId(),
      ["recordingOverlayMs"] = Math.Round(recordingOverlayStopwatch.Elapsed.TotalMilliseconds, 2),
      ["captureStartMs"] = Math.Round(captureStartStopwatch.Elapsed.TotalMilliseconds, 2),
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

  private async Task StopTranscribeAndInsertIfRecordingAsync(RunLifetime owner)
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

      Stopwatch transcribingOverlayStopwatch = Stopwatch.StartNew();
      await overlayService
        .ShowStateAsync(
          DictationSessionState.Transcribing,
          display: OverlayDisplayOptions.AnchoredTranscribing,
          cancellationToken: GetRuntimeToken(owner))
        .ConfigureAwait(false);
      transcribingOverlayStopwatch.Stop();

      AppSettings effectiveSettings = activeSessionSettings ?? settings;
      TranscriptionModelSelection transcriptionSelection = effectiveSettings.GetConfiguredTranscriptionSelection();
      StopTranscriptionOutcome stopOutcome = await StopAndTranscribeAsync(owner, effectiveSettings, transcriptionSelection)
        .ConfigureAwait(false);
      _ = GetRuntimeToken(owner);
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
            cancellationToken: GetRuntimeToken(owner))
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
          GetRuntimeToken(owner))
        .ConfigureAwait(false);
      _ = GetRuntimeToken(owner);
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
          GetRuntimeToken(owner))
        .ConfigureAwait(false);
      _ = GetRuntimeToken(owner);
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
        transcribingOverlayStopwatch.Elapsed,
        transformationStopwatch.Elapsed,
        insertionStopwatch.Elapsed,
        pipelineStopwatch.Elapsed,
        insertion);
      bool historyRecorded = await RecordHistoryAsync(
          owner, transcriptionSelection,
          transcription,
          transformation,
          transformationStopwatch.Elapsed,
          pipelineStopwatch.Elapsed,
          recoveryRequired ? "global-hotkey-recovery" : "global-hotkey")
        .ConfigureAwait(false);
      _ = GetRuntimeToken(owner);

      await overlayService
        .ShowStateAsync(
          DictationSessionState.Completed,
          recoveryRequired
            ? BuildRecoveryMessage(insertion.RecoveryCopyAvailable, historyRecorded)
            : null,
          display: recoveryRequired
            ? OverlayDisplayOptions.AnchoredStatus
            : OverlayDisplayOptions.AnchoredCompletion,
          cancellationToken: GetRuntimeToken(owner))
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
    RunLifetime owner,
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
          GetRuntimeToken(owner))
        .ConfigureAwait(false);
      return true;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      DiagnosticBoundary.Report(() => diagnostics.Warning($"Local history write failed: {ex.Message}"));
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
    RunLifetime owner,
    AppSettings effectiveSettings,
    TranscriptionModelSelection transcriptionSelection)
  {
    string modelId = effectiveSettings.GetConfiguredTranscriptionModelId();

    if (audioCaptureService is IChunkedAudioCaptureService chunkedAudioCaptureService
        && chunkedTranscriptionSession is not null)
    {
      Stopwatch captureFinalizationStopwatch = Stopwatch.StartNew();
      AudioCaptureChunk? finalChunk = await chunkedAudioCaptureService
        .StopAndFlushChunkAsync(GetRuntimeToken(owner))
        .ConfigureAwait(false);
      captureFinalizationStopwatch.Stop();
      chunkedTranscriptionSession.QueueChunk(finalChunk);
      Stopwatch chunkCompletionStopwatch = Stopwatch.StartNew();
      IReadOnlyList<TranscriptionChunkResult> chunks = await chunkedTranscriptionSession
        .CompleteAsync(GetRuntimeToken(owner))
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
        IsChunked: true,
        CaptureDuration: chunkedTranscriptionSession.TotalDuration,
        PeakBacklogDuration: chunkedTranscriptionSession.PeakPendingDuration);
    }

    Stopwatch captureStopwatch = Stopwatch.StartNew();
    AudioCaptureResult audio = await audioCaptureService.StopAsync(GetRuntimeToken(owner)).ConfigureAwait(false);
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
      .TranscribeAsync(audio, modelId, GetRuntimeToken(owner))
      .ConfigureAwait(false);
    transcriptionWallStopwatch.Stop();
    return new StopTranscriptionOutcome(
      transcription,
      captureStopwatch.Elapsed,
      transcriptionWallStopwatch.Elapsed,
      IsChunked: false,
      CaptureDuration: audio.Duration,
      PeakBacklogDuration: audio.Duration);
  }

  private void LogPipelineTiming(
    TranscriptionModelSelection selection,
    StopTranscriptionOutcome stopOutcome,
    TimeSpan transcribingOverlayDuration,
    TimeSpan transformationDuration,
    TimeSpan insertionDuration,
    TimeSpan stopToVisibleDuration,
    InsertionResult insertion)
  {
    Dictionary<string, object?> properties = new(StringComparer.Ordinal)
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
      ["transcribingOverlayMs"] = Math.Round(transcribingOverlayDuration.TotalMilliseconds, 2),
      ["captureFinalizationMs"] = Math.Round(stopOutcome.CaptureFinalizationDuration.TotalMilliseconds, 2),
      ["transcriptionWallMs"] = Math.Round(stopOutcome.TranscriptionWallDuration.TotalMilliseconds, 2),
      ["modelReportedMs"] = Math.Round(stopOutcome.Transcription.Duration.TotalMilliseconds, 2),
      ["transformationMs"] = Math.Round(transformationDuration.TotalMilliseconds, 2),
      ["insertionMs"] = Math.Round(insertionDuration.TotalMilliseconds, 2),
      ["stopToVisibleMs"] = Math.Round(stopToVisibleDuration.TotalMilliseconds, 2),
      ["insertionMethod"] = insertion.MethodUsed.ToString(),
      ["insertionOutcome"] = insertion.Outcome.ToString(),
    };
    if (string.Equals(Environment.GetEnvironmentVariable("DICTATEANYWHERE_BENCHMARK_EVIDENCE"), "1", StringComparison.Ordinal))
    {
      // Explicit benchmark telemetry contains numeric timing only. Keep normal
      // redaction of speech, audio, targets and correlation IDs intact.
      properties["captureDurationMs"] = Math.Round(stopOutcome.CaptureDuration.TotalMilliseconds, 2);
      properties["peakBacklogMs"] = Math.Round(stopOutcome.PeakBacklogDuration.TotalMilliseconds, 2);
    }

    if (diagnostics is IStructuredDiagnostics structuredDiagnostics)
    {
      structuredDiagnostics.Info("Dictation stop-to-visible timing completed.", properties);
      return;
    }

    diagnostics.Info(
      string.Format(
        CultureInfo.InvariantCulture,
        "Dictation stop-to-visible timing completed: overlay={0:F0} ms, capture={1:F0} ms, transcription={2:F0} ms, transform={3:F0} ms, insertion={4:F0} ms, total={5:F0} ms.",
        transcribingOverlayDuration.TotalMilliseconds,
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
    bool IsChunked,
    TimeSpan CaptureDuration = default,
    TimeSpan PeakBacklogDuration = default);

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Error recovery must swallow secondary failures while attempting safe reset.")]
  private async Task HandlePipelineErrorAsync(RunLifetime owner, string message, Exception? exception)
  {
    DiagnosticBoundary.Report(() =>
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

    });
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
          cancellationToken: GetRuntimeToken(owner))
        .ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!owner.Accepting || owner.Token.IsCancellationRequested) { }
    catch (Exception overlayException)
    {
      DiagnosticBoundary.Report(() => diagnostics.Warning($"Failed to show error overlay: {overlayException.Message}"));
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
        DiagnosticBoundary.Report(() => diagnostics.Warning($"Failed to stop capture during error recovery: {stopException.Message}"));
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
        DiagnosticBoundary.Report(() => diagnostics.Warning($"Failed to hide overlay while resetting state: {ex.Message}"));
      }
    }

    insertionTargetSession?.ClearCapturedTarget();
    activeSessionSettings = null;
    activeOperationId = null;
  }

  private CancellationToken GetRuntimeToken(RunLifetime owner)
  {
    lock (runtimeSync)
    {
      // This check is a stage-admission point. A stage admitted before stop may
      // finish in flight; subsequent stages must not revive the canceled run.
      if (!owner.Accepting || !ReferenceEquals(lifetime, owner)) throw new OperationCanceledException(owner.Token);
      owner.Token.ThrowIfCancellationRequested();
      return owner.Token;
    }
  }

  private sealed class RunLifetime
  {
    internal CancellationTokenSource Cancellation { get; } = new();
    internal CancellationToken Token { get; }
    internal TaskCompletionSource Start { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource? Stop { get; set; }
    internal Task? StartDriver { get; set; }
    internal Task? StopDriver { get; set; }
    internal EventHandler<HotkeyEventArgs>? Pressed { get; set; }
    internal EventHandler<HotkeyEventArgs>? Released { get; set; }
    internal bool Accepting { get; set; } = true;
    internal bool Ready { get; set; }
    internal int Operations { get; set; } = 1; // Startup is an accepted resource user.
    internal RunLifetime() => Token = Cancellation.Token;
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
