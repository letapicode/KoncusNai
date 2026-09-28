using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DictateAnywhere.App.Presentation;

namespace DictateAnywhere.App.Workbench;

/// <summary>Owns expanded prompt presentation and forwards user intent.</summary>
public partial class WorkbenchExpandedPromptView : UserControl
{
  private bool isApplyingText;

  public WorkbenchExpandedPromptView()
  {
    InitializeComponent();
    Collapse.Click += (_, args) => CollapseClicked?.Invoke(this, args);
    Submit.Click += (_, args) => SubmitClicked?.Invoke(this, args);
    Prompt.KeyDown += (_, args) => PromptKeyDown?.Invoke(this, args);
    Prompt.TextChanged += OnPromptTextChanged;
    Prompt.GotKeyboardFocus += (_, _) => UpdateFocusBorder();
    Prompt.LostKeyboardFocus += (_, _) => UpdateFocusBorder();
  }

  public event RoutedEventHandler? CollapseClicked;
  public event RoutedEventHandler? SubmitClicked;
  public event KeyEventHandler? PromptKeyDown;
  public event TextChangedEventHandler? PromptTextChanged;

  internal string Text => Prompt.Text;
  internal bool IsOpen => Visibility == Visibility.Visible;
  internal TextBox PromptElement => Prompt;
  internal WorkbenchModelNoticeView Notice => ModelNotice;
  internal void SetCanSubmit(bool enabled) => Submit.IsEnabled = enabled;

  internal void SetText(string text)
  {
    if (string.Equals(Prompt.Text, text ?? string.Empty, StringComparison.Ordinal)) return;
    isApplyingText = true;
    try
    {
      Prompt.Text = text ?? string.Empty;
    }
    finally
    {
      isApplyingText = false;
    }
  }

  internal void ShowAndFocus()
  {
    Visibility = Visibility.Visible;
    Prompt.Focus();
    Prompt.CaretIndex = Prompt.Text.Length;
    Prompt.ScrollToEnd();
  }

  internal void Hide() => Visibility = Visibility.Collapsed;

  internal void SetOpen(bool isOpen) =>
    Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;

  internal void ApplyTypography(double fontSize, FontFamily fontFamily)
  {
    ArgumentNullException.ThrowIfNull(fontFamily);
    Prompt.FontSize = fontSize;
    Prompt.FontFamily = fontFamily;
  }

  internal void SetPaperView(bool enabled)
  {
    DictateAnywhere.App.Presentation.PaperChatResources.Apply(this, enabled);
    bool showGrain = enabled && !WindowThemeBehavior.GetIsHighContrastActive(this);
    PaperGrain.Visibility = showGrain ? Visibility.Visible : Visibility.Collapsed;
    Surface.SetResourceReference(Border.BackgroundProperty,
      enabled ? "Brush.Paper.Page" : "Brush.Surface.Composer");
    Prompt.SetResourceReference(Control.ForegroundProperty,
      enabled ? "Brush.Paper.Ink" : "Brush.Text.Primary");
    UpdateFocusBorder();
  }

  private void UpdateFocusBorder() => Surface.SetResourceReference(
    Border.BorderBrushProperty, Prompt.IsKeyboardFocused ? "Brush.Progress.Value" : "Brush.Border.Subtle");

  private void OnPromptTextChanged(object sender, TextChangedEventArgs args)
  {
    if (!isApplyingText)
    {
      PromptTextChanged?.Invoke(this, args);
    }
  }
}
