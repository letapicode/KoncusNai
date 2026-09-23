using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Audio;
using DictateAnywhere.Audio.WASAPI;

namespace DictateAnywhere.App.Composition;

public sealed class WasapiAudioInputDeviceService : IAudioInputDeviceService
{
  public async Task<IReadOnlyList<AudioInputDeviceOption>> GetInputDevicesAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    try
    {
      await using WasapiAudioInputSource inputSource = new();
      AudioDeviceCatalog catalog = new(inputSource);
      IReadOnlyList<AudioInputDevice> devices = catalog.GetInputDevices();

      List<AudioInputDeviceOption> options = new(devices.Count + 1)
      {
        new(null, "System Default"),
      };

      foreach (AudioInputDevice device in devices)
      {
        string label = device.IsDefault
          ? $"{device.DisplayName} (Windows Default)"
          : device.DisplayName;
        options.Add(new AudioInputDeviceOption(device.DeviceId, label));
      }

      return options;
    }
    catch (Exception ex) when (ex is AudioCaptureException or COMException or UnauthorizedAccessException or InvalidOperationException)
    {
      throw new InvalidOperationException($"Unable to enumerate audio input devices: {ex.Message}", ex);
    }
  }
}
