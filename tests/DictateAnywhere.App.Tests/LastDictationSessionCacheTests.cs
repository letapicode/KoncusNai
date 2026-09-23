using System;
using DictateAnywhere.App.History;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(LastDictationSessionCacheCollection.Name)]
public sealed class LastDictationSessionCacheTests : IDisposable
{
  public void Dispose()
  {
    LastDictationSessionCache.Clear();
  }

  [Xunit.Fact]
  public void TryGet_ReturnsSameSessionRecord_WhenUnexpired()
  {
    DictationHistoryRecord record = CreateRecord(DateTimeOffset.UtcNow);

    LastDictationSessionCache.Store(record);

    bool found = LastDictationSessionCache.TryGet(TimeSpan.FromMinutes(15), out DictationHistoryRecord? latest);

    Xunit.Assert.True(found);
    Xunit.Assert.NotNull(latest);
    Xunit.Assert.Equal("final", latest.FinalText);
  }

  [Xunit.Fact]
  public void TryGet_RejectsExpiredRecord()
  {
    LastDictationSessionCache.Store(CreateRecord(DateTimeOffset.UtcNow - TimeSpan.FromHours(1)));

    bool found = LastDictationSessionCache.TryGet(TimeSpan.FromMinutes(15), out DictationHistoryRecord? latest);

    Xunit.Assert.False(found);
    Xunit.Assert.NotNull(latest);
  }

  private static DictationHistoryRecord CreateRecord(DateTimeOffset createdUtc)
  {
    return new DictationHistoryRecord(
      CreatedUtc: createdUtc,
      ProfileId: "default",
      TranscriptionProviderId: TranscriptionProviderIds.CohereLocal,
      TranscriptionModelId: "cohere-transcribe-03-2026",
      RawTranscript: "raw",
      FinalText: "final",
      TranscriptionDuration: TimeSpan.Zero,
      TextTransformationDuration: TimeSpan.Zero,
      TotalPipelineDuration: TimeSpan.Zero);
  }
}
