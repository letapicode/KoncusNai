using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(LastDictationSessionCacheCollection.Name)]
public sealed class CachingDictationHistoryRecorderTests : IDisposable
{
  public CachingDictationHistoryRecorderTests()
  {
    LastDictationSessionCache.Clear();
  }

  public void Dispose()
  {
    LastDictationSessionCache.Clear();
  }

  [Xunit.Fact]
  public async Task RecordAsync_PublishesOnlyAfterPersistenceSucceeds()
  {
    RecordingHistoryStore store = new();
    DictationHistoryChangeNotifier notifier = new();
    CachingDictationHistoryRecorder recorder = new(store, notifier);
    DictationHistoryRecord? published = null;
    notifier.RecordAdded += (_, record) => published = record;
    DictationHistoryRecord expected = CreateRecord("saved text").Normalize();

    await recorder.RecordAsync(expected);

    Xunit.Assert.Equal(expected, store.Recorded);
    Xunit.Assert.Equal(expected, published);
  }

  [Xunit.Fact]
  public async Task RecordAsync_DoesNotPublishWhenPersistenceFails()
  {
    DictationHistoryChangeNotifier notifier = new();
    CachingDictationHistoryRecorder recorder = new(new FailingHistoryStore(), notifier);
    int publishCount = 0;
    notifier.RecordAdded += (_, _) => publishCount++;

    await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => recorder.RecordAsync(CreateRecord("not saved")));

    Xunit.Assert.Equal(0, publishCount);
  }

  private static DictationHistoryRecord CreateRecord(string text)
  {
    return new DictationHistoryRecord(
      CreatedUtc: DateTimeOffset.UtcNow,
      ProfileId: "default",
      TranscriptionProviderId: TranscriptionProviderIds.CohereLocal,
      TranscriptionModelId: "cohere-transcribe-03-2026",
      RawTranscript: text,
      FinalText: text,
      TranscriptionDuration: TimeSpan.Zero,
      TextTransformationDuration: TimeSpan.Zero,
      TotalPipelineDuration: TimeSpan.Zero,
      Source: "test");
  }

  private sealed class RecordingHistoryStore : IDictationHistoryRecorder
  {
    public DictationHistoryRecord? Recorded { get; private set; }

    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default)
    {
      Recorded = record;
      return Task.CompletedTask;
    }
  }

  private sealed class FailingHistoryStore : IDictationHistoryRecorder
  {
    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default)
    {
      throw new InvalidOperationException("History is locked.");
    }
  }
}
