using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
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
public sealed class AutomatedAccessibilityVerificationTests
{
  private static readonly XNamespace PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
  private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void PrimaryWindows_DeclareMinimumBoundsAndResizableConstraints()
  {
    string appRoot = Path.Combine(FindRepoRoot(), "src", "DictateAnywhere.App");
    string[] windowFiles =
    [
      Path.Combine(appRoot, "Workbench", "TextboxWorkbenchWindow.xaml"),
      Path.Combine(appRoot, "Settings", "SettingsWindow.xaml"),
      Path.Combine(appRoot, "History", "HistoryWindow.xaml"),
      Path.Combine(appRoot, "Workbench", "Reading", "ReaderWindow.xaml"),
      Path.Combine(appRoot, "Workbench", "Publishing", "YouTubePublishingWindow.xaml"),
      Path.Combine(appRoot, "FirstRun", "FirstRunWizardWindow.xaml"),
      Path.Combine(appRoot, "Workbench", "AboutWindow.xaml"),
      Path.Combine(appRoot, "Workbench", "Reading", "ReadingStudioHelpWindow.xaml"),
      Path.Combine(appRoot, "Hotkeys", "HotkeySettingsWindow.xaml"),
      Path.Combine(appRoot, "Hotkeys", "HotkeyTestWindow.xaml"),
    ];

    foreach (string file in windowFiles)
    {
      XDocument doc = XDocument.Load(file);
      XElement root = doc.Root ?? throw new InvalidOperationException($"Empty document: {file}");

      XAttribute? minWidth = root.Attribute("MinWidth");
      XAttribute? minHeight = root.Attribute("MinHeight");

      Assert.True(minWidth is not null, $"Window in '{Path.GetFileName(file)}' must declare MinWidth");
      Assert.True(minHeight is not null, $"Window in '{Path.GetFileName(file)}' must declare MinHeight");

      double width = double.Parse(minWidth.Value, System.Globalization.CultureInfo.InvariantCulture);
      double height = double.Parse(minHeight.Value, System.Globalization.CultureInfo.InvariantCulture);

      Assert.True(width >= 450, $"Window in '{Path.GetFileName(file)}' MinWidth must be at least 450, got {width}");
      Assert.True(height >= 250, $"Window in '{Path.GetFileName(file)}' MinHeight must be at least 250, got {height}");
    }

    // Also verify live instantiated dialog windows
    RunOnSta(() =>
    {
      YouTubePublishingWindow publishing = new("Test", 1);
      Assert.True(publishing.MinWidth >= 700);
      Assert.True(publishing.MinHeight >= 500);
      Assert.Equal(ResizeMode.CanResize, publishing.ResizeMode);
      publishing.Close();

      AboutWindow about = new();
      Assert.True(about.MinWidth >= 700);
      Assert.True(about.MinHeight >= 500);
      Assert.Equal(ResizeMode.CanResize, about.ResizeMode);
      about.Close();

      ReadingStudioHelpWindow help = new();
      Assert.True(help.MinWidth >= 700);
      Assert.True(help.MinHeight >= 500);
      Assert.Equal(ResizeMode.CanResize, help.ResizeMode);
      help.Close();

      HotkeyTestWindow testWindow = new();
      Assert.True(testWindow.MinWidth >= 450);
      Assert.True(testWindow.MinHeight >= 300);
      testWindow.Close();
    });
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void PrimaryViews_InstantiatedControls_HaveAccessibleNames()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView composer = new();
      AssertControlHasName(composer.FindName("Prompt"), "Composer prompt");
      AssertControlHasName(composer.FindName("NewChatButton"), "New chat");
      AssertControlHasName(composer.FindName("AddFile"), "Add file");
      AssertControlHasName(composer.FindName("ReadDocument"), "Open Reading Studio");
      AssertControlHasName(composer.FindName("ExportChatButton"), "Export this chat");
      AssertControlHasName(composer.FindName("StopSpeaking"), "Stop reading aloud");
      AssertControlHasName(composer.FindName("ExpandPrompt"), "Expand composer");
      AssertControlHasName(composer.FindName("Record"), "Dictate");
      AssertControlHasName(composer.FindName("StopDictation"), "Stop dictation");
      AssertControlHasName(composer.FindName("Send"), "Submit");
      AssertControlHasName(composer.FindName("StopChat"), "Stop response");

      WorkbenchSidebarView sidebar = new();
      AssertControlHasName(sidebar.FindName("HistorySearchTextBox"), "Search history");
      AssertControlHasName(sidebar.FindName("ChatHistoryListBox"), "Saved chats");
      AssertControlHasName(sidebar.FindName("HistoryListBox"), "Dictation history days");
      AssertControlHasName(sidebar.FindName("SettingsButton"), "Settings");

      WorkbenchHeaderView header = new();
      AssertControlHasName(header.FindName("ChatTitle"), "Chat title");
      AssertControlHasName(header.FindName("NewSession"), "New dictation session");
      AssertControlHasName(header.FindName("SaveHistoryEdits"), "Save dictation history edits");
      AssertControlHasName(header.FindName("DeleteHistorySession"), "Delete selected dictation");

      WorkbenchQuickSettingsView quickSettings = new();
      AssertControlHasName(quickSettings.FindName("AdvancedButton"), "All settings");
      AssertControlHasName(quickSettings.FindName("ThemeButton"), "Toggle theme");
      AssertControlHasName(quickSettings.FindName("TextSizeSlider"), "Chat text size");
      AssertControlHasName(quickSettings.FindName("ChatModel"), "Chat model");
      AssertControlHasName(quickSettings.FindName("DownloadChatModel"), "Download chat model");
      AssertControlHasName(quickSettings.FindName("TranscriptionModel"), "Transcription model");
      AssertControlHasName(quickSettings.FindName("AboutButton"), "About Koncus Nai");

      WorkbenchExpandedPromptView expandedPrompt = new();
      AssertControlHasName(expandedPrompt.FindName("Prompt"), "Expanded prompt");
      AssertControlHasName(expandedPrompt.FindName("Collapse"), "Collapse composer");
      AssertControlHasName(expandedPrompt.FindName("Submit"), "Submit");

      WorkbenchChatTranscriptView transcriptView = new();
      AssertControlHasName(transcriptView.FindName("Transcript"), "Chat transcript");

      WorkbenchOperationalStatusView operationalStatus = new();
      AssertControlHasName(operationalStatus.FindName("ModelProgress"), "Model status progress");

      WorkbenchInlineSettingsView inlineSettings = new();
      AssertControlHasName(inlineSettings.FindName("InlineLoadingProgress"), "Loading settings progress");

      YouTubePublishingWindow publishing = new("Audiobook", 2);
      AssertControlHasName(publishing.FindName("ClientIdTextBox"), "OAuth client ID");
      AssertControlHasName(publishing.FindName("ClientSecretPasswordBox"), "OAuth client secret");
      AssertControlHasName(publishing.FindName("SeriesTitleTextBox"), "Series title");
      AssertControlHasName(publishing.FindName("DescriptionTextBox"), "Series description");
      AssertControlHasName(publishing.FindName("TagsTextBox"), "Tags");
      AssertControlHasName(publishing.FindName("VideoFormatComboBox"), "Video format");
      AssertControlHasName(publishing.FindName("CaptionStyleComboBox"), "Caption treatment");
      AssertControlHasName(publishing.FindName("PrivacyComboBox"), "Visibility");
      AssertControlHasName(publishing.FindName("PublishDatePicker"), "First publication date");
      AssertControlHasName(publishing.FindName("PublishTimeTextBox"), "First publication local time");
      AssertControlHasName(publishing.FindName("PublishingIntervalTextBox"), "Hours between episodes");
      AssertControlHasName(publishing.FindName("ClosePublishingButton"), "Close");
      publishing.Close();

      HotkeyCaptureControl capture = new();
      AssertControlHasName(capture.FindName("CaptureButton"), "Change hotkey");
      AssertControlHasName(capture.FindName("HotkeyTextBox"), "Assigned hotkey");

      HotkeyTestWindow testWindow = new();
      AssertControlHasName(testWindow.FindName("EventsListBox"), "Captured hotkey events");
      testWindow.Close();

      ReaderSidebarView readerSidebar = new();
      AssertControlHasName(readerSidebar.FindName("StartSectionComboBox"), "Start section");
      AssertControlHasName(readerSidebar.FindName("EndSectionComboBox"), "End section");
      AssertControlHasName(readerSidebar.FindName("LanguageComboBox"), "Reading language");
      AssertControlHasName(readerSidebar.FindName("VoiceComboBox"), "Narration voice");
      AssertControlHasName(readerSidebar.FindName("VoicePreviewProgressBar"), "Voice preview loading progress");
      AssertControlHasName(readerSidebar.FindName("PrepareRangeButton"), "Prepare section range");
      AssertControlHasName(readerSidebar.FindName("FollowAlongComboBox"), "Reading focus");
      AssertControlHasName(readerSidebar.FindName("HighlightStyleComboBox"), "Highlight style");
      AssertControlHasName(readerSidebar.FindName("HighlightColorListBox"), "Focus color");
      AssertControlHasName(readerSidebar.FindName("FontComboBox"), "Typeface");
      AssertControlHasName(readerSidebar.FindName("ThemeListBox"), "Reading theme");
      AssertControlHasName(readerSidebar.FindName("FontSizeSlider"), "Text size");
      AssertControlHasName(readerSidebar.FindName("SpeedSlider"), "Playback speed");
      AssertControlHasName(readerSidebar.FindName("VideoFormatComboBox"), "Video canvas");
      readerSidebar.Dispose();

      ReaderDocumentView doc = new();
      AssertControlHasName(doc.FindName("DraftTextBox"), "Reading draft");
      AssertControlHasName(doc.FindName("PreparationOverlay"), "Reading operation progress");
      AssertControlHasName(doc.FindName("PreparationProgressBar"), "Preparation progress");

      ReaderTransportView transport = new();
      AssertControlHasName(transport.FindName("SeekSlider"), "Reading position");
      AssertControlHasName(transport.FindName("PreviousButton"), "Previous section");
      AssertControlHasName(transport.FindName("PlayPauseButton"), "Play");
      AssertControlHasName(transport.FindName("NextButton"), "Next section");
      AssertControlHasName(transport.FindName("EditDocumentButton"), "Edit text");
      AssertControlHasName(transport.FindName("DistractionFreeButton"), "Fullscreen reading mode");
      AssertControlHasName(transport.FindName("DocumentProgressBar"), "Document progress");

      WindowCaptionButtons caption = new();
      AssertControlHasName(caption.FindName("MinimizeButton"), "Minimize");
      AssertControlHasName(caption.FindName("MaximizeRestoreButton"), "Maximize");
      AssertControlHasName(caption.FindName("CloseButton"), "Close");
    });
  }

  [Fact]
  public void XamlFiles_ExplicitNamedControls_HaveAccessibleNames()
  {
    string appRoot = Path.Combine(FindRepoRoot(), "src", "DictateAnywhere.App");

    // Check SettingsPanel.xaml
    XDocument settingsDoc = XDocument.Load(Path.Combine(appRoot, "Settings", "SettingsPanel.xaml"));
    AssertElementHasAccessibleName(settingsDoc, "AudioDeviceComboBox", "Microphone");
    AssertElementHasAccessibleName(settingsDoc, "TranscriptionProviderComboBox", "Transcription provider");
    AssertElementHasAccessibleName(settingsDoc, "ModelComboBox", "Transcription model");
    AssertElementHasAccessibleName(settingsDoc, "TranscriptionLanguageComboBox", "Transcription language");
    AssertElementHasAccessibleName(settingsDoc, "ChatTypefaceComboBox", "Chat typeface");
    AssertElementHasAccessibleName(settingsDoc, "ModelProgressBar", "Model operation progress");

    // Check HistoryWindow.xaml
    XDocument historyDoc = XDocument.Load(Path.Combine(appRoot, "History", "HistoryWindow.xaml"));
    AssertElementHasAccessibleName(historyDoc, "HistorySearchTextBox", "Search dictation history");
    AssertElementHasAccessibleName(historyDoc, "HistoryListBox", "Dictation history entries");
    AssertElementHasAccessibleName(historyDoc, "SaveEditsButton", "Save dictation changes");
    AssertElementHasAccessibleName(historyDoc, "DeleteButton", "Delete selected dictation");
    AssertElementHasAccessibleName(historyDoc, "TranscriptTextBox", "Selected dictation text");

    // Check FirstRunWizardWindow.xaml
    XDocument wizardDoc = XDocument.Load(Path.Combine(appRoot, "FirstRun", "FirstRunWizardWindow.xaml"));
    AssertElementHasAccessibleName(wizardDoc, "ModelComboBox", "Select model");
    AssertElementHasAccessibleName(wizardDoc, "DownloadProgressBar", "Model download progress");
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void DynamicErrorAndStatusIndicators_DeclareLiveRegions()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView composer = new();
      AssertLiveSetting(composer.FindName("HotkeyStatusText"), AutomationLiveSetting.Polite);

      WorkbenchHeaderView header = new();
      AssertLiveSetting(header.FindName("ChatStatusText"), AutomationLiveSetting.Polite);
      AssertLiveSetting(header.FindName("SelectionMetadata"), AutomationLiveSetting.Polite);

      WorkbenchOperationalStatusView operationalStatus = new();
      AssertLiveSetting(operationalStatus.FindName("ModelReadiness"), AutomationLiveSetting.Polite);
      AssertLiveSetting(operationalStatus.FindName("SessionStatus"), AutomationLiveSetting.Polite);
      AssertLiveSetting(operationalStatus.FindName("ModelDetail"), AutomationLiveSetting.Polite);

      WorkbenchSidebarView sidebar = new();
      AssertLiveSetting(sidebar.FindName("ChatHistoryStatusTextBlock"), AutomationLiveSetting.Polite);
      AssertLiveSetting(sidebar.FindName("HistorySidebarStatusTextBlock"), AutomationLiveSetting.Polite);

      WorkbenchQuickSettingsView quickSettings = new();
      AssertLiveSetting(quickSettings.FindName("TranscriptionStatus"), AutomationLiveSetting.Polite);

      YouTubePublishingWindow publishing = new("Title", 1);
      AssertLiveSetting(publishing.FindName("ValidationTextBlock"), AutomationLiveSetting.Polite);
      AssertLiveSetting(publishing.FindName("ConnectionStatusTextBlock"), AutomationLiveSetting.Polite);
      AssertLiveSetting(publishing.FindName("EpisodeSummaryTextBlock"), AutomationLiveSetting.Polite);
      publishing.Close();

      HotkeyCaptureControl capture = new();
      AssertLiveSetting(capture.FindName("StatusTextBlock"), AutomationLiveSetting.Polite);

      HotkeyTestWindow hotkeyTest = new();
      AssertLiveSetting(hotkeyTest.FindName("EventsListBox"), AutomationLiveSetting.Polite);
      hotkeyTest.Close();

      ReaderTransportView transport = new();
      AssertLiveSetting(transport.FindName("PlaybackStatusTextBlock"), AutomationLiveSetting.Polite);

      ReaderSidebarView readerSidebar = new();
      AssertLiveSetting(readerSidebar.FindName("VoicePreviewStatusTextBlock"), AutomationLiveSetting.Polite);
      readerSidebar.Dispose();

      ReaderDocumentView doc = new();
      AssertLiveSetting(doc.FindName("PreparationMessageTextBlock"), AutomationLiveSetting.Polite);
      AssertLiveSetting(doc.FindName("PreparationProgressTextBlock"), AutomationLiveSetting.Polite);

      ReaderCompletionToastWindow toast = new("Summary text");
      AssertLiveSetting(toast.FindName("DetailTextBlock"), AutomationLiveSetting.Polite);
      toast.Close();
    });

    // Check XAML markup for DI-managed windows
    string appRoot = Path.Combine(FindRepoRoot(), "src", "DictateAnywhere.App");
    XDocument settingsDoc = XDocument.Load(Path.Combine(appRoot, "Settings", "SettingsPanel.xaml"));
    AssertElementHasLiveSetting(settingsDoc, "ModelStatusTextBlock", "Polite");
    AssertElementHasLiveSetting(settingsDoc, "ModelActionStatusTextBlock", "Polite");
    AssertElementHasLiveSetting(settingsDoc, "ModelBenchmarkSummaryTextBlock", "Polite");
    AssertElementHasLiveSetting(settingsDoc, "PersistStatusTextBlock", "Polite");

    XDocument historyDoc = XDocument.Load(Path.Combine(appRoot, "History", "HistoryWindow.xaml"));
    AssertElementHasLiveSetting(historyDoc, "HistoryStatusTextBlock", "Polite");
    AssertElementHasLiveSetting(historyDoc, "ActionStatusTextBlock", "Polite");

    XDocument wizardDoc = XDocument.Load(Path.Combine(appRoot, "FirstRun", "FirstRunWizardWindow.xaml"));
    AssertElementHasLiveSetting(wizardDoc, "BenchmarkStatusTextBlock", "Polite");
    AssertElementHasLiveSetting(wizardDoc, "BenchmarkRecommendationTextBlock", "Polite");
    AssertElementHasLiveSetting(wizardDoc, "ModelActionStatusTextBlock", "Polite");

    XDocument hotkeySettingsDoc = XDocument.Load(Path.Combine(appRoot, "Hotkeys", "HotkeySettingsWindow.xaml"));
    AssertElementHasLiveSetting(hotkeySettingsDoc, "PersistStatusTextBlock", "Polite");
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void DynamicStatusContainers_HaveTextWrappingToPreventClipping()
  {
    RunOnSta(() =>
    {
      YouTubePublishingWindow publishing = new("Title", 1);
      TextBlock validation = (TextBlock)publishing.FindName("ValidationTextBlock");
      Assert.Equal(TextWrapping.Wrap, validation.TextWrapping);
      TextBlock connection = (TextBlock)publishing.FindName("ConnectionStatusTextBlock");
      Assert.Equal(TextWrapping.Wrap, connection.TextWrapping);
      publishing.Close();

      WorkbenchOperationalStatusView operationalStatus = new();
      TextBlock modelDetail = (TextBlock)operationalStatus.FindName("ModelDetail");
      Assert.Equal(TextWrapping.Wrap, modelDetail.TextWrapping);

      HotkeyCaptureControl capture = new();
      TextBlock captureStatus = (TextBlock)capture.FindName("StatusTextBlock");
      Assert.Equal(TextWrapping.Wrap, captureStatus.TextWrapping);

      ReaderCompletionToastWindow toast = new("Detail");
      TextBlock toastDetail = (TextBlock)toast.FindName("DetailTextBlock");
      Assert.Equal(TextWrapping.Wrap, toastDetail.TextWrapping);
      toast.Close();
    });

    // Check XAML markup for DI-managed elements
    string appRoot = Path.Combine(FindRepoRoot(), "src", "DictateAnywhere.App");
    XDocument settingsDoc = XDocument.Load(Path.Combine(appRoot, "Settings", "SettingsPanel.xaml"));
    AssertElementHasTextWrapping(settingsDoc, "ModelActionStatusTextBlock", "Wrap");
    AssertElementHasTextWrapping(settingsDoc, "PersistStatusTextBlock", "Wrap");

    XDocument wizardDoc = XDocument.Load(Path.Combine(appRoot, "FirstRun", "FirstRunWizardWindow.xaml"));
    AssertElementHasTextWrapping(wizardDoc, "ModelActionStatusTextBlock", "Wrap");
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void TabNavigation_InteractiveControls_AreFocusableAndTabStops()
  {
    RunOnSta(() =>
    {
      YouTubePublishingWindow publishing = new("Title", 1);
      TextBox clientId = (TextBox)publishing.FindName("ClientIdTextBox");
      PasswordBox clientSecret = (PasswordBox)publishing.FindName("ClientSecretPasswordBox");
      TextBox seriesTitle = (TextBox)publishing.FindName("SeriesTitleTextBox");
      Button connectButton = (Button)publishing.FindName("ConnectButton");

      Assert.True(clientId.Focusable, "ClientIdTextBox must be focusable");
      Assert.True(clientId.IsTabStop, "ClientIdTextBox must be a tab stop");
      Assert.True(clientSecret.Focusable, "ClientSecretPasswordBox must be focusable");
      Assert.True(clientSecret.IsTabStop, "ClientSecretPasswordBox must be a tab stop");
      Assert.True(seriesTitle.Focusable, "SeriesTitleTextBox must be focusable");
      Assert.True(seriesTitle.IsTabStop, "SeriesTitleTextBox must be a tab stop");
      Assert.True(connectButton.Focusable, "ConnectButton must be focusable");
      Assert.True(connectButton.IsTabStop, "ConnectButton must be a tab stop");
      publishing.Close();
    });
  }

  [Fact]
  public void HighContrast_SystemPalette_CoversAllThemeResourceKeys()
  {
    var highContrastPalette = AppThemeManager.GetHighContrastPalette();
    Assert.NotEmpty(highContrastPalette);

    Type managerType = typeof(AppThemeManager);
    FieldInfo darkField = managerType.GetField("DarkPalette", BindingFlags.NonPublic | BindingFlags.Static)!;
    FieldInfo lightField = managerType.GetField("LightPalette", BindingFlags.NonPublic | BindingFlags.Static)!;

    IReadOnlyDictionary<string, System.Windows.Media.Color> dark = (IReadOnlyDictionary<string, System.Windows.Media.Color>)darkField.GetValue(null)!;
    IReadOnlyDictionary<string, System.Windows.Media.Color> light = (IReadOnlyDictionary<string, System.Windows.Media.Color>)lightField.GetValue(null)!;

    foreach (string key in dark.Keys)
    {
      Assert.True(highContrastPalette.ContainsKey(key), $"High contrast palette must contain key '{key}' from DarkPalette");
    }

    foreach (string key in light.Keys)
    {
      Assert.True(highContrastPalette.ContainsKey(key), $"High contrast palette must contain key '{key}' from LightPalette");
    }
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ReducedMotion_ClientAreaAnimationParameter_IsRecognized()
  {
    RunOnSta(() =>
    {
      // Verify that SystemParameters.ClientAreaAnimation is evaluated without throwing
      bool animationAllowed = SystemParameters.ClientAreaAnimation;
      _ = animationAllowed;

      // Instantiate progress view and toast to verify no exceptions occur in animation initialization
      WorkbenchChatProgressView progressView = new();
      Assert.NotNull(progressView);

      ReaderCompletionToastWindow toast = new("Verification detail");
      Assert.NotNull(toast);
      toast.Close();
    });
  }

  private static void AssertControlHasName(object? control, string expectedName)
  {
    Assert.NotNull(control);
    DependencyObject dep = Assert.IsAssignableFrom<DependencyObject>(control);
    string actualName = AutomationProperties.GetName(dep);
    if (string.IsNullOrWhiteSpace(actualName) && control is ContentControl { Content: string textContent })
    {
      actualName = textContent;
    }
    else if (string.IsNullOrWhiteSpace(actualName) && control is HeaderedContentControl { Header: string headerContent })
    {
      actualName = headerContent;
    }

    Assert.False(string.IsNullOrWhiteSpace(actualName), $"Control '{dep.GetType().Name}' has no accessible name; expected '{expectedName}'");
    Assert.Equal(expectedName, actualName);
  }

  private static void AssertLiveSetting(object? control, AutomationLiveSetting expectedSetting)
  {
    Assert.NotNull(control);
    DependencyObject dep = Assert.IsAssignableFrom<DependencyObject>(control);
    AutomationLiveSetting setting = AutomationProperties.GetLiveSetting(dep);
    Assert.Equal(expectedSetting, setting);
  }

  private static void AssertElementHasAccessibleName(XDocument doc, string elementName, string expectedName)
  {
    XElement? element = doc.Descendants().FirstOrDefault(e => e.Attribute(XNs + "Name")?.Value == elementName);
    Assert.NotNull(element);

    XAttribute? nameAttr = element.Attribute("AutomationProperties.Name");
    if (nameAttr is not null)
    {
      Assert.Equal(expectedName, nameAttr.Value);
      return;
    }

    XAttribute? contentAttr = element.Attribute("Content");
    if (contentAttr is not null)
    {
      Assert.Equal(expectedName, contentAttr.Value);
      return;
    }

    Assert.Fail($"Element '{elementName}' in XAML does not have an accessible name matching '{expectedName}'");
  }

  private static void AssertElementHasLiveSetting(XDocument doc, string elementName, string expectedSetting)
  {
    XElement? element = doc.Descendants().FirstOrDefault(e => e.Attribute(XNs + "Name")?.Value == elementName);
    Assert.NotNull(element);

    XAttribute? liveAttr = element.Attribute("AutomationProperties.LiveSetting");
    Assert.NotNull(liveAttr);
    Assert.Equal(expectedSetting, liveAttr.Value);
  }

  private static void AssertElementHasTextWrapping(XDocument doc, string elementName, string expectedWrapping)
  {
    XElement? element = doc.Descendants().FirstOrDefault(e => e.Attribute(XNs + "Name")?.Value == elementName);
    Assert.NotNull(element);

    XAttribute? wrapAttr = element.Attribute("TextWrapping");
    Assert.NotNull(wrapAttr);
    Assert.Equal(expectedWrapping, wrapAttr.Value);
  }



  // ── WP-25 Tests ─────────────────────────────────────────────────────────────

  /// <summary>
  /// Verifies that every named custom Button / ToggleButton / Slider style
  /// in the app's XAML files has at least one of the two accepted keyboard-
  /// focus-visual mechanisms:
  ///   (a) FocusVisualStyle set to a named resource (not {x:Null}), OR
  ///   (b) a Trigger on IsKeyboardFocused inside its ControlTemplate.
  /// Both are correct WPF patterns. The test rejects styles that null the
  /// focus visual AND provide no template-level alternative.
  /// </summary>
  [Fact]
  public void KeyboardFocusVisual_AllCustomButtonStyles_HaveFocusRingDeclared()
  {
    string appRoot = Path.Combine(FindRepoRoot(), "src", "DictateAnywhere.App");

    // Styles whose FocusVisualStyle arrives via BasedOn inheritance.
    // Each entry is justified: the named ancestor sets AppKeyboardFocusVisualStyle.
    //
    // ControlStyles.xaml inheritance chains:
    //   AppActionButtonStyle (base)       → sets AppKeyboardFocusVisualStyle directly
    //   AppPrimaryActionButtonStyle       → BasedOn AppActionButtonStyle, own Template, no reset
    //   AppDangerActionButtonStyle        → BasedOn AppActionButtonStyle, own Template, no reset
    //   AppToolbarIconButtonStyle         → BasedOn AppActionButtonStyle, no Template override
    //   AppDangerIconButtonStyle          → BasedOn AppToolbarIconButtonStyle
    //
    // TextboxWorkbenchWindow.xaml:
    //   WorkbenchToolbarButtonStyle       → BasedOn AppToolbarIconButtonStyle
    //   WorkbenchDangerToolbarButtonStyle → BasedOn AppDangerIconButtonStyle
    //   ComposerIconButtonStyle           → BasedOn AppToolbarIconButtonStyle, own Template, no reset
    //   ComposerSubmitButtonStyle         → BasedOn AppToolbarIconButtonStyle, own Template, no reset
    //   ComposerStopButtonStyle           → BasedOn AppActionButtonStyle
    //
    // YouTubePublishingWindow.xaml:
    //   PrimaryButton                     → BasedOn PublishingButtonBase
    //   SecondaryButton                   → BasedOn PublishingButtonBase
    //
    // ReaderWindow.xaml:
    //   ReaderPrimaryButton               → BasedOn ReaderSecondaryButton
    //   ReaderIconButton                  → BasedOn ReaderSecondaryButton
    HashSet<string> basedOnInheritedExempt = new(StringComparer.OrdinalIgnoreCase)
    {
      "AppPrimaryActionButtonStyle", "AppDangerActionButtonStyle",
      "AppToolbarIconButtonStyle", "AppDangerIconButtonStyle",
      // TextboxWorkbenchWindow.xaml — simple margin wrappers around AppActionButtonStyle:
      "WorkbenchButtonStyle", "WorkbenchPrimaryButtonStyle",
      "WorkbenchToolbarButtonStyle", "WorkbenchDangerToolbarButtonStyle",
      "ComposerIconButtonStyle", "ComposerSubmitButtonStyle", "ComposerStopButtonStyle",
      "PrimaryButton", "SecondaryButton",
      "ReaderPrimaryButton", "ReaderIconButton",
    };

    (string FilePath, string[] ExplicitExemptKeys)[] files =
    [
      (Path.Combine(appRoot, "Theming", "ControlStyles.xaml"),
       // AppChromeWindowButtonStyle / AppChromeCloseButtonStyle: x:Null + inline IsKeyboardFocused trigger ✓
       // AppScrollBarRepeatButtonStyle: non-interactive (Focusable=False, IsTabStop=False)
       new[] { "AppChromeWindowButtonStyle", "AppChromeCloseButtonStyle",
               "AppScrollBarRepeatButtonStyle" }),
      (Path.Combine(appRoot, "Workbench", "WorkbenchSidebarView.xaml"), Array.Empty<string>()),
      (Path.Combine(appRoot, "Workbench", "WorkbenchQuickSettingsView.xaml"), Array.Empty<string>()),
      (Path.Combine(appRoot, "Workbench", "TextboxWorkbenchWindow.xaml"), Array.Empty<string>()),
      (Path.Combine(appRoot, "Workbench", "Publishing", "YouTubePublishingWindow.xaml"), Array.Empty<string>()),
      (Path.Combine(appRoot, "Workbench", "Reading", "ReaderWindow.xaml"),
       // Use x:Null + inline IsKeyboardFocused triggers (verified by source inspection):
       new[] { "ReaderBareIconButton", "ReaderSwatchItem", "ReaderProcessingToggle",
               "ReaderDraftInput" }),
    ];

    XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace xKey = "http://schemas.microsoft.com/winfx/2006/xaml";
    List<string> violations = [];

    foreach ((string filePath, string[] explicitExemptKeys) in files)
    {
      XDocument doc = XDocument.Load(filePath);
      string fileName = Path.GetFileName(filePath);

      foreach (XElement style in doc.Descendants(ns + "Style"))
      {
        string? key = style.Attribute(xKey + "Key")?.Value;
        if (key is null) continue;

        string? targetType = style.Attribute("TargetType")?.Value;
        if (targetType is null) continue;

        bool isInteractive =
          targetType.Contains("Button", StringComparison.OrdinalIgnoreCase) ||
          targetType.Contains("ToggleButton", StringComparison.OrdinalIgnoreCase) ||
          targetType.Contains("Slider", StringComparison.OrdinalIgnoreCase);

        if (!isInteractive) continue;

        // Skip styles that are exempt by explicit name (inline-trigger pattern or non-interactive)
        if (explicitExemptKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;

        // Skip styles that inherit FocusVisualStyle from a named BasedOn ancestor (verified by audit)
        if (basedOnInheritedExempt.Contains(key)) continue;

        // Mechanism (a): FocusVisualStyle present and not {x:Null}
        XElement? fvsSetter = style.Elements(ns + "Setter")
          .FirstOrDefault(s => s.Attribute("Property")?.Value == "FocusVisualStyle");
        bool hasNamedFvs = fvsSetter is not null &&
          !string.Equals(fvsSetter.Attribute("Value")?.Value, "{x:Null}", StringComparison.Ordinal);

        if (hasNamedFvs) continue;

        // Mechanism (b): ControlTemplate has an IsKeyboardFocused trigger
        bool hasInlineFocusTrigger = style
          .Descendants(ns + "Trigger")
          .Any(t => t.Attribute("Property")?.Value == "IsKeyboardFocused");

        if (!hasInlineFocusTrigger)
        {
          violations.Add($"{fileName}: style '{key}' (TargetType={targetType}) has no focus-ring mechanism");
        }
      }
    }

    Assert.Empty(violations);
  }

  /// <summary>
  /// Extends keyboard tab-stop coverage to History window action buttons and
  /// Reader transport view playback controls. Complements
  /// <see cref="TabNavigation_InteractiveControls_AreFocusableAndTabStops"/>.
  /// </summary>
  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void TabNavigation_AllPrimaryWindowInteractiveControls_AreFocusableAndTabStops()
  {
    // History window buttons: DI-managed; verify via XAML that they are not
    // explicitly excluded from tab navigation.
    string appRoot = Path.Combine(FindRepoRoot(), "src", "DictateAnywhere.App");
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    XDocument historyDoc = XDocument.Load(Path.Combine(appRoot, "History", "HistoryWindow.xaml"));

    foreach (string buttonName in new[] { "SaveEditsButton", "DeleteButton" })
    {
      XElement? btn = historyDoc.Descendants()
        .FirstOrDefault(e => e.Attribute(x + "Name")?.Value == buttonName);
      Assert.True(btn is not null, $"HistoryWindow must contain Button x:Name=\"{buttonName}\"");
      string? isTabStop = btn.Attribute("IsTabStop")?.Value;
      string? focusable = btn.Attribute("Focusable")?.Value;
      Assert.False(string.Equals(isTabStop, "False", StringComparison.OrdinalIgnoreCase),
        $"HistoryWindow.{buttonName} must not set IsTabStop=False");
      Assert.False(string.Equals(focusable, "False", StringComparison.OrdinalIgnoreCase),
        $"HistoryWindow.{buttonName} must not set Focusable=False");
    }

    // Reader transport view: instantiate and verify focusability of playback controls.
    RunOnSta(() =>
    {
      ReaderTransportView transport = new();
      foreach (string name in new[]
               { "PreviousButton", "PlayPauseButton", "NextButton",
                 "EditDocumentButton", "DistractionFreeButton" })
      {
        object? ctrl = transport.FindName(name);
        Assert.True(ctrl is not null, $"ReaderTransportView must expose control '{name}'");
        Button btn = Assert.IsType<Button>(ctrl);
        Assert.True(btn.Focusable, $"ReaderTransportView.{name} must be Focusable");
        Assert.True(btn.IsTabStop, $"ReaderTransportView.{name} must be a tab stop");
      }
    });
  }

  /// <summary>
  /// Verifies that RTL text handling and reduced-motion code paths exist in
  /// the application source. Static source-presence checks confirm features
  /// were not accidentally deleted.
  /// </summary>
  [Fact]
  public void RTLAndReducedMotion_CodePaths_ExistInSource()
  {
    string appRoot = Path.Combine(FindRepoRoot(), "src", "DictateAnywhere.App");

    // RTL: ReaderDocumentView.xaml.cs must reference FlowDirection.RightToLeft
    string readerDocCode = File.ReadAllText(
      Path.Combine(appRoot, "Workbench", "Reading", "ReaderDocumentView.xaml.cs"));
    Assert.Contains("FlowDirection.RightToLeft", readerDocCode, StringComparison.Ordinal);

    // Reduced motion: at least one source file must reference SystemParameters.ClientAreaAnimation
    bool foundReducedMotion = Directory
      .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
      .Any(f => File.ReadAllText(f).Contains("ClientAreaAnimation", StringComparison.Ordinal));

    Assert.True(foundReducedMotion,
      "At least one .cs file in DictateAnywhere.App must reference " +
      "SystemParameters.ClientAreaAnimation to support reduced-motion behavior.");
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

    throw new InvalidOperationException("Could not locate repo root containing DictateAnywhere.sln.");
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void SemanticTypography_DynamicResourcesUpdateExistingChromeWithoutChangingLocalContentTypography()
  {
    RunOnSta(() =>
    {
      ResourceDictionary resources = new();
      AppTextScaleManager.Apply(resources, 12d);
      Grid surface = new() { Resources = resources };
      TextBlock chromeLabel = new();
      TextBlock userContent = new() { FontSize = 23d };
      chromeLabel.SetResourceReference(TextBlock.FontSizeProperty, "Font.Size.Section");
      surface.Children.Add(chromeLabel);
      surface.Children.Add(userContent);
      surface.Measure(new Size(500d, 300d));
      surface.Arrange(new Rect(0d, 0d, 500d, 300d));

      AppTextScaleManager.Apply(resources, 24d);
      surface.Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));

      Assert.Equal(32d, chromeLabel.FontSize);
      Assert.Equal(23d, userContent.FontSize);
    });
  }

  [Fact]
  public void ProseTypography_UsesSemanticResourcesOrAnExplicitContentOrGlyphException()
  {
    string appRoot = Path.Combine(FindRepoRoot(), "src", "DictateAnywhere.App");
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    string[] allowedContentStyles = ["ComposerTextBoxStyle", "ChatTranscriptStyle", "ReaderDraftInput"];
    string[] allowedContentNames = ["Prompt", "PromptPlaceholder", "ReaderTextBlock", "FocusedReadingTextBlock"];
    List<string> violations = [];
    int discovered = 0;

    foreach (string path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories))
    {
      XDocument document = XDocument.Load(path, LoadOptions.SetLineInfo);
      foreach (XElement element in document.Descendants())
      {
        XAttribute? literal = element.Attribute("FontSize");
        bool literalSetter = element.Name.LocalName == "Setter"
          && string.Equals((string?)element.Attribute("Property"), "FontSize", StringComparison.Ordinal)
          && double.TryParse((string?)element.Attribute("Value"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
        if (!(literal is not null && double.TryParse(literal.Value,
              System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            && !literalSetter)
        {
          continue;
        }

        discovered++;
        XElement? style = element.AncestorsAndSelf().FirstOrDefault(candidate => candidate.Name.LocalName == "Style");
        string? styleKey = (string?)style?.Attribute(x + "Key");
        string? name = (string?)element.Attribute(x + "Name");
        bool isGlyph = ((string?)element.Attribute("FontFamily"))?.Contains("MDL2", StringComparison.Ordinal) == true
          || ((string?)element.Attribute("Style"))?.Contains("AppIconGlyphTextBlockStyle", StringComparison.Ordinal) == true
          || styleKey is "AppIconGlyphTextBlockStyle" or "AppChromeGlyphTextBlockStyle"
          || string.Equals((string?)element.Attribute("Text"), "?", StringComparison.Ordinal);
        bool isUserContent = allowedContentStyles.Contains(styleKey, StringComparer.Ordinal)
          || allowedContentNames.Contains(name, StringComparer.Ordinal)
          || path.EndsWith("WorkbenchExpandedPromptView.xaml", StringComparison.OrdinalIgnoreCase);
        if (!isGlyph && !isUserContent)
        {
          System.Xml.IXmlLineInfo line = (System.Xml.IXmlLineInfo)element;
          violations.Add($"{Path.GetRelativePath(appRoot, path)}:{line.LineNumber} {element.Name.LocalName}");
        }
      }
    }

    Assert.True(discovered > 0, "The application XAML literal-font inventory was empty.");
    Assert.Empty(violations);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The helper must transfer any WPF-thread assertion or construction failure to the test thread.")]
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
    bool completed = thread.Join(TimeSpan.FromSeconds(15));
    Assert.True(completed, "STA test thread timed out.");
    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
  }
}
