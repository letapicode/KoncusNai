using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchChatSendControllerTests
{
  [Xunit.Theory]
  [Xunit.InlineData("rejected", "rejected the request format")]
  [Xunit.InlineData("connection", "connection to the local model failed")]
  [Xunit.InlineData("timeout", "response time limit")]
  [Xunit.InlineData("server", "server could not process")]
  public async Task FailureReason_IsAccurateAndNeverPresentedAsAModelAnswer(string kind, string expected)
  {
    Exception failure = kind switch
    {
      "rejected" => new System.Net.Http.HttpRequestException("private diagnostic", null, System.Net.HttpStatusCode.BadRequest),
      "connection" => new System.Net.Http.HttpRequestException("private diagnostic"),
      "server" => new System.Net.Http.HttpRequestException("private diagnostic", null, System.Net.HttpStatusCode.InternalServerError),
      _ => new TimeoutException("private diagnostic"),
    };
    await using WorkbenchChatController chat = new(_ => new FailureService(failure));
    chat.SetReadiness(true, true);
    WorkbenchChatSendController sender = CreateSender(chat, (_, record, _) => Task.FromResult(Saved(record)));
    await sender.SendAsync("what is an algorithm", "Test", "Gemma 3", AppSettings.Default);
    string message = chat.Messages[1].Content;
    Xunit.Assert.Contains(expected, message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("app error, not a model response", message, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("private diagnostic", message, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Continue", message, StringComparison.Ordinal);
    ChatCompletionRequest retry = chat.BeginCompletion("Try again", DateTimeOffset.UtcNow);
    Xunit.Assert.DoesNotContain(retry.Messages, item => item.Content == message);
  }

  private sealed class FailureService(Exception failure) : IChatCompletionService
  {
    public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
      Task.FromException<ChatCompletionResult>(failure);
  }

  [Xunit.Fact]
  public async Task SendAsync_LocalGreetingMutatesAndPersistsWithoutCreatingModelService()
  {
    int serviceCreationCount = 0;
    ChatHistoryRecord? saved = null;
    await using WorkbenchChatController chat = new(_ =>
    {
      serviceCreationCount++;
      return new ImmediateChatService();
    });
    WorkbenchChatSendController sender = CreateSender(
      chat,
      (_, record, _) =>
      {
        saved = record;
        return Task.FromResult(Saved(record));
      });
    List<WorkbenchChatSendProgressKind> progress = [];

    WorkbenchChatSendResult result = await sender.SendAsync(
      "hello",
      "New chat",
      "Local model",
      AppSettings.Default,
      update => progress.Add(update.Kind));

    Xunit.Assert.True(result.OperationAccepted);
    Xunit.Assert.True(result.ShouldRefreshHistory);
    Xunit.Assert.StartsWith("Quick local reply.", result.StatusMessage, StringComparison.Ordinal);
    Xunit.Assert.Equal(2, chat.Messages.Count);
    Xunit.Assert.NotNull(saved);
    Xunit.Assert.Equal(0, serviceCreationCount);
    Xunit.Assert.Equal(
      [WorkbenchChatSendProgressKind.OperationStarted, WorkbenchChatSendProgressKind.LocalReplyAdded],
      progress);
  }

  [Xunit.Fact]
  public async Task SendAsync_CompletionOwnsMutationProgressAndPersistence()
  {
    ChatHistoryRecord? saved = null;
    await using WorkbenchChatController chat = new(_ => new ImmediateChatService());
    chat.SetReadiness(isInstalled: true, isRuntimeReady: true);
    WorkbenchChatSendController sender = CreateSender(
      chat,
      (_, record, _) =>
      {
        saved = record;
        return Task.FromResult(Saved(record));
      });
    List<WorkbenchChatSendProgressKind> progress = [];

    WorkbenchChatSendResult result = await sender.SendAsync(
      "Explain this.",
      "New chat",
      "Local model",
      AppSettings.Default,
      update => progress.Add(update.Kind));

    Xunit.Assert.True(result.OperationAccepted);
    Xunit.Assert.True(result.ShouldRefreshHistory);
    Xunit.Assert.Equal("Chat saved.", result.StatusMessage);
    Xunit.Assert.Equal(2, chat.Messages.Count);
    Xunit.Assert.Equal("response", chat.Messages[1].Content);
    Xunit.Assert.NotNull(saved);
    Xunit.Assert.Equal(
      [
        WorkbenchChatSendProgressKind.OperationStarted,
        WorkbenchChatSendProgressKind.CompletionStarted,
        WorkbenchChatSendProgressKind.CompletionAdded,
      ],
      progress);
  }

  [Xunit.Fact]
  public async Task SendAsync_CancellationIsObservedWithoutSavingPartialConversation()
  {
    CancelableChatService service = new();
    int saveCount = 0;
    await using WorkbenchChatController chat = new(_ => service);
    chat.SetReadiness(isInstalled: true, isRuntimeReady: true);
    WorkbenchChatSendController sender = CreateSender(
      chat,
      (_, record, _) =>
      {
        saveCount++;
        return Task.FromResult(Saved(record));
      });

    Task<WorkbenchChatSendResult> send = sender.SendAsync(
      "Long request",
      "New chat",
      "Local model",
      AppSettings.Default);
    await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.Equal(WorkbenchChatOperationKind.Completion, chat.CancelActiveOperation());
    WorkbenchChatSendResult result = await send.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.Equal("Response stopped.", result.StatusMessage);
    Xunit.Assert.Equal(0, saveCount);
    Xunit.Assert.Single(chat.Messages);
  }

  [Xunit.Fact]
  public async Task SendAsync_ExpectedFailureAddsRetryMessageAndRefreshesReadiness()
  {
    RecordingDiagnostics diagnostics = new();
    int readinessChecks = 0;
    await using WorkbenchChatController chat = new(_ => new FailingChatService());
    chat.SetReadiness(isInstalled: true, isRuntimeReady: true);
    WorkbenchChatSendController sender = new(
      chat,
      (_, _) =>
      {
        readinessChecks++;
        return Task.FromResult(Ready());
      },
      (_, record, _) => Task.FromResult(Saved(record)),
      diagnostics);

    WorkbenchChatSendResult result = await sender.SendAsync(
      "Explain this.",
      "New chat",
      "Local model",
      AppSettings.Default);

    Xunit.Assert.True(result.OperationAccepted);
    Xunit.Assert.Contains("Local model failed", result.StatusMessage, StringComparison.Ordinal);
    Xunit.Assert.Equal(2, chat.Messages.Count);
    Xunit.Assert.Contains("app error, not a model response", chat.Messages[1].Content, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Continue", chat.Messages[1].Content, StringComparison.Ordinal);
    Xunit.Assert.Equal(1, readinessChecks);
    Xunit.Assert.Single(diagnostics.Warnings);
  }

  [Xunit.Fact]
  public async Task SendAsync_ReadinessRefreshFailureDoesNotReplaceOriginalChatFailure()
  {
    RecordingDiagnostics diagnostics = new();
    await using WorkbenchChatController chat = new(_ => new FailingChatService());
    chat.SetReadiness(isInstalled: true, isRuntimeReady: true);
    WorkbenchChatSendController sender = new(
      chat,
      (_, _) => Task.FromException<WorkbenchChatModelReadinessState>(
        new InvalidOperationException("readiness probe failed")),
      (_, record, _) => Task.FromResult(Saved(record)),
      diagnostics);

    WorkbenchChatSendResult result = await sender.SendAsync(
      "Explain this.",
      "New chat",
      "Local model",
      AppSettings.Default);

    Xunit.Assert.True(result.OperationAccepted);
    Xunit.Assert.Contains("Retry or see Diagnostics", result.StatusMessage, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("worker timed out", result.StatusMessage, StringComparison.Ordinal);
    Xunit.Assert.Equal(2, diagnostics.Warnings.Count);
    Xunit.Assert.Contains("readiness probe failed", diagnostics.Warnings[1], StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task SendAsync_HoldsOperationLockWhileRefreshingReadiness()
  {
    TaskCompletionSource<bool> readinessStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<WorkbenchChatModelReadinessState> readiness = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await using WorkbenchChatController chat = new(_ => new ImmediateChatService());
    WorkbenchChatSendController sender = new(
      chat,
      (_, _) =>
      {
        readinessStarted.TrySetResult(true);
        return readiness.Task;
      },
      (_, record, _) => Task.FromResult(Saved(record)),
      new RecordingDiagnostics());

    Task<WorkbenchChatSendResult> send = sender.SendAsync(
      "Explain this.",
      "New chat",
      "Local model",
      AppSettings.Default);
    await readinessStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.True(chat.IsBusy);
    Xunit.Assert.Null(chat.TryBeginOperation(WorkbenchChatOperationKind.LocalReply));

    readiness.SetResult(Ready());
    WorkbenchChatSendResult result = await send.WaitAsync(TimeSpan.FromSeconds(2));
    Xunit.Assert.True(result.ShouldRefreshHistory);
    Xunit.Assert.False(chat.IsBusy);
  }

  [Xunit.Fact]
  public async Task SendAsync_StaleCompletionCannotMutateReplacementConversation()
  {
    ControlledChatService service = new();
    int saveCount = 0;
    await using WorkbenchChatController chat = new(_ => service);
    chat.SetReadiness(isInstalled: true, isRuntimeReady: true);
    WorkbenchChatIdentity source = chat.CaptureIdentity();
    WorkbenchChatSendController sender = CreateSender(
      chat,
      (_, record, _) =>
      {
        saveCount++;
        return Task.FromResult(Saved(record));
      });

    Task<WorkbenchChatSendResult> send = sender.SendAsync(
      "Long request",
      "New chat",
      "Local model",
      AppSettings.Default);
    await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    chat.NewChat();
    service.Completion.SetResult(new ChatCompletionResult("old response", "fake", "fake", TimeSpan.Zero));

    WorkbenchChatSendResult result = await send.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.False(chat.IsCurrent(source));
    Xunit.Assert.Empty(chat.Messages);
    Xunit.Assert.Equal(0, saveCount);
    Xunit.Assert.Contains("no longer open", result.StatusMessage, StringComparison.Ordinal);
  }

  private static WorkbenchChatSendController CreateSender(
    WorkbenchChatController chat,
    Func<AppSettings, ChatHistoryRecord, CancellationToken, Task<WorkbenchHistoryMutationResult>> save) =>
    new(chat, (_, _) => Task.FromResult(Ready()), save, new RecordingDiagnostics());

  private static WorkbenchChatModelReadinessState Ready() => new(
    IsInstalled: true,
    IsRuntimeReady: true,
    Progress: 100,
    Detail: "Ready.",
    Status: "Ready.");

  private static WorkbenchHistoryMutationResult Saved(ChatHistoryRecord record) => new(
    HistoryCommandStatus.Succeeded,
    "Chat saved.",
    ChatRecord: record,
    AffectedCount: 1);

  private sealed class ImmediateChatService : IChatCompletionService
  {
    public Task<ChatCompletionResult> CompleteAsync(
      ChatCompletionRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(new ChatCompletionResult("response", "fake", "fake", TimeSpan.Zero));
  }

  private sealed class CancelableChatService : IChatCompletionService
  {
    public TaskCompletionSource<bool> Started { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ChatCompletionResult> CompleteAsync(
      ChatCompletionRequest request,
      CancellationToken cancellationToken = default)
    {
      Started.TrySetResult(true);
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      throw new InvalidOperationException("Unreachable after cancellation.");
    }
  }

  private sealed class ControlledChatService : IChatCompletionService
  {
    public TaskCompletionSource<bool> Started { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<ChatCompletionResult> Completion { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ChatCompletionResult> CompleteAsync(
      ChatCompletionRequest request,
      CancellationToken cancellationToken = default)
    {
      Started.TrySetResult(true);
      return Completion.Task.WaitAsync(cancellationToken);
    }
  }

  private sealed class FailingChatService : IChatCompletionService
  {
    public Task<ChatCompletionResult> CompleteAsync(
      ChatCompletionRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromException<ChatCompletionResult>(new TimeoutException("worker timed out"));
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<string> Warnings { get; } = [];

    public void Info(string message)
    {
    }

    public void Warning(string message) => Warnings.Add(message);

    public void Error(string message, Exception? exception = null)
    {
    }
  }
}
