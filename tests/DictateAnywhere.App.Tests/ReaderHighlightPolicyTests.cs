using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderHighlightPolicyTests
{
  [Fact]
  public void Resolve_HandlesEveryDeclaredStyleAndMode()
  {
    foreach (ReaderHighlightVisualStyle style in Enum.GetValues<ReaderHighlightVisualStyle>())
    {
      foreach (ReadingHighlightMode mode in Enum.GetValues<ReadingHighlightMode>())
      {
        Assert.NotNull(ReaderHighlightPlan.Resolve(style, mode, preferWeightOverUnderline: false));
      }
    }
  }

  [Fact]
  public void Resolve_DisablesEveryTreatmentWhenFollowAlongIsOff()
  {
    ReaderHighlightPlan plan = ReaderHighlightPlan.Resolve(
      ReaderHighlightVisualStyle.Spotlight,
      ReadingHighlightMode.Off,
      preferWeightOverUnderline: false);

    Assert.Equal(ReaderHighlightTextTone.Base, plan.TextTone);
    Assert.Equal(ReaderHighlightSurfaceTreatment.None, plan.Surface);
    Assert.Equal(ReaderHighlightTextEmphasis.Normal, plan.TextEmphasis);
    Assert.False(plan.DimUnhighlightedWords);
    Assert.False(plan.JoinHighlightedWords);
  }

  [Theory]
  [InlineData((int)ReaderHighlightVisualStyle.ReaderPage)]
  [InlineData((int)ReaderHighlightVisualStyle.AccentFill)]
  public void Resolve_JoinsOnlySentenceSurfaceTreatments(int styleValue)
  {
    ReaderHighlightVisualStyle style = (ReaderHighlightVisualStyle)styleValue;
    ReaderHighlightPlan sentence = ReaderHighlightPlan.Resolve(
      style,
      ReadingHighlightMode.Sentence,
      preferWeightOverUnderline: false);
    ReaderHighlightPlan word = ReaderHighlightPlan.Resolve(
      style,
      ReadingHighlightMode.Word,
      preferWeightOverUnderline: false);

    Assert.Equal(ReaderHighlightSurfaceTreatment.SoftAccent, sentence.Surface);
    Assert.True(sentence.JoinHighlightedWords);
    Assert.False(word.JoinHighlightedWords);
  }

  [Theory]
  [InlineData((int)ReadingHighlightMode.Word)]
  [InlineData((int)ReadingHighlightMode.Sentence)]
  public void Resolve_UsesWeightInsteadOfUnderlineForComplexScripts(int modeValue)
  {
    ReadingHighlightMode mode = (ReadingHighlightMode)modeValue;
    ReaderHighlightPlan standard = ReaderHighlightPlan.Resolve(
      ReaderHighlightVisualStyle.Underline,
      mode,
      preferWeightOverUnderline: false);
    ReaderHighlightPlan complex = ReaderHighlightPlan.Resolve(
      ReaderHighlightVisualStyle.Underline,
      mode,
      preferWeightOverUnderline: true);

    Assert.Equal(ReaderHighlightTextEmphasis.Underline, standard.TextEmphasis);
    Assert.Equal(ReaderHighlightTextEmphasis.Bold, complex.TextEmphasis);
  }

  [Fact]
  public void Resolve_DescribesAccessibleStylesWithoutDependingOnColorAlone()
  {
    ReaderHighlightPlan ring = ReaderHighlightPlan.Resolve(
      ReaderHighlightVisualStyle.FocusRing,
      ReadingHighlightMode.Word,
      preferWeightOverUnderline: false);
    ReaderHighlightPlan spotlight = ReaderHighlightPlan.Resolve(
      ReaderHighlightVisualStyle.Spotlight,
      ReadingHighlightMode.Word,
      preferWeightOverUnderline: false);

    Assert.Equal(ReaderHighlightSurfaceTreatment.AccentOutline, ring.Surface);
    Assert.True(spotlight.DimUnhighlightedWords);
    Assert.Equal(ReaderHighlightTextEmphasis.Underline, spotlight.TextEmphasis);
  }

  [Fact]
  public void Resolve_GivesWordFillAContrastingTextTone()
  {
    ReaderHighlightPlan plan = ReaderHighlightPlan.Resolve(
      ReaderHighlightVisualStyle.AccentFill,
      ReadingHighlightMode.Word,
      preferWeightOverUnderline: false);

    Assert.Equal(ReaderHighlightSurfaceTreatment.SolidAccent, plan.Surface);
    Assert.Equal(ReaderHighlightTextTone.Contrast, plan.TextTone);
  }

  [Fact]
  public void Resolve_RejectsUnknownStyleAndModeValues()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => ReaderHighlightPlan.Resolve(
      (ReaderHighlightVisualStyle)int.MaxValue,
      ReadingHighlightMode.Word,
      preferWeightOverUnderline: false));
    Assert.Throws<ArgumentOutOfRangeException>(() => ReaderHighlightPlan.Resolve(
      ReaderHighlightVisualStyle.ReaderPage,
      (ReadingHighlightMode)int.MaxValue,
      preferWeightOverUnderline: false));
  }
}
