using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface ITextTransformationService
{
  Task<TextTransformationResult> TransformAsync(
    TextTransformationRequest request,
    CancellationToken cancellationToken = default);
}
