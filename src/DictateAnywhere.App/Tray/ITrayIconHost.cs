using System;
using System.Collections.Generic;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using Forms = System.Windows.Forms;

namespace DictateAnywhere.App.Tray;

internal interface ITrayIconHost : IDisposable
{
  event EventHandler? OpenSettingsRequested;
  event EventHandler? OpenWorkbenchRequested;
  event EventHandler? OpenHistoryRequested;
  event EventHandler? QuitRequested;
  event EventHandler<bool>? StartupToggleRequested;
  event EventHandler? RetryLastDictationRequested;
  event EventHandler<TranscriptionModelSelection>? QuickModelSwitchRequested;
  event EventHandler? ExportDiagnosticsRequested;

  void SetStatus(DictationSessionState state);
  void SetModelReadiness(ModelReadinessSnapshot snapshot);
  void SetStartOnLoginEnabled(bool enabled);
  void SetModelMenu(IReadOnlyList<ModelInfo> models, TranscriptionModelSelection activeSelection);
  void ShowNotification(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info, int timeoutMilliseconds = 5000);
}
