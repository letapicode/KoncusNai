using System;
using System.Windows;
using System.Windows.Media;

namespace DictateAnywhere.App.Presentation;

/// <summary>Draws continuous paper grain in viewport coordinates, without image tiles.</summary>
internal sealed class PaperTexture : FrameworkElement
{
  private DrawingGroup? grain;
  private Size grainSize;

  public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
    nameof(CornerRadius), typeof(double), typeof(PaperTexture),
    new FrameworkPropertyMetadata(7d, FrameworkPropertyMetadataOptions.AffectsRender),
    value => value is double radius && double.IsFinite(radius) && radius >= 0);

  public double CornerRadius
  {
    get => (double)GetValue(CornerRadiusProperty);
    set => SetValue(CornerRadiusProperty, value);
  }

  public PaperTexture()
  {
    IsHitTestVisible = false;
    Focusable = false;
    ClipToBounds = true;
  }

  protected override void OnRender(DrawingContext drawingContext)
  {
    base.OnRender(drawingContext);
    if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
    if (grain is null || grainSize != RenderSize)
    {
      grainSize = RenderSize;
      grain = CreateGrain(grainSize);
    }
    drawingContext.PushClip(new RectangleGeometry(new Rect(RenderSize), CornerRadius, CornerRadius));
    drawingContext.DrawDrawing(grain);
    drawingContext.Pop();
  }

  // Frozen vector geometry is reused while typing and scrolling. Bound the geometry
  // count on unusually large displays; nothing is repeated or stretched into bands.
  internal static DrawingGroup CreateGrain(Size size)
  {
    if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height))
      throw new ArgumentOutOfRangeException(nameof(size), "Paper dimensions must be finite.");
    ArgumentOutOfRangeException.ThrowIfNegative(size.Width);
    ArgumentOutOfRangeException.ThrowIfNegative(size.Height);
    DrawingGroup result = new();
    if (size.Width == 0 || size.Height == 0)
    {
      result.Freeze();
      return result;
    }
    double spacing = Math.Max(3, Math.Sqrt(size.Width * size.Height / 60000));
    Random random = new(73129);
    StreamGeometry shadows = new();
    StreamGeometry highlights = new();
    using (StreamGeometryContext dark = shadows.Open())
    using (StreamGeometryContext light = highlights.Open())
    {
      for (double y = 0; y < size.Height; y += spacing)
      {
        for (double x = 0; x < size.Width; x += spacing)
        {
          double px = Math.Min(size.Width, x + random.NextDouble() * spacing);
          double py = Math.Min(size.Height, y + random.NextDouble() * spacing);
          double length = 0.6 + random.NextDouble() * 2.8;
          double slope = (random.NextDouble() - 0.5) * 1.8;
          StreamGeometryContext context = random.Next(2) == 0 ? dark : light;
          context.BeginFigure(new Point(px, py), false, false);
          context.LineTo(new Point(px + length, py + slope), true, false);
        }
      }
    }
    shadows.Freeze();
    highlights.Freeze();
    using (DrawingContext context = result.Open())
    {
      context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(17, 93, 100, 110)), 0.65), shadows);
      context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(105, 255, 255, 255)), 0.75), highlights);
    }
    result.Freeze();
    return result;
  }
}
