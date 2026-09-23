using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DictateAnywhere.App.Presentation;

public partial class WindowCaptionButtons : UserControl
{
  public static readonly DependencyProperty ContextTitleProperty = DependencyProperty.Register(
    nameof(ContextTitle),
    typeof(string),
    typeof(WindowCaptionButtons),
    new PropertyMetadata(string.Empty, OnContextTitleChanged));

  private Window? hostWindow;

  public WindowCaptionButtons()
  {
    InitializeComponent();
    Loaded += OnLoaded;
    Unloaded += OnUnloaded;
    UpdateAccessibleNames();
  }

  public string ContextTitle
  {
    get => (string)GetValue(ContextTitleProperty);
    set => SetValue(ContextTitleProperty, value);
  }

  private static void OnContextTitleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
  {
    ((WindowCaptionButtons)dependencyObject).UpdateAccessibleNames();
  }

  private void OnLoaded(object sender, RoutedEventArgs args)
  {
    Window window = Window.GetWindow(this)
      ?? throw new InvalidOperationException("WindowCaptionButtons must be hosted inside a Window.");
    if (ReferenceEquals(hostWindow, window))
    {
      return;
    }

    DetachHostWindow();
    hostWindow = window;
    hostWindow.StateChanged += OnHostWindowStateChanged;
    UpdateMaximizeState();
  }

  private void OnUnloaded(object sender, RoutedEventArgs args) => DetachHostWindow();

  private void OnHostWindowStateChanged(object? sender, EventArgs args) => UpdateMaximizeState();

  private void OnMinimizeClicked(object sender, RoutedEventArgs args) =>
    WindowShellOperations.Minimize(GetHostWindow());

  private void OnMaximizeRestoreClicked(object sender, RoutedEventArgs args) =>
    WindowShellOperations.ToggleMaximize(GetHostWindow());

  private void OnCloseClicked(object sender, RoutedEventArgs args) =>
    WindowShellOperations.Close(GetHostWindow());

  private Window GetHostWindow() => hostWindow
    ?? Window.GetWindow(this)
    ?? throw new InvalidOperationException("WindowCaptionButtons is not attached to a Window.");

  private void UpdateMaximizeState()
  {
    bool isMaximized = hostWindow?.WindowState == WindowState.Maximized;
    MaximizeRestoreGlyphTextBlock.Text = isMaximized ? "\uE923" : "\uE922";
    MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    AutomationProperties.SetName(
      MaximizeRestoreButton,
      BuildAccessibleName(isMaximized ? "Restore" : "Maximize"));
  }

  private void UpdateAccessibleNames()
  {
    AutomationProperties.SetName(MinimizeButton, BuildAccessibleName("Minimize"));
    AutomationProperties.SetName(CloseButton, BuildAccessibleName("Close"));
    UpdateMaximizeState();
  }

  private string BuildAccessibleName(string action)
  {
    string context = ContextTitle?.Trim() ?? string.Empty;
    return context.Length == 0 ? action : $"{action} {context}";
  }

  private void DetachHostWindow()
  {
    if (hostWindow is not null)
    {
      hostWindow.StateChanged -= OnHostWindowStateChanged;
      hostWindow = null;
    }
  }
}
