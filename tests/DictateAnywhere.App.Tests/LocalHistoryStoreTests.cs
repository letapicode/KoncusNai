using System;
using System.IO;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class LocalHistoryStoreTests
{
  [Xunit.Fact]
  public async Task AppendAfterValidRecordWithoutNewlineRetainsBothRecords()
  {
    using TempDirectoryScope root = new();
    string path = Path.Combine(root.DirectoryPath, "dictations.jsonl");
    LocalDictationHistoryStore store = new(path);
    await store.RecordAsync(CreateDictation("first", "original"));
    string original = (await File.ReadAllTextAsync(path)).TrimEnd('\r', '\n');
    await File.WriteAllTextAsync(path, original);
    await store.RecordAsync(CreateDictation("second", "new"));
    Xunit.Assert.Equal(new[] { "second", "first" }, (await store.ReadRecentAsync(10)).Select(record => record.EntryId));
  }

  [Xunit.Fact]
  public async Task SearchScansBeyondRecentWindow_AndLimitsMatchesInsteadOfInput()
  {
    using TempDirectoryScope root = new();
    string dictationPath = Path.Combine(root.DirectoryPath, "dictations.jsonl");
    string chatPath = Path.Combine(root.DirectoryPath, "chats.jsonl");
    LocalDictationHistoryStore dictations = new(dictationPath);
    LocalChatHistoryStore chats = new(chatPath);
    VersionedJsonLinesFile<ChatHistoryRecord> chatFile = new(chatPath);
    await dictations.RecordAsync(CreateDictation("old", "buried needle"));
    await chats.SaveAsync(CreateChat("old", "buried needle"));
    for (int index = 0; index < 220; index++)
    {
      await dictations.RecordAsync(CreateDictation($"new-{index}", "recent"));
      await chatFile.AppendAsync(CreateChat($"new-{index}", "recent").Normalize());
    }
    Xunit.Assert.Equal("old", Xunit.Assert.Single(await dictations.SearchRecentAsync("needle", 1)).EntryId);
    Xunit.Assert.Equal("old", Xunit.Assert.Single(await chats.SearchRecentAsync("needle", 1)).ConversationId);
    Xunit.Assert.Equal(3, (await dictations.SearchRecentAsync("recent", 3)).Count);
    Xunit.Assert.Equal(3, (await chats.SearchRecentAsync("recent", 3)).Count);
    await using HistoryQueryCoordinator query = new((_, search, limit, token) => dictations.SearchRecentAsync(search, limit, token));
    Xunit.Assert.Single((await query.QueryLatestAsync(AppSettings.Default, "needle"))!.Records);
  }

  [Xunit.Theory]
  [Xunit.InlineData("{\"version\":1,\"record\":")]
  [Xunit.InlineData("{\"version\":999,\"record\":{}}")]
  public async Task AppendAfterUnterminatedTail_PreservesTailAndReadsNewRecord(string tail)
  {
    using TempDirectoryScope root = new();
    string path = Path.Combine(root.DirectoryPath, "history.jsonl");
    await File.WriteAllTextAsync(path, tail);
    LocalDictationHistoryStore store = new(path);
    await store.RecordAsync(CreateDictation("first", "readable"));
    await store.RecordAsync(CreateDictation("second", "also readable"));
    Xunit.Assert.Equal(2, (await store.ReadRecentAsync(10)).Count);
    Xunit.Assert.StartsWith(tail + "\n", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task ChatSemanticCorruption_DoesNotPreventUnrelatedMutation_AndPreservesBytes()
  {
    using TempDirectoryScope root = new();
    string path = Path.Combine(root.DirectoryPath, "history.jsonl");
    byte[] corrupt = System.Text.Encoding.UTF8.GetBytes("{\"version\":1,\"record\":{\"conversationId\":\"bad\",\"messages\":[null]}}\n")
      .Concat(new byte[] { 0xff, 0xfe, 10 }).ToArray();
    await File.WriteAllBytesAsync(path, corrupt);
    LocalChatHistoryStore store = new(path);
    await store.SaveAsync(CreateChat("good", "hello"));
    Xunit.Assert.True(await store.RenameAsync("good", "renamed"));
    Xunit.Assert.Single(await store.ReadRecentAsync(10));
    Xunit.Assert.Equal(1, await store.DeleteConversationAsync("good"));
    Xunit.Assert.Equal(corrupt, await File.ReadAllBytesAsync(path));
  }

  [Xunit.Fact]
  public async Task MissingPersistedIdentity_IsNotRegeneratedOnEachRead()
  {
    using TempDirectoryScope root = new();
    string path = Path.Combine(root.DirectoryPath, "history.jsonl");
    const string bad = "{\"version\":1,\"record\":{\"messages\":[]}}";
    await File.WriteAllTextAsync(path, bad);
    LocalChatHistoryStore store = new(path);
    Xunit.Assert.Empty(await store.ReadRecentAsync(10));
    await store.SaveAsync(CreateChat("valid", "hello"));
    Xunit.Assert.Single(await store.ReadRecentAsync(10));
    Xunit.Assert.StartsWith(bad, await File.ReadAllTextAsync(path), StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task DictationHistory_IsPlaintextUnlimitedAndBoundedOnRead()
  {
    using TempDirectoryScope root = new();
    string path = Path.Combine(root.DirectoryPath, "dictation.local.jsonl");
    LocalDictationHistoryStore store = new(path);

    for (int index = 0; index < 120; index++)
    {
      await store.RecordAsync(CreateDictation($"entry-{index}", $"text-{index}"));
    }

    string persisted = await File.ReadAllTextAsync(path);
    Xunit.Assert.Contains("text-119", persisted, StringComparison.Ordinal);
    Xunit.Assert.Equal(120, File.ReadLines(path).Count());
    IReadOnlyList<DictationHistoryRecord> recent = await store.ReadRecentAsync(7);
    Xunit.Assert.Equal(7, recent.Count);
    Xunit.Assert.Equal("text-119", recent[0].FinalText);
    Xunit.Assert.Equal("text-113", recent[^1].FinalText);
  }

  [Xunit.Fact]
  public async Task DictationMutation_PreservesCorruptLinesAndStableIds()
  {
    using TempDirectoryScope root = new();
    string path = Path.Combine(root.DirectoryPath, "dictation.local.jsonl");
    LocalDictationHistoryStore store = new(path);
    DictationHistoryRecord original = CreateDictation("entry-a", "before");
    await store.RecordAsync(original);
    await File.AppendAllTextAsync(path, "not-json" + Environment.NewLine);

    bool updated = await store.UpdateAsync(original with { FinalText = "after" });

    Xunit.Assert.True(updated);
    Xunit.Assert.Contains("not-json", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    DictationHistoryRecord saved = Xunit.Assert.Single(await store.ReadRecentAsync(10));
    Xunit.Assert.Equal("entry-a", saved.EntryId);
    Xunit.Assert.Equal("after", saved.FinalText);
  }

  [Xunit.Fact]
  public async Task DictationHistory_ConcurrentStoreInstancesDoNotLoseWrites()
  {
    using TempDirectoryScope root = new();
    string path = Path.Combine(root.DirectoryPath, "dictation.local.jsonl");
    Task[] writes = Enumerable.Range(0, 32)
      .Select(index => new LocalDictationHistoryStore(path)
        .RecordAsync(CreateDictation($"entry-{index}", $"text-{index}")))
      .ToArray();

    await Task.WhenAll(writes);

    IReadOnlyList<DictationHistoryRecord> records = await new LocalDictationHistoryStore(path).ReadRecentAsync(100);
    Xunit.Assert.Equal(32, records.Count);
    Xunit.Assert.Equal(32, records.Select(static record => record.EntryId).Distinct().Count());
  }

  [Xunit.Fact]
  public async Task ChatHistory_UpsertsRenamesAndDeletesWithoutRetention()
  {
    using TempDirectoryScope root = new();
    string path = Path.Combine(root.DirectoryPath, "chat.local.jsonl");
    LocalChatHistoryStore store = new(path);
    await store.SaveAsync(CreateChat("chat-a", "first"));
    await store.SaveAsync(CreateChat("chat-b", "second"));
    await store.SaveAsync(CreateChat("chat-a", "replacement"));

    Xunit.Assert.Equal(2, File.ReadLines(path).Count());
    Xunit.Assert.True(await store.RenameAsync("chat-a", "Renamed"));
    Xunit.Assert.Equal(1, await store.DeleteConversationAsync("chat-b"));
    ChatHistoryRecord saved = Xunit.Assert.Single(await store.ReadRecentAsync(10));
    Xunit.Assert.Equal("chat-a", saved.ConversationId);
    Xunit.Assert.Equal("Renamed", saved.Title);
    Xunit.Assert.Equal("replacement", saved.Messages[0].Content);
  }

  [Xunit.Fact]
  public async Task ImportMissing_IsIdempotent()
  {
    using TempDirectoryScope root = new();
    string path = Path.Combine(root.DirectoryPath, "dictation.local.jsonl");
    LocalDictationHistoryStore store = new(path);
    DictationHistoryRecord first = CreateDictation("entry-a", "one");
    DictationHistoryRecord second = CreateDictation("entry-b", "two");

    await store.ImportMissingAsync([first, second]);
    await store.ImportMissingAsync([first, second]);

    IReadOnlyList<DictationHistoryRecord> records = await store.ReadRecentAsync(10);
    Xunit.Assert.Equal(2, records.Count);
    Xunit.Assert.Equal(2, File.ReadLines(path).Count());
  }

  private static DictationHistoryRecord CreateDictation(string entryId, string text) => new(
    CreatedUtc: DateTimeOffset.UtcNow,
    ProfileId: "default",
    TranscriptionProviderId: TranscriptionProviderIds.CohereLocal,
    TranscriptionModelId: "cohere-transcribe-03-2026",
    RawTranscript: text,
    FinalText: text,
    TranscriptionDuration: TimeSpan.FromMilliseconds(10),
    TextTransformationDuration: TimeSpan.Zero,
    TotalPipelineDuration: TimeSpan.FromMilliseconds(10),
    EntryId: entryId,
    SessionId: entryId);

  private static ChatHistoryRecord CreateChat(string conversationId, string content)
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    return new ChatHistoryRecord(
      ConversationId: conversationId,
      Title: string.Empty,
      CreatedUtc: now,
      UpdatedUtc: now,
      ProviderId: ChatProviderIds.GemmaLocal,
      ModelId: "gemma-4-E2B-it",
      Messages: [new ChatMessage(ChatMessageRoles.User, content, now)]).Normalize();
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "Notype.LocalHistory.Tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
      if (Directory.Exists(DirectoryPath))
      {
        Directory.Delete(DirectoryPath, recursive: true);
      }
    }
  }
}
