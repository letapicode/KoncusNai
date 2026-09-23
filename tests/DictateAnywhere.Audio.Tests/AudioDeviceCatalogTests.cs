using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Audio;

namespace DictateAnywhere.Audio.Tests;

public sealed class AudioDeviceCatalogTests
{
  [Xunit.Fact]
  public async Task GetDefaultDevice_ReturnsMatchingDefault()
  {
    await using FakeSource source = new(
      devices:
      [
        new AudioInputDevice("mic-1", "Mic One", false),
        new AudioInputDevice("mic-2", "Mic Two", true),
      ],
      defaultId: "mic-2");

    AudioDeviceCatalog catalog = new(source);
    AudioInputDevice? device = catalog.GetDefaultDevice();

    Xunit.Assert.NotNull(device);
    Xunit.Assert.Equal("mic-2", device!.DeviceId);
  }

  [Xunit.Fact]
  public void ResolveInputDeviceId_UsesMicrophone_WhenSystemDefaultIsStereoMix()
  {
    IReadOnlyList<AudioInputDevice> devices =
    [
      new AudioInputDevice("stereo-mix", "Stereo Mix (Realtek(R) Audio)", true),
      new AudioInputDevice("mic-array", "Microphone Array", false),
    ];

    string? selected = AudioDeviceCatalog.ResolveInputDeviceId(
      devices,
      preferredDeviceId: null,
      defaultDeviceId: "stereo-mix");

    Xunit.Assert.Equal("mic-array", selected);
  }

  [Xunit.Fact]
  public void ResolveInputDeviceId_RespectsExplicitSystemMixPreference()
  {
    IReadOnlyList<AudioInputDevice> devices =
    [
      new AudioInputDevice("stereo-mix", "Stereo Mix (Realtek(R) Audio)", true),
      new AudioInputDevice("mic-array", "Microphone Array", false),
    ];

    string? selected = AudioDeviceCatalog.ResolveInputDeviceId(
      devices,
      preferredDeviceId: "stereo-mix",
      defaultDeviceId: "mic-array");

    Xunit.Assert.Equal("stereo-mix", selected);
  }

  [Xunit.Fact]
  public void ResolveInputDeviceId_KeepsDefault_WhenDefaultIsMicrophone()
  {
    IReadOnlyList<AudioInputDevice> devices =
    [
      new AudioInputDevice("mic-array", "Microphone Array", true),
      new AudioInputDevice("headset", "Headset Microphone", false),
    ];

    string? selected = AudioDeviceCatalog.ResolveInputDeviceId(
      devices,
      preferredDeviceId: null,
      defaultDeviceId: "mic-array");

    Xunit.Assert.Equal("mic-array", selected);
  }

  private sealed class FakeSource : IAudioInputSource
  {
    private readonly IReadOnlyList<AudioInputDevice> devices;
    private readonly string? defaultId;

    public FakeSource(IReadOnlyList<AudioInputDevice> devices, string? defaultId)
    {
      this.devices = devices;
      this.defaultId = defaultId;
    }

    public event System.EventHandler<AudioRawDataEventArgs>? DataAvailable
    {
      add { }
      remove { }
    }

    public event System.EventHandler<AudioInputErrorEventArgs>? DeviceError
    {
      add { }
      remove { }
    }

    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
      return devices;
    }

    public string? GetDefaultInputDeviceId()
    {
      return defaultId;
    }

    public Task StartAsync(string? preferredDeviceId, CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }
  }
}
