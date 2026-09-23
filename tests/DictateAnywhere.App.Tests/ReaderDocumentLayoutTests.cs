using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class ReaderDocumentLayoutTests
{
  private const double OuterGutterTolerance = 1d;
  private const double EffectiveGutterTolerance = 2d;

  [Theory]
  [InlineData(640d, 700d)]
  [InlineData(641d, 700d)]
  [InlineData(1000d, 700d)]
  [InlineData(1180d, 860d)]
  [InlineData(1400d, 860d)]
  public void StandardDraft_UsesBalancedRenderedPageToEditorGutters(double width, double height)
  {
    RunOnSta(() =>
    {
      Window window = CreateOffscreenWindow(width, height);
      using ReaderDocumentView view = new();
      window.Content = view;
      try
      {
        view.RenderSurface(ReaderDocumentSurface.Draft);
        window.Show();
        SettleLayout(window);

        Border frame = Find<Border>(view, "ReaderPageFrame");
        TextBox editor = Find<TextBox>(view, "DraftTextBox");
        (double left, double right) = GetHorizontalGutters(frame, editor);

        AssertBalanced(left, right, OuterGutterTolerance, "draft control");
        Assert.True(double.IsFinite(editor.ActualWidth) && editor.ActualWidth > 0d);
        Assert.True(frame.ActualWidth <= Math.Min(1180d, view.ActualWidth) + OuterGutterTolerance);
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  public void StandardDraft_BalancesEffectiveTextAndReservesOneScrollbarLane()
  {
    RunOnSta(() =>
    {
      ReaderWindow window = CreateReader(string.Empty, beginInEditor: true);
      try
      {
        window.Show();
        SettleLayout(window);
        ReaderDocumentView view = Find<ReaderDocumentView>(window, "DocumentView");
        TextBox editor = Find<TextBox>(view, "DraftTextBox");
        Border frame = Find<Border>(view, "ReaderPageFrame");
        Border focusBoundary = Find<Border>(view, "DraftFocusBoundary");
        ControlTemplate? readerOwnedTemplate = window.TryFindResource("ReaderDraftInputTemplate") as ControlTemplate;
        Assert.NotNull(readerOwnedTemplate);
        Assert.Same(readerOwnedTemplate, editor.Template);
        ScrollViewer contentHost = FindVisual<ScrollViewer>(editor, part =>
          string.Equals(part.Name, "PART_ContentHost", StringComparison.Ordinal));
        Assert.Same(contentHost, editor.Template.FindName("PART_ContentHost", editor));
        Assert.Same(contentHost, VisualTreeHelper.GetChild(editor, 0));
        Assert.False(contentHost.Focusable);
        Assert.Equal(new Thickness(0), editor.BorderThickness);

        editor.Text = "Short harmless fixture.";
        SettleLayout(window);
        DraftGeometry withoutScrollbar = CaptureDraftGeometry(frame, editor, contentHost);
        Assert.Equal(Visibility.Collapsed, contentHost.ComputedVerticalScrollBarVisibility);

        editor.Text = string.Join(Environment.NewLine, Enumerable.Repeat(
          "A wrapping line with enough harmless fixture text to require vertical scrolling.", 80));
        SettleLayout(window);
        DraftGeometry withScrollbar = CaptureDraftGeometry(frame, editor, contentHost);
        Assert.Equal(Visibility.Visible, contentHost.ComputedVerticalScrollBarVisibility);

        AssertBalanced(withoutScrollbar.OuterLeft, withoutScrollbar.OuterRight, OuterGutterTolerance, "draft control");
        AssertBalanced(withScrollbar.OuterLeft, withScrollbar.OuterRight, OuterGutterTolerance, "draft control");
        AssertBalanced(
          withoutScrollbar.EffectiveLeft,
          withoutScrollbar.EffectiveRight,
          EffectiveGutterTolerance,
          "draft text without scrollbar");
        AssertBalanced(
          withScrollbar.EffectiveLeft,
          withScrollbar.EffectiveRight,
          EffectiveGutterTolerance,
          "draft text with scrollbar");
        Assert.Equal(withoutScrollbar.ViewportWidth, withScrollbar.ViewportWidth, precision: 3);
        Assert.Equal(0d, withoutScrollbar.ScrollbarWidth);
        Assert.Equal(8d, withScrollbar.ScrollbarWidth, precision: 3);
        Assert.Equal(ReaderDocumentLayoutMetrics.StandardTextHorizontalInset, editor.Padding.Left);
        Assert.Equal(
          ReaderDocumentLayoutMetrics.StandardTextHorizontalInset - withScrollbar.ScrollbarWidth,
          editor.Padding.Right,
          precision: 3);

        const string reproduction =
          "Resize continuously; test minimum size, just above minimum, maximize/restore, and sidebar hidden/shown. &#x20;";
        string reproductionText = string.Concat(Enumerable.Repeat(reproduction + " ", 80));
        editor.Text = reproductionText;
        SettleLayout(window);
        Thickness stablePadding = editor.Padding;
        double stableViewportWidth = contentHost.ViewportWidth;
        editor.CaretIndex = 0;
        editor.SelectedText = "X";
        Assert.Equal("X" + reproductionText, editor.Text);
        Assert.Equal(stablePadding, editor.Padding);
        Assert.Equal(stableViewportWidth, contentHost.ViewportWidth, precision: 3);
        SettleLayout(window);
        Assert.Equal(stablePadding, editor.Padding);
        Assert.Equal(stableViewportWidth, contentHost.ViewportWidth, precision: 3);

        FieldInfo operationField = typeof(ReaderDocumentView).GetField(
          "insetUpdateOperation",
          BindingFlags.Instance | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException("Inset operation ownership field was not found.");
        Assert.Null(operationField.GetValue(view));
        contentHost.ScrollToVerticalOffset(contentHost.ScrollableHeight / 2d);
        window.UpdateLayout();
        Assert.Null(operationField.GetValue(view));

        double widthBeforeFocus = editor.ActualWidth;
        Size frameSizeBeforeFocus = frame.RenderSize;
        Size editorSizeBeforeFocus = editor.RenderSize;
        Thickness paddingBeforeFocus = editor.Padding;
        SolidColorBrush focusBrush = new(Colors.DodgerBlue);
        view.Resources["Brush.Control.Primary"] = focusBrush;
        Assert.Null(editor.FocusVisualStyle);
        Assert.False(focusBoundary.IsHitTestVisible);
        Assert.False(focusBoundary.Focusable);
        Assert.Equal(new Thickness(2), focusBoundary.BorderThickness);
        Assert.Same(Brushes.Transparent, focusBoundary.BorderBrush);
        Assert.Equal(frame.RenderSize, focusBoundary.RenderSize);
        window.Activate();
        Assert.Same(editor, Keyboard.Focus(editor));
        view.UpdateDraftFocusPresentation(Keyboard.PrimaryDevice);
        SettleLayout(window);
        Assert.True(view.IsDraftKeyboardFocusVisible);
        Assert.Same(focusBrush, focusBoundary.BorderBrush);
        Assert.Equal(widthBeforeFocus, editor.ActualWidth, precision: 3);
        Assert.Equal(frameSizeBeforeFocus, frame.RenderSize);
        Assert.Equal(editorSizeBeforeFocus, editor.RenderSize);
        Assert.Equal(paddingBeforeFocus, editor.Padding);

        view.UpdateDraftFocusPresentation(InputManager.Current.PrimaryMouseDevice);
        Assert.False(view.IsDraftKeyboardFocusVisible);
        Assert.Same(Brushes.Transparent, focusBoundary.BorderBrush);
        Assert.True(ReaderDocumentView.ShouldRevealDraftFocusForKey(Key.LeftAlt, Key.None, ModifierKeys.None));
        Assert.True(ReaderDocumentView.ShouldRevealDraftFocusForKey(Key.System, Key.Space, ModifierKeys.Alt));
        Assert.False(ReaderDocumentView.ShouldRevealDraftFocusForKey(Key.A, Key.None, ModifierKeys.None));

        view.RenderSurface(ReaderDocumentSurface.FullPage);
        Assert.False(view.IsDraftKeyboardFocusVisible);
        Assert.Same(Brushes.Transparent, focusBoundary.BorderBrush);
        view.RenderSurface(ReaderDocumentSurface.Draft);
        SettleLayout(window);
        Assert.Equal(frame.RenderSize, focusBoundary.RenderSize);

        editor.Text = string.Empty;
        SettleLayout(window);
        Assert.Equal(Visibility.Collapsed, contentHost.ComputedVerticalScrollBarVisibility);
        Assert.Equal(ReaderDocumentLayoutMetrics.StandardTextHorizontalInset, editor.Padding.Right);
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  public void StandardReadOnly_BalancesEffectiveTextWithHiddenAndVisibleScrollbar()
  {
    RunOnSta(() =>
    {
      Window window = CreateOffscreenWindow(1000d, 700d);
      using ReaderDocumentView view = new();
      window.Content = view;
      try
      {
        window.Show();
        RenderReading(view, "One short reading sentence.");
        SettleLayout(window);
        ReadOnlyGeometry withoutScrollbar = CaptureReadOnlyGeometry(view);
        Assert.Equal(Visibility.Collapsed, withoutScrollbar.ScrollbarVisibility);

        string longText = string.Join(" ", Enumerable.Repeat(
          "A harmless wrapping fixture sentence with several words.", 300));
        RenderReading(view, longText);
        SettleLayout(window);
        ReadOnlyGeometry withScrollbar = CaptureReadOnlyGeometry(view);
        Assert.Equal(Visibility.Visible, withScrollbar.ScrollbarVisibility);

        AssertBalanced(
          withoutScrollbar.EffectiveLeft,
          withoutScrollbar.EffectiveRight,
          EffectiveGutterTolerance,
          "read-only text without scrollbar");
        AssertBalanced(
          withScrollbar.EffectiveLeft,
          withScrollbar.EffectiveRight,
          EffectiveGutterTolerance,
          "read-only text with scrollbar");
        Assert.Equal(0d, withoutScrollbar.ScrollbarWidth);
        Assert.Equal(6d, withScrollbar.ScrollbarWidth, precision: 3);
        Assert.Equal(withoutScrollbar.TextWidth, withScrollbar.TextWidth, precision: 3);
        Assert.Equal(
          ReaderDocumentLayoutMetrics.StandardTextHorizontalInset - withScrollbar.ScrollbarWidth,
          withScrollbar.TextMargin.Right,
          precision: 3);
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  public void ReaderScrollbars_InHighContrastRemainDistinctFromTheSelectedReaderPage()
  {
    RunOnSta(() =>
    {
      SolidColorBrush originalThumb = Assert.IsType<SolidColorBrush>(Application.Current.Resources["Brush.Scrollbar.Thumb"]);
      SolidColorBrush originalHover = Assert.IsType<SolidColorBrush>(Application.Current.Resources["Brush.Scrollbar.ThumbHover"]);
      Color pageColor = (Color)ColorConverter.ConvertFromString("#F9F2E8");

      ReaderWindow window = CreateReader(string.Empty, beginInEditor: true);
      ReaderDocumentView view = Find<ReaderDocumentView>(window, "DocumentView");
      try
      {
        window.Show();
        window.Height = 700d;
        Application.Current.Resources["Brush.Scrollbar.Thumb"] = new SolidColorBrush(pageColor);
        Application.Current.Resources["Brush.Scrollbar.ThumbHover"] = new SolidColorBrush(pageColor);
        WindowThemeBehavior.SetIsHighContrastActive(window, true);
        RenderReading(view, string.Join(Environment.NewLine, Enumerable.Repeat("Visible fixture line.", 100)));
        SettleLayout(window);

        ScrollViewer reader = Find<ScrollViewer>(view, "ReaderScrollViewer");
        ScrollBar scrollbar = FindVisual<ScrollBar>(reader, candidate =>
          candidate.Orientation == Orientation.Vertical && candidate.Visibility == Visibility.Visible);
        Thumb thumb = FindVisual<Thumb>(scrollbar, _ => true);
        Border thumbChrome = FindVisual<Border>(thumb, candidate => candidate.Background is SolidColorBrush);
        Color thumbColor = Assert.IsType<SolidColorBrush>(thumbChrome.Background).Color;

        Assert.NotEqual(pageColor, thumbColor);

        view.RenderSurface(ReaderDocumentSurface.Draft);
        TextBox editor = Find<TextBox>(view, "DraftTextBox");
        editor.Text = string.Join(Environment.NewLine, Enumerable.Repeat("Draft fixture line.", 100));
        SettleLayout(window);
        ScrollViewer draftHost = FindVisual<ScrollViewer>(editor, candidate =>
          string.Equals(candidate.Name, "PART_ContentHost", StringComparison.Ordinal));
        ScrollBar draftScrollbar = FindVisual<ScrollBar>(draftHost, candidate =>
          candidate.Orientation == Orientation.Vertical && candidate.Visibility == Visibility.Visible);
        Thumb draftThumb = FindVisual<Thumb>(draftScrollbar, _ => true);
        Border draftChrome = FindVisual<Border>(draftThumb, candidate => candidate.Background is SolidColorBrush);
        double widthInHighContrast = editor.ActualWidth;
        Assert.NotEqual(pageColor, Assert.IsType<SolidColorBrush>(draftChrome.Background).Color);

        WindowThemeBehavior.SetIsHighContrastActive(window, false);
        SettleLayout(window);
        Color applicationThumb = Assert.IsType<SolidColorBrush>(Application.Current.Resources["Brush.Scrollbar.Thumb"]).Color;
        Assert.Equal(applicationThumb, Assert.IsType<SolidColorBrush>(draftChrome.Background).Color);
        Assert.False(view.Resources.Contains("Brush.Scrollbar.Thumb"));
        Assert.Equal(widthInHighContrast, editor.ActualWidth, precision: 3);
      }
      finally
      {
        window.Close();
        Application.Current.Resources["Brush.Scrollbar.Thumb"] = originalThumb;
        Application.Current.Resources["Brush.Scrollbar.ThumbHover"] = originalHover;
      }
    });
  }

  [Fact]
  public void ReaderScrollbarContrast_FollowsEveryReaderPageAndRestoresApplicationOwnership()
  {
    RunOnSta(() =>
    {
      Window window = CreateOffscreenWindow(1000d, 700d);
      using ReaderDocumentView view = new();
      window.Content = view;
      try
      {
        window.Show();
        foreach (ReaderThemeOption theme in ReaderThemeOption.Defaults)
        {
          WindowThemeBehavior.SetIsHighContrastActive(window, true);
          RenderReading(view, "Safe fixture", theme);
          SettleLayout(window);

          Color page = (Color)ColorConverter.ConvertFromString(theme.PageColor);
          Color ink = (Color)ColorConverter.ConvertFromString(theme.InkColor);
          SolidColorBrush thumb = Assert.IsType<SolidColorBrush>(view.Resources["Brush.Scrollbar.Thumb"]);
          SolidColorBrush hover = Assert.IsType<SolidColorBrush>(view.Resources["Brush.Scrollbar.ThumbHover"]);
          Assert.Equal(ink, thumb.Color);
          Assert.Equal(ink, hover.Color);
          Assert.True(ContrastRatio(page, thumb.Color) >= 3d,
            $"{theme.DisplayName} scrollbar contrast was {ContrastRatio(page, thumb.Color):F2}:1.");

          WindowThemeBehavior.SetIsHighContrastActive(window, false);
          Assert.False(view.Resources.Contains("Brush.Scrollbar.Thumb"));
          Assert.False(view.Resources.Contains("Brush.Scrollbar.ThumbHover"));
        }
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  public void LightDarkAndHighContrastPaletteChanges_DoNotMoveReaderDocumentGeometry()
  {
    RunOnSta(() =>
    {
      Application application = Application.Current
        ?? throw new InvalidOperationException("The WPF application was not initialized.");
      string[] paletteKeys = AppThemeManager.GetHighContrastPalette().Keys.ToArray();
      Dictionary<string, object?> originalResources = paletteKeys.ToDictionary(
        key => key,
        key => application.Resources.Contains(key) ? application.Resources[key] : null,
        StringComparer.Ordinal);
      Window window = CreateOffscreenWindow(1000d, 700d);
      using ReaderDocumentView view = new();
      window.Content = view;
      try
      {
        view.RenderSurface(ReaderDocumentSurface.Draft);
        window.Show();
        SettleLayout(window);

        AppThemeManager.ApplyPalette(application.Resources, useDarkTheme: false, isHighContrast: false);
        SettleLayout(window);
        LayoutSnapshot light = CaptureLayout(view);

        AppThemeManager.ApplyPalette(application.Resources, useDarkTheme: true, isHighContrast: false);
        SettleLayout(window);
        LayoutSnapshot dark = CaptureLayout(view);

        AppThemeManager.ApplyPalette(application.Resources, useDarkTheme: false, isHighContrast: true);
        SettleLayout(window);
        LayoutSnapshot highContrast = CaptureLayout(view);

        Assert.Equal(light, dark);
        Assert.Equal(light, highContrast);
      }
      finally
      {
        window.Close();
        foreach ((string key, object? value) in originalResources)
        {
          if (value is null)
          {
            application.Resources.Remove(key);
          }
          else
          {
            application.Resources[key] = value;
          }
        }
      }
    });
  }

  [Fact]
  public void DistractionFreeAndStandardModes_HaveExplicitSymmetricGeometry()
  {
    RunOnSta(() =>
    {
      using ReaderDocumentView view = new();

      view.RenderDistractionFree(enabled: true);
      Border frame = Find<Border>(view, "ReaderPageFrame");
      Border surface = Find<Border>(view, "ReaderPageSurface");
      TextBlock text = Find<TextBlock>(view, "ReaderTextBlock");
      Assert.Equal(ReaderDocumentLayoutMetrics.DistractionFreePagePadding, surface.Padding);
      Assert.Equal(ReaderDocumentLayoutMetrics.DistractionFreeTextInsets, text.Margin);
      Assert.Equal(0d, frame.MinWidth);
      Assert.True(double.IsPositiveInfinity(frame.MaxWidth));

      view.RenderDistractionFree(enabled: false);
      Assert.Equal(ReaderDocumentLayoutMetrics.StandardPagePadding, surface.Padding);
      Assert.Equal(ReaderDocumentLayoutMetrics.StandardTextInsets, text.Margin);
      Assert.Equal(560d, frame.MinWidth);
      Assert.Equal(1180d, frame.MaxWidth);
    });
  }

  [Fact]
  public void DraftContentVariants_KeepFiniteStableGeometry()
  {
    RunOnSta(() =>
    {
      Window window = CreateOffscreenWindow(1000d, 700d);
      using ReaderDocumentView view = new();
      window.Content = view;
      try
      {
        view.RenderSurface(ReaderDocumentSurface.Draft);
        window.Show();
        TextBox editor = Find<TextBox>(view, "DraftTextBox");
        string[] values =
        [
          string.Empty,
          "Short.",
          "Leading and trailing whitespace   ",
          "One\tindented\tline.",
          "hidden/shown hidden\\shown // \\\\ https://example.invalid/a/b C:\\safe\\fixture 1/2 &#x20;",
          "Resize continuously; test minimum size, just above minimum, maximize/restore, and sidebar hidden/shown. &#x20;",
          "Resize continuously; test minimum size, just above minimum, maximize\\restore, and sidebar hidden\\shown. &#x20;",
          "Unicode 😀 café नमस्ते.",
          "مرحبا بالعالم",
          "Notype 123 — مرحبا — test@example.invalid",
          new('x', 4096),
          string.Join(Environment.NewLine, Enumerable.Repeat("Multiline content.", 80)),
        ];

        foreach (string value in values)
        {
          editor.Text = value;
          editor.FlowDirection = value.Contains('م') ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
          editor.CaretIndex = editor.Text.Length;
          SettleLayout(window);

          Assert.True(double.IsFinite(editor.ActualWidth) && editor.ActualWidth > 0d);
          Assert.True(double.IsFinite(editor.Padding.Left) && editor.Padding.Left >= 0d);
          Assert.True(double.IsFinite(editor.Padding.Right) && editor.Padding.Right >= 0d);
          Assert.Equal(ReaderDocumentLayoutMetrics.StandardTextHorizontalInset, editor.Padding.Left);
        }
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Theory]
  [InlineData(double.NaN)]
  [InlineData(double.PositiveInfinity)]
  [InlineData(double.NegativeInfinity)]
  [InlineData(-1d)]
  public void StandardTextInsets_RejectInvalidScrollbarWidths(double width)
  {
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      ReaderDocumentLayoutMetrics.CreateStandardTextInsets(width));
  }

  [Fact]
  public void StandardTextInsets_ClampOversizedScrollbarWithoutNegativeGeometry()
  {
    Thickness insets = ReaderDocumentLayoutMetrics.CreateStandardTextInsets(1000d);

    Assert.Equal(ReaderDocumentLayoutMetrics.StandardTextHorizontalInset, insets.Left);
    Assert.Equal(0d, insets.Right);
    Assert.All(
      new[] { insets.Left, insets.Top, insets.Right, insets.Bottom },
      value => Assert.True(double.IsFinite(value) && value >= 0d));
  }

  [Fact]
  public void Disposal_AbortsPendingInsetWorkAndDetachesScrollbarCallbacks()
  {
    RunOnSta(() =>
    {
      Window window = CreateOffscreenWindow(1000d, 700d);
      ReaderDocumentView view = new();
      window.Content = view;
      try
      {
        view.RenderSurface(ReaderDocumentSurface.Draft);
        window.Show();
        TextBox editor = Find<TextBox>(view, "DraftTextBox");
        editor.Text = string.Join(Environment.NewLine, Enumerable.Repeat("Pending layout.", 80));

        view.Dispose();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

        FieldInfo operationField = typeof(ReaderDocumentView).GetField(
          "insetUpdateOperation",
          BindingFlags.Instance | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException("Inset operation ownership field was not found.");
        FieldInfo scrollViewerField = typeof(ReaderDocumentView).GetField(
          "draftScrollViewer",
          BindingFlags.Instance | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException("Draft ScrollViewer ownership field was not found.");
        Assert.Null(operationField.GetValue(view));
        Assert.Null(scrollViewerField.GetValue(view));
      }
      finally
      {
        window.Close();
        view.Dispose();
      }
    });
  }

  private static void RenderReading(ReaderDocumentView view, string text, ReaderThemeOption? theme = null)
  {
    using ReaderSidebarView sidebar = new();
    ReaderSidebarSelection selection = sidebar.Selection;
    ReadingDocument document = ReadingTextLayout.Create("Safe fixture", text, targetSectionCharacters: 50_000);
    view.RenderSurface(ReaderDocumentSurface.FullPage);
    view.RenderSection(
      document,
      currentSectionIndex: 0,
      activeWordIndex: -1,
      new ReaderDocumentAppearance(
        selection.Font,
        selection.FontSize,
        theme ?? selection.Theme,
        selection.HighlightMode,
        selection.HighlightStyle.Style,
        selection.HighlightColor,
        selection.Typography,
        selection.Language.Capability.Text.Direction));
  }

  private static DraftGeometry CaptureDraftGeometry(Border frame, TextBox editor, ScrollViewer contentHost)
  {
    (double outerLeft, double outerRight) = GetHorizontalGutters(frame, editor);
    double scrollbarWidth = FindVisualOptional<ScrollBar>(
      contentHost,
      part => part.Orientation == Orientation.Vertical && part.Visibility == Visibility.Visible)?.ActualWidth ?? 0d;
    return new DraftGeometry(
      outerLeft,
      outerRight,
      outerLeft + editor.Padding.Left,
      outerRight + editor.Padding.Right + scrollbarWidth,
      contentHost.ViewportWidth,
      scrollbarWidth);
  }

  private static ReadOnlyGeometry CaptureReadOnlyGeometry(ReaderDocumentView view)
  {
    Border frame = Find<Border>(view, "ReaderPageFrame");
    ScrollViewer reader = Find<ScrollViewer>(view, "ReaderScrollViewer");
    TextBlock text = Find<TextBlock>(view, "ReaderTextBlock");
    (double outerLeft, double outerRight) = GetHorizontalGutters(frame, reader);
    double scrollbarWidth = FindVisualOptional<ScrollBar>(
      reader,
      part => part.Orientation == Orientation.Vertical && part.Visibility == Visibility.Visible)?.ActualWidth ?? 0d;
    return new ReadOnlyGeometry(
      outerLeft + text.Margin.Left,
      outerRight + text.Margin.Right + scrollbarWidth,
      scrollbarWidth,
      reader.ComputedVerticalScrollBarVisibility,
      text.Margin,
      text.ActualWidth);
  }

  private static LayoutSnapshot CaptureLayout(ReaderDocumentView view)
  {
    Border frame = Find<Border>(view, "ReaderPageFrame");
    Border surface = Find<Border>(view, "ReaderPageSurface");
    TextBox editor = Find<TextBox>(view, "DraftTextBox");
    Point editorOrigin = editor.TransformToAncestor(frame).Transform(new Point());
    return new LayoutSnapshot(
      frame.ActualWidth,
      frame.ActualHeight,
      surface.Padding,
      editorOrigin.X,
      editorOrigin.Y,
      editor.ActualWidth,
      editor.ActualHeight,
      editor.Padding);
  }

  private static (double Left, double Right) GetHorizontalGutters(
    FrameworkElement ancestor,
    FrameworkElement descendant)
  {
    Point origin = descendant.TransformToAncestor(ancestor).Transform(new Point());
    return (origin.X, ancestor.ActualWidth - origin.X - descendant.ActualWidth);
  }

  private static void AssertBalanced(double left, double right, double tolerance, string name) =>
    Assert.True(
      Math.Abs(left - right) <= tolerance,
      $"{name} gutters must be balanced within {tolerance:F3} DIP; left={left:F3}, right={right:F3}.");

  private static double ContrastRatio(Color first, Color second)
  {
    static double Luminance(Color color)
    {
      static double Channel(byte value)
      {
        double normalized = value / 255d;
        return normalized <= 0.03928d
          ? normalized / 12.92d
          : Math.Pow((normalized + 0.055d) / 1.055d, 2.4d);
      }

      return (0.2126d * Channel(color.R)) + (0.7152d * Channel(color.G)) + (0.0722d * Channel(color.B));
    }

    double lighter = Math.Max(Luminance(first), Luminance(second));
    double darker = Math.Min(Luminance(first), Luminance(second));
    return (lighter + 0.05d) / (darker + 0.05d);
  }

  private static ReaderWindow CreateReader(string text, bool beginInEditor)
  {
    MethodInfo factory = typeof(ReaderWindowInitializationTests).GetMethod(
      "CreateReader",
      BindingFlags.NonPublic | BindingFlags.Static)
      ?? throw new InvalidOperationException("Reader test factory was not found.");
    ReaderWindow window = Assert.IsType<ReaderWindow>(factory.Invoke(null, [
      "Safe fixture",
      text,
      new UnusedSpeechService(),
      beginInEditor,
      null,
      null,
      null,
    ]));
    window.Left = -10_000;
    window.Top = -10_000;
    window.ShowActivated = true;
    window.ShowInTaskbar = false;
    return window;
  }

  private static Window CreateOffscreenWindow(double width, double height) => new()
  {
    Width = width,
    Height = height,
    Left = -10_000,
    Top = -10_000,
    ShowActivated = false,
    ShowInTaskbar = false,
    WindowStyle = WindowStyle.None,
  };

  private static void SettleLayout(Window window)
  {
    for (int pass = 0; pass < 3; pass++)
    {
      window.UpdateLayout();
      window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
  }

  private static T Find<T>(FrameworkElement root, string name) where T : class =>
    root.FindName(name) as T
      ?? throw new InvalidOperationException($"Reader element '{name}' was not found as {typeof(T).Name}.");

  private static T FindVisual<T>(DependencyObject root, Predicate<T> predicate) where T : DependencyObject =>
    FindVisualOptional(root, predicate)
      ?? throw new InvalidOperationException($"Visual descendant {typeof(T).Name} was not found.");

  private static T? FindVisualOptional<T>(DependencyObject root, Predicate<T> predicate) where T : DependencyObject
  {
    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
      DependencyObject child = VisualTreeHelper.GetChild(root, index);
      if (child is T typed && predicate(typed))
      {
        return typed;
      }
      T? descendant = FindVisualOptional(child, predicate);
      if (descendant is not null)
      {
        return descendant;
      }
    }
    return null;
  }

  private sealed class UnusedSpeechService : ITextToSpeechService
  {
    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromException<TextToSpeechResult>(new InvalidOperationException("Speech is not used by layout tests."));
  }

  private sealed record DraftGeometry(
    double OuterLeft,
    double OuterRight,
    double EffectiveLeft,
    double EffectiveRight,
    double ViewportWidth,
    double ScrollbarWidth);

  private sealed record ReadOnlyGeometry(
    double EffectiveLeft,
    double EffectiveRight,
    double ScrollbarWidth,
    Visibility ScrollbarVisibility,
    Thickness TextMargin,
    double TextWidth);

  private sealed record LayoutSnapshot(
    double FrameWidth,
    double FrameHeight,
    Thickness SurfacePadding,
    double EditorX,
    double EditorY,
    double EditorWidth,
    double EditorHeight,
    Thickness EditorPadding);

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The bounded STA helper transfers WPF failures to the asserting thread.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }
        action();
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA test thread timed out.");
    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
  }
}
