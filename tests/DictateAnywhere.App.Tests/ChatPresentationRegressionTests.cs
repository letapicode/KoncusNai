using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class ChatPresentationRegressionTests
{
  [Theory]
  [InlineData("2026-12-31T23:30:00-05:00", "what day is tomorrow?", "Friday, January 1, 2027")]
  [InlineData("2024-02-28T12:00:00-05:00", "what day is tomorrow? Hi", "Thursday, February 29, 2024")]
  [InlineData("2026-03-08T04:30:00Z", "what date is tomorrow", "Sunday, March 8, 2026")]
  public void Calendar_UsesLocalCalendarDays(string instant, string question, string expected)
  {
    Assert.True(LocalCalendarContext.CanAnswer(question));
    Assert.Contains(expected, LocalCalendarContext.Answer(question, DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture),
      TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")));
  }

  [Theory]
  [InlineData("Translate 'what day is tomorrow' into Nepali")]
  [InlineData("what day is tomorrow in Tokyo?")]
  [InlineData("In my story, what day is today?")]
  public void Calendar_DoesNotHijackOtherRequests(string prompt) => Assert.False(LocalCalendarContext.CanAnswer(prompt));

  [Fact]
  public async Task ClockContext_IsFreshAndNeverPersisted()
  {
    await using WorkbenchChatController controller = new(_ => throw new InvalidOperationException(), () => TimeZoneInfo.Utc);
    DateTimeOffset first = new(2026, 9, 15, 23, 59, 0, TimeSpan.Zero);
    controller.BeginCompletion("A question", first);
    var next = controller.BeginCompletion("Another question", first.AddMinutes(2));
    Assert.Contains("2026-09-16 Wednesday", next.Messages[0].Content);
    Assert.DoesNotContain(controller.Messages, message => message.Role == "system");
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void Markdown_PreservesCodeAndRendersProseWithoutBlankParagraphs()
  {
    WpfTestSta.Run(() =>
    {
      FlowDocument document = new();
      ChatMarkdownRenderer.Append(document, "# Title\n\nLearn *how* and **why**.\nsoft break\n\n---\n\n- First\n- Second\n\n```python\na = 2 * 3\n```", Brushes.Black);
      Section section = Assert.IsType<Section>(document.Blocks.FirstBlock);
      var paragraphs = section.Blocks.OfType<Paragraph>().ToArray();
      Assert.Equal(2, paragraphs.Length);
      Assert.Contains(paragraphs[1].Inlines, inline => inline is Italic);
      Assert.Contains(paragraphs[1].Inlines, inline => inline is Bold);
      Assert.Contains(section.Blocks, block => block is System.Windows.Documents.List);
      Assert.Equal("a = 2 * 3", ChatMarkdownRenderer.ExtractFencedCode("```python\na = 2 * 3\n```"));
      Assert.Contains("Learn how", ChatMarkdownRenderer.ToSpeechText("Learn *how*."));
      Assert.DoesNotContain("*", ChatMarkdownRenderer.ToSpeechText("Learn *how*."));
    });
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void UserMessage_DisplaysPastedMarkdownAndCodeLiterally()
  {
    WpfTestSta.Run(() =>
    {
      const string message = "class Node {\n```\n  Node(int data) {\n    this.data = data;\n  }\n```\n}";
      FlowDocument document = new();
      ChatMarkdownRenderer.Append(document, message, Brushes.Black, isUser: true);

      Section section = Assert.IsType<Section>(document.Blocks.FirstBlock);
      Paragraph paragraph = Assert.IsType<Paragraph>(Assert.Single(section.Blocks.Cast<Block>()));
      Run text = Assert.IsType<Run>(Assert.Single(paragraph.Inlines.Cast<Inline>()));
      Assert.Equal(message, text.Text);
      Assert.IsType<ChatUserBubble>(section.Tag);
    });
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void Markdown_PaperCodeStaysSelectableScrollableAndCopiesOriginalSource()
  {
    WpfTestSta.Run(() =>
    {
      string? copied = null;
      FlowDocument document = new();
      ChatMarkdownRenderer.Append(document, "```java\npublic class Demo { int n = 12; }\n```",
        Brushes.Black, onCopyCode: code => copied = code, isPaper: true);
      Section section = Assert.IsType<Section>(document.Blocks.FirstBlock);
      Border border = Assert.IsType<Border>(Assert.IsType<BlockUIContainer>(section.Blocks.FirstBlock).Child);
      Grid grid = Assert.IsType<Grid>(border.Child);
      RichTextBox codeBox = Assert.Single(grid.Children.OfType<RichTextBox>());
      Button copyButton = Assert.Single(grid.Children.OfType<Button>());
      Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Auto, codeBox.HorizontalScrollBarVisibility);
      Assert.InRange(codeBox.Document.PageWidth, 100, 700);
      Assert.Equal(Brushes.Transparent, border.Background);
      Assert.Equal(new Thickness(0), border.BorderThickness);
      Paragraph codeParagraph = Assert.IsType<Paragraph>(Assert.Single(codeBox.Document.Blocks.Cast<Block>()));
      Assert.Equal("public class Demo { int n = 12; }", new TextRange(codeParagraph.ContentStart, codeParagraph.ContentEnd).Text);
      Assert.Equal(FontWeights.Normal, codeBox.FontWeight);
      Assert.Equal(15 * 1.55, codeBox.Document.LineHeight);
      copyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      Assert.Equal("public class Demo { int n = 12; }", copied);
    });
  }
}
