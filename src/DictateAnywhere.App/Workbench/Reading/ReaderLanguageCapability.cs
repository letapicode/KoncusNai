namespace DictateAnywhere.App.Workbench.Reading;

internal enum ReaderScriptFamily
{
  Latin,
  Han,
  Japanese,
  Devanagari,
  BengaliAssamese,
  Gujarati,
  Gurmukhi,
  Kannada,
  Malayalam,
  Odia,
  Tamil,
  Telugu,
  OlChiki,
  ArabicDerived,
}

internal enum ReaderTextDirection
{
  LeftToRight,
  RightToLeft,
}

internal enum ReaderFontProfile
{
  Latin,
  SystemMultilingual,
  BundledDevanagari,
}

internal enum ReaderLineMetricsProfile
{
  Standard,
  ComplexScript,
}

internal enum ReaderSegmentationProfile
{
  UnicodeWord,
  CjkCharacter,
}

internal enum ReaderWordTimingStrategy
{
  NativeWithDeterministicFallback,
  LocalForcedAlignment,
}

internal enum ReaderOcrSupport
{
  Unavailable,
  English,
  Latin,
  Devanagari,
  Chinese,
  Japanese,
}

internal sealed record ReaderScriptProfile(
  ReaderScriptFamily Script,
  ReaderTextDirection Direction,
  ReaderFontProfile Font,
  ReaderLineMetricsProfile LineMetrics,
  ReaderSegmentationProfile Segmentation)
{
  public static ReaderScriptProfile Create(ReaderScriptFamily script) => script switch
  {
    ReaderScriptFamily.Latin => new(
      script,
      ReaderTextDirection.LeftToRight,
      ReaderFontProfile.Latin,
      ReaderLineMetricsProfile.Standard,
      ReaderSegmentationProfile.UnicodeWord),
    ReaderScriptFamily.Han or ReaderScriptFamily.Japanese => new(
      script,
      ReaderTextDirection.LeftToRight,
      ReaderFontProfile.SystemMultilingual,
      ReaderLineMetricsProfile.Standard,
      ReaderSegmentationProfile.CjkCharacter),
    ReaderScriptFamily.Devanagari => new(
      script,
      ReaderTextDirection.LeftToRight,
      ReaderFontProfile.BundledDevanagari,
      ReaderLineMetricsProfile.ComplexScript,
      ReaderSegmentationProfile.UnicodeWord),
    ReaderScriptFamily.BengaliAssamese
      or ReaderScriptFamily.Gujarati
      or ReaderScriptFamily.Gurmukhi
      or ReaderScriptFamily.Kannada
      or ReaderScriptFamily.Malayalam
      or ReaderScriptFamily.Odia
      or ReaderScriptFamily.Tamil
      or ReaderScriptFamily.Telugu
      or ReaderScriptFamily.OlChiki => new(
        script,
        ReaderTextDirection.LeftToRight,
        ReaderFontProfile.SystemMultilingual,
        ReaderLineMetricsProfile.ComplexScript,
        ReaderSegmentationProfile.UnicodeWord),
    ReaderScriptFamily.ArabicDerived => new(
      script,
      ReaderTextDirection.RightToLeft,
      ReaderFontProfile.SystemMultilingual,
      ReaderLineMetricsProfile.ComplexScript,
      ReaderSegmentationProfile.UnicodeWord),
    _ => throw new ArgumentOutOfRangeException(nameof(script), script, "Unknown reader script family."),
  };
}

internal sealed record ReaderLanguageCapability(
  ReaderScriptProfile Text,
  ReaderWordTimingStrategy WordTiming,
  ReaderOcrSupport Ocr,
  bool SupportsVideoExport = true);
