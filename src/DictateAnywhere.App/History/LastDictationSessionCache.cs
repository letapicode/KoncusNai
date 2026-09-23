using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal static class LastDictationSessionCache
{
  private static readonly object Sync = new();
  private static DictationHistoryRecord? latest;

  public static void Store(DictationHistoryRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);
    lock (Sync)
    {
      latest = record.Normalize();
    }
  }

  public static bool TryGet(TimeSpan maxAge, out DictationHistoryRecord? record)
  {
    lock (Sync)
    {
      record = latest;
    }

    if (record is null)
    {
      return false;
    }

    if (maxAge <= TimeSpan.Zero)
    {
      return true;
    }

    return DateTimeOffset.UtcNow - record.CreatedUtc <= maxAge;
  }

  public static void Clear()
  {
    lock (Sync)
    {
      latest = null;
    }
  }

  /// <summary>Removes the cached record only when it belongs to a confirmed deletion.</summary>
  public static bool ClearIfMatches(IEnumerable<string> sessionIds)
  {
    ArgumentNullException.ThrowIfNull(sessionIds);
    string[] stableSessionIds = sessionIds
      .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
      .ToArray();
    if (stableSessionIds.Length == 0)
    {
      return false;
    }

    lock (Sync)
    {
      if (latest is null)
      {
        return false;
      }

      string cachedSessionId = string.IsNullOrWhiteSpace(latest.SessionId)
        ? latest.EntryId
        : latest.SessionId;
      if (!stableSessionIds.Contains(cachedSessionId, StringComparer.OrdinalIgnoreCase))
      {
        return false;
      }

      latest = null;
      return true;
    }
  }

  /// <summary>Keeps the cache synchronized without promoting an arbitrary older record.</summary>
  public static bool UpdateIfMatches(DictationHistoryRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);
    DictationHistoryRecord normalized = record.Normalize();
    lock (Sync)
    {
      if (latest is null || !Matches(latest, normalized))
      {
        return false;
      }

      latest = normalized;
      return true;
    }
  }

  private static bool Matches(DictationHistoryRecord left, DictationHistoryRecord right)
  {
    string leftSessionId = string.IsNullOrWhiteSpace(left.SessionId) ? left.EntryId : left.SessionId;
    string rightSessionId = string.IsNullOrWhiteSpace(right.SessionId) ? right.EntryId : right.SessionId;
    return string.Equals(leftSessionId, rightSessionId, StringComparison.OrdinalIgnoreCase);
  }
}
