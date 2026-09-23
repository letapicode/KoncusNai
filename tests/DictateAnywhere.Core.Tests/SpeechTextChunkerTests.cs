using DictateAnywhere.Core.Services;

namespace DictateAnywhere.Core.Tests;

public sealed class SpeechTextChunkerTests
{
  [Xunit.Fact]
  public void Split_PreservesSentenceBoundaries_WhenTheyFitWithinTheBudget()
  {
    IReadOnlyList<string> segments = SpeechTextChunker.Split(
      "First sentence is deliberately long enough to stand by itself in the queue. Second sentence is also deliberately long enough to use a separate segment. Third sentence ends here.",
      maximumCharacters: 80);

    Xunit.Assert.Equal(3, segments.Count);
    Xunit.Assert.Equal("First sentence is deliberately long enough to stand by itself in the queue.", segments[0]);
    Xunit.Assert.Equal("Second sentence is also deliberately long enough to use a separate segment.", segments[1]);
    Xunit.Assert.Equal("Third sentence ends here.", segments[2]);
  }

  [Xunit.Fact]
  public void Split_RecognizesHindiSentenceBoundaries()
  {
    IReadOnlyList<string> segments = SpeechTextChunker.Split(
      "यह पहला वाक्य पर्याप्त रूप से लंबा बनाया गया है ताकि वह अकेला रहे। यह दूसरा वाक्य भी पर्याप्त रूप से लंबा बनाया गया है ताकि अलग खंड बने।",
      maximumCharacters: 80);

    Xunit.Assert.Equal(2, segments.Count);
    Xunit.Assert.EndsWith("।", segments[0], StringComparison.Ordinal);
    Xunit.Assert.StartsWith("यह दूसरा", segments[1], StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void Split_NormalizesUnicodeToNfcAndPreservesDoubleDanda()
  {
    string decomposed = "Cafe\u0301 पठति॥ पुनः स्पष्टं वदति।";

    IReadOnlyList<string> segments = SpeechTextChunker.Split(decomposed, maximumCharacters: 80);

    Xunit.Assert.Single(segments);
    Xunit.Assert.Contains("Café", segments[0], StringComparison.Ordinal);
    Xunit.Assert.Contains("॥", segments[0], StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void Split_UsesUrduPhraseAndWordBoundariesWithoutChangingScript()
  {
    string text = string.Join(' ', Enumerable.Repeat(
      "یہ عبارت واضح رہنی چاہیے، اور کسی لفظ کے اندر تقسیم نہیں ہونی چاہیے۔",
      4));

    IReadOnlyList<string> segments = SpeechTextChunker.Split(text, maximumCharacters: 90);

    Xunit.Assert.True(segments.Count > 1);
    Xunit.Assert.Equal(
      string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
      string.Join(' ', segments));
  }

}
