using System;
using System.Collections.Generic;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class HistorySearchFilterTests
{
  [Xunit.Fact]
  public void FilterLatestDictationSessions_WhenQueryMatchesOlderSessionRecord_ReturnsLatestRecord()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    DictationHistoryRecord olderMatch = CreateDictationRecord(
      now.AddMinutes(-10),
      "session-a",
      "quarterly budget review",
      "quarterly budget review");
    DictationHistoryRecord latestSameSession = CreateDictationRecord(
      now,
      "session-a",
      "follow up",
      "follow up");
    DictationHistoryRecord unrelated = CreateDictationRecord(
      now.AddMinutes(-1),
      "session-b",
      "travel plan",
      "travel plan");

    IReadOnlyList<DictationHistoryRecord> results = HistorySearchFilter.FilterLatestDictationSessions(
      [olderMatch, latestSameSession, unrelated],
      "budget");

    DictationHistoryRecord result = Xunit.Assert.Single(results);
    Xunit.Assert.Equal(latestSameSession.EntryId, result.EntryId);
  }

  [Xunit.Fact]
  public void FilterLatestDictationSessions_RequiresAllSearchTerms()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    DictationHistoryRecord first = CreateDictationRecord(now, "session-a", "alpha beta", "alpha beta");
    DictationHistoryRecord second = CreateDictationRecord(now.AddMinutes(-1), "session-b", "alpha", "alpha");

    IReadOnlyList<DictationHistoryRecord> results = HistorySearchFilter.FilterLatestDictationSessions(
      [first, second],
      "alpha beta");

    DictationHistoryRecord result = Xunit.Assert.Single(results);
    Xunit.Assert.Equal(first.EntryId, result.EntryId);
  }

  [Xunit.Fact]
  public void FilterChats_SearchesMessageContentAndTitle()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    ChatHistoryRecord meetingChat = CreateChatRecord(
      "chat-a",
      "Planning",
      now,
      "Summarize the release blockers",
      "The installer task is still open.");
    ChatHistoryRecord unrelated = CreateChatRecord(
      "chat-b",
      "Notes",
      now.AddMinutes(1),
      "What is next?",
      "Nothing urgent.");

    IReadOnlyList<ChatHistoryRecord> byMessage = HistorySearchFilter.FilterChats(
      [meetingChat, unrelated],
      "installer");
    IReadOnlyList<ChatHistoryRecord> byTitle = HistorySearchFilter.FilterChats(
      [meetingChat, unrelated],
      "planning");

    Xunit.Assert.Equal("chat-a", Xunit.Assert.Single(byMessage).ConversationId);
    Xunit.Assert.Equal("chat-a", Xunit.Assert.Single(byTitle).ConversationId);
  }

  [Xunit.Fact]
  public void AudioFileTranscriptionImporter_RecognizesSupportedAudioExtensions()
  {
    Xunit.Assert.True(AudioFileTranscriptionImporter.IsSupportedAudioFile("sample.WAV"));
    Xunit.Assert.True(AudioFileTranscriptionImporter.IsSupportedAudioFile("sample.m4a"));
    Xunit.Assert.False(AudioFileTranscriptionImporter.IsSupportedAudioFile("notes.txt"));
  }

  private static DictationHistoryRecord CreateDictationRecord(
    DateTimeOffset createdUtc,
    string sessionId,
    string rawTranscript,
    string finalText)
  {
    return new DictationHistoryRecord(
      CreatedUtc: createdUtc,
      ProfileId: "default",
      TranscriptionProviderId: TranscriptionProviderIds.CohereLocal,
      TranscriptionModelId: "cohere-transcribe-03-2026",
      RawTranscript: rawTranscript,
      FinalText: finalText,
      TranscriptionDuration: TimeSpan.FromMilliseconds(100),
      TextTransformationDuration: TimeSpan.Zero,
      TotalPipelineDuration: TimeSpan.FromMilliseconds(100),
      EntryId: Guid.NewGuid().ToString("N"),
      SessionId: sessionId);
  }

  private static ChatHistoryRecord CreateChatRecord(
    string conversationId,
    string title,
    DateTimeOffset now,
    string prompt,
    string answer)
  {
    return new ChatHistoryRecord(
      conversationId,
      title,
      now,
      now.AddSeconds(1),
      ChatProviderIds.GemmaLocal,
      "gemma-4-E2B-it",
      [
        new ChatMessage(ChatMessageRoles.User, prompt, now),
        new ChatMessage(ChatMessageRoles.Assistant, answer, now.AddSeconds(1)),
      ]);
  }
}
