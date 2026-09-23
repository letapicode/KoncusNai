using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Paints sentence bands behind the arranged inline controls without affecting text layout.</summary>
public sealed class ReaderHighlightTextBlock : TextBlock
{
  private IReadOnlyList<ReaderWordVisual> words = [];
  private (int Start, int End) range = (-1, -1);
  private ReaderHighlightSurfaceTreatment treatment;
  private Brush accent = Brushes.Transparent;
  private Brush fill = Brushes.Transparent;
  private Rect[] bands = [];
  private bool underline;
  private (Rect Bounds, bool Active)[] previousBoxes = [];

  public ReaderHighlightTextBlock()
  {
    // Inline positions can change without this element's size changing (font, wrapping, or direction).
    LayoutUpdated += (_, _) => RefreshBands();
  }

  internal IReadOnlyList<Rect> HighlightBands => bands;

  internal void SetHighlight(IReadOnlyList<ReaderWordVisual> visuals, (int Start, int End) sentence,
    ReaderHighlightSurfaceTreatment surface, Brush solidAccent, Brush softAccent, bool drawUnderline = false)
  {
    words = visuals.ToArray();
    underline = drawUnderline;
    range = sentence;
    treatment = surface;
    accent = solidAccent;
    fill = surface == ReaderHighlightSurfaceTreatment.SoftAccent ? softAccent : solidAccent;
    RefreshBands();
    PaintBands();
  }

  private void RefreshBands()
  {
    List<(Rect Bounds, bool Active)> boxes = [];
    if (range.Start >= 0 && (treatment != ReaderHighlightSurfaceTreatment.None || underline))
    {
      foreach (ReaderWordVisual word in words)
      {
        if (!word.Surface.IsArrangeValid || !IsAncestorOf(word.Surface))
        {
          continue;
        }
        Rect bounds = word.Surface.TransformToAncestor(this).TransformBounds(new Rect(word.Surface.RenderSize));
        if (bounds.Width > 0 && bounds.Height > 0)
        {
          boxes.Add((bounds, word.Index >= range.Start && word.Index <= range.End));
        }
      }
    }

    if (previousBoxes.SequenceEqual(boxes)) return;
    previousBoxes = boxes.ToArray();
    List<Rect> next = [];
    // Group by physical line, then physical horizontal order: logical token order is insufficient for bidi text.
    var ordered = boxes.OrderBy(box => box.Bounds.Top).ToArray();
    int position = 0;
    while (position < ordered.Length)
    {
      Rect first = ordered[position].Bounds;
      List<(Rect Bounds, bool Active)> line = [];
      do
      {
        line.Add(ordered[position++]);
      }
      while (position < ordered.Length
        && Math.Min(first.Bottom, ordered[position].Bounds.Bottom) - Math.Max(first.Top, ordered[position].Bounds.Top)
          > Math.Min(first.Height, ordered[position].Bounds.Height) / 2);
      line.Sort((left, right) => left.Bounds.Left.CompareTo(right.Bounds.Left));
      double top = line.Min(box => box.Bounds.Top);
      double bottom = line.Max(box => box.Bounds.Bottom);
      Rect band = Rect.Empty;
      foreach (var box in line)
      {
        if (box.Active)
        {
          Rect part = new(box.Bounds.Left, top, box.Bounds.Width, bottom - top);
          if (band.IsEmpty) band = part;
          else band.Union(part);
        }
        else if (!band.IsEmpty)
        {
          next.Add(band);
          band = Rect.Empty;
        }
      }
      if (!band.IsEmpty) next.Add(band);
    }
    if (!bands.SequenceEqual(next))
    {
      bands = next.ToArray();
      PaintBands();
    }
  }

  private void PaintBands()
  {
    DrawingGroup drawing = new();
    using (DrawingContext context = drawing.Open())
    {
      // Fill all bands as one geometry so negative inline margins cannot double the
      // translucent color where adjacent lines touch or overlap.
      GeometryGroup geometry = new() { FillRule = FillRule.Nonzero };
      foreach (Rect band in bands)
      {
        geometry.Children.Add(new RectangleGeometry(band, 5, 5));
      }
      bool outline = treatment == ReaderHighlightSurfaceTreatment.AccentOutline;
      if (underline)
      {
        foreach (Rect band in bands)
          context.DrawLine(new Pen(accent, 1.5), new Point(band.Left, Math.Min(ActualHeight - 1, band.Bottom - 2)),
            new Point(band.Right, Math.Min(ActualHeight - 1, band.Bottom - 2)));
      }
      else if (outline)
      {
        // Union before stroking: GeometryGroup alone retains internal rectangle edges.
        Geometry contour = Geometry.Empty;
        foreach (Rect band in bands)
        {
          Rect inside = band;
          inside.Intersect(new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
          if (!inside.IsEmpty)
            contour = Geometry.Combine(contour, new RectangleGeometry(inside, 5, 5), GeometryCombineMode.Union, null);
        }
        context.DrawGeometry(null, new Pen(accent, 2), contour);
      }
      else context.DrawGeometry(fill, null, geometry);
    }
    drawing.Freeze();
    // Absolute coordinates keep the drawing aligned with inline children instead of stretching
    // the highlighted sentence to the entire text block's bounds.
    Background = new DrawingBrush(drawing)
    {
      ViewboxUnits = BrushMappingMode.Absolute,
      ViewportUnits = BrushMappingMode.Absolute,
      Viewbox = new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)),
      Viewport = new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)),
      Stretch = Stretch.Fill,
    };
  }
}
