using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Settings;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Lifecycle;

/// <summary>Owns first-party window instances, their event wiring, and per-window disposal.</summary>
internal sealed class WindowCoordinator : IAsyncDisposable
{
  private readonly ApplicationComposition composition;
  private readonly ISettingsStore settingsStore;
  private readonly IDiagnostics diagnostics;
  private readonly Dispatcher dispatcher;
  private readonly DictationHistoryChangeNotifier historyChangeNotifier;
  private readonly CoalescingRefreshSession historyRefreshSession;

  private TextboxWorkbenchWindow? workbenchWindow;
  private HistoryWindow? historyWindow;
  private SettingsPanel? settingsPanel;
  private bool disposed;

  public WindowCoordinator(
    ApplicationComposition composition,
    Dispatcher dispatcher,
    DictationHistoryChangeNotifier historyChangeNotifier)
  {
    this.composition = composition ?? throw new ArgumentNullException(nameof(composition));
    settingsStore = composition.SettingsStore;
    diagnostics = composition.Diagnostics;
    this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    this.historyChangeNotifier = historyChangeNotifier ?? throw new ArgumentNullException(nameof(historyChangeNotifier));
    historyRefreshSession = new CoalescingRefreshSession(RefreshOpenHistoryViewsAsync);
    this.historyChangeNotifier.RecordAdded += OnDictationHistoryRecordAdded;
  }

  public bool IsWorkbenchOpen => workbenchWindow is not null;

  public event Action? SettingsRequested;
  public event EventHandler? SettingsSaved;
  public event EventHandler? HistoryRequested;
  public event Action<AppThemePreference>? ThemePreferenceRequested;
  public event Action<TranscriptionModelSelection>? TranscriptionModelSelectionRequested;
  public event Action<int>? ChatOutputFontSizeRequested;
  public event Action<int>? WorkbenchZoomRequested;

  public async Task EnsureWorkbenchAsync(
    AppSettings settings,
    bool registerWorkbenchHotkey,
    Func<AppSettings, IDiagnostics, ITranscriptionService>? transcriptionServiceFactory)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(settings);

    if (workbenchWindow is null)
    {
      workbenchWindow = composition.CreateWorkbenchWindow(transcriptionServiceFactory);
      SubscribeToWorkbench(workbenchWindow);
    }

    await workbenchWindow.ApplySettingsAsync(settings, registerWorkbenchHotkey).ConfigureAwait(true);
    if (!workbenchWindow.IsVisible)
    {
      workbenchWindow.Show();
    }

    if (workbenchWindow.WindowState == System.Windows.WindowState.Minimized)
    {
      workbenchWindow.WindowState = System.Windows.WindowState.Normal;
    }
    _ = workbenchWindow.Activate();
  }

  public async Task OpenSettingsAsync(
    bool registerWorkbenchHotkey,
    Func<AppSettings, IDiagnostics, ITranscriptionService>? transcriptionServiceFactory)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (workbenchWindow is null)
    {
      AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
      await EnsureWorkbenchAsync(settings, registerWorkbenchHotkey, transcriptionServiceFactory).ConfigureAwait(true);
    }
    else
    {
      if (!workbenchWindow.IsVisible)
      {
        workbenchWindow.Show();
      }

      _ = workbenchWindow.Activate();
    }

    if (workbenchWindow is null)
    {
      return;
    }

    try
    {
      if (settingsPanel is null)
      {
        workbenchWindow.ShowInlineSettingsLoading();
        await Dispatcher.Yield(DispatcherPriority.Render);
      }

      workbenchWindow.ShowInlineSettings(GetOrCreateSettingsPanel());
    }
    catch
    {
      workbenchWindow.HideInlineSettings();
      throw;
    }
  }

  public async Task OpenHistoryAsync()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (historyWindow is not null)
    {
      if (!historyWindow.IsVisible)
      {
        historyWindow.Show();
      }

      _ = historyWindow.Activate();
      return;
    }

    AppSettings settings = CurrentSettingsPolicy.Normalize(await settingsStore.LoadAsync().ConfigureAwait(true));
    HistoryWindow window = composition.CreateHistoryWindow(settings);
    window.Closed += OnHistoryWindowClosed;
    historyWindow = window;
    window.Show();
    _ = window.Activate();
  }

  public Task ApplySettingsToOpenHistoryAsync(AppSettings settings)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(settings);
    return historyWindow is null
      ? Task.CompletedTask
      : historyWindow.ApplySettingsAsync(settings);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    historyChangeNotifier.RecordAdded -= OnDictationHistoryRecordAdded;
    await historyRefreshSession.DisposeAsync().ConfigureAwait(true);

    if (settingsPanel is not null)
    {
      SettingsPanel panel = settingsPanel;
      settingsPanel = null;
      panel.ManageHistoryRequested -= OnSettingsManageHistoryRequested;
      await panel.DisposeAsync().ConfigureAwait(true);
    }

    if (historyWindow is not null)
    {
      HistoryWindow window = historyWindow;
      historyWindow = null;
      window.Closed -= OnHistoryWindowClosed;
      window.Close();
      await window.DisposeAsync().ConfigureAwait(true);
    }

    await CloseWorkbenchAsync().ConfigureAwait(true);
  }

  private SettingsPanel GetOrCreateSettingsPanel()
  {
    if (settingsPanel is null)
    {
      settingsPanel = composition.CreateSettingsPanel();
      settingsPanel.ManageHistoryRequested += OnSettingsManageHistoryRequested;
    }

    return settingsPanel;
  }

  private void SubscribeToWorkbench(TextboxWorkbenchWindow window)
  {
    window.OpenSettingsRequested += OnWorkbenchSettingsRequested;
    window.SettingsSaved += OnWorkbenchSettingsSaved;
    window.ThemePreferenceRequested += OnWorkbenchThemePreferenceRequested;
    window.TranscriptionModelSelectionRequested += OnWorkbenchTranscriptionModelSelectionRequested;
    window.ChatOutputFontSizeRequested += OnWorkbenchChatOutputFontSizeRequested;
    window.WorkbenchZoomRequested += OnWorkbenchZoomRequested;
    window.Closed += OnWorkbenchWindowClosed;
  }

  private void UnsubscribeFromWorkbench(TextboxWorkbenchWindow window)
  {
    window.OpenSettingsRequested -= OnWorkbenchSettingsRequested;
    window.SettingsSaved -= OnWorkbenchSettingsSaved;
    window.ThemePreferenceRequested -= OnWorkbenchThemePreferenceRequested;
    window.TranscriptionModelSelectionRequested -= OnWorkbenchTranscriptionModelSelectionRequested;
    window.ChatOutputFontSizeRequested -= OnWorkbenchChatOutputFontSizeRequested;
    window.WorkbenchZoomRequested -= OnWorkbenchZoomRequested;
    window.Closed -= OnWorkbenchWindowClosed;
  }

  private async Task CloseWorkbenchAsync()
  {
    if (workbenchWindow is null)
    {
      return;
    }

    TextboxWorkbenchWindow window = workbenchWindow;
    workbenchWindow = null;
    UnsubscribeFromWorkbench(window);
    window.Close();
    await window.DisposeAsync().ConfigureAwait(true);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "WPF close events cannot return a Task; disposal failures are observed and logged.")]
  private async void OnWorkbenchWindowClosed(object? sender, EventArgs e)
  {
    if (sender is not TextboxWorkbenchWindow window)
    {
      return;
    }

    UnsubscribeFromWorkbench(window);
    if (ReferenceEquals(workbenchWindow, window))
    {
      workbenchWindow = null;
    }

    try
    {
      await window.DisposeAsync().ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      diagnostics.Error("Workbench window disposal failed.", ex);
    }
  }

  private void OnHistoryWindowClosed(object? sender, EventArgs e)
  {
    if (sender is not HistoryWindow window)
    {
      return;
    }

    window.Closed -= OnHistoryWindowClosed;
    if (ReferenceEquals(historyWindow, window))
    {
      historyWindow = null;
    }
  }

  private void OnWorkbenchSettingsRequested() => SettingsRequested?.Invoke();
  private void OnWorkbenchSettingsSaved(object? sender, EventArgs e) => SettingsSaved?.Invoke(sender, e);
  private void OnSettingsManageHistoryRequested(object? sender, EventArgs e) => HistoryRequested?.Invoke(this, EventArgs.Empty);
  private void OnWorkbenchThemePreferenceRequested(AppThemePreference preference) => ThemePreferenceRequested?.Invoke(preference);
  private void OnWorkbenchTranscriptionModelSelectionRequested(TranscriptionModelSelection selection) => TranscriptionModelSelectionRequested?.Invoke(selection);
  private void OnWorkbenchZoomRequested(int percent) => WorkbenchZoomRequested?.Invoke(percent);

  private void OnWorkbenchChatOutputFontSizeRequested(int fontSize) => ChatOutputFontSizeRequested?.Invoke(fontSize);

  private void OnDictationHistoryRecordAdded(object? sender, DictationHistoryRecord record)
  {
    _ = dispatcher.BeginInvoke(new Action(QueueOpenHistoryViewRefresh));
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The notifier event boundary must observe unexpected refresh failures.")]
  private async void QueueOpenHistoryViewRefresh()
  {
    if (!historyRefreshSession.TryRequestRefresh(out Task refreshRun))
    {
      return;
    }

    try
    {
      await refreshRun.ConfigureAwait(true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      diagnostics.Warning($"Open history view refresh failed: {ex.Message}");
    }
    catch (Exception ex)
    {
      diagnostics.Error("Open history view refresh failed unexpectedly.", ex);
    }
  }

  private async Task RefreshOpenHistoryViewsAsync(CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (workbenchWindow?.IsLoaded == true)
    {
      await workbenchWindow.RefreshPersistedDictationHistoryAsync(cancellationToken).ConfigureAwait(true);
    }

    cancellationToken.ThrowIfCancellationRequested();
    if (historyWindow?.IsLoaded == true)
    {
      await historyWindow.RefreshPersistedHistoryAsync(cancellationToken).ConfigureAwait(true);
    }
  }
}
