using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Publishing;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class AccessibilityFocusAutomationTests
{
  private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

  [Fact]
  public void PrimaryCustomStyles_HaveAKeyboardFocusCueWithoutLayoutDimensions()
  {
    string root = FindRepoRoot();
    string controlStylesPath = Path.Combine(root, "src", "DictateAnywhere.App", "Theming", "ControlStyles.xaml");
    XDocument controls = XDocument.Load(controlStylesPath);
    XElement focusStyle = FindStyle(controls, "AppKeyboardFocusVisualStyle");
    XElement[] outlines = focusStyle.Descendants().Where(element => element.Name.LocalName == "Border").ToArray();
    Assert.Equal(2, outlines.Length);
    Assert.Equal(["3", "1"], outlines.Select(outline => outline.Attribute("BorderThickness")?.Value));
    Assert.Equal(
      ["{DynamicResource Brush.Surface.Base}", "{DynamicResource Brush.Text.Primary}"],
      outlines.Select(outline => outline.Attribute("BorderBrush")?.Value));
    Assert.All(outlines, outline =>
    {
      Assert.Null(outline.Attribute("Width"));
      Assert.Null(outline.Attribute("Height"));
    });

    (string RelativePath, string StyleKey, bool UsesSharedCue)[] cases =
    [
      ("src/DictateAnywhere.App/Theming/ControlStyles.xaml", "AppActionButtonStyle", true),
      ("src/DictateAnywhere.App/Workbench/WorkbenchSidebarView.xaml", "SidebarActionButtonStyle", true),
      ("src/DictateAnywhere.App/Workbench/WorkbenchQuickSettingsView.xaml", "SettingsMenuButtonStyle", true),
      ("src/DictateAnywhere.App/Workbench/WorkbenchQuickSettingsView.xaml", "ChatTextSizeSliderStyle", true),
      ("src/DictateAnywhere.App/Workbench/Reading/ReaderWindow.xaml", "ReaderControl", false),
      ("src/DictateAnywhere.App/Workbench/Reading/ReaderWindow.xaml", "ReaderSecondaryButton", true),
      ("src/DictateAnywhere.App/Workbench/Reading/ReaderWindow.xaml", "ReaderBareIconButton", false),
      ("src/DictateAnywhere.App/Workbench/Reading/ReaderWindow.xaml", "ReaderWindowControlButton", true),
      ("src/DictateAnywhere.App/Workbench/Reading/ReaderWindow.xaml", "ReaderSlider", true),
      ("src/DictateAnywhere.App/Workbench/Reading/ReaderWindow.xaml", "ReaderProcessingToggle", false),
      ("src/DictateAnywhere.App/Workbench/Reading/ReaderWindow.xaml", "ReaderSwatchItem", false),
      ("src/DictateAnywhere.App/Workbench/Publishing/YouTubePublishingWindow.xaml", "PublishingButtonBase", true),
      ("src/DictateAnywhere.App/Workbench/Publishing/YouTubePublishingWindow.xaml", "PublishingWindowControlButton", true),
    ];

    foreach ((string relativePath, string styleKey, bool usesSharedCue) in cases)
    {
      XDocument document = XDocument.Load(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
      XElement style = FindStyle(document, styleKey);
      if (usesSharedCue)
      {
        Assert.Contains(style.Descendants(), element =>
          element.Name.LocalName == "Setter"
          && element.Attribute("Property")?.Value == "FocusVisualStyle"
          && element.Attribute("Value")?.Value?.Contains("AppKeyboardFocusVisualStyle", StringComparison.Ordinal) == true);
      }
      else
      {
        Assert.Contains(style.Descendants(), element =>
          element.Name.LocalName == "Trigger"
          && element.Attribute("Property")?.Value is "IsKeyboardFocused" or "IsKeyboardFocusWithin"
          && element.Attribute("Value")?.Value == "True");
      }
    }

    XDocument workbench = XDocument.Load(Path.Combine(
      root,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "TextboxWorkbenchWindow.xaml"));
    XElement composerTextBoxStyle = FindStyle(workbench, "ComposerTextBoxStyle");
    Assert.Contains(composerTextBoxStyle.Descendants(), element =>
      element.Name.LocalName == "Setter"
      && element.Attribute("Property")?.Value == "FocusVisualStyle"
      && element.Attribute("Value")?.Value == "{x:Null}");

    XDocument readerWindow = XDocument.Load(Path.Combine(
      root,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading",
      "ReaderWindow.xaml"));
    XElement readerDraftInputStyle = FindStyle(readerWindow, "ReaderDraftInput");
    Assert.Contains(readerDraftInputStyle.Descendants(), element =>
      element.Name.LocalName == "Setter"
      && element.Attribute("Property")?.Value == "FocusVisualStyle"
      && element.Attribute("Value")?.Value == "{x:Null}");
    Assert.Contains(readerDraftInputStyle.Descendants(), element =>
      element.Name.LocalName == "Setter"
      && element.Attribute("Property")?.Value == "Template"
      && element.Attribute("Value")?.Value == "{StaticResource ReaderDraftInputTemplate}");
    XElement readerDraftInputTemplate = readerWindow.Descendants().Single(element =>
      element.Name.LocalName == "ControlTemplate"
      && string.Equals(element.Attribute(XNs + "Key")?.Value, "ReaderDraftInputTemplate", StringComparison.Ordinal));
    Assert.DoesNotContain(readerDraftInputTemplate.Descendants(), element => element.Name.LocalName == "Border");
    XElement draftContentHost = Assert.Single(readerDraftInputTemplate.Descendants().Where(element =>
      element.Name.LocalName == "ScrollViewer"
      && string.Equals(element.Attribute(XNs + "Name")?.Value, "PART_ContentHost", StringComparison.Ordinal)));
    Assert.Equal("False", draftContentHost.Attribute("Focusable")?.Value);
    Assert.Equal("{TemplateBinding Padding}", draftContentHost.Attribute("Padding")?.Value);

    XDocument readerDocumentView = XDocument.Load(Path.Combine(
      root,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading",
      "ReaderDocumentView.xaml"));
    XElement draftFocusBoundary = readerDocumentView.Descendants().Single(element =>
      element.Name.LocalName == "Border"
      && string.Equals(element.Attribute(XNs + "Name")?.Value, "DraftFocusBoundary", StringComparison.Ordinal));
    Assert.Equal("False", draftFocusBoundary.Attribute("Focusable")?.Value);
    Assert.Equal("False", draftFocusBoundary.Attribute("IsHitTestVisible")?.Value);
    Assert.Equal("2", draftFocusBoundary.Attribute("BorderThickness")?.Value);
    Assert.Equal("Transparent", draftFocusBoundary.Attribute("BorderBrush")?.Value);
    Assert.Equal("{Binding ActualWidth, ElementName=ReaderPageFrame}", draftFocusBoundary.Attribute("Width")?.Value);
    Assert.Equal("{Binding ActualHeight, ElementName=ReaderPageFrame}", draftFocusBoundary.Attribute("Height")?.Value);
  }

  [Fact]
  public void RepresentativePrimaryControls_ResolveTheSharedFocusVisual()
  {
    string root = FindRepoRoot();
    XDocument workbench = XDocument.Load(Path.Combine(
      root,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "TextboxWorkbenchWindow.xaml"));
    XDocument composer = XDocument.Load(Path.Combine(
      root,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "WorkbenchComposerView.xaml"));
    XElement composerIconStyle = FindStyle(workbench, "ComposerIconButtonStyle");
    Assert.Equal("{StaticResource AppToolbarIconButtonStyle}", composerIconStyle.Attribute("BasedOn")?.Value);
    foreach (string name in new[] { "NewChatButton", "AddFile", "ReadDocument", "ExportChatButton" })
    {
      XElement button = composer.Descendants().Single(element => element.Attribute(XNs + "Name")?.Value == name);
      Assert.Equal(name == "ExportChatButton" ? "{DynamicResource ComposerIconButtonStyle}" : "{DynamicResource AppActionButtonStyle}", button.Attribute("Style")?.Value);
    }

    RunOnSta(() =>
    {
      WorkbenchSidebarView sidebar = new();
      WorkbenchQuickSettingsView quickSettings = new();
      YouTubePublishingWindow publishing = new("Safe fixture", 1);
      try
      {
        foreach (string name in new[] { "SettingsButton" })
        {
          Assert.NotNull(Assert.IsType<Button>(sidebar.FindName(name)).FocusVisualStyle);
        }

        foreach (string name in new[] { "AdvancedButton", "ThemeButton", "AboutButton" })
        {
          Assert.NotNull(Assert.IsType<Button>(quickSettings.FindName(name)).FocusVisualStyle);
        }

        Assert.NotNull(Assert.IsType<Slider>(quickSettings.FindName("TextSizeSlider")).FocusVisualStyle);
        Assert.NotNull(Assert.IsType<Button>(publishing.FindName("ConnectButton")).FocusVisualStyle);
        Assert.NotNull(Assert.IsType<Button>(publishing.FindName("ClosePublishingButton")).FocusVisualStyle);
      }
      finally
      {
        publishing.Close();
      }
    });
  }

  [Fact]
  public void FocusOutline_UsesDistinctDarkLightAndHighContrastSemanticColors()
  {
    Type managerType = typeof(AppThemeManager);
    IReadOnlyDictionary<string, Color> dark = (IReadOnlyDictionary<string, Color>)(managerType
      .GetField("DarkPalette", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
      ?? throw new InvalidOperationException("Dark palette was not found."));
    IReadOnlyDictionary<string, Color> light = (IReadOnlyDictionary<string, Color>)(managerType
      .GetField("LightPalette", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
      ?? throw new InvalidOperationException("Light palette was not found."));
    IReadOnlyDictionary<string, Color> highContrast = AppThemeManager.GetHighContrastPalette();

    foreach (IReadOnlyDictionary<string, Color> palette in new[] { dark, light, highContrast })
    {
      Assert.NotEqual(palette["Brush.Surface.Base"], palette["Brush.Text.Primary"]);
      Assert.NotEqual(palette["Brush.Surface.Composer"], palette["Brush.Control.Primary"]);
    }

    Assert.Equal(SystemColors.WindowColor, highContrast["Brush.Surface.Base"]);
    Assert.Equal(SystemColors.WindowTextColor, highContrast["Brush.Text.Primary"]);
  }

  [Fact]
  public void AccessibilityPreflightPolicy_HasNonzeroUniqueStateAwareScenarios()
  {
    string policyPath = Path.Combine(FindRepoRoot(), "scripts", "accessibility-preflight-scenarios.json");
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(policyPath));
    JsonElement root = document.RootElement;
    Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
    Assert.Equal("NotTested", root.GetProperty("manualEvidenceStatus").GetString());

    JsonElement.ArrayEnumerator scenarios = root.GetProperty("scenarios").EnumerateArray();
    HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
    HashSet<string> states = new(StringComparer.Ordinal) { "enabled", "disabled", "hidden", "conditional" };
    int scenarioCount = 0;
    int controlCount = 0;
    foreach (JsonElement scenario in scenarios)
    {
      scenarioCount++;
      string id = scenario.GetProperty("id").GetString() ?? string.Empty;
      Assert.False(string.IsNullOrWhiteSpace(id));
      Assert.True(ids.Add(id), $"Duplicate scenario id: {id}");
      Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("owner").GetString()));
      JsonElement controls = scenario.GetProperty("controls");
      Assert.True(controls.GetArrayLength() > 0, $"Scenario '{id}' has no controls.");
      foreach (JsonElement control in controls.EnumerateArray())
      {
        controlCount++;
        Assert.False(string.IsNullOrWhiteSpace(control.GetProperty("name").GetString()));
        Assert.Contains(control.GetProperty("state").GetString() ?? string.Empty, states);
      }
    }

    Assert.True(scenarioCount >= 10);
    Assert.True(controlCount >= 30);
    Assert.Contains("workbench-empty", ids);
    Assert.Contains("workbench-long-input-model-ready", ids);
    Assert.Contains("workbench-sidebar-hidden", ids);
    Assert.Contains("quick-settings-focus-contained", ids);
    Assert.Contains("settings-focus-contained", ids);
    Assert.Contains("settings-scroll-focus-clipped", ids);
    Assert.Contains("workbench-minimum-width-composer", ids);
    Assert.Contains("reading-studio-work-area-placement", ids);
    Assert.Contains("reading-studio-sidebar-scroll-focus-clipped", ids);
    Assert.Contains("reading-studio-draft-tab-escape", ids);
    Assert.Contains("reading-studio-document-gutter-symmetry", ids);
    Assert.Contains("reader-hover-help-centered-overlay-and-composer-and-draft-focus", ids);
    Assert.Contains("static-information-scroll-regions", ids);
    Assert.Contains("publishing-safe-default", ids);

    JsonElement staticContent = root.GetProperty("scenarios").EnumerateArray().Single(
      scenario => scenario.GetProperty("id").GetString() == "static-information-scroll-regions");
    string[] staticExpectations = staticContent.GetProperty("controls").EnumerateArray()
      .Select(control => control.GetProperty("name").GetString() ?? string.Empty)
      .ToArray();
    Assert.Contains("Home reaches vertical beginning", staticExpectations);
    Assert.Contains("End reaches vertical end", staticExpectations);
    Assert.Contains("Page Up and Page Down remain native", staticExpectations);
    Assert.Contains("Keyboard focus remains on content", staticExpectations);
    Assert.Contains("Caption controls remain isolated", staticExpectations);

    JsonElement readerLayout = root.GetProperty("scenarios").EnumerateArray().Single(
      scenario => scenario.GetProperty("id").GetString() == "reading-studio-document-gutter-symmetry");
    string[] readerLayoutExpectations = readerLayout.GetProperty("controls").EnumerateArray()
      .Select(control => control.GetProperty("name").GetString() ?? string.Empty)
      .ToArray();
    Assert.Contains("Standard draft outer gutter symmetry", readerLayoutExpectations);
    Assert.Contains("Standard draft effective text gutter symmetry", readerLayoutExpectations);
    Assert.Contains("Draft hidden scrollbar lane", readerLayoutExpectations);
    Assert.Contains("Draft visible scrollbar lane", readerLayoutExpectations);
    Assert.Contains("Read-only text alignment", readerLayoutExpectations);
    Assert.Contains("Light Dark and High Contrast geometry invariance", readerLayoutExpectations);
    Assert.Contains("Minimum and maximum Reader page widths", readerLayoutExpectations);
    Assert.Contains("Distraction-free explicit geometry", readerLayoutExpectations);
  }

  [Fact]
  public void WorkbenchOverlays_DeclareActiveSurfaceContainmentAndUnderlyingInteractionBoundary()
  {
    string root = FindRepoRoot();
    XDocument quickSettings = XDocument.Load(Path.Combine(
      root,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "WorkbenchQuickSettingsView.xaml"));
    XElement quickSurface = quickSettings.Descendants().Single(element =>
      element.Attribute(XNs + "Name")?.Value == "Surface");
    Assert.Equal("Cycle", quickSurface.Attribute("KeyboardNavigation.TabNavigation")?.Value);
    Assert.Equal("OnPreviewKeyDown", quickSettings.Root?.Attribute("PreviewKeyDown")?.Value);

    XDocument workbench = XDocument.Load(Path.Combine(
      root,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "TextboxWorkbenchWindow.xaml"));
    Assert.Single(workbench.Descendants().Where(element =>
      element.Attribute(XNs + "Name")?.Value == "WorkbenchInteractionSurface"));

    XElement workbenchRoot = workbench.Descendants().Single(element =>
      element.Attribute(XNs + "Name")?.Value == "WorkbenchRoot");
    Assert.Equal("Cycle", workbenchRoot.Attribute("KeyboardNavigation.TabNavigation")?.Value);
    XElement interactionSurface = workbench.Descendants().Single(element =>
      element.Attribute(XNs + "Name")?.Value == "WorkbenchInteractionSurface");
    Assert.Contains(interactionSurface.Descendants(), element =>
      element.Attribute(XNs + "Name")?.Value == "ComposerView");
    Assert.DoesNotContain(interactionSurface.Descendants(), element =>
      element.Attribute(XNs + "Name")?.Value is "QuickSettingsView" or "InlineSettingsView");
  }

  [Fact]
  public void SettingsScrollViewport_OwnsAndClipsKeyboardFocusAdorners()
  {
    string path = Path.Combine(
      FindRepoRoot(),
      "src",
      "DictateAnywhere.App",
      "Settings",
      "SettingsPanel.xaml");
    XDocument settings = XDocument.Load(path);
    XElement scrollViewer = settings.Descendants().Single(element =>
      element.Attribute(XNs + "Name")?.Value == "SettingsScrollViewer");
    Assert.Equal("True", scrollViewer.Attribute("ClipToBounds")?.Value);
    XElement adornerDecorator = Assert.Single(scrollViewer.Elements().Where(element =>
      element.Name.LocalName == "AdornerDecorator"));
    Assert.Equal("True", adornerDecorator.Attribute("ClipToBounds")?.Value);
    Assert.Contains(settings.Descendants(), element =>
      element.Attribute(XNs + "Name")?.Value == "BackButton");
  }

  [Fact]
  public void SidebarToggles_UseSharedSemanticGlyphAndActionNames()
  {
    string root = FindRepoRoot();
    string[] toggleFiles =
    [
      Path.Combine(root, "src", "DictateAnywhere.App", "Workbench", "TextboxWorkbenchWindow.xaml"),
      Path.Combine(root, "src", "DictateAnywhere.App", "Workbench", "Reading", "ReaderWindow.xaml"),
    ];

    foreach (string toggleFile in toggleFiles)
    {
      XDocument document = XDocument.Load(toggleFile);
      XElement toggle = document.Descendants().Single(element =>
        element.Attribute(XNs + "Name")?.Value == "SidebarToggleButton");
      XElement glyph = Assert.Single(toggle.Elements().Where(element => element.Name.LocalName == "Path"));
      Assert.Equal("SidebarToggleGlyph", glyph.Attribute(XNs + "Name")?.Value);
      Assert.Equal("{StaticResource AppSidebarToggleIconStyle}", glyph.Attribute("Style")?.Value);
      Assert.DoesNotContain(toggle.Descendants(), element => element.Name.LocalName == "TextBlock");
    }

    XDocument controlStyles = XDocument.Load(Path.Combine(
      root,
      "src",
      "DictateAnywhere.App",
      "Theming",
      "ControlStyles.xaml"));
    XElement iconStyle = FindStyle(controlStyles, "AppSidebarToggleIconStyle");
    Assert.Equal("Path", iconStyle.Attribute("TargetType")?.Value);
    Assert.Contains(iconStyle.Elements(), element =>
      element.Name.LocalName == "Setter"
      && element.Attribute("Property")?.Value == "Stroke"
      && element.Attribute("Value")?.Value?.Contains("AncestorType={x:Type Button}", StringComparison.Ordinal) == true);
    Assert.Contains(iconStyle.Elements(), element =>
      element.Name.LocalName == "Setter"
      && element.Attribute("Property")?.Value == "IsHitTestVisible"
      && element.Attribute("Value")?.Value == "False");
    Assert.Contains(iconStyle.Elements(), element =>
      element.Name.LocalName == "Setter"
      && element.Attribute("Property")?.Value == "Focusable"
      && element.Attribute("Value")?.Value == "False");

    RunOnSta(() =>
    {
      Button toggle = new();
      TextboxWorkbenchWindow.SetSidebarToggleAccessibility(toggle, isSidebarVisible: true);
      Assert.Equal("Hide sidebar", AutomationProperties.GetName(toggle));
      Assert.Equal("Hide sidebar", toggle.ToolTip);

      TextboxWorkbenchWindow.SetSidebarToggleAccessibility(toggle, isSidebarVisible: false);
      Assert.Equal("Show sidebar", AutomationProperties.GetName(toggle));
      Assert.Equal("Show sidebar", toggle.ToolTip);

      Style sharedStyle = Assert.IsType<Style>(Application.Current.FindResource("AppSidebarToggleIconStyle"));
      SolidColorBrush initialBrush = new(Colors.CornflowerBlue);
      Button renderedToggle = new() { Foreground = initialBrush };
      System.Windows.Shapes.Path renderedGlyph = new() { Style = sharedStyle };
      renderedToggle.Content = renderedGlyph;
      renderedToggle.Measure(new Size(36, 36));
      renderedToggle.Arrange(new Rect(0, 0, 36, 36));
      renderedToggle.UpdateLayout();

      Assert.Equal(16, renderedGlyph.ActualWidth);
      Assert.Equal(16, renderedGlyph.ActualHeight);
      Assert.Same(initialBrush, renderedGlyph.Stroke);
      Assert.False(renderedGlyph.IsHitTestVisible);
      Assert.False(renderedGlyph.Focusable);
      Assert.NotNull(renderedGlyph.Data);
      Assert.Equal(2, renderedGlyph.Data.GetFlattenedPathGeometry().Figures.Count);

      SolidColorBrush updatedBrush = new(Colors.Gold);
      renderedToggle.Foreground = updatedBrush;
      Assert.Same(updatedBrush, renderedGlyph.Stroke);
    });
  }

  private static XElement FindStyle(XDocument document, string key) =>
    document.Descendants().Single(element =>
      element.Name.LocalName == "Style"
      && string.Equals(element.Attribute(XNs + "Key")?.Value, key, StringComparison.Ordinal));

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
      throw new InvalidOperationException("STA test execution failed.", failure);
    }
  }
}
