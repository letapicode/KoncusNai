using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface IBenchmarkService
{
  Task<BenchmarkResult> RunAsync(string? languageScope = null, CancellationToken cancellationToken = default);

  Task<BenchmarkResult?> LoadLastResultAsync(CancellationToken cancellationToken = default);
}
