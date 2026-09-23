using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal sealed class CachingDictationHistoryRecorder : IDictationHistoryRecorder
{
  private readonly IDictationHistoryRecorder inner;
  private readonly DictationHistoryChangeNotifier? changeNotifier;

  public CachingDictationHistoryRecorder(
    IDictationHistoryRecorder inner,
    DictationHistoryChangeNotifier? changeNotifier = null)
  {
    this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    this.changeNotifier = changeNotifier;
  }

  public async Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default)
  {
    DictationHistoryRecord normalized = record.Normalize();
    LastDictationSessionCache.Store(normalized);
    await inner.RecordAsync(normalized, cancellationToken).ConfigureAwait(false);
    changeNotifier?.PublishRecordAdded(normalized);
  }
}
