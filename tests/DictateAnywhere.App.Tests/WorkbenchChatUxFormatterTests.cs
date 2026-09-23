using System;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchChatUxFormatterTests
{
  [Xunit.Fact]
  public void RequestProgress_HidesForQuickReplies_ThenAdvancesThroughStages()
  {
    Xunit.Assert.False(ChatRequestProgress.ShouldShow(TimeSpan.FromMilliseconds(549)));
    Xunit.Assert.Equal(string.Empty, ChatRequestProgress.GetStage(TimeSpan.FromMilliseconds(549)));
    Xunit.Assert.Equal("Thinking", ChatRequestProgress.GetStage(TimeSpan.FromMilliseconds(550)));
    Xunit.Assert.Equal("Thinking", ChatRequestProgress.GetStage(TimeSpan.FromMilliseconds(1800)));
    Xunit.Assert.Equal("Working", ChatRequestProgress.GetStage(TimeSpan.FromMilliseconds(3750)));
    Xunit.Assert.Equal("Almost ready", ChatRequestProgress.GetStage(TimeSpan.FromSeconds(30)));
    Xunit.Assert.Equal(0, ChatRequestProgress.GetLetterIndex(TimeSpan.FromMilliseconds(550), 8));
    Xunit.Assert.Equal(1, ChatRequestProgress.GetLetterIndex(TimeSpan.FromMilliseconds(910), 8));
  }

  [Xunit.Fact]
  public void DurationFormatter_UsesHoursMinutesSeconds_WhenRequestRunsLong()
  {
    Xunit.Assert.Equal("42s", ChatRequestDurationFormatter.Format(TimeSpan.FromSeconds(42.8)));
    Xunit.Assert.Equal("1m 05s", ChatRequestDurationFormatter.Format(TimeSpan.FromSeconds(65.9)));
    Xunit.Assert.Equal("2h 03m 04s", ChatRequestDurationFormatter.Format(new TimeSpan(2, 3, 4)));
  }

  [Xunit.Fact]
  public void ChatTitleFormatter_UsesFewWordsFromPrompt_WhenTitleIsPlaceholder()
  {
    string title = ChatHistoryTitleFormatter.ResolveTitle(
      ChatHistoryTitleFormatter.PlaceholderTitle,
      [
        new ChatMessage(
          ChatMessageRoles.User,
          "Rewrite the following and fix grammar and flow: All right. What's this interesting idea?",
          DateTimeOffset.UtcNow),
      ]);

    Xunit.Assert.Equal("All right What's this interesting idea", title);
  }

  [Xunit.Fact]
  public void ChatTitleFormatter_DoesNotPrefixSidebarTitleWithDate()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    ChatHistoryRecord record = new(
      "chat-a",
      "Partnership negotiation summary",
      now,
      now,
      ChatProviderIds.GemmaLocal,
      "gemma-4-E2B-it",
      [
        new ChatMessage(ChatMessageRoles.User, "hello", now),
      ]);

    string title = ChatHistoryTitleFormatter.FormatSidebarTitle(record);

    Xunit.Assert.Equal("Partnership negotiation summary", title);
  }

  [Xunit.Fact]
  public void ChatTitleFormatter_SentenceCasesAutomaticallyGeneratedTitles()
  {
    string title = ChatHistoryTitleFormatter.ResolveTitle(
      ChatHistoryTitleFormatter.PlaceholderTitle,
      [
        new ChatMessage(ChatMessageRoles.User, "hello from notype", DateTimeOffset.UtcNow),
      ]);

    Xunit.Assert.Equal("Hello from notype", title);
  }

  [Xunit.Fact]
  public void ChatTitleFormatter_PreservesIntentionalMixedCaseAndManualTitles()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;

    string generated = ChatHistoryTitleFormatter.ResolveTitle(
      ChatHistoryTitleFormatter.PlaceholderTitle,
      [new ChatMessage(ChatMessageRoles.User, "iPhone setup notes", now)]);
    string manual = ChatHistoryTitleFormatter.ResolveTitle(
      "lowercase by choice",
      [new ChatMessage(ChatMessageRoles.User, "ignored", now)]);

    Xunit.Assert.Equal("iPhone setup notes", generated);
    Xunit.Assert.Equal("lowercase by choice", manual);
  }

  [Xunit.Fact]
  public void DictationTitleFormatter_DoesNotPrefixSidebarTitleWithDateOrSource()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    DictationHistoryRecord record = new DictationHistoryRecord(
      CreatedUtc: now,
      ProfileId: "default",
      TranscriptionProviderId: TranscriptionProviderIds.CohereLocal,
      TranscriptionModelId: "base",
      RawTranscript: "raw",
      FinalText: "Meeting recap and next steps",
      TranscriptionDuration: TimeSpan.Zero,
      TextTransformationDuration: TimeSpan.Zero,
      TotalPipelineDuration: TimeSpan.Zero,
      Source: "audio-file").Normalize();

    string title = DictationHistoryTitleFormatter.FormatSidebarTitle(record);

    Xunit.Assert.Equal("Meeting recap and next steps", title);
  }
}
