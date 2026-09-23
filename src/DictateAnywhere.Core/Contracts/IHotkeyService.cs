using System;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

public interface IHotkeyService : IAsyncDisposable
{
  event EventHandler<HotkeyEventArgs>? HotkeyPressed;

  event EventHandler<HotkeyEventArgs>? HotkeyReleased;

  Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default);

  Task UnregisterAsync(CancellationToken cancellationToken = default);
}
