using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Owns the primary WPF media player and translates its event-based open lifetime.</summary>
internal sealed class WpfReaderPlaybackMedia : IReaderPlaybackMedia
{
  private readonly MediaPlayer player = new();
  private TaskCompletionSource<TimeSpan>? pendingOpen;
  private CancellationTokenRegistration openCancellation;
  private bool disposed;

  public WpfReaderPlaybackMedia()
  {
    player.MediaOpened += OnMediaOpened;
    player.MediaEnded += OnMediaEnded;
    player.MediaFailed += OnMediaFailed;
  }

  public event EventHandler? Ended;

  public event EventHandler<ReaderPlaybackMediaFailedEventArgs>? Failed;

  public TimeSpan Position
  {
    get => player.Position;
    set => player.Position = value;
  }

  public double SpeedRatio
  {
    get => player.SpeedRatio;
    set => player.SpeedRatio = value;
  }

  public Task<TimeSpan> OpenAsync(string audioPath, CancellationToken cancellationToken)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
    cancellationToken.ThrowIfCancellationRequested();
    if (pendingOpen is not null)
    {
      throw new InvalidOperationException("A media-open operation is already active.");
    }

    TaskCompletionSource<TimeSpan> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    pendingOpen = completion;
    openCancellation = cancellationToken.Register(() => CancelPendingOpen(cancellationToken));
    if (!ReferenceEquals(pendingOpen, completion))
    {
      openCancellation.Dispose();
      openCancellation = default;
      return completion.Task;
    }

    try
    {
      player.Open(new Uri(audioPath, UriKind.Absolute));
      return completion.Task;
    }
    catch
    {
      CompletePendingOpen();
      throw;
    }
  }

  public void Play() => player.Play();

  public void Pause() => player.Pause();

  public void Stop() => player.Stop();

  public void Close() => player.Close();

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    TaskCompletionSource<TimeSpan>? completion = pendingOpen;
    CompletePendingOpen();
    completion?.TrySetCanceled();
    player.MediaOpened -= OnMediaOpened;
    player.MediaEnded -= OnMediaEnded;
    player.MediaFailed -= OnMediaFailed;
    player.Close();
  }

  private void OnMediaOpened(object? sender, EventArgs e)
  {
    TaskCompletionSource<TimeSpan>? completion = pendingOpen;
    if (completion is null)
    {
      return;
    }

    TimeSpan duration = player.NaturalDuration.HasTimeSpan
      ? player.NaturalDuration.TimeSpan
      : TimeSpan.Zero;
    player.Position = TimeSpan.Zero;
    CompletePendingOpen();
    completion.TrySetResult(duration);
  }

  private void OnMediaEnded(object? sender, EventArgs e) => Ended?.Invoke(this, EventArgs.Empty);

  private void OnMediaFailed(object? sender, ExceptionEventArgs e)
  {
    Exception exception = e.ErrorException ?? new InvalidOperationException("The prepared audio could not be opened.");
    TaskCompletionSource<TimeSpan>? completion = pendingOpen;
    if (completion is not null)
    {
      CompletePendingOpen();
      completion.TrySetException(exception);
      return;
    }

    Failed?.Invoke(this, new ReaderPlaybackMediaFailedEventArgs(exception));
  }

  private void CancelPendingOpen(CancellationToken cancellationToken)
  {
    if (!player.Dispatcher.CheckAccess())
    {
      _ = player.Dispatcher.BeginInvoke(() => CancelPendingOpen(cancellationToken));
      return;
    }

    TaskCompletionSource<TimeSpan>? completion = pendingOpen;
    if (completion is null)
    {
      return;
    }

    CompletePendingOpen();
    player.Close();
    completion.TrySetCanceled(cancellationToken);
  }

  private void CompletePendingOpen()
  {
    pendingOpen = null;
    openCancellation.Dispose();
    openCancellation = default;
  }
}
