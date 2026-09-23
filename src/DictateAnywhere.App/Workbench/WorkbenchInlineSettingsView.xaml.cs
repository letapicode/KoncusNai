using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DictateAnywhere.App.Settings;

namespace DictateAnywhere.App.Workbench;

/// <summary>Owns inline Settings hosting, loading presentation, and panel event lifetime.</summary>
public partial class WorkbenchInlineSettingsView : UserControl
{
  private SettingsPanel? panel;
  private DispatcherOperation? pendingInitialFocus;

  public WorkbenchInlineSettingsView() => InitializeComponent();

  public event EventHandler? SettingsSaved;
  internal event EventHandler? BackRequested;

  internal bool IsOpen => Visibility == Visibility.Visible;

  internal void ShowPanel(SettingsPanel settingsPanel)
  {
    ArgumentNullException.ThrowIfNull(settingsPanel);
    Hide();
    panel = settingsPanel;
    panel.BackRequested += OnBackRequested;
    panel.SettingsSaved += OnSettingsSaved;
    PanelHost.Content = panel;
    LoadingState.Visibility = Visibility.Collapsed;
    Visibility = Visibility.Visible;
    QueueInitialFocus(() => ReferenceEquals(panel, settingsPanel) && settingsPanel.FocusInitialControl());
  }

  internal void ShowLoading()
  {
    Hide();
    LoadingState.Visibility = Visibility.Visible;
    Visibility = Visibility.Visible;
    QueueInitialFocus(() => LoadingState.Focus());
  }

  internal void Hide()
  {
    pendingInitialFocus?.Abort();
    pendingInitialFocus = null;
    if (IsKeyboardFocusWithin)
    {
      Keyboard.ClearFocus();
    }
    if (panel is not null)
    {
      panel.BackRequested -= OnBackRequested;
      panel.SettingsSaved -= OnSettingsSaved;
      panel = null;
    }

    PanelHost.Content = null;
    LoadingState.Visibility = Visibility.Collapsed;
    Visibility = Visibility.Collapsed;
  }

  private void QueueInitialFocus(Func<bool> focusAction)
  {
    pendingInitialFocus?.Abort();
    pendingInitialFocus = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
    {
      pendingInitialFocus = null;
      if (IsOpen)
      {
        _ = focusAction();
      }
    }));
  }

  private void OnBackRequested(object? sender, EventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

  private void OnSettingsSaved(object? sender, EventArgs e) => SettingsSaved?.Invoke(this, EventArgs.Empty);
}
