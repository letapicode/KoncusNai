using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DictateAnywhere.Overlay;

public sealed class WindowsOverlayPresenter : IOverlayPresenter, IAsyncDisposable
{
  private readonly Thread uiThread;
  private readonly TaskCompletionSource<OverlayUiContext> uiContextReady =
    new(TaskCreationOptions.RunContinuationsAsynchronously);

  private bool disposed;

  public WindowsOverlayPresenter()
  {
    uiThread = new Thread(OverlayUiThreadStart)
    {
      IsBackground = true,
      Name = "DictateAnywhere.Overlay.UI",
    };
    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
  }

  public async Task ShowAsync(OverlayPresentation presentation, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(presentation);

    OverlayUiContext uiContext = await uiContextReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    await InvokeOnUiThreadAsync(
      uiContext.StatusWindow,
      () =>
      {
        if (presentation.IsAnchoredIndicator)
        {
          uiContext.StatusWindow.HideOverlay();
          uiContext.IndicatorWindow.ApplyPresentation(presentation);
        }
        else
        {
          uiContext.IndicatorWindow.HideOverlay();
          uiContext.StatusWindow.ApplyPresentation(presentation);
        }
      },
      cancellationToken).ConfigureAwait(false);
  }

  public async Task HideAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);

    OverlayUiContext uiContext = await uiContextReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    await InvokeOnUiThreadAsync(
      uiContext.StatusWindow,
      () =>
      {
        uiContext.IndicatorWindow.HideOverlay();
        uiContext.StatusWindow.HideOverlay();
      },
      cancellationToken).ConfigureAwait(false);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;

    if (uiContextReady.Task.IsCompletedSuccessfully)
    {
      OverlayUiContext uiContext = await uiContextReady.Task.ConfigureAwait(false);
      await InvokeOnUiThreadAsync(
        uiContext.StatusWindow,
        () =>
        {
          uiContext.IndicatorWindow.HideOverlay();
          uiContext.StatusWindow.HideOverlay();
          uiContext.Context.ExitThread();
        },
        CancellationToken.None).ConfigureAwait(false);
    }

    if (uiThread.IsAlive)
    {
      _ = uiThread.Join(TimeSpan.FromSeconds(2));
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The UI dispatch boundary must complete its task for every callback outcome so callers cannot wait indefinitely.")]
  private static async Task InvokeOnUiThreadAsync(Form window, Action action, CancellationToken cancellationToken)
  {
    if (window.IsDisposed)
    {
      return;
    }

    if (!window.InvokeRequired)
    {
      action();
      return;
    }

    TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    void InvokeAction()
    {
      try
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (!window.IsDisposed)
        {
          action();
        }

        completion.TrySetResult(true);
      }
      catch (Exception ex)
      {
        completion.TrySetException(ex);
      }
    }

    IAsyncResult asyncResult;
    try
    {
      asyncResult = window.BeginInvoke((Action)InvokeAction);
    }
    catch (ObjectDisposedException)
    {
      return;
    }
    catch (InvalidOperationException)
    {
      return;
    }

    _ = asyncResult;
    await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The overlay UI thread must surface startup failures through the task completion source.")]
  private void OverlayUiThreadStart()
  {
    try
    {
      Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
      using ApplicationContext context = new();
      using OverlayWindow statusWindow = new();
      using OverlayIndicatorWindow indicatorWindow = new();
      _ = statusWindow.Handle;
      _ = indicatorWindow.Handle;

      uiContextReady.TrySetResult(new OverlayUiContext(statusWindow, indicatorWindow, context));
      Application.Run(context);
    }
    catch (Exception ex)
    {
      uiContextReady.TrySetException(ex);
    }
  }

  private sealed record OverlayUiContext(
    OverlayWindow StatusWindow,
    OverlayIndicatorWindow IndicatorWindow,
    ApplicationContext Context);
}
