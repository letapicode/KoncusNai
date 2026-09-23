using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface IUndoInsertionService
{
  Task<UndoInsertionResult> UndoLastInsertionAsync(CancellationToken cancellationToken = default);
}
