using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Composition;

public interface IAudioInputDeviceService
{
  Task<IReadOnlyList<AudioInputDeviceOption>> GetInputDevicesAsync(CancellationToken cancellationToken = default);
}
