using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal sealed record WorkbenchChatModelSetupCommandResult(
  string StatusMessage,
  bool OperationAccepted);

/// <summary>Owns chat-model setup arbitration, runtime reset, and the post-setup readiness transaction.</summary>
[SuppressMessage(
  "Design",
  "CA1031:Do not catch general exception types",
  Justification = "This UI command boundary records unexpected model-management failures and returns bounded presentation state.")]
internal sealed class WorkbenchChatModelSetupCommandController
{
  private readonly WorkbenchChatController chatController;
  private readonly WorkbenchChatModelController modelController;
  private readonly WorkbenchChatSendController chatSendController;
  private readonly IDiagnostics diagnostics;

  public WorkbenchChatModelSetupCommandController(
    WorkbenchChatController chatController,
    WorkbenchChatModelController modelController,
    WorkbenchChatSendController chatSendController,
    IDiagnostics diagnostics)
  {
    this.chatController = chatController ?? throw new ArgumentNullException(nameof(chatController));
    this.modelController = modelController ?? throw new ArgumentNullException(nameof(modelController));
    this.chatSendController = chatSendController ?? throw new ArgumentNullException(nameof(chatSendController));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  public async Task<WorkbenchChatModelSetupCommandResult> SetupAsync(
    IProgress<WorkbenchChatModelSetupProgress>? progress = null,
    Action<WorkbenchChatSendProgress>? readinessProgress = null,
    Action? operationStarted = null)
  {
    using WorkbenchChatOperation? operation = chatController.TryBeginOperation(WorkbenchChatOperationKind.ModelSetup);
    if (operation is null)
    {
      return new WorkbenchChatModelSetupCommandResult(string.Empty, OperationAccepted: false);
    }

    operationStarted?.Invoke();
    try
    {
      WorkbenchChatModelSetupResult setup = await modelController
        .SetupAsync(
          chatController.Selection,
          chatController.IsModelInstalled,
          chatController.IsRuntimeReady,
          progress,
          operation.CancellationToken)
        .ConfigureAwait(true);
      if (!setup.RefreshReadiness)
      {
        chatController.MarkRuntimeNotReady();
        string status = setup.Failure is null
          ? setup.Status
          : "Model setup failed. See Diagnostics.";
        return new WorkbenchChatModelSetupCommandResult(status, OperationAccepted: true);
      }

      await chatController.ResetCompletionServiceAsync().ConfigureAwait(true);
      WorkbenchChatModelReadinessState? readiness = await chatSendController
        .RefreshReadinessAsync(readinessProgress, operation.CancellationToken)
        .ConfigureAwait(true);
      return new WorkbenchChatModelSetupCommandResult(
        readiness?.Status ?? "Model selection changed; readiness was not applied.",
        OperationAccepted: true);
    }
    catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
    {
      return new WorkbenchChatModelSetupCommandResult("Model setup stopped.", OperationAccepted: true);
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected Workbench chat-model setup failure.", ex);
      chatController.MarkRuntimeNotReady();
      return new WorkbenchChatModelSetupCommandResult(
        "Model setup failed. See Diagnostics.",
        OperationAccepted: true);
    }
  }
}
