using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using Forms = System.Windows.Forms;

namespace DictateAnywhere.App.Tray;

public sealed class TrayIconHost : ITrayIconHost
{
  private const string ProductName = AppBrand.Name;
  private readonly Forms.NotifyIcon notifyIcon;
  private readonly Icon notifyIconAsset;
  private readonly bool ownsNotifyIconAsset;
  private readonly Forms.ContextMenuStrip menu;
  private readonly Forms.ToolStripMenuItem statusItem;
  private readonly Forms.ToolStripMenuItem readinessItem;
  private readonly Forms.ToolStripMenuItem openSettingsItem;
  private readonly Forms.ToolStripMenuItem openWorkbenchItem;
  private readonly Forms.ToolStripMenuItem openHistoryItem;
  private readonly Forms.ToolStripMenuItem startOnLoginItem;
  private readonly Forms.ToolStripMenuItem retryLastDictationItem;
  private readonly Forms.ToolStripMenuItem modelsItem;
  private readonly Forms.ToolStripMenuItem exportDiagnosticsItem;
  private readonly Forms.ToolStripMenuItem quitItem;
  private bool suppressStartupToggleEvent;
  private string currentStatus = "Idle";
  private string currentReadinessSummary = "Models pending";
  private bool disposed;

  public TrayIconHost()
  {
    (notifyIconAsset, ownsNotifyIconAsset) = LoadApplicationIcon();

    statusItem = new Forms.ToolStripMenuItem("Status: Idle")
    {
      Enabled = false,
    };

    readinessItem = new Forms.ToolStripMenuItem("Models: pending")
    {
      Enabled = true,
    };

    openSettingsItem = new Forms.ToolStripMenuItem("Open Settings");
    openSettingsItem.Click += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    openWorkbenchItem = new Forms.ToolStripMenuItem("Open Textbox Workbench");
    openWorkbenchItem.Click += (_, _) => OpenWorkbenchRequested?.Invoke(this, EventArgs.Empty);

    openHistoryItem = new Forms.ToolStripMenuItem("Open Dictation History");
    openHistoryItem.Click += (_, _) => OpenHistoryRequested?.Invoke(this, EventArgs.Empty);

    startOnLoginItem = new Forms.ToolStripMenuItem("Start with Windows")
    {
      CheckOnClick = true,
    };
    startOnLoginItem.CheckedChanged += OnStartupToggleCheckedChanged;

    retryLastDictationItem = new Forms.ToolStripMenuItem("Retry Last Dictation");
    retryLastDictationItem.Click += (_, _) => RetryLastDictationRequested?.Invoke(this, EventArgs.Empty);

    modelsItem = new Forms.ToolStripMenuItem("Quick Model Switch");
    modelsItem.DropDownItems.Add(new Forms.ToolStripMenuItem("No installed models")
    {
      Enabled = false,
    });

    exportDiagnosticsItem = new Forms.ToolStripMenuItem("Export Diagnostics Bundle");
    exportDiagnosticsItem.Click += (_, _) => ExportDiagnosticsRequested?.Invoke(this, EventArgs.Empty);

    quitItem = new Forms.ToolStripMenuItem("Quit");
    quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

    menu = new Forms.ContextMenuStrip();
    menu.Items.Add(statusItem);
    menu.Items.Add(readinessItem);
    menu.Items.Add(new Forms.ToolStripSeparator());
    menu.Items.Add(openSettingsItem);
    menu.Items.Add(openWorkbenchItem);
    menu.Items.Add(openHistoryItem);
    menu.Items.Add(startOnLoginItem);
    menu.Items.Add(new Forms.ToolStripSeparator());
    menu.Items.Add(retryLastDictationItem);
    menu.Items.Add(new Forms.ToolStripSeparator());
    menu.Items.Add(modelsItem);
    menu.Items.Add(exportDiagnosticsItem);
    menu.Items.Add(new Forms.ToolStripSeparator());
    menu.Items.Add(quitItem);

    notifyIcon = new Forms.NotifyIcon
    {
      Icon = notifyIconAsset,
      Text = ProductName,
      Visible = true,
      ContextMenuStrip = menu,
    };
    notifyIcon.DoubleClick += (_, _) => OpenWorkbenchRequested?.Invoke(this, EventArgs.Empty);
  }

  public event EventHandler? OpenSettingsRequested;

  public event EventHandler? OpenWorkbenchRequested;

  public event EventHandler? OpenHistoryRequested;

  public event EventHandler? QuitRequested;

  public event EventHandler<bool>? StartupToggleRequested;


  public event EventHandler? RetryLastDictationRequested;

  public event EventHandler<TranscriptionModelSelection>? QuickModelSwitchRequested;

  public event EventHandler? ExportDiagnosticsRequested;

  public void SetStatus(DictationSessionState state)
  {
    string status = state switch
    {
      DictationSessionState.Recording => "Recording",
      DictationSessionState.Transcribing => "Transcribing",
      DictationSessionState.Inserting => "Inserting",
      DictationSessionState.Error => "Error",
      _ => "Idle",
    };

    currentStatus = status;
    statusItem.Text = $"Status: {status}";
    UpdateNotifyIconText();
  }

  public void SetModelReadiness(ModelReadinessSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    readinessItem.DropDownItems.Clear();
    if (snapshot.Entries.Count == 0)
    {
      currentReadinessSummary = "Models pending";
      readinessItem.Text = "Models: pending";
      UpdateNotifyIconText();
      return;
    }

    currentReadinessSummary = FormatReadinessSummary(snapshot.Entries);
    readinessItem.Text = $"Models: {currentReadinessSummary}";
    foreach (ModelReadinessEntry entry in snapshot.Entries)
    {
      readinessItem.DropDownItems.Add(new Forms.ToolStripMenuItem(FormatReadinessDetail(entry))
      {
        Enabled = false,
      });
    }

    UpdateNotifyIconText();
  }

  public void SetStartOnLoginEnabled(bool enabled)
  {
    suppressStartupToggleEvent = true;
    try
    {
      startOnLoginItem.Checked = enabled;
    }
    finally
    {
      suppressStartupToggleEvent = false;
    }
  }

  public void SetModelMenu(IReadOnlyList<ModelInfo> models, TranscriptionModelSelection activeSelection)
  {
    ArgumentNullException.ThrowIfNull(models);
    ArgumentNullException.ThrowIfNull(activeSelection);

    modelsItem.DropDownItems.Clear();
    int installedCount = 0;
    foreach (ModelInfo model in models)
    {
      if (!model.IsInstalled)
      {
        continue;
      }

      installedCount++;
      TranscriptionModelSelection selection = new(model.ProviderId, model.ModelId);
      string identity = $"{model.ProviderId}/{model.ModelId}";
      Forms.ToolStripMenuItem item = new($"{model.DisplayName} ({identity})")
      {
        Checked =
          string.Equals(selection.ProviderId, activeSelection.ProviderId, StringComparison.OrdinalIgnoreCase)
          && string.Equals(selection.ModelId, activeSelection.ModelId, StringComparison.OrdinalIgnoreCase),
      };
      item.Click += (_, _) => QuickModelSwitchRequested?.Invoke(this, selection);
      modelsItem.DropDownItems.Add(item);
    }

    if (installedCount == 0)
    {
      modelsItem.DropDownItems.Add(new Forms.ToolStripMenuItem("No installed models")
      {
        Enabled = false,
      });
    }
  }

  public void ShowNotification(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info, int timeoutMilliseconds = 5000)
  {
    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
    {
      return;
    }

    notifyIcon.BalloonTipTitle = title;
    notifyIcon.BalloonTipText = message;
    notifyIcon.BalloonTipIcon = icon;
    notifyIcon.ShowBalloonTip(timeoutMilliseconds);
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    notifyIcon.Visible = false;
    notifyIcon.Dispose();
    if (ownsNotifyIconAsset)
    {
      notifyIconAsset.Dispose();
    }

    menu.Dispose();
  }

  private void OnStartupToggleCheckedChanged(object? sender, EventArgs e)
  {
    if (suppressStartupToggleEvent)
    {
      return;
    }

    StartupToggleRequested?.Invoke(this, startOnLoginItem.Checked);
  }

  private void UpdateNotifyIconText()
  {
    notifyIcon.Text = ProductName;
  }

  private static string FormatReadinessSummary(IReadOnlyList<ModelReadinessEntry> entries)
  {
    int readyCount = entries.Count(entry => entry.State == ModelReadinessState.Ready);
    int warmingCount = entries.Count(entry => entry.State == ModelReadinessState.Warming || entry.State == ModelReadinessState.Pending);
    int failedCount = entries.Count(entry => entry.State == ModelReadinessState.Failed);
    int installedOrPendingCount = entries.Count(entry => entry.State != ModelReadinessState.NotInstalled);

    if (installedOrPendingCount == 0)
    {
      return "not installed";
    }

    if (readyCount == installedOrPendingCount)
    {
      return $"ready {readyCount}/{installedOrPendingCount}";
    }

    if (failedCount > 0)
    {
      return $"ready {readyCount}/{installedOrPendingCount}, failed {failedCount}";
    }

    if (warmingCount > 0)
    {
      return $"warming {readyCount}/{installedOrPendingCount}";
    }

    return $"ready {readyCount}/{installedOrPendingCount}";
  }

  private static string FormatReadinessDetail(ModelReadinessEntry entry)
  {
    string label = $"{entry.ProviderId}/{entry.ModelId}";
    string duration = entry.WarmupDuration is null
      ? string.Empty
      : string.Format(CultureInfo.InvariantCulture, " ({0:F1}s)", entry.WarmupDuration.Value.TotalSeconds);
    string error = string.IsNullOrWhiteSpace(entry.ErrorMessage)
      ? string.Empty
      : $": {entry.ErrorMessage}";

    return $"{label}: {entry.State}{duration}{error}";
  }

  private static string TruncateNotifyIconText(string text)
  {
    const int maxLength = 63;
    if (text.Length <= maxLength)
    {
      return text;
    }

    return text[..maxLength];
  }

  internal static (Icon Icon, bool OwnsIcon) LoadApplicationIcon()
  {
    // Load our own resource even under a test host or `dotnet App.dll`.
    // Clone before disposing the stream: System.Drawing.Icon can retain its input.
    using System.IO.Stream stream = typeof(TrayIconHost).Assembly.GetManifestResourceStream("KoncusNai.TrayIcon")
      ?? throw new InvalidOperationException("The Koncus Nai tray icon resource is missing.");
    using Icon source = new(stream, Forms.SystemInformation.SmallIconSize);
    return ((Icon)source.Clone(), true);
  }
}
