using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface IDictationHistoryRecorder
{
  Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default);
}
