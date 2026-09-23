using System.Globalization;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;

namespace DictateAnywhere.App.Tests;

public sealed class UnicodeReadingTextSegmenterTests
{
  public static IEnumerable<object[]> ScriptCorpus =>
  [
    [(int)ReaderScriptFamily.Latin, "Café déjà vu."],
    [(int)ReaderScriptFamily.Han, "你好世界。"],
    [(int)ReaderScriptFamily.Japanese, "日本語を読む。"],
    [(int)ReaderScriptFamily.Devanagari, "हिन्दी भाषा।"],
    [(int)ReaderScriptFamily.BengaliAssamese, "বাংলা ভাষা।"],
    [(int)ReaderScriptFamily.Gujarati, "ગુજરાતી ભાષા।"],
    [(int)ReaderScriptFamily.Gurmukhi, "ਪੰਜਾਬੀ ਭਾਸ਼ਾ।"],
    [(int)ReaderScriptFamily.Kannada, "ಕನ್ನಡ ಭಾಷೆ."],
    [(int)ReaderScriptFamily.Malayalam, "മലയാളം ഭാഷ."],
    [(int)ReaderScriptFamily.Odia, "ଓଡ଼ିଆ ଭାଷା।"],
    [(int)ReaderScriptFamily.Tamil, "தமிழ் மொழி."],
    [(int)ReaderScriptFamily.Telugu, "తెలుగు భాష."],
    [(int)ReaderScriptFamily.OlChiki, "ᱥᱟᱱᱛᱟᱲᱤ ᱯᱟᱨᱥᱤ."],
    [(int)ReaderScriptFamily.ArabicDerived, "اردو زبان۔"],
  ];

  [Xunit.Fact]
  public void ScriptCorpus_CoversEveryDeclaredReaderScriptFamily()
  {
    Xunit.Assert.Equal(
      Enum.GetValues<ReaderScriptFamily>(),
      ScriptCorpus.Select(row => (ReaderScriptFamily)(int)row[0]).Distinct());
  }

  [Xunit.Theory]
  [Xunit.MemberData(nameof(ScriptCorpus))]
  public void Segment_PreservesSourceSpansAtTextElementBoundaries(
    int scriptValue,
    string text)
  {
    ReaderScriptFamily script = (ReaderScriptFamily)scriptValue;
    ReadingTextSegmentationResult result = UnicodeReadingTextSegmenter.Segment(
      text,
      ReaderScriptProfile.Create(script).Direction);
    HashSet<int> textElementBoundaries = StringInfo.ParseCombiningCharacters(text)
      .Append(text.Length)
      .ToHashSet();

    Xunit.Assert.NotEmpty(result.Tokens);
    Xunit.Assert.All(result.Tokens, token =>
    {
      Xunit.Assert.Equal(token.Text, text.Substring(token.SourceStart, token.SourceLength));
      Xunit.Assert.Contains(token.SourceStart, textElementBoundaries);
      Xunit.Assert.Contains(token.SourceEnd, textElementBoundaries);
    });
  }

  [Xunit.Fact]
  public void Segment_DoesNotSplitCombiningMarksOrEmojiSequences()
  {
    const string text = "Cafe\u0301 👩🏽‍💻";

    ReadingTextSegmentationResult result = UnicodeReadingTextSegmenter.Segment(text);

    Xunit.Assert.Equal(["Cafe\u0301", "👩🏽‍💻"], result.Tokens.Select(token => token.Text));
  }

  [Xunit.Fact]
  public void Segment_UsesOneCjkTextElementPerHighlightToken()
  {
    ReadingTextSegmentationResult result = UnicodeReadingTextSegmenter.Segment("你好世界。次へ！");

    Xunit.Assert.Equal(["你", "好", "世", "界", "。", "次", "へ", "！"], result.Tokens.Select(token => token.Text));
  }

  [Xunit.Theory]
  [Xunit.InlineData("(123) English اردو", "LeftToRight")]
  [Xunit.InlineData("(123) اردو English", "RightToLeft")]
  [Xunit.InlineData("، English", "LeftToRight")]
  public void ResolveParagraphDirection_UsesTheFirstStrongLetter(
    string text,
    string expectedName)
  {
    ReaderTextDirection expected = Enum.Parse<ReaderTextDirection>(expectedName);
    Xunit.Assert.Equal(
      expected,
      UnicodeReadingTextSegmenter.ResolveParagraphDirection(text.AsSpan()));
  }

  [Xunit.Fact]
  public void Segment_ResolvesDirectionIndependentlyForEachParagraph()
  {
    ReadingTextSegmentationResult result = UnicodeReadingTextSegmenter.Segment(
      "English first.\n\nاردو پہلے۔");

    Xunit.Assert.Equal(2, result.Paragraphs.Count);
    Xunit.Assert.Equal(ReaderTextDirection.LeftToRight, result.Paragraphs[0].Direction);
    Xunit.Assert.Equal(ReaderTextDirection.RightToLeft, result.Paragraphs[1].Direction);
  }

  [Xunit.Fact]
  public void ReadingSection_UsesTheSelectedScriptDirectionWhenAParagraphHasNoStrongLetter()
  {
    ReadingSection section = new(
      0,
      UnicodeReadingTextSegmenter.Segment("123 …"));

    ReaderTextDirection direction = Xunit.Assert.Single(
      section.GetParagraphDirections(ReaderTextDirection.RightToLeft));

    Xunit.Assert.Equal(ReaderTextDirection.RightToLeft, direction);
  }

  [Xunit.Fact]
  public void ReadingSection_ResolvesFocusedWordDirectionFromItsParagraph()
  {
    ReadingSection section = ReadingTextLayout.Create(
      "Mixed",
      "English first.\n\nاردو پہلے۔").Sections[0];
    int urduIndex = section.Paragraphs[1].StartTokenIndex;

    Xunit.Assert.Equal(
      ReaderTextDirection.LeftToRight,
      section.GetDirectionForWord(0, ReaderTextDirection.LeftToRight));
    Xunit.Assert.Equal(
      ReaderTextDirection.RightToLeft,
      section.GetDirectionForWord(urduIndex, ReaderTextDirection.LeftToRight));
  }

  [Xunit.Fact]
  public void ReadingSection_SourceRangeRoundTripsExactDocumentText()
  {
    const string body = "First  body line.\nSecond line.";
    ReadingDocument document = ReadingTextLayout.Create(
      "Stories",
      [new ReadableDocumentSection("A Title", body)]);
    ReadingSection section = document.Sections[0];

    Xunit.Assert.Equal(body, section.GetSourceTextForTokenRange(section.TitleWordCount));
  }
}
