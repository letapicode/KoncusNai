using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal sealed class DictationHistoryChangeNotifier
{
  public event EventHandler<DictationHistoryRecord>? RecordAdded;

  public void PublishRecordAdded(DictationHistoryRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);
    RecordAdded?.Invoke(this, record.Normalize());
  }
}
