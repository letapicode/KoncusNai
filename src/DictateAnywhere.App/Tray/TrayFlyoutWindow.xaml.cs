using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DictateAnywhere.Core.Contracts;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DictateAnywhere.App.Tray;

public partial class TrayFlyoutWindow : Window
{
  private const uint SwpNoSize = 0x0001;
  private const uint SwpNoZOrder = 0x0004;
  private const uint SwpNoActivate = 0x0010;
  private bool opening;
  private bool closing;
  private bool startWithWindows;

  internal TrayFlyoutWindow()
  {
    InitializeComponent();
  }

  internal event EventHandler? OpenSettingsRequested;
  internal event EventHandler? OpenWorkbenchRequested;
  internal event EventHandler? OpenHistoryRequested;
  internal event EventHandler? RetryLastDictationRequested;
  internal event EventHandler<bool>? StartupToggleRequested;
  internal event EventHandler<TranscriptionModelSelection>? QuickModelSwitchRequested;
  internal event EventHandler? ExportDiagnosticsRequested;
  internal event EventHandler? QuitRequested;

  internal void SetPresentation(TrayMenuPresentation presentation)
  {
    ArgumentNullException.ThrowIfNull(presentation);
    SessionStatusText.Text = presentation.SessionStatus;
    SpeechModelStatusText.Text = presentation.SpeechModelStatus;
    SetupModelButton.Content = presentation.SpeechModelActionText;
    System.Windows.Automation.AutomationProperties.SetName(SetupModelButton, presentation.SpeechModelActionText);
    SetupModelButton.Visibility = presentation.NeedsSpeechModelSetup ? Visibility.Visible : Visibility.Collapsed;
    ModelSwitcherButton.IsEnabled = presentation.InstalledModels.Count > 0;
    ModelSwitcherButton.Visibility = presentation.InstalledModels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    if (presentation.InstalledModels.Count == 0) ModelListPanel.Visibility = Visibility.Collapsed;
    ModelListPanel.Children.Clear();

    foreach (ModelInfo model in presentation.InstalledModels.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
    {
      TranscriptionModelSelection selection = new(model.ProviderId, model.ModelId);
      bool selected = TrayMenuPresentation.IsSelection(model.ProviderId, model.ModelId, presentation.ActiveSelection);
      Button button = new()
      {
        Style = (Style)FindResource("TrayActionStyle"),
        Content = $"{(selected ? "✓  " : "     ")}{model.DisplayName}",
        ToolTip = $"{model.ProviderId}/{model.ModelId}",
      };
      System.Windows.Automation.AutomationProperties.SetName(button,
        $"{model.DisplayName}{(selected ? ", selected" : string.Empty)}");
      button.Click += (_, _) => RunAndClose(() => QuickModelSwitchRequested?.Invoke(this, selection));
      ModelListPanel.Children.Add(button);
    }
  }

  internal void SetStartWithWindows(bool enabled)
  {
    startWithWindows = enabled;
    StartupCheckmark.Visibility = enabled ? Visibility.Visible : Visibility.Hidden;
    System.Windows.Automation.AutomationProperties.SetHelpText(StartWithWindowsButton,
      enabled ? "On" : "Off");
  }

  internal void ShowAt(Drawing.Point cursor)
  {
    opening = true;
    Opacity = 0;
    Show();
    UpdateLayout();

    HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
    if (source?.CompositionTarget is not null)
    {
      System.Windows.Media.Matrix transform = source.CompositionTarget.TransformToDevice;
      Drawing.Rectangle workArea = Forms.Screen.FromPoint(cursor).WorkingArea;
      MaxHeight = Math.Min(620, Math.Max(80, workArea.Height / transform.M22 - 16));
      UpdateLayout();
      Drawing.Size size = new(
        (int)Math.Ceiling(ActualWidth * transform.M11),
        (int)Math.Ceiling(ActualHeight * transform.M22));
      Drawing.Point location = TrayFlyoutPlacement.Compute(cursor, workArea, size);
      _ = SetWindowPos(source.Handle, IntPtr.Zero, location.X, location.Y, 0, 0,
        SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    Opacity = 1;
    opening = false;
    _ = Activate();
    _ = Keyboard.Focus(SetupModelButton.IsVisible ? SetupModelButton : OpenWorkbenchButton);
  }

  internal void Dismiss()
  {
    if (closing) return;
    closing = true;
    Close();
  }

  private void RunAndClose(Action action)
  {
    if (closing) return;
    Dismiss();
    action();
  }

  private void OnDeactivated(object sender, EventArgs e)
  {
    if (!opening && !closing)
    {
      Dismiss();
    }
  }

  private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) => closing = true;

  private void OnPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      e.Handled = true;
      Dismiss();
    }
  }

  private void OnSetupModelClicked(object sender, RoutedEventArgs e) =>
    RunAndClose(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
  private void OnOpenWorkbenchClicked(object sender, RoutedEventArgs e) =>
    RunAndClose(() => OpenWorkbenchRequested?.Invoke(this, EventArgs.Empty));
  private void OnOpenHistoryClicked(object sender, RoutedEventArgs e) =>
    RunAndClose(() => OpenHistoryRequested?.Invoke(this, EventArgs.Empty));
  private void OnModelSwitcherClicked(object sender, RoutedEventArgs e)
  {
    bool expand = ModelListPanel.Visibility != Visibility.Visible;
    ModelListPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
    ModelSwitcherChevron.Text = expand ? "▴" : "▾";
    UpdateLayout();
    HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
    if (source?.CompositionTarget is null) return;
    System.Windows.Media.Matrix transform = source.CompositionTarget.TransformToDevice;
    Drawing.Size size = new((int)Math.Ceiling(ActualWidth * transform.M11), (int)Math.Ceiling(ActualHeight * transform.M22));
    if (!GetWindowRect(source.Handle, out NativeRect rect)) return;
    Drawing.Point current = new(rect.Left, rect.Top);
    Drawing.Point center = new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
    Drawing.Rectangle work = Forms.Screen.FromPoint(center).WorkingArea;
    Drawing.Point adjusted = TrayFlyoutPlacement.Clamp(current, work, size);
    _ = SetWindowPos(source.Handle, IntPtr.Zero, adjusted.X, adjusted.Y, 0, 0,
      SwpNoSize | SwpNoZOrder | SwpNoActivate);
  }
  private void OnRetryLastDictationClicked(object sender, RoutedEventArgs e) =>
    RunAndClose(() => RetryLastDictationRequested?.Invoke(this, EventArgs.Empty));
  private void OnOpenSettingsClicked(object sender, RoutedEventArgs e) =>
    RunAndClose(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
  private void OnStartWithWindowsClicked(object sender, RoutedEventArgs e)
  {
    bool requested = !startWithWindows;
    SetStartWithWindows(requested);
    RunAndClose(() => StartupToggleRequested?.Invoke(this, requested));
  }
  private void OnExportDiagnosticsClicked(object sender, RoutedEventArgs e) =>
    RunAndClose(() => ExportDiagnosticsRequested?.Invoke(this, EventArgs.Empty));
  private void OnQuitClicked(object sender, RoutedEventArgs e) =>
    RunAndClose(() => QuitRequested?.Invoke(this, EventArgs.Empty));

  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y,
    int cx, int cy, uint flags);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

  [StructLayout(LayoutKind.Sequential)]
  private struct NativeRect
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
    public int Width => Right - Left;
    public int Height => Bottom - Top;
  }
}
