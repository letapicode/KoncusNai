using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Insertion;

public interface IElevatedInsertionBridge
{
  Task<InsertionResult> InsertAsync(
    string text,
    InsertionMethod preferredMethod,
    bool restoreClipboard,
    CancellationToken cancellationToken = default);

  Task<InsertionResult> InsertAsync(
    string text,
    InsertionMethod preferredMethod,
    bool restoreClipboard,
    InsertionTargetIdentity target,
    CancellationToken cancellationToken = default) => Task.FromResult(InsertionResult.Blocked(
      preferredMethod, "This helper cannot validate the insertion target.", InsertionBlockReason.TargetChanged));
}
