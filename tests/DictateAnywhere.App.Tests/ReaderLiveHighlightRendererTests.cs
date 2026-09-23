using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class ReaderLiveHighlightRendererTests
{
  [Fact]
  public void Apply_UsesWeightForComplexScriptSentenceEmphasis()
  {
    RunOnSta(() =>
    {
      ReaderWordVisual[] visuals = CreateVisuals(["नमस्ते", "दुनिया", "।"]);

      ReaderLiveHighlightRenderer.Apply(
        visuals,
        visuals.Select(visual => visual.Text.Text).ToArray(),
        Brushes.Black,
        activeWordIndex: 0,
        ReadingHighlightMode.Sentence,
        ReaderHighlightVisualStyle.Underline,
        "#4D9FFF",
        preferWeightOverUnderline: true);

      Assert.All(visuals, visual =>
      {
        Assert.Equal(FontWeights.Bold, visual.Text.FontWeight);
        Assert.Null(visual.Text.TextDecorations);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#4D9FFF"), ((SolidColorBrush)visual.Text.Foreground).Color);
      });
    });
  }

  [Fact]
  public void Apply_LeavesSentenceSurfacesTransparentAndResetsEveryMutableProperty()
  {
    RunOnSta(() =>
    {
      ReaderWordVisual[] visuals = CreateVisuals(["Read", "this", "."]);

      ReaderLiveHighlightRenderer.Apply(
        visuals,
        visuals.Select(visual => visual.Text.Text).ToArray(),
        Brushes.Black,
        activeWordIndex: 0,
        ReadingHighlightMode.Sentence,
        ReaderHighlightVisualStyle.ReaderPage,
        "#D4A94F",
        preferWeightOverUnderline: false);

      Assert.All(visuals, visual => Assert.Same(Brushes.Transparent, visual.Surface.Background));
      Assert.All(visuals.Where(visual => visual.TrailingSeparator is not null),
        visual => Assert.Same(Brushes.Transparent, visual.TrailingSeparator!.Background));

      ReaderLiveHighlightRenderer.Apply(
        visuals,
        visuals.Select(visual => visual.Text.Text).ToArray(),
        Brushes.Black,
        activeWordIndex: 0,
        ReadingHighlightMode.Off,
        ReaderHighlightVisualStyle.ReaderPage,
        "#D4A94F",
        preferWeightOverUnderline: false);

      Assert.All(visuals, visual =>
      {
        Assert.Same(Brushes.Transparent, visual.Surface.Background);
        Assert.Same(Brushes.Transparent, visual.Surface.BorderBrush);
        Assert.Equal(1d, visual.Surface.Opacity);
        Assert.Equal(visual.DefaultCornerRadius, visual.Surface.CornerRadius);
        Assert.Equal(visual.DefaultFontWeight, visual.Text.FontWeight);
        Assert.Null(visual.Text.TextDecorations);
      });
      Assert.All(visuals.Where(visual => visual.TrailingSeparator is not null),
        visual => Assert.Same(Brushes.Transparent, visual.TrailingSeparator!.Background));
    });
  }

  [Fact]
  public void Apply_UsesSolidContrastFillAndDimsOnlySpotlightSurroundings()
  {
    RunOnSta(() =>
    {
      ReaderWordVisual[] visuals = CreateVisuals(["Read", "this"]);

      ReaderLiveHighlightRenderer.Apply(
        visuals,
        visuals.Select(visual => visual.Text.Text).ToArray(),
        Brushes.White,
        activeWordIndex: 1,
        ReadingHighlightMode.Word,
        ReaderHighlightVisualStyle.AccentFill,
        "#4D9FFF",
        preferWeightOverUnderline: false);

      Assert.Equal(Colors.Transparent, ((SolidColorBrush)visuals[0].Surface.Background).Color);
      Assert.Equal((Color)ColorConverter.ConvertFromString("#4D9FFF"), ((SolidColorBrush)visuals[1].Surface.Background).Color);
      Assert.Equal(Color.FromRgb(15, 23, 42), ((SolidColorBrush)visuals[1].Text.Foreground).Color);

      ReaderLiveHighlightRenderer.Apply(
        visuals,
        visuals.Select(visual => visual.Text.Text).ToArray(),
        Brushes.White,
        activeWordIndex: 1,
        ReadingHighlightMode.Word,
        ReaderHighlightVisualStyle.Spotlight,
        "#4D9FFF",
        preferWeightOverUnderline: false);

      Assert.Equal(0.72d, visuals[0].Surface.Opacity);
      Assert.Equal(1d, visuals[1].Surface.Opacity);
      Assert.Same(TextDecorations.Underline, visuals[1].Text.TextDecorations);
    });
  }

  [Fact]
  public void Apply_LeavesRightToLeftSentenceSurfacesForLineRenderer()
  {
    RunOnSta(() =>
    {
      ReaderWordVisual[] visuals = CreateVisuals(["اردو", "متن", "۔"]);
      Assert.All(visuals, visual => visual.Text.FlowDirection = FlowDirection.RightToLeft);

      ReaderLiveHighlightRenderer.Apply(
        visuals,
        visuals.Select(visual => visual.Text.Text).ToArray(),
        Brushes.Black,
        activeWordIndex: 0,
        ReadingHighlightMode.Sentence,
        ReaderHighlightVisualStyle.ReaderPage,
        "#D4A94F",
        preferWeightOverUnderline: true);

      Assert.All(visuals, visual => Assert.Same(Brushes.Transparent, visual.Surface.Background));
    });
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public void SentenceBands_ConnectSpacesWrapAndResetWithoutMovingWords(bool centered, bool rtl)
  {
    RunOnSta(() =>
    {
      ReaderHighlightTextBlock block = new()
      {
        FontSize = 24, TextWrapping = TextWrapping.Wrap,
        TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
        FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
      };
      ReaderWordVisual[] visuals = CreateVisuals(rtl
        ? ["مرحبا", "هذا", "نص", "للقراءة", "بصوت", "واضح."]
        : ["Hello", "this", "sentence", "wraps", "across", "lines."]);
      foreach (ReaderWordVisual visual in visuals)
      {
        visual.Surface.Padding = new Thickness(2, 3, 2, 4);
        visual.Surface.BorderThickness = new Thickness(2);
        block.Inlines.Add(new InlineUIContainer(visual.Surface) { BaselineAlignment = BaselineAlignment.Baseline });
        if (visual.TrailingSeparator is not null) block.Inlines.Add(visual.TrailingSeparator);
      }
      Grid host = new() { FlowDirection = FlowDirection.LeftToRight };
      host.Children.Add(block);
      void Layout(double width)
      {
        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, width, host.DesiredSize.Height));
        host.UpdateLayout();
      }
      void Apply(ReadingHighlightMode mode) => ReaderLiveHighlightRenderer.Apply(visuals,
        visuals.Select(v => v.Text.Text).ToArray(), Brushes.Black, 0, mode,
        ReaderHighlightVisualStyle.AccentFill, "#40BB8A", false, block);
      Layout(230);
      Point[] before = visuals.Select(v => v.Surface.TranslatePoint(new Point(), block)).ToArray();
      Apply(ReadingHighlightMode.Sentence);
      Layout(230);
      Assert.True(block.HighlightBands.Count >= 2);
      foreach (ReaderWordVisual visual in visuals)
      {
        Rect bounds = visual.Surface.TransformToAncestor(block).TransformBounds(new Rect(visual.Surface.RenderSize));
        Assert.Contains(block.HighlightBands, band => band.Contains(bounds));
      }
      Assert.Equal(before, visuals.Select(v => v.Surface.TranslatePoint(new Point(), block)).ToArray());
      // Pixel check through the gap between inline controls: the sentence fill must cover it continuously.
      var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(230, (int)Math.Ceiling(block.ActualHeight),
        96, 96, PixelFormats.Pbgra32);
      bitmap.Render(host);
      string? gallery = Environment.GetEnvironmentVariable("NOTYPE_UI_GALLERY");
      if (!string.IsNullOrEmpty(gallery))
      {
        System.IO.Directory.CreateDirectory(gallery);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(gallery, $"sentence-{centered}-{rtl}.png"));
        encoder.Save(file);
      }
      byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
      bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
      foreach (Rect localBand in block.HighlightBands)
      {
        Rect band = block.TransformToAncestor(host).TransformBounds(localBand);
        int y = (int)Math.Ceiling(band.Top + 2);
        for (int x = (int)Math.Ceiling(band.Left + 6); x < Math.Floor(band.Right - 6); x++)
          Assert.True(pixels[(y * bitmap.PixelWidth + x) * 4 + 3] > 0, $"Gap at {x},{y}");
      }
      Layout(900);
      Assert.Single(block.HighlightBands);
      Apply(ReadingHighlightMode.Off);
      Assert.Empty(block.HighlightBands);
    });
  }

  [Theory]
  [InlineData(false, 29)]
  [InlineData(true, 29)]
  [InlineData(false, 42)]
  [InlineData(true, 42)]
  public void DocumentView_SentenceHighlightFollowsActualPageLayout(bool focused, double fontSize)
  {
    RunOnSta(() =>
    {
      using ReaderSidebarView sidebar = new();
      using ReaderDocumentView view = new();
      var selection = sidebar.Selection;
      var document = ReadingTextLayout.Create("Sentence fixture",
        "Hello! When I woke up this morning, a gentle rain was falling outside my window, and a cool breeze was softly moving the green leaves on the trees. In the distance, I could hear a bell.");
      view.RenderSurface(focused ? ReaderDocumentSurface.FocusedSentence : ReaderDocumentSurface.FullPage);
      view.RenderSection(document, 0, 1, new ReaderDocumentAppearance(selection.Font, fontSize, selection.Theme,
        ReadingHighlightMode.Sentence, ReaderHighlightVisualStyle.AccentFill, selection.HighlightColor,
        selection.Typography, selection.Language.Capability.Text.Direction));
      void Layout()
      {
        view.Measure(new Size(1000, 900));
        view.Arrange(new Rect(0, 0, 1000, 900));
        view.UpdateLayout();
      }
      Layout();
      var block = (ReaderHighlightTextBlock)view.FindName(focused ? "FocusedReadingTextBlock" : "ReaderTextBlock");
      Assert.True(block.HighlightBands.Count > 1);
      Rect[] original = block.HighlightBands.ToArray();
      view.UpdateHighlight(3);
      Layout();
      Assert.Equal(original, block.HighlightBands.ToArray());
      string? gallery = Environment.GetEnvironmentVariable("NOTYPE_UI_GALLERY");
      if (!string.IsNullOrEmpty(gallery))
      {
        System.IO.Directory.CreateDirectory(gallery);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, 900, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(gallery, $"page-{focused}-{fontSize}.png"));
        encoder.Save(file);
      }
      view.UpdateHighlight(0);
      Layout();
      Assert.Single(block.HighlightBands);
      Assert.NotEqual(original, block.HighlightBands.ToArray());
    });
  }

  [Fact]
  public void FocusedUnderline_FollowsTheSpokenTokenWithoutReplacingTheSentence()
  {
    RunOnSta(() =>
    {
      using ReaderSidebarView sidebar = new();
      using ReaderDocumentView view = new();
      var selection = sidebar.Selection;
      view.RenderSurface(ReaderDocumentSurface.FocusedSentence);
      view.RenderSection(ReadingTextLayout.Create("Test", "One two three. Next sentence."), 0, 0,
        new ReaderDocumentAppearance(selection.Font, 29, selection.Theme, ReadingHighlightMode.Sentence,
          ReaderHighlightVisualStyle.Underline, selection.HighlightColor, selection.Typography,
          selection.Language.Capability.Text.Direction));
      view.Measure(new Size(1000, 900));
      view.Arrange(new Rect(0, 0, 1000, 900));
      view.UpdateLayout();
      var block = (TextBlock)view.FindName("FocusedReadingTextBlock");
      TextBlock[] words = Descendants(block).OfType<TextBlock>().ToArray();
      Assert.Equal(3, words.Length);
      Assert.Single(words.Where(word => word.TextDecorations is { Count: > 0 }));
      view.UpdateHighlight(1);
      Assert.Equal("two", Assert.Single(words.Where(word => word.TextDecorations is { Count: > 0 })).Text);
      view.UpdateHighlight(-1);
      Assert.Empty(words.Where(word => word.TextDecorations is { Count: > 0 }));
    });
  }

  private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
  {
    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
    {
      var child = VisualTreeHelper.GetChild(parent, index);
      yield return child;
      foreach (var descendant in Descendants(child)) yield return descendant;
    }
  }

  [Theory]
  [InlineData("یہ پہلا جملہ ہے۔ یہ دوسرا جملہ ہے۔", 4)]
  [InlineData("Is 3.14 correct? Yes it is.", 3)]
  [InlineData("Visit example.com today. Next sentence.", 3)]
  public void SentenceBoundaries_IgnoreInternalPeriodsAndRecognizeUrdu(string text, int next)
  {
    var words = UnicodeReadingTextSegmenter.Segment(text).Tokens.Select(token => token.Text).ToArray();
    Assert.Equal((0, next - 1), ReadingPlaybackTiming.GetSentenceRange(words, 0));
    Assert.Equal(next, ReadingPlaybackTiming.GetSentenceRange(words, next).Start);
  }

  [Fact]
  public void SourceSeparatorsAndLongFocusedSentencesRemainReachable()
  {
    RunOnSta(() =>
    {
      using ReaderSidebarView sidebar = new();
      using ReaderDocumentView view = new();
      var selection = sidebar.Selection;
      var section = new ReadingSection(0, UnicodeReadingTextSegmenter.Segment("One\u00a0two\u202fthree " + string.Join(" ", Enumerable.Repeat("long", 100)) + "."));
      view.RenderSurface(ReaderDocumentSurface.FocusedSentence);
      view.RenderSection(new ReadingDocument("Test", [section]), 0, 0,
        new ReaderDocumentAppearance(selection.Font, 42, selection.Theme, ReadingHighlightMode.Sentence,
          ReaderHighlightVisualStyle.AccentFill, selection.HighlightColor, selection.Typography,
          selection.Language.Capability.Text.Direction));
      view.Measure(new Size(760, 600));
      view.Arrange(new Rect(0, 0, 760, 600));
      view.UpdateLayout();
      var block = (TextBlock)view.FindName("FocusedReadingTextBlock");
      Assert.Contains("\u00a0", new TextRange(block.ContentStart, block.ContentEnd).Text);
      Assert.Contains("\u202f", new TextRange(block.ContentStart, block.ContentEnd).Text);
      Assert.Contains(Descendants(view).OfType<ScrollViewer>(), scroll => scroll.ScrollableHeight > 0);
    });
  }

  public static IEnumerable<object[]> LanguageCases => ReaderLanguageRegistry.Languages.Select(language =>
    new object[] { language.ProviderId, language.Code });

  private static readonly IReadOnlyDictionary<string, string> LanguageSamples = new Dictionary<string, string>
  {
    ["en"] = "In the distance, I could hear the quiet ringing of a bell, while people walked along the street carrying bright and colorful umbrellas.", ["en-us"] = "The morning is quiet.", ["en-gb"] = "The morning is quiet.",
    ["es"] = "La mañana es tranquila.", ["fr"] = "Le matin est calme.", ["it"] = "La mattina è tranquilla.",
    ["pt-br"] = "A manhã está tranquila.", ["zh"] = "今天早上很安静。", ["ja"] = "今朝は静かです。",
    ["as"] = "আজি বতৰ ভাল।", ["bn"] = "আজ আবহাওয়া ভালো।", ["brx"] = "दिनै बोथोर मोजां।",
    ["doi"] = "अज्ज मौसम अच्छा ऐ।", ["gu"] = "આજે હવામાન સારું છે.", ["hi"] = "आज मौसम अच्छा है।",
    ["kn"] = "ಇಂದು ಹವಾಮಾನ ಚೆನ್ನಾಗಿದೆ.", ["kok"] = "आयज हवामान बरें आसा।", ["mai"] = "आइ मौसम नीक अछि।",
    ["ml"] = "ഇന്ന് നല്ല കാലാവസ്ഥയാണ്.", ["mni"] = "নুংঙাইবা নুমিত অমনি।", ["mr"] = "आज हवामान चांगले आहे।",
    ["ne"] = "आज मौसम राम्रो छ।", ["or"] = "ଆଜି ପାଗ ଭଲ ଅଛି।", ["sa"] = "अद्य वातावरणं सुन्दरम् अस्ति।",
    ["sat"] = "ᱟᱡ ᱢᱮᱱᱟᱭᱟ.", ["sd"] = "اڄ موسم سٺي آهي۔", ["ta"] = "இன்று வானிலை நன்றாக உள்ளது.",
    ["te"] = "ఈ రోజు వాతావరణం బాగుంది.", ["ur"] = "آج موسم اچھا ہے۔", ["hne"] = "आज मौसम बने हे।",
    ["ks"] = "اَز چھُ موسم اَصٕل۔", ["pa"] = "ਅੱਜ ਮੌਸਮ ਚੰਗਾ ਹੈ।",
  };

  [Theory]
  [MemberData(nameof(LanguageCases))]
  public void EveryRegisteredLanguage_HasStableSourceSentenceAndEmphasisMappings(string provider, string code)
  {
    var language = ReaderLanguageRegistry.Languages.Single(item => item.Code == code && item.ProviderId == provider);
    Assert.True(LanguageSamples.TryGetValue(code, out string? sample), $"Missing fixture: {provider}/{code}");
    var section = new ReadingSection(0, UnicodeReadingTextSegmenter.Segment(sample + " " + sample,
      language.Capability.Text.Direction));
    int next = section.Tokens.ToList().FindIndex(token => token.SourceStart > sample!.Length);
    Assert.True(next > 0);
    Assert.Equal((0, next - 1), ReadingPlaybackTiming.GetSentenceRange(section, 0));
    Assert.Equal(next, ReadingPlaybackTiming.GetSentenceRange(section, next).Start);
    foreach (ReadingToken token in section.Tokens)
      Assert.Equal(token.Text, section.Text.Substring(token.SourceStart, token.SourceLength));
    foreach (ReaderHighlightVisualStyle style in Enum.GetValues<ReaderHighlightVisualStyle>())
    {
      var expected = style is ReaderHighlightVisualStyle.ReaderPage or ReaderHighlightVisualStyle.AccentFill
        ? ReadingHighlightMode.Sentence : ReadingHighlightMode.Word;
      Assert.Equal(expected, ReaderHighlightPlan.ResolveEmphasisMode(ReaderDocumentSurface.FocusedSentence,
        ReadingHighlightMode.Sentence, style));
      Assert.Equal(ReadingHighlightMode.Sentence, ReaderHighlightPlan.ResolveEmphasisMode(ReaderDocumentSurface.FullPage,
        ReadingHighlightMode.Sentence, style));
    }
  }

  public static IEnumerable<object[]> ScriptRenderCases => ReaderLanguageRegistry.Languages
    .GroupBy(language => language.Capability.Text.Script).Select(group => group.First())
    .SelectMany(language => new[] { false, true }.SelectMany(focused =>
      new[] { ReaderHighlightVisualStyle.Underline, ReaderHighlightVisualStyle.FocusRing, ReaderHighlightVisualStyle.Spotlight }
        .Select(style => new object[] { language.Code, focused, (int)style })));

  [Theory]
  [MemberData(nameof(ScriptRenderCases))]
  public void ScriptStyles_RenderAndAdvanceWithoutLayoutChanges(string code, bool focused, int styleValue)
  {
    RunOnSta(() =>
    {
      var language = ReaderLanguageRegistry.Languages.First(item => item.Code == code);
      using ReaderSidebarView sidebar = new();
      using ReaderDocumentView view = new();
      var selection = sidebar.Selection;
      var style = (ReaderHighlightVisualStyle)styleValue;
      string sample = LanguageSamples[code];
      var section = new ReadingSection(0, UnicodeReadingTextSegmenter.Segment(sample + " " + sample,
        language.Capability.Text.Direction));
      view.RenderSurface(focused ? ReaderDocumentSurface.FocusedSentence : ReaderDocumentSurface.FullPage);
      view.RenderSection(new ReadingDocument("Script test", [section]), 0, 0,
        new ReaderDocumentAppearance(selection.Font with { FontFamilyName = "Segoe UI" }, 37, ReaderThemeOption.Defaults[0],
          ReadingHighlightMode.Sentence, style, selection.HighlightColor,
          ReaderTypographyCatalog.GetMetrics(language.Capability.Text.LineMetrics), language.Capability.Text.Direction));
      void Layout()
      {
        view.Measure(new Size(1000, 900));
        view.Arrange(new Rect(0, 0, 1000, 900));
        view.UpdateLayout();
      }
      Layout();
      var block = (ReaderHighlightTextBlock)view.FindName(focused ? "FocusedReadingTextBlock" : "ReaderTextBlock");
      var words = Descendants(block).OfType<TextBlock>().ToArray();
      Point[] positions = words.Select(word => word.TranslatePoint(new Point(), block)).ToArray();
      for (int active = 0; active < Math.Min(2, section.SentenceRanges[0].End + 1); active++)
      {
        view.UpdateHighlight(active);
        Layout();
        Assert.Equal(positions, words.Select(word => word.TranslatePoint(new Point(), block)).ToArray());
        if (focused && style == ReaderHighlightVisualStyle.FocusRing)
          Assert.Single(Descendants(block).OfType<Border>().Where(border => border.BorderBrush != Brushes.Transparent));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, 900, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        string? gallery = Environment.GetEnvironmentVariable("NOTYPE_UI_GALLERY");
        if (!string.IsNullOrEmpty(gallery))
        {
          System.IO.Directory.CreateDirectory(gallery);
          var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
          encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
          using var file = System.IO.File.Create(System.IO.Path.Combine(gallery, $"style-{code}-{focused}-{style}-{active}.png"));
          encoder.Save(file);
        }
      }
    });
  }

  [Fact]
  public void AccessibleDecorations_HaveContrastAcrossEveryThemeAndAccent()
  {
    foreach (var theme in ReaderThemeOption.Defaults)
    foreach (var accent in ReaderHighlightColorOption.Defaults)
    {
      var background = (Color)ColorConverter.ConvertFromString(theme.PageColor);
      var ink = (Color)ColorConverter.ConvertFromString(theme.InkColor);
      Color result = ReaderHighlightContrast.Resolve((Color)ColorConverter.ConvertFromString(accent.HexColor), background, ink);
      Assert.True(ReaderHighlightContrast.Ratio(result, background) >= 3);
    }
  }

  [Theory]
  [InlineData("Dr. Smith paid 3.14 today. Next.", 5)]
  [InlineData("Open report.txt now. Next.", 3)]
  [InlineData("کیا حال ہے؟ سب ٹھیک ہے۔", 3)]
  [InlineData("Wait... Next.", 1)]
  public void AdditionalSentenceBoundaries(string text, int next)
  {
    var section = new ReadingSection(0, UnicodeReadingTextSegmenter.Segment(text));
    Assert.Equal((0, next - 1), ReadingPlaybackTiming.GetSentenceRange(section, 0));
    Assert.Equal(next, ReadingPlaybackTiming.GetSentenceRange(section, next).Start);
  }

  [Fact]
  public void ParagraphAndTitleBoundariesDoNotNeedTerminalPunctuation()
  {
    var section = new ReadingSection(0, UnicodeReadingTextSegmenter.Segment("Title\n\nFirst paragraph\n\nSecond paragraph"), "Title", 1);
    Assert.Equal((0, 0), ReadingPlaybackTiming.GetSentenceRange(section, 0));
    Assert.Equal((1, 2), ReadingPlaybackTiming.GetSentenceRange(section, 1));
    Assert.Equal((3, 4), ReadingPlaybackTiming.GetSentenceRange(section, 3));
  }

  [Fact]
  public void SectionChunkingPreservesInternalPeriodsAndNonbreakingSpaces()
  {
    string text = string.Join(" ", Enumerable.Repeat("Open report.txt with 3.14 and one\u00a0two.", 30));
    var document = ReadingTextLayout.Create("Test", text, 240);
    Assert.Equal(30, document.Sections.SelectMany(section => section.Words).Count(word => word == "report.txt"));
    Assert.Equal(30, document.Sections.SelectMany(section => section.Words).Count(word => word == "3.14"));
    Assert.Equal(30, document.Sections.Sum(section => section.Text.Count(character => character == '\u00a0')));
  }

  [Theory]
  [InlineData("你好。下一句。", 2, "你")]
  [InlineData("𠀀 next.", 0, "𠀀")]
  public void FocusedSentenceKeepsPunctuationOwnershipAndSupplementaryLetters(string text, int active, string expectedFirst)
  {
    RunOnSta(() =>
    {
      using ReaderSidebarView sidebar = new();
      using ReaderDocumentView view = new();
      var selection = sidebar.Selection;
      view.RenderSurface(ReaderDocumentSurface.FocusedSentence);
      view.RenderSection(ReadingTextLayout.Create("Test", text), 0, active,
        new ReaderDocumentAppearance(selection.Font, 29, selection.Theme, ReadingHighlightMode.Sentence,
          ReaderHighlightVisualStyle.FocusRing, selection.HighlightColor, selection.Typography,
          selection.Language.Capability.Text.Direction));
      view.Measure(new Size(1000, 900));
      view.Arrange(new Rect(0, 0, 1000, 900));
      view.UpdateLayout();
      var block = (TextBlock)view.FindName("FocusedReadingTextBlock");
      Assert.Equal(expectedFirst, Descendants(block).OfType<TextBlock>().First().Text);
      if (active == 0)
        Assert.Equal(expectedFirst, Assert.Single(Descendants(block).OfType<Border>()
          .Where(border => border.BorderBrush != Brushes.Transparent)).Child is TextBlock word ? word.Text : "");
    });
  }

  private static ReaderWordVisual[] CreateVisuals(IReadOnlyList<string> words)
  {
    return words.Select((word, index) =>
    {
      TextBlock text = new()
      {
        Text = word,
        FontWeight = FontWeights.Normal,
        FontSize = 24,
        Foreground = Brushes.Black,
      };
      Run? separator = index < words.Count - 1 ? new Run(" ") : null;
      return new ReaderWordVisual(
        index,
        new Border
        {
          Child = text,
          Background = Brushes.Transparent,
          BorderBrush = Brushes.Transparent,
          CornerRadius = new CornerRadius(5),
        },
        text,
        separator,
        new CornerRadius(5),
        FontWeights.Normal,
        24);
    }).ToArray();
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The helper must transfer any WPF-thread assertion or construction failure to the test thread.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
      {
        action();
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(15));
    Assert.True(completed, "STA test thread timed out.");

    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
  }
}
