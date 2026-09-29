using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using Forms = System.Windows.Forms;

namespace DictateAnywhere.App.Tray;

public sealed class TrayIconHost : ITrayIconHost
{
  private readonly Forms.NotifyIcon notifyIcon;
  private readonly Icon notifyIconAsset;
  private readonly bool ownsNotifyIconAsset;
  private TrayFlyoutWindow? flyout;
  private DictationSessionState sessionState = DictationSessionState.Idle;
  private ModelReadinessSnapshot readiness = ModelReadinessSnapshot.Empty;
  private IReadOnlyList<ModelInfo> models = Array.Empty<ModelInfo>();
  private TranscriptionModelSelection activeSelection = new(string.Empty, string.Empty);
  private bool modelCatalogLoaded;
  private bool startOnLoginEnabled;
  private bool shuttingDown;
  private bool disposed;

  public TrayIconHost()
  {
    (notifyIconAsset, ownsNotifyIconAsset) = LoadApplicationIcon();
    notifyIcon = new Forms.NotifyIcon
    {
      Icon = notifyIconAsset,
      Text = AppBrand.Name,
      Visible = true,
    };
    notifyIcon.MouseUp += OnNotifyIconMouseUp;
    notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
  }

  public event EventHandler? OpenSettingsRequested;
  public event EventHandler? OpenWorkbenchRequested;
  public event EventHandler? OpenHistoryRequested;
  public event EventHandler? QuitRequested;
  public event EventHandler<bool>? StartupToggleRequested;
  public event EventHandler? RetryLastDictationRequested;
  public event EventHandler<TranscriptionModelSelection>? QuickModelSwitchRequested;
  public event EventHandler? ExportDiagnosticsRequested;

  public void SetStatus(DictationSessionState state)
  {
    if (sessionState == state) return;
    sessionState = state;
    RefreshFlyout();
  }

  public void SetModelReadiness(ModelReadinessSnapshot snapshot)
  {
    readiness = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    RefreshFlyout();
  }

  public void SetStartOnLoginEnabled(bool enabled)
  {
    startOnLoginEnabled = enabled;
    flyout?.SetStartWithWindows(enabled);
  }

  public void SetModelMenu(IReadOnlyList<ModelInfo> availableModels, TranscriptionModelSelection selection)
  {
    ArgumentNullException.ThrowIfNull(availableModels);
    activeSelection = selection ?? throw new ArgumentNullException(nameof(selection));
    models = availableModels.ToArray();
    modelCatalogLoaded = true;
    RefreshFlyout();
  }

  public void ShowNotification(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info, int timeoutMilliseconds = 5000)
  {
    if (shuttingDown || disposed || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message)) return;
    notifyIcon.BalloonTipTitle = title;
    notifyIcon.BalloonTipText = message;
    notifyIcon.BalloonTipIcon = icon;
    notifyIcon.ShowBalloonTip(timeoutMilliseconds);
  }

  public void Dispose()
  {
    if (disposed) return;
    BeginShutdown();
    disposed = true;
    notifyIcon.Dispose();
    if (ownsNotifyIconAsset) notifyIconAsset.Dispose();
  }

  public void BeginShutdown()
  {
    if (shuttingDown || disposed) return;
    shuttingDown = true;
    notifyIcon.MouseUp -= OnNotifyIconMouseUp;
    notifyIcon.DoubleClick -= OnNotifyIconDoubleClick;
    notifyIcon.Visible = false;
    flyout?.Dismiss();
  }

  internal bool HasOpenFlyout => flyout is not null;

  internal void OnNotifyIconMouseUp(object? sender, Forms.MouseEventArgs e)
  {
    if (shuttingDown || disposed || e.Button != Forms.MouseButtons.Right) return;
    Application? app = Application.Current;
    if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished) return;
    if (flyout is not null)
    {
      flyout.Dismiss();
      return;
    }

    Window? mainWindow = app.MainWindow;
    TrayFlyoutWindow window = new();
    if (ReferenceEquals(app.MainWindow, window))
    {
      app.MainWindow = mainWindow;
    }
    flyout = window;
    window.Closed += (_, _) =>
    {
      if (ReferenceEquals(flyout, window)) flyout = null;
    };
    window.OpenSettingsRequested += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    window.OpenWorkbenchRequested += (_, _) => OpenWorkbenchRequested?.Invoke(this, EventArgs.Empty);
    window.OpenHistoryRequested += (_, _) => OpenHistoryRequested?.Invoke(this, EventArgs.Empty);
    window.RetryLastDictationRequested += (_, _) => RetryLastDictationRequested?.Invoke(this, EventArgs.Empty);
    window.StartupToggleRequested += (_, enabled) => StartupToggleRequested?.Invoke(this, enabled);
    window.QuickModelSwitchRequested += (_, selection) => QuickModelSwitchRequested?.Invoke(this, selection);
    window.ExportDiagnosticsRequested += (_, _) => ExportDiagnosticsRequested?.Invoke(this, EventArgs.Empty);
    window.QuitRequested += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
    window.SetPresentation(CreatePresentation());
    window.SetStartWithWindows(startOnLoginEnabled);
    window.ShowAt(Forms.Cursor.Position);
  }

  private void OnNotifyIconDoubleClick(object? sender, EventArgs e)
  {
    Application? app = Application.Current;
    if (shuttingDown || disposed || app is null
        || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished) return;
    OpenWorkbenchRequested?.Invoke(this, EventArgs.Empty);
  }

  private TrayMenuPresentation CreatePresentation() =>
    TrayMenuPresentation.Create(sessionState, readiness, models, activeSelection, modelCatalogLoaded);

  private void RefreshFlyout() => flyout?.SetPresentation(CreatePresentation());

  internal static (Icon Icon, bool OwnsIcon) LoadApplicationIcon()
  {
    using System.IO.Stream stream = typeof(TrayIconHost).Assembly.GetManifestResourceStream("KoncusNai.TrayIcon")
      ?? throw new InvalidOperationException("The Koncus Nai tray icon resource is missing.");
    using Icon source = new(stream, Forms.SystemInformation.SmallIconSize);
    return ((Icon)source.Clone(), true);
  }
}
