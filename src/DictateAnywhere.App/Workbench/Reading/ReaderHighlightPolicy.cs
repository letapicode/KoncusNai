using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

internal enum ReaderHighlightTextTone
{
  Base,
  Accent,
  Contrast,
}

internal enum ReaderHighlightSurfaceTreatment
{
  None,
  SoftAccent,
  SolidAccent,
  AccentOutline,
}

internal enum ReaderHighlightTextEmphasis
{
  Normal,
  Underline,
  Bold,
}

/// <summary>Defines highlight semantics independently of the live and exported rendering adapters.</summary>
internal sealed record ReaderHighlightPlan(
  ReaderHighlightTextTone TextTone,
  ReaderHighlightSurfaceTreatment Surface,
  ReaderHighlightTextEmphasis TextEmphasis,
  bool DimUnhighlightedWords,
  bool JoinHighlightedWords)
{
  internal static ReadingHighlightMode ResolveEmphasisMode(ReaderDocumentSurface surface,
    ReadingHighlightMode mode, ReaderHighlightVisualStyle style) =>
    surface == ReaderDocumentSurface.FocusedSentence && mode == ReadingHighlightMode.Sentence
      && style is not (ReaderHighlightVisualStyle.ReaderPage or ReaderHighlightVisualStyle.AccentFill)
        ? ReadingHighlightMode.Word : mode;

  public static ReaderHighlightPlan Resolve(
    ReaderHighlightVisualStyle style,
    ReadingHighlightMode mode,
    bool preferWeightOverUnderline)
  {
    if (!Enum.IsDefined(style))
    {
      throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown reader highlight style.");
    }
    if (!Enum.IsDefined(mode))
    {
      throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown reader follow-along mode.");
    }
    if (mode == ReadingHighlightMode.Off)
    {
      return new(
        ReaderHighlightTextTone.Base,
        ReaderHighlightSurfaceTreatment.None,
        ReaderHighlightTextEmphasis.Normal,
        DimUnhighlightedWords: false,
        JoinHighlightedWords: false);
    }

    ReaderHighlightTextEmphasis lineEmphasis = preferWeightOverUnderline
      ? ReaderHighlightTextEmphasis.Bold
      : ReaderHighlightTextEmphasis.Underline;
    return style switch
    {
      ReaderHighlightVisualStyle.ReaderPage when mode == ReadingHighlightMode.Sentence => new(
        ReaderHighlightTextTone.Base,
        ReaderHighlightSurfaceTreatment.SoftAccent,
        ReaderHighlightTextEmphasis.Normal,
        DimUnhighlightedWords: false,
        JoinHighlightedWords: true),
      ReaderHighlightVisualStyle.ReaderPage => new(
        ReaderHighlightTextTone.Accent,
        ReaderHighlightSurfaceTreatment.None,
        ReaderHighlightTextEmphasis.Normal,
        DimUnhighlightedWords: false,
        JoinHighlightedWords: false),
      ReaderHighlightVisualStyle.FocusType => new(
        ReaderHighlightTextTone.Accent,
        ReaderHighlightSurfaceTreatment.None,
        ReaderHighlightTextEmphasis.Normal,
        DimUnhighlightedWords: false,
        JoinHighlightedWords: false),
      ReaderHighlightVisualStyle.Underline => new(
        ReaderHighlightTextTone.Accent,
        ReaderHighlightSurfaceTreatment.None,
        lineEmphasis,
        DimUnhighlightedWords: false,
        JoinHighlightedWords: false),
      ReaderHighlightVisualStyle.AccentFill when mode == ReadingHighlightMode.Sentence => new(
        ReaderHighlightTextTone.Base,
        ReaderHighlightSurfaceTreatment.SoftAccent,
        ReaderHighlightTextEmphasis.Normal,
        DimUnhighlightedWords: false,
        JoinHighlightedWords: true),
      ReaderHighlightVisualStyle.AccentFill => new(
        ReaderHighlightTextTone.Contrast,
        ReaderHighlightSurfaceTreatment.SolidAccent,
        ReaderHighlightTextEmphasis.Normal,
        DimUnhighlightedWords: false,
        JoinHighlightedWords: false),
      ReaderHighlightVisualStyle.BoldFocus => new(
        ReaderHighlightTextTone.Accent,
        ReaderHighlightSurfaceTreatment.None,
        ReaderHighlightTextEmphasis.Bold,
        DimUnhighlightedWords: false,
        JoinHighlightedWords: false),
      ReaderHighlightVisualStyle.FocusRing => new(
        ReaderHighlightTextTone.Base,
        ReaderHighlightSurfaceTreatment.AccentOutline,
        ReaderHighlightTextEmphasis.Normal,
        DimUnhighlightedWords: false,
        JoinHighlightedWords: false),
      ReaderHighlightVisualStyle.Spotlight => new(
        ReaderHighlightTextTone.Accent,
        ReaderHighlightSurfaceTreatment.None,
        lineEmphasis,
        DimUnhighlightedWords: true,
        JoinHighlightedWords: false),
      _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown reader highlight style."),
    };
  }
}
