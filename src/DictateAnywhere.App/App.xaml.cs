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

  protected override async void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
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
      Shutdown(0);
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
      () => Shutdown(),
      (operation, message, exception) => ReportUserFacingError(operation, message, exception, MessageBoxImage.Warning));
    trayCommandCoordinator = composition.CreateTrayCommandCoordinator(
      trayHandlers,
      () => applicationHost?.CurrentState ?? DictationSessionState.Idle);

    try
    {
      AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
      AppThemeManager.ApplyThemeResources(settings.ThemePreference);
      _ = OllamaStartupShortcutPolicy.TryDisableAutomaticStartup();
      if (!AppLegalAcceptancePolicy.HasCurrentAcceptance(settings))
      {
        LegalAcknowledgementWindow legalAcknowledgement = new();
        if (legalAcknowledgement.ShowDialog() != true)
        {
          Shutdown();
          return;
        }

        settings = AppLegalAcceptancePolicy.AcceptCurrentVersion(settings, DateTimeOffset.UtcNow);
        await settingsStore.SaveAsync(settings).ConfigureAwait(true);
      }

      if (FirstRunWizardGuard.ShouldShowWizard(settings))
      {
        FirstRunWizardWindow wizard = composition.CreateFirstRunWizard();

        bool? wizardResult = wizard.ShowDialog();
        if (wizardResult != true)
        {
          Shutdown();
          return;
        }

        settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
        AppThemeManager.ApplyThemeResources(settings.ThemePreference);
      }

      trayCommandCoordinator.Start(
        applicationHost.CurrentReadiness,
        startupRegistrationService.IsEnabled());
      await RefreshTrayModelsAsync().ConfigureAwait(true);
      await RefreshModelReadinessAsync(settings).ConfigureAwait(true);
      await RestartProductivityHotkeysAsync(settings).ConfigureAwait(true);
      bool runtimeStarted = await StartSelectedExperienceAsync(settings, showWorkbench: !AppLaunchOptions.IsBackgroundLaunch(e.Args)).ConfigureAwait(true);
      ShowPendingRuntimeNotice();

      if (!runtimeStarted)
      {
        trayCommandCoordinator?.SetStatus(DictationSessionState.Error);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ModelManagementException)
    {
      ReportUserFacingError("Application startup", "Application startup failed.", ex, MessageBoxImage.Error);
      Shutdown(-1);
    }
  }

  protected override async void OnExit(ExitEventArgs e)
  {
    activationTimer?.Stop();
    if (activationTimer is not null) activationTimer.Tick -= OnActivationRequested;
    activationSignal?.Dispose();
    activationSignal = null;
    Activated -= OnApplicationActivated;
    textScaleSubscription?.Dispose();
    textScaleSubscription = null;

    if (runtimeWatchdogTimer is not null)
    {
      runtimeWatchdogTimer.Stop();
      runtimeWatchdogTimer.Tick -= OnRuntimeWatchdogTimerTick;
      runtimeWatchdogTimer = null;
    }

    if (trayCommandCoordinator is not null)
    {
      await trayCommandCoordinator.DisposeAsync().ConfigureAwait(true);
      trayCommandCoordinator = null;
    }

    if (windowCoordinator is not null)
    {
      windowCoordinator.SettingsRequested -= OnWorkbenchOpenSettingsRequested;
      windowCoordinator.SettingsSaved -= OnSettingsSaved;
      windowCoordinator.HistoryRequested -= OnSettingsManageHistoryRequested;
      windowCoordinator.ThemePreferenceRequested -= OnWorkbenchThemePreferenceRequested;
      windowCoordinator.TranscriptionModelSelectionRequested -= OnWorkbenchTranscriptionModelSelectionRequested;
      windowCoordinator.ChatOutputFontSizeRequested -= OnWorkbenchChatOutputFontSizeRequested;
      windowCoordinator.WorkbenchZoomRequested -= OnWorkbenchZoomRequested;
      await windowCoordinator.DisposeAsync().ConfigureAwait(true);
      windowCoordinator = null;
    }

    if (applicationHost is not null)
    {
      applicationHost.ModelReadinessChanged -= OnModelReadinessSnapshotChanged;
      await applicationHost.DisposeAsync().ConfigureAwait(true);
      applicationHost = null;
    }

    if (productivityHotkeyCoordinator is not null)
    {
      await productivityHotkeyCoordinator.DisposeAsync().ConfigureAwait(true);
      productivityHotkeyCoordinator = null;
    }

    await OllamaProcessOwnership.StopOwnedAsync().ConfigureAwait(true);

    diagnostics?.Dispose();
    diagnostics = null;

    instanceGuard?.Dispose();
    instanceGuard = null;

    watchdogTickLock.Dispose();
    base.OnExit(e);
  }

  private void OnApplicationActivated(object? sender, EventArgs e)
  {
    AppThemeManager.ApplyThemeResources(AppThemeManager.CurrentPreference);
  }

  private async void OnActivationRequested(object? sender, EventArgs e)
  {
    if (activationSignal?.WaitOne(0) == true)
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

      AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
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
    if (applicationHost is null)
    {
      throw new InvalidOperationException("Application host is not initialized.");
    }

    RuntimeSettingsApplyResult result = await applicationHost
      .ApplyRuntimeSettingsAsync(settings)
      .ConfigureAwait(true);
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

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
    AppSettings updated = settings with
    {
      ThemePreference = preference,
    };
    await settingsStore.SaveAsync(updated).ConfigureAwait(true);
    AppThemeManager.ApplyThemeResources(preference);
  }

  private async Task ApplyWorkbenchChatOutputFontSizeAsync(int fontSize)
  {
    if (settingsStore is null)
    {
      throw new InvalidOperationException("Settings store is not initialized.");
    }

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
    AppSettings updated = settings with
    {
      ChatOutputFontSize = ChatTextSizePolicy.Normalize(fontSize),
    };
    await settingsStore.SaveAsync(updated).ConfigureAwait(true);
  }

  private async Task ApplyWorkbenchZoomAsync(int percent)
  {
    if (settingsStore is null)
    {
      throw new InvalidOperationException("Settings store is not initialized.");
    }

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
    AppSettings updated = settings with
    {
      WorkbenchZoomPercent = Math.Clamp(percent, 80, 150),
    };
    await settingsStore.SaveAsync(updated).ConfigureAwait(true);
  }

  private async Task OpenSettingsAsync()
  {
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

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
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

    await EnsureWorkbenchAsync(settings, registerWorkbenchHotkey: !runtimeStarted).ConfigureAwait(true);
    await RefreshTrayModelsAsync().ConfigureAwait(true);
    await RestartProductivityHotkeysAsync(settings).ConfigureAwait(true);
  }

  private async Task OpenHistoryAsync()
  {
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

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
    if (composition is null)
    {
      throw new InvalidOperationException("Application composition is not initialized.");
    }

    ProductivityActionResult result = await composition
      .RetryLastDictationAsync(settings)
      .ConfigureAwait(true);
    ShowProductivityActionResult(result);
  }

  private static void ShowProductivityActionResult(ProductivityActionResult result)
  {
    if (result.Success)
    {
      return;
    }

    _ = MessageBox.Show(
      result.Message,
      "Koncus Nai",
      MessageBoxButton.OK,
      MessageBoxImage.Information);
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

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
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

    await modelManager.SetActiveModelAsync(normalizedSelection).ConfigureAwait(true);

    AppSettings updated = settings.WithConfiguredTranscription(
      normalizedSelection.ProviderId,
      normalizedSelection.ModelId);
    await settingsStore.SaveAsync(updated).ConfigureAwait(true);

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
    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
    trayCommandCoordinator.SetModelMenu(models, settings.GetConfiguredTranscriptionSelection());
  }

  private async Task RefreshModelReadinessAsync(AppSettings settings)
  {
    if (applicationHost is null || trayCommandCoordinator is null)
    {
      return;
    }

    await applicationHost.RefreshReadinessAsync(settings).ConfigureAwait(true);
    trayCommandCoordinator.SetModelReadiness(applicationHost.CurrentReadiness);
  }

  private Task RestartProductivityHotkeysAsync(AppSettings settings)
  {
    if (productivityHotkeyCoordinator is null)
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
    string bundlePath = diagnostics.ExportBundle(destinationDirectory);

    _ = MessageBox.Show(
      $"Diagnostics bundle exported to:\n{bundlePath}\n\nAttach this file when reporting an issue.",
      "Koncus Nai",
      MessageBoxButton.OK,
      MessageBoxImage.Information);
    return Task.CompletedTask;
  }

  private void OnModelReadinessSnapshotChanged(object? sender, ModelReadinessSnapshot snapshot)
  {
    _ = Dispatcher.BeginInvoke(new Action(() =>
    {
      trayCommandCoordinator?.SetModelReadiness(snapshot);
    }));
  }

  private async void OnRuntimeWatchdogTimerTick(object? sender, EventArgs e)
  {
    if (runtimeWatchdog is null)
    {
      return;
    }

    if (!await watchdogTickLock.WaitAsync(0).ConfigureAwait(true))
    {
      return;
    }

    try
    {
      await runtimeWatchdog.TickAsync().ConfigureAwait(true);
      await TryApplyPendingRuntimeSettingsAsync().ConfigureAwait(true);
      trayCommandCoordinator?.SetStatus(applicationHost?.CurrentState ?? DictationSessionState.Idle);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      diagnostics?.Warning($"Runtime watchdog tick failed: {ex.Message}");
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
    if (trayCommandCoordinator is null || pendingRuntimeNotice is null)
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
