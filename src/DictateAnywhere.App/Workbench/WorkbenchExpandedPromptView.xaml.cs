using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
  }

  public event RoutedEventHandler? CollapseClicked;
  public event RoutedEventHandler? SubmitClicked;
  public event KeyEventHandler? PromptKeyDown;
  public event TextChangedEventHandler? PromptTextChanged;

  internal string Text => Prompt.Text;
  internal bool IsOpen => Visibility == Visibility.Visible;
  internal TextBox PromptElement => Prompt;

  internal void SetText(string text)
  {
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

  private void OnPromptTextChanged(object sender, TextChangedEventArgs args)
  {
    if (!isApplyingText)
    {
      PromptTextChanged?.Invoke(this, args);
    }
  }
}
