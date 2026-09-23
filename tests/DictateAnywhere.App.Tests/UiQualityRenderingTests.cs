using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using System.Windows.Markup;
using DictateAnywhere.Core.Contracts;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class UiQualityRenderingTests
{
  [Theory]
  [InlineData(false, 1d)]
  [InlineData(true, 1.25d)]
  [InlineData(true, 1.5d)]
  [InlineData(false, 2d)]
  public void BrandMark_RespondsToInheritedHighContrastWithoutLosingItsSilhouette(bool dark, double scale)
  {
    RunOnSta(() =>
    {
      Image mark = new() { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
      mark.SetResourceReference(FrameworkElement.StyleProperty, "AppBrandMarkStyle");
      Window host = Host(mark, dark);
      host.Width = 96;
      host.Height = 96;
      try
      {
        host.Show();
        Settle(host);
        DrawingImage colored = Assert.IsType<DrawingImage>(mark.Source);
        Capture(Render(host, scale), $"koncus-nai-color-{dark}-{scale}");
        AppThemeManager.ApplyPalette(host.Resources, dark, isHighContrast: true);
        WindowThemeBehavior.SetIsHighContrastActive(host, true);
        Settle(host);
        DrawingImage contrast = Assert.IsType<DrawingImage>(mark.Source);
        Assert.NotSame(colored, contrast);
        DrawingGroup drawing = Assert.IsType<DrawingGroup>(contrast.Drawing);
        SolidColorBrush expected = Assert.IsType<SolidColorBrush>(host.FindResource("Brush.Text.Primary"));
        foreach (GeometryDrawing face in drawing.Children)
        {
          Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(face.Brush).Color);
        }
        Capture(Render(host, scale), $"koncus-nai-contrast-{dark}-{scale}");
        WindowThemeBehavior.SetIsHighContrastActive(host, false);
        Settle(host);
        Assert.Same(colored, mark.Source);
      }
      finally { host.Close(); }
    });
  }

  [Theory]
  [InlineData("Workbench/TextboxWorkbenchWindow.xaml", true, 1d)]
  [InlineData("Workbench/TextboxWorkbenchWindow.xaml", false, 1.5d)]
  [InlineData("Workbench/TextboxWorkbenchWindow.xaml", true, 2d)]
  [InlineData("Workbench/Reading/ReaderWindow.xaml", true, 1d)]
  [InlineData("Workbench/Reading/ReaderWindow.xaml", true, 2d)]
  [InlineData("Workbench/AboutWindow.xaml", true, 1d)]
  [InlineData("Workbench/AboutWindow.xaml", false, 2d)]
  [InlineData("History/HistoryWindow.xaml", true, 1d)]
  [InlineData("Settings/SettingsPanel.xaml", true, 1d)]
  [InlineData("Settings/SettingsPanel.xaml", true, 2d)]
  [InlineData("Workbench/Reading/ReadingStudioHelpWindow.xaml", true, 1d)]
  public void PrimaryMarkup_RendersAtMinimumWidthWithoutWorkflowSideEffects(string relativePath, bool dark, double textScale)
  {
    RunOnSta(() =>
    {
      // Load the production visual tree without constructing workflow dependencies or firing commands.
      DirectoryInfo? root = new(AppContext.BaseDirectory);
      while (root is not null && !File.Exists(Path.Combine(root.FullName, "DictateAnywhere.sln"))) { root = root.Parent; }
      Assert.NotNull(root);
      XElement markup = XElement.Load(Path.Combine(root.FullName, "src/DictateAnywhere.App", relativePath));
      XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
      XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
      markup.Attribute(xaml + "Class")?.Remove();
      foreach (XAttribute attribute in markup.DescendantsAndSelf().Attributes().ToArray())
      {
        if ((attribute.Value.StartsWith("On", StringComparison.Ordinal) && !attribute.IsNamespaceDeclaration) || attribute.Name.LocalName.Contains("Behavior.", StringComparison.Ordinal)) { attribute.Remove(); }
        else if (attribute.IsNamespaceDeclaration && attribute.Value.StartsWith("clr-namespace:DictateAnywhere.App", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal))
        { attribute.Value += ";assembly=DictateAnywhere.App"; }
      }
      foreach (XElement element in markup.DescendantsAndSelf())
      {
        if (element.Name.NamespaceName.StartsWith("clr-namespace:DictateAnywhere.App", StringComparison.Ordinal))
        {
          element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=DictateAnywhere.App");
        }
      }
      XElement? resources = markup.Element(wpf + markup.Name.LocalName + ".Resources");
      Assert.NotNull(resources);
      XElement dictionary = new(wpf + "ResourceDictionary", resources.Elements().ToArray());
      resources.ReplaceNodes(dictionary);
      dictionary.AddFirst(new XElement(wpf + "ResourceDictionary.MergedDictionaries",
        new XElement(wpf + "ResourceDictionary", new XAttribute("Source", "/DictateAnywhere.App;component/Theming/DesignTokens.xaml")),
        new XElement(wpf + "ResourceDictionary", new XAttribute("Source", "/DictateAnywhere.App;component/Theming/ControlStyles.xaml"))));
      object visual = XamlReader.Parse(markup.ToString());
      Window window = visual as Window ?? Host(visual);
      if (visual is not Window) { window.MinWidth = 1040; }
      try
      {
        window.Width = window.MinWidth;
        window.Height = Math.Max(window.MinHeight, 780);
        window.Left = -10000;
        window.Top = -10000;
        window.ShowInTaskbar = false;
        AppThemeManager.ApplyPalette(window.Resources, dark);
        AppTextScaleManager.Apply(window.Resources, 12 * textScale);
        if (visual is FrameworkElement surface && visual is not Window)
        {
          AppThemeManager.ApplyPalette(surface.Resources, dark);
          AppTextScaleManager.Apply(surface.Resources, 12 * textScale);
        }
        if (window.FindName("DocumentView") is ReaderDocumentView document && window.FindName("SidebarView") is ReaderSidebarView readerSidebar)
        {
          readerSidebar.Render(new ReaderSidebarPresentation(true, false, false, false, true, false));
          ReaderSidebarSelection selection = readerSidebar.Selection;
          document.RenderSurface(ReaderDocumentSurface.Draft);
          document.RenderSection(new ReaderDocumentSession("Untitled", "", true).Document, 0, -1,
            new ReaderDocumentAppearance(selection.Font, selection.FontSize, selection.Theme, selection.HighlightMode,
              selection.HighlightStyle.Style, selection.HighlightColor, selection.Typography, selection.Language.Capability.Text.Direction));
          Assert.IsType<ReaderTransportView>(window.FindName("TransportView")).Visibility = Visibility.Collapsed;
        }
        if (window.FindName("SidebarView") is WorkbenchSidebarView sidebar)
        {
          sidebar.RenderHistory(new WorkbenchHistoryViewState("", [], [], "", "", true), null, null);
        }
        if (window.FindName("ComposerView") is WorkbenchComposerView emptyComposer)
        {
          emptyComposer.SetConversationActionState(true, false);
          emptyComposer.SetChatActionState(false, false, false, true, true, false, false, false, false);
        }
        window.Show();
        Settle(window);
        if (window.FindName("ComposerView") is WorkbenchComposerView composer)
        {
          Button record = Assert.IsType<Button>(composer.FindName("Record"));
          Button import = Assert.IsType<Button>(composer.FindName("AddFile"));
          Assert.True(record.IsVisible && import.IsVisible);
          Rect recordBounds = record.TransformToAncestor(window).TransformBounds(new Rect(record.RenderSize));
          Assert.True(recordBounds.Right <= window.ActualWidth && recordBounds.Bottom <= window.ActualHeight);
        }
        Capture(Render(window, 1d), $"{Path.GetFileNameWithoutExtension(relativePath)}-{dark}-{textScale}");
      }
      finally
      {
        (window.FindName("DocumentView") as ReaderDocumentView)?.Dispose();
        (window.FindName("SidebarView") as ReaderSidebarView)?.Dispose();
        (window.FindName("ComposerView") as WorkbenchComposerView)?.DisposePresentation();
        (window.FindName("QuickSettingsView") as WorkbenchQuickSettingsView)?.DisposePresentation();
        window.Close();
      }
    });
  }

  [Theory]
  [InlineData(true, false, 1d)]
  [InlineData(true, false, 1.5d)]
  [InlineData(true, false, 2d)]
  [InlineData(false, false, 1d)]
  [InlineData(false, true, 1d)]
  public void DisabledHistory_KeepsThemedPixelsSelectionAndScrolling(bool dark, bool highContrast, double scale)
  {
    RunOnSta(() =>
    {
      ListBox list = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Height = 180 };
      for (int index = 0; index < 60; index++)
      {
        list.Items.Add($"Saved entry {index + 1}");
      }
      list.SelectedIndex = 0;
      Window host = Host(list, dark, highContrast);
      try
      {
        host.Show();
        Settle(host);
        list.IsEnabled = false;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOTYPE_UI_GALLERY")))
        {
          Style themed = list.Style;
          list.Style = new Style(typeof(ListBox));
          Settle(host);
          Capture(Render(host, scale), $"list-platform-disabled-{dark}-{highContrast}-{scale}");
          list.Style = themed;
        }
        Settle(host);
        Assert.Equal(0, list.SelectedIndex);
        Assert.True(ScrollViewer.GetCanContentScroll(list));
        Assert.True(System.Windows.Controls.VirtualizingPanel.GetIsVirtualizing(list));
        Border chrome = Assert.IsType<Border>(list.Template.FindName("ListChrome", list));
        Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(chrome.Background).Color);
        RenderTargetBitmap bitmap = Render(host, scale);
        // Outside the first row, disabled list chrome must expose the canvas, never a platform fill.
        Color expected = Assert.IsType<SolidColorBrush>(host.Background).Color;
        Assert.Equal(expected, Pixel(bitmap, (int)(100 * scale), (int)(210 * scale)));
        Capture(bitmap, $"list-disabled-{dark}-{highContrast}-{scale}");
        list.IsEnabled = true;
        list.ScrollIntoView(list.Items[^1]);
        Settle(host);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(59));
        Assert.Equal(0, list.SelectedIndex);
      }
      finally { host.Close(); }
    });
  }

  [Fact]
  public void Search_FocusTextClearAndRtlKeepTheQueryEditableWithoutDecoration()
  {
    RunOnSta(() =>
    {
      TextBox search = new() { Width = 280 };
      search.SetResourceReference(FrameworkElement.StyleProperty, "AppSearchTextBoxStyle");
      Button other = new() { Content = "Another action" };
      StackPanel panel = new();
      panel.Children.Add(other);
      panel.Children.Add(search);
      Window host = Host(panel);
      try
      {
        host.Show();
        host.Activate();
        other.Focus();
        Settle(host);
        FrameworkElement decoration = Assert.IsAssignableFrom<FrameworkElement>(search.Template.FindName("SearchDecoration", search));
        Button clear = Assert.IsType<Button>(search.Template.FindName("ClearSearchButton", search));
        Assert.Equal(Visibility.Visible, decoration.Visibility);
        Assert.Equal(Visibility.Collapsed, clear.Visibility);
        double width = search.ActualWidth;
        Assert.True(search.Focus());
        Settle(host);
        Assert.Equal(Visibility.Collapsed, decoration.Visibility);
        foreach (string query in new[] { "a pasted query", "بحث", "日本語" })
        {
          search.FlowDirection = query == "بحث" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
          search.Text = query;
          Settle(host);
          Assert.Equal(query, search.Text);
          Assert.Equal(Visibility.Visible, clear.Visibility);
          Assert.Equal(Visibility.Collapsed, decoration.Visibility);
          Assert.Equal(width, search.ActualWidth);
          Rect caret = search.GetRectFromCharacterIndex(0);
          Assert.True(caret.X >= 0 && caret.X < search.ActualWidth);
          clear.Focus();
          SearchFieldBehavior.ClearCommand.Execute(null, search);
          Settle(host);
          Assert.Empty(search.Text);
          Assert.Same(search, Keyboard.FocusedElement);
          Assert.Equal(Visibility.Collapsed, decoration.Visibility);
        }
        Capture(Render(host, 1d), "search-focused-empty");
      }
      finally { host.Close(); }
    });
  }

  [Fact]
  public void Sidebar_EmptyListsCollapseAndSelectedHistorySurvivesModalIsolation()
  {
    RunOnSta(() =>
    {
      WorkbenchSidebarView view = new();
      Window host = Host(view);
      try
      {
        host.Show();
        view.RenderHistory(new WorkbenchHistoryViewState("", [], [], "", "", true), null, null);
        ListBox chats = Assert.IsType<ListBox>(view.FindName("ChatHistoryListBox"));
        ListBox dictations = Assert.IsType<ListBox>(view.FindName("HistoryListBox"));
        Assert.Equal(Visibility.Collapsed, chats.Visibility);
        Assert.Equal(Visibility.Collapsed, dictations.Visibility);
        HistoryItemViewModel item = HistoryItemViewModel.FromRecords([
          new DictationHistoryRecord(DateTimeOffset.UtcNow, "default", TranscriptionProviderIds.CohereLocal, "model", "Fixture", "Fixture", TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero).Normalize(),
        ]);
        view.RenderHistory(new WorkbenchHistoryViewState("", [item], [], "", "", true), null, null);
        dictations.SelectedItem = item;
        view.IsEnabled = false;
        Settle(host);
        Capture(Render(host, 1d), "sidebar-settings-open");
        Assert.False(dictations.IsEnabled);
        Assert.Equal(Visibility.Visible, dictations.Visibility);
        view.IsEnabled = true;
        Assert.Same(item, dictations.SelectedItem);
      }
      finally { host.Close(); }
    });
  }

  [Fact]
  public void ReadingInvitation_IsAnOverlayAndNeverChangesDraftContentOrUndo()
  {
    RunOnSta(() =>
    {
      using ReaderDocumentView view = new();
      Window host = Host(view);
      host.Width = 900;
      host.Height = 700;
      try
      {
        host.Show();
        view.RenderSurface(ReaderDocumentSurface.Draft);
        Settle(host);
        TextBlock hint = Assert.IsType<TextBlock>(view.FindName("DraftInvitation"));
        TextBox draft = Assert.IsType<TextBox>(view.FindName("DraftTextBox"));
        Assert.True(hint.IsVisible);
        Assert.False(hint.IsHitTestVisible);
        Assert.Empty(view.DraftText);
        draft.Text = "A real draft.";
        Settle(host);
        Assert.False(hint.IsVisible);
        draft.Clear();
        Settle(host);
        Assert.True(hint.IsVisible);
        draft.Undo();
        Settle(host);
        Assert.Equal("A real draft.", view.DraftText);
        Assert.False(hint.IsVisible);
        view.RenderSurface(ReaderDocumentSurface.FullPage);
        Settle(host);
        Assert.False(hint.IsVisible);
      }
      finally { host.Close(); }
    });
  }

  private static Window Host(object content, bool dark = true, bool highContrast = false)
  {
    Window host = new()
    {
      Content = content, Width = 360, Height = 520, Left = -10000, Top = -10000,
      ShowInTaskbar = false, WindowStyle = WindowStyle.None, FontFamily = new FontFamily("Segoe UI"),
    };
    host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DictateAnywhere.App;component/Theming/DesignTokens.xaml", UriKind.Relative) });
    host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DictateAnywhere.App;component/Theming/ControlStyles.xaml", UriKind.Relative) });
    AppThemeManager.ApplyPalette(host.Resources, dark, highContrast);
    WindowThemeBehavior.SetIsHighContrastActive(host, highContrast);
    host.SetResourceReference(Control.BackgroundProperty, "Brush.Surface.Canvas");
    host.SetResourceReference(Control.ForegroundProperty, "Brush.Text.Primary");
    return host;
  }

  private static void Settle(Window host)
  {
    host.UpdateLayout();
    host.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    host.UpdateLayout();
  }

  private static RenderTargetBitmap Render(FrameworkElement view, double scale)
  {
    RenderTargetBitmap image = new((int)Math.Ceiling(view.ActualWidth * scale), (int)Math.Ceiling(view.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
    image.Render(view);
    return image;
  }

  private static Color Pixel(BitmapSource image, int x, int y)
  {
    byte[] bytes = new byte[4];
    image.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
    return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
  }

  private static void Capture(BitmapSource image, string name)
  {
    string? directory = Environment.GetEnvironmentVariable("NOTYPE_UI_GALLERY");
    if (string.IsNullOrWhiteSpace(directory)) { return; }
    Directory.CreateDirectory(directory);
    PngBitmapEncoder encoder = new();
    encoder.Frames.Add(BitmapFrame.Create(image));
    using FileStream stream = File.Create(Path.Combine(directory, name + ".png"));
    encoder.Save(stream);
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Transfers STA assertions to the test runner.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try { action(); }
      catch (Exception exception) { failure = exception; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA render timed out.");
    if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
  }
}
