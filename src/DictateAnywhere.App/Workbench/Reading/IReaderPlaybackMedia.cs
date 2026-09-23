using System;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>The narrow media surface shared by Reading Studio preparation and playback.</summary>
internal interface IReaderPlaybackMedia : IDisposable
{
  event EventHandler? Ended;

  event EventHandler<ReaderPlaybackMediaFailedEventArgs>? Failed;

  TimeSpan Position { get; set; }

  double SpeedRatio { get; set; }

  Task<TimeSpan> OpenAsync(string audioPath, CancellationToken cancellationToken);

  void Play();

  void Pause();

  void Stop();

  void Close();
}

internal sealed class ReaderPlaybackMediaFailedEventArgs(Exception exception) : EventArgs
{
  public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));
}
