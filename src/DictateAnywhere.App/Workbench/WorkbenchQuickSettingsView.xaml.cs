using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

/// <summary>Renders compact Workbench settings and forwards user intent.</summary>
public partial class WorkbenchQuickSettingsView : UserControl
{
  private const double SidebarInset = 10d;
  private const double MinimumSurfaceWidth = 240d;
  private const double ButtonGap = 8d;
  private readonly DispatcherTimer textSizeCommitTimer;
  private DispatcherOperation? pendingInitialFocus;
  private bool isApplyingTextSize;
  private bool isApplyingModelSelections;
  private bool presentationDisposed;
  private int lastCommittedTextSize;

  public WorkbenchQuickSettingsView()
  {
    InitializeComponent();
    Overlay.MouseDown += (_, args) => OverlayMouseDown?.Invoke(this, args);
    Surface.MouseDown += (_, args) => SurfaceMouseDown?.Invoke(this, args);
    AdvancedButton.Click += (_, args) => OpenAdvancedClicked?.Invoke(this, args);
    ThemeButton.Click += (_, args) => ToggleThemeClicked?.Invoke(this, args);
    lastCommittedTextSize = (int)TextSizeSlider.Value;
    textSizeCommitTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
    {
      Interval = TimeSpan.FromMilliseconds(350),
    };
    textSizeCommitTimer.Tick += OnTextSizeCommitTimerTick;
    TextSizeSlider.ValueChanged += OnTextSizeChanged;
    TextSizeSlider.LostMouseCapture += OnTextSizeInteractionCompleted;
    TextSizeSlider.PreviewKeyUp += OnTextSizeKeyUp;
    ChatModel.SelectionChanged += (_, args) =>
    {
      if (!isApplyingModelSelections)
      {
        ChatModelSelectionChanged?.Invoke(this, args);
      }
    };
    DownloadChatModel.Click += (_, args) => DownloadChatModelClicked?.Invoke(this, args);
    TranscriptionModel.SelectionChanged += (_, args) =>
    {
      if (!isApplyingModelSelections)
      {
        TranscriptionModelSelectionChanged?.Invoke(this, args);
      }
    };
    AboutButton.Click += (_, args) => AboutClicked?.Invoke(this, args);
  }

  internal event Action<int>? ZoomRequested;
  internal void SetZoom(int percent) => ZoomValue.Content = $"{percent}%";
  private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomRequested?.Invoke(-10);
  private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomRequested?.Invoke(10);
  private void OnZoomReset(object sender, RoutedEventArgs e) => ZoomRequested?.Invoke(0);

  public event MouseButtonEventHandler? OverlayMouseDown;
  public event MouseButtonEventHandler? SurfaceMouseDown;
  public event RoutedEventHandler? OpenAdvancedClicked;
  public event RoutedEventHandler? ToggleThemeClicked;
  public event Action<int>? ChatTextSizePreviewRequested;
  public event Action<int>? ChatTextSizeCommitRequested;
  public event SelectionChangedEventHandler? ChatModelSelectionChanged;
  public event RoutedEventHandler? DownloadChatModelClicked;
  public event SelectionChangedEventHandler? TranscriptionModelSelectionChanged;
  public event RoutedEventHandler? AboutClicked;
  internal event EventHandler? DismissRequested;

  internal bool IsOpen => Visibility == Visibility.Visible;

  internal void Show()
  {
    Overlay.Visibility = Visibility.Visible;
    Visibility = Visibility.Visible;
    pendingInitialFocus?.Abort();
    pendingInitialFocus = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
    {
      pendingInitialFocus = null;
      if (!presentationDisposed && IsOpen)
      {
        _ = AdvancedButton.Focus();
      }
    }));
  }

  internal void Hide()
  {
    pendingInitialFocus?.Abort();
    pendingInitialFocus = null;
    if (IsKeyboardFocusWithin)
    {
      Keyboard.ClearFocus();
    }
    Visibility = Visibility.Collapsed;
    Overlay.Visibility = Visibility.Collapsed;
  }

  private void OnPreviewKeyDown(object sender, KeyEventArgs args)
  {
    if (args.Key != Key.Escape)
    {
      return;
    }

    args.Handled = true;
    DismissRequested?.Invoke(this, EventArgs.Empty);
  }

  internal void SetThemeState(bool isDark)
  {
    ThemeGlyph.Text = isDark ? "\uE708" : "\uE706";
    ThemeMode.Text = isDark ? "Dark Mode" : "Light Mode";
    ThemeMode.ToolTip = isDark ? "Switch to light mode" : "Switch to dark mode";
  }

  internal void SetTranscriptionStatus(string status) =>
    TranscriptionStatus.Text = status ?? string.Empty;

  internal ChatModelOptionViewModel? SelectedChatModel =>
    ChatModel.SelectedItem as ChatModelOptionViewModel;

  internal TranscriptionModelOptionViewModel? SelectedTranscriptionModel =>
    TranscriptionModel.SelectedItem as TranscriptionModelOptionViewModel;

  internal bool HasTranscriptionModels => TranscriptionModel.ItemsSource is not null;

  internal void SetChatModels(IReadOnlyList<ChatModelOptionViewModel> models)
  {
    ArgumentNullException.ThrowIfNull(models);
    ApplyModelSelection(() => ChatModel.ItemsSource = models);
  }

  internal ChatModelOptionViewModel? SelectChatModel(ChatModelSelection selection)
  {
    ChatModelSelection normalized = selection.Normalize();
    IEnumerable<ChatModelOptionViewModel> options = ChatModel.ItemsSource as IEnumerable<ChatModelOptionViewModel>
      ?? Array.Empty<ChatModelOptionViewModel>();
    ChatModelOptionViewModel? selected = options.FirstOrDefault(candidate =>
      string.Equals(candidate.Selection.ProviderId, normalized.ProviderId, StringComparison.OrdinalIgnoreCase)
      && string.Equals(candidate.Selection.ModelId, normalized.ModelId, StringComparison.OrdinalIgnoreCase))
      ?? options.FirstOrDefault();
    ApplyModelSelection(() => ChatModel.SelectedItem = selected);
    return selected;
  }

  internal void SetTranscriptionModels(WorkbenchTranscriptionModelState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    ApplyModelSelection(() =>
    {
      TranscriptionModel.ItemsSource = state.Options;
      TranscriptionModel.SelectedItem = state.Selected;
    });
    TranscriptionModel.IsEnabled = state.HasInstalledModel;
    SetTranscriptionStatus(state.Status);
  }

  internal void SetTextSize(int size)
  {
    isApplyingTextSize = true;
    try
    {
      TextSizeSlider.Value = size;
      TextSizeValue.Text = $"{size} px";
      lastCommittedTextSize = size;
      textSizeCommitTimer.Stop();
    }
    finally
    {
      isApplyingTextSize = false;
    }
  }

  internal void SetTextSizeLabel(int size) => TextSizeValue.Text = $"{size} px";

  internal void SetChatModelState(
    bool canInteract,
    bool isInstalled,
    bool isRuntimeReady,
    bool usesExternalRuntime)
  {
    ChatModel.IsEnabled = canInteract;
    DownloadChatModel.Content = usesExternalRuntime
      ? "Start / set up Ollama"
      : isInstalled && !isRuntimeReady
        ? "Update Runtime"
        : "Download Model";
    bool needsSetup = !isInstalled || !isRuntimeReady;
    DownloadChatModel.IsEnabled = canInteract && needsSetup;
    DownloadChatModel.Visibility = canInteract && needsSetup
      ? Visibility.Visible
      : Visibility.Collapsed;
  }

  internal void Render(WorkbenchQuickSettingsPresentation state)
  {
    ArgumentNullException.ThrowIfNull(state);
    SetChatModelState(
      state.CanInteract,
      state.IsChatModelInstalled,
      state.IsChatRuntimeReady,
      state.UsesExternalRuntime);
  }

  internal void DisposePresentation()
  {
    presentationDisposed = true;
    pendingInitialFocus?.Abort();
    pendingInitialFocus = null;
    textSizeCommitTimer.Stop();
    textSizeCommitTimer.Tick -= OnTextSizeCommitTimerTick;
  }

  internal bool IsTextSizeCommitPending => textSizeCommitTimer.IsEnabled;

  internal void UpdatePlacement(
    FrameworkElement root,
    FrameworkElement sidebar,
    FrameworkElement settingsButton,
    double fallbackSidebarWidth)
  {
    if (!IsOpen || root.ActualWidth <= 0d || root.ActualHeight <= 0d)
    {
      return;
    }

    Point sidebarPosition = sidebar.TranslatePoint(new Point(0d, 0d), root);
    Point buttonPosition = settingsButton.TranslatePoint(new Point(0d, 0d), root);
    double sidebarWidth = sidebar.ActualWidth > 0d ? sidebar.ActualWidth : fallbackSidebarWidth;
    double surfaceWidth = System.Math.Max(MinimumSurfaceWidth, sidebarWidth - (SidebarInset * 2d));
    double left = System.Math.Max(0d, sidebarPosition.X + SidebarInset);
    double bottom = System.Math.Max(ButtonGap, root.ActualHeight - buttonPosition.Y + ButtonGap);
    Surface.MaxHeight = System.Math.Max(1d, root.ActualHeight - bottom - SidebarInset);
    Surface.Width = surfaceWidth;
    Surface.Margin = new Thickness(left, 0d, 0d, bottom);
  }

  private void OnTextSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
  {
    if (presentationDisposed || isApplyingTextSize)
    {
      return;
    }

    int size = ChatTextSizePolicy.Normalize((int)Math.Round(args.NewValue, MidpointRounding.AwayFromZero));
    SetTextSizeLabel(size);
    ChatTextSizePreviewRequested?.Invoke(size);
    textSizeCommitTimer.Stop();
    textSizeCommitTimer.Start();
  }

  private void OnTextSizeInteractionCompleted(object sender, MouseEventArgs args) => CommitPendingTextSize();

  private void OnTextSizeKeyUp(object sender, KeyEventArgs args) => CommitPendingTextSize();

  private void OnTextSizeCommitTimerTick(object? sender, EventArgs args) => CommitPendingTextSize();

  internal void CommitPendingTextSize()
  {
    textSizeCommitTimer.Stop();
    if (presentationDisposed)
    {
      return;
    }
    int size = ChatTextSizePolicy.Normalize((int)Math.Round(TextSizeSlider.Value, MidpointRounding.AwayFromZero));
    if (size == lastCommittedTextSize)
    {
      return;
    }

    lastCommittedTextSize = size;
    ChatTextSizeCommitRequested?.Invoke(size);
  }

  private void ApplyModelSelection(Action action)
  {
    isApplyingModelSelections = true;
    try
    {
      action();
    }
    finally
    {
      isApplyingModelSelections = false;
    }
  }

}
