using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Productivity;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using DictateAnywhere.Diagnostics;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchChatSendProgressKind
{
  OperationStarted,
  ReadinessChanged,
  LocalReplyAdded,
  CompletionStarted,
  CompletionAdded,
  FailureAdded,
}

internal sealed record WorkbenchChatSendProgress(
  WorkbenchChatSendProgressKind Kind,
  WorkbenchChatModelReadinessState? Readiness = null,
  WorkbenchChatIdentity? Conversation = null);

internal sealed record WorkbenchChatSendResult(
  string StatusMessage,
  string Title,
  bool ShouldRefreshHistory,
  bool OperationAccepted,
  WorkbenchChatIdentity Conversation);

/// <summary>
/// Owns one Workbench chat-send transaction: readiness, operation arbitration,
/// conversation mutation, completion, persistence, failure policy, and timing.
/// </summary>
internal sealed class WorkbenchChatSendController
{
  private readonly WorkbenchChatController chatController;
  private readonly Func<ChatModelSelection, CancellationToken, Task<WorkbenchChatModelReadinessState>> checkReadinessAsync;
  private readonly Func<AppSettings, ChatHistoryRecord, CancellationToken, Task<WorkbenchHistoryMutationResult>> saveChatAsync;
  private readonly IDiagnostics diagnostics;

  public WorkbenchChatSendController(
    WorkbenchChatController chatController,
    Func<ChatModelSelection, CancellationToken, Task<WorkbenchChatModelReadinessState>> checkReadinessAsync,
    Func<AppSettings, ChatHistoryRecord, CancellationToken, Task<WorkbenchHistoryMutationResult>> saveChatAsync,
    IDiagnostics diagnostics)
  {
    this.chatController = chatController ?? throw new ArgumentNullException(nameof(chatController));
    this.checkReadinessAsync = checkReadinessAsync ?? throw new ArgumentNullException(nameof(checkReadinessAsync));
    this.saveChatAsync = saveChatAsync ?? throw new ArgumentNullException(nameof(saveChatAsync));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  public async Task<WorkbenchChatModelReadinessState?> RefreshReadinessAsync(
    Action<WorkbenchChatSendProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ChatModelSelection selection = chatController.Selection.Normalize();
    WorkbenchChatModelReadinessState state = await checkReadinessAsync(selection, cancellationToken)
      .ConfigureAwait(true);
    if (!chatController.IsCurrentSelection(selection))
    {
      return null;
    }

    chatController.SetReadiness(state.IsInstalled, state.IsRuntimeReady);
    if (state.Failure is not null)
    {
      diagnostics.Warning($"Chat model readiness check failed: {state.Failure.Message}");
    }

    progress?.Invoke(new WorkbenchChatSendProgress(WorkbenchChatSendProgressKind.ReadinessChanged, state));
    return state;
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This command boundary records unexpected local-runtime failures and returns bounded presentation state.")]
  public async Task<WorkbenchChatSendResult> SendAsync(
    string prompt,
    string title,
    string modelDisplayName,
    AppSettings settings,
    Action<WorkbenchChatSendProgress>? progress = null)
  {
    ArgumentNullException.ThrowIfNull(settings);
    string normalizedPrompt = prompt?.Trim() ?? string.Empty;
    string currentTitle = title ?? string.Empty;
    WorkbenchChatIdentity identity = chatController.CaptureIdentity();
    if (normalizedPrompt.Length == 0)
    {
      return new WorkbenchChatSendResult(
        "Enter a message before sending.",
        currentTitle,
        ShouldRefreshHistory: false,
        OperationAccepted: false,
        identity);
    }

    if (chatController.PendingFiles.Count == 0
        && (LocalGreetingResponder.IsStandaloneGreeting(normalizedPrompt) || LocalCalendarContext.CanAnswer(normalizedPrompt)))
    {
      return await SendLocalGreetingAsync(normalizedPrompt, currentTitle, settings, progress).ConfigureAwait(true);
    }

    using WorkbenchChatOperation? operation = chatController.TryBeginOperation(WorkbenchChatOperationKind.Completion);
    if (operation is null)
    {
      return new WorkbenchChatSendResult(
        string.Empty,
        currentTitle,
        ShouldRefreshHistory: false,
        OperationAccepted: false,
        identity);
    }

    progress?.Invoke(new WorkbenchChatSendProgress(WorkbenchChatSendProgressKind.OperationStarted, Conversation: identity));

    if (!chatController.IsModelInstalled || !chatController.IsRuntimeReady)
    {
      WorkbenchChatModelReadinessState? readiness = await RefreshReadinessAsync(progress).ConfigureAwait(true);
      if (!chatController.IsCurrent(identity))
      {
        return StaleResult(currentTitle, identity);
      }

      if (readiness is null || !chatController.IsModelInstalled)
      {
        return new WorkbenchChatSendResult(
          "Download the local model before sending.",
          currentTitle,
          ShouldRefreshHistory: false,
          OperationAccepted: true,
          identity);
      }

      if (!chatController.IsRuntimeReady)
      {
        return new WorkbenchChatSendResult(
          "Update the local model runtime before sending.",
          currentTitle,
          ShouldRefreshHistory: false,
          OperationAccepted: true,
          identity);
      }
    }

    Stopwatch stopwatch = Stopwatch.StartNew();
    using OperationDiagnosticScope opScope = OperationDiagnosticScope.Begin(
      diagnostics,
      operationName: "ChatSend",
      provider: chatController.Selection.ProviderId,
      model: modelDisplayName,
      runtime: "local",
      initialProperties: new Dictionary<string, object?> { ["promptLength"] = normalizedPrompt.Length });

    try
    {
      if (!chatController.IsCurrent(identity))
      {
        return StaleResult(currentTitle, identity);
      }

      opScope.Stage("completion");
      ChatCompletionRequest request = chatController.BeginCompletion(normalizedPrompt, DateTimeOffset.UtcNow);
      progress?.Invoke(new WorkbenchChatSendProgress(WorkbenchChatSendProgressKind.CompletionStarted, Conversation: identity));

      IChatCompletionService service = await chatController.GetCompletionServiceAsync().ConfigureAwait(true);
      ChatCompletionResult completion = await service
        .CompleteAsync(request, operation.CancellationToken)
        .ConfigureAwait(true);
      operation.CancellationToken.ThrowIfCancellationRequested();
      if (!chatController.IsCurrent(identity))
      {
        return StaleResult(currentTitle, identity);
      }

      chatController.AddCompletion(completion, DateTimeOffset.UtcNow);
      progress?.Invoke(new WorkbenchChatSendProgress(WorkbenchChatSendProgressKind.CompletionAdded, Conversation: identity));

      opScope.Stage("save_history");
      WorkbenchChatSendResult sendResult = await SaveConversationAsync(currentTitle, settings, identity, operation.CancellationToken).ConfigureAwait(true);
      opScope.Complete();
      return sendResult;
    }
    catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
    {
      opScope.Cancel("User stopped chat response.");
      return new WorkbenchChatSendResult(
        "Response stopped.",
        currentTitle,
        ShouldRefreshHistory: false,
        OperationAccepted: true,
        identity);
    }
    catch (Exception ex) when (IsExpectedLocalFailure(ex))
    {
      stopwatch.Stop();
      opScope.Fail(ex, $"{modelDisplayName} chat failed: {ex.Message}", level: "WARN");
      if (!chatController.IsCurrent(identity))
      {
        return StaleResult(currentTitle, identity);
      }

      chatController.AddFailure(stopwatch.Elapsed, DateTimeOffset.UtcNow, DescribeFailure(ex));
      progress?.Invoke(new WorkbenchChatSendProgress(WorkbenchChatSendProgressKind.FailureAdded, Conversation: identity));
      await TryRefreshReadinessAfterFailureAsync(progress).ConfigureAwait(true);
      return new WorkbenchChatSendResult(
        $"{modelDisplayName} failed after {ChatRequestDurationFormatter.Format(stopwatch.Elapsed)}. Retry or see Diagnostics.",
        currentTitle,
        ShouldRefreshHistory: false,
        OperationAccepted: true,
        identity);
    }
    catch (Exception ex)
    {
      stopwatch.Stop();
      opScope.Fail(ex, "Unexpected local chat failure.");
      if (!chatController.IsCurrent(identity))
      {
        return StaleResult(currentTitle, identity);
      }

      chatController.AddFailure(stopwatch.Elapsed, DateTimeOffset.UtcNow);
      progress?.Invoke(new WorkbenchChatSendProgress(WorkbenchChatSendProgressKind.FailureAdded, Conversation: identity));
      await TryRefreshReadinessAfterFailureAsync(progress).ConfigureAwait(true);
      return new WorkbenchChatSendResult(
        $"{modelDisplayName} stopped after {ChatRequestDurationFormatter.Format(stopwatch.Elapsed)}. The error was recorded in Diagnostics.",
        currentTitle,
        ShouldRefreshHistory: false,
        OperationAccepted: true,
        identity);
    }
  }

  private async Task<WorkbenchChatSendResult> SendLocalGreetingAsync(
    string prompt,
    string title,
    AppSettings settings,
    Action<WorkbenchChatSendProgress>? progress)
  {
    WorkbenchChatIdentity identity = chatController.CaptureIdentity();
    using WorkbenchChatOperation? operation = chatController.TryBeginOperation(WorkbenchChatOperationKind.LocalReply);
    if (operation is null)
    {
      return new WorkbenchChatSendResult(string.Empty, title, false, false, identity);
    }

    progress?.Invoke(new WorkbenchChatSendProgress(WorkbenchChatSendProgressKind.OperationStarted, Conversation: identity));

    if (!chatController.TryAddLocalGreeting(prompt, DateTimeOffset.UtcNow))
    {
      return new WorkbenchChatSendResult(string.Empty, title, false, true, identity);
    }

    progress?.Invoke(new WorkbenchChatSendProgress(WorkbenchChatSendProgressKind.LocalReplyAdded, Conversation: identity));
    WorkbenchChatSendResult saved = await SaveConversationAsync(title, settings, identity, operation.CancellationToken)
      .ConfigureAwait(true);
    return saved with { StatusMessage = $"Quick local reply. {saved.StatusMessage}" };
  }

  private async Task<WorkbenchChatSendResult> SaveConversationAsync(
    string title,
    AppSettings settings,
    WorkbenchChatIdentity identity,
    CancellationToken cancellationToken)
  {
    if (!chatController.IsCurrent(identity))
    {
      return StaleResult(title, identity);
    }

    if (chatController.Messages.Count == 0)
    {
      return new WorkbenchChatSendResult("Nothing saved.", title, false, true, identity);
    }

    ChatHistoryRecord record = chatController.CreateHistoryRecord(title);
    WorkbenchHistoryMutationResult result = await saveChatAsync(settings, record, cancellationToken)
      .ConfigureAwait(true);
    if (result.Status == HistoryCommandStatus.Succeeded && chatController.IsCurrent(identity))
    {
      chatController.MarkSaved(record);
    }

    return new WorkbenchChatSendResult(
      result.Message ?? "Chat not saved.",
      record.Title,
      result.Status == HistoryCommandStatus.Succeeded,
      OperationAccepted: true,
      identity);
  }

  private static WorkbenchChatSendResult StaleResult(string title, WorkbenchChatIdentity identity) => new(
    "The response finished for a conversation that is no longer open. Your current chat was left unchanged.",
    title,
    ShouldRefreshHistory: false,
    OperationAccepted: true,
    identity);

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "A secondary readiness probe must not replace the chat failure already returned by this command boundary.")]
  private async Task TryRefreshReadinessAfterFailureAsync(
    Action<WorkbenchChatSendProgress>? progress)
  {
    try
    {
      await RefreshReadinessAsync(progress).ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      diagnostics.Warning($"Chat readiness refresh after failure also failed: {ex.Message}");
    }
  }

  private static bool IsExpectedLocalFailure(Exception exception) =>
    exception is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or HttpRequestException;

  private static string DescribeFailure(Exception exception) => exception switch
  {
    HttpRequestException { StatusCode: HttpStatusCode.BadRequest } =>
      "The local model rejected the request format before returning an answer. See Diagnostics for details.",
    HttpRequestException { StatusCode: not null } =>
      "The local model server could not process the request. See Diagnostics for details.",
    TimeoutException => "The local model did not finish within the response time limit. See Diagnostics for details.",
    HttpRequestException => "The connection to the local model failed. See Diagnostics for details.",
    _ => "The local model could not complete the request. See Diagnostics for details.",
  };
}
