using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Workbench;

internal readonly record struct WorkbenchChatIdentity(string ConversationId, long Revision);

/// <summary>Owns Workbench chat conversation state, pending context, model readiness, and completion-service lifetime.</summary>
internal sealed class WorkbenchChatController : IAsyncDisposable
{
  private readonly Func<ChatModelSelection, IChatCompletionService> completionServiceFactory;
  private readonly WorkbenchChatOperationSession operationSession = new();
  private readonly List<ChatFileAttachment> pendingFiles = [];
  private List<ChatMessage> messages = [];
  private IChatCompletionService? completionService;
  private ChatModelSelection? serviceSelection;
  private ChatHistoryRecord? selectedRecord;
  private long conversationRevision;
  private bool disposed;
  private readonly Func<TimeZoneInfo> timeZone;

  public WorkbenchChatController(Func<ChatModelSelection, IChatCompletionService> completionServiceFactory,
    Func<TimeZoneInfo>? timeZone = null)
  {
    this.completionServiceFactory = completionServiceFactory ?? throw new ArgumentNullException(nameof(completionServiceFactory));
    this.timeZone = timeZone ?? (() => TimeZoneInfo.Local);
    NewChat();
  }

  public string ConversationId { get; private set; } = string.Empty;
  public ChatModelSelection Selection { get; private set; } = ChatModelSelection.Default;
  public IReadOnlyList<ChatMessage> Messages => messages;
  public IReadOnlyList<ChatFileAttachment> PendingFiles => pendingFiles;
  public ChatHistoryRecord? SelectedRecord => selectedRecord;
  public bool IsModelInstalled { get; private set; }
  public bool IsRuntimeReady { get; private set; }
  public bool IsBusy => operationSession.IsBusy;
  public bool CanCancel => operationSession.CanCancel;

  public WorkbenchChatIdentity CaptureIdentity() => new(ConversationId, conversationRevision);

  public bool IsCurrent(WorkbenchChatIdentity identity) =>
    identity.Revision == conversationRevision
    && string.Equals(identity.ConversationId, ConversationId, StringComparison.Ordinal);

  public WorkbenchChatOperation? TryBeginOperation(WorkbenchChatOperationKind kind) => operationSession.TryBegin(kind);

  public WorkbenchChatOperationKind? CancelActiveOperation() => operationSession.CancelActive();

  public void NewChat()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    conversationRevision++;
    ConversationId = Guid.NewGuid().ToString("N");
    selectedRecord = null;
    messages = [];
    pendingFiles.Clear();
  }

  public void Load(ChatHistoryRecord record)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ChatHistoryRecord normalized = (record ?? throw new ArgumentNullException(nameof(record))).Normalize();
    conversationRevision++;
    pendingFiles.Clear();
    ConversationId = normalized.ConversationId;
    selectedRecord = normalized;
    Selection = new ChatModelSelection(normalized.ProviderId, normalized.ModelId).Normalize();
    messages = normalized.Messages.Select(message => message.Normalize()).ToList();
  }

  public void SelectModel(ChatModelSelection selection)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    Selection = (selection ?? throw new ArgumentNullException(nameof(selection))).Normalize();
    IsModelInstalled = false;
    IsRuntimeReady = false;
  }

  public bool IsCurrentSelection(ChatModelSelection selection) =>
    SelectionsEqual(Selection, selection);

  public void SetReadiness(bool isInstalled, bool isRuntimeReady)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    IsModelInstalled = isInstalled;
    IsRuntimeReady = isInstalled && isRuntimeReady;
  }

  public void MarkRuntimeNotReady()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    IsRuntimeReady = false;
  }

  public void AddMessage(ChatMessage message)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    messages.Add((message ?? throw new ArgumentNullException(nameof(message))).Normalize());
  }

  public bool TryAddLocalGreeting(string prompt, DateTimeOffset now)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    string reply;
    if (pendingFiles.Count > 0)
    {
      return false;
    }
    if (LocalCalendarContext.CanAnswer(prompt)) reply = LocalCalendarContext.Answer(prompt, now, timeZone());
    else if (!LocalGreetingResponder.TryCreateReply(prompt, out reply)) return false;

    messages.Add(new ChatMessage(ChatMessageRoles.User, prompt, now).Normalize());
    messages.Add(new ChatMessage(ChatMessageRoles.Assistant, reply, now).Normalize());
    return true;
  }

  public ChatCompletionRequest BeginCompletion(string prompt, DateTimeOffset now)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
    MergePendingFileContext(now);
    messages.Add(new ChatMessage(ChatMessageRoles.User, prompt, now).Normalize());
    // Brand only the outgoing prompt. Saved history and file-context identifiers
    // must stay intact, and providers that retain one system message need both
    // the introduction and the existing context in that message.
    string introduction = AppBrand.AssistantIntroduction + "\n\n" + LocalCalendarContext.Instructions(now, timeZone());
    ChatMessage[] requestMessages = messages.Where(message => !IsAppFailureNotice(message)).Select(RemoveContinuationNotice).Select(message =>
      string.Equals(message.Role, ChatMessageRoles.System, StringComparison.OrdinalIgnoreCase)
        ? message with { Content = introduction + "\n\n" + message.Content }
        : message).ToArray();
    if (!requestMessages.Any(message => string.Equals(message.Role, ChatMessageRoles.System, StringComparison.OrdinalIgnoreCase)))
    {
      requestMessages = [new ChatMessage(ChatMessageRoles.System, introduction, now), .. requestMessages];
    }
    return new ChatCompletionRequest(requestMessages, Selection);
  }

  public void AddCompletion(ChatCompletionResult completion, DateTimeOffset now)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(completion);
    string reply = completion.WasTruncated
      ? completion.Text + "\n\n**More available:** Send **Continue** and I’ll add the next part without repeating this answer."
      : completion.Text;
    messages.Add(new ChatMessage(ChatMessageRoles.Assistant, reply, now).Normalize());
  }

  public void AddFailure(TimeSpan elapsed, DateTimeOffset now, string? explanation = null)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    messages.Add(new ChatMessage(
      ChatMessageRoles.Assistant,
      $"**Response failed after {ChatRequestDurationFormatter.Format(elapsed)}.**\n\n"
        + (explanation ?? "The local model could not complete the request. See Diagnostics for details.")
        + "\n\nThis is an app error, not a model response. Please retry your question.",
      now).Normalize());
  }

  private static bool IsAppFailureNotice(ChatMessage message) =>
    message.Role == ChatMessageRoles.Assistant && (
      (message.Content.StartsWith("**Response failed after ", StringComparison.Ordinal)
        && message.Content.EndsWith("This is an app error, not a model response. Please retry your question.", StringComparison.Ordinal))
      || (message.Content.StartsWith("I couldn’t finish that local response after ", StringComparison.Ordinal)
        && message.Content.EndsWith("Please send **Continue** and I’ll retry with a smaller, focused part of the answer.", StringComparison.Ordinal)));

  private static ChatMessage RemoveContinuationNotice(ChatMessage message)
  {
    const string notice = "\n\n**More available:** Send **Continue** and I’ll add the next part without repeating this answer.";
    return message.Role == ChatMessageRoles.Assistant && message.Content.EndsWith(notice, StringComparison.Ordinal)
      ? message with { Content = message.Content[..^notice.Length] } : message;
  }

  public bool RemovePendingFile(string attachmentId)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    return pendingFiles.RemoveAll(item => string.Equals(item.Id, attachmentId, StringComparison.Ordinal)) > 0;
  }

  public void AddPendingFile(ChatFileAttachment attachment)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(attachment);
    pendingFiles.RemoveAll(item => string.Equals(item.SourcePath, attachment.SourcePath, StringComparison.OrdinalIgnoreCase));
    pendingFiles.Add(attachment);
  }

  public void MergePendingFileContext(DateTimeOffset now)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (pendingFiles.Count == 0)
    {
      return;
    }

    messages = ChatFileContext.Merge(messages, pendingFiles, now).ToList();
    pendingFiles.Clear();
  }

  public ChatHistoryRecord CreateHistoryRecord(string title)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    DateTimeOffset now = DateTimeOffset.UtcNow;
    DateTimeOffset createdUtc = selectedRecord?.CreatedUtc
      ?? (messages.Count == 0 ? now : messages[0].CreatedUtc);
    return new ChatHistoryRecord(
      ConversationId,
      ChatHistoryTitleFormatter.ResolveTitle(title, messages),
      createdUtc,
      now,
      Selection.ProviderId,
      Selection.ModelId,
      messages.ToArray()).Normalize();
  }

  public void MarkSaved(ChatHistoryRecord record)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    selectedRecord = (record ?? throw new ArgumentNullException(nameof(record))).Normalize();
  }

  public async Task<IChatCompletionService> GetCompletionServiceAsync()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (completionService is not null && SelectionsEqual(serviceSelection, Selection))
    {
      return completionService;
    }

    await ResetCompletionServiceAsync().ConfigureAwait(true);
    completionService = completionServiceFactory(Selection);
    serviceSelection = Selection.Normalize();
    return completionService;
  }

  public async Task ResetCompletionServiceAsync()
  {
    if (completionService is IAsyncDisposable disposable)
    {
      await disposable.DisposeAsync().ConfigureAwait(true);
    }

    completionService = null;
    serviceSelection = null;
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    await operationSession.DisposeAsync().ConfigureAwait(true);
    await ResetCompletionServiceAsync().ConfigureAwait(true);
  }

  private static bool SelectionsEqual(ChatModelSelection? left, ChatModelSelection? right)
  {
    if (left is null || right is null)
    {
      return false;
    }

    ChatModelSelection normalizedLeft = left.Normalize();
    ChatModelSelection normalizedRight = right.Normalize();
    return string.Equals(normalizedLeft.ProviderId, normalizedRight.ProviderId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(normalizedLeft.ModelId, normalizedRight.ModelId, StringComparison.OrdinalIgnoreCase);
  }
}
