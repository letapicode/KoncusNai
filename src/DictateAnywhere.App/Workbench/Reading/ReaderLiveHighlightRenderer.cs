using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Applies shared highlight semantics to the live WPF word surfaces.</summary>
internal static class ReaderLiveHighlightRenderer
{
  public static void Apply(
    IReadOnlyList<ReaderWordVisual> visuals,
    IReadOnlyList<string> words,
    Brush baseForeground,
    int activeWordIndex,
    ReadingHighlightMode mode,
    ReaderHighlightVisualStyle style,
    string accentHex,
    bool preferWeightOverUnderline,
    ReaderHighlightTextBlock? textBlock = null,
    (int Start, int End)? selectedSentence = null,
    Brush? pageBackground = null)
  {
    ArgumentNullException.ThrowIfNull(visuals);
    ArgumentNullException.ThrowIfNull(words);
    ArgumentNullException.ThrowIfNull(baseForeground);
    ArgumentException.ThrowIfNullOrWhiteSpace(accentHex);

    ReaderHighlightPlan plan = ReaderHighlightPlan.Resolve(style, mode, preferWeightOverUnderline);
    (int Start, int End) sentenceRange = mode == ReadingHighlightMode.Sentence
      ? selectedSentence ?? ReadingPlaybackTiming.GetSentenceRange(words, activeWordIndex)
      : (-1, -1);
    SolidColorBrush accent = CreateBrush(accentHex);
    SolidColorBrush softAccent = CreateBrush(accentHex, 76);

    bool accessibleStyle = style is ReaderHighlightVisualStyle.Underline or ReaderHighlightVisualStyle.Spotlight or ReaderHighlightVisualStyle.FocusRing;
    if (accessibleStyle && pageBackground is SolidColorBrush background && baseForeground is SolidColorBrush ink)
    {
      accent = new SolidColorBrush(ReaderHighlightContrast.Resolve(accent.Color, background.Color, ink.Color));
      plan = plan with { TextTone = ReaderHighlightTextTone.Base };
    }
    bool sentenceSurface = mode == ReadingHighlightMode.Sentence;
    textBlock?.SetHighlight(visuals, sentenceSurface && activeWordIndex >= 0 ? sentenceRange : (-1, -1),
      sentenceSurface ? plan.Surface : ReaderHighlightSurfaceTreatment.None, accent, softAccent,
      sentenceSurface && plan.TextEmphasis == ReaderHighlightTextEmphasis.Underline);

    foreach (ReaderWordVisual visual in visuals)
    {
      Reset(visual, baseForeground);
      bool highlighted = activeWordIndex >= 0
        && (mode == ReadingHighlightMode.Word && visual.Index == activeWordIndex
          || mode == ReadingHighlightMode.Sentence
            && visual.Index >= sentenceRange.Start
            && visual.Index <= sentenceRange.End);
      if (plan.DimUnhighlightedWords && activeWordIndex >= 0 && !highlighted)
      {
        visual.Surface.Opacity = 0.72d;
      }

      if (!highlighted)
      {
        continue;
      }

      ApplyText(visual, plan, baseForeground, accent);
      if (sentenceSurface && textBlock is not null && plan.TextEmphasis == ReaderHighlightTextEmphasis.Underline)
        visual.Text.TextDecorations = null;
      if (!sentenceSurface)
      {
        ApplySurface(visual, plan, accent, softAccent);
      }
    }
  }

  private static void Reset(ReaderWordVisual visual, Brush baseForeground)
  {
    visual.Surface.Background = Brushes.Transparent;
    visual.Surface.BorderBrush = Brushes.Transparent;
    visual.Surface.Opacity = 1d;
    visual.Surface.CornerRadius = visual.DefaultCornerRadius;
    visual.Text.Foreground = baseForeground;
    visual.Text.FontWeight = visual.DefaultFontWeight;
    visual.Text.FontSize = visual.DefaultFontSize;
    visual.Text.TextDecorations = null;
    if (visual.TrailingSeparator is not null)
    {
      visual.TrailingSeparator.Background = Brushes.Transparent;
    }
  }

  private static void ApplyText(
    ReaderWordVisual visual,
    ReaderHighlightPlan plan,
    Brush baseForeground,
    Brush accent)
  {
    visual.Text.Foreground = plan.TextTone switch
    {
      ReaderHighlightTextTone.Base => baseForeground,
      ReaderHighlightTextTone.Accent => accent,
      ReaderHighlightTextTone.Contrast => new SolidColorBrush(Color.FromRgb(15, 23, 42)),
      _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.TextTone, "Unknown reader highlight text tone."),
    };
    switch (plan.TextEmphasis)
    {
      case ReaderHighlightTextEmphasis.Normal:
        break;
      case ReaderHighlightTextEmphasis.Underline:
        visual.Text.TextDecorations = TextDecorations.Underline;
        break;
      case ReaderHighlightTextEmphasis.Bold:
        visual.Text.FontWeight = FontWeights.Bold;
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(plan), plan.TextEmphasis, "Unknown reader highlight text emphasis.");
    }
  }

  private static void ApplySurface(
    ReaderWordVisual visual,
    ReaderHighlightPlan plan,
    Brush accent,
    Brush softAccent)
  {
    switch (plan.Surface)
    {
      case ReaderHighlightSurfaceTreatment.None:
        return;
      case ReaderHighlightSurfaceTreatment.SoftAccent:
        visual.Surface.Background = softAccent;
        break;
      case ReaderHighlightSurfaceTreatment.SolidAccent:
        visual.Surface.Background = accent;
        visual.Surface.BorderBrush = accent;
        break;
      case ReaderHighlightSurfaceTreatment.AccentOutline:
        visual.Surface.BorderBrush = accent;
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(plan), plan.Surface, "Unknown reader highlight surface treatment.");
    }

  }

  private static SolidColorBrush CreateBrush(string hex, byte alpha = byte.MaxValue)
  {
    Color color = (Color)ColorConverter.ConvertFromString(hex);
    return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
  }
}

internal sealed record ReaderWordVisual(
  int Index,
  Border Surface,
  TextBlock Text,
  Run? TrailingSeparator,
  CornerRadius DefaultCornerRadius,
  FontWeight DefaultFontWeight,
  double DefaultFontSize);
