using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Hotkeys;

public interface IHotkeyRegistrationValidator
{
  Task<HotkeyRegistrationResult> ValidateAsync(HotkeyBinding binding, CancellationToken cancellationToken = default);
}
