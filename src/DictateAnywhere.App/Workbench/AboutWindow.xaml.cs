using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DictateAnywhere.App.Workbench;

public partial class AboutWindow : Window
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

  private static readonly HashSet<string> LegalDocumentNames = new(StringComparer.Ordinal)
  {
    "LICENSE",
    "DISCLAIMER.md",
    "PRIVACY.md",
    "SECURITY.md",
    "MODEL_LICENSES.md",
    "THIRD_PARTY_NOTICES.md",
  };

  public AboutWindow()
  {
    InitializeComponent();
    VersionTextBlock.Text = FormatVersion(Assembly.GetEntryAssembly()?.GetName().Version);
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
    double viewportWidth = AboutContentScrollViewer.ViewportWidth;
    if (viewportWidth <= 0d)
    {
      viewportWidth = AboutContentScrollViewer.ActualWidth - SystemParameters.VerticalScrollBarWidth;
    }

    if (viewportWidth <= 0d)
    {
      return;
    }

    double contentWidth = CalculateContentWidth(viewportWidth, zoomPercent);
    if (double.IsNaN(AboutContentPanel.Width) || Math.Abs(AboutContentPanel.Width - contentWidth) > 0.5d)
    {
      AboutContentPanel.Width = contentWidth;
    }
  }

  private bool IsHeroVisible()
  {
    if (!AboutContentScrollViewer.IsLoaded)
    {
      return false;
    }

    Rect heroBounds = AboutHeroTextBlock.TransformToAncestor(AboutContentScrollViewer)
      .TransformBounds(new Rect(AboutHeroTextBlock.RenderSize));
    return heroBounds.Bottom > 0d && heroBounds.Top < AboutContentScrollViewer.ViewportHeight;
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
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(AboutContentScrollViewer.ScrollToHome));
      }
      return;
    }

    zoomPercent = percent;
    Resources["About.Prose.MaxWidth"] = 920d + (3d * (percent - 100));
    foreach (string resourceKey in ZoomedTextResources)
    {
      if (percent == 100)
      {
        Resources.Remove(resourceKey);
      }
      else if (Application.Current?.TryFindResource(resourceKey) is double baseSize)
      {
        // Window-local tokens enlarge text and line spacing while allowing content to reflow.
        Resources[resourceKey] = baseSize * percent / 100d;
      }
    }

    UpdateResponsiveLayout();
    if (revealHero)
    {
      // A retained scroll offset can hide the enlarged heading beneath the fixed title bar.
      Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(AboutContentScrollViewer.ScrollToHome));
    }
  }

  private void OnOpenLegalDocumentClick(object sender, RoutedEventArgs e)
  {
    if (sender is not Button { Tag: string fileName } || !LegalDocumentNames.Contains(fileName))
    {
      return;
    }

    string legalRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "legal"));
    string path = Path.GetFullPath(Path.Combine(legalRoot, fileName));
    if (!path.StartsWith(legalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || !File.Exists(path))
    {
      MessageBox.Show(this, "That legal document is not available in this build.", "Koncus Nai", MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
  }

  private static string FormatVersion(Version? version)
  {
    if (version is null)
    {
      return "Current build";
    }

    return version.Build > 0
      ? $"Version {version.Major}.{version.Minor}.{version.Build}"
      : $"Version {version.Major}.{version.Minor}";
  }

}
