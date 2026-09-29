using System;
using System.Windows;
using System.Windows.Input;

namespace DictateAnywhere.App.Productivity;

public partial class RetryFeedbackWindow : Window
{
  internal RetryFeedbackWindow(ProductivityActionResult result)
  {
    ArgumentNullException.ThrowIfNull(result);
    InitializeComponent();
    bool noPreviousDictation = result.OutcomeCode is RetryOutcomeCode.NoHistory or RetryOutcomeCode.Expired;
    HeadingText.Text = noPreviousDictation ? "Nothing to retry yet" : "Could not retry dictation";
    MessageText.Text = noPreviousDictation
      ? $"{result.Message}\n\nStart a new dictation in Workbench, then try Retry Last Dictation again."
      : result.Message;
  }

  private void OnLoaded(object sender, RoutedEventArgs e) => _ = CloseButton.Focus();

  private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

  private void OnPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key != Key.Escape) return;
    e.Handled = true;
    Close();
  }
}
