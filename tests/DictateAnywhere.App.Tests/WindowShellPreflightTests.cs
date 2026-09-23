using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using DictateAnywhere.App.Hotkeys;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Publishing;
using DictateAnywhere.App.Workbench.Reading;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class WindowShellPreflightTests
{
  private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

  [Fact]
  public void ShellPolicy_MatchesCanonicalInventoryAndXamlContracts()
  {
    string root = FindRepoRoot();
    string policyPath = Path.Combine(root, "scripts", "accessibility-preflight-scenarios.json");
    using JsonDocument policy = JsonDocument.Parse(File.ReadAllText(policyPath));
    JsonElement policyRoot = policy.RootElement;
    Assert.Equal(2, policyRoot.GetProperty("schemaVersion").GetInt32());
    Assert.Equal("NotTested", policyRoot.GetProperty("manualEvidenceStatus").GetString());

    string inventoryRelative = policyRoot.GetProperty("canonicalShellInventory").GetString()
      ?? throw new InvalidOperationException("The shell inventory path is missing.");
    string inventoryPath = Path.Combine(root, inventoryRelative.Replace('/', Path.DirectorySeparatorChar));
    string[] inventoryNames = ReadInventoryNames(inventoryPath);
    Assert.Equal(11, inventoryNames.Length);

    JsonElement[] shells = policyRoot.GetProperty("shells").EnumerateArray().ToArray();
    Assert.Equal(inventoryNames.Length, shells.Length);
    Assert.Equal(
      inventoryNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
      shells.Select(shell => shell.GetProperty("inventoryName").GetString() ?? string.Empty)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
      StringComparer.OrdinalIgnoreCase);

    HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
    foreach (JsonElement shell in shells)
    {
      string id = Required(shell, "id");
      string xamlRelative = Required(shell, "xamlPath");
      string codeBehindRelative = Required(shell, "codeBehindPath");
      Assert.True(paths.Add(Normalize(xamlRelative)), $"Duplicate shell path: {xamlRelative}");
      Assert.True(paths.Add(Normalize(codeBehindRelative)), $"Duplicate shell path: {codeBehindRelative}");

      string xamlPath = Path.Combine(root, xamlRelative.Replace('/', Path.DirectorySeparatorChar));
      string codeBehindPath = Path.Combine(root, codeBehindRelative.Replace('/', Path.DirectorySeparatorChar));
      Assert.True(File.Exists(xamlPath), $"Shell '{id}' XAML is missing: {xamlRelative}");
      Assert.True(File.Exists(codeBehindPath), $"Shell '{id}' code-behind is missing: {codeBehindRelative}");

      XDocument xaml = XDocument.Load(xamlPath);
      XElement window = xaml.Root ?? throw new InvalidOperationException($"Shell '{id}' has no XAML root.");
      Assert.Equal("Window", window.Name.LocalName);
      Assert.Equal(Required(shell, "className"), window.Attribute(XamlNamespace + "Class")?.Value);
      Assert.Equal(shell.GetProperty("defaultSize").GetProperty("width").GetInt32(), ReadInt(window, "Width"));
      Assert.Equal(shell.GetProperty("defaultSize").GetProperty("height").GetInt32(), ReadInt(window, "Height"));
      Assert.Equal(shell.GetProperty("minimumSize").GetProperty("width").GetInt32(), ReadInt(window, "MinWidth", 0));
      Assert.Equal(shell.GetProperty("minimumSize").GetProperty("height").GetInt32(), ReadInt(window, "MinHeight", 0));
      Assert.Equal(Required(shell, "resizeMode"), window.Attribute("ResizeMode")?.Value ?? "CanResize");

      string chrome = Required(shell, "chrome");
      if (chrome == "custom-caption")
      {
        Assert.Contains(window.Descendants(), element => element.Name.LocalName == nameof(WindowCaptionButtons));
      }
      else if (chrome == "specialized-custom")
      {
        Assert.Contains(window.Descendants(), element =>
          element.Attribute(XamlNamespace + "Name")?.Value == "ClosePublishingButton");
      }

      bool isToast = chrome == "toast";
      bool hasThemeBehavior = window.Attributes().Any(attribute =>
        attribute.Name.LocalName.EndsWith(".IsEnabled", StringComparison.Ordinal)
        && attribute.Name.NamespaceName.Contains("DictateAnywhere.App.Presentation", StringComparison.Ordinal)
        && attribute.Value == "True");
      Assert.Equal(!isToast, hasThemeBehavior);
      Assert.NotEmpty(shell.GetProperty("states").EnumerateArray());
      Assert.NotEmpty(shell.GetProperty("capabilities").EnumerateArray());
      Assert.NotEmpty(shell.GetProperty("manualChecks").EnumerateArray());
      Assert.All(shell.GetProperty("operatorCases").EnumerateArray(), testCase =>
        Assert.Equal("NotTested", testCase.GetProperty("status").GetString()));
    }
  }

  [Fact]
  public void HighContrastClientBoundaryStyle_IsSemanticNoninteractiveAndLayoutNeutral()
  {
    string root = FindRepoRoot();
    string stylesPath = Path.Combine(root, "src", "DictateAnywhere.App", "Theming", "ControlStyles.xaml");
    XDocument styles = XDocument.Load(stylesPath);
    XElement boundaryStyle = Assert.Single(styles.Descendants().Where(element =>
      element.Name.LocalName == "Style"
      && element.Attribute(XamlNamespace + "Key")?.Value == "AppHighContrastWindowBoundaryStyle"));

    Dictionary<string, string> setters = boundaryStyle.Elements()
      .Where(element => element.Name.LocalName == "Setter")
      .ToDictionary(
        element => RequiredAttribute(element, "Property"),
        element => RequiredAttribute(element, "Value"),
        StringComparer.Ordinal);

    Assert.Equal("{DynamicResource Brush.Border.Subtle}", setters["BorderBrush"]);
    Assert.Equal("0", setters["BorderThickness"]);
    Assert.Equal("False", setters["Focusable"]);
    Assert.Equal("False", setters["IsHitTestVisible"]);
    Assert.Equal("Transparent", setters["Background"]);
    Assert.False(setters.ContainsKey("Margin"));
    Assert.False(setters.ContainsKey("Padding"));
    Assert.Contains(boundaryStyle.Descendants(), element =>
      element.Name.LocalName == "Setter"
      && element.Attribute("Property")?.Value == "BorderThickness"
      && element.Attribute("Value")?.Value == "1");
    Assert.Contains(boundaryStyle.Descendants(), element =>
      element.Name.LocalName == "DataTrigger"
      && element.Attribute("Value")?.Value == "Maximized");
  }

  [Fact]
  public void CustomChromeShells_HaveExactlyOneClientRenderedHighContrastBoundary()
  {
    string root = FindRepoRoot();
    string policyPath = Path.Combine(root, "scripts", "accessibility-preflight-scenarios.json");
    using JsonDocument policy = JsonDocument.Parse(File.ReadAllText(policyPath));
    JsonElement[] customShells = policy.RootElement.GetProperty("shells")
      .EnumerateArray()
      .Where(shell =>
      {
        string chrome = Required(shell, "chrome");
        return chrome is "custom-caption" or "specialized-custom";
      })
      .ToArray();

    Assert.Equal(7, customShells.Length);
    foreach (JsonElement shell in customShells)
    {
      string id = Required(shell, "id");
      string xamlRelative = Required(shell, "xamlPath");
      XDocument xaml = XDocument.Load(Path.Combine(root, xamlRelative.Replace('/', Path.DirectorySeparatorChar)));
      XElement window = xaml.Root ?? throw new InvalidOperationException($"Shell '{id}' has no XAML root.");

      int sharedBoundaryCount = window.Descendants()
        .Count(element =>
          element.Name.LocalName == "Border"
          && string.Equals(
            element.Attribute("Style")?.Value,
            "{StaticResource AppHighContrastWindowBoundaryStyle}",
            StringComparison.Ordinal));
      int existingBoundaryCount = HasExistingRootBoundary(window) ? 1 : 0;

      Assert.True(
        sharedBoundaryCount + existingBoundaryCount == 1,
        $"Custom shell '{id}' must render exactly one High Contrast client boundary; "
        + $"found {sharedBoundaryCount} shared and {existingBoundaryCount} existing boundaries.");

      if (id is "about" or "reading-studio-help")
      {
        Assert.Equal(0, sharedBoundaryCount);
        Assert.Equal(1, existingBoundaryCount);
      }
      else
      {
        Assert.Equal(1, sharedBoundaryCount);
        Assert.Equal(0, existingBoundaryCount);
        XElement boundary = Assert.Single(window.Descendants().Where(element =>
          element.Name.LocalName == "Border"
          && string.Equals(
            element.Attribute("Style")?.Value,
            "{StaticResource AppHighContrastWindowBoundaryStyle}",
            StringComparison.Ordinal)));
        XElement parentGrid = boundary.Parent
          ?? throw new InvalidOperationException($"Custom shell '{id}' boundary has no parent grid.");
        Assert.Equal("Grid", parentGrid.Name.LocalName);
        int rowCount = CountGridDefinitions(parentGrid, "Grid.RowDefinitions");
        int columnCount = CountGridDefinitions(parentGrid, "Grid.ColumnDefinitions");
        Assert.True(ReadAttachedSpan(boundary, "Grid.RowSpan") >= rowCount,
          $"Custom shell '{id}' boundary does not cover all {rowCount} root rows.");
        Assert.True(ReadAttachedSpan(boundary, "Grid.ColumnSpan") >= columnCount,
          $"Custom shell '{id}' boundary does not cover all {columnCount} root columns.");
      }
    }

    JsonElement[] windowsOwnedByWindowsOrExempt = policy.RootElement.GetProperty("shells")
      .EnumerateArray()
      .Where(shell => Required(shell, "chrome") is "native" or "toast")
      .ToArray();
    Assert.Equal(4, windowsOwnedByWindowsOrExempt.Length);
    foreach (JsonElement shell in windowsOwnedByWindowsOrExempt)
    {
      string id = Required(shell, "id");
      string xamlRelative = Required(shell, "xamlPath");
      XDocument xaml = XDocument.Load(Path.Combine(root, xamlRelative.Replace('/', Path.DirectorySeparatorChar)));
      Assert.DoesNotContain(xaml.Descendants(), element =>
        element.Name.LocalName == "Border"
        && string.Equals(
          element.Attribute("Style")?.Value,
          "{StaticResource AppHighContrastWindowBoundaryStyle}",
          StringComparison.Ordinal));
    }
  }

  [Fact]
  public void SafeShells_ConstructAndCloseWithinBoundedStaLifetime()
  {
    RunOnSta(() =>
    {
      Func<Window>[] factories =
      [
        () => new AboutWindow(),
        () => new ReadingStudioHelpWindow(),
        () => new HotkeyTestWindow(),
        () => new YouTubePublishingWindow("Safe fixture", 1),
        () => new ReaderCompletionToastWindow("Safe fixture"),
      ];

      foreach (Func<Window> factory in factories)
      {
        Window window = factory();
        Assert.True(window.Width > 0);
        Assert.True(window.Height > 0);
        window.Close();
      }
    });
  }

  [Fact]
  public void CaptionCommands_TargetOnlyTheirOwningWindow()
  {
    RunOnSta(() =>
    {
      Window first = CreateOffscreenWindow();
      Window second = CreateOffscreenWindow();
      WindowCaptionButtons firstCaption = new() { ContextTitle = "First" };
      WindowCaptionButtons secondCaption = new() { ContextTitle = "Second" };
      first.Content = firstCaption;
      second.Content = secondCaption;
      bool firstClosed = false;
      first.Closed += (_, _) => firstClosed = true;
      try
      {
        first.Show();
        second.Show();
        firstCaption.UpdateLayout();
        secondCaption.UpdateLayout();

        Button firstMaximize = Assert.IsType<Button>(firstCaption.FindName("MaximizeRestoreButton"));
        firstMaximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Maximized, first.WindowState);
        Assert.Equal(WindowState.Normal, second.WindowState);

        Button firstClose = Assert.IsType<Button>(firstCaption.FindName("CloseButton"));
        firstClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        first.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        Assert.True(firstClosed);
        Assert.True(second.IsLoaded);
      }
      finally
      {
        if (first.IsLoaded)
        {
          first.Close();
        }
        second.Close();
      }
    });
  }

  [Fact]
  public void TrayKeyboardAlternatives_AreNamedAndRemainStructurallyWired()
  {
    string root = FindRepoRoot();
    string trayPath = Path.Combine(root, "src", "DictateAnywhere.App", "Tray", "TrayIconHost.cs");
    string source = File.ReadAllText(trayPath);

    Assert.Contains("Open Settings", source, StringComparison.Ordinal);
    Assert.Contains("Open Textbox Workbench", source, StringComparison.Ordinal);
    Assert.Contains("Open Dictation History", source, StringComparison.Ordinal);
    Assert.Contains("OpenSettingsRequested?.Invoke", source, StringComparison.Ordinal);
    Assert.Contains("OpenWorkbenchRequested?.Invoke", source, StringComparison.Ordinal);
    Assert.Contains("OpenHistoryRequested?.Invoke", source, StringComparison.Ordinal);
  }

  private static Window CreateOffscreenWindow() => new()
  {
    Width = 300,
    Height = 200,
    Left = -10_000,
    Top = -10_000,
    ShowActivated = false,
    ShowInTaskbar = false,
    WindowStyle = WindowStyle.None,
  };

  private static int ReadInt(XElement element, string name, int? defaultValue = null)
  {
    string? value = element.Attribute(name)?.Value;
    if (value is null && defaultValue.HasValue)
    {
      return defaultValue.Value;
    }
    return int.Parse(value ?? throw new InvalidOperationException($"Missing {name}."), CultureInfo.InvariantCulture);
  }

  private static bool HasExistingRootBoundary(XElement window)
  {
    XElement? contentRoot = window.Elements().FirstOrDefault(element =>
      element.Name.LocalName is not "Window.Resources" and not "WindowChrome.WindowChrome");
    return contentRoot is not null
      && contentRoot.Name.LocalName == "Border"
      && string.Equals(contentRoot.Attribute("BorderThickness")?.Value, "1", StringComparison.Ordinal)
      && string.Equals(
        contentRoot.Attribute("BorderBrush")?.Value,
        "{DynamicResource Brush.Border.Subtle}",
        StringComparison.Ordinal);
  }

  private static string RequiredAttribute(XElement element, string name) =>
    element.Attribute(name)?.Value
    ?? throw new InvalidOperationException($"Element '{element.Name.LocalName}' is missing required attribute '{name}'.");

  private static int CountGridDefinitions(XElement grid, string collectionName)
  {
    XElement? collection = grid.Elements().FirstOrDefault(element => element.Name.LocalName == collectionName);
    return collection is null ? 1 : Math.Max(1, collection.Elements().Count());
  }

  private static int ReadAttachedSpan(XElement element, string attributeName) =>
    int.TryParse(element.Attribute(attributeName)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int span)
      ? span
      : 1;

  private static string Required(JsonElement element, string property)
  {
    string value = element.GetProperty(property).GetString() ?? string.Empty;
    Assert.False(string.IsNullOrWhiteSpace(value), $"Required value '{property}' is empty.");
    return value;
  }

  private static string Normalize(string path) => path.Replace('\\', '/').Trim();

  private static string[] ReadInventoryNames(string path)
  {
    bool insideTable = false;
    List<string> names = [];
    foreach (string line in File.ReadLines(path))
    {
      if (line.StartsWith("| Window |", StringComparison.Ordinal))
      {
        insideTable = true;
        continue;
      }
      if (!insideTable)
      {
        continue;
      }
      if (!line.StartsWith('|'))
      {
        break;
      }
      if (line.StartsWith("| ---", StringComparison.Ordinal))
      {
        continue;
      }
      names.Add(line.Trim('|').Split('|')[0].Trim());
    }
    return names.ToArray();
  }

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
