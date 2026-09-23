using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

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
    Func<IHotkeyService> hotkeyServiceFactory)
  {
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.audioCaptureServiceFactory = audioCaptureServiceFactory ?? throw new ArgumentNullException(nameof(audioCaptureServiceFactory));
    this.transcriptionServiceFactory = transcriptionServiceFactory ?? throw new ArgumentNullException(nameof(transcriptionServiceFactory));
    this.hotkeyServiceFactory = hotkeyServiceFactory ?? throw new ArgumentNullException(nameof(hotkeyServiceFactory));
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
      await audioCaptureService.StartAsync(cancellationToken).ConfigureAwait(true);
      return true;
    }
    catch
    {
      stateMachine.Reset();
      throw;
    }
  }

  public async Task<WorkbenchTranscriptionOutcome> StopAndTranscribeAsync(
    CancellationToken cancellationToken = default)
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
      AudioCaptureResult capture = await audioCaptureService.StopAsync(cancellationToken).ConfigureAwait(true);
      if (capture.Pcm16Mono.Length == 0)
      {
        return Complete(WorkbenchTranscriptionStatus.NoAudibleSpeech, string.Empty, capture.Duration);
      }

      TranscriptionResult transcription = await transcriptionService
        .TranscribeAsync(capture, settings.GetConfiguredTranscriptionModelId(), cancellationToken)
        .ConfigureAwait(true);
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
}
