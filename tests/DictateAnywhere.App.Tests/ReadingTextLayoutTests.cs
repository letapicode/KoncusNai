using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Tests;

public sealed class ReadingTextLayoutTests
{
  [Xunit.Fact]
  public void Create_WithSemanticStories_KeepsOneStableSectionPerTitle()
  {
    ReadableDocumentSection[] stories =
    [
      new("The First Story", "First paragraph.\n\nSecond paragraph."),
      new("The Second Story", string.Join(' ', Enumerable.Repeat("A deliberately long body.", 200))),
    ];

    ReadingDocument document = ReadingTextLayout.Create("Story collection", stories);

    Xunit.Assert.Equal(2, document.Sections.Count);
    Xunit.Assert.Equal("The First Story", document.Sections[0].Title);
    Xunit.Assert.Equal(3, document.Sections[0].TitleWordCount);
    Xunit.Assert.Contains("Second paragraph.", document.Sections[0].Text, StringComparison.Ordinal);
    Xunit.Assert.Equal("The Second Story", document.Sections[1].Title);
  }

  [Xunit.Fact]
  public void Create_FormatsLongTextIntoSpeakableSectionsWithoutLosingWords()
  {
    string text = string.Join(" ", Enumerable.Range(1, 120).Select(index => $"word{index}."));

    ReadingDocument document = ReadingTextLayout.Create("A book", text, targetSectionCharacters: 180);

    Xunit.Assert.True(document.Sections.Count > 1);
    Xunit.Assert.Equal(120, document.TotalWordCount);
    Xunit.Assert.All(document.Sections, section => Xunit.Assert.NotEmpty(section.Words));
  }

  [Xunit.Fact]
  public void GetWordIndex_MapsPlaybackProgressToCurrentWord()
  {
    int start = ReadingPlaybackTiming.GetWordIndex(TimeSpan.Zero, TimeSpan.FromSeconds(10), 10);
    int halfway = ReadingPlaybackTiming.GetWordIndex(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), 10);
    int end = ReadingPlaybackTiming.GetWordIndex(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(10), 10);

    Xunit.Assert.Equal(0, start);
    Xunit.Assert.Equal(5, halfway);
    Xunit.Assert.Equal(9, end);
  }

  [Xunit.Fact]
  public void GetWordIndex_UsesWordLengthAndPunctuationForANaturalCue()
  {
    string[] words = ["I", "will", "wait.", "Extraordinary"];

    int index = ReadingPlaybackTiming.GetWordIndex(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), words);

    Xunit.Assert.Equal(2, index);
  }

  [Xunit.Fact]
  public void GetDocumentProgress_IncludesCompletedAndCurrentSectionWords()
  {
    ReadingDocument document = ReadingTextLayout.Create("A book", "one two three four five six", targetSectionCharacters: 240);
    ReadingDocument split = document with
    {
      Sections =
      [
        new ReadingSection(0, UnicodeReadingTextSegmenter.Segment("one two")),
        new ReadingSection(1, UnicodeReadingTextSegmenter.Segment("three four five six")),
      ],
    };

    double progress = ReadingPlaybackTiming.GetDocumentProgress(split, 1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));

    Xunit.Assert.Equal(4d / 6d, progress, precision: 6);
  }

  [Xunit.Fact]
  public void GetSentenceRange_ExtendsToTheContainingSentence()
  {
    string[] words = ["First", "sentence.", "The", "second", "one", "ends!"];

    (int start, int end) = ReadingPlaybackTiming.GetSentenceRange(words, 3);

    Xunit.Assert.Equal(2, start);
    Xunit.Assert.Equal(5, end);
  }

  [Xunit.Fact]
  public void GetSentenceRange_KeepsAClosingQuotationWithTheSentenceItCloses()
  {
    string[] words = ["She", "said.", "\"", "Then", "she", "left."];

    Xunit.Assert.Equal((0, 2), ReadingPlaybackTiming.GetSentenceRange(words, 1));
    Xunit.Assert.Equal((0, 2), ReadingPlaybackTiming.GetSentenceRange(words, 2));
    Xunit.Assert.Equal((3, 5), ReadingPlaybackTiming.GetSentenceRange(words, 3));
  }

  [Xunit.Fact]
  public void Create_DoesNotSplitAClosingQuoteIntoTheNextSection()
  {
    string quoted = string.Join(" ", Enumerable.Repeat("A long introduction continues", 24)) + ".\" The next sentence begins here.";

    ReadingDocument document = ReadingTextLayout.Create("Quoted", quoted, targetSectionCharacters: 240);

    Xunit.Assert.DoesNotContain(document.Sections.Skip(1), section => section.Text.StartsWith("\"", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public void Create_TokenizesCjkTextWithoutInventingSpaces()
  {
    ReadingDocument document = ReadingTextLayout.Create("中文", "你好世界。下一句。", targetSectionCharacters: 240);

    Xunit.Assert.True(document.Sections[0].Words.Count > 4);
    Xunit.Assert.Equal("你好世界。下一句。", ReadingTextLayout.JoinWords(document.Sections[0].Words));
  }

  [Xunit.Fact]
  public void Create_PreservesParagraphBreaksForReaderAndVideoLayout()
  {
    ReadingDocument document = ReadingTextLayout.Create("Book", "First paragraph.\n\nSecond paragraph.", targetSectionCharacters: 240);
    ReadingSection section = document.Sections[0];

    Xunit.Assert.Equal([2], section.ParagraphStartWordIndices);
    Xunit.Assert.Equal("First paragraph.\r\n\r\nSecond paragraph.", ReadingTextLayout.JoinWords(section.Words, paragraphStarts: section.ParagraphStartWordIndices));
  }

  [Xunit.Fact]
  public void GetSentenceRange_RecognizesHindiDanda()
  {
    ReadingDocument document = ReadingTextLayout.Create("हिंदी", "यह पहला वाक्य है। यह दूसरा वाक्य है।", targetSectionCharacters: 240);
    int secondSentenceWord = document.Sections[0].Words.ToList().IndexOf("यह", 1);

    (int start, int end) = ReadingPlaybackTiming.GetSentenceRange(document.Sections[0].Words, secondSentenceWord);

    Xunit.Assert.Equal(secondSentenceWord, start);
    Xunit.Assert.Equal("।", document.Sections[0].Words[end]);
  }

  [Xunit.Fact]
  public void GetPositionForWordIndex_RoundTripsThroughTheWeightedCueMap()
  {
    string[] words = ["Short", "extraordinary", "sentence.", "Next"];
    TimeSpan duration = TimeSpan.FromSeconds(12);

    TimeSpan position = ReadingPlaybackTiming.GetPositionForWordIndex(duration, words, 2);

    Xunit.Assert.Equal(2, ReadingPlaybackTiming.GetWordIndex(position, duration, words));
  }

  [Xunit.Fact]
  public void LanguageRegistry_ExposesAllSupportedReaderLanguages()
  {
    Xunit.Assert.Equal(33, ReaderLanguageRegistry.Languages.Count);
    Xunit.Assert.Equal("amit", ReaderLanguageRegistry.Languages[0].DefaultVoice.Id);
    Xunit.Assert.Equal(3, ReaderLanguageRegistry.Languages.Count(language => language.IsExperimental));
    Xunit.Assert.Contains(ReaderLanguageRegistry.Languages, language => language.Code == "pt-br");
    Xunit.Assert.Contains(ReaderLanguageRegistry.Languages, language => language.Code == "ne" && language.DefaultVoice.Speaker == "Amrita");
    Xunit.Assert.Contains(ReaderLanguageRegistry.Languages, language => language.Code == "sa" && language.DefaultVoice.Speaker == "Aryan");
    Xunit.Assert.Contains(ReaderLanguageRegistry.Languages, language => language.Code == "ks" && language.IsExperimental);
    ReaderLanguageOption kokoroEnglish = Xunit.Assert.Single(ReaderLanguageRegistry.Languages, language => language.Code == "en-us");
    ReaderLanguageOption parlerEnglish = Xunit.Assert.Single(ReaderLanguageRegistry.Languages, language => language.Code == "en");
    Xunit.Assert.Contains("Kokoro", kokoroEnglish.DisplayName, StringComparison.Ordinal);
    Xunit.Assert.Contains("Indic Parler", parlerEnglish.DisplayName, StringComparison.Ordinal);
    Xunit.Assert.Same(kokoroEnglish, ReaderLanguageRegistry.DefaultLanguage);
    Xunit.Assert.Equal("af_bella", kokoroEnglish.DefaultVoice.Id);
    Xunit.Assert.Contains("Bella", kokoroEnglish.DefaultVoice.DisplayName, StringComparison.Ordinal);
    Xunit.Assert.Contains("warm, expressive", kokoroEnglish.DefaultVoice.DisplayName, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("Kokoro", kokoroEnglish.DefaultVoice.DisplayName, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.All(parlerEnglish.Voices, voice =>
      Xunit.Assert.DoesNotContain("Indic Parler", voice.DisplayName, StringComparison.OrdinalIgnoreCase));
    Xunit.Assert.NotEqual(kokoroEnglish.ProviderId, parlerEnglish.ProviderId);
    Xunit.Assert.Equal(54, ReaderLanguageRegistry.Languages
      .Where(language => language.ProviderId == KokoroTextToSpeechService.ProviderId)
      .Sum(language => language.Voices.Count));
    Xunit.Assert.Contains(ReaderLanguageRegistry.Languages, language =>
      language.Code == "hi" && language.ProviderId == KokoroTextToSpeechService.ProviderId && language.Voices.Count == 4);
  }

  [Xunit.Fact]
  public void ReaderAppearance_OffersFocusedViewsAndColorIndependentHighlights()
  {
    Xunit.Assert.Contains(ReaderViewOption.Defaults, option => option.Mode == ReaderViewMode.OneSentence);
    Xunit.Assert.Contains(ReaderViewOption.Defaults, option => option.Mode == ReaderViewMode.OneWord);
    Xunit.Assert.Contains(ReaderHighlightVisualOption.Defaults, option => option.Style == ReaderHighlightVisualStyle.FocusRing);
    Xunit.Assert.Contains(ReaderHighlightVisualOption.Defaults, option => option.Style == ReaderHighlightVisualStyle.Spotlight);
    Xunit.Assert.Contains(ReaderHighlightVisualOption.Defaults, option => option.Style == ReaderHighlightVisualStyle.BoldFocus);
    Xunit.Assert.Contains(ReaderHighlightVisualOption.Defaults, option => option.Style == ReaderHighlightVisualStyle.ReaderPage);
    Xunit.Assert.True(ReaderHighlightColorOption.Defaults.Count >= 6);
    Xunit.Assert.Contains(ReaderTypographyCatalog.Options, option => option.Id == ReaderFontIds.DevanagariLiterary);
    Xunit.Assert.Contains(ReaderTypographyCatalog.Options, option => option.Id == ReaderFontIds.DevanagariHandDrawn);
  }

  [Xunit.Fact]
  public void ExactWordTimingMap_UsesAudioTimestampsAndCoversPunctuation()
  {
    bool created = ReaderWordTimingMap.TryCreate(
      ["Hello", ",", "world", "!"],
      [
        new SpeechWordTiming("Hello", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.7)),
        new SpeechWordTiming("world", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.8)),
      ],
      TimeSpan.FromSeconds(2),
      ReaderWordTimingSource.ForcedAlignment,
      out ReaderWordTimingMap? map);

    Xunit.Assert.True(created);
    Xunit.Assert.NotNull(map);
    Xunit.Assert.Equal(0, map.GetWordIndex(TimeSpan.FromSeconds(0.3)));
    Xunit.Assert.Equal(2, map.GetWordIndex(TimeSpan.FromSeconds(1.4)));
    Xunit.Assert.Equal(TimeSpan.FromSeconds(1), map.GetStart(2));
    Xunit.Assert.Equal(1d, map.GetProgressForBoundary(4, TimeSpan.FromSeconds(2)));
  }

  [Xunit.Fact]
  public void ExactWordTimingMap_SplitsAnAlignedCjkUnitForWordLevelFollowAlong()
  {
    bool created = ReaderWordTimingMap.TryCreate(
      ["你", "好", "世", "界", "。"],
      [new SpeechWordTiming("你好世界", TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(2.4))],
      TimeSpan.FromSeconds(2.6),
      ReaderWordTimingSource.ForcedAlignment,
      out ReaderWordTimingMap? map);

    Xunit.Assert.True(created);
    Xunit.Assert.NotNull(map);
    Xunit.Assert.Equal(TimeSpan.FromSeconds(1.4), map.GetStart(2));
    Xunit.Assert.Equal(3, map.GetWordIndex(TimeSpan.FromSeconds(2.1)));
  }

  [Xunit.Fact]
  public void ExactWordTimingMap_JoinsHindiWordsSplitByInlinePunctuation()
  {
    bool created = ReaderWordTimingMap.TryCreate(
      ["ठीक-ठीक", "कहें", "।"],
      [
        new SpeechWordTiming("ठीक", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.7)),
        new SpeechWordTiming("ठीक", TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.3)),
        new SpeechWordTiming("कहें", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.0)),
      ],
      TimeSpan.FromSeconds(2.2),
      ReaderWordTimingSource.ForcedAlignment,
      out ReaderWordTimingMap? map);

    Xunit.Assert.True(created);
    Xunit.Assert.NotNull(map);
    Xunit.Assert.Equal(TimeSpan.FromSeconds(0.2), map.GetStart(0));
    Xunit.Assert.Equal(TimeSpan.FromSeconds(1.5), map.GetStart(1));
    Xunit.Assert.Equal(0, map.GetWordIndex(TimeSpan.FromSeconds(1.0)));
  }

  [Xunit.Fact]
  public void SemanticTitleNarration_InsertsAPauseBeforeTheOpeningSentence()
  {
    ReadingDocument document = ReadingTextLayout.Create(
      "Stories",
      [new ReadableDocumentSection("The Clever Fox", "A fox once lived beside a forest.")]);

    string narration = ReaderNarrationText.Create(document.Sections[0]);

    Xunit.Assert.StartsWith("The Clever Fox.\nA fox", narration, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void EditableDocument_PreservesKnownTitlesAndDetectsAnEditedTitle()
  {
    ReadingDocument baseline = ReadingTextLayout.Create(
      "Stories",
      [
        new ReadableDocumentSection("Old Fox Title", "The first story has a complete sentence."),
        new ReadableDocumentSection("Second Story", "The second story also has a complete sentence."),
      ]);
    string edited = "New Fox Title\n\nThe first story has a complete sentence with more detail.\n\nSecond Story\n\nThe second story also has a complete sentence.";

    ReadingDocument document = ReaderEditableDocumentBuilder.Create("Stories", edited, baseline);

    Xunit.Assert.Equal(2, document.Sections.Count);
    Xunit.Assert.Equal("New Fox Title", document.Sections[0].Title);
    Xunit.Assert.Equal("Second Story", document.Sections[1].Title);
  }

  [Xunit.Fact]
  public void Registry_AssignsComplexMetricsToScriptsWithTallOrJoinedGlyphs()
  {
    ReaderLanguageOption sanskrit = Xunit.Assert.Single(
      ReaderLanguageRegistry.Languages,
      language => language.Code == "sa");
    ReaderLanguageOption marathi = Xunit.Assert.Single(
      ReaderLanguageRegistry.Languages,
      language => language.Code == "mr");
    ReaderLanguageOption english = Xunit.Assert.Single(
      ReaderLanguageRegistry.Languages,
      language => language.Code == "en-us");
    ReaderLanguageOption tamil = Xunit.Assert.Single(
      ReaderLanguageRegistry.Languages,
      language => language.Code == "ta");
    ReaderLanguageOption urdu = Xunit.Assert.Single(
      ReaderLanguageRegistry.Languages,
      language => language.Code == "ur");

    Xunit.Assert.Equal(ReaderLineMetricsProfile.ComplexScript, sanskrit.Capability.Text.LineMetrics);
    Xunit.Assert.Equal(ReaderLineMetricsProfile.ComplexScript, marathi.Capability.Text.LineMetrics);
    Xunit.Assert.Equal(ReaderLineMetricsProfile.ComplexScript, tamil.Capability.Text.LineMetrics);
    Xunit.Assert.Equal(ReaderLineMetricsProfile.ComplexScript, urdu.Capability.Text.LineMetrics);
    Xunit.Assert.Equal(ReaderLineMetricsProfile.Standard, english.Capability.Text.LineMetrics);
  }

  [Xunit.Fact]
  public void KokoroTimingFallback_CoversTheCompleteSectionWithoutAnExternalAligner()
  {
    string[] words = ["The", "Clever", "Fox", ".", "Once", "upon", "a", "time."];
    TimeSpan duration = TimeSpan.FromSeconds(4);

    IReadOnlyList<SpeechWordTiming> timings = ReaderTimingResolver.CreateDeterministicWordTimings(words, duration);
    bool created = ReaderWordTimingMap.TryCreate(
      words,
      timings,
      duration,
      ReaderWordTimingSource.DeterministicEstimate,
      out ReaderWordTimingMap? map);

    Xunit.Assert.True(created);
    Xunit.Assert.NotNull(map);
    Xunit.Assert.Equal(words.Length, map!.Words.Count);
    Xunit.Assert.Equal(duration, map.Words[^1].End);
    Xunit.Assert.Equal(ReaderWordTimingSource.DeterministicEstimate, map.Source);
    Xunit.Assert.False(map.IsAudioGrounded);
  }
}
