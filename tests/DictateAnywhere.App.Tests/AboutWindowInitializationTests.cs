using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(WpfApplicationCollection.Name)]
public sealed class AboutWindowInitializationTests
{
  [Xunit.Fact]
  [Xunit.Trait("Category", "WindowsWpf")]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The test must report any WPF construction failure to the asserting thread.")]
  public void AboutWindow_IsLargeAndResizable()
  {
    Exception? failure = null;
    bool isLargeAndResizable = false;
    AboutWindow? window = null;
    Thread thread = new(() =>
    {
      try
      {
        if (System.Windows.Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }
        window = new AboutWindow();
        isLargeAndResizable = window.Width >= 900
          && window.Height >= 700
          && window.MinWidth >= 700
          && window.ResizeMode == System.Windows.ResizeMode.CanResize;
      }
      catch (Exception ex)
      {
        failure = ex;
      }
      finally
      {
        window?.Close();
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(15));

    Xunit.Assert.True(completed, "STA test thread timed out.");
    Xunit.Assert.Null(failure);
    Xunit.Assert.True(isLargeAndResizable);
  }

  [Xunit.Fact]
  public void AboutWindow_DescribesCurrentFeatureSet()
  {
    XDocument document = XDocument.Load(FindAboutWindowPath());
    string[] visibleText = document
      .Descendants()
      .Attributes()
      .Where(attribute => attribute.Name.LocalName is "Text" or "Content")
      .Select(attribute => attribute.Value)
      .ToArray();

    Xunit.Assert.Contains(visibleText, text => text.Contains("Reading Studio", StringComparison.Ordinal));
    Xunit.Assert.Contains(visibleText, text => text.Contains("Indic Parler", StringComparison.Ordinal));
    Xunit.Assert.Contains(visibleText, text => text.Contains("YouTube", StringComparison.Ordinal));
    Xunit.Assert.Contains(visibleText, text => text.Contains("Ollama is kept on demand", StringComparison.Ordinal));
    XElement features = Xunit.Assert.Single(document.Descendants().Where(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "AboutFeaturesPanel"));
    XElement[] sections = features.Elements().ToArray();
    Xunit.Assert.Equal(8, sections.Length);
    Xunit.Assert.All(sections, section => Xunit.Assert.Equal(2, section.Elements().Count()));
    Xunit.Assert.Empty(features.Descendants().Where(element => element.Name.LocalName == "Expander"));
    Xunit.Assert.DoesNotContain("â€", File.ReadAllText(FindAboutWindowPath()), StringComparison.Ordinal);
  }

  [Xunit.Fact]
  [Xunit.Trait("Category", "WindowsWpf")]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The test must report any WPF failure to the asserting thread.")]
  public void AboutWindow_ZoomShortcutsResizeTextAndResetWhileMaximized()
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      AboutWindow? window = null;
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }

        Application application = Application.Current ?? throw new InvalidOperationException("WPF application was not initialized.");
        window = new AboutWindow { WindowState = WindowState.Maximized };
        TextBlock hero = Xunit.Assert.IsType<TextBlock>(window.FindName("AboutHeroTextBlock"));
        TextBlock lead = Xunit.Assert.IsType<TextBlock>(window.FindName("AboutLeadTextBlock"));
        ScrollViewer content = Xunit.Assert.IsType<ScrollViewer>(window.FindName("AboutContentScrollViewer"));
        double originalSize = hero.FontSize;
        double originalLeadSize = lead.FontSize;
        double originalLineHeight = lead.LineHeight;
        double applicationSize = Xunit.Assert.IsType<double>(application.FindResource("Font.Size.Hero"));

        Xunit.Assert.True(window.HandleZoomShortcut(Key.OemPlus, ModifierKeys.Control));
        Xunit.Assert.Equal(originalSize * 1.1d, hero.FontSize, 6);
        Xunit.Assert.True(window.HandleZoomShortcut(Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));
        Xunit.Assert.Equal(originalSize * 1.2d, hero.FontSize, 6);
        Xunit.Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
        Xunit.Assert.Equal(originalSize * 1.3d, hero.FontSize, 6);
        Xunit.Assert.True(window.HandleZoomShortcut(Key.OemMinus, ModifierKeys.Control));
        Xunit.Assert.Equal(originalSize * 1.2d, hero.FontSize, 6);
        for (int step = 0; step < 8; step++)
        {
          Xunit.Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
        }
        Xunit.Assert.Equal(originalSize * 2d, hero.FontSize, 6);
        Xunit.Assert.Equal(originalLeadSize * 2d, lead.FontSize, 6);
        Xunit.Assert.Equal(originalLineHeight * 2d, lead.LineHeight, 6);
        Xunit.Assert.False(window.HandleZoomShortcut(Key.OemPlus, ModifierKeys.None));
        Xunit.Assert.True(window.HandleZoomShortcut(Key.D0, ModifierKeys.Control));
        Xunit.Assert.Equal(originalSize, hero.FontSize, 6);
        Xunit.Assert.Equal(applicationSize, application.FindResource("Font.Size.Hero"));
        Xunit.Assert.Equal(ScrollBarVisibility.Disabled, content.HorizontalScrollBarVisibility);
        Xunit.Assert.Equal(TextWrapping.Wrap, hero.TextWrapping);
      }
      catch (Exception ex)
      {
        failure = ex;
      }
      finally
      {
        window?.Close();
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Xunit.Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA test thread timed out.");
    Xunit.Assert.Null(failure);
  }

  [Xunit.Fact]
  [Xunit.Trait("Category", "WindowsWpf")]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The test must report any WPF layout failure to the asserting thread.")]
  public void AboutWindow_ContentReflowsAtRestoredAndWideSizesAcrossZoomLevels()
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      AboutWindow? window = null;
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }

        window = new AboutWindow
        {
          Left = -10_000,
          Top = -10_000,
          ShowInTaskbar = false,
          ShowActivated = false,
          Opacity = 0,
        };
        if (Environment.GetEnvironmentVariable("ABOUT_VISUAL_REVIEW_DARK") == "1")
        {
          AppThemeManager.ApplyPalette(window.Resources, useDarkTheme: true);
        }
        window.Show();
        foreach (Expander expander in LogicalDescendants(window).OfType<Expander>())
        {
          expander.IsExpanded = true;
        }

        foreach (double width in new[] { 920d, 1600d })
        {
          window.Width = width;
          foreach (int zoom in new[] { 100, 150, 200 })
          {
            SetZoom(window, zoom);
            AssertResponsiveLayout(window, zoom, expectWide: width == 1600d);
            AssertFooterAtEnd(window, zoom);
          }
        }

        window.WindowState = WindowState.Maximized;
        foreach (int zoom in new[] { 100, 150, 200 })
        {
          SetZoom(window, zoom);
          AssertResponsiveLayout(window, zoom, expectWide: false);
          AssertFooterAtEnd(window, zoom);
        }

        SetZoom(window, 100);
        window.UpdateLayout();
        ScrollViewer viewport = Xunit.Assert.IsType<ScrollViewer>(window.FindName("AboutContentScrollViewer"));
        TextBlock hero = Xunit.Assert.IsType<TextBlock>(window.FindName("AboutHeroTextBlock"));
        viewport.ScrollToVerticalOffset(60d);
        window.UpdateLayout();
        Rect partiallyVisibleHero = hero.TransformToAncestor(viewport).TransformBounds(new Rect(hero.RenderSize));
        Xunit.Assert.True(partiallyVisibleHero.Top < 0d && partiallyVisibleHero.Bottom > 0d);
        Xunit.Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
        Xunit.Assert.Equal(0d, viewport.VerticalOffset, 1);
        Rect visibleHero = hero.TransformToAncestor(viewport).TransformBounds(new Rect(hero.RenderSize));
        Xunit.Assert.True(visibleHero.Top >= 32d, "Zoom must reveal the heading below the fixed header.");
        CaptureLayoutIfRequested(window, viewport, 110, "after-partial-hero-zoom");

        viewport.ScrollToVerticalOffset(60d);
        window.UpdateLayout();
        Xunit.Assert.True(window.HandleZoomShortcut(Key.OemMinus, ModifierKeys.Control));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
        Xunit.Assert.Equal(0d, viewport.VerticalOffset, 1);
        Xunit.Assert.True(hero.TransformToAncestor(viewport).TransformBounds(new Rect(hero.RenderSize)).Top >= 32d);

        viewport.ScrollToVerticalOffset(60d);
        window.UpdateLayout();
        Xunit.Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
        viewport.ScrollToVerticalOffset(60d);
        window.UpdateLayout();
        Xunit.Assert.True(window.HandleZoomShortcut(Key.D0, ModifierKeys.Control));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
        Xunit.Assert.Equal(0d, viewport.VerticalOffset, 1);
        Xunit.Assert.True(hero.TransformToAncestor(viewport).TransformBounds(new Rect(hero.RenderSize)).Top >= 32d);

        viewport.ScrollToVerticalOffset(hero.ActualHeight + 200d);
        window.UpdateLayout();
        Xunit.Assert.True(hero.TransformToAncestor(viewport).TransformBounds(new Rect(hero.RenderSize)).Bottom < 0d);
        Xunit.Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
        Xunit.Assert.True(viewport.VerticalOffset > 0d, "Zooming lower on the page must preserve reading position.");
        CaptureLayoutIfRequested(window, viewport, 110, "after-scrolled-zoom");
      }
      catch (Exception ex)
      {
        failure = ex;
      }
      finally
      {
        window?.Close();
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Xunit.Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA layout test thread timed out.");
    Xunit.Assert.Null(failure);
  }

  private static void SetZoom(AboutWindow window, int percent)
  {
    Xunit.Assert.True(window.HandleZoomShortcut(Key.D0, ModifierKeys.Control));
    for (int current = 100; current < percent; current += 10)
    {
      Xunit.Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
    }
  }

  private static void AssertResponsiveLayout(AboutWindow window, int percent, bool expectWide)
  {
    window.UpdateLayout();
    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    window.UpdateLayout();
    ScrollViewer viewport = Xunit.Assert.IsType<ScrollViewer>(window.FindName("AboutContentScrollViewer"));
    StackPanel content = Xunit.Assert.IsType<StackPanel>(window.FindName("AboutContentPanel"));
    Border dataCard = Xunit.Assert.IsType<Border>(window.FindName("AboutDataCard"));
    Border modelsCard = Xunit.Assert.IsType<Border>(window.FindName("AboutModelsCard"));
    WrapPanel badges = Xunit.Assert.IsType<WrapPanel>(window.FindName("AboutBadgesPanel"));
    CaptureLayoutIfRequested(window, viewport, percent);

    Xunit.Assert.True(viewport.ViewportWidth > 0d);
    double expectedWidth = AboutWindow.CalculateContentWidth(viewport.ViewportWidth, percent);
    Xunit.Assert.Equal(expectedWidth, content.ActualWidth, 1);
    Xunit.Assert.True(content.ActualWidth + content.Margin.Left + content.Margin.Right <= viewport.ViewportWidth + 1d);
    if (expectWide)
    {
      Xunit.Assert.True(content.ActualWidth > 820d, "A wide window must use more than the original 820-DIP column.");
    }

    Rect dataBounds = dataCard.TransformToAncestor(viewport).TransformBounds(new Rect(dataCard.RenderSize));
    Rect modelBounds = modelsCard.TransformToAncestor(viewport).TransformBounds(new Rect(modelsCard.RenderSize));
    Xunit.Assert.True(modelBounds.Top >= dataBounds.Bottom,
      "The information cards must follow one another without an uneven column gap.");

    if (viewport.VerticalOffset < 0.5d)
    {
      Grid header = Xunit.Assert.IsType<Grid>(window.FindName("AboutHeaderGrid"));
      TextBlock hero = Xunit.Assert.IsType<TextBlock>(window.FindName("AboutHeroTextBlock"));
      Rect headerBounds = header.TransformToAncestor(window).TransformBounds(new Rect(header.RenderSize));
      Rect heroBounds = hero.TransformToAncestor(window).TransformBounds(new Rect(hero.RenderSize));
      Xunit.Assert.True(heroBounds.Top >= headerBounds.Bottom + 30d,
        "The full heading must start below the fixed header at the top of the page.");
    }

    foreach (FrameworkElement element in new FrameworkElement[] { dataCard, modelsCard, badges })
    {
      Rect bounds = element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
      Xunit.Assert.True(bounds.Left >= -1d && bounds.Right <= viewport.ViewportWidth + 1d,
        $"{element.Name} extends beyond the visible horizontal area at {percent}% zoom.");
    }

    foreach (FrameworkElement badge in badges.Children.OfType<FrameworkElement>())
    {
      Rect bounds = badge.TransformToAncestor(viewport).TransformBounds(new Rect(badge.RenderSize));
      Xunit.Assert.True(bounds.Right <= viewport.ViewportWidth + 1d,
        $"A badge extends beyond the visible horizontal area at {percent}% zoom.");
    }

    foreach (TextBlock text in VisualDescendants(content).OfType<TextBlock>().Where(element => element.IsVisible))
    {
      Rect bounds = text.TransformToAncestor(viewport).TransformBounds(new Rect(text.RenderSize));
      Xunit.Assert.True(bounds.Left >= -1d && bounds.Right <= viewport.ViewportWidth + 1d,
        $"Text '{text.Text}' extends beyond the visible horizontal area at {percent}% zoom.");
    }
  }

  private static void AssertFooterAtEnd(AboutWindow window, int percent)
  {
    ScrollViewer viewport = Xunit.Assert.IsType<ScrollViewer>(window.FindName("AboutContentScrollViewer"));
    TextBlock footer = Xunit.Assert.IsType<TextBlock>(window.FindName("AboutFinalNote"));
    Expander runtimeDetails = Xunit.Assert.Single(LogicalDescendants(window).OfType<Expander>());
    runtimeDetails.IsExpanded = false;
    window.UpdateLayout();
    runtimeDetails.IsExpanded = true;
    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    window.UpdateLayout();
    Xunit.Assert.True(StaticContentScrollViewerBehavior.HandleKey(viewport, Key.End, ModifierKeys.None));
    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    window.UpdateLayout();
    Rect footerBounds = footer.TransformToAncestor(viewport).TransformBounds(new Rect(footer.RenderSize));
    Xunit.Assert.True(footerBounds.Top >= 0d && footerBounds.Bottom <= viewport.ViewportHeight - 20d,
      $"The full final paragraph needs bottom space at {percent}% zoom; footer={footerBounds} viewport={viewport.ViewportHeight:F1}.");
    Xunit.Assert.Equal(viewport.ScrollableHeight, viewport.VerticalOffset, 1);

    if (window.WindowState == WindowState.Maximized)
    {
      Rect workArea = SystemParameters.WorkArea;
      double dpiScaleY = VisualTreeHelper.GetDpi(window).DpiScaleY;
      double screenBottom = window.PointToScreen(new Point(0d, window.ActualHeight)).Y;
      Xunit.Assert.True(screenBottom <= (workArea.Bottom + 12d) * dpiScaleY,
        $"The maximized window must end above the taskbar; screen bottom={screenBottom:F1}px, work area bottom={workArea.Bottom * dpiScaleY:F1}px.");
    }

    CaptureLayoutIfRequested(window, viewport, percent, window.WindowState == WindowState.Maximized ? "maximized-bottom" : window.Width < 1000d ? "restored-bottom" : "wide-bottom");
    Xunit.Assert.True(StaticContentScrollViewerBehavior.HandleKey(viewport, Key.Home, ModifierKeys.None));
    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    window.UpdateLayout();
  }

  private static void CaptureLayoutIfRequested(AboutWindow window, ScrollViewer viewport, int percent, string? scenario = null)
  {
    string? directory = Environment.GetEnvironmentVariable("ABOUT_VISUAL_REVIEW_DIR");
    if (string.IsNullOrWhiteSpace(directory))
    {
      return;
    }

    Directory.CreateDirectory(directory);
    string mode = scenario ?? (window.WindowState == WindowState.Maximized ? "maximized" : window.Width < 1000d ? "restored" : "wide");
    TextBlock hero = Xunit.Assert.IsType<TextBlock>(window.FindName("AboutHeroTextBlock"));
    TextBlock footer = Xunit.Assert.IsType<TextBlock>(window.FindName("AboutFinalNote"));
    Rect heroBounds = hero.TransformToAncestor(viewport).TransformBounds(new Rect(hero.RenderSize));
    Rect footerBounds = footer.TransformToAncestor(viewport).TransformBounds(new Rect(footer.RenderSize));
    File.AppendAllText(Path.Combine(directory, "measurements.txt"),
      $"{mode} {percent}% viewport={viewport.ViewportWidth:F1}x{viewport.ViewportHeight:F1} offset={viewport.VerticalOffset:F1} hero={heroBounds.Top:F1}..{heroBounds.Bottom:F1} height={hero.ActualHeight:F1} footer={footerBounds.Top:F1}..{footerBounds.Bottom:F1} screenBottom={window.PointToScreen(new Point(0d, window.ActualHeight)).Y:F1}px workBottom={SystemParameters.WorkArea.Bottom * VisualTreeHelper.GetDpi(window).DpiScaleY:F1}px{Environment.NewLine}");

    FrameworkElement surface = Xunit.Assert.IsAssignableFrom<FrameworkElement>(window.Content);
    RenderTargetBitmap bitmap = new((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(surface);
    PngBitmapEncoder encoder = new();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using FileStream stream = File.Create(Path.Combine(directory, $"about-{mode}-{percent}.png"));
    encoder.Save(stream);
  }

  private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
  {
    foreach (object child in LogicalTreeHelper.GetChildren(root))
    {
      if (child is not DependencyObject element)
      {
        continue;
      }

      yield return element;
      foreach (DependencyObject descendant in LogicalDescendants(element))
      {
        yield return descendant;
      }
    }
  }

  private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
  {
    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
      DependencyObject child = VisualTreeHelper.GetChild(root, index);
      yield return child;
      foreach (DependencyObject descendant in VisualDescendants(child))
      {
        yield return descendant;
      }
    }
  }

  private static string FindAboutWindowPath()
  {
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
      string candidate = Path.Combine(directory.FullName, "src", "DictateAnywhere.App", "Workbench", "AboutWindow.xaml");
      if (File.Exists(candidate))
      {
        return candidate;
      }

      directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the repository root containing AboutWindow.xaml.");
  }
}
