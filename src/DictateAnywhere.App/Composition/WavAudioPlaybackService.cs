using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace DictateAnywhere.App.Composition;

internal sealed class WavAudioPlaybackFailedEventArgs(Exception exception) : EventArgs
{
  public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));
}

internal interface IWavAudioPlaybackService : IDisposable
{
  event EventHandler? PlaybackEnded;

  event EventHandler<WavAudioPlaybackFailedEventArgs>? PlaybackFailed;

  void Play(string audioPath);

  void Stop();
}

/// <summary>Dispatcher-bound local WAV playback with explicit completion and failure signals.</summary>
internal sealed class WavAudioPlaybackService : IWavAudioPlaybackService
{
  private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
  private MediaPlayer? player;
  private bool disposed;

  public event EventHandler? PlaybackEnded;

  public event EventHandler<WavAudioPlaybackFailedEventArgs>? PlaybackFailed;

  public void Play(string wavePath)
  {
    if (string.IsNullOrWhiteSpace(wavePath) || !File.Exists(wavePath))
    {
      throw new FileNotFoundException("The generated speech WAV was not found.", wavePath);
    }

    InvokeOnDispatcher(() =>
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      StopCore();

      var nextPlayer = new MediaPlayer();
      nextPlayer.MediaEnded += OnMediaEnded;
      nextPlayer.MediaFailed += OnMediaFailed;
      player = nextPlayer;

      try
      {
        nextPlayer.Open(new Uri(Path.GetFullPath(wavePath), UriKind.Absolute));
        nextPlayer.Play();
      }
      catch
      {
        ReleasePlayer();
        throw;
      }
    });
  }

  public void Stop()
  {
    InvokeOnDispatcher(StopCore);
  }

  public void Dispose()
  {
    InvokeOnDispatcher(() =>
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      StopCore();
      PlaybackEnded = null;
      PlaybackFailed = null;
    });
  }

  private void OnMediaEnded(object? sender, EventArgs eventArgs)
  {
    if (!ReferenceEquals(sender, player))
    {
      return;
    }

    ReleasePlayer();
    PlaybackEnded?.Invoke(this, EventArgs.Empty);
  }

  private void OnMediaFailed(object? sender, ExceptionEventArgs eventArgs)
  {
    if (!ReferenceEquals(sender, player))
    {
      return;
    }

    ReleasePlayer();
    PlaybackFailed?.Invoke(this, new WavAudioPlaybackFailedEventArgs(eventArgs.ErrorException));
  }

  private void StopCore()
  {
    player?.Stop();
    ReleasePlayer();
  }

  private void ReleasePlayer()
  {
    MediaPlayer? currentPlayer = player;
    player = null;
    if (currentPlayer is null)
    {
      return;
    }

    currentPlayer.MediaEnded -= OnMediaEnded;
    currentPlayer.MediaFailed -= OnMediaFailed;
    currentPlayer.Close();
  }

  private void InvokeOnDispatcher(Action action)
  {
    if (dispatcher.CheckAccess())
    {
      action();
      return;
    }

    dispatcher.Invoke(action);
  }
}
