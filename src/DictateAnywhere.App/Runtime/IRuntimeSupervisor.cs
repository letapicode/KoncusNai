using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Runtime;

public interface IRuntimeSupervisor
{
  bool IsRunning { get; }

  Task StartAsync(CancellationToken cancellationToken = default);
}
