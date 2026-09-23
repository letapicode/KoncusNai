using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Audio;

public interface IAudioInputSource : IAsyncDisposable
{
  event EventHandler<AudioRawDataEventArgs>? DataAvailable;

  event EventHandler<AudioInputErrorEventArgs>? DeviceError;

  IReadOnlyList<AudioInputDevice> GetInputDevices();

  string? GetDefaultInputDeviceId();

  Task StartAsync(string? preferredDeviceId, CancellationToken cancellationToken = default);

  Task StopAsync(CancellationToken cancellationToken = default);
}
