using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.Audio;

public sealed class AudioDeviceCatalog
{
  private readonly IAudioInputSource inputSource;

  public AudioDeviceCatalog(IAudioInputSource inputSource)
  {
    this.inputSource = inputSource;
  }

  public IReadOnlyList<AudioInputDevice> GetInputDevices()
  {
    return inputSource.GetInputDevices();
  }

  public AudioInputDevice? GetDefaultDevice()
  {
    string? defaultId = inputSource.GetDefaultInputDeviceId();
    if (string.IsNullOrWhiteSpace(defaultId))
    {
      return null;
    }

    return inputSource.GetInputDevices().FirstOrDefault(d => d.DeviceId == defaultId);
  }

  public static string? ResolveInputDeviceId(
    IReadOnlyList<AudioInputDevice> devices,
    string? preferredDeviceId,
    string? defaultDeviceId)
  {
    if (!string.IsNullOrWhiteSpace(preferredDeviceId)
        && devices.Any(device => device.DeviceId == preferredDeviceId))
    {
      return preferredDeviceId;
    }

    AudioInputDevice? defaultDevice = !string.IsNullOrWhiteSpace(defaultDeviceId)
      ? devices.FirstOrDefault(device => device.DeviceId == defaultDeviceId)
      : devices.FirstOrDefault(device => device.IsDefault);
    if (defaultDevice is not null && !LooksLikeSystemMix(defaultDevice.DisplayName))
    {
      return defaultDevice.DeviceId;
    }

    AudioInputDevice? microphone = devices.FirstOrDefault(device =>
      LooksLikeMicrophone(device.DisplayName) && !LooksLikeSystemMix(device.DisplayName));
    return microphone?.DeviceId
           ?? defaultDevice?.DeviceId
           ?? devices.FirstOrDefault()?.DeviceId;
  }

  private static bool LooksLikeMicrophone(string displayName)
  {
    string normalized = displayName.ToLowerInvariant();
    return normalized.Contains("microphone")
           || normalized.Contains("mic")
           || normalized.Contains("headset")
           || normalized.Contains("webcam")
           || normalized.Contains("camera");
  }

  private static bool LooksLikeSystemMix(string displayName)
  {
    string normalized = displayName.ToLowerInvariant();
    return normalized.Contains("stereo mix")
           || normalized.Contains("what u hear")
           || normalized.Contains("wave out")
           || normalized.Contains("loopback")
           || normalized.Contains("monitor of");
  }
}
