using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DictateAnywhere.App.Workbench.Reading;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderTypographyCatalogTests
{
  [Xunit.Fact]
  public void EveryFontProfile_HasACompatibleDefaultAndAtLeastOneChoice()
  {
    foreach (ReaderFontProfile profile in Enum.GetValues<ReaderFontProfile>())
    {
      IReadOnlyList<ReaderFontOption> options = ReaderTypographyCatalog.GetOptions(profile);
      ReaderFontOption defaultOption = ReaderTypographyCatalog.GetDefault(profile);

      Xunit.Assert.NotEmpty(options);
      Xunit.Assert.Contains(defaultOption, options);
      Xunit.Assert.All(options, option => Xunit.Assert.True(option.Supports(profile)));
    }
  }

  [Xunit.Fact]
  public void FontOptions_HaveStableUniqueIds()
  {
    Xunit.Assert.Equal(
      ReaderTypographyCatalog.Options.Count,
      ReaderTypographyCatalog.Options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count());
    Xunit.Assert.All(ReaderTypographyCatalog.Options, option =>
    {
      Xunit.Assert.False(string.IsNullOrWhiteSpace(option.Id));
      Xunit.Assert.False(string.IsNullOrWhiteSpace(option.FontFamilyName));
      Xunit.Assert.NotEmpty(option.SupportedProfiles);
    });
  }

  [Xunit.Fact]
  public void EveryReaderLanguage_ResolvesToACompatibleFontAndMetricsProfile()
  {
    Xunit.Assert.All(ReaderLanguageRegistry.Languages, language =>
    {
      ReaderScriptProfile text = language.Capability.Text;
      Xunit.Assert.True(ReaderTypographyCatalog.GetDefault(text.Font).Supports(text.Font));
      Xunit.Assert.NotNull(ReaderTypographyCatalog.GetMetrics(text.LineMetrics));
    });
  }

  [Xunit.Fact]
  public void ComplexScriptMetrics_ReserveMoreVerticalSpaceAndAvoidUnderlines()
  {
    ReaderTypographyMetrics standard = ReaderTypographyCatalog.GetMetrics(ReaderLineMetricsProfile.Standard);
    ReaderTypographyMetrics complex = ReaderTypographyCatalog.GetMetrics(ReaderLineMetricsProfile.ComplexScript);

    Xunit.Assert.True(complex.PageLineHeightScale > standard.PageLineHeightScale);
    Xunit.Assert.True(complex.FocusedWordLineHeightScale > standard.FocusedWordLineHeightScale);
    Xunit.Assert.True(complex.FocusedSentenceLineHeightScale > standard.FocusedSentenceLineHeightScale);
    Xunit.Assert.True(complex.LandscapeVideoLineHeightScale > standard.LandscapeVideoLineHeightScale);
    Xunit.Assert.True(complex.ShortVideoLineHeightScale > standard.ShortVideoLineHeightScale);
    Xunit.Assert.True(complex.PreferWeightOverUnderline);
    Xunit.Assert.False(standard.PreferWeightOverUnderline);
  }

  [Xunit.Fact]
  public void EveryScriptSample_ShapesWithItsDefaultFontAndDeclaredLineMetrics()
  {
    foreach (object[] row in UnicodeReadingTextSegmenterTests.ScriptCorpus)
    {
      ReaderScriptProfile profile = ReaderScriptProfile.Create((ReaderScriptFamily)(int)row[0]);
      ReaderFontOption fontOption = ReaderTypographyCatalog.GetDefault(profile.Font);
      ReaderTypographyMetrics metrics = ReaderTypographyCatalog.GetMetrics(profile.LineMetrics);
      FontFamily fontFamily = ReaderFontFamilyResolver.Resolve(fontOption);
      string text = (string)row[1];
      FormattedText formatted = new(
        text,
        CultureInfo.CurrentUICulture,
        profile.Direction == ReaderTextDirection.RightToLeft
          ? FlowDirection.RightToLeft
          : FlowDirection.LeftToRight,
        new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        32d,
        Brushes.Black,
        1d)
      {
        MaxTextWidth = 1_200d,
        LineHeight = 32d * metrics.PageLineHeightScale,
      };

      Xunit.Assert.True(formatted.WidthIncludingTrailingWhitespace > 0d);
      Xunit.Assert.True(formatted.Height > 0d);
      Xunit.Assert.False(formatted.BuildGeometry(new Point()).Bounds.IsEmpty);
    }
  }

  [Xunit.Fact]
  public void BundledReaderFonts_IncludeTheirFontFilesAndLicenses()
  {
    string repoRoot = FindRepoRoot();
    string fontsRoot = Path.Combine(repoRoot, "src", "DictateAnywhere.App", "Assets", "Fonts");

    AssertFontBundle(fontsRoot, "NotoSerifDevanagari", "NotoSerifDevanagari.ttf");
    AssertFontBundle(fontsRoot, "Kalam", "Kalam-Regular.ttf");
    AssertFontBundle(fontsRoot, "Excalifont", "Excalifont-Regular.ttf");
  }

  private static void AssertFontBundle(string fontsRoot, string directory, string fontFile)
  {
    string bundle = Path.Combine(fontsRoot, directory);
    Xunit.Assert.True(File.Exists(Path.Combine(bundle, fontFile)), $"Missing bundled font: {directory}/{fontFile}");
    Xunit.Assert.True(File.Exists(Path.Combine(bundle, "OFL.txt")), $"Missing bundled font license: {directory}/OFL.txt");
  }

  private static string FindRepoRoot()
  {
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
      if (File.Exists(Path.Combine(directory.FullName, "DictateAnywhere.sln")))
      {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    throw new InvalidOperationException("Unable to locate repository root from test base directory.");
  }
}
