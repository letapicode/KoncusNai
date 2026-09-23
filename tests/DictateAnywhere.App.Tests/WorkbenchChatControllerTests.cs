using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "WorkbenchChatController owns and disposes completion services produced by the test factory.")]
public sealed class WorkbenchChatControllerTests
{
  [Xunit.Fact]
  public async Task Retry_ExcludesAppErrorsFromModelContext_WithoutRewritingDisplayedHistory()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    await using WorkbenchChatController controller = new(_ => new FakeChatService());
    controller.BeginCompletion("what is an algorithm", now);
    controller.AddMessage(new ChatMessage(ChatMessageRoles.Assistant,
      "I couldn’t finish that local response after 8s. Please send **Continue** and I’ll retry with a smaller, focused part of the answer.", now));
    controller.AddFailure(TimeSpan.FromSeconds(8), now);
    ChatHistoryRecord saved = controller.CreateHistoryRecord("Test");
    controller.Load(saved);
    ChatCompletionRequest request = controller.BeginCompletion("Please retry my question.", now);
    Xunit.Assert.Equal(4, controller.Messages.Count);
    Xunit.Assert.DoesNotContain(request.Messages, message => message.Role == ChatMessageRoles.Assistant);
    Xunit.Assert.Contains(request.Messages, message => message.Content == "what is an algorithm");
    Xunit.Assert.Equal(3, saved.Messages.Count);
  }

  [Xunit.Fact]
  public async Task LoadAndCreateHistoryRecord_PreserveConversationIdentityAndCreatedTime()
  {
    DateTimeOffset created = DateTimeOffset.UtcNow.AddHours(-1);
    ChatHistoryRecord saved = new(
      "conversation-1",
      "Saved chat",
      created,
      created.AddMinutes(1),
      ChatProviderIds.GemmaLocal,
      "model-a",
      [new ChatMessage(ChatMessageRoles.User, "hello", created)]);
    await using WorkbenchChatController controller = new(_ => new FakeChatService());

    controller.Load(saved);
    controller.AddMessage(new ChatMessage(ChatMessageRoles.Assistant, "hi", created.AddMinutes(2)));
    ChatHistoryRecord updated = controller.CreateHistoryRecord("Renamed chat");

    Xunit.Assert.Equal(saved.ConversationId, updated.ConversationId);
    Xunit.Assert.Equal(saved.CreatedUtc, updated.CreatedUtc);
    Xunit.Assert.Equal("Renamed chat", updated.Title);
    Xunit.Assert.Equal(2, updated.Messages.Count);
  }

  [Xunit.Fact]
  public async Task PendingFiles_AreDeduplicatedAndMergedOnce()
  {
    await using WorkbenchChatController controller = new(_ => new FakeChatService());
    controller.AddMessage(new ChatMessage(ChatMessageRoles.User, "question", DateTimeOffset.UtcNow));
    controller.AddPendingFile(ChatFileAttachment.Create("C:\\docs\\notes.txt", "first"));
    controller.AddPendingFile(ChatFileAttachment.Create("C:\\docs\\notes.txt", "replacement"));

    controller.MergePendingFileContext(DateTimeOffset.UtcNow);
    int messageCount = controller.Messages.Count;
    controller.MergePendingFileContext(DateTimeOffset.UtcNow);

    Xunit.Assert.Empty(controller.PendingFiles);
    Xunit.Assert.Equal(messageCount, controller.Messages.Count);
    ChatMessage context = Xunit.Assert.Single(controller.Messages, ChatFileContext.IsFileContextMessage);
    Xunit.Assert.Contains("replacement", context.Content, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("first", context.Content, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task BeginCompletion_OwnsPendingContextAndUserMessageMutation()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    await using WorkbenchChatController controller = new(_ => new FakeChatService());
    controller.AddPendingFile(ChatFileAttachment.Create("C:\\docs\\notes.txt", "bounded context"));

    ChatCompletionRequest request = controller.BeginCompletion("Summarize this.", now);

    Xunit.Assert.Empty(controller.PendingFiles);
    Xunit.Assert.Equal(2, controller.Messages.Count);
    Xunit.Assert.True(ChatFileContext.IsFileContextMessage(controller.Messages[0]));
    Xunit.Assert.Equal("Summarize this.", controller.Messages[1].Content);
    Xunit.Assert.StartsWith("You are Koncus Nai,", request.Messages[0].Content, StringComparison.Ordinal);
    Xunit.Assert.Contains(controller.Messages[0].Content, request.Messages[0].Content, StringComparison.Ordinal);
    Xunit.Assert.Equal(controller.Messages[1], request.Messages[1]);
    Xunit.Assert.DoesNotContain("You are Koncus Nai,", controller.CreateHistoryRecord("Notes").Messages[0].Content, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task LocalGreetingCompletionAndFailure_AreMutatedByConversationOwner()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    await using WorkbenchChatController controller = new(_ => new FakeChatService());

    bool greeted = controller.TryAddLocalGreeting("hello", now);
    controller.AddCompletion(
      new ChatCompletionResult("partial", "fake", "fake", TimeSpan.Zero, WasTruncated: true),
      now.AddSeconds(1));
    controller.AddFailure(TimeSpan.FromSeconds(3), now.AddSeconds(2));

    Xunit.Assert.True(greeted);
    Xunit.Assert.Equal(4, controller.Messages.Count);
    Xunit.Assert.Contains("More available", controller.Messages[2].Content, StringComparison.Ordinal);
    Xunit.Assert.Contains("after 3", controller.Messages[3].Content, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task GetCompletionServiceAsync_ReusesSelectionAndDisposesOnSelectionChange()
  {
    List<FakeChatService> services = [];
    await using WorkbenchChatController controller = new(_ => Add(services, new FakeChatService()));

    IChatCompletionService first = await controller.GetCompletionServiceAsync();
    IChatCompletionService reused = await controller.GetCompletionServiceAsync();
    controller.SelectModel(new ChatModelSelection(ChatProviderIds.GemmaLocal, "different-model"));
    IChatCompletionService replacement = await controller.GetCompletionServiceAsync();

    Xunit.Assert.Same(first, reused);
    Xunit.Assert.NotSame(first, replacement);
    Xunit.Assert.True(services[0].Disposed);
    Xunit.Assert.False(services[1].Disposed);
  }

  private static T Add<T>(ICollection<T> collection, T item)
  {
    collection.Add(item);
    return item;
  }

  private sealed class FakeChatService : IChatCompletionService, IAsyncDisposable
  {
    public bool Disposed { get; private set; }

    public Task<ChatCompletionResult> CompleteAsync(
      ChatCompletionRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(new ChatCompletionResult("response", "fake", "fake", TimeSpan.Zero));

    public ValueTask DisposeAsync()
    {
      Disposed = true;
      return ValueTask.CompletedTask;
    }
  }
}
