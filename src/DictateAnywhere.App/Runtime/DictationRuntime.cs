using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Audio;
using DictateAnywhere.Audio.WASAPI;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Core.Services;
using DictateAnywhere.Hotkeys;
using DictateAnywhere.Inference;
using DictateAnywhere.Insertion;
using DictateAnywhere.Overlay;

namespace DictateAnywhere.App.Runtime;

public sealed class DictationRuntime : IApplicationRuntimeSession
{
  private readonly ISettingsStore settingsStore;
  private readonly IDiagnostics diagnostics;
  private readonly ModelReadinessCoordinator? modelReadinessCoordinator;
  private readonly DictationHistoryChangeNotifier historyChangeNotifier;
  private readonly Func<
    AppSettings,
    IDiagnostics,
    Func<AppSettings, IDiagnostics, ITranscriptionService>?,
    DictationHistoryChangeNotifier?,
    RuntimeServices> runtimeServicesFactory;
  private readonly SemaphoreSlim lifecycleLock = new(1, 1);

  private RuntimeServices? services;
  private DictationPipelineCoordinator? coordinator;
  private UndoHotkeyCoordinator? undoHotkeyCoordinator;
  private RuntimeStartupNotice? startupNotice;
  private bool isRunning;
  private bool disposed;

  internal DictationRuntime(
    ISettingsStore settingsStore,
    IDiagnostics diagnostics,
    ModelReadinessCoordinator? modelReadinessCoordinator,
    DictationHistoryChangeNotifier historyChangeNotifier,
    Func<
      AppSettings,
      IDiagnostics,
      Func<AppSettings, IDiagnostics, ITranscriptionService>?,
      DictationHistoryChangeNotifier?,
      RuntimeServices> runtimeServicesFactory)
  {
    this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.modelReadinessCoordinator = modelReadinessCoordinator;
    this.historyChangeNotifier = historyChangeNotifier ?? throw new ArgumentNullException(nameof(historyChangeNotifier));
    this.runtimeServicesFactory = runtimeServicesFactory ?? throw new ArgumentNullException(nameof(runtimeServicesFactory));
  }

  public DictationSessionState CurrentState => coordinator?.CurrentState ?? DictationSessionState.Idle;

  public bool IsRunning => isRunning;

  public RuntimeStartupNotice? ConsumeStartupNotice()
  {
    RuntimeStartupNotice? notice = startupNotice;
    startupNotice = null;
    return notice;
  }

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);

    await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);

      if (coordinator is not null)
      {
        isRunning = true;
        return;
      }

      await CreateAndStartCoordinatorAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      lifecycleLock.Release();
    }
  }

  public async Task RestartAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);

    await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);

      await StopAndDisposeCoordinatorAsync(cancellationToken).ConfigureAwait(false);
      await CreateAndStartCoordinatorAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      lifecycleLock.Release();
    }
  }

  public async Task<bool> TryRestartWhenIdleAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);

    await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);

      if (coordinator?.CurrentState != DictationSessionState.Idle)
      {
        return false;
      }

      await StopAndDisposeCoordinatorAsync(cancellationToken).ConfigureAwait(false);
      await CreateAndStartCoordinatorAsync(cancellationToken).ConfigureAwait(false);
      return true;
    }
    finally
    {
      lifecycleLock.Release();
    }
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);

    await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);

      await StopAndDisposeCoordinatorAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      lifecycleLock.Release();
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    await lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
    try
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      await StopAndDisposeCoordinatorAsync(CancellationToken.None).ConfigureAwait(false);
    }
    finally
    {
      lifecycleLock.Release();
      lifecycleLock.Dispose();
    }
  }

  private async Task CreateAndStartCoordinatorAsync(CancellationToken cancellationToken)
  {
    AppSettings settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
    diagnostics.Info(RuntimeStartupAdvisory.DescribeSettings(settings));
    Func<AppSettings, IDiagnostics, ITranscriptionService>? transcriptionServiceFactory =
      modelReadinessCoordinator is null
        ? null
        : modelReadinessCoordinator.CreateTranscriptionService;
    RuntimeServices runtimeServices = runtimeServicesFactory(
      settings,
      diagnostics,
      transcriptionServiceFactory,
      historyChangeNotifier);
    DictationPipelineCoordinator runtimeCoordinator = new(
      runtimeServices.HotkeyService,
      runtimeServices.AudioCaptureService,
      runtimeServices.TranscriptionService,
      runtimeServices.TextInsertionService,
      runtimeServices.TextTransformationService,
      runtimeServices.OverlayService,
      settingsStore,
      diagnostics,
      runtimeServices.HistoryRecorder);

    try
    {
      await runtimeCoordinator.StartAsync(cancellationToken).ConfigureAwait(false);

      UndoHotkeyCoordinator? runtimeUndoCoordinator = null;
      if (runtimeServices.UndoInsertionService is not null)
      {
        if (settings.UndoHotkey == settings.Hotkey)
        {
          diagnostics.Warning("Undo hotkey matches dictation hotkey. Undo hotkey registration skipped.");
        }
        else
        {
          runtimeUndoCoordinator = new UndoHotkeyCoordinator(
            runtimeServices.UndoHotkeyService,
            runtimeServices.UndoInsertionService,
            diagnostics,
            settings.UndoHotkey);

          try
          {
            await runtimeUndoCoordinator.StartAsync(cancellationToken).ConfigureAwait(false);
          }
          catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
          {
            diagnostics.Warning($"Undo hotkey unavailable: {ex.Message}");
            await runtimeUndoCoordinator.DisposeAsync().ConfigureAwait(false);
            runtimeUndoCoordinator = null;
          }
        }
      }

      undoHotkeyCoordinator = runtimeUndoCoordinator;
    }
    catch
    {
      undoHotkeyCoordinator = null;
      await runtimeCoordinator.DisposeAsync().ConfigureAwait(false);
      await runtimeServices.DisposeAsync().ConfigureAwait(false);
      isRunning = false;
      throw;
    }

    services = runtimeServices;
    coordinator = runtimeCoordinator;
    startupNotice = BuildStartupNotice(settings, runtimeServices);
    isRunning = true;
  }

  private async Task StopAndDisposeCoordinatorAsync(CancellationToken cancellationToken)
  {
    DictationPipelineCoordinator? coordinatorToStop = coordinator;
    RuntimeServices? servicesToDispose = services;
    UndoHotkeyCoordinator? undoCoordinatorToStop = undoHotkeyCoordinator;

    coordinator = null;
    services = null;
    undoHotkeyCoordinator = null;
    startupNotice = null;
    isRunning = false;

    if (undoCoordinatorToStop is not null)
    {
      await undoCoordinatorToStop.StopAsync(cancellationToken).ConfigureAwait(false);
      await undoCoordinatorToStop.DisposeAsync().ConfigureAwait(false);
    }

    if (coordinatorToStop is not null)
    {
      await coordinatorToStop.StopAsync(cancellationToken).ConfigureAwait(false);
      await coordinatorToStop.DisposeAsync().ConfigureAwait(false);
    }

    if (servicesToDispose is not null)
    {
      await servicesToDispose.DisposeAsync().ConfigureAwait(false);
    }
  }

  internal sealed class RuntimeServices : IAsyncDisposable
  {
    public RuntimeServices(
      IHotkeyService hotkeyService,
      IHotkeyService undoHotkeyService,
      IAudioCaptureService audioCaptureService,
      ITranscriptionService transcriptionService,
      ITextInsertionService textInsertionService,
      ITextTransformationService textTransformationService,
      IOverlayService overlayService,
      IUndoInsertionService? undoInsertionService,
      IDictationHistoryRecorder historyRecorder)
    {
      HotkeyService = hotkeyService;
      UndoHotkeyService = undoHotkeyService;
      AudioCaptureService = audioCaptureService;
      TranscriptionService = transcriptionService;
      TextInsertionService = textInsertionService;
      TextTransformationService = textTransformationService;
      OverlayService = overlayService;
      UndoInsertionService = undoInsertionService;
      HistoryRecorder = historyRecorder;
    }

    public IHotkeyService HotkeyService { get; }

    public IHotkeyService UndoHotkeyService { get; }

    public IAudioCaptureService AudioCaptureService { get; }

    public ITranscriptionService TranscriptionService { get; }

    public ITextInsertionService TextInsertionService { get; }

    public ITextTransformationService TextTransformationService { get; }

    public IOverlayService OverlayService { get; }

    public IUndoInsertionService? UndoInsertionService { get; }

    public IDictationHistoryRecorder HistoryRecorder { get; }

    [SuppressMessage(
      "Reliability",
      "CA2000:Dispose objects before losing scope",
      Justification = "RuntimeServices owns these disposables for runtime lifetime and releases them in DisposeAsync.")]
    public static RuntimeServices Create(
      AppSettings settings,
      IDiagnostics diagnostics,
      Func<AppSettings, IDiagnostics, ITranscriptionService>? transcriptionServiceFactory = null,
      DictationHistoryChangeNotifier? historyChangeNotifier = null)
    {
      ArgumentNullException.ThrowIfNull(diagnostics);

      OverlayServiceOptions overlayOptions = OverlayServiceOptions.Default with
      {
        Enabled = settings.OverlayEnabled,
        CaretIndicatorEnabled = settings.CaretIndicatorEnabled,
        FallbackToCornerOverlay = settings.FallbackToCornerOverlay,
      };

      IWindowFocusProvider windowFocusProvider = new WindowsWindowFocusProvider();
      TextInsertionOptions insertionOptions = new(
        EnableSecureFieldDetection: settings.EnableSecureFieldDetection,
        BlockedProcessNames: settings.InsertionBlockedProcessNames ?? Array.Empty<string>(),
        EnableElevatedInsertion: settings.EnableElevatedInsertion);
      WindowsTextInsertionService textInsertionService = new(
        new WindowsClipboardController(),
        new WindowsInputDispatcher(),
        windowFocusProvider,
        new WindowsPrivilegeBoundaryDetector(),
        insertionOptions,
        new UiAccessHelperProcessBridge(UiAccessHelperProcessBridgeOptions.Default, diagnostics),
        diagnostics);
      IOverlayAnchorProvider overlayAnchorProvider = new WindowsOverlayAnchorProvider(windowFocusProvider);
      IHotkeyService hotkeyService = new GlobalToggleHotkeyService(
        new WindowsHotkeyService(),
        windowFocusProvider,
        diagnostics);
      IDictationHistoryRecorder persistentRecorder = new LocalDictationHistoryStore(
        LocalDictationHistoryStore.DefaultHistoryFilePath);
      IDictationHistoryRecorder historyRecorder = new CachingDictationHistoryRecorder(
        persistentRecorder,
        historyChangeNotifier);

      return new RuntimeServices(
        hotkeyService: hotkeyService,
        undoHotkeyService: new WindowsHotkeyService(WindowsHotkeyService.DefaultHotkeyId + 1),
        audioCaptureService: RuntimeServiceFactory.CreateAudioCaptureService(settings),
        transcriptionService: transcriptionServiceFactory?.Invoke(settings, diagnostics)
                              ?? RuntimeServiceFactory.CreateTranscriptionService(settings, registry: null, diagnostics: diagnostics),
        textInsertionService: textInsertionService,
        textTransformationService: RuntimeServiceFactory.CreateTextTransformationService(settings),
        overlayService: new FaultTolerantOverlayService(
          new WindowsOverlayService(
            () => new WindowsOverlayPresenter(),
            overlayOptions,
            overlayAnchorProvider,
            diagnostics),
          diagnostics),
        undoInsertionService: textInsertionService,
        historyRecorder: historyRecorder);
    }

    public async ValueTask DisposeAsync()
    {
      if (HotkeyService is IAsyncDisposable hotkeyDisposable)
      {
        await hotkeyDisposable.DisposeAsync().ConfigureAwait(false);
      }

      if (UndoHotkeyService is IAsyncDisposable undoHotkeyDisposable)
      {
        await undoHotkeyDisposable.DisposeAsync().ConfigureAwait(false);
      }

      if (AudioCaptureService is IAsyncDisposable captureDisposable)
      {
        await captureDisposable.DisposeAsync().ConfigureAwait(false);
      }

      if (TranscriptionService is IAsyncDisposable transcriptionDisposable)
      {
        await transcriptionDisposable.DisposeAsync().ConfigureAwait(false);
      }

      if (TextTransformationService is IAsyncDisposable textTransformationDisposable)
      {
        await textTransformationDisposable.DisposeAsync().ConfigureAwait(false);
      }

      if (OverlayService is IAsyncDisposable overlayDisposable)
      {
        await overlayDisposable.DisposeAsync().ConfigureAwait(false);
      }
    }
  }

  private static RuntimeStartupNotice? BuildStartupNotice(AppSettings settings, RuntimeServices runtimeServices)
  {
    GlobalHotkeyRegistrationOutcome? outcome = runtimeServices.HotkeyService is GlobalToggleHotkeyService hotkeyService
      ? hotkeyService.LastRegistrationOutcome
      : null;
    return RuntimeStartupAdvisory.BuildNotice(settings, outcome);
  }
}

public sealed record RuntimeStartupNotice(string Title, string Message);
