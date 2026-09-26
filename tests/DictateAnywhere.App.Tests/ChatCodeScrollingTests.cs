using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class ChatCodeScrollingTests
{
  [Theory]
  [InlineData("    ", 15d, false)]
  [InlineData("\t", 15d, true)]
  [InlineData("        ", 30d, false)]
  public void WrappedContinuation_UsesHangingIndentAndPreservesBlankLinesAndPartialSelection(string prefix, double fontSize, bool paper)
  {
    RunOnSta(() =>
    {
      string source = prefix + "// Full implementation omitted for " + new string('x', 140) + " brevity.\n\n" + prefix + "return;\n";
      FlowDocument document = new();
      ChatMarkdownRenderer.Append(document, $"```java\n{source}\n```", Brushes.Black, isPaper: paper);
      RichTextBox code = Assert.Single(CodeGrid(document).Children.OfType<RichTextBox>());
      ChatMarkdownRenderer.ApplyCodeTypography(code, fontSize);
      Window host = Host(new RichTextBox { Document = document, IsReadOnly = true });
      try
      {
        host.Show();
        Settle(host);
        string original = new TextRange(code.Document.ContentStart, code.Document.ContentEnd).Text;
        Run comment = Assert.IsType<Paragraph>(code.Document.Blocks.FirstBlock).Inlines.OfType<Run>().First(run => run.Text.StartsWith("//", StringComparison.Ordinal));
        code.Selection.Select(comment.ContentStart.GetPositionAtOffset(3)!, comment.ContentStart.GetPositionAtOffset(23)!);
        string selected = code.Selection.Text;
        ChatCodeWrapping.SetIsWrapped(code, true);
        Settle(host);
        Paragraph line = Assert.IsType<Paragraph>(code.Document.Blocks.FirstBlock);
        Rect first = comment.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        Rect continuation = comment.ContentStart.GetPositionAtOffset(comment.Text.IndexOf("brevity", StringComparison.Ordinal))!.GetCharacterRect(LogicalDirection.Forward);
        Assert.True(continuation.Top > first.Top);
        Assert.True(continuation.Left > first.Left + 1, $"First={first.Left}, continuation={continuation.Left}");
        Assert.True(line.Margin.Left > 0);
        Assert.InRange(line.Margin.Left, 0, (code.ActualWidth - code.Padding.Left - code.Padding.Right) * 0.4 + 1);
        Assert.Equal(original, new TextRange(code.Document.ContentStart, code.Document.ContentEnd).Text);
        Assert.Equal(selected, code.Selection.Text);
        host.Width = 430;
        Settle(host);
        Assert.True(code.ExtentWidth <= code.ViewportWidth + 1);
        Assert.InRange(line.Margin.Left, 0, (code.ActualWidth - code.Padding.Left - code.Padding.Right) * 0.4 + 1);
        ChatCodeWrapping.SetIsWrapped(code, false);
        Settle(host);
        Assert.Single(code.Document.Blocks.Cast<Block>());
        Assert.Equal(original, new TextRange(code.Document.ContentStart, code.Document.ContentEnd).Text);
        Assert.Equal(selected, code.Selection.Text);
      }
      finally { host.Close(); }
    });
  }
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void WrapToggle_ReflowsLongLinesWithoutChangingSourceAndSurvivesResizeAndAppearance(bool paper)
  {
    RunOnSta(() =>
    {
      string source = "\twhile (node.left != null && " + new string('x', 180) + ") { TAIL; }\nreturn;";
      WorkbenchChatTranscriptView view = new();
      string? copied = null;
      ChatMessage[] messages = [new(ChatMessageRoles.Assistant, $"```java\n{source}\n```", DateTimeOffset.UtcNow)];
      view.Render(messages, _ => Brushes.Black, _ => { }, text => copied = text, _ => { }, paperView: paper);
      Window host = Host(view);
      try
      {
        host.Show();
        Settle(host);
        Grid grid = CodeGrid(view.TranscriptElement.Document);
        RichTextBox code = Assert.Single(grid.Children.OfType<RichTextBox>());
        CheckBox toggle = Assert.Single(grid.Children.OfType<CheckBox>());
        Assert.False(toggle.IsChecked);
        Assert.True(code.ExtentWidth > code.ViewportWidth);
        code.Selection.Select(code.Document.ContentStart, code.Document.ContentEnd);
        string original = code.Selection.Text;
        toggle.IsChecked = true;
        Settle(host);
        Assert.True(ChatCodeWrapping.GetIsWrapped(code));
        Assert.True(double.IsNaN(code.Document.PageWidth), $"PageWidth={code.Document.PageWidth}; font={code.FontSize}; wrap={ChatCodeWrapping.GetIsWrapped(code)}; paragraphs={code.Document.Blocks.Count}");
        Assert.Equal(ScrollBarVisibility.Disabled, code.HorizontalScrollBarVisibility);
        Assert.True(code.ExtentWidth <= code.ViewportWidth + 1);
        Paragraph paragraph = Assert.IsType<Paragraph>(code.Document.Blocks.FirstBlock);
        Run first = paragraph.Inlines.OfType<Run>().First();
        Run last = code.Document.Blocks.OfType<Paragraph>().SelectMany(line => line.Inlines.OfType<Run>()).Last();
        Assert.True(last.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Top
          > first.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top + code.FontSize * 2);
        Assert.Equal(original, code.Selection.Text);
        Assert.Single(grid.Children.OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(source, copied);
        host.Width = 700;
        view.ApplyTypography(30, new FontFamily("Segoe UI"));
        Settle(host);
        Assert.True(double.IsNaN(code.Document.PageWidth), $"PageWidth={code.Document.PageWidth}; font={code.FontSize}; wrap={ChatCodeWrapping.GetIsWrapped(code)}; paragraphs={code.Document.Blocks.Count}");
        Assert.True(code.ExtentWidth <= code.ViewportWidth + 1);
        view.Render(messages, _ => Brushes.Black, _ => { }, _ => { }, _ => { }, paperView: !paper);
        Settle(host);
        grid = CodeGrid(view.TranscriptElement.Document);
        code = Assert.Single(grid.Children.OfType<RichTextBox>());
        toggle = Assert.Single(grid.Children.OfType<CheckBox>());
        Assert.True(toggle.IsChecked);
        view.Render([.. messages, new(ChatMessageRoles.User, "Next", DateTimeOffset.UtcNow)],
          _ => Brushes.Black, _ => { }, _ => { }, _ => { }, paperView: !paper);
        Settle(host);
        Assert.True(ChatCodeWrapping.GetIsWrapped(code));
        Canvas layer = Assert.IsType<Canvas>(view.FindName("CodeScrollLayer"));
        Assert.False(Assert.Single(layer.Children.OfType<ScrollBar>()).IsVisible);
        toggle.IsChecked = false;
        Settle(host);
        Assert.False(ChatCodeWrapping.GetIsWrapped(code));
        Assert.Equal(ScrollBarVisibility.Auto, code.HorizontalScrollBarVisibility);
        Assert.True(code.ExtentWidth > code.ViewportWidth);
        Assert.Equal(original.TrimEnd('\r', '\n'), new TextRange(code.Document.ContentStart, code.Document.ContentEnd).Text.TrimEnd('\r', '\n'));
      }
      finally { host.Close(); }
    });
  }
  [Theory]
  [InlineData(12d, false)]
  [InlineData(15d, false)]
  [InlineData(30d, true)]
  public void NativeHorizontalBar_HasAFullWidthTrackAndCanRevealTheLastCharacter(double size, bool paper)
  {
    RunOnSta(() =>
    {
      string source = "while (node != null && node.left.color && " + new string('x', 180) + ") { TAIL; }";
      FlowDocument document = new();
      string? copied = null;
      ChatMarkdownRenderer.Append(document, $"```java\n{source}\n```", Brushes.Black, onCopyCode: text => copied = text, isPaper: paper);
      Grid grid = CodeGrid(document);
      RichTextBox code = Assert.Single(grid.Children.OfType<RichTextBox>());
      ChatMarkdownRenderer.ApplyCodeTypography(code, size);
      Window host = Host(new RichTextBox { Document = document, IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
      try
      {
        host.Show();
        Settle(host);
        ScrollBar bar = Assert.Single(Descendants(code).OfType<ScrollBar>().Where(bar => bar.Orientation == Orientation.Horizontal));
        Assert.True(bar.IsVisible);
        Assert.True(bar.ActualWidth > 400);
        Track track = Assert.IsType<Track>(bar.Template.FindName("PART_Track", bar));
        Assert.Equal(Orientation.Horizontal, track.Orientation);
        Assert.True(track.ActualWidth > 400);
        Assert.Same(ScrollBar.PageRightCommand, track.IncreaseRepeatButton.Command);
        double before = code.HorizontalOffset;
        ScrollBar.PageRightCommand.Execute(null, bar);
        Settle(host);
        Assert.True(code.HorizontalOffset > before);
        double moved = code.HorizontalOffset;
        ScrollBar.PageLeftCommand.Execute(null, bar);
        Settle(host);
        Assert.True(code.HorizontalOffset < moved);
        code.ScrollToHorizontalOffset(code.ExtentWidth);
        Settle(host);
        Paragraph paragraph = Assert.IsType<Paragraph>(code.Document.Blocks.FirstBlock);
        Run last = paragraph.Inlines.OfType<Run>().Last();
        Rect tail = last.ContentEnd.GetPositionAtOffset(-1)!.GetCharacterRect(LogicalDirection.Forward);
        Assert.InRange(tail.Right, 0, code.ActualWidth);
        Assert.Equal(source, new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text);
        Assert.Single(grid.Children.OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(source, copied);
      }
      finally { host.Close(); }
    });
  }

  [Fact]
  public void TallCode_HasHorizontalAccessInTheMiddleAndPreservesOffsetsThroughPaperAndAppend()
  {
    RunOnSta(() =>
    {
      WorkbenchChatTranscriptView view = new();
      string source = string.Join("\n", Enumerable.Repeat("while (node.left != null && " + new string('x', 170) + ") { }", 80));
      ChatMessage[] messages = [new(ChatMessageRoles.Assistant, $"```java\n{source}\n```", DateTimeOffset.UtcNow)];
      view.Render(messages, _ => Brushes.Black, _ => { }, _ => { }, _ => { });
      view.ApplyTypography(15, new FontFamily("Segoe UI"));
      Window host = Host(view);
      try
      {
        host.Show();
        Settle(host);
        view.TranscriptElement.ScrollToVerticalOffset(600);
        Settle(host);
        Canvas layer = Assert.IsType<Canvas>(view.FindName("CodeScrollLayer"));
        ScrollBar bar = Assert.Single(layer.Children.OfType<ScrollBar>());
        Assert.True(bar.IsVisible);
        Rect barBounds = bar.TransformToAncestor(view).TransformBounds(new Rect(bar.RenderSize));
        Assert.InRange(barBounds.Bottom, 40, view.ActualHeight);
        Assert.True(barBounds.Top >= view.TranscriptElement.ActualHeight - 1);
        RichTextBox code = Assert.Single(CodeGrid(view.TranscriptElement.Document).Children.OfType<RichTextBox>());
        code.Selection.Select(code.Document.ContentStart, code.Document.ContentEnd);
        string selection = code.Selection.Text;
        ScrollBar.PageRightCommand.Execute(null, bar);
        Settle(host);
        Assert.True(code.HorizontalOffset > 0);
        Assert.Equal(selection, code.Selection.Text);
        double offset = code.HorizontalOffset;
        view.Render(messages, _ => Brushes.Black, _ => { }, _ => { }, _ => { }, paperView: true);
        Settle(host);
        RichTextBox updated = Assert.Single(CodeGrid(view.TranscriptElement.Document).Children.OfType<RichTextBox>());
        Assert.InRange(Math.Abs(updated.HorizontalOffset - offset), 0, 1);
        view.Render([.. messages, new(ChatMessageRoles.User, "Follow up", DateTimeOffset.UtcNow)],
          _ => Brushes.Black, _ => { }, _ => { }, _ => { }, paperView: true);
        Settle(host);
        Assert.Same(updated, Assert.Single(CodeGrid(view.TranscriptElement.Document).Children.OfType<RichTextBox>()));
        Assert.InRange(Math.Abs(updated.HorizontalOffset - offset), 0, 1);
        host.Width = 1000;
        view.ApplyTypography(30, new FontFamily("Segoe UI"));
        Settle(host);
        bar = Assert.Single(layer.Children.OfType<ScrollBar>());
        Assert.True(bar.Maximum > 0);
        Assert.True(bar.Width <= view.ActualWidth);
        view.Clear();
        Settle(host);
        Assert.Empty(layer.Children);
      }
      finally { host.Close(); }
    });
  }

  [Fact]
  public void MeasuredCodeWidth_AccountsForTabsUnicodeAndFallbackWithoutWrapping()
  {
    RunOnSta(() =>
    {
      const string source = "\t\tString text = \"नमस्ते 世界 🙂\"; // " + "a long trailing comment that remains on the same line even with fallback fonts and tabs";
      FlowDocument document = new();
      ChatMarkdownRenderer.Append(document, $"```java\n{source}\n```", Brushes.Black);
      RichTextBox code = Assert.Single(CodeGrid(document).Children.OfType<RichTextBox>());
      ChatMarkdownRenderer.ApplyCodeTypography(code, 30);
      Window host = Host(new RichTextBox { Document = document, IsReadOnly = true });
      try
      {
        host.Show();
        Settle(host);
        code.ScrollToHorizontalOffset(code.ExtentWidth);
        Settle(host);
        Paragraph paragraph = Assert.IsType<Paragraph>(code.Document.Blocks.FirstBlock);
        Run first = paragraph.Inlines.OfType<Run>().First();
        Run last = paragraph.Inlines.OfType<Run>().Last();
        Rect start = first.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        Rect end = last.ContentEnd.GetPositionAtOffset(-1)!.GetCharacterRect(LogicalDirection.Forward);
        Assert.InRange(Math.Abs(end.Top - start.Top), 0, 1);
        Assert.InRange(end.Right, 0, code.ActualWidth);
      }
      finally { host.Close(); }
    });
  }

  [Fact]
  public void ShortCode_DoesNotOfferHorizontalScrollingAndVerticalScrollbarsStayVertical()
  {
    RunOnSta(() =>
    {
      WorkbenchChatTranscriptView view = new();
      view.Render([new(ChatMessageRoles.Assistant, "```java\nreturn 1;\n```", DateTimeOffset.UtcNow)],
        _ => Brushes.Black, _ => { }, _ => { }, _ => { });
      Window host = Host(view);
      try
      {
        host.Show();
        Settle(host);
        RichTextBox code = Assert.Single(CodeGrid(view.TranscriptElement.Document).Children.OfType<RichTextBox>());
        Assert.True(code.ExtentWidth <= code.ViewportWidth + 0.5);
        Canvas layer = Assert.IsType<Canvas>(view.FindName("CodeScrollLayer"));
        Assert.False(Assert.Single(layer.Children.OfType<ScrollBar>()).IsVisible);
        ScrollBar vertical = Descendants(view.TranscriptElement).OfType<ScrollBar>().First(bar => bar.Orientation == Orientation.Vertical);
        vertical.ApplyTemplate();
        Track track = Assert.IsType<Track>(vertical.Template.FindName("PART_Track", vertical));
        Assert.Equal(Orientation.Vertical, track.Orientation);
        Assert.Same(ScrollBar.PageDownCommand, track.IncreaseRepeatButton.Command);
        Assert.Equal(8, vertical.Width);
      }
      finally { host.Close(); }
    });
  }

  private static Grid CodeGrid(FlowDocument document) => Assert.IsType<Grid>(Assert.IsType<Border>(
    Assert.IsType<BlockUIContainer>(Assert.IsType<Section>(document.Blocks.FirstBlock).Blocks.FirstBlock).Child).Child);

  private static Window Host(object content)
  {
    Window host = new() { Content = content, Width = 600, Height = 360, Left = -10000, Top = -10000, ShowInTaskbar = false };
    host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DictateAnywhere.App;component/Theming/DesignTokens.xaml", UriKind.Relative) });
    host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DictateAnywhere.App;component/Theming/ControlStyles.xaml", UriKind.Relative) });
    AppThemeManager.ApplyPalette(host.Resources, true);
    return host;
  }

  private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
  {
    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
      DependencyObject child = VisualTreeHelper.GetChild(root, index);
      yield return child;
      foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
    }
  }

  private static void Settle(Window host)
  {
    host.UpdateLayout();
    host.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    host.UpdateLayout();
  }

  [SuppressMessage("Design", "CA1031", Justification = "Transfers STA test failures to the test runner.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() => { try { action(); } catch (Exception exception) { failure = exception; } });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "STA test timed out.");
    if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
  }
}
