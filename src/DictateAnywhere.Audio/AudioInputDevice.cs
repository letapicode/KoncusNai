namespace DictateAnywhere.Audio;

public sealed record AudioInputDevice(
  string DeviceId,
  string DisplayName,
  bool IsDefault);
