using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface ITextInsertionService
{
  Task<InsertionResult> InsertAsync(
    string text,
    InsertionMethod preferredMethod,
    bool restoreClipboard,
    CancellationToken cancellationToken = default);
}
