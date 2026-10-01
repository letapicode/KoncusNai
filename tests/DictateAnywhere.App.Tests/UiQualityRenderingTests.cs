using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using System.Windows.Markup;
using DictateAnywhere.Core.Contracts;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
  [Fact]
  public void ProductionWorkbench_AppearanceChangesPreserveDraftConversationAndSelection()
  {
    RunOnSta(() =>
    {
      if (Application.Current is null)
      {
        // Loading App itself queues production OnStartup on the test dispatcher.
        // Use its resources without starting tray/runtime services or a second logger.
        Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
          { Source = new Uri("/DictateAnywhere.App;component/Theming/DesignTokens.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
          { Source = new Uri("/DictateAnywhere.App;component/Theming/ControlStyles.xaml", UriKind.Relative) });
      }
      // Construct the production window and event handlers, but do not load settings,
      // register hotkeys, query history, send a request or write to user stores.
      string logRoot = Path.Combine(Path.GetTempPath(), "KoncusNai.Rendering.Diagnostics.Tests", Guid.NewGuid().ToString("N"));
      using DictateAnywhere.App.Diagnostics.LocalFileDiagnostics isolatedDiagnostics = new(
        DictateAnywhere.Diagnostics.StructuredDiagnosticsOptions.Default with { LogsDirectoryPath = logRoot },
        new DictateAnywhere.Diagnostics.DiagnosticsBundleExporter(), Path.Combine(logRoot, "unused-settings.json"));
      DictateAnywhere.App.Composition.ApplicationComposition composition = DictateAnywhere.App.Composition.ApplicationComposition.CreateProduction(isolatedDiagnostics);
      Assert.Same(isolatedDiagnostics, composition.Diagnostics);
      TextboxWorkbenchWindow window = composition.CreateWorkbenchWindow();
      try
      {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.ShowInTaskbar = false;
        WorkbenchChatController controller = Assert.IsType<WorkbenchChatController>(
          typeof(TextboxWorkbenchWindow).GetField("chatController", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window));
        controller.AddMessage(new ChatMessage(ChatMessageRoles.User, "Implement binary search in Java.", DateTimeOffset.UtcNow));
        controller.AddMessage(new ChatMessage(ChatMessageRoles.Assistant,
          "```java\npublic class BinarySearch {\n  public static int search(int[] nums, int target) {\n    int left = 0;\n    int right = nums.length - 1;\n    while (left <= right) {\n      int mid = left + (right - left) / 2;\n      if (nums[mid] == target) return mid;\n      if (nums[mid] < target) left = mid + 1;\n      else right = mid - 1;\n    }\n    return -1;\n  }\n}\n```", DateTimeOffset.UtcNow));
        ChatMessage[] original = controller.Messages.ToArray();
        WorkbenchComposerView composer = Assert.IsType<WorkbenchComposerView>(window.FindName("ComposerView"));
        WorkbenchQuickSettingsView quick = Assert.IsType<WorkbenchQuickSettingsView>(window.FindName("QuickSettingsView"));
        Button paper = Assert.IsType<Button>(quick.FindName("PaperViewButton"));
        WorkbenchChatTranscriptView transcript = Assert.IsType<WorkbenchChatTranscriptView>(window.FindName("ChatTranscriptView"));
        window.Show();
        Assert.IsType<TextBox>(composer.FindName("Prompt")).Text = "My unsent draft — नमस्ते";
        controller.AddPendingFile(ChatFileAttachment.Create("C:\\test\\notes.txt", "Pending test context"));
        foreach (bool dark in new[] { true, false })
        {
          AppThemeManager.ApplyPalette(window.Resources, dark);
          foreach (bool enabled in new[] { true, false })
          {
            paper.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.Equal(enabled ? Visibility.Visible : Visibility.Collapsed, Assert.IsType<Border>(window.FindName("PaperSurface")).Visibility);
            Border fullPaper = Assert.IsType<Border>(window.FindName("PaperSurface"));
            Assert.Equal(new Thickness(0), fullPaper.BorderThickness);
            if (enabled)
            {
              Assert.Equal(window.ActualWidth, fullPaper.ActualWidth, 1);
              Assert.Same(Brushes.Transparent, window.Resources["Brush.Surface.Canvas"]);
              Assert.Same(Brushes.Transparent, window.Resources["Brush.Surface.Sidebar"]);
              foreach (int zoom in new[] { 80, 150, 100 })
              {
                Grid rootGrid = Assert.IsType<Grid>(window.FindName("WorkbenchRoot"));
                rootGrid.LayoutTransform = new ScaleTransform(zoom / 100d, zoom / 100d);
                Settle(window);
                Assert.Equal("My unsent draft — नमस्ते", composer.PromptText);
                Capture(Render(window, 1d), $"production-paper-{dark}-zoom{zoom}");
              }
            }
            Assert.Equal(original, controller.Messages);
            Assert.Equal("My unsent draft — नमस्ते", composer.PromptText);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(composer.FindName("PromptPlaceholder")).Visibility);
            Assert.Single(controller.PendingFiles);
            transcript.TranscriptElement.SelectAll();
            string selected = transcript.TranscriptElement.Selection.Text;
            Assert.Contains("Implement binary search", selected);
            typeof(TextboxWorkbenchWindow).GetMethod("RenderChatTranscript", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.Equal(selected, transcript.TranscriptElement.Selection.Text);
            transcript.TranscriptElement.Selection.Select(transcript.TranscriptElement.Document.ContentStart, transcript.TranscriptElement.Document.ContentStart);
            transcript.TranscriptElement.ScrollToHome();
            Settle(window);
            Capture(Render(window, 1d), $"production-workbench-{(dark ? "dark" : "light")}-{(enabled ? "paper" : "standard")}");
            Button mic = Assert.IsType<Button>(composer.FindName("Record"));
            composer.SetDictationState(WorkbenchViewModelFactory.Create(WorkbenchSessionState.Recording, "", ""), true);
            mic.Focus();
            Settle(window);
            Capture(Render(window, 1.5d), $"recording-focus-{dark}-{enabled}");
            Assert.Null(mic.FocusVisualStyle);
            composer.SetDictationState(WorkbenchViewModelFactory.Create(WorkbenchSessionState.Idle, "", ""), false);
          }
        }
        quick.SetTextSize(15);
        foreach (int size in new[] { 12, 30, 15 })
        {
          Assert.IsType<Slider>(quick.FindName("TextSizeSlider")).Value = size;
          Settle(window);
          Assert.Equal(size, transcript.TranscriptElement.FontSize);
          Assert.Equal("My unsent draft — नमस्ते", composer.PromptText);
          Assert.Single(controller.PendingFiles);
          Capture(Render(window, 1d), $"production-workbench-text-{size}");
        }
        controller.SetReadiness(false, false, false);
        controller.SetModelStatus("Ollama is not running. Start Ollama to check the installed model.");
        typeof(TextboxWorkbenchWindow).GetMethod("SetChatStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
          .Invoke(window, ["Copied reply.", false]);
        Assert.StartsWith("Ollama is not running", controller.ModelStatus, StringComparison.Ordinal);
        Assert.IsType<WorkbenchOperationalStatusView>(window.FindName("OperationalStatusView")).ExpireTransientOutcome();
        Assert.IsType<Button>(composer.FindName("NewChatButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.StartsWith("Ollama is not running", controller.ModelStatus, StringComparison.Ordinal);
        Grid noticeGrid = Assert.IsType<Grid>(Assert.IsType<WorkbenchModelNoticeView>(window.FindName("ModelNotice")).Content);
        Assert.StartsWith("Ollama is not running", Assert.IsType<TextBlock>(noticeGrid.Children.OfType<ScrollViewer>().Single().Content).Text, StringComparison.Ordinal);
        WorkbenchModelNoticeView notice = Assert.IsType<WorkbenchModelNoticeView>(window.FindName("ModelNotice"));
        Border compact = Assert.IsType<Border>(composer.FindName("CompactComposer"));
        WorkbenchSidebarView sidebar = Assert.IsType<WorkbenchSidebarView>(window.FindName("SidebarView"));
        Grid root = Assert.IsType<Grid>(window.FindName("WorkbenchRoot"));
        WorkbenchRecoveryStore recoveries = Assert.IsType<WorkbenchRecoveryStore>(typeof(TextboxWorkbenchWindow)
          .GetField("recoveries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window));
        WorkbenchRecovery recovery = recoveries.Add("Recover this exact transcript", "Dictation wasn’t inserted. History wasn’t saved.", false);
        typeof(TextboxWorkbenchWindow).GetMethod("SelectRecovery", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
          .Invoke(window, [recovery.Id]);
        Button dismiss = noticeGrid.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Single(button => Equals(button.Content, "Dismiss"));
        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(recoveries.Current);
        Assert.Equal(recovery, Assert.Single(recoveries.Pending));
        Assert.StartsWith("Ollama is not running", Assert.IsType<TextBlock>(noticeGrid.Children.OfType<ScrollViewer>().Single().Content).Text, StringComparison.Ordinal);
        Button recover = noticeGrid.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Single(button => Equals(button.Content, "Recover dictation"));
        Assert.True(recover.IsVisible);
        MenuItem retained = Assert.IsType<MenuItem>(recover.ContextMenu.Items[0]);
        retained.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(recovery.Id, notice.RecoveryIdentity);
        foreach (double width in new[] { 1000d, 2400d })
          foreach (bool showSidebar in new[] { true, false })
            foreach (double zoom in new[] { .8d, 1d, 1.5d })
            {
              window.Width = width;
              sidebar.SetSidebarVisible(showSidebar);
              root.LayoutTransform = new ScaleTransform(zoom, zoom);
              Settle(window);
              Rect editorBounds = compact.TransformToAncestor(window).TransformBounds(new Rect(compact.RenderSize));
              Rect noticeBounds = notice.TransformToAncestor(window).TransformBounds(new Rect(notice.RenderSize));
              Assert.Equal(editorBounds.Left, noticeBounds.Left, 1);
              Assert.Equal(editorBounds.Right, noticeBounds.Right, 1);
              Capture(Render(window, 1.25d), $"notice-aligned-{width}-{showSidebar}-{zoom}");
            }
        root.LayoutTransform = Transform.Identity;
        composer.PromptText = "Preserve this unsent expanded draft";
        WorkbenchExpandedPromptView expanded = Assert.IsType<WorkbenchExpandedPromptView>(window.FindName("ExpandedPromptView"));
        typeof(TextboxWorkbenchWindow).GetMethod("OnExpandPromptClicked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
          .Invoke(window, [composer, new RoutedEventArgs()]);
        expanded.PromptElement.Select(2, 4);
        string draft = expanded.Text;
        typeof(TextboxWorkbenchWindow).GetMethod("OnExpandedSendChatClicked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
          .Invoke(window, [expanded, new RoutedEventArgs()]);
        Assert.True(expanded.IsOpen);
        Assert.Equal(draft, expanded.Text);
        Assert.Equal(2, expanded.PromptElement.SelectionStart);
        Assert.Equal(4, expanded.PromptElement.SelectionLength);
        KeyEventArgs blockedEnter = new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter)
          { RoutedEvent = Keyboard.KeyDownEvent };
        expanded.PromptElement.RaiseEvent(blockedEnter);
        Assert.True(blockedEnter.Handled);
        Assert.True(expanded.IsOpen);
        Assert.Equal(2, expanded.PromptElement.SelectionStart);
        Assert.Equal(4, expanded.PromptElement.SelectionLength);
        Settle(window);
        Assert.Equal(expanded.PromptElement.ActualWidth, expanded.Notice.ActualWidth, 1);
      }
      finally
      {
        Task disposal = window.DisposeAsync().AsTask();
        while (!disposal.IsCompleted) window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        disposal.GetAwaiter().GetResult();
        window.Close();
        composition.Diagnostics.Dispose();
        Assert.NotEmpty(Directory.GetFiles(logRoot, "*.log"));
        string ownedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KoncusNai.Rendering.Diagnostics.Tests")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(ownedParent, Path.GetFullPath(logRoot), StringComparison.OrdinalIgnoreCase);
        Directory.Delete(logRoot, recursive: true);
      }
    });
  }

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
  [InlineData("Workbench/TextboxWorkbenchWindow.xaml", true, 1.25d)]
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
      if (relativePath == "Workbench/AboutWindow.xaml")
      {
        string prose = string.Join(" ", markup.Descendants().Attributes("Text").Select(attribute => attribute.Value));
        Assert.Contains("Copy and Dismiss", prose);
        Assert.Contains("Model notices above Just Ask", prose);
        Assert.DoesNotContain("bottom-center", prose);
        Assert.DoesNotContain("always saved", prose);
      }
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
        if (window.FindName("ChatTranscriptView") is WorkbenchChatTranscriptView transcript)
        {
          bool paper = textScale == 1.25d || !dark;
          transcript.ApplyTypography(15, new FontFamily("Segoe UI"));
          transcript.SetPaperView(paper);
          transcript.Render(
            [
              new ChatMessage(ChatMessageRoles.User, "Give me the code for binary search in Java.", DateTimeOffset.UtcNow),
              new ChatMessage(ChatMessageRoles.Assistant,
                "Here is a binary search method:\n\n```java\npublic class BinarySearch {\n  public static int search(int[] nums, int target) {\n    int left = 0;\n    int right = nums.length - 1;\n    while (left <= right) {\n      int mid = left + (right - left) / 2;\n      if (nums[mid] == target) return mid;\n      if (nums[mid] < target) left = mid + 1;\n      else right = mid - 1;\n    }\n    return -1;\n  }\n}\n```",
                DateTimeOffset.UtcNow),
            ],
            _ => paper ? Brushes.Black : Brushes.White,
            _ => { }, _ => { }, _ => { }, paper);
          if (window.FindName("PaperSurface") is Border paperSurface)
            paperSurface.Visibility = paper ? Visibility.Visible : Visibility.Collapsed;
          if (window.FindName("ComposerView") is WorkbenchComposerView chatComposer)
          {
            chatComposer.SetPaperView(paper);
            chatComposer.Margin = new Thickness(24, 0, 24, 18);
          }
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
        if (relativePath == "Workbench/TextboxWorkbenchWindow.xaml" && dark && textScale == 1.25d
            && window.FindName("WorkbenchRoot") is Grid paperRoot
            && window.FindName("PaperSurface") is Border texture
            && window.FindName("ChatTranscriptView") is WorkbenchChatTranscriptView paperTranscript)
        {
          paperRoot.LayoutTransform = new ScaleTransform(0.8, 0.8);
          Settle(window);
          Capture(Render(window, 1d), "TextboxWorkbenchWindow-paper-zoom80");
          paperRoot.LayoutTransform = new ScaleTransform(1.5, 1.5);
          Settle(window);
          Capture(Render(window, 1d), "TextboxWorkbenchWindow-paper-zoom150");
          paperRoot.LayoutTransform = Transform.Identity;
          window.Width = 1600;
          window.Height = 900;
          Settle(window);
          Capture(Render(window, 1d), "TextboxWorkbenchWindow-paper-wide");
          AppThemeManager.ApplyPalette(window.Resources, useDarkTheme: true, isHighContrast: true);
          Assert.IsType<WorkbenchComposerView>(window.FindName("ComposerView")).SetPaperView(true);
          texture.Visibility = Visibility.Collapsed;
          paperTranscript.Render(
            [new ChatMessage(ChatMessageRoles.Assistant, "```java\npublic class Contrast { int value = 15; }\n```", DateTimeOffset.UtcNow)],
            _ => SystemColors.WindowTextBrush, _ => { }, _ => { }, _ => { }, paperView: true);
          Settle(window);
          Assert.Equal(Visibility.Collapsed, texture.Visibility);
          Capture(Render(window, 1d), "TextboxWorkbenchWindow-paper-high-contrast");
        }
        if (relativePath == "Workbench/TextboxWorkbenchWindow.xaml" && dark && textScale == 1d
            && window.FindName("WorkbenchRoot") is Grid workbenchRoot
            && window.FindName("QuickSettingsView") is WorkbenchQuickSettingsView quickSettings
            && window.FindName("SidebarView") is WorkbenchSidebarView workbenchSidebar)
        {
          workbenchRoot.LayoutTransform = new ScaleTransform(1.5, 1.5);
          quickSettings.Show();
          quickSettings.SetZoom(150);
          Settle(window);
          quickSettings.UpdatePlacement(workbenchRoot, workbenchSidebar.Surface,
            workbenchSidebar.SettingsAction, workbenchSidebar.ActualWidth);
          Settle(window);
          Capture(Render(window, 1d), "TextboxWorkbenchWindow-quick-settings-zoom150");
          quickSettings.Hide();
          workbenchRoot.LayoutTransform = Transform.Identity;
          WorkbenchChatTranscriptView longTranscript = Assert.IsType<WorkbenchChatTranscriptView>(window.FindName("ChatTranscriptView"));
          longTranscript.Render(
            [new ChatMessage(ChatMessageRoles.Assistant,
              "```java\npublic class Demo { // " + new string('x', 220) + "\n}\n```",
              DateTimeOffset.UtcNow)],
            _ => Brushes.White, _ => { }, _ => { }, _ => { });
          Settle(window);
          Section section = Assert.IsType<Section>(longTranscript.TranscriptElement.Document.Blocks.FirstBlock);
          Border codeBorder = Assert.IsType<Border>(Assert.IsType<BlockUIContainer>(section.Blocks.FirstBlock).Child);
          RichTextBox longCode = Assert.Single(Assert.IsType<Grid>(codeBorder.Child).Children.OfType<RichTextBox>());
          Assert.True(longCode.Document.PageWidth > longCode.ActualWidth);
          Assert.True(longCode.ActualWidth <= longTranscript.ActualWidth);
          Capture(Render(window, 1d), "TextboxWorkbenchWindow-long-code-scroll");
        }
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

  [Theory]
  [InlineData(false, false, false, 900d, .8d)]
  [InlineData(false, false, false, 460d, 1.5d)]
  [InlineData(true, false, false, 900d, 1d)]
  [InlineData(true, false, false, 460d, 1.5d)]
  [InlineData(false, true, false, 900d, 1d)]
  [InlineData(true, true, false, 460d, 1.5d)]
  [InlineData(false, true, true, 460d, 1.5d)]
  [InlineData(true, false, true, 900d, 1d)]
  public void ComposerExpansionLayout_WrapsWithoutCollisions(bool dark, bool paper, bool contrast, double width, double zoom)
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView view = new();
      Window host = Host(view, dark, contrast);
      LoadProductionComposerStyles(host);
      host.Width = width;
      host.Height = 740;
      view.LayoutTransform = new ScaleTransform(zoom, zoom);
      try
      {
        host.Show();
        view.SetPaperView(paper);
        TextBox editor = view.PromptElement;
        Button expand = view.ExpandPromptElement;
        Button reading = Assert.IsType<Button>(view.FindName("ReadDocument"));
        WrapPanel toolbar = Assert.IsType<WrapPanel>(expand.Parent);
        Assert.Same(toolbar, reading.Parent);
        Assert.Equal(toolbar.Children.IndexOf(reading) + 1, toolbar.Children.IndexOf(expand));
        Assert.Equal(toolbar.Children.IndexOf(expand) + 1,
          toolbar.Children.IndexOf(Assert.IsType<Button>(view.FindName("ExportChatButton"))));
        foreach (string draft in new[] { "", "Short", new string('x', 121), "a\nb\nc", "Short", "" })
        {
          view.PromptText = draft;
          Settle(host);
          view.RefreshExpansionButton(false);
          Assert.Equal(PromptExpansionPolicy.ShouldOfferExpansion(draft, editor.LineCount)
            ? Visibility.Visible : Visibility.Collapsed, expand.Visibility);
        }
        view.PromptText = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"Line {i}: a long editable draft नमस्ते"));
        view.SetPendingFiles([ChatFileAttachment.Create("C:\\test\\notes.txt", "Pending context")]);
        Settle(host);
        editor.Select(4, 3);
        editor.ScrollToVerticalOffset(20);
        string text = editor.Text;
        long revision = view.PromptRevision;
        for (int phase = 0; phase < 3; phase++)
        {
          host.Width = phase == 1 ? width + 120 : width;
          view.SetConversationActionState(true, phase != 0);
          view.SetChatActionState(phase == 2, phase == 2, false, true, phase != 2, phase != 2,
            false, false, phase == 1);
          view.SetDictationState(WorkbenchViewModelFactory.Create(phase == 0 ? WorkbenchSessionState.Recording
            : phase == 1 ? WorkbenchSessionState.Transcribing : WorkbenchSessionState.Idle, "", ""), phase == 0);
          view.ApplyTypography(phase == 1 ? 30 : 15, new FontFamily(phase == 1 ? "Georgia" : "Segoe UI"));
          view.RefreshExpansionButton(false);
          Settle(host);
          Assert.Equal(Visibility.Visible, expand.Visibility);
          if (phase == 0 && width == 900 && !dark && !paper && !contrast)
          {
            // Recreate the original competing placement using the production controls.
            Grid originalGrid = Assert.IsType<Grid>(toolbar.Parent);
            toolbar.Children.Remove(expand);
            originalGrid.Children.Add(expand);
            Grid.SetRow(expand, 1); Grid.SetColumn(expand, 5);
            Settle(host);
            Button newChat = Assert.IsType<Button>(view.FindName("NewChatButton"));
            Rect oldExpand = expand.TransformToAncestor(host).TransformBounds(new Rect(expand.RenderSize));
            Rect oldNewChat = newChat.TransformToAncestor(host).TransformBounds(new Rect(newChat.RenderSize));
            Assert.True(oldExpand.IntersectsWith(oldNewChat), "The original overlap was not reproduced.");
            originalGrid.Children.Remove(expand);
            expand.ClearValue(Grid.RowProperty); expand.ClearValue(Grid.ColumnProperty);
            toolbar.Children.Insert(toolbar.Children.IndexOf(reading) + 1, expand);
            Settle(host);
          }
          Button[] buttons = toolbar.Children.OfType<Button>().Concat(new[] { "Record", "Send", "StopChat" }
            .Select(name => Assert.IsType<Button>(view.FindName(name)))).Where(button => button.IsVisible).ToArray();
          Rect composer = Assert.IsType<Border>(view.FindName("CompactComposer"))
            .TransformToAncestor(host).TransformBounds(new Rect(Assert.IsType<Border>(view.FindName("CompactComposer")).RenderSize));
          Rect[] bounds = buttons.Select(button => button.TransformToAncestor(host).TransformBounds(new Rect(button.RenderSize))).ToArray();
          for (int i = 0; i < bounds.Length; i++)
          {
            Assert.True(bounds[i].Left >= composer.Left - .5 && bounds[i].Right <= composer.Right + .5
              && bounds[i].Top >= composer.Top - .5 && bounds[i].Bottom <= composer.Bottom + .5
              && bounds[i].Right <= host.ActualWidth + .5 && bounds[i].Bottom <= host.ActualHeight + .5,
              $"{buttons[i].Name} exceeds composer bounds at {width}/{zoom}.");
            for (int j = i + 1; j < bounds.Length; j++)
            {
              Rect intersection = Rect.Intersect(bounds[i], bounds[j]);
              Assert.True(intersection.IsEmpty || intersection.Width < .5 || intersection.Height < .5,
                $"{buttons[i].Name} overlaps {buttons[j].Name} at {width}/{zoom}.");
            }
          }
          Button[] flowing = toolbar.Children.OfType<Button>().Where(button => button.IsVisible).ToArray();
          for (int i = 1; i < flowing.Length; i++)
          {
            Point previous = flowing[i - 1].TranslatePoint(new Point(), host);
            Point current = flowing[i].TranslatePoint(new Point(), host);
            Assert.True(current.Y > previous.Y + .5 || current.X > previous.X, "Toolbar flow order changed.");
          }
          Assert.Equal(text, editor.Text);
          Assert.Equal(revision, view.PromptRevision);
          Assert.Equal(4, editor.SelectionStart);
          Assert.Equal(3, editor.SelectionLength);
          double scroll = editor.VerticalOffset;
          bool undo = editor.CanUndo;
          view.RefreshExpansionButton(true);
          Assert.Equal(Visibility.Collapsed, expand.Visibility);
          view.RefreshExpansionButton(false);
          Assert.Equal(scroll, editor.VerticalOffset);
          Assert.Equal(undo, editor.CanUndo);
        }
        foreach (double dpiScale in new[] { 1d, 1.25d, 1.5d, 2d })
        {
          RenderTargetBitmap rendered = Render(host, dpiScale);
          Assert.Equal((int)Math.Ceiling(host.ActualWidth * dpiScale), rendered.PixelWidth);
          Assert.Equal((int)Math.Ceiling(host.ActualHeight * dpiScale), rendered.PixelHeight);
          Capture(rendered, $"composer-expand-{dark}-{paper}-{contrast}-{width}-{zoom}-dpi{dpiScale}");
        }
      }
      finally { view.DisposePresentation(); host.Close(); }
    });
  }

  [Fact]
  public void ComposerExpansion_HidingFocusedButtonPreservesDraftAndReturnsFocusWithoutStealingFromExpandedEditor()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView view = new();
      WorkbenchExpandedPromptView expanded = new();
      Grid panel = new(); panel.Children.Add(view); panel.Children.Add(expanded);
      expanded.SetOpen(false);
      Window host = Host(panel);
      LoadProductionComposerStyles(host);
      host.Width = 700;
      try
      {
        host.Show();
        TextBox editor = view.PromptElement;
        view.PromptText = "First line\nSecond line";
        Settle(host);
        view.RefreshExpansionButton(false);
        Button reading = Assert.IsType<Button>(view.FindName("ReadDocument"));
        reading.Focus();
        Assert.True(reading.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
        Assert.True(view.ExpandPromptElement.IsKeyboardFocused);
        Assert.True(view.ExpandPromptElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)));
        Assert.True(reading.IsKeyboardFocused);
        view.ExpandPromptElement.Focus();
        view.PromptText = "Short";
        editor.Select(1, 2);
        long revision = view.PromptRevision;
        view.RefreshExpansionButton(false);
        Assert.True(editor.IsKeyboardFocused);
        Assert.Equal(1, editor.SelectionStart);
        Assert.Equal(2, editor.SelectionLength);
        Assert.Equal(revision, view.PromptRevision);
        view.PromptText = "First line\nSecond line";
        Settle(host);
        view.RefreshExpansionButton(false);
        int clicks = 0;
        view.ExpandPromptClicked += (_, _) => clicks++;
        view.ExpandPromptElement.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, clicks);
        view.ExpandPromptElement.Focus();
        foreach (Key key in new[] { Key.Space, Key.Enter })
        {
          PresentationSource source = PresentationSource.FromVisual(host)!;
          view.ExpandPromptElement.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            { RoutedEvent = Keyboard.KeyDownEvent });
          view.ExpandPromptElement.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            { RoutedEvent = Keyboard.KeyUpEvent });
        }
        Assert.Equal(3, clicks);
        expanded.SetText(view.PromptText);
        expanded.SetOpen(true);
        Settle(host);
        expanded.ShowAndFocus();
        view.SetCompactComposerVisible(false);
        view.RefreshExpansionButton(true);
        Assert.True(expanded.PromptElement.IsKeyboardFocused);
        Assert.False(editor.IsKeyboardFocused);
        Assert.Equal("First line\nSecond line", expanded.Text);
      }
      finally { view.DisposePresentation(); host.Close(); }
    });
  }

  private static void LoadProductionComposerStyles(Window host)
  {
    DirectoryInfo? root = new(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "DictateAnywhere.sln"))) root = root.Parent;
    Assert.NotNull(root);
    XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    XElement markup = XElement.Load(Path.Combine(root.FullName, "src/DictateAnywhere.App/Workbench/TextboxWorkbenchWindow.xaml"));
    XElement[] styles = markup.Descendants(wpf + "Style").Where(element => ((string?)element.Attribute(xaml + "Key")) is "ComposerIconButtonStyle" or "ComposerSubmitButtonStyle" or "ComposerStopButtonStyle" or "ComposerTextBoxStyle").ToArray();
    Assert.Equal(4, styles.Length);
    XElement dictionary = new(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", xaml),
      new XElement(wpf + "ResourceDictionary.MergedDictionaries",
        new XElement(wpf + "ResourceDictionary", new XAttribute("Source", "/DictateAnywhere.App;component/Theming/ControlStyles.xaml"))), styles);
    host.Resources.MergedDictionaries.Add(Assert.IsType<ResourceDictionary>(XamlReader.Parse(dictionary.ToString())));
  }

  private static Window Host(object content, bool dark = true, bool highContrast = false)
  {
    Window host = new()
    {
      Content = content,
      Width = 360,
      Height = 520,
      Left = -10000,
      Top = -10000,
      ShowInTaskbar = false,
      WindowStyle = WindowStyle.None,
      FontFamily = new FontFamily("Segoe UI"),
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
