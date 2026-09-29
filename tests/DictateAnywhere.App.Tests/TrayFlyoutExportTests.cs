using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DictateAnywhere.App.Diagnostics;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Productivity;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Tray;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Diagnostics;
using Xunit;
using Forms = System.Windows.Forms;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class TrayFlyoutExportTests
{
  [Fact]
  public void QueuedTrayMouseUpAfterShutdownDoesNotConstructFlyout()
  {
    RunOnSta(() =>
    {
      using TrayIconHost host = new();
      host.BeginShutdown();
      host.OnNotifyIconMouseUp(null,
        new Forms.MouseEventArgs(Forms.MouseButtons.Right, 1, 0, 0, 0));
      Assert.False(host.HasOpenFlyout);
    });
  }

  [Fact]
  public void TrayFlyoutHighContrastRemovesShadowAndStrengthensBorder()
  {
    RunOnSta(() =>
    {
      TrayFlyoutWindow flyout = CreateFlyout();
      try
      {
        flyout.Show();
        WindowThemeBehavior.SetIsHighContrastActive(flyout, true);
        flyout.UpdateLayout();
        Border surface = Assert.IsType<Border>(flyout.Content);
        Assert.Equal(new CornerRadius(0), surface.CornerRadius);
        Assert.Equal(new Thickness(2), surface.BorderThickness);
        Assert.Null(surface.Effect);
      }
      finally { flyout.Dismiss(); }
    });
  }

  [Fact]
  public void RetryEmptyStateUsesThemedNoticeInsteadOfNativeMessageBox()
  {
    RunOnSta(() =>
    {
      RetryFeedbackWindow notice = new(
        ProductivityActionResult.Failed("No dictation history entry is available yet.", RetryOutcomeCode.NoHistory));
      notice.Left = -10000;
      notice.Top = -10000;
      try
      {
        Assert.True(WindowThemeBehavior.GetIsEnabled(notice));
        Assert.Equal(WindowStyle.None, notice.WindowStyle);
        Assert.Equal("Nothing to retry yet", notice.HeadingText.Text);
        Assert.Contains("Start a new dictation", notice.MessageText.Text, StringComparison.Ordinal);
        notice.Show();
        WindowThemeBehavior.SetIsHighContrastActive(notice, true);
        notice.UpdateLayout();
        Border surface = Assert.IsType<Border>(notice.Content);
        Assert.Equal(new CornerRadius(0), surface.CornerRadius);
        Assert.Equal(new Thickness(2), surface.BorderThickness);
        notice.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(notice.IsVisible);
      }
      finally { if (notice.IsVisible) notice.Close(); }
    });
  }

  [Fact]
  public void TrayActionButtonsDispatchOnceEvenWhenDismissalRepeats()
  {
    RunOnSta(() =>
    {
      (Func<TrayFlyoutWindow, Button> Button, Action<TrayFlyoutWindow, Action> Subscribe)[] actions =
      [
        (window => window.SetupModelButton,
          (window, hit) => window.OpenSettingsRequested += (_, _) => hit()),
        (window => window.OpenWorkbenchButton,
          (window, hit) => window.OpenWorkbenchRequested += (_, _) => hit()),
        (window => window.OpenHistoryButton,
          (window, hit) => window.OpenHistoryRequested += (_, _) => hit()),
        (window => window.RetryLastDictationButton,
          (window, hit) => window.RetryLastDictationRequested += (_, _) => hit()),
        (window => window.OpenSettingsButton,
          (window, hit) => window.OpenSettingsRequested += (_, _) => hit()),
        (window => window.StartWithWindowsButton,
          (window, hit) => window.StartupToggleRequested += (_, _) => hit()),
        (window => window.ExportDiagnosticsButton,
          (window, hit) => window.ExportDiagnosticsRequested += (_, _) => hit()),
        (window => window.QuitButton,
          (window, hit) => window.QuitRequested += (_, _) => hit()),
      ];

      foreach ((Func<TrayFlyoutWindow, Button> buttonSelector,
        Action<TrayFlyoutWindow, Action> subscribe) in actions)
      {
        TrayFlyoutWindow window = CreateFlyout();
        int hits = 0;
        subscribe(window, () => hits++);
        Button button = buttonSelector(window);
        try
        {
          window.Show();
          button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
          window.Dismiss();
          button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
          Assert.Equal(1, hits);
          Assert.False(window.IsVisible);
        }
        finally { window.Dismiss(); }
      }

      TrayFlyoutWindow modelWindow = new();
      ModelInfo model = new("cohere-local", "cohere-transcribe-03-2026", "Cohere", true, true, ["en"]);
      modelWindow.SetPresentation(TrayMenuPresentation.Create(DictationSessionState.Idle,
        ModelReadinessSnapshot.Empty, [model],
        new TranscriptionModelSelection(model.ProviderId, model.ModelId), modelCatalogLoaded: true));
      int modelHits = 0;
      modelWindow.QuickModelSwitchRequested += (_, _) => modelHits++;
      Button modelButton = Assert.IsType<Button>(modelWindow.ModelListPanel.Children[0]);
      try
      {
        modelWindow.Show();
        modelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        modelWindow.Dismiss();
        modelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, modelHits);
      }
      finally { modelWindow.Dismiss(); }
    });
  }

  [Fact]
  public void ExportClickClosesOnceAndProducesVerifiedBundleAndResult()
  {
    string root = Path.Combine(Path.GetTempPath(), "KoncusNai.TrayExport.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      RunOnSta(() =>
      {
        string settingsPath = Path.Combine(root, "settings.json");
        File.WriteAllText(settingsPath, "{\"apiToken\":\"private-test-value\",\"theme\":\"Dark\"}");
        StructuredDiagnosticsOptions options = StructuredDiagnosticsOptions.Default with
        {
          LogsDirectoryPath = Path.Combine(root, "logs"),
        };
        using LocalFileDiagnostics diagnostics = new(options, new DiagnosticsBundleExporter(), settingsPath);
        diagnostics.Info("Tray export regression test");

        TrayFlyoutWindow flyout = new();
        flyout.Left = -10000;
        flyout.Top = -10000;
        flyout.SetPresentation(TrayMenuPresentation.Create(DictationSessionState.Idle,
          ModelReadinessSnapshot.Empty, Array.Empty<ModelInfo>(),
          new TranscriptionModelSelection("cohere-local", "cohere-transcribe-03-2026"),
          modelCatalogLoaded: true));
        int commands = 0;
        string? copiedPath = null;
        DiagnosticsExportResultWindow? result = null;
        string? bundlePath = null;
        flyout.ExportDiagnosticsRequested += (_, _) =>
        {
          commands++;
          bundlePath = diagnostics.ExportBundle(Path.Combine(root, "support"));
          DiagnosticsBundleVerifier.Verify(bundlePath);
          result = new DiagnosticsExportResultWindow(bundlePath, path => copiedPath = path);
          result.Show();
        };

        try
        {
          flyout.Show();
          flyout.ExportDiagnosticsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
          flyout.Dismiss();
          flyout.ExportDiagnosticsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

          Assert.Equal(1, commands);
          Assert.False(flyout.IsVisible);
          Assert.NotNull(bundlePath);
          Assert.True(File.Exists(bundlePath));
          Assert.NotNull(result);
          Assert.True(result.IsVisible);
          Assert.Equal(bundlePath, result.BundlePathTextBox.Text);
          Assert.Equal("Copy path", result.CopyPathButton.Content);
          result.CopyPathButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
          Assert.Equal(bundlePath, copiedPath);
          Assert.Equal("Copied", result.CopyPathButton.Content);
          using ZipArchive archive = ZipFile.OpenRead(bundlePath);
          Assert.NotNull(archive.GetEntry("manifest.json"));
          Assert.DoesNotContain(archive.Entries, entry =>
            entry.FullName.Contains("history", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
          result?.Close();
          flyout.Dismiss();
        }
      });
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Transfers a WPF STA failure back to the xUnit test thread.")]
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
      catch (Exception exception)
      {
        failure = exception;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Tray UI test timed out.");
    if (failure is not null) throw new InvalidOperationException("Tray UI test failed.", failure);
  }

  private static TrayFlyoutWindow CreateFlyout()
  {
    TrayFlyoutWindow window = new();
    window.Left = -10000;
    window.Top = -10000;
    window.SetPresentation(TrayMenuPresentation.Create(DictationSessionState.Idle,
      ModelReadinessSnapshot.Empty, Array.Empty<ModelInfo>(),
      new TranscriptionModelSelection("cohere-local", "cohere-transcribe-03-2026"),
      modelCatalogLoaded: true));
    return window;
  }
}
