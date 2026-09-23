using System.Windows.Documents;
using System.Windows.Media;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

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
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "STA boundary captures and rethrows the test failure on the test thread.")]
  public void Markdown_PreservesCodeAndRendersProseWithoutBlankParagraphs()
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
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
      }
      catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
    if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
  }
}
