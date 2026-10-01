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
  public async Task AlreadyCanceledRecordCannotReplaceCacheOrWriteHistory()
  {
    DictationHistoryRecord previous = CreateRecord("previous").Normalize();
    LastDictationSessionCache.Store(previous);
    RecordingHistoryStore store = new();
    using CancellationTokenSource canceled = new();
    canceled.Cancel();
    CachingDictationHistoryRecorder recorder = new(store);
    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => recorder.RecordAsync(CreateRecord("canceled"), canceled.Token));
    Xunit.Assert.Null(store.Recorded);
    Xunit.Assert.True(LastDictationSessionCache.TryGet(TimeSpan.Zero, out DictationHistoryRecord? retained));
    Xunit.Assert.Equal(previous, retained);
  }

  [Xunit.Fact]
  public void PersistenceThatIgnoresCancellationCannotPublishLateSuccess() => LifecycleTestContext.Run(async context =>
  {
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    context.ReleaseOnTimeout(() => release.TrySetResult());
    RecordingHistoryStore store = new() { Gate = release.Task };
    DictationHistoryChangeNotifier notifier = new();
    int published = 0;
    notifier.RecordAdded += (_, _) => published++;
    using CancellationTokenSource cancellation = new();
    CachingDictationHistoryRecorder recorder = new(store, notifier);
    Task write = recorder.RecordAsync(CreateRecord("already admitted"), cancellation.Token);
    try
    {
      Xunit.Assert.NotNull(store.Recorded);
      cancellation.Cancel();
      Xunit.Assert.False(write.IsCompleted);
    }
    finally { release.TrySetResult(); }
    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
    Xunit.Assert.Equal(0, published);
    // The already-admitted cache/write effect is retained; cancellation is not rollback.
    Xunit.Assert.True(LastDictationSessionCache.TryGet(TimeSpan.Zero, out _));
  });

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
    internal Task Gate { get; init; } = Task.CompletedTask;

    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default)
    {
      Recorded = record;
      return Gate;
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
