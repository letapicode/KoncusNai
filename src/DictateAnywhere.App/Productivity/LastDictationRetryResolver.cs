using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Productivity;

internal static class LastDictationRetryResolver
{
  public static async Task<LastDictationRetryResolution> ResolveAsync(
    AppSettings settings,
    Func<CancellationToken, Task<DictationHistoryRecord?>> readHistoryLatestAsync,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ArgumentNullException.ThrowIfNull(readHistoryLatestAsync);

    TimeSpan retryWindow = ResolveRetryWindow(settings);
    bool cacheHadUnexpiredRecord = LastDictationSessionCache.TryGet(retryWindow, out DictationHistoryRecord? latest);
    if (cacheHadUnexpiredRecord && latest is not null)
    {
      return LastDictationRetryResolution.Succeeded(latest.Normalize(), "same-session");
    }

    latest = await readHistoryLatestAsync(cancellationToken).ConfigureAwait(false);
    bool expiredCacheEntryExists = LastDictationSessionCache.TryGet(TimeSpan.Zero, out _);
    return latest is null
      ? LastDictationRetryResolution.Failed(expiredCacheEntryExists
        ? "Last dictation retry expired and no saved history entry is available."
        : "No dictation history entry is available yet.")
      : LastDictationRetryResolution.Succeeded(latest.Normalize(), "local-history");
  }

  private static TimeSpan ResolveRetryWindow(AppSettings settings)
  {
    return TimeSpan.FromSeconds(Math.Clamp(settings.LastDictationRetryWindowSeconds, 60, 86_400));
  }
}

internal sealed record LastDictationRetryResolution(
  bool Success,
  string Message,
  string Source,
  DictationHistoryRecord? Record)
{
  public static LastDictationRetryResolution Succeeded(DictationHistoryRecord record, string source)
  {
    ArgumentNullException.ThrowIfNull(record);
    return new LastDictationRetryResolution(true, string.Empty, source, record);
  }

  public static LastDictationRetryResolution Failed(string message)
  {
    return new LastDictationRetryResolution(false, message, string.Empty, null);
  }
}
