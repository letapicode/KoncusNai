using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench.Reading;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class ReadingStudioHelpWindowLayoutTests
{
  [Fact]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The STA worker reports failures to the asserting thread.")]
  public void HelpZoomShortcutsChangeOnlyWindowTextAndReset()
  {
    RunOnSta(() =>
    {
      Application application = EnsureApplication();
      using WindowScope scope = new(new ReadingStudioHelpWindow());
      ReadingStudioHelpWindow window = scope.Help;
      TextBlock hero = Assert.IsType<TextBlock>(window.FindName("HelpHeroTextBlock"));
      TextBlock lead = Assert.IsType<TextBlock>(window.FindName("HelpLeadTextBlock"));
      ScrollViewer viewport = Assert.IsType<ScrollViewer>(window.FindName("HelpContentScrollViewer"));
      double originalHero = hero.FontSize;
      double originalLead = lead.FontSize;
      double originalLineHeight = lead.LineHeight;
      double globalHero = Assert.IsType<double>(application.FindResource("Font.Size.Hero"));

      Assert.True(window.HandleZoomShortcut(Key.OemPlus, ModifierKeys.Control));
      Assert.True(window.HandleZoomShortcut(Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));
      Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
      Assert.Equal(originalHero * 1.3d, hero.FontSize, 6);
      Assert.True(window.HandleZoomShortcut(Key.OemMinus, ModifierKeys.Control));
      Assert.True(window.HandleZoomShortcut(Key.Subtract, ModifierKeys.Control));
      Assert.Equal(originalHero * 1.1d, hero.FontSize, 6);
      Assert.False(window.HandleZoomShortcut(Key.OemPlus, ModifierKeys.None));
      Assert.False(window.HandleZoomShortcut(Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift));
      for (int step = 0; step < 9; step++)
      {
        Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
      }
      Assert.Equal(originalHero * 2d, hero.FontSize, 6);
      Assert.Equal(originalLead * 2d, lead.FontSize, 6);
      Assert.Equal(originalLineHeight * 2d, lead.LineHeight, 6);
      Assert.True(window.HandleZoomShortcut(Key.D0, ModifierKeys.Control));
      Assert.Equal(originalHero, hero.FontSize, 6);
      Assert.Equal(globalHero, application.FindResource("Font.Size.Hero"));
      Assert.Equal(ScrollBarVisibility.Disabled, viewport.HorizontalScrollBarVisibility);
    });
  }

  [Fact]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The STA worker reports failures to the asserting thread.")]
  public void HelpLayoutStaysReadableFromTopToEndAtEveryZoom()
  {
    RunOnSta(() =>
    {
      EnsureApplication();
      ReadingStudioHelpWindow window = new()
      {
        Left = -10_000,
        Top = -10_000,
        ShowInTaskbar = false,
        ShowActivated = false,
        Opacity = 0,
      };
      using WindowScope scope = new(window);
      AppThemeManager.ApplyPalette(window.Resources, useDarkTheme: true);
      window.Show();

      StackPanel reference = Assert.IsType<StackPanel>(window.FindName("HelpReferencePanel"));
      Assert.Equal(10, reference.Children.Count);
      Assert.All(reference.Children.OfType<StackPanel>(), section => Assert.Equal(2, section.Children.Count));
      Assert.Empty(Descendants(reference).OfType<Expander>());
      Assert.Equal("1. Start with the page", Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(reference.Children[0]).Children[0]).Text);
      Assert.Equal("Good to know", Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(reference.Children[^1]).Children[0]).Text);

      foreach (int zoom in new[] { 100, 150, 200 })
      {
        SetZoom(window, zoom);
        AssertLayoutAtTop(window, zoom, "restored");
        AssertFooterAtEnd(window, zoom, "restored");
      }

      window.WindowState = WindowState.Maximized;
      foreach (int zoom in new[] { 100, 150, 200 })
      {
        SetZoom(window, zoom);
        AssertLayoutAtTop(window, zoom, "maximized");
        AssertFooterAtEnd(window, zoom, "maximized");
      }

      SetZoom(window, 100);
      Settle(window);
      ScrollViewer viewport = Assert.IsType<ScrollViewer>(window.FindName("HelpContentScrollViewer"));
      TextBlock hero = Assert.IsType<TextBlock>(window.FindName("HelpHeroTextBlock"));
      viewport.ScrollToVerticalOffset(60d);
      Settle(window);
      Rect partialHero = Bounds(hero, viewport);
      Assert.True(partialHero.Top < 0d && partialHero.Bottom > 0d);
      Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
      Settle(window);
      Assert.Equal(0d, viewport.VerticalOffset, 1);
      Assert.True(Bounds(hero, viewport).Top >= 24d);

      viewport.ScrollToVerticalOffset(hero.ActualHeight + 200d);
      Settle(window);
      Assert.True(Bounds(hero, viewport).Bottom < 0d);
      Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
      Settle(window);
      Assert.True(viewport.VerticalOffset > 0d, "Zooming lower on the page must retain reading position.");

      AssertFooterAtEnd(window, 120, "after-scrolled-zoom");
      Assert.True(window.HandleZoomShortcut(Key.OemMinus, ModifierKeys.Control));
      Settle(window);
      AssertFooterAtEnd(window, 110, "after-minus");
      Assert.True(window.HandleZoomShortcut(Key.D0, ModifierKeys.Control));
      Settle(window);
      AssertFooterAtEnd(window, 100, "after-reset");
    });
  }

  private static Application EnsureApplication()
  {
    if (Application.Current is null)
    {
      DictateAnywhere.App.App app = new();
      app.InitializeComponent();
    }
    return Application.Current ?? throw new InvalidOperationException("WPF application was not initialized.");
  }

  private static void SetZoom(ReadingStudioHelpWindow window, int percent)
  {
    Assert.True(window.HandleZoomShortcut(Key.D0, ModifierKeys.Control));
    for (int current = 100; current < percent; current += 10)
    {
      Assert.True(window.HandleZoomShortcut(Key.Add, ModifierKeys.Control));
    }
    Settle(window);
  }

  private static void AssertLayoutAtTop(ReadingStudioHelpWindow window, int percent, string mode)
  {
    Settle(window);
    ScrollViewer viewport = Assert.IsType<ScrollViewer>(window.FindName("HelpContentScrollViewer"));
    StackPanel content = Assert.IsType<StackPanel>(window.FindName("HelpContentPanel"));
    Border quickStart = Assert.IsType<Border>(window.FindName("HelpQuickStartCard"));
    TextBlock hero = Assert.IsType<TextBlock>(window.FindName("HelpHeroTextBlock"));
    Grid header = Assert.IsType<Grid>(window.FindName("HelpHeaderGrid"));

    Assert.Equal(0d, viewport.VerticalOffset, 1);
    Assert.Equal(ReadingStudioHelpWindow.CalculateContentWidth(viewport.ViewportWidth, percent), content.ActualWidth, 1);
    Assert.True(content.ActualWidth + content.Margin.Left + content.Margin.Right <= viewport.ViewportWidth + 1d);
    Assert.True(Bounds(hero, window).Top >= Bounds(header, window).Bottom + 20d,
      $"The entire heading must start below the fixed header at {mode} {percent}% zoom.");
    AssertHorizontalBounds(quickStart, viewport, percent);
    foreach (TextBlock text in Descendants(content).OfType<TextBlock>().Where(element => element.IsVisible))
    {
      AssertHorizontalBounds(text, viewport, percent);
    }
    CaptureIfRequested(window, viewport, $"{mode}-{percent}");
  }

  private static void AssertFooterAtEnd(ReadingStudioHelpWindow window, int percent, string mode)
  {
    ScrollViewer viewport = Assert.IsType<ScrollViewer>(window.FindName("HelpContentScrollViewer"));
    TextBlock footer = Assert.IsType<TextBlock>(window.FindName("HelpFinalNote"));
    Assert.True(StaticContentScrollViewerBehavior.HandleKey(viewport, Key.End, ModifierKeys.None));
    Settle(window);
    Rect footerBounds = Bounds(footer, viewport);
    Assert.True(footerBounds.Top >= 0d && footerBounds.Bottom <= viewport.ViewportHeight - 20d,
      $"The final paragraph must be fully visible with bottom space at {mode} {percent}%; footer={footerBounds}, viewport={viewport.ViewportHeight:F1}.");
    Assert.Equal(viewport.ScrollableHeight, viewport.VerticalOffset, 1);
    if (window.WindowState == WindowState.Maximized)
    {
      double screenBottom = window.PointToScreen(new Point(0d, window.ActualHeight)).Y;
      double workBottom = SystemParameters.WorkArea.Bottom * VisualTreeHelper.GetDpi(window).DpiScaleY;
      Assert.True(screenBottom <= workBottom + 18d,
        $"The maximized Help window extends behind the taskbar: {screenBottom:F1}px > {workBottom:F1}px.");
    }
    CaptureIfRequested(window, viewport, $"{mode}-bottom-{percent}");
    Assert.True(StaticContentScrollViewerBehavior.HandleKey(viewport, Key.Home, ModifierKeys.None));
    Settle(window);
  }

  private static void AssertHorizontalBounds(FrameworkElement element, ScrollViewer viewport, int percent)
  {
    Rect bounds = Bounds(element, viewport);
    Assert.True(bounds.Left >= -1d && bounds.Right <= viewport.ViewportWidth + 1d,
      $"{element.Name} extends beyond the visible width at {percent}% zoom: {bounds}.");
  }

  private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
    element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

  private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
  {
    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
      DependencyObject child = VisualTreeHelper.GetChild(root, index);
      yield return child;
      foreach (DependencyObject descendant in Descendants(child))
      {
        yield return descendant;
      }
    }
  }

  private static void Settle(Window window)
  {
    window.UpdateLayout();
    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    window.UpdateLayout();
  }

  private static void CaptureIfRequested(Window window, ScrollViewer viewport, string scenario)
  {
    string? directory = Environment.GetEnvironmentVariable("HELP_VISUAL_REVIEW_DIR");
    if (string.IsNullOrWhiteSpace(directory))
    {
      return;
    }

    Directory.CreateDirectory(directory);
    TextBlock hero = Assert.IsType<TextBlock>(window.FindName("HelpHeroTextBlock"));
    TextBlock footer = Assert.IsType<TextBlock>(window.FindName("HelpFinalNote"));
    Rect heroBounds = Bounds(hero, viewport);
    Rect footerBounds = Bounds(footer, viewport);
    File.AppendAllText(Path.Combine(directory, "measurements.txt"),
      $"{scenario} viewport={viewport.ViewportWidth:F1}x{viewport.ViewportHeight:F1} offset={viewport.VerticalOffset:F1} hero={heroBounds.Top:F1}..{heroBounds.Bottom:F1} footer={footerBounds.Top:F1}..{footerBounds.Bottom:F1} screenBottom={window.PointToScreen(new Point(0d, window.ActualHeight)).Y:F1}px workBottom={SystemParameters.WorkArea.Bottom * VisualTreeHelper.GetDpi(window).DpiScaleY:F1}px{Environment.NewLine}");
    FrameworkElement surface = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
    RenderTargetBitmap bitmap = new((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(surface);
    PngBitmapEncoder encoder = new();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using FileStream stream = File.Create(Path.Combine(directory, $"help-{scenario}.png"));
    encoder.Save(stream);
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The STA worker reports failures to the asserting thread.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try { action(); }
      catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA layout test thread timed out.");
    Assert.Null(failure);
  }

  private sealed class WindowScope : IDisposable
  {
    public WindowScope(ReadingStudioHelpWindow help) => Help = help;
    public ReadingStudioHelpWindow Help { get; }
    public void Dispose() => Help.Close();
  }
}
