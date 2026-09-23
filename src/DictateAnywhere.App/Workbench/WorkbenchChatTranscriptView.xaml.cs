using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Threading;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

/// <summary>Owns chat transcript rendering and its empty-state presentation.</summary>
public partial class WorkbenchChatTranscriptView : UserControl
{
  private ChatMessage[] renderedMessages = [];
  private DispatcherOperation? pendingScroll;
  public WorkbenchChatTranscriptView() => InitializeComponent();

  public void Render(
    IReadOnlyList<ChatMessage> messages,
    Func<bool, Brush> resolveMessageBrush,
    Action<string> copyReply,
    Action<string> copyCode,
    Action<string> speakReply)
  {
    ArgumentNullException.ThrowIfNull(messages);
    ArgumentNullException.ThrowIfNull(resolveMessageBrush);
    ArgumentNullException.ThrowIfNull(copyReply);
    ArgumentNullException.ThrowIfNull(copyCode);
    ArgumentNullException.ThrowIfNull(speakReply);

    ChatMessage[] visible = messages.Select(message => message.Normalize())
      .Where(message => message.Role != ChatMessageRoles.System).ToArray();
    bool append = visible.Length >= renderedMessages.Length
      && renderedMessages.SequenceEqual(visible.Take(renderedMessages.Length));
    bool follow = renderedMessages.Length == 0 || Transcript.ExtentHeight - Transcript.ViewportHeight - Transcript.VerticalOffset <= 24;
    double offset = Transcript.VerticalOffset;
    int selectionStart = Transcript.Document.ContentStart.GetOffsetToPosition(Transcript.Selection.Start);
    int selectionEnd = Transcript.Document.ContentStart.GetOffsetToPosition(Transcript.Selection.End);
    if (!append || renderedMessages.Length == 0) Transcript.Document.Blocks.Clear();
    int renderedMessageCount = 0;
    foreach (ChatMessage source in visible)
    {
      ChatMessage message = source.Normalize();
      if (string.Equals(message.Role, ChatMessageRoles.System, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      bool isAssistant = string.Equals(message.Role, ChatMessageRoles.Assistant, StringComparison.OrdinalIgnoreCase);
      if (append && renderedMessageCount < renderedMessages.Length)
      {
        // Keep document elements and selection intact on presentation-only refreshes.
        renderedMessageCount++;
        continue;
      }
      ChatMarkdownRenderer.Append(
        Transcript.Document,
        message.Content,
        resolveMessageBrush(isAssistant),
        isAssistant ? copyReply : null,
        isAssistant ? copyCode : null,
        isAssistant ? speakReply : null);
      renderedMessageCount++;
    }

    bool isEmpty = renderedMessageCount == 0;
    Welcome.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    Transcript.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    bool changed = !renderedMessages.SequenceEqual(visible);
    renderedMessages = visible;
    int messageIndex = 0;
    foreach (Section section in Transcript.Document.Blocks.OfType<Section>())
      section.Foreground = resolveMessageBrush(visible[messageIndex++].Role == ChatMessageRoles.Assistant);
    if (!append && !isEmpty)
    {
      TextPointer? start = Transcript.Document.ContentStart.GetPositionAtOffset(selectionStart);
      TextPointer? end = Transcript.Document.ContentStart.GetPositionAtOffset(selectionEnd);
      if (start is not null && end is not null) Transcript.Selection.Select(start, end);
    }
    if (!isEmpty && changed)
    {
      pendingScroll?.Abort();
      pendingScroll = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
      {
        pendingScroll = null;
        if (follow) Transcript.ScrollToEnd();
        else Transcript.ScrollToVerticalOffset(offset);
      }));
    }
  }

  internal RichTextBox TranscriptElement => Transcript;

  internal bool IsEmptyStateVisible => Welcome.Visibility == Visibility.Visible;

  internal void SetWelcomeMessage(string message) => WelcomeMessage.Text = message ?? string.Empty;

  internal void Clear()
  {
    pendingScroll?.Abort();
    pendingScroll = null;
    renderedMessages = [];
    Transcript.Document.Blocks.Clear();
  }

  private void OnTranscriptSizeChanged(object sender, SizeChangedEventArgs e)
  {
    double inset = Math.Max(20, (Transcript.ActualWidth - 740) / 2);
    Transcript.Document.PagePadding = new Thickness(inset, 0, inset, 0);
  }

  internal void ApplyTypography(double fontSize, FontFamily fontFamily)
  {
    ArgumentNullException.ThrowIfNull(fontFamily);
    Transcript.FontSize = fontSize;
    Transcript.FontFamily = fontFamily;
    Transcript.Document.FontSize = fontSize;
    Transcript.Document.FontFamily = fontFamily;
  }
}
