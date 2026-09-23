using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Productivity;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(LastDictationSessionCacheCollection.Name)]
public sealed class LastDictationRetryResolverTests : IDisposable
{
  public void Dispose()
  {
    LastDictationSessionCache.Clear();
  }

  [Xunit.Fact]
  public async Task ResolveAsync_ReturnsSameSessionRecordBeforeReadingDisk()
  {
    DictationHistoryRecord record = CreateRecord(DateTimeOffset.UtcNow, "same session");
    LastDictationSessionCache.Store(record);

    LastDictationRetryResolution resolution = await LastDictationRetryResolver.ResolveAsync(
      AppSettings.Default with { LastDictationRetryWindowSeconds = 60 },
      _ => throw new InvalidOperationException("Persistent history should not be read."));

    Xunit.Assert.True(resolution.Success);
    Xunit.Assert.Equal("same-session", resolution.Source);
    Xunit.Assert.Equal("same session", resolution.Record?.FinalText);
  }

  [Xunit.Fact]
  public async Task ResolveAsync_ReturnsExpiredMessage_WhenCacheExpiredAndHistoryEmpty()
  {
    LastDictationSessionCache.Store(CreateRecord(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5), "expired"));

    LastDictationRetryResolution resolution = await LastDictationRetryResolver.ResolveAsync(
      AppSettings.Default with { LastDictationRetryWindowSeconds = 60 },
      _ => Task.FromResult<DictationHistoryRecord?>(null));

    Xunit.Assert.False(resolution.Success);
    Xunit.Assert.Contains("expired", resolution.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task ResolveAsync_ReturnsHistoryRecord_WhenCacheMissing()
  {
    DictationHistoryRecord historyRecord = CreateRecord(DateTimeOffset.UtcNow - TimeSpan.FromHours(2), "from history");

    LastDictationRetryResolution resolution = await LastDictationRetryResolver.ResolveAsync(
      AppSettings.Default,
      _ => Task.FromResult<DictationHistoryRecord?>(historyRecord));

    Xunit.Assert.True(resolution.Success);
    Xunit.Assert.Equal("local-history", resolution.Source);
    Xunit.Assert.Equal("from history", resolution.Record?.FinalText);
  }

  [Xunit.Fact]
  public async Task ResolveAsync_ReturnsNoHistoryMessage_WhenNoCacheExists()
  {
    LastDictationRetryResolution resolution = await LastDictationRetryResolver.ResolveAsync(
      AppSettings.Default,
      _ => Task.FromResult<DictationHistoryRecord?>(null));

    Xunit.Assert.False(resolution.Success);
    Xunit.Assert.Contains("history", resolution.Message, StringComparison.OrdinalIgnoreCase);
  }

  private static DictationHistoryRecord CreateRecord(DateTimeOffset createdUtc, string finalText)
  {
    return new DictationHistoryRecord(
      CreatedUtc: createdUtc,
      ProfileId: "default",
      TranscriptionProviderId: TranscriptionProviderIds.CohereLocal,
      TranscriptionModelId: "cohere-transcribe-03-2026",
      RawTranscript: finalText,
      FinalText: finalText,
      TranscriptionDuration: TimeSpan.Zero,
      TextTransformationDuration: TimeSpan.Zero,
      TotalPipelineDuration: TimeSpan.Zero);
  }
}
