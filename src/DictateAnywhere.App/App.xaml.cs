using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DictateAnywhere.App.Diagnostics;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Experience;
using DictateAnywhere.App.FirstRun;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Productivity;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Settings;
using DictateAnywhere.App.Startup;
using DictateAnywhere.App.Tray;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Diagnostics;
using DictateAnywhere.Hotkeys;
using DictateAnywhere.Models;
using Forms = System.Windows.Forms;

namespace DictateAnywhere.App;

public partial class App : Application
{
  private readonly SemaphoreSlim watchdogTickLock = new(1, 1);
  private readonly DictationHistoryChangeNotifier historyChangeNotifier = new();
  private ApplicationComposition? composition;

  private SingleInstanceMutexGuard? instanceGuard;
  private ISettingsStore? settingsStore;
  private IModelManager? modelManager;
  private IStartupRegistrationService? startupRegistrationService;
  private LocalFileDiagnostics? diagnostics;
  private ApplicationHost? applicationHost;
  private ProductivityHotkeyCoordinator? productivityHotkeyCoordinator;
  private WindowCoordinator? windowCoordinator;
  private TrayCommandCoordinator? trayCommandCoordinator;
  private DispatcherTimer? runtimeWatchdogTimer;
  private RuntimeWatchdog? runtimeWatchdog;
  private RuntimeStartupNotice? pendingRuntimeNotice;
  private string? lastShownRuntimeNoticeMessage;
  private IDisposable? textScaleSubscription;
  private EventWaitHandle? activationSignal;
  private DispatcherTimer? activationTimer;
  private bool isShuttingDown;
  private readonly CancellationTokenSource shutdownCancellation = new();
  private Task startupTask = Task.CompletedTask;
  private Task watchdogTask = Task.CompletedTask;
  private ApplicationShutdown? shutdown;
  private Task? quitTask;
  private int requestedExitCode;
  private Task admissionTask = Task.CompletedTask;
  private Task windowCleanup = Task.CompletedTask;

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
    startupTask = StartApplicationAsync(e);
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Startup is an async WPF event boundary; all failures must enter owned shutdown.")]
  private async Task StartApplicationAsync(StartupEventArgs e)
  {
    // Publish startup ownership before a nested modal dispatcher can request quit.
    await Task.Yield();
    try
    {
      shutdownCancellation.Token.ThrowIfCancellationRequested();
      Activated += OnApplicationActivated;
      AppTextScaleManager.ApplyCurrent(Resources);
      textScaleSubscription = AppTextScaleManager.Subscribe(Dispatcher, Resources);
      AppThemeManager.ApplyThemeResources(AppSettings.Default.ThemePreference);

      activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset,
        @"Local\DictateAnywhere.Activate." + Environment.UserName);
      instanceGuard = new SingleInstanceMutexGuard(SingleInstanceMutexGuard.DefaultMutexName);
      if (!instanceGuard.TryAcquire())
      {
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) activationSignal.Set();
        RequestShutdown(0);
        return;
      }

      composition = ApplicationComposition.CreateProduction();
      settingsStore = composition.SettingsStore;
      modelManager = composition.TranscriptionModelManager;
      startupRegistrationService = composition.CreateStartupRegistrationService();
      diagnostics = composition.Diagnostics;
      applicationHost = composition.CreateApplicationHost(historyChangeNotifier);
      applicationHost.ModelReadinessChanged += OnModelReadinessSnapshotChanged;
      productivityHotkeyCoordinator = composition.CreateProductivityHotkeyCoordinator();
      windowCoordinator = composition.CreateWindowCoordinator(Dispatcher, historyChangeNotifier);
      windowCoordinator.SettingsRequested += OnWorkbenchOpenSettingsRequested;
      windowCoordinator.SettingsSaved += OnSettingsSaved;
      windowCoordinator.HistoryRequested += OnSettingsManageHistoryRequested;
      windowCoordinator.ThemePreferenceRequested += OnWorkbenchThemePreferenceRequested;
      windowCoordinator.TranscriptionModelSelectionRequested += OnWorkbenchTranscriptionModelSelectionRequested;
      windowCoordinator.ChatOutputFontSizeRequested += OnWorkbenchChatOutputFontSizeRequested;
      windowCoordinator.ChatPaperViewRequested += OnWorkbenchChatPaperViewRequested;
      windowCoordinator.WorkbenchZoomRequested += OnWorkbenchZoomRequested;
      activationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
      activationTimer.Tick += OnActivationRequested;
      activationTimer.Start();
      TrayCommandHandlers trayHandlers = new(
        OpenSettingsAsync,
        OpenWorkbenchAsync,
        OpenHistoryAsync,
        ApplyStartupToggleAsync,
        RetryLastDictationAsync,
        ApplyQuickModelSwitchAsync,
        ExportDiagnosticsBundleAsync,
        () => RequestShutdown(),
        (operation, message, exception) => ReportUserFacingError(operation, message, exception, MessageBoxImage.Warning));
      trayCommandCoordinator = composition.CreateTrayCommandCoordinator(
        trayHandlers,
        () => applicationHost?.IsRunning == true
          ? applicationHost.CurrentState
          : DictationSessionState.Error);

      AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
      shutdownCancellation.Token.ThrowIfCancellationRequested();
      AppThemeManager.ApplyThemeResources(settings.ThemePreference);
      _ = OllamaStartupShortcutPolicy.TryDisableAutomaticStartup();
      if (!AppLegalAcceptancePolicy.HasCurrentAcceptance(settings))
      {
        LegalAcknowledgementWindow legalAcknowledgement = new();
        composition.WindowLifetimes.Register(legalAcknowledgement, legalAcknowledgement.Close, () => Task.CompletedTask);
        legalAcknowledgement.Closed += (_, _) => _ = LifecycleCleanup.ObserveAsync(
          composition.WindowLifetimes.CloseAsync(legalAcknowledgement, alreadyClosed: true),
          diagnostics.Error, "Legal acknowledgement close");
        if (legalAcknowledgement.ShowDialog() != true)
        {
          RequestShutdown();
          return;
        }

        AppSettings accepted = AppLegalAcceptancePolicy.AcceptCurrentVersion(settings, DateTimeOffset.UtcNow);
        settings = await settingsStore.SaveChangesAsync(settings, accepted, shutdownCancellation.Token).ConfigureAwait(true);
        shutdownCancellation.Token.ThrowIfCancellationRequested();
      }

      if (FirstRunWizardGuard.ShouldShowWizard(settings))
      {
        FirstRunWizardWindow wizard = composition.CreateFirstRunWizard();

        bool? wizardResult = wizard.ShowDialog();
        if (wizardResult != true)
        {
          RequestShutdown();
          return;
        }

        settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
        AppThemeManager.ApplyThemeResources(settings.ThemePreference);
      }

      shutdownCancellation.Token.ThrowIfCancellationRequested();
      trayCommandCoordinator.Start(
        applicationHost.CurrentReadiness,
        startupRegistrationService.IsEnabled());
      await RefreshTrayModelsAsync().ConfigureAwait(true);
      await RestartProductivityHotkeysAsync(settings).ConfigureAwait(true);
      bool runtimeStarted = await StartSelectedExperienceAsync(settings, showWorkbench: !AppLaunchOptions.IsBackgroundLaunch(e.Args)).ConfigureAwait(true);
      ShowPendingRuntimeNotice();

      if (!runtimeStarted)
      {
        trayCommandCoordinator?.SetStatus(DictationSessionState.Error);
      }
    }
    catch (OperationCanceledException) when (isShuttingDown) { }
    catch (Exception ex)
    {
      LifecycleCleanup.Report((_, error) => ReportUserFacingError("Application startup", "Application startup failed.", error, MessageBoxImage.Error),
        "Application startup", ex);
      RequestShutdown(-1);
    }
  }

  protected override void OnExit(ExitEventArgs e)
  {
    // Cooperative paths finish cleanup before calling Shutdown. Forced termination
    // cannot be made awaitable from this synchronous WPF notification.
    base.OnExit(e);
  }

  protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
  {
    // Veto this cooperative request while our dispatcher drains. Windows may still
    // force termination; this is not a guarantee against logoff deadlines/power loss.
    e.Cancel = true;
    RequestShutdown();
    base.OnSessionEnding(e);
  }

  private void RequestShutdown(int exitCode = 0)
  {
    if (exitCode != 0) requestedExitCode = exitCode;
    if (quitTask is not null) return;
    shutdown ??= new ApplicationShutdown(TimeSpan.FromSeconds(10),
      (name, exception) => diagnostics?.Error($"Shutdown incomplete: {name}", exception));
    // Begin synchronously: queued input must not sneak in before the next UI turn.
    isShuttingDown = true;
    TaskCompletionSource admission = new(TaskCreationOptions.RunContinuationsAsynchronously);
    admissionTask = admission.Task;
    quitTask = QuitAsync();
    _ = StopAdmissionAsync(admission);
  }

  [SuppressMessage("Design", "CA1031", Justification = "Transfers cancellation and admission failures to the published shutdown task.")]
  private async Task StopAdmissionAsync(TaskCompletionSource source)
  {
    try
    {
      await LifecycleCleanup.RunAsync(
        LifecycleCleanup.Sync("Stop tray input", () => trayCommandCoordinator?.BeginShutdown()),
        LifecycleCleanup.Sync("Cancel application startup", shutdownCancellation.Cancel),
        LifecycleCleanup.Sync("Stop runtime admission", () => applicationHost?.BeginShutdown()),
        LifecycleCleanup.Sync("Stop activation timer", () =>
        {
          activationTimer?.Stop();
          if (activationTimer is not null) activationTimer.Tick -= OnActivationRequested;
          Activated -= OnApplicationActivated;
        }),
        LifecycleCleanup.Sync("Stop watchdog", StopRuntimeWatchdog),
        LifecycleCleanup.Sync("Start window cleanup", () =>
        {
          windowCleanup = windowCoordinator?.DisposeAsync().AsTask()
            ?? composition?.WindowLifetimes.DisposeAsync() ?? Task.CompletedTask;
        })).ConfigureAwait(true);
      source.TrySetResult();
    }
    catch (Exception error) { source.TrySetException(error); }
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Final logger disposal must not prevent dispatcher shutdown.")]
  private async Task QuitAsync()
  {
    await Task.Yield(); // Never await a tray command from within that command's stack.
    bool shellDrained = false;
    bool windowsDrained = false;
    bool completed = await shutdown!.RunAsync(
      new CleanupStep("Stop admission", () => admissionTask),
      new CleanupStep("Drain application commands", async () =>
      {
        try
        {
          await LifecycleCleanup.RunAsync(
            new CleanupStep("Startup", () => startupTask),
            new CleanupStep("Tray", () => trayCommandCoordinator?.DisposeAsync().AsTask() ?? Task.CompletedTask),
            new CleanupStep("Watchdog", () => watchdogTask)).ConfigureAwait(true);
        }
        finally { shellDrained = startupTask.IsCompleted && watchdogTask.IsCompleted
          && (trayCommandCoordinator is null || trayCommandCoordinator.IsDisposed); }
      }),
      new CleanupStep("Drain windows", async () =>
      {
        try { await windowCleanup.ConfigureAwait(true); }
        finally { windowsDrained = windowCleanup.IsCompletedSuccessfully; }
      }),
      new CleanupStep("Runtime and readiness", () => shellDrained && windowsDrained
        ? applicationHost?.DisposeAsync().AsTask() ?? Task.CompletedTask
        : Task.FromException(new InvalidOperationException("Runtime retained because dependent work has not drained."))),
      new CleanupStep("Productivity hotkeys", () => shellDrained
        ? productivityHotkeyCoordinator?.DisposeAsync().AsTask() ?? Task.CompletedTask
        : Task.FromException(new InvalidOperationException("Hotkey resources retained because commands have not drained."))),
      new CleanupStep("Owned Ollama", () => composition is not null && shellDrained && windowsDrained
        ? OllamaProcessOwnership.StopOwnedAsync() : Task.CompletedTask),
      new CleanupStep("Final process resources", () => LifecycleCleanup.RunAsync(
        LifecycleCleanup.Sync("Activation signal", () => activationSignal?.Dispose()),
        LifecycleCleanup.Sync("Theme subscription", () => textScaleSubscription?.Dispose()),
        LifecycleCleanup.Sync("Instance guard", () => instanceGuard?.Dispose())))).ConfigureAwait(true);
    // Timed-out tasks may still report; retain their logger until process exit.
    if (completed)
    {
      try { diagnostics?.Dispose(); }
      catch (Exception exception)
      {
        completed = false;
        LifecycleCleanup.Report((name, error) => diagnostics?.Error(name, error), "Diagnostics disposal", exception);
      }
    }
    Shutdown(requestedExitCode != 0 ? requestedExitCode : completed ? 0 : -1);
  }

  private void OnApplicationActivated(object? sender, EventArgs e)
  {
    if (isShuttingDown) return;
    AppThemeManager.ApplyThemeResources(AppThemeManager.CurrentPreference);
  }

  private async void OnActivationRequested(object? sender, EventArgs e)
  {
    if (!isShuttingDown && activationSignal?.WaitOne(0) == true)
      await RunTrayActionAsync(OpenWorkbenchAsync).ConfigureAwait(true);
  }

  private void InitializeRuntimeWatchdog()
  {
    if (applicationHost is null || diagnostics is null)
    {
      return;
    }

    StopRuntimeWatchdog();

    runtimeWatchdog = new RuntimeWatchdog(
      applicationHost,
      diagnostics,
      retryInterval: TimeSpan.FromSeconds(20));

    runtimeWatchdogTimer = new DispatcherTimer
    {
      Interval = TimeSpan.FromSeconds(5),
    };
    runtimeWatchdogTimer.Tick += OnRuntimeWatchdogTimerTick;
    runtimeWatchdogTimer.Start();
  }

  private void StopRuntimeWatchdog()
  {
    if (runtimeWatchdogTimer is not null)
    {
      runtimeWatchdogTimer.Stop();
      runtimeWatchdogTimer.Tick -= OnRuntimeWatchdogTimerTick;
      runtimeWatchdogTimer = null;
    }

    runtimeWatchdog = null;
  }

  private async void OnSettingsSaved(object? sender, EventArgs e)
  {
    await RunTrayActionAsync(async () =>
    {
      if (settingsStore is null)
      {
        throw new InvalidOperationException("Settings store is not initialized.");
      }

      AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
      shutdownCancellation.Token.ThrowIfCancellationRequested();
      AppThemeManager.ApplyThemeResources(settings.ThemePreference);
      if (windowCoordinator is not null)
      {
        await windowCoordinator.ApplySettingsToOpenHistoryAsync(settings).ConfigureAwait(true);
      }
      bool runtimeStarted = await RestartRuntimeForSettingsAsync(settings).ConfigureAwait(true);
      if (windowCoordinator?.IsWorkbenchOpen == true)
      {
        await EnsureWorkbenchAsync(settings, registerWorkbenchHotkey: !runtimeStarted).ConfigureAwait(true);
      }

      if (startupRegistrationService is not null && trayCommandCoordinator is not null)
      {
        trayCommandCoordinator.SetStartOnLoginEnabled(startupRegistrationService.IsEnabled());
      }

      await RefreshTrayModelsAsync().ConfigureAwait(true);
      await RestartProductivityHotkeysAsync(settings).ConfigureAwait(true);
    }).ConfigureAwait(true);
  }

  private async Task<bool> StartSelectedExperienceAsync(AppSettings settings, bool showWorkbench)
  {
    bool runtimeStarted = await RestartRuntimeForSettingsAsync(settings).ConfigureAwait(true);
    if (showWorkbench && settings.AssistantFeaturesEnabled)
    {
      await EnsureWorkbenchAsync(settings, registerWorkbenchHotkey: !runtimeStarted).ConfigureAwait(true);
    }

    return runtimeStarted;
  }

  private async Task<bool> RestartRuntimeForSettingsAsync(AppSettings settings)
  {
    if (isShuttingDown) return false;
    if (applicationHost is null)
    {
      throw new InvalidOperationException("Application host is not initialized.");
    }

    RuntimeSettingsApplyResult result = await applicationHost
      .ApplyRuntimeSettingsAsync(settings, shutdownCancellation.Token)
      .ConfigureAwait(true);
    if (isShuttingDown) return false;
    QueueRuntimeNotice(result.Notice);
    if (result.Deferred)
    {
      return true;
    }

    StopRuntimeWatchdog();
    if (!result.IsRunning)
    {
      if (result.Failure is not null)
      {
        ReportUserFacingError(
          "Runtime startup",
          "Dictation runtime failed to start. The watchdog will retry automatically.",
          result.Failure,
          MessageBoxImage.Warning);
      }

      trayCommandCoordinator?.SetStatus(DictationSessionState.Error);
      InitializeRuntimeWatchdog();
      return false;
    }

    InitializeRuntimeWatchdog();
    return true;
  }

  private Task EnsureWorkbenchAsync(AppSettings settings, bool registerWorkbenchHotkey = true)
  {
    if (isShuttingDown) return Task.CompletedTask;
    if (windowCoordinator is null)
    {
      throw new InvalidOperationException("Window coordinator is not initialized.");
    }

    Func<AppSettings, IDiagnostics, ITranscriptionService>? transcriptionFactory = applicationHost is null
      ? null
      : applicationHost.CreateTranscriptionService;
    return windowCoordinator.EnsureWorkbenchAsync(settings, registerWorkbenchHotkey, transcriptionFactory);
  }

  private async void OnWorkbenchOpenSettingsRequested()
  {
    await RunTrayActionAsync(OpenSettingsAsync).ConfigureAwait(true);
  }

  private async void OnWorkbenchThemePreferenceRequested(AppThemePreference preference)
  {
    await RunTrayActionAsync(() => ApplyWorkbenchThemePreferenceAsync(preference)).ConfigureAwait(true);
  }

  private async void OnWorkbenchTranscriptionModelSelectionRequested(TranscriptionModelSelection selection)
  {
    await RunTrayActionAsync(() => ApplyQuickModelSwitchAsync(selection)).ConfigureAwait(true);
  }

  private async void OnWorkbenchChatOutputFontSizeRequested(int fontSize)
  {
    await RunTrayActionAsync(() => ApplyWorkbenchChatOutputFontSizeAsync(fontSize)).ConfigureAwait(true);
  }

  private async void OnWorkbenchChatPaperViewRequested(bool enabled)
  {
    await RunTrayActionAsync(() => ApplyWorkbenchChatPaperViewAsync(enabled)).ConfigureAwait(true);
  }

  private async void OnWorkbenchZoomRequested(int percent)
  {
    await RunTrayActionAsync(() => ApplyWorkbenchZoomAsync(percent)).ConfigureAwait(true);
  }

  private async Task ApplyWorkbenchThemePreferenceAsync(AppThemePreference preference)
  {
    if (settingsStore is null)
    {
      throw new InvalidOperationException("Settings store is not initialized.");
    }

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
    AppSettings updated = settings with
    {
      ThemePreference = preference,
    };
    _ = await settingsStore.SaveChangesAsync(settings, updated, shutdownCancellation.Token).ConfigureAwait(true);
    shutdownCancellation.Token.ThrowIfCancellationRequested();
    AppThemeManager.ApplyThemeResources(preference);
  }

  private async Task ApplyWorkbenchChatOutputFontSizeAsync(int fontSize)
  {
    if (settingsStore is null)
    {
      throw new InvalidOperationException("Settings store is not initialized.");
    }

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
    AppSettings updated = settings with
    {
      ChatOutputFontSize = ChatTextSizePolicy.Normalize(fontSize),
    };
    _ = await settingsStore.SaveChangesAsync(settings, updated, shutdownCancellation.Token).ConfigureAwait(true);
  }

  private async Task ApplyWorkbenchChatPaperViewAsync(bool enabled)
  {
    if (settingsStore is null)
      throw new InvalidOperationException("Settings store is not initialized.");

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
    _ = await settingsStore.SaveChangesAsync(settings, settings with { ChatPaperViewEnabled = enabled }, shutdownCancellation.Token).ConfigureAwait(true);
  }

  private async Task ApplyWorkbenchZoomAsync(int percent)
  {
    if (settingsStore is null)
    {
      throw new InvalidOperationException("Settings store is not initialized.");
    }

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
    AppSettings updated = settings with
    {
      WorkbenchZoomPercent = Math.Clamp(percent, 80, 150),
    };
    _ = await settingsStore.SaveChangesAsync(settings, updated, shutdownCancellation.Token).ConfigureAwait(true);
  }

  private async Task OpenSettingsAsync()
  {
    if (isShuttingDown) return;
    if (windowCoordinator is null)
    {
      throw new InvalidOperationException("Window coordinator is not initialized.");
    }

    Func<AppSettings, IDiagnostics, ITranscriptionService>? transcriptionFactory = applicationHost is null
      ? null
      : applicationHost.CreateTranscriptionService;
    await windowCoordinator
      .OpenSettingsAsync(applicationHost?.IsRunning != true, transcriptionFactory)
      .ConfigureAwait(true);
  }

  private async void OnSettingsManageHistoryRequested(object? sender, EventArgs e)
  {
    await RunTrayActionAsync(OpenHistoryAsync).ConfigureAwait(true);
  }

  private async Task OpenWorkbenchAsync()
  {
    if (settingsStore is null)
    {
      throw new InvalidOperationException("Settings store is not initialized.");
    }

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
    if (isShuttingDown) return;
    if (!settings.AssistantFeaturesEnabled)
    {
      await OpenSettingsAsync().ConfigureAwait(true);
      return;
    }
    bool runtimeStarted = applicationHost?.IsRunning == true;
    if (!runtimeStarted)
    {
      runtimeStarted = await RestartRuntimeForSettingsAsync(settings).ConfigureAwait(true);
    }

    if (isShuttingDown) return;
    await EnsureWorkbenchAsync(settings, registerWorkbenchHotkey: !runtimeStarted).ConfigureAwait(true);
    await RefreshTrayModelsAsync().ConfigureAwait(true);
    await RestartProductivityHotkeysAsync(settings).ConfigureAwait(true);
  }

  private async Task OpenHistoryAsync()
  {
    if (isShuttingDown) return;
    if (windowCoordinator is null)
    {
      throw new InvalidOperationException("Window coordinator is not initialized.");
    }

    await windowCoordinator.OpenHistoryAsync().ConfigureAwait(true);
  }

  private Task ApplyStartupToggleAsync(bool enabled)
  {
    if (startupRegistrationService is null || trayCommandCoordinator is null)
    {
      throw new InvalidOperationException("Startup registration service is not initialized.");
    }

    startupRegistrationService.SetEnabled(enabled);
    trayCommandCoordinator.SetStartOnLoginEnabled(startupRegistrationService.IsEnabled());
    return Task.CompletedTask;
  }

  private async Task RetryLastDictationAsync()
  {
    if (settingsStore is null || diagnostics is null)
    {
      throw new InvalidOperationException("Application services are not initialized.");
    }

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
    if (composition is null)
    {
      throw new InvalidOperationException("Application composition is not initialized.");
    }

    ProductivityActionResult result = await composition
      .RetryLastDictationAsync(settings)
      .ConfigureAwait(true);
    if (isShuttingDown) return;
    RetryActionDiagnostics.Write(diagnostics, result);
    ShowProductivityActionResult(result);
  }

  private void ShowProductivityActionResult(ProductivityActionResult result)
  {
    if (result.Success || isShuttingDown)
    {
      return;
    }

    Window? mainWindow = MainWindow;
    RetryFeedbackWindow notice = new(result);
    if (ReferenceEquals(MainWindow, notice)) MainWindow = mainWindow;
    notice.Show();
  }

  private async Task ApplyQuickModelSwitchAsync(TranscriptionModelSelection selection)
  {
    ArgumentNullException.ThrowIfNull(selection);

    TranscriptionModelSelection normalizedSelection = selection.Normalize();
    if (string.IsNullOrWhiteSpace(normalizedSelection.ModelId))
    {
      return;
    }

    if (modelManager is null || settingsStore is null)
    {
      throw new InvalidOperationException("Application services are not initialized.");
    }

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
    if (isShuttingDown) return;
    AppSettings baseline = settings;
    if (!CrisperWhisperLicenseConfirmation.EnsureAccepted(
          Current?.MainWindow,
          normalizedSelection,
          settings))
    {
      return;
    }

    if (CrisperWhisperLicensePolicy.IsCrisperWhisper(normalizedSelection.ProviderId)
        && !CrisperWhisperLicensePolicy.HasCurrentAcceptance(settings))
    {
      settings = CrisperWhisperLicensePolicy.AcceptCurrentVersion(settings);
    }

    await modelManager.SetActiveModelAsync(normalizedSelection, shutdownCancellation.Token).ConfigureAwait(true);
    shutdownCancellation.Token.ThrowIfCancellationRequested();

    AppSettings updated = settings.WithConfiguredTranscription(
      normalizedSelection.ProviderId,
      normalizedSelection.ModelId);
    updated = await settingsStore.SaveChangesAsync(baseline, updated, shutdownCancellation.Token).ConfigureAwait(true);
    shutdownCancellation.Token.ThrowIfCancellationRequested();

    bool runtimeStarted = false;
    if (applicationHost is not null)
    {
      runtimeStarted = await RestartRuntimeForSettingsAsync(updated).ConfigureAwait(true);
    }

    if (windowCoordinator?.IsWorkbenchOpen == true)
    {
      await EnsureWorkbenchAsync(updated, registerWorkbenchHotkey: !runtimeStarted).ConfigureAwait(true);
    }

    await RefreshTrayModelsAsync().ConfigureAwait(true);
  }

  private async Task RefreshTrayModelsAsync()
  {
    if (trayCommandCoordinator is null || modelManager is null || settingsStore is null)
    {
      return;
    }

    IReadOnlyList<ModelInfo> models = await modelManager.GetModelsAsync().ConfigureAwait(true);
    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync(shutdownCancellation.Token).ConfigureAwait(true));
    if (isShuttingDown) return;
    trayCommandCoordinator.SetModelMenu(models, settings.GetConfiguredTranscriptionSelection());
  }

  private Task RestartProductivityHotkeysAsync(AppSettings settings)
  {
    if (isShuttingDown || productivityHotkeyCoordinator is null)
    {
      return Task.CompletedTask;
    }

    return productivityHotkeyCoordinator.RestartAsync(
      settings,
      () => Dispatcher.InvokeAsync(async () => await RunTrayActionAsync(RetryLastDictationAsync).ConfigureAwait(true)).Task.Unwrap());
  }

  private Task ExportDiagnosticsBundleAsync()
  {
    if (diagnostics is null)
    {
      throw new InvalidOperationException("Diagnostics service is not initialized.");
    }

    string destinationDirectory = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "support");
    try
    {
      string bundlePath = diagnostics.ExportBundle(destinationDirectory);
      DiagnosticsBundleVerifier.Verify(bundlePath);
      Window? mainWindow = MainWindow;
      DiagnosticsExportResultWindow result = new(bundlePath);
      if (ReferenceEquals(MainWindow, result)) MainWindow = mainWindow;
      result.Show();
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException
      or UnauthorizedAccessException or InvalidOperationException)
    {
      _ = MessageBox.Show(
        $"Could not export diagnostics to:\n{destinationDirectory}\n\n{exception.Message}\n\nCheck that the folder is writable and has free space, then try again.",
        "Diagnostics export failed",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
    }
    return Task.CompletedTask;
  }

  private void OnModelReadinessSnapshotChanged(object? sender, ModelReadinessSnapshot snapshot)
  {
    _ = Dispatcher.BeginInvoke(new Action(() =>
    {
      if (!isShuttingDown) trayCommandCoordinator?.SetModelReadiness(snapshot);
    }));
  }

  private void OnRuntimeWatchdogTimerTick(object? sender, EventArgs e)
  {
    if (isShuttingDown || !watchdogTask.IsCompleted) return;
    watchdogTask = RunWatchdogTickAsync();
    _ = LifecycleCleanup.ObserveAsync(watchdogTask,
      (name, error) => diagnostics?.Error(name, error), "Watchdog callback");
  }

  private async Task RunWatchdogTickAsync()
  {
    if (isShuttingDown || runtimeWatchdog is null)
    {
      return;
    }

    if (!await watchdogTickLock.WaitAsync(0).ConfigureAwait(true))
    {
      return;
    }

    try
    {
      await runtimeWatchdog.TickAsync(shutdownCancellation.Token).ConfigureAwait(true);
      if (isShuttingDown) return;
      await TryApplyPendingRuntimeSettingsAsync().ConfigureAwait(true);
      trayCommandCoordinator?.SetStatus(applicationHost?.CurrentState ?? DictationSessionState.Idle);
    }
    catch (OperationCanceledException) when (isShuttingDown) { }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      DiagnosticBoundary.Report(() => diagnostics?.Warning($"Runtime watchdog tick failed: {ex.Message}"));
    }
    finally
    {
      watchdogTickLock.Release();
    }
  }

  private async Task TryApplyPendingRuntimeSettingsAsync()
  {
    if (applicationHost?.HasPendingSettings != true
        || applicationHost.CurrentState != DictationSessionState.Idle
        || trayCommandCoordinator is null)
    {
      return;
    }

    _ = await trayCommandCoordinator.TryRunAsync(async () =>
    {
      if (applicationHost.HasPendingSettings
          && applicationHost.CurrentState == DictationSessionState.Idle)
      {
        RuntimeSettingsApplyResult? result = await applicationHost.TryApplyPendingSettingsAsync().ConfigureAwait(true);
        if (result is not null)
        {
          QueueRuntimeNotice(result.Notice);
          if (result.Failure is not null)
          {
            throw new InvalidOperationException("Pending runtime settings could not be applied.", result.Failure);
          }
        }
      }
    }).ConfigureAwait(true);
  }

  private Task RunTrayActionAsync(Func<Task> action)
  {
    if (isShuttingDown) return Task.CompletedTask;
    if (trayCommandCoordinator is null)
    {
      throw new InvalidOperationException("Tray command coordinator is not initialized.");
    }

    return trayCommandCoordinator.RunAsync(action);
  }

  private void QueueRuntimeNotice(RuntimeStartupNotice? notice)
  {
    if (notice is null || string.IsNullOrWhiteSpace(notice.Message))
    {
      return;
    }

    pendingRuntimeNotice = notice;
    ShowPendingRuntimeNotice();
  }

  private void ShowPendingRuntimeNotice()
  {
    if (isShuttingDown || trayCommandCoordinator is null || pendingRuntimeNotice is null)
    {
      return;
    }

    if (string.Equals(lastShownRuntimeNoticeMessage, pendingRuntimeNotice.Message, StringComparison.Ordinal))
    {
      pendingRuntimeNotice = null;
      return;
    }

    trayCommandCoordinator.ShowNotification(
      pendingRuntimeNotice.Title,
      pendingRuntimeNotice.Message,
      Forms.ToolTipIcon.Warning);
    lastShownRuntimeNoticeMessage = pendingRuntimeNotice.Message;
    pendingRuntimeNotice = null;
  }

  private void ReportUserFacingError(string operationName, string diagnosticsMessage, Exception exception, MessageBoxImage messageBoxImage)
  {
    if (isShuttingDown) return;
    UserFacingDiagnosticError userFacingError = DiagnosticErrorClassifier.Describe(operationName, diagnosticsMessage, exception);
    diagnostics?.Error(diagnosticsMessage, exception);

    string dialogBody = string.Concat(
      userFacingError.Message,
      "\n\nNext steps:\n",
      string.Join("\n", userFacingError.NextSteps));

    _ = MessageBox.Show(
      dialogBody,
      userFacingError.Title,
      MessageBoxButton.OK,
      messageBoxImage);
  }
}
