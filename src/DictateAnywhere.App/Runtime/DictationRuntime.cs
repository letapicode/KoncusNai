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
  private readonly object disposalSync = new();
  private readonly CancellationTokenSource disposalCancellation = new();
  private TaskCompletionSource? disposal;
  private Task? disposalDriver;
  private Task? teardown;
  private Task? teardownDriver;
  private TaskCompletionSource? stopRequest;
  private Task? stopDriver;
  private CancellationTokenSource? pendingStartupCancellation;

  private RuntimeServices? services;
  private DictationPipelineCoordinator? coordinator;
  private UndoHotkeyCoordinator? undoHotkeyCoordinator;
  private RuntimeStartupNotice? startupNotice;
  private volatile bool isRunning;
  private volatile bool disposed;

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
    this.diagnostics = DiagnosticBoundary.Wrap(diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
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
    using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposalCancellation.Token);
    cancellationToken = startup.Token;

    await AcquireLifecycleAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (teardown is not null) await teardown.WaitAsync(cancellationToken).ConfigureAwait(false);

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
    using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposalCancellation.Token);
    cancellationToken = startup.Token;

    await AcquireLifecycleAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);

      await StopAndDisposeCoordinatorAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
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
    using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposalCancellation.Token);
    cancellationToken = startup.Token;

    await AcquireLifecycleAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ObjectDisposedException.ThrowIf(disposed, this);

      if (coordinator?.CurrentState != DictationSessionState.Idle)
      {
        return false;
      }

      await StopAndDisposeCoordinatorAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
      await CreateAndStartCoordinatorAsync(cancellationToken).ConfigureAwait(false);
      return true;
    }
    finally
    {
      lifecycleLock.Release();
    }
  }

  public Task StopAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    Task stop = RequestStop();
    return cancellationToken.CanBeCanceled ? stop.WaitAsync(cancellationToken) : stop;
  }

  private Task RequestStop()
  {
    TaskCompletionSource source;
    CancellationTokenSource? startupToCancel;
    DictationPipelineCoordinator? activeCoordinator;
    lock (disposalSync)
    {
      if (stopRequest is not null) return stopRequest.Task;
      source = stopRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
      isRunning = false;
      startupToCancel = pendingStartupCancellation;
      activeCoordinator = coordinator;
    }
    stopDriver = StopCoreAsync(source, startupToCancel, activeCoordinator);
    return source.Task;
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Stop closes startup admission before waiting for the lifecycle lock and owns cleanup independently of caller waits.")]
  private async Task StopCoreAsync(TaskCompletionSource source, CancellationTokenSource? startupToCancel,
    DictationPipelineCoordinator? activeCoordinator)
  {
    List<Exception> failures = [];
    // Close a ready graph before lock acquisition too. Startup's cancellation
    // registration owns the corresponding request for a not-yet-published graph.
    Task<Exception?> activeStop = StopOwner(() =>
    {
      try { return activeCoordinator?.StopAsync() ?? Task.CompletedTask; }
      catch (ObjectDisposedException) { return Task.CompletedTask; } // An already-disposed snapshot belongs to serialized teardown.
    });
    try { startupToCancel?.Cancel(); }
    catch (Exception exception) { failures.Add(exception); }
    await lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
    try { await StopAndDisposeCoordinatorAsync().ConfigureAwait(false); }
    catch (Exception exception) { failures.Add(exception); }
    finally { lifecycleLock.Release(); }
    Exception? activeFailure = await activeStop.ConfigureAwait(false);
    if (activeFailure is not null) failures.Add(activeFailure);
    if (failures.Count == 0) source.TrySetResult();
    else { source.TrySetException(new AggregateException(failures)); _ = source.Task.Exception; }
  }

  private async Task AcquireLifecycleAsync(CancellationToken token)
  {
    while (true)
    {
      Task? stopping;
      lock (disposalSync) stopping = stopRequest?.Task;
      if (stopping is not null) await stopping.WaitAsync(token).ConfigureAwait(false);
      await lifecycleLock.WaitAsync(token).ConfigureAwait(false);
      // Never await a stop holding the semaphore that stop itself needs.
      lock (disposalSync) { if (stopRequest?.Task.IsCompleted != false) return; }
      lifecycleLock.Release();
    }
  }

  public ValueTask DisposeAsync()
  {
    TaskCompletionSource source;
    lock (disposalSync)
    {
      if (disposal is not null) return new ValueTask(disposal.Task);
      disposed = true;
      source = disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    disposalDriver = DisposeCoreAsync(source);
    return new ValueTask(source.Task);
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Cancellation reporting failures cannot prevent owned runtime teardown.")]
  private async Task DisposeCoreAsync(TaskCompletionSource source)
  {
    List<Exception> failures = [];
    try { disposalCancellation.Cancel(); }
    catch (Exception exception) { failures.Add(exception); }
    try
    {
      await RequestStop().ConfigureAwait(false);
    }
    catch (Exception exception) { failures.Add(exception); }
    // Queued public callers may still unwind. These managed admission objects
    // are reclaimed with the owner, never disposed beneath those callers.
    if (failures.Count == 0) source.TrySetResult();
    else { source.TrySetException(new AggregateException(failures)); _ = source.Task.Exception; }
  }

  private async Task CreateAndStartCoordinatorAsync(CancellationToken cancellationToken)
  {
    CancellationTokenSource admission;
    lock (disposalSync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (stopRequest?.Task.IsCompleted == false) throw new OperationCanceledException(cancellationToken);
      stopRequest = null;
      admission = pendingStartupCancellation = new();
    }
    // The raw admission source is reclaimed with its owner: a stop can hold a
    // snapshot while startup finishes. Its linked registrations are disposed.
    using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, admission.Token);
    try { await CreateAndStartOwnedCoordinatorAsync(startup.Token).ConfigureAwait(false); }
    finally { lock (disposalSync) pendingStartupCancellation = null; }
  }

  [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
    Justification = "Successful graphs transfer to runtime fields under the admission lock; LifecycleCleanup releases every partial owner on failure.")]
  private async Task CreateAndStartOwnedCoordinatorAsync(CancellationToken cancellationToken)
  {
    AppSettings settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    ObjectDisposedException.ThrowIf(disposed, this);
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

    UndoHotkeyCoordinator? runtimeUndoCoordinator = null;
    using CancellationTokenRegistration stopStartupCoordinator = cancellationToken.Register(() =>
    {
      try
      {
        // Core retains/observes its shared stop promise. Request only: awaiting
        // this inside the cancellation callback would self-await startup.
        Task retainedStop = runtimeCoordinator.StopAsync();
      }
      catch (ObjectDisposedException) { /* Partial startup already released this coordinator. */ }
    });
    try
    {
      await runtimeCoordinator.StartAsync(cancellationToken).ConfigureAwait(false);

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
            DiagnosticBoundary.Report(() => diagnostics.Warning($"Undo hotkey unavailable: {ex.Message}"));
            await runtimeUndoCoordinator.DisposeAsync().ConfigureAwait(false);
            runtimeUndoCoordinator = null;
          }
        }
      }

      lock (disposalSync)
      {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopRequest is not null) throw new OperationCanceledException(cancellationToken);
        undoHotkeyCoordinator = runtimeUndoCoordinator;
        services = runtimeServices;
        coordinator = runtimeCoordinator;
        teardown = null;
        startupNotice = BuildStartupNotice(settings, runtimeServices);
        isRunning = true;
      }
    }
    catch
    {
      undoHotkeyCoordinator = null;
      await LifecycleCleanup.RunAsync(
        new CleanupStep("Partial undo coordinator", () => runtimeUndoCoordinator?.DisposeAsync().AsTask() ?? Task.CompletedTask),
        new CleanupStep("Partial coordinator", () => runtimeCoordinator.DisposeAsync().AsTask()),
        new CleanupStep("Partial runtime services", () => runtimeServices.DisposeAsync().AsTask())).ConfigureAwait(false);
      isRunning = false;
      throw;
    }

  }

  private Task StopAndDisposeCoordinatorAsync()
  {
    if (teardown is not null) return teardown;
    DictationPipelineCoordinator? coordinatorToStop = coordinator;
    RuntimeServices? servicesToDispose = services;
    UndoHotkeyCoordinator? undoCoordinatorToStop = undoHotkeyCoordinator;

    startupNotice = null;
    isRunning = false;
    TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
    teardown = source.Task;
    teardownDriver = TeardownCoreAsync(source, coordinatorToStop, servicesToDispose, undoCoordinatorToStop);
    return teardown;
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Stop failures are reported after every retained owner drains and independent disposal is attempted.")]
  private async Task TeardownCoreAsync(TaskCompletionSource source, DictationPipelineCoordinator? coordinatorToStop,
    RuntimeServices? servicesToDispose, UndoHotkeyCoordinator? undoCoordinatorToStop)
  {
    // Stop both event admissions before awaiting either drain. An undo operation
    // must not leave global dictation hotkeys accepting new capture during quit.
    Task<Exception?> dictationStop = StopOwner(() => coordinatorToStop?.StopAsync() ?? Task.CompletedTask);
    Task<Exception?> undoStop = StopOwner(() => undoCoordinatorToStop?.StopAsync() ?? Task.CompletedTask);
    Exception?[] stopFailures = await Task.WhenAll(dictationStop, undoStop).ConfigureAwait(false);
    List<Exception> failures = stopFailures.OfType<Exception>().ToList();
    try
    {
      await LifecycleCleanup.RunAsync(
        new CleanupStep("Undo coordinator", () => undoCoordinatorToStop?.DisposeAsync().AsTask() ?? Task.CompletedTask),
        new CleanupStep("Dictation coordinator", () => coordinatorToStop?.DisposeAsync().AsTask() ?? Task.CompletedTask),
        new CleanupStep("Runtime services", () => servicesToDispose?.DisposeAsync().AsTask() ?? Task.CompletedTask)).ConfigureAwait(false);
    }
    catch (Exception exception) { failures.Add(exception); }
    coordinator = null;
    services = null;
    undoHotkeyCoordinator = null;
    if (failures.Count == 0) source.TrySetResult();
    else { source.TrySetException(new AggregateException(failures)); _ = source.Task.Exception; }
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Retained stop attempts report faults after all independent owners drain.")]
  private static async Task<Exception?> StopOwner(Func<Task> stop)
  {
    try { await stop().ConfigureAwait(false); return null; }
    catch (Exception exception) { return exception; }
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
        textInsertionService: new WorkbenchTextInsertionService(textInsertionService,
          () => (settings.InsertionBlockedProcessNames ?? Array.Empty<string>()).Any(name =>
            string.Equals(System.IO.Path.GetFileNameWithoutExtension(name),
              System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase))
            ? null : WorkbenchTextInsertionService.CaptureActiveComposer()),
        textTransformationService: RuntimeServiceFactory.CreateTextTransformationService(settings),
        overlayService: new FaultTolerantOverlayService(
          new WindowsOverlayService(
            () => new OwnedOverlayPresenter(new WindowsOverlayPresenter()),
            overlayOptions,
            overlayAnchorProvider,
            diagnostics),
          diagnostics),
        undoInsertionService: textInsertionService,
        historyRecorder: historyRecorder);
    }

    public ValueTask DisposeAsync() => new(LifecycleCleanup.RunAsync(
      new CleanupStep("Hotkey service", () => DisposeOwnedAsync(HotkeyService)),
      new CleanupStep("Undo hotkey service", () => DisposeOwnedAsync(UndoHotkeyService)),
      new CleanupStep("Audio capture", () => DisposeOwnedAsync(AudioCaptureService)),
      new CleanupStep("Transcription", () => DisposeOwnedAsync(TranscriptionService)),
      new CleanupStep("Transformation", () => DisposeOwnedAsync(TextTransformationService)),
      new CleanupStep("Overlay", () => DisposeOwnedAsync(OverlayService))));

    private static Task DisposeOwnedAsync(object service) => service is IAsyncDisposable disposable
      ? disposable.DisposeAsync().AsTask() : Task.CompletedTask;
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
