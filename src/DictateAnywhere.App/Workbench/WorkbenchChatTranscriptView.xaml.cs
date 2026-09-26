using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
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
  private bool renderedPaperView;
  private DispatcherOperation? pendingScroll;
  private bool bubbleLayoutPending;
  private bool bubbleWidthsDirty = true;
  private readonly ChatCodeScrollOverlay codeScrollOverlay;
  public WorkbenchChatTranscriptView()
  {
    InitializeComponent();
    codeScrollOverlay = new ChatCodeScrollOverlay(CodeScrollLayer, Transcript);
    LayoutUpdated += (_, _) =>
    {
      RefreshBubbleLayout();
      codeScrollOverlay.Refresh();
    };
    Transcript.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) =>
    {
      bubbleLayoutPending = true;
      RefreshBubbleLayout();
      codeScrollOverlay.Refresh();
    }));
  }

  public void Render(
    IReadOnlyList<ChatMessage> messages,
    Func<bool, Brush> resolveMessageBrush,
    Action<string> copyReply,
    Action<string> copyCode,
    Action<string> speakReply,
    bool paperView = false)
  {
    ArgumentNullException.ThrowIfNull(messages);
    ArgumentNullException.ThrowIfNull(resolveMessageBrush);
    ArgumentNullException.ThrowIfNull(copyReply);
    ArgumentNullException.ThrowIfNull(copyCode);
    ArgumentNullException.ThrowIfNull(speakReply);

    ChatMessage[] visible = messages.Select(message => message.Normalize())
      .Where(message => message.Role != ChatMessageRoles.System).ToArray();
    bool append = paperView == renderedPaperView && visible.Length >= renderedMessages.Length
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
        isAssistant ? speakReply : null,
        isPaper: paperView,
        isUser: !isAssistant);
      renderedMessageCount++;
    }

    bool isEmpty = renderedMessageCount == 0;
    Welcome.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    Transcript.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    bool changed = !renderedMessages.SequenceEqual(visible);
    if (changed || !append) codeScrollOverlay.Rebuild(Transcript.Document.Blocks, paperView);
    bubbleWidthsDirty |= changed || !append;
    bubbleLayoutPending = true;
    renderedMessages = visible;
    renderedPaperView = paperView;
    int messageIndex = 0;
    foreach (Section section in Transcript.Document.Blocks.OfType<Section>())
      section.Foreground = resolveMessageBrush(visible[messageIndex++].Role == ChatMessageRoles.Assistant);
    ApplyCodeTypography(Transcript.Document.Blocks, Transcript.FontSize);
    if (!append && !isEmpty)
    {
      TextPointer? start = Transcript.Document.ContentStart.GetPositionAtOffset(selectionStart);
      TextPointer? end = Transcript.Document.ContentStart.GetPositionAtOffset(selectionEnd);
      if (start is not null && end is not null) Transcript.Selection.Select(start, end);
    }
    if (!isEmpty && (changed || !append))
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
    BubbleLayer.Children.Clear();
    codeScrollOverlay.Clear();
    bubbleWidthsDirty = true;
  }

  private void OnTranscriptSizeChanged(object sender, SizeChangedEventArgs e)
  {
    double inset = Math.Max(24, (Transcript.ActualWidth - 900) / 2);
    Transcript.Document.PagePadding = new Thickness(inset, 0, inset, 0);
    bubbleWidthsDirty = true;
    bubbleLayoutPending = true;
  }

  internal void ApplyTypography(double fontSize, FontFamily fontFamily)
  {
    ArgumentNullException.ThrowIfNull(fontFamily);
    Transcript.FontSize = fontSize;
    Transcript.FontFamily = fontFamily;
    Transcript.Document.FontSize = fontSize;
    Transcript.Document.FontFamily = fontFamily;
    ApplyCodeTypography(Transcript.Document.Blocks, fontSize);
    bubbleWidthsDirty = true;
    bubbleLayoutPending = true;
  }

  internal void SetPaperView(bool enabled)
  {
    WelcomeMessage.SetResourceReference(TextBlock.ForegroundProperty, enabled ? "Brush.Paper.Ink" : "Brush.Text.Primary");
    WelcomeSubtitle.SetResourceReference(TextBlock.ForegroundProperty, enabled ? "Brush.Paper.Comment" : "Brush.Text.Secondary");
  }

  private static void ApplyCodeTypography(BlockCollection blocks, double fontSize)
  {
    foreach (Block block in blocks)
    {
      switch (block)
      {
        case BlockUIContainer { Child: Border { Child: Grid grid } }:
          foreach (RichTextBox code in grid.Children.OfType<RichTextBox>())
          {
            if (code.FontSize != fontSize) ChatMarkdownRenderer.ApplyCodeTypography(code, fontSize);
          }
          break;
        case Section section:
          ApplyCodeTypography(section.Blocks, fontSize);
          break;
        case System.Windows.Documents.List list:
          foreach (ListItem item in list.ListItems) ApplyCodeTypography(item.Blocks, fontSize);
          break;
      }
    }
  }

  // Paint behind the transparent editor rather than embedding a second editor per message.
  // This keeps document selection, copy and accessibility in the original text flow.
  private void RefreshBubbleLayout()
  {
    if (!bubbleLayoutPending || Transcript.ActualWidth <= 0 || Transcript.Visibility != Visibility.Visible) return;
    bubbleLayoutPending = false;
    Section[] users = Transcript.Document.Blocks.OfType<Section>().Where(section => section.Tag is ChatUserBubble).ToArray();
    double viewport = Transcript.ViewportWidth > 0 ? Transcript.ViewportWidth : Transcript.ActualWidth;
    double available = Math.Max(40, viewport - Transcript.Document.PagePadding.Left - Transcript.Document.PagePadding.Right);
    if (bubbleWidthsDirty)
    {
      bubbleWidthsDirty = false;
      foreach (Section section in users)
      {
        double maxWidth = Math.Max(40, available * 0.75);
        string text = new TextRange(section.ContentStart, section.ContentEnd).Text.TrimEnd('\r', '\n');
        double width = maxWidth;
        if (section.Blocks.Count == 1 && section.Blocks.FirstBlock is Paragraph && text.Length <= 10_000)
        {
          FormattedText measured = new(text.Length == 0 ? " " : text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Transcript.FontFamily, Transcript.FontStyle, Transcript.FontWeight, Transcript.FontStretch),
            Transcript.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(Transcript).PixelsPerDip)
          { MaxTextWidth = Math.Max(1, maxWidth - 36) };
          width = Math.Min(maxWidth, measured.WidthIncludingTrailingWhitespace + 36);
        }
        section.Margin = new Thickness(Math.Max(0, available - width), 0, 0, 24);
      }
      bubbleLayoutPending = true;
      return; // TextPointer geometry is valid after the document reflows with its new margins.
    }
    while (BubbleLayer.Children.Count > users.Length) BubbleLayer.Children.RemoveAt(BubbleLayer.Children.Count - 1);
    for (int index = 0; index < users.Length; index++)
    {
      Section section = users[index];
      if (BubbleLayer.Children.Count <= index)
        BubbleLayer.Children.Add(new Border { CornerRadius = new CornerRadius(18) });
      Border bubble = (Border)BubbleLayer.Children[index];
      bubble.SetResourceReference(Border.BackgroundProperty, ((ChatUserBubble)section.Tag).IsPaper ? "Brush.Paper.User" : "Brush.Chat.User");
      Rect start = section.ContentStart.GetCharacterRect(LogicalDirection.Forward);
      Rect end = section.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
      if (start.IsEmpty || end.IsEmpty) { bubble.Visibility = Visibility.Collapsed; continue; }
      bubble.Visibility = Visibility.Visible;
      bubble.Width = Math.Max(1, available - section.Margin.Left);
      bubble.Height = Math.Max(1, end.Bottom - start.Top + section.Padding.Top + section.Padding.Bottom);
      Canvas.SetLeft(bubble, Transcript.Document.PagePadding.Left + section.Margin.Left);
      Canvas.SetTop(bubble, start.Top - section.Padding.Top);
    }
  }
}
