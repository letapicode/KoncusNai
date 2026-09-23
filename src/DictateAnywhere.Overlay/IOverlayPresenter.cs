using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Overlay;

public interface IOverlayPresenter
{
  Task ShowAsync(OverlayPresentation presentation, CancellationToken cancellationToken = default);

  Task HideAsync(CancellationToken cancellationToken = default);
}
