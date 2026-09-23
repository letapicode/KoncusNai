using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface IChatCompletionService
{
  Task<ChatCompletionResult> CompleteAsync(
    ChatCompletionRequest request,
    CancellationToken cancellationToken = default);
}
