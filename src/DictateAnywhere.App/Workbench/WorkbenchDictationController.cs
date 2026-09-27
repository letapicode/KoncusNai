using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchTranscriptionStatus
{
  Completed,
  NoAudibleSpeech,
  Ignored,
}

internal sealed record WorkbenchTranscriptionOutcome(
  WorkbenchTranscriptionStatus Status,
  string Text,
  TimeSpan AudioDuration);

/// <summary>Owns Workbench microphone, transcription, hotkey, and dictation state lifetimes.</summary>
internal sealed class WorkbenchDictationController : IAsyncDisposable
{
  private readonly IDiagnostics diagnostics;
  private readonly Func<AppSettings, IAudioCaptureService> audioCaptureServiceFactory;
  private readonly Func<AppSettings, IDiagnostics, ITranscriptionService> transcriptionServiceFactory;
  private readonly Func<IHotkeyService> hotkeyServiceFactory;
  private readonly Func<AppSettings, IOverlayService>? overlayFactory;
  private IOverlayService? overlay;
  private readonly WorkbenchSessionStateMachine stateMachine = new();

  private IAudioCaptureService? audioCaptureService;
  private ITranscriptionService? transcriptionService;
  private IHotkeyService? hotkeyService;
  private AppSettings settings = AppSettings.Default;
  private bool disposed;

  public WorkbenchDictationController(
    IDiagnostics diagnostics,
    Func<AppSettings, IAudioCaptureService> audioCaptureServiceFactory,
    Func<AppSettings, IDiagnostics, ITranscriptionService> transcriptionServiceFactory,
    Func<IHotkeyService> hotkeyServiceFactory,
    Func<AppSettings, IOverlayService>? overlayFactory = null)
  {
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.audioCaptureServiceFactory = audioCaptureServiceFactory ?? throw new ArgumentNullException(nameof(audioCaptureServiceFactory));
    this.transcriptionServiceFactory = transcriptionServiceFactory ?? throw new ArgumentNullException(nameof(transcriptionServiceFactory));
    this.hotkeyServiceFactory = hotkeyServiceFactory ?? throw new ArgumentNullException(nameof(hotkeyServiceFactory));
    this.overlayFactory = overlayFactory;
  }

  public event EventHandler? ToggleRequested;

  public WorkbenchSessionState State => stateMachine.State;
  public bool IsConfigured => audioCaptureService is not null && transcriptionService is not null;

  public async Task<WorkbenchHotkeyRegistrationOutcome?> ConfigureAsync(
    AppSettings newSettings,
    bool registerHotkey,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(newSettings);
    await ResetAsync().ConfigureAwait(true);

    settings = newSettings;
    try
    {
      audioCaptureService = new WorkbenchAudioCaptureServiceAdapter(audioCaptureServiceFactory(newSettings));
      transcriptionService = new WorkbenchTranscriptionServiceAdapter(
        transcriptionServiceFactory(newSettings, diagnostics));
      overlay = overlayFactory?.Invoke(newSettings);
      if (!registerHotkey)
      {
        return null;
      }

      hotkeyService = hotkeyServiceFactory();
      hotkeyService.HotkeyPressed += OnHotkeyPressed;
      WorkbenchHotkeyRegistrationCoordinator registration = new(
        (binding, token) => hotkeyService.RegisterAsync(binding, token));
      return await registration
        .RegisterWithFallbackAsync(newSettings.Hotkey, cancellationToken)
        .ConfigureAwait(true);
    }
    catch
    {
      await ResetAsync().ConfigureAwait(true);
      throw;
    }
  }

  [SuppressMessage("Design", "CA1031", Justification = "Startup cleanup must preserve the original failure while observing cleanup failures.")]
  public async Task<bool> StartRecordingAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (audioCaptureService is null)
    {
      throw new InvalidOperationException("Workbench audio service is not configured.");
    }

    if (!stateMachine.TryBeginRecording())
    {
      return false;
    }

    try
    {
      Stopwatch startup = Stopwatch.StartNew();
      await audioCaptureService.StartAsync(cancellationToken).ConfigureAwait(true);
      diagnostics.Info($"Workbench recording startupMs={startup.Elapsed.TotalMilliseconds:F2}.");
      if (overlay is not null)
        await overlay.ShowStateAsync(DictationSessionState.Recording,
          display: OverlayDisplayOptions.AnchoredRecording, cancellationToken: cancellationToken).ConfigureAwait(true);
      return true;
    }
    catch
    {
      // Startup feedback can fail after the device has already been acquired.
      // Release the device without allowing cancellation to skip cleanup.
      if (audioCaptureService.IsCapturing)
      {
        try { await audioCaptureService.StopAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (Exception cleanupFailure) { diagnostics.Error("Recording startup cleanup failed.", cleanupFailure); }
      }
      if (overlay is not null)
      {
        try { await overlay.HideAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (Exception cleanupFailure) { diagnostics.Error("Recording startup overlay cleanup failed.", cleanupFailure); }
      }
      stateMachine.Reset();
      throw;
    }
  }

  public async Task<WorkbenchTranscriptionOutcome> StopAndTranscribeAsync(
    CancellationToken cancellationToken = default, Action? onTranscribing = null)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (audioCaptureService is null || transcriptionService is null)
    {
      throw new InvalidOperationException("Workbench dictation services are not configured.");
    }

    if (!stateMachine.TryBeginTranscribing())
    {
      return new WorkbenchTranscriptionOutcome(
        WorkbenchTranscriptionStatus.Ignored,
        string.Empty,
        TimeSpan.Zero);
    }

    try
    {
      onTranscribing?.Invoke();
      Stopwatch finalize = Stopwatch.StartNew();
      AudioCaptureResult capture = await audioCaptureService.StopAsync(cancellationToken).ConfigureAwait(true);
      diagnostics.Info($"Workbench capture finalizationMs={finalize.Elapsed.TotalMilliseconds:F2}.");
      if (overlay is not null)
        await overlay.ShowStateAsync(DictationSessionState.Transcribing,
          display: OverlayDisplayOptions.AnchoredTranscribing, cancellationToken: cancellationToken).ConfigureAwait(true);
      if (capture.Pcm16Mono.Length == 0)
      {
        return Complete(WorkbenchTranscriptionStatus.NoAudibleSpeech, string.Empty, capture.Duration);
      }

      Stopwatch transcriptionTimer = Stopwatch.StartNew();
      TranscriptionResult transcription = await transcriptionService
        .TranscribeAsync(capture, settings.GetConfiguredTranscriptionModelId(), cancellationToken)
        .ConfigureAwait(true);
      diagnostics.Info($"Workbench transcriptionMs={transcriptionTimer.Elapsed.TotalMilliseconds:F2}.");
      string text = (transcription.Text ?? string.Empty).Trim();
      return string.IsNullOrWhiteSpace(text)
        ? Complete(WorkbenchTranscriptionStatus.NoAudibleSpeech, string.Empty, capture.Duration)
        : Complete(WorkbenchTranscriptionStatus.Completed, text, capture.Duration);
    }
    catch (OperationCanceledException)
    {
      stateMachine.Reset();
      throw;
    }
    catch
    {
      stateMachine.CompleteTranscription();
      throw;
    }
  }

  public Task<TranscriptionResult> TranscribeImportedAudioAsync(
    AudioCaptureResult audio,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    return transcriptionService is null
      ? throw new InvalidOperationException("Workbench transcription service is not configured.")
      : transcriptionService.TranscribeAsync(
        audio,
        settings.GetConfiguredTranscriptionModelId(),
        cancellationToken);
  }

  public async Task ResetAsync()
  {
    if (overlay is IAsyncDisposable overlayDisposable)
      await overlayDisposable.DisposeAsync().ConfigureAwait(true);
    overlay = null;
    if (hotkeyService is not null)
    {
      hotkeyService.HotkeyPressed -= OnHotkeyPressed;
      await hotkeyService.DisposeAsync().ConfigureAwait(true);
      hotkeyService = null;
    }

    if (audioCaptureService is IAsyncDisposable captureDisposable)
    {
      await captureDisposable.DisposeAsync().ConfigureAwait(true);
    }

    if (transcriptionService is IAsyncDisposable transcriptionDisposable)
    {
      await transcriptionDisposable.DisposeAsync().ConfigureAwait(true);
    }

    audioCaptureService = null;
    transcriptionService = null;
    stateMachine.Reset();
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    await ResetAsync().ConfigureAwait(true);
  }

  private WorkbenchTranscriptionOutcome Complete(
    WorkbenchTranscriptionStatus status,
    string text,
    TimeSpan duration)
  {
    stateMachine.CompleteTranscription();
    return new WorkbenchTranscriptionOutcome(status, text, duration);
  }

  private void OnHotkeyPressed(object? sender, HotkeyEventArgs e) => ToggleRequested?.Invoke(this, EventArgs.Empty);

  internal Task ShowOutcomeAsync(bool inserted, string message) => overlay is null ? Task.CompletedTask
    : overlay.ShowStateAsync(inserted ? DictationSessionState.Completed : DictationSessionState.Error,
      message, display: inserted ? OverlayDisplayOptions.AnchoredCompletion : OverlayDisplayOptions.AnchoredNotice);
}
