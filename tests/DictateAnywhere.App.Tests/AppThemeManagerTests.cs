using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class AppThemeManagerTests
{
  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public void CodeAndPaperSmallText_MeetsContrastAgainstItsSurface(bool dark)
  {
    ResourceDictionary resources = new();
    AppThemeManager.ApplyPalette(resources, dark);
    foreach (string prefix in new[] { "Code", "Paper" })
    {
      Color surface = ((SolidColorBrush)resources[$"Brush.{prefix}.{(prefix == "Code" ? "Background" : "Page")}"]).Color;
      foreach (string token in new[] { "Comment", "Keyword", "Identifier", "Primitive", "Type", "Method", "Member", "Literal", "Number", "String", "Operator", "Bracket", "Punctuation" })
      {
        Color foreground = ((SolidColorBrush)resources[$"Brush.{prefix}.{token}"]).Color;
        double first = Luminance(surface);
        double second = Luminance(foreground);
        double contrast = (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        Xunit.Assert.True(contrast >= 4.5, $"{prefix}.{token} contrast is {contrast:F2} in {(dark ? "Dark" : "Light")}.");
      }
    }
  }

  private static double Luminance(Color color)
  {
    static double Linear(byte value)
    {
      double channel = value / 255d;
      return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
    return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
  }

  [Xunit.Theory]
  [Xunit.InlineData(12d, 1d)]
  [Xunit.InlineData(15d, 1.25d)]
  [Xunit.InlineData(18d, 1.5d)]
  [Xunit.InlineData(21d, 1.75d)]
  [Xunit.InlineData(24d, 2d)]
  [Xunit.InlineData(120d, 2.25d)]
  [Xunit.InlineData(0d, 1d)]
  [Xunit.InlineData(-12d, 1d)]
  public void TextScale_CalculatesWindowsAccessibilityScaleWithoutDpiMultiplication(
    double messageFontSize,
    double expectedScale)
  {
    Xunit.Assert.Equal(expectedScale, AppTextScaleManager.CalculateScale(messageFontSize));
  }

  [Xunit.Fact]
  public void TextScale_InvalidMetricsFallBackAndSemanticResourcesUpdateAtomically()
  {
    Xunit.Assert.Equal(1d, AppTextScaleManager.CalculateScale(double.NaN));
    Xunit.Assert.Equal(1d, AppTextScaleManager.CalculateScale(double.PositiveInfinity));

    ResourceDictionary resources = new();
    AppTextScaleManager.Apply(resources, messageFontSize: 24d);

    Xunit.Assert.Equal(2d, resources["Font.Scale.WindowsText"]);
    Xunit.Assert.Equal(24d, resources["Font.Size.Caption"]);
    Xunit.Assert.Equal(26d, resources["Font.Size.FieldLabel"]);
    Xunit.Assert.Equal(28d, resources["Font.Size.Body"]);
    Xunit.Assert.Equal(32d, resources["Font.Size.Section"]);
    Xunit.Assert.Equal(68d, resources["Font.Size.Hero"]);
    Xunit.Assert.Equal(360d, Xunit.Assert.IsType<GridLength>(resources["Layout.Workbench.SidebarWidth"]).Value);
    Xunit.Assert.Equal(400d, Xunit.Assert.IsType<GridLength>(resources["Layout.Reader.SidebarWidth"]).Value);
    Xunit.Assert.Equal(1, resources["Layout.Responsive.Columns"]);

    AppTextScaleManager.Apply(resources, messageFontSize: 12d);
    Xunit.Assert.Equal(1d, resources["Font.Scale.WindowsText"]);
    Xunit.Assert.Equal(12d, resources["Font.Size.Caption"]);
    Xunit.Assert.Equal(2, resources["Layout.Responsive.Columns"]);
  }

  [Xunit.Fact]
  public void TextScale_UpdatesTheExistingMergedTokenOwnerInsteadOfShadowingIt()
  {
    ResourceDictionary tokens = new()
    {
      ["Font.Size.Section"] = 16d,
    };
    ResourceDictionary applicationResources = new();
    applicationResources.MergedDictionaries.Add(tokens);

    AppTextScaleManager.Apply(applicationResources, messageFontSize: 24d);

    Xunit.Assert.Equal(32d, tokens["Font.Size.Section"]);
    Xunit.Assert.DoesNotContain(
      applicationResources.Keys.Cast<object>(),
      key => Equals(key, "Font.Size.Section"));
  }

  [Xunit.Fact]
  public void ApplyNativeWindowThemeAttributes_UsesSemanticHighContrastOuterBoundary()
  {
    List<(int Attribute, int Value)> applied = [];

    AppThemeManager.ApplyNativeWindowThemeAttributes(
      AppThemePreference.Light,
      isHighContrast: true,
      usesCustomChrome: true,
      SystemColors.WindowTextColor,
      SystemColors.WindowColor,
      (attribute, value) => applied.Add((attribute, value)));

    Xunit.Assert.Equal(3, applied.Count);
    Xunit.Assert.Contains((AppThemeManager.DwmAttributeUseImmersiveDarkMode, 0), applied);
    Xunit.Assert.Contains((AppThemeManager.DwmAttributeUseImmersiveDarkModeBefore20H1, 0), applied);
    Xunit.Assert.Contains(
      (AppThemeManager.DwmAttributeBorderColor, AppThemeManager.ToColorRef(SystemColors.WindowTextColor)),
      applied);
    Xunit.Assert.Single(applied.Where(entry => entry.Attribute == AppThemeManager.DwmAttributeBorderColor));
  }

  [Xunit.Theory]
  [Xunit.InlineData(AppThemePreference.Light, 0)]
  [Xunit.InlineData(AppThemePreference.Dark, 1)]
  public void ApplyNativeWindowThemeAttributes_WithoutHighContrast_RestoresWindowsDefaultBorder(
    AppThemePreference preference,
    int expectedDarkMode)
  {
    List<(int Attribute, int Value)> applied = [];

    AppThemeManager.ApplyNativeWindowThemeAttributes(
      preference,
      isHighContrast: false,
      usesCustomChrome: true,
      SystemColors.WindowTextColor,
      SystemColors.WindowColor,
      (attribute, value) => applied.Add((attribute, value)));

    Xunit.Assert.Contains((AppThemeManager.DwmAttributeUseImmersiveDarkMode, expectedDarkMode), applied);
    Xunit.Assert.Contains((AppThemeManager.DwmAttributeUseImmersiveDarkModeBefore20H1, expectedDarkMode), applied);
    Xunit.Assert.Contains((AppThemeManager.DwmAttributeBorderColor, AppThemeManager.DwmColorDefault), applied);
  }

  [Xunit.Fact]
  public void ApplyNativeWindowThemeAttributes_NativeChromeRetainsWindowsBorderOwnership()
  {
    List<(int Attribute, int Value)> applied = [];

    AppThemeManager.ApplyNativeWindowThemeAttributes(
      AppThemePreference.Light,
      isHighContrast: true,
      usesCustomChrome: false,
      SystemColors.WindowTextColor,
      SystemColors.WindowColor,
      (attribute, value) => applied.Add((attribute, value)));

    Xunit.Assert.Equal(2, applied.Count);
    Xunit.Assert.DoesNotContain(applied, entry => entry.Attribute == AppThemeManager.DwmAttributeBorderColor);
  }

  [Xunit.Fact]
  public void ResolveHighContrastBorderColor_RejectsTransparentOrIndistinguishableBoundaries()
  {
    Color surface = Color.FromRgb(10, 20, 30);

    Xunit.Assert.Throws<InvalidOperationException>(() =>
      AppThemeManager.ResolveHighContrastBorderColor(Colors.Transparent, surface));
    Xunit.Assert.Throws<InvalidOperationException>(() =>
      AppThemeManager.ResolveHighContrastBorderColor(surface, surface));
  }

  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public void ApplyPalette_ProvidesCompleteDangerActionPalette(bool useDarkTheme)
  {
    ResourceDictionary resources = new();

    AppThemeManager.ApplyPalette(resources, useDarkTheme);

    Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Control.Danger"]);
    Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Control.DangerHover"]);
    Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Control.DangerPressed"]);
    Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Text.Inverse"]);
  }

  [Xunit.Fact]
  public void ApplyPalette_WhenHighContrastEnabled_AppliesSystemColorMappings()
  {
    ResourceDictionary resources = new();

    AppThemeManager.ApplyPalette(resources, useDarkTheme: true, isHighContrast: true);

    SolidColorBrush canvasBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Surface.Canvas"]);
    SolidColorBrush baseBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Surface.Base"]);
    SolidColorBrush elevatedBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Surface.Elevated"]);
    SolidColorBrush textPrimaryBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Text.Primary"]);
    SolidColorBrush textSecondaryBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Text.Secondary"]);
    SolidColorBrush textInverseBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Text.Inverse"]);
    SolidColorBrush borderSubtleBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Border.Subtle"]);
    SolidColorBrush controlPrimaryBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Control.Primary"]);
    SolidColorBrush controlHoverBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Control.Hover"]);

    Xunit.Assert.Equal(SystemColors.WindowColor, canvasBrush.Color);
    Xunit.Assert.Equal(SystemColors.WindowColor, baseBrush.Color);
    Xunit.Assert.Equal(SystemColors.WindowColor, elevatedBrush.Color);
    Xunit.Assert.Equal(SystemColors.WindowTextColor, textPrimaryBrush.Color);
    Xunit.Assert.Equal(SystemColors.WindowTextColor, textSecondaryBrush.Color);
    Xunit.Assert.Equal(SystemColors.HighlightTextColor, textInverseBrush.Color);
    Xunit.Assert.Equal(SystemColors.WindowTextColor, borderSubtleBrush.Color);
    Xunit.Assert.NotEqual(canvasBrush.Color, borderSubtleBrush.Color);
    Xunit.Assert.Equal(SystemColors.HighlightColor, controlPrimaryBrush.Color);
    Xunit.Assert.Equal(SystemColors.HighlightColor, controlHoverBrush.Color);
  }

  [Xunit.Theory]
  [Xunit.InlineData(false)]
  [Xunit.InlineData(true)]
  public void ApplyPalette_WhenHighContrastDisabled_PreservesStandardPalettes(bool useDarkTheme)
  {
    ResourceDictionary resources = new();

    AppThemeManager.ApplyPalette(resources, useDarkTheme, isHighContrast: false);

    SolidColorBrush canvasBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Surface.Canvas"]);
    SolidColorBrush textPrimaryBrush = Xunit.Assert.IsType<SolidColorBrush>(resources["Brush.Text.Primary"]);

    if (useDarkTheme)
    {
      Xunit.Assert.Equal(Color.FromRgb(10, 15, 24), canvasBrush.Color);
      Xunit.Assert.Equal(Color.FromRgb(243, 244, 246), textPrimaryBrush.Color);
    }
    else
    {
      Xunit.Assert.Equal(Color.FromRgb(242, 240, 235), canvasBrush.Color);
      Xunit.Assert.Equal(Color.FromRgb(31, 36, 48), textPrimaryBrush.Color);
    }
  }

  [Xunit.Fact]
  public void GetHighContrastPalette_ContainsAllRequiredPaletteKeys()
  {
    IReadOnlyDictionary<string, Color> palette = AppThemeManager.GetHighContrastPalette();

    string[] requiredKeys =
    [
      "Brush.Text.Primary",
      "Brush.Text.Secondary",
      "Brush.Text.Inverse",
      "Brush.Status.Success",
      "Brush.Status.Warning",
      "Brush.Status.Error",
      "Brush.Surface.Subtle",
      "Brush.Surface.Canvas",
      "Brush.Surface.Sidebar",
      "Brush.Surface.Composer",
      "Brush.Surface.BlueWash",
      "Brush.Surface.Base",
      "Brush.Surface.Elevated",
      "Brush.Accent.Chat",
      "Brush.Accent.Dictation",
      "Brush.Code.Background",
      "Brush.Code.Border",
      "Brush.Code.Text",
      "Brush.Code.Comment",
      "Brush.Code.String",
      "Brush.Code.Keyword",
      "Brush.Code.Identifier",
      "Brush.Code.Number",
      "Brush.Code.Header", "Brush.Code.Label", "Brush.Code.Icon", "Brush.Code.Primitive",
      "Brush.Code.Type", "Brush.Code.Method", "Brush.Code.Member", "Brush.Code.Literal",
      "Brush.Code.Operator", "Brush.Code.Bracket", "Brush.Code.Punctuation",
      "Brush.Chat.User",
      "Brush.Chat.UserText",
      "Brush.Paper.Ink",
      "Brush.Paper.User",
      "Brush.Paper.UserText",
      "Brush.Paper.Composer",
      "Brush.Paper.Keyword",
      "Brush.Paper.Identifier",
      "Brush.Paper.Number",
      "Brush.Paper.String",
      "Brush.Paper.Comment",
      "Brush.Paper.Page", "Brush.Paper.Border", "Brush.Paper.Control", "Brush.Paper.Hover",
      "Brush.Paper.Primitive", "Brush.Paper.Type", "Brush.Paper.Method", "Brush.Paper.Member",
      "Brush.Paper.Literal", "Brush.Paper.Operator", "Brush.Paper.Bracket", "Brush.Paper.Punctuation",
      "Brush.Border.Subtle",
      "Brush.Control.Input",
      "Brush.Control.InputDisabled",
      "Brush.Control.Hover",
      "Brush.Control.Muted",
      "Brush.Control.Pressed",
      "Brush.Control.Selection",
      "Brush.Control.Primary",
      "Brush.Control.PrimaryHover",
      "Brush.Control.PrimaryPressed",
      "Brush.Control.Danger",
      "Brush.Control.DangerHover",
      "Brush.Control.DangerPressed",
      "Brush.Progress.Track",
      "Brush.Progress.Value",
      "Brush.Scrollbar.Thumb",
      "Brush.Scrollbar.ThumbHover",
    ];

    Xunit.Assert.Equal(requiredKeys.Length, palette.Count);
    foreach (string key in requiredKeys)
    {
      Xunit.Assert.True(palette.ContainsKey(key), $"Missing palette key: {key}");
    }
  }
}
