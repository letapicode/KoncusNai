using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal interface IDictationHistoryStore : IDictationHistoryRecorder
{
  Task<DictationHistoryRecord?> ReadLatestAsync(CancellationToken cancellationToken = default);

  Task<IReadOnlyList<DictationHistoryRecord>> ReadRecentAsync(
    int limit,
    CancellationToken cancellationToken = default);

  Task<bool> UpdateAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default);

  Task<int> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}