using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Overlay;

namespace DictateAnywhere.App.Runtime;

/// <summary>The newest recording owns presentation; older pipelines may finish independently.</summary>
internal sealed class OwnedOverlayPresenter(IOverlayPresenter inner) : IOverlayPresenter, IAsyncDisposable
{
  private readonly IOverlayPresenter presenter = inner;
  private static readonly SemaphoreSlim PresentationGate = new(1, 1);
  private static OwnedOverlayPresenter? owner;
  private bool retired;

  public async Task ShowAsync(OverlayPresentation presentation, CancellationToken cancellationToken = default)
  {
    await PresentationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (presentation.VisualState == OverlayVisualState.Recording && !ReferenceEquals(owner, this))
      {
        if (owner is { } previous)
        {
          previous.retired = true;
          await previous.presenter.HideAsync(CancellationToken.None).ConfigureAwait(false);
        }
        owner = this;
        retired = false;
      }
      else if (owner is null && !retired) owner = this;
      if (ReferenceEquals(owner, this)) await presenter.ShowAsync(presentation, cancellationToken).ConfigureAwait(false);
    }
    finally { PresentationGate.Release(); }
  }

  public async Task HideAsync(CancellationToken cancellationToken = default)
  {
    await PresentationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (!ReferenceEquals(owner, this)) return;
      await presenter.HideAsync(cancellationToken).ConfigureAwait(false);
      owner = null;
      retired = true;
    }
    finally { PresentationGate.Release(); }
  }

  public async ValueTask DisposeAsync()
  {
    await HideAsync().ConfigureAwait(false);
    if (presenter is IAsyncDisposable disposable) await disposable.DisposeAsync().ConfigureAwait(false);
  }
}
