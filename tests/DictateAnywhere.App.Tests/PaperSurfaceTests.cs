using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class PaperSurfaceTests
{
  [Theory]
  [InlineData(7)]
  [InlineData(17)]
  public void PaperTexture_LooseXamlCanResolveTypeAndCornerRadiusWithoutRendering(int radius) => RunOnSta(() =>
  {
    string markup = $$"""
      <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:presentation="clr-namespace:DictateAnywhere.App.Presentation;assembly=DictateAnywhere.App">
        <presentation:PaperTexture x:Name="Grain" CornerRadius="{{radius}}" />
      </Grid>
      """;
    Grid grid = Assert.IsType<Grid>(XamlReader.Parse(markup));
    PaperTexture texture = Assert.IsType<PaperTexture>(grid.FindName("Grain"));
    Assert.Same(texture, Assert.Single(grid.Children.Cast<UIElement>()));
    Assert.Equal(radius, texture.CornerRadius);
    Assert.False(texture.IsHitTestVisible);
    Assert.False(texture.Focusable);
  });

  [Fact]
  public void Grain_IsFrozenVectorGeometryAndBoundedAtLargeSizes() => RunOnSta(() =>
  {
    Assert.Empty(PaperTexture.CreateGrain(new Size(0, 0)).Children);
    Assert.Throws<ArgumentOutOfRangeException>(() => PaperTexture.CreateGrain(new Size(double.PositiveInfinity, 100)));
    foreach (Size size in new[] { new Size(900, 700), new Size(3840, 2160), new Size(10000, 10000) })
    {
      DrawingGroup grain = PaperTexture.CreateGrain(size);
      Assert.True(grain.IsFrozen);
      Assert.Equal(2, grain.Children.Count);
      foreach (GeometryDrawing drawing in grain.Children)
      {
        Assert.IsType<StreamGeometry>(drawing.Geometry);
        Assert.Null(drawing.Brush);
        Assert.True(drawing.Pen.IsFrozen);
        Assert.InRange(drawing.Bounds.Right, 0, size.Width + 8);
        Assert.InRange(drawing.Bounds.Bottom, 0, size.Height + 8);
      }
    }
    PaperTexture texture = new();
    Assert.False(texture.IsHitTestVisible);
    Assert.False(texture.Focusable);
  });

  [Fact]
  public void PaperComposer_SharesSurfaceAndRestoresThemeWithoutChangingPrompt() => RunOnSta(() =>
  {
    WorkbenchComposerView composer = new();
    Grid host = new();
    host.Children.Add(composer);
    Brush input = Brushes.Navy;
    Brush background = Brushes.SlateGray;
    host.Resources["Brush.Control.Input"] = input;
    host.Resources["Brush.Surface.Composer"] = background;
    host.Resources["Brush.Paper.Composer"] = Brushes.White;
    composer.PromptText = "Draft with\nmultiple lines";
    composer.SetPaperView(true);
    Assert.Same(Brushes.Transparent, composer.PromptElement.Background);
    Assert.Same(Brushes.Transparent, Assert.IsType<Border>(composer.FindName("CompactComposer")).Background);
    Assert.Same(Brushes.Transparent, composer.PromptElement.Resources["Brush.Control.InputDisabled"]);
    WindowThemeBehavior.SetIsHighContrastActive(composer, true);
    composer.SetPaperView(true);
    Assert.Same(Brushes.White, composer.PromptElement.Background);
    Assert.False(composer.PromptElement.Resources.Contains("Brush.Control.InputDisabled"));
    WindowThemeBehavior.SetIsHighContrastActive(composer, false);
    composer.SetPaperView(false);
    Assert.Same(input, composer.PromptElement.Background);
    Assert.Same(background, Assert.IsType<Border>(composer.FindName("CompactComposer")).Background);
    Assert.Equal("Draft with\nmultiple lines", composer.PromptText);
    composer.DisposePresentation();
  });

  [Fact]
  public void ExpandedPaper_UsesGrainAndPreservesDraftAcrossAppearanceChanges() => RunOnSta(() =>
  {
    WorkbenchExpandedPromptView expanded = new();
    Grid host = new();
    host.Children.Add(expanded);
    host.Resources["Brush.Paper.Page"] = Brushes.WhiteSmoke;
    host.Resources["Brush.Surface.Composer"] = Brushes.Navy;
    host.Resources["Brush.Control.Input"] = Brushes.Gray;
    expanded.SetText("Unsent draft");
    expanded.SetPaperView(true);
    PaperTexture grain = Assert.IsType<PaperTexture>(expanded.FindName("PaperGrain"));
    Assert.Equal(Visibility.Visible, grain.Visibility);
    Assert.Same(Brushes.Transparent, expanded.PromptElement.Background);
    WindowThemeBehavior.SetIsHighContrastActive(expanded, true);
    expanded.SetPaperView(true);
    Assert.Equal(Visibility.Collapsed, grain.Visibility);
    expanded.SetPaperView(false);
    Assert.Same(Brushes.Gray, expanded.PromptElement.Background);
    Assert.Same(Brushes.Navy, Assert.IsType<Border>(expanded.FindName("Surface")).Background);
    Assert.Equal("Unsent draft", expanded.Text);
  });

  [SuppressMessage("Design", "CA1031", Justification = "Transfers STA test failures to the test runner.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() => { try { action(); } catch (Exception exception) { failure = exception; } });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "STA test timed out.");
    if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
  }
}
