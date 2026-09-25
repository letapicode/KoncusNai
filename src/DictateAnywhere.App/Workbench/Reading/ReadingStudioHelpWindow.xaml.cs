using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DictateAnywhere.App.Workbench.Reading;

public partial class ReadingStudioHelpWindow : Window
{
  private const int MinimumZoomPercent = 80;
  private const int MaximumZoomPercent = 200;
  private const int ZoomStepPercent = 10;
  private const double HorizontalContentInset = 84d;
  private const double BaseContentMaxWidth = 1000d;

  private static readonly string[] ZoomedTextResources =
  [
    "Font.Size.Caption",
    "Font.Size.FieldLabel",
    "Font.Size.Body",
    "Font.Size.BodyComfortable",
    "Font.Size.Control",
    "Font.Size.Lead",
    "Font.Size.Section",
    "Font.Size.Subheading",
    "Font.Size.Heading",
    "Font.Size.Display",
    "Font.Size.Hero",
    "Line.Height.Caption",
    "Line.Height.Body",
    "Line.Height.Lead",
  ];

  private int zoomPercent = 100;

  public ReadingStudioHelpWindow()
  {
    InitializeComponent();
  }

  private void OnContentViewportSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();

  private void OnContentScrollChanged(object sender, ScrollChangedEventArgs e)
  {
    if (Math.Abs(e.ViewportWidthChange) > 0.5d)
    {
      UpdateResponsiveLayout();
    }
  }

  internal static double CalculateContentWidth(double viewportWidth, int percent) =>
    Math.Max(0d, Math.Min(viewportWidth - HorizontalContentInset, BaseContentMaxWidth + (4d * (percent - 100))));

  private void UpdateResponsiveLayout()
  {
    double viewportWidth = HelpContentScrollViewer.ViewportWidth;
    if (viewportWidth <= 0d)
    {
      viewportWidth = HelpContentScrollViewer.ActualWidth - SystemParameters.VerticalScrollBarWidth;
    }

    if (viewportWidth <= 0d)
    {
      return;
    }

    double contentWidth = CalculateContentWidth(viewportWidth, zoomPercent);
    if (double.IsNaN(HelpContentPanel.Width) || Math.Abs(HelpContentPanel.Width - contentWidth) > 0.5d)
    {
      HelpContentPanel.Width = contentWidth;
    }
  }

  private bool IsHeroVisible()
  {
    if (!HelpContentScrollViewer.IsLoaded)
    {
      return false;
    }

    Rect heroBounds = HelpHeroTextBlock.TransformToAncestor(HelpContentScrollViewer)
      .TransformBounds(new Rect(HelpHeroTextBlock.RenderSize));
    return heroBounds.Bottom > 0d && heroBounds.Top < HelpContentScrollViewer.ViewportHeight;
  }

  private void OnPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (HandleZoomShortcut(e.Key, e.KeyboardDevice.Modifiers))
    {
      e.Handled = true;
    }
  }

  internal bool HandleZoomShortcut(Key key, ModifierKeys modifiers)
  {
    if (modifiers != ModifierKeys.Control && modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
    {
      return false;
    }

    int? targetPercent = key switch
    {
      Key.Add or Key.OemPlus => zoomPercent + ZoomStepPercent,
      Key.Subtract or Key.OemMinus when modifiers == ModifierKeys.Control => zoomPercent - ZoomStepPercent,
      Key.D0 or Key.NumPad0 when modifiers == ModifierKeys.Control => 100,
      _ => null,
    };

    if (targetPercent is null)
    {
      return false;
    }

    SetZoom(Math.Clamp(targetPercent.Value, MinimumZoomPercent, MaximumZoomPercent));
    return true;
  }

  private void SetZoom(int percent)
  {
    bool revealHero = IsHeroVisible();
    if (percent == zoomPercent)
    {
      if (revealHero)
      {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(HelpContentScrollViewer.ScrollToHome));
      }
      return;
    }

    zoomPercent = percent;
    Resources["Help.Prose.MaxWidth"] = 920d + (3d * (percent - 100));
    foreach (string resourceKey in ZoomedTextResources)
    {
      if (percent == 100)
      {
        Resources.Remove(resourceKey);
      }
      else if (Application.Current?.TryFindResource(resourceKey) is double baseSize)
      {
        Resources[resourceKey] = baseSize * percent / 100d;
      }
    }

    UpdateResponsiveLayout();
    if (revealHero)
    {
      Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(HelpContentScrollViewer.ScrollToHome));
    }
  }
}
