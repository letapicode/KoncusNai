using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Settings;

/// <summary>Reference-counted path leases include waiters; no entry or gate outlives its users.</summary>
internal static class SettingsFileAccess
{
  private static readonly object Sync = new();
  private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

  internal static async Task<IDisposable> AcquireAsync(string path, CancellationToken token)
  {
    Entry entry;
    lock (Sync)
    {
      if (!Entries.TryGetValue(path, out entry!)) Entries.Add(path, entry = new Entry());
      entry.Users++;
    }
    try
    {
      await entry.Gate.WaitAsync(token).ConfigureAwait(false);
      return new Lease(() => Release(path, entry, true));
    }
    catch { Release(path, entry, false); throw; }
  }

  private static void Release(string path, Entry entry, bool acquired)
  {
    if (acquired) entry.Gate.Release();
    lock (Sync)
    {
      if (--entry.Users != 0) return;
      Entries.Remove(path);
      entry.Gate.Dispose();
    }
  }

  private sealed class Entry
  {
    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal int Users;
  }

  private sealed class Lease(Action release) : IDisposable
  {
    private Action? releaseAction = release;
    public void Dispose() => Interlocked.Exchange(ref releaseAction, null)?.Invoke();
  }
}
