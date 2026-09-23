using System;
using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.App.Workbench.Reading;

internal static class ReaderFontIds
{
  public const string BookSerif = "book-serif";
  public const string LiterarySerif = "literary-serif";
  public const string ModernSerif = "modern-serif";
  public const string FocusSans = "focus-sans";
  public const string Excalifont = "excalifont";
  public const string SystemMultilingual = "system-multilingual";
  public const string DevanagariLiterary = "devanagari-literary";
  public const string DevanagariHandDrawn = "devanagari-hand-drawn";
}

internal sealed record ReaderFontOption(
  string Id,
  string DisplayName,
  string FontFamilyName,
  string Description,
  IReadOnlyList<ReaderFontProfile> SupportedProfiles)
{
  public bool Supports(ReaderFontProfile profile) => SupportedProfiles.Contains(profile);

  public override string ToString() => DisplayName;
}

internal sealed record ReaderWordSurfaceMetrics(
  double WidthExpansion,
  double HorizontalPadding,
  double TopPadding,
  double BottomPadding,
  double VerticalMargin);

internal sealed record ReaderTypographyMetrics(
  double PageLineHeightScale,
  double FocusedWordLineHeightScale,
  double FocusedSentenceLineHeightScale,
  double LandscapeVideoLineHeightScale,
  double ShortVideoLineHeightScale,
  bool PreferWeightOverUnderline,
  ReaderWordSurfaceMetrics PageWord,
  ReaderWordSurfaceMetrics FocusedWord,
  ReaderWordSurfaceMetrics FocusedSentence);

internal static class ReaderTypographyCatalog
{
  private static readonly IReadOnlyList<ReaderFontProfile> AllProfiles =
    Array.AsReadOnly(Enum.GetValues<ReaderFontProfile>());

  private static readonly ReaderTypographyMetrics StandardMetrics = new(
    PageLineHeightScale: 1.52d,
    FocusedWordLineHeightScale: 1.35d,
    FocusedSentenceLineHeightScale: 1.55d,
    LandscapeVideoLineHeightScale: 1.50d,
    ShortVideoLineHeightScale: 1.326d,
    PreferWeightOverUnderline: false,
    PageWord: new ReaderWordSurfaceMetrics(3d, 1d, 1d, 2d, -1d),
    FocusedWord: new ReaderWordSurfaceMetrics(8d, 6d, 4d, 6d, 0d),
    FocusedSentence: new ReaderWordSurfaceMetrics(5d, 2d, 1d, 2d, -1d));

  private static readonly ReaderTypographyMetrics ComplexScriptMetrics = new(
    PageLineHeightScale: 1.72d,
    FocusedWordLineHeightScale: 1.70d,
    FocusedSentenceLineHeightScale: 1.75d,
    LandscapeVideoLineHeightScale: 1.70d,
    ShortVideoLineHeightScale: 1.55d,
    PreferWeightOverUnderline: true,
    PageWord: new ReaderWordSurfaceMetrics(8d, 3d, 7d, 4d, 0d),
    FocusedWord: new ReaderWordSurfaceMetrics(14d, 8d, 12d, 9d, 0d),
    FocusedSentence: new ReaderWordSurfaceMetrics(10d, 4d, 7d, 5d, 0d));

  public static IReadOnlyList<ReaderFontOption> Options { get; } =
  [
    new(
      ReaderFontIds.BookSerif,
      "Book Serif",
      "Georgia",
      "Comfortable long-form Latin reading",
      [ReaderFontProfile.Latin]),
    new(
      ReaderFontIds.LiterarySerif,
      "Literary Serif",
      "Palatino Linotype",
      "Elegant Latin book typography",
      [ReaderFontProfile.Latin]),
    new(
      ReaderFontIds.ModernSerif,
      "Modern Serif",
      "Cambria",
      "Polished Latin reading",
      [ReaderFontProfile.Latin]),
    new(
      ReaderFontIds.FocusSans,
      "Focus Sans",
      "Segoe UI",
      "Crisp Latin reading",
      [ReaderFontProfile.Latin]),
    new(
      ReaderFontIds.Excalifont,
      "Excalifont",
      "./Assets/Fonts/Excalifont/#Excalifont",
      "Hand-drawn Latin reading",
      [ReaderFontProfile.Latin]),
    new(
      ReaderFontIds.SystemMultilingual,
      "Multilingual Sans",
      "Global User Interface",
      "Windows script-aware font fallback",
      AllProfiles),
    new(
      ReaderFontIds.DevanagariLiterary,
      "Devanagari Literary",
      "./Assets/Fonts/NotoSerifDevanagari/#Noto Serif Devanagari",
      "Bundled literary Devanagari",
      [ReaderFontProfile.BundledDevanagari]),
    new(
      ReaderFontIds.DevanagariHandDrawn,
      "Devanagari Hand-drawn",
      "./Assets/Fonts/Kalam/#Kalam",
      "Bundled handwritten Devanagari and Latin",
      [ReaderFontProfile.BundledDevanagari]),
  ];

  public static IReadOnlyList<ReaderFontOption> GetOptions(ReaderFontProfile profile)
    => Options.Where(option => option.Supports(profile)).ToArray();

  public static ReaderFontOption GetDefault(ReaderFontProfile profile)
  {
    string id = profile switch
    {
      ReaderFontProfile.Latin => ReaderFontIds.BookSerif,
      ReaderFontProfile.SystemMultilingual => ReaderFontIds.SystemMultilingual,
      ReaderFontProfile.BundledDevanagari => ReaderFontIds.DevanagariLiterary,
      _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown reader font profile."),
    };
    return Options.Single(option => option.Id == id);
  }

  public static ReaderTypographyMetrics GetMetrics(ReaderLineMetricsProfile profile)
  {
    return profile switch
    {
      ReaderLineMetricsProfile.Standard => StandardMetrics,
      ReaderLineMetricsProfile.ComplexScript => ComplexScriptMetrics,
      _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown reader line-metrics profile."),
    };
  }
}
