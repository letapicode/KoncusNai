using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Hotkeys;

public sealed class WindowsHotkeyRegistrationValidator : IHotkeyRegistrationValidator
{
  private readonly Func<IHotkeyService> hotkeyServiceFactory;

  public WindowsHotkeyRegistrationValidator()
    : this(() => new WindowsHotkeyService())
  {
  }

  public WindowsHotkeyRegistrationValidator(Func<IHotkeyService> hotkeyServiceFactory)
  {
    this.hotkeyServiceFactory = hotkeyServiceFactory ?? throw new ArgumentNullException(nameof(hotkeyServiceFactory));
  }

  public async Task<HotkeyRegistrationResult> ValidateAsync(HotkeyBinding binding, CancellationToken cancellationToken = default)
  {
    await using IHotkeyService service = hotkeyServiceFactory();
    HotkeyRegistrationResult result = await service.RegisterAsync(binding, cancellationToken).ConfigureAwait(false);
    if (result.Success)
    {
      await service.UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
    }

    return result;
  }
}
