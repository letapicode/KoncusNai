using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchSettingsApplyStatus
{
  Applied,
  Deferred,
  Cancelled,
  Failed,
}

internal sealed record WorkbenchSettingsApplyResult(
  WorkbenchSettingsApplyStatus Status,
  AppSettings Settings,
  string StatusMessage,
  string? HotkeyStatus,
  bool ShouldRefreshModels,
  bool ShouldRefreshHistory,
  bool ShouldRefreshChatReadiness,
  bool ShouldRenderTranscript)
{
  public bool WasApplied => Status == WorkbenchSettingsApplyStatus.Applied;
}

/// <summary>
/// Owns normalized Workbench settings, deferred replacement, and the settings-application transaction.
/// WPF callers render the returned state but do not own settings-operation lifetime or recovery policy.
/// </summary>
internal sealed class WorkbenchSettingsApplicationController : IAsyncDisposable
{
  private readonly IDiagnostics diagnostics;
  private readonly WorkbenchQuickSettingsController quickSettingsController;
  private readonly WorkbenchOperationSession operationSession;
  private readonly WorkbenchDictationController dictationController;
  private readonly WorkbenchChatController chatController;
  private readonly WorkbenchReadAloudController readAloudController;

  private AppSettings currentSettings = AppSettings.Default;
  private AppSettings? pendingSettings;
  private bool registerWorkbenchHotkey = true;
  private bool disposed;

  public WorkbenchSettingsApplicationController(
    IDiagnostics diagnostics,
    WorkbenchQuickSettingsController quickSettingsController,
    WorkbenchOperationSession operationSession,
    WorkbenchDictationController dictationController,
    WorkbenchChatController chatController,
    WorkbenchReadAloudController readAloudController)
  {
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.quickSettingsController = quickSettingsController ?? throw new ArgumentNullException(nameof(quickSettingsController));
    this.operationSession = operationSession ?? throw new ArgumentNullException(nameof(operationSession));
    this.dictationController = dictationController ?? throw new ArgumentNullException(nameof(dictationController));
    this.chatController = chatController ?? throw new ArgumentNullException(nameof(chatController));
    this.readAloudController = readAloudController ?? throw new ArgumentNullException(nameof(readAloudController));
  }

  public AppSettings CurrentSettings => currentSettings;

  public bool HasPendingSettings => pendingSettings is not null;

  public AppSettings Queue(AppSettings settings, bool registerHotkey)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    AppSettings normalized = WorkbenchSettingsPolicy.Normalize(settings ?? throw new ArgumentNullException(nameof(settings)));
    pendingSettings = normalized;
    registerWorkbenchHotkey = registerHotkey;
    return normalized;
  }

  public void ApplyPresentationOnly(AppSettings settings)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    currentSettings = WorkbenchSettingsPolicy.Normalize(settings ?? throw new ArgumentNullException(nameof(settings)));
  }

  public Task<WorkbenchSettingsApplyResult> ApplyQueuedAsync(CancellationToken cancellationToken = default) =>
    TryApplyPendingAsync(cancellationToken);

  public async Task<WorkbenchSettingsApplyResult> TryApplyPendingAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (pendingSettings is null)
    {
      return CreateResult(WorkbenchSettingsApplyStatus.Deferred, currentSettings, string.Empty);
    }

    if (dictationController.State != WorkbenchSessionState.Idle || chatController.IsBusy)
    {
      return DeferForActiveWorkflow();
    }

    if (cancellationToken.IsCancellationRequested)
    {
      return CreateResult(WorkbenchSettingsApplyStatus.Cancelled, currentSettings, string.Empty);
    }

    using WorkbenchOperation? operation = operationSession.TryBegin(WorkbenchOperationKind.SettingsApply, cancellationToken);
    if (operation is null)
    {
      return DeferForActiveWorkflow();
    }

    AppSettings settings = pendingSettings;
    pendingSettings = null;
    try
    {
      await quickSettingsController.CancelLatestQueryAndWaitAsync().ConfigureAwait(false);
      await chatController.ResetCompletionServiceAsync().ConfigureAwait(false);
      await readAloudController.ResetAsync().ConfigureAwait(false);

      WorkbenchHotkeyRegistrationOutcome? outcome = await dictationController
        .ConfigureAsync(settings, registerWorkbenchHotkey, operation.CancellationToken)
        .ConfigureAwait(false);

      currentSettings = outcome?.Success == true
        ? settings with { Hotkey = outcome.ActiveBinding }
        : settings;

      if (outcome is null)
      {
        diagnostics.Info("Workbench hotkey registration skipped because the background dictation runtime owns the global hotkey.");
        return CreateResult(
          WorkbenchSettingsApplyStatus.Applied,
          currentSettings,
          string.Empty,
          "Hotkey: handled by Koncus Nai global dictation.",
          refresh: true);
      }

      if (!outcome.Success)
      {
        diagnostics.Warning(outcome.DiagnosticsMessage);
        return CreateResult(
          WorkbenchSettingsApplyStatus.Applied,
          currentSettings,
          string.Empty,
          outcome.StatusMessage);
      }

      if (outcome.UsedFallbackBinding)
      {
        diagnostics.Warning(outcome.DiagnosticsMessage);
        return CreateResult(
          WorkbenchSettingsApplyStatus.Applied,
          currentSettings,
          string.Empty,
          outcome.StatusMessage);
      }

      diagnostics.Info(outcome.DiagnosticsMessage);
      return CreateResult(
        WorkbenchSettingsApplyStatus.Applied,
        currentSettings,
        string.Empty,
        outcome.StatusMessage,
        refresh: true);
    }
    catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
    {
      return CreateResult(WorkbenchSettingsApplyStatus.Cancelled, currentSettings, string.Empty);
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
    {
      diagnostics.Error("Workbench settings apply failed.", ex);
      return CreateResult(
        WorkbenchSettingsApplyStatus.Failed,
        currentSettings,
        "Settings apply failed. See Diagnostics.");
    }
  }

  public ValueTask DisposeAsync()
  {
    disposed = true;
    pendingSettings = null;
    return ValueTask.CompletedTask;
  }

  private WorkbenchSettingsApplyResult DeferForActiveWorkflow()
  {
    diagnostics.Info("Workbench settings update deferred until the active operation finishes.");
    string status = dictationController.State != WorkbenchSessionState.Idle
      ? "Settings will apply after this recording."
      : "Settings will apply after the current operation.";
    return CreateResult(WorkbenchSettingsApplyStatus.Deferred, pendingSettings ?? currentSettings, status);
  }

  private static WorkbenchSettingsApplyResult CreateResult(
    WorkbenchSettingsApplyStatus status,
    AppSettings settings,
    string statusMessage,
    string? hotkeyStatus = null,
    bool refresh = false) =>
    new(
      status,
      settings,
      statusMessage,
      hotkeyStatus,
      ShouldRefreshModels: refresh,
      ShouldRefreshHistory: refresh,
      ShouldRefreshChatReadiness: refresh,
      ShouldRenderTranscript: refresh);
}
