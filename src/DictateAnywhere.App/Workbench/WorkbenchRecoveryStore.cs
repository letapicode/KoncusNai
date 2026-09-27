using System;
using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.App.Workbench;

internal sealed record WorkbenchRecovery(Guid Id, string Text, string Message, bool Saved);

/// <summary>Window-lifetime recovery; unsaved records survive dismissal and later recordings.</summary>
internal sealed class WorkbenchRecoveryStore
{
  private readonly Dictionary<Guid, WorkbenchRecovery> records = new();
  private IReadOnlyList<WorkbenchRecovery> pending = Array.Empty<WorkbenchRecovery>();
  internal IReadOnlyList<WorkbenchRecovery> Pending => pending;
  internal WorkbenchRecovery? Current { get; private set; }
  internal WorkbenchRecovery Add(string text, string message, bool saved)
  {
    WorkbenchRecovery record = new(Guid.NewGuid(), text, message, saved);
    records.Add(record.Id, record);
    pending = records.Values.ToArray();
    Current = record;
    return record;
  }
  internal bool Select(Guid id)
  {
    if (!records.TryGetValue(id, out WorkbenchRecovery? record)) return false;
    Current = record;
    return true;
  }
  internal void Hide() => Current = null;
  internal bool Dismiss(Guid id, bool copied = false)
  {
    if (Current?.Id != id) return false;
    if (copied || Current.Saved)
    {
      records.Remove(id);
      pending = records.Values.ToArray();
    }
    Current = null;
    return true;
  }
}
