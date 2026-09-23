using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Automation;
using System.Xml.Linq;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench.Reading;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class ReaderAccessibilityRegressionTests
{
  private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

  [Fact]
  public void ReaderStartupBounds_AreClampedToTheMonitorWorkAreaWithoutDroppingBelowMinimums()
  {
    MethodInfo? method = typeof(WindowWorkAreaConstraintBehavior).GetMethod(
      "ConstrainBoundsToWorkArea",
      BindingFlags.NonPublic | BindingFlags.Static);

    Assert.NotNull(method);
    Rect requested = new(78, -22, 1380, 860);
    Rect workArea = new(0, 0, 1536, 816);
    Size minimum = new(1040, 700);
    Rect constrained = Assert.IsType<Rect>(method.Invoke(null, [requested, workArea, minimum]));

    Assert.Equal(new Rect(78, 0, 1380, 816), constrained);
    Assert.True(constrained.Width >= minimum.Width);
    Assert.True(constrained.Height >= minimum.Height);
    Assert.True(workArea.Contains(constrained));

    Rect oversized = Assert.IsType<Rect>(method.Invoke(null, [
      new Rect(-500, -300, 4000, 3000),
      new Rect(100, 50, 1200, 760),
      minimum,
    ]));
    Assert.Equal(new Rect(100, 50, 1200, 760), oversized);
  }

  [Fact]
  public void ReaderStartupBounds_HandleOversizedMinimumsInvalidRequestedPositionsAndRejectInvalidWorkAreas()
  {
    MethodInfo method = typeof(WindowWorkAreaConstraintBehavior).GetMethod(
      "ConstrainBoundsToWorkArea",
      BindingFlags.NonPublic | BindingFlags.Static)
      ?? throw new InvalidOperationException("The work-area constraint calculation was not found.");

    Rect constrained = Assert.IsType<Rect>(method.Invoke(null, [
      new Rect(double.NaN, double.NaN, 900, 600),
      new Rect(-1200, 40, 800, 500),
      new Size(1040, 700),
    ]));
    Assert.Equal(new Rect(-1200, 40, 800, 500), constrained);

    TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [
      new Rect(0, 0, 900, 600),
      new Rect(0, 0, 0, 500),
      new Size(700, 500),
    ]));
    Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
  }

  [Fact]
  public void ReaderSidebar_HasAFixedHeaderRowAndOwnsAClippedFocusAdornerViewport()
  {
    XDocument document = XDocument.Load(Path.Combine(
      FindRepoRoot(),
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading",
      "ReaderSidebarView.xaml"));

    XElement rootGrid = document.Descendants().First(element =>
      element.Name.LocalName == "Grid"
      && element.Attribute(XamlNamespace + "Name")?.Value == "SidebarLayoutRoot");
    XElement[] rows = rootGrid.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions")
      .Elements().ToArray();
    Assert.Equal(2, rows.Length);
    Assert.Equal("46", rows[0].Attribute("Height")?.Value);
    Assert.Equal("*", rows[1].Attribute("Height")?.Value);

    XElement viewport = document.Descendants().Single(element =>
      element.Name.LocalName == "ScrollViewer"
      && element.Attribute(XamlNamespace + "Name")?.Value == "ReaderSidebarScrollViewer");
    Assert.Equal("1", viewport.Attribute("Grid.Row")?.Value);
    Assert.Equal("True", viewport.Attribute("ClipToBounds")?.Value);
    XElement adorner = Assert.Single(viewport.Elements().Where(element => element.Name.LocalName == "AdornerDecorator"));
    Assert.Equal("True", adorner.Attribute("ClipToBounds")?.Value);

    XElement content = adorner.Descendants().Single(element =>
      element.Name.LocalName == "StackPanel"
      && element.Attribute(XamlNamespace + "Name")?.Value == "ReaderSidebarContent");
    Thickness margin = (Thickness)new ThicknessConverter().ConvertFromInvariantString(
      content.Attribute("Margin")?.Value ?? string.Empty)!;
    Assert.True(margin.Top < 46, "Scrollable content must begin below the fixed header instead of using an overlay-sized top margin.");
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ReaderSidebar_ScrollingMovesTheFocusedControlInsideItsClippedViewport()
  {
    RunOnSta(() =>
    {
      Window window = new()
      {
        Width = 360,
        Height = 420,
        Left = -10_000,
        Top = -10_000,
        ShowActivated = true,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
      };
      using ReaderSidebarView sidebar = new();
      window.Content = sidebar;
      Assert.IsType<Expander>(sidebar.FindName("ReadingAppearanceExpander")).IsExpanded = true;
      sidebar.Render(new ReaderSidebarPresentation(true, true, true, true, true, false));
      try
      {
        window.Show();
        window.Activate();
        window.UpdateLayout();

        ScrollViewer viewport = Assert.IsType<ScrollViewer>(sidebar.FindName("ReaderSidebarScrollViewer"));
        Slider target = Assert.IsType<Slider>(sidebar.FindName("FontSizeSlider"));
        target.BringIntoView();
        Assert.True(target.Focus());
        window.UpdateLayout();
        Point before = target.TransformToAncestor(viewport).Transform(new Point());
        double originalOffset = viewport.VerticalOffset;
        viewport.ScrollToVerticalOffset(Math.Min(viewport.ScrollableHeight, originalOffset + 80));
        window.UpdateLayout();
        Point after = target.TransformToAncestor(viewport).Transform(new Point());

        Assert.True(viewport.VerticalOffset > originalOffset);
        Assert.True(after.Y < before.Y, "The focused control geometry must move with scrolled content.");
        Assert.True(viewport.ClipToBounds);
        Assert.NotNull(AdornerLayer.GetAdornerLayer(target));
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  public void ReaderDraft_PreservesPlainTabAndDefinesCtrlTabAsTheEscapeRoute()
  {
    MethodInfo? method = typeof(ReaderDocumentView).GetMethod(
      "ShouldMoveFocusOutOfDraft",
      BindingFlags.NonPublic | BindingFlags.Static);
    Assert.NotNull(method);

    Assert.False(InvokeBoolean(method, Key.Tab, ModifierKeys.None));
    Assert.True(InvokeBoolean(method, Key.Tab, ModifierKeys.Control));
    Assert.True(InvokeBoolean(method, Key.Tab, ModifierKeys.Control | ModifierKeys.Shift));
    Assert.False(InvokeBoolean(method, Key.Enter, ModifierKeys.Control));

    XDocument document = XDocument.Load(Path.Combine(
      FindRepoRoot(),
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading",
      "ReaderDocumentView.xaml"));
    XElement draft = document.Descendants().Single(element =>
      element.Attribute(XamlNamespace + "Name")?.Value == "DraftTextBox");
    Assert.Equal("OnDraftPreviewKeyDown", draft.Attribute("PreviewKeyDown")?.Value);
  }

  [Fact]
  public void ReaderDraft_UsesConciseNormalHelpSeparateFromValidation()
  {
    XDocument document = XDocument.Load(Path.Combine(
      FindRepoRoot(),
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading",
      "ReaderDocumentView.xaml"));
    XElement draft = document.Descendants().Single(element =>
      element.Attribute(XamlNamespace + "Name")?.Value == "DraftTextBox");

    XElement toolTip = draft.Descendants().Single(element => element.Name.LocalName == "ToolTip");
    XElement helpText = toolTip.Descendants().Single(element => element.Name.LocalName == "TextBlock");
    Assert.Equal("Edit narration text", helpText.Attribute("Text")?.Value);
    Assert.Equal("Segoe UI", helpText.Attribute("FontFamily")?.Value);
    Assert.Equal("{DynamicResource Font.Size.Body}", helpText.Attribute("FontSize")?.Value);
    Assert.Equal("{DynamicResource Line.Height.Caption}", helpText.Attribute("LineHeight")?.Value);
    Assert.Equal("BlockLineHeight", helpText.Attribute("LineStackingStrategy")?.Value);
    Assert.Equal("Wrap", helpText.Attribute("TextWrapping")?.Value);
    Assert.Equal("380", toolTip.Attribute("MaxWidth")?.Value);
    Assert.Equal("Edit narration text", draft.Attribute("AutomationProperties.HelpText")?.Value);
    Assert.Equal("Reading draft", draft.Attribute("AutomationProperties.Name")?.Value);
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ReaderDraft_TabInsertionFormsItsOwnUndoUnit()
  {
    RunOnSta(() =>
    {
      Window window = new()
      {
        Width = 800,
        Height = 600,
        Left = -10_000,
        Top = -10_000,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
      };
      using ReaderDocumentView view = new();
      window.Content = view;
      try
      {
        view.RenderSurface(ReaderDocumentSurface.Draft);
        window.Show();
        window.UpdateLayout();
        TextBox editor = Assert.IsType<TextBox>(view.FindName("DraftTextBox"));
        editor.Text = string.Empty;
        editor.IsUndoEnabled = false;
        editor.IsUndoEnabled = true;
        using (editor.DeclareChangeBlock())
        {
          editor.SelectedText = "hi";
        }
        editor.LockCurrentUndoUnit();
        editor.CaretIndex = editor.Text.Length;

        MethodInfo method = typeof(ReaderDocumentView).GetMethod(
          "InsertDraftTabAsUndoUnit",
          BindingFlags.NonPublic | BindingFlags.Static)
          ?? throw new InvalidOperationException("The Reader draft Tab undo boundary was not found.");
        method.Invoke(null, [editor]);

        Assert.Equal("hi\t", editor.Text);
        Assert.True(editor.Undo());
        Assert.Equal("hi", editor.Text);
        Assert.True(editor.Undo());
        Assert.Equal(string.Empty, editor.Text);
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ReaderDraft_ProgrammaticLoadClearsHistoryAndValidationDoesNotReplaceItsName()
  {
    RunOnSta(() =>
    {
      Window window = new()
      {
        Width = 800,
        Height = 600,
        Left = -10_000,
        Top = -10_000,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
      };
      using ReaderDocumentView view = new();
      window.Content = view;
      try
      {
        view.RenderSurface(ReaderDocumentSurface.Draft);
        window.Show();
        TextBox editor = Assert.IsType<TextBox>(view.FindName("DraftTextBox"));
        editor.IsUndoEnabled = false;
        editor.IsUndoEnabled = true;
        using (editor.DeclareChangeBlock())
        {
          editor.SelectedText = "stale user edit";
        }
        Assert.True(editor.CanUndo);

        view.SetDraftText("loaded draft");
        Assert.False(editor.CanUndo);
        Assert.Equal("loaded draft", editor.Text);

        view.SetDraftValidationMessage("Type some text before creating a Videobook.");
        Assert.Equal("Reading draft", AutomationProperties.GetName(editor));
        Assert.Equal("Type some text before creating a Videobook.", AutomationProperties.GetHelpText(editor));
        ToolTip toolTip = Assert.IsType<ToolTip>(editor.ToolTip);
        TextBlock toolTipText = Assert.IsType<TextBlock>(toolTip.Content);
        editor.FontSize = 38;
        TextBlock.SetLineHeight(editor, 72);
        toolTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal("Segoe UI", toolTipText.FontFamily.Source);
        Assert.Equal(14, toolTipText.FontSize);
        Assert.Equal(17, toolTipText.LineHeight);
        Assert.True(toolTip.DesiredSize.Width <= 380 && toolTip.DesiredSize.Height < 60);
        Assert.Equal("Type some text before creating a Videobook.", toolTipText.Text);

        view.ClearDraftValidationMessage();
        Assert.Equal("Reading draft", AutomationProperties.GetName(editor));
        Assert.Equal("Edit narration text", AutomationProperties.GetHelpText(editor));
        Assert.Equal("Edit narration text", toolTipText.Text);
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ReaderDraft_TabUndoRestoresReplacedSelectionAndCaret()
  {
    RunOnSta(() =>
    {
      Window window = new()
      {
        Width = 800,
        Height = 600,
        Left = -10_000,
        Top = -10_000,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
      };
      using ReaderDocumentView view = new();
      window.Content = view;
      try
      {
        view.RenderSurface(ReaderDocumentSurface.Draft);
        window.Show();
        TextBox editor = Assert.IsType<TextBox>(view.FindName("DraftTextBox"));
        editor.Text = "before selected after";
        editor.IsUndoEnabled = false;
        editor.IsUndoEnabled = true;
        editor.Select(7, 8);

        MethodInfo method = typeof(ReaderDocumentView).GetMethod(
          "InsertDraftTabAsUndoUnit",
          BindingFlags.NonPublic | BindingFlags.Static)
          ?? throw new InvalidOperationException("The Reader draft Tab undo boundary was not found.");
        method.Invoke(null, [editor]);

        Assert.Equal("before \t after", editor.Text);
        Assert.True(editor.Undo());
        Assert.Equal("before selected after", editor.Text);
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Theory]
  [InlineData("Workbench/AboutWindow.xaml", "AboutContentScrollViewer", "About Koncus Nai content")]
  [InlineData("Workbench/Reading/ReadingStudioHelpWindow.xaml", "HelpContentScrollViewer", "Reading Studio help content")]
  public void StaticInformationWindows_ExposeOneNamedKeyboardScrollableRegion(
    string relativePath,
    string expectedName,
    string expectedAutomationName)
  {
    XDocument document = XDocument.Load(Path.Combine(
      FindRepoRoot(),
      "src",
      "DictateAnywhere.App",
      relativePath.Replace('/', Path.DirectorySeparatorChar)));
    XElement scrollViewer = document.Descendants().Single(element =>
      element.Name.LocalName == "ScrollViewer"
      && element.Attribute(XamlNamespace + "Name")?.Value == expectedName);

    Assert.Equal("True", scrollViewer.Attribute("Focusable")?.Value);
    Assert.Equal("True", scrollViewer.Attribute("IsTabStop")?.Value);
    Assert.Equal("{DynamicResource AppKeyboardFocusVisualStyle}", scrollViewer.Attribute("FocusVisualStyle")?.Value);
    Assert.Equal(expectedAutomationName, scrollViewer.Attribute("AutomationProperties.Name")?.Value);
    Assert.Equal("True", scrollViewer.Attributes().Single(attribute =>
      attribute.Name.LocalName == "StaticContentScrollViewerBehavior.IsEnabled").Value);
    Assert.DoesNotContain(scrollViewer.Descendants(), element =>
      element.Name.LocalName == "TextBlock"
      && string.Equals(element.Attribute("Focusable")?.Value, "True", StringComparison.OrdinalIgnoreCase));
  }

  private static bool InvokeBoolean(MethodInfo method, Key key, ModifierKeys modifiers) =>
    Assert.IsType<bool>(method.Invoke(null, [key, modifiers]));

  private static string FindRepoRoot()
  {
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "DictateAnywhere.sln")))
      {
        return current.FullName;
      }
      current = current.Parent;
    }
    throw new InvalidOperationException("Could not locate the repository root.");
  }

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
    Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA test thread timed out.");
    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
  }
}
