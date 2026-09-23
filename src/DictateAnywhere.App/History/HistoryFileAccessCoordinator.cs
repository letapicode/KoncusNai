using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.History;

/// <summary>Serializes in-process access to each history file across independent store instances.</summary>
internal static class HistoryFileAccessCoordinator
{
  private static readonly object Sync = new();
  private static readonly Dictionary<string, LockEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

  public static async ValueTask<HistoryFileAccessLease> AcquireAsync(
    string path,
    CancellationToken cancellationToken = default)
  {
    string normalizedPath = NormalizePath(path);
    LockEntry entry;
    lock (Sync)
    {
      if (!Entries.TryGetValue(normalizedPath, out entry!))
      {
        entry = new LockEntry();
        Entries.Add(normalizedPath, entry);
      }

      entry.ReferenceCount++;
    }

    try
    {
      await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      return new HistoryFileAccessLease(
        () => ReleaseReference(normalizedPath, entry, releaseSemaphore: true));
    }
    catch
    {
      ReleaseReference(normalizedPath, entry, releaseSemaphore: false);
      throw;
    }
  }

  internal static bool IsTracked(string path)
  {
    string normalizedPath = NormalizePath(path);
    lock (Sync)
    {
      return Entries.ContainsKey(normalizedPath);
    }
  }

  private static string NormalizePath(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      throw new ArgumentException("History file path must not be empty.", nameof(path));
    }

    return Path.GetFullPath(path);
  }

  private static void ReleaseReference(
    string normalizedPath,
    LockEntry entry,
    bool releaseSemaphore)
  {
    if (releaseSemaphore)
    {
      entry.Semaphore.Release();
    }

    lock (Sync)
    {
      entry.ReferenceCount--;
      if (entry.ReferenceCount != 0)
      {
        return;
      }

      _ = Entries.Remove(normalizedPath);
      entry.Semaphore.Dispose();
    }
  }

  internal sealed class HistoryFileAccessLease : IDisposable
  {
    private Action? release;

    internal HistoryFileAccessLease(Action release)
    {
      this.release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public void Dispose()
    {
      Interlocked.Exchange(ref release, null)?.Invoke();
    }
  }

  private sealed class LockEntry
  {
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    public int ReferenceCount { get; set; }
  }
}
