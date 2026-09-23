using System;
using System.IO;

namespace DictateAnywhere.App.Tests;

public sealed class SettingsPanelMarkupTests
{
  [Xunit.Fact]
  public void SettingsPanel_UsesOnePageAndOmitsRemovedAdvancedControls()
  {
    string path = Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory,
      "..", "..", "..", "..", "..",
      "src", "DictateAnywhere.App", "Settings", "SettingsPanel.xaml"));
    string markup = File.ReadAllText(path);

    Xunit.Assert.DoesNotContain("<TabControl", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Profile and insertion", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Voice snippets", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Blocked apps", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("OnImportSettingsClicked", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("OnExportSettingsClicked", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Text=\"Microphone\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Text=\"GENERAL\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Text=\"APPEARANCE\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Text=\"OVERLAY\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Open Workbench", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Content=\"Save\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Content=\"Reset\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Local preferences for dictation", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Restore clipboard contents after insertion", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("x:Name=\"EnableTextCleanupCheckBox\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"EnableAutomaticPunctuationCheckBox\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Content=\"Automatic punctuation\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"AssistantFeaturesEnabledCheckBox\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Content=\"Enable chat and Reading Studio\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Content=\"Spoken formatting commands\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Gemma refinement", markup, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("x:Name=\"ChatTypefaceComboBox\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Text=\"Chat typeface\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Content=\"Manage Dictation History\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Saved locally with no automatic entry limit.", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"ActivateModelButton\" Style=\"{StaticResource AppActionButtonStyle}\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("x:Name=\"ActivateModelButton\" Style=\"{StaticResource AppPrimaryActionButtonStyle}\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("HistoryPassword", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Retention", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("EnableEncryptedHistory", markup, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void CompactSettings_UsesContinuousTextSizeAndCompactChatActions()
  {
    string workbenchRoot = Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory,
      "..", "..", "..", "..", "..",
      "src", "DictateAnywhere.App", "Workbench"));
    string markup = string.Join(
      Environment.NewLine,
      File.ReadAllText(Path.Combine(workbenchRoot, "TextboxWorkbenchWindow.xaml")),
      File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchSidebarView.xaml")),
      File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchQuickSettingsView.xaml")),
      File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchComposerView.xaml")),
      File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchExpandedPromptView.xaml")));

    Xunit.Assert.Contains("Text=\"Text Size\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"TextSizeSlider\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Maximum=\"30\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("x:Name=\"ChatTextSizeComboBox\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"ExportChatButton\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("ToolTip=\"Export this chat\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("VerticalContentAlignment=\"Center\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("WorkbenchChatProgressView", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Background=\"Transparent\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Foreground=\"{DynamicResource Brush.Text.Primary}\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"CompactComposer\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Text=\"Just Ask\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Ask Koncus Nai", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("WorkbenchExpandedPromptView", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"AddFile\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("ToolTip=\"Add file\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"PendingFiles\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("x:Name=\"ImportAudioButton\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("PreviewMouseLeftButtonDown=\"OnHistoryPreviewMouseLeftButtonDown\"", markup, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Margin=\"46,70,46,104\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"ZoomValue\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("OnZoomOut", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("OnZoomIn", markup, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void FirstRunWizard_OffersDictationOnlyAndFullAssistantModes()
  {
    string path = Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory,
      "..", "..", "..", "..", "..",
      "src", "DictateAnywhere.App", "FirstRun", "FirstRunWizardWindow.xaml"));
    string markup = File.ReadAllText(path);

    Xunit.Assert.Contains("x:Name=\"DictationOnlyRadioButton\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Name=\"FullAssistantRadioButton\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Content=\"Dictation only\"", markup, StringComparison.Ordinal);
    Xunit.Assert.Contains("Content=\"Full assistant\"", markup, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void SharedDangerAction_UsesSolidThemeAwareSurfaces()
  {
    string root = Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory,
      "..", "..", "..", "..", "..",
      "src", "DictateAnywhere.App"));
    string styles = File.ReadAllText(Path.Combine(root, "Theming", "ControlStyles.xaml"));
    string tokens = File.ReadAllText(Path.Combine(root, "Theming", "DesignTokens.xaml"));

    Xunit.Assert.Contains("x:Key=\"AppDangerActionButtonStyle\"", styles, StringComparison.Ordinal);
    Xunit.Assert.Contains("Brush.Control.DangerHover", styles, StringComparison.Ordinal);
    Xunit.Assert.Contains("Brush.Control.DangerPressed", styles, StringComparison.Ordinal);
    Xunit.Assert.Contains("x:Key=\"Brush.Control.Danger\"", tokens, StringComparison.Ordinal);
  }
}
