using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace DictateAnywhere.App.Presentation;

/// <summary>Provides horizontal access when a code block's own scrollbar is below the chat viewport.</summary>
internal sealed class ChatCodeScrollOverlay(Canvas layer, FrameworkElement viewport)
{
  private readonly List<Entry> entries = [];

  internal void Rebuild(BlockCollection blocks, bool isPaper)
  {
    Entry[] previous = entries.ToArray();
    entries.Clear();
    layer.Children.Clear();
    foreach (RichTextBox code in FindCode(blocks))
    {
      ScrollBar bar = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed,
        Focusable = true, IsTabStop = true, SmallChange = 40 };
      AutomationProperties.SetName(bar, "Scroll code horizontally");
      bar.SetResourceReference(Control.BackgroundProperty, isPaper ? "Brush.Paper.Page" : "Brush.Code.Background");
      int index = entries.Count;
      double? restore = index < previous.Length && Equals(previous[index].Code.Tag, code.Tag)
        ? previous[index].Code.HorizontalOffset : null;
      if (restore.HasValue)
        ChatCodeWrapping.SetIsWrapped(code, ChatCodeWrapping.GetIsWrapped(previous[index].Code));
      entries.Add(new Entry(code, bar, restore));
      bar.Scroll += (_, args) => code.ScrollToHorizontalOffset(args.NewValue);
      layer.Children.Add(bar);
    }
  }

  internal void Clear()
  {
    entries.Clear();
    layer.Children.Clear();
    layer.Height = 0;
  }

  internal void Refresh()
  {
    bool anyVisible = false;
    foreach (Entry entry in entries)
    {
      RichTextBox code = entry.Code;
      ScrollBar bar = entry.Bar;
      if (ChatCodeWrapping.GetIsWrapped(code))
      {
        entry.RestoreOffset = null;
        bar.Visibility = Visibility.Collapsed;
        continue;
      }
      if (!code.IsVisible || !code.IsDescendantOf(viewport) || code.ActualWidth <= 0)
      {
        bar.Visibility = Visibility.Collapsed;
        continue;
      }
      double maximum = Math.Max(0, code.ExtentWidth - code.ViewportWidth);
      if (entry.RestoreOffset is double offset && code.ViewportWidth > 0 && code.ExtentWidth > 0
        && code.Document.ContentStart.HasValidLayout)
      {
        entry.RestoreOffset = null;
        code.ScrollToHorizontalOffset(Math.Min(offset, maximum));
      }
      Rect bounds = code.TransformToAncestor(viewport).TransformBounds(new Rect(code.RenderSize));
      double top = Math.Max(0, bounds.Top);
      double bottom = Math.Min(viewport.ActualHeight, bounds.Bottom);
      // Use the native bar when its bottom is on screen; avoid overlaying a second bar.
      if (maximum <= 0.5 || bottom - top < 40 || bounds.Bottom <= viewport.ActualHeight)
      {
        bar.Visibility = Visibility.Collapsed;
        continue;
      }
      double left = Math.Max(0, bounds.Left);
      double right = Math.Min(viewport.ActualWidth, bounds.Right);
      if (right <= left) { bar.Visibility = Visibility.Collapsed; continue; }
      bar.Minimum = 0;
      bar.Maximum = maximum;
      bar.ViewportSize = code.ViewportWidth;
      bar.LargeChange = code.ViewportWidth;
      bar.Value = Math.Clamp(code.HorizontalOffset, 0, maximum);
      bar.Width = right - left;
      bar.Height = 12;
      Canvas.SetLeft(bar, left);
      Canvas.SetTop(bar, 0);
      bar.Visibility = Visibility.Visible;
      anyVisible = true;
    }
    // Reserve a small rail below the transcript instead of covering the last visible code line.
    layer.Height = anyVisible ? 12 : 0;
  }

  private static IEnumerable<RichTextBox> FindCode(BlockCollection blocks)
  {
    foreach (Block block in blocks)
    {
      switch (block)
      {
        case BlockUIContainer { Child: Border { Child: Grid grid } }:
          foreach (RichTextBox code in grid.Children.OfType<RichTextBox>().Where(code => code.Tag is string)) yield return code;
          break;
        case Section section:
          foreach (RichTextBox code in FindCode(section.Blocks)) yield return code;
          break;
        case System.Windows.Documents.List list:
          foreach (ListItem item in list.ListItems)
            foreach (RichTextBox code in FindCode(item.Blocks)) yield return code;
          break;
      }
    }
  }

  private sealed class Entry(RichTextBox code, ScrollBar bar, double? restoreOffset)
  {
    internal RichTextBox Code { get; } = code;
    internal ScrollBar Bar { get; } = bar;
    internal double? RestoreOffset { get; set; } = restoreOffset;
  }
}
